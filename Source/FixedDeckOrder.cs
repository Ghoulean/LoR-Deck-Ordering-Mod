using System;
using System.Collections.Generic;
using System.Reflection;
using GameSave;
using HarmonyLib;
using LOR_DiceSystem;
using UI;
using UnityEngine;

namespace DeckOrdering
{
    // Fixed decks (e.g. Binah) have special decklist loading logic that must be overwritten
	// 
    // Saved under CustomStorage["Gh-LoRDeckOrderingMod-FixedDeckOrder"]:
    //   [ { "book": <LorId>, "cards": [ <LorId>, ... ] }, ... ]
    internal static class FixedDeckOrderStore
    {
        private const string SaveKey = "FixedDeckOrder";

        private static Dictionary<LorId, List<LorId>> Read()
        {
            Dictionary<LorId, List<LorId>> result = new Dictionary<LorId, List<LorId>>();
            SaveData root = ModSave.Get(SaveKey);
            if (root == null || root.GetDataType() != SaveDataType.List)
                return result;
            foreach (SaveData entry in root)
            {
                if (entry.GetDataType() != SaveDataType.Dictionary)
                    continue;
                LorId book = ModSave.ToLorId(entry.GetData("book"));
                SaveData cardsData = entry.GetData("cards");
                if (book == null || cardsData == null || cardsData.GetDataType() != SaveDataType.List)
                    continue;
                List<LorId> cards = new List<LorId>();
                foreach (SaveData card in cardsData)
                {
                    LorId id = ModSave.ToLorId(card);
                    if (id != null)
                        cards.Add(id);
                }
                result[book] = cards;
            }
            return result;
        }

        private static void Write(Dictionary<LorId, List<LorId>> orders)
        {
            SaveData root = new SaveData();
            foreach (KeyValuePair<LorId, List<LorId>> kv in orders)
            {
                SaveData cards = new SaveData(SaveDataType.List);
                foreach (LorId card in kv.Value)
                    cards.AddToList(ModSave.FromLorId(card));
                SaveData entry = new SaveData();
                entry.AddData("book", ModSave.FromLorId(kv.Key));
                entry.AddData("cards", cards);
                root.AddToList(entry);
            }
            ModSave.Set(SaveKey, root);
        }

        internal static void Set(BookModel book, List<LorId> order)
        {
            if (book?.ClassInfo == null)
                return;
            Dictionary<LorId, List<LorId>> orders = Read();
            orders[book.ClassInfo.id] = order;
            Write(orders);
        }

        internal static void Apply(BookModel book, List<DiceCardXmlInfo> cards)
        {
            if (cards == null || cards.Count < 2 || book?.ClassInfo == null || !Read().TryGetValue(book.ClassInfo.id, out List<LorId> order))
                return;
            List<DiceCardXmlInfo> remaining = new List<DiceCardXmlInfo>(cards);
            List<DiceCardXmlInfo> result = new List<DiceCardXmlInfo>(cards.Count);
            foreach (LorId id in order)
            {
                int index = remaining.FindIndex(c => c.id == id);
                if (index < 0)
                    continue;
                result.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
            result.AddRange(remaining);
            cards.Clear();
            cards.AddRange(result);
        }
    }

    // Overwrites the following BookModel methods:
	// GetCardListFromCurrentDeck, GetCardListByIndex, and GetCardListFromAllDeck.
	// Overwritten logic is identical to vanilla except it uses custom sorting logic instead of vanilla sort
    internal static class BookCardLists
    {
        internal static readonly AccessTools.FieldRef<BookModel, DeckModel> CurrentDeck =
            AccessTools.FieldRefAccess<BookModel, DeckModel>("_deck");
        internal static readonly AccessTools.FieldRef<BookModel, List<DeckModel>> DeckList =
            AccessTools.FieldRefAccess<BookModel, List<DeckModel>>("_deckList");

        internal static List<DiceCardXmlInfo> FixedDeck(BookModel book)
        {
            List<DiceCardXmlInfo> list = new List<DiceCardXmlInfo>();
            DeckXmlInfo data = Singleton<DeckXmlList>.Instance.GetData(book.ClassInfo.id);
            if (data != null)
            {
                foreach (LorId cardId in data.cardIdList)
                {
                    DiceCardXmlInfo card = ItemXmlDataList.instance.GetCardItem(cardId);
                    if (card != null)
                        list.Add(card);
                }
            }
            FixedDeckOrderStore.Apply(book, list);
            return list;
        }

        internal static bool DeferToOtherMod(BookModel book, List<DiceCardXmlInfo> result)
        {
            try
            {
                if (result != null && book.IsFixedDeck())
                    FixedDeckOrderStore.Apply(book, result);
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + "Failed to order another mod's deck result: " + e);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(BookModel), nameof(BookModel.GetCardListFromCurrentDeck))]
    [HarmonyPriority(Priority.Low)]
    internal static class ReplaceGetCardListFromCurrentDeck
    {
        private static bool Prefix(BookModel __instance, ref List<DiceCardXmlInfo> __result, bool __runOriginal)
        {
            if (!__runOriginal)
                return BookCardLists.DeferToOtherMod(__instance, __result);
            try
            {
                __result = __instance.IsFixedDeck() ? BookCardLists.FixedDeck(__instance) : BookCardLists.CurrentDeck(__instance).GetAllCardList();
                return false;
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + "GetCardListFromCurrentDeck replacement failed: " + e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(BookModel), nameof(BookModel.GetCardListByIndex), new[] { typeof(int) })]
    [HarmonyPriority(Priority.Low)]
    internal static class ReplaceGetCardListByIndex
    {
        private static bool Prefix(BookModel __instance, int index, ref List<DiceCardXmlInfo> __result, bool __runOriginal)
        {
            if (!__runOriginal)
                return BookCardLists.DeferToOtherMod(__instance, __result);
            try
            {
                if (__instance.IsFixedDeck())
                {
                    __result = BookCardLists.FixedDeck(__instance);
                }
                else
                {
                    List<DeckModel> decks = BookCardLists.DeckList(__instance);
                    if (!__instance.IsMultiDeck() || index < 0 || index >= decks.Count)
                        index = 0;
                    __result = decks[index].GetAllCardList();
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + "GetCardListByIndex replacement failed: " + e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(BookModel), nameof(BookModel.GetCardListFromAllDeck))]
    [HarmonyPriority(Priority.Low)]
    internal static class ReplaceGetCardListFromAllDeck
    {
        private static bool Prefix(BookModel __instance, ref List<DiceCardXmlInfo> __result, bool __runOriginal)
        {
            if (!__runOriginal)
                return BookCardLists.DeferToOtherMod(__instance, __result);
            try
            {
                if (__instance.IsFixedDeck())
                {
                    __result = BookCardLists.FixedDeck(__instance);
                }
                else
                {
                    List<DiceCardXmlInfo> list = new List<DiceCardXmlInfo>();
                    foreach (DeckModel deck in BookCardLists.DeckList(__instance))
                        list.AddRange(deck.GetAllCardList());
                    list.Sort(SortUtil.CardInfoCompByCost);
                    __result = list;
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + "GetCardListFromAllDeck replacement failed: " + e);
                return true;
            }
        }
    }
}
