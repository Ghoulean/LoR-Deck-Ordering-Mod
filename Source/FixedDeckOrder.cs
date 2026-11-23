using System;
using System.Collections.Generic;
using System.Linq;
using GameSave;
using HarmonyLib;
using LOR_DiceSystem;
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

        private sealed class Partition
        {
            internal readonly IReadOnlyList<DiceCardXmlInfo> Picked;
            internal readonly IReadOnlyList<DiceCardXmlInfo> Remaining;

            internal Partition(IReadOnlyList<DiceCardXmlInfo> picked, IReadOnlyList<DiceCardXmlInfo> remaining)
            {
                Picked = picked;
                Remaining = remaining;
            }

            internal Partition Take(LorId id)
            {
                int index = Remaining.ToList().FindIndex(c => c.id == id);
                return index < 0
                    ? this
                    : new Partition(Picked.Concat(new[] { Remaining[index] }).ToList(), Remaining.Where((_, i) => i != index).ToList());
            }

            internal IReadOnlyList<DiceCardXmlInfo> Result => Picked.Concat(Remaining).ToList();
        }

        private static IReadOnlyDictionary<LorId, IReadOnlyList<LorId>> Read() => Parse(ModSave.Get(SaveKey));

        private static IReadOnlyDictionary<LorId, IReadOnlyList<LorId>> Parse(SaveData root) =>
            ModSave.Dictionaries(root)
                .Select(entry => new KeyValuePair<LorId, SaveData>(ModSave.ToLorId(entry.GetData("book")), entry.GetData("cards")))
                .Where(entry => entry.Key != null && entry.Value?.GetDataType() == SaveDataType.List)
                .GroupBy(entry => entry.Key)
                .ToDictionary(group => group.Key, group => ParseCards(group.Last().Value));

        private static IReadOnlyList<LorId> ParseCards(SaveData cards) =>
            ModSave.Items(cards).Select(ModSave.ToLorId).Where(id => id != null).ToList();

        private static SaveData Serialize(IReadOnlyDictionary<LorId, IReadOnlyList<LorId>> orders) =>
            ModSave.NonEmptyListOf(orders.Select(kv => ModSave.DictOf(
                ModSave.Entry("book", ModSave.FromLorId(kv.Key)),
                ModSave.Entry("cards", ModSave.ListOf(kv.Value.Select(ModSave.FromLorId))))));

        private static IReadOnlyDictionary<LorId, IReadOnlyList<LorId>> With(
            IReadOnlyDictionary<LorId, IReadOnlyList<LorId>> orders, LorId book, IReadOnlyList<LorId> order) =>
            orders.Where(kv => !kv.Key.Equals(book))
                .Concat(new[] { new KeyValuePair<LorId, IReadOnlyList<LorId>>(book, order) })
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        internal static void Set(BookModel book, IReadOnlyList<LorId> order)
        {
            if (book?.ClassInfo == null)
                return;
            ModSave.Set(SaveKey, Serialize(With(Read(), book.ClassInfo.id, order)));
        }

        internal static IReadOnlyList<DiceCardXmlInfo> Apply(BookModel book, IReadOnlyList<DiceCardXmlInfo> cards) =>
            cards == null || cards.Count < 2 || book?.ClassInfo == null || !Read().TryGetValue(book.ClassInfo.id, out IReadOnlyList<LorId> order)
                ? cards
                : Ordered(cards, order);

        private static IReadOnlyList<DiceCardXmlInfo> Ordered(IReadOnlyList<DiceCardXmlInfo> cards, IEnumerable<LorId> order) =>
            order.Aggregate(new Partition(new List<DiceCardXmlInfo>(), cards), (partition, id) => partition.Take(id)).Result;
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

        private static readonly IComparer<DiceCardXmlInfo> ByCost = Comparer<DiceCardXmlInfo>.Create(SortUtil.CardInfoCompByCost);

        internal static IEnumerable<DiceCardXmlInfo> FixedDeck(BookModel book) =>
            FixedDeckOrderStore.Apply(book, XmlCards(Singleton<DeckXmlList>.Instance.GetData(book.ClassInfo.id)));

        private static IReadOnlyList<DiceCardXmlInfo> XmlCards(DeckXmlInfo data) =>
            (data?.cardIdList ?? Enumerable.Empty<LorId>())
                .Select(cardId => ItemXmlDataList.instance.GetCardItem(cardId))
                .Where(card => card != null)
                .ToList();

        internal static IEnumerable<DiceCardXmlInfo> CurrentDeckCards(BookModel book) =>
            book.IsFixedDeck() ? FixedDeck(book) : CurrentDeck(book).GetAllCardList();

        internal static IEnumerable<DiceCardXmlInfo> DeckCardsByIndex(BookModel book, int index) =>
            book.IsFixedDeck() ? FixedDeck(book) : DeckList(book)[DeckIndex(index, book.IsMultiDeck(), DeckList(book).Count)].GetAllCardList();

        private static int DeckIndex(int requested, bool multiDeck, int deckCount) =>
            multiDeck && requested >= 0 && requested < deckCount ? requested : 0;

        internal static IEnumerable<DiceCardXmlInfo> AllDeckCards(BookModel book) =>
            book.IsFixedDeck() ? FixedDeck(book) : DeckList(book).SelectMany(deck => deck.GetAllCardList()).OrderBy(card => card, ByCost);

        internal static bool Replace(string method, BookModel book, ref List<DiceCardXmlInfo> result, bool runOriginal,
            Func<BookModel, IEnumerable<DiceCardXmlInfo>> cards)
        {
            if (!runOriginal)
            {
                result = DeferToOtherMod(book, result);
                return false;
            }
            try
            {
                result = cards(book).ToList();
                return false;
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + method + " replacement failed: " + e);
                return true;
            }
        }

        private static List<DiceCardXmlInfo> DeferToOtherMod(BookModel book, List<DiceCardXmlInfo> result)
        {
            try
            {
                return result != null && book.IsFixedDeck() ? FixedDeckOrderStore.Apply(book, result).ToList() : result;
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + "Failed to order another mod's deck result: " + e);
                return result;
            }
        }
    }

    [HarmonyPatch(typeof(BookModel), nameof(BookModel.GetCardListFromCurrentDeck))]
    [HarmonyPriority(Priority.Low)]
    internal static class ReplaceGetCardListFromCurrentDeck
    {
        private static bool Prefix(BookModel __instance, ref List<DiceCardXmlInfo> __result, bool __runOriginal) =>
            BookCardLists.Replace(nameof(BookModel.GetCardListFromCurrentDeck), __instance, ref __result, __runOriginal, BookCardLists.CurrentDeckCards);
    }

    [HarmonyPatch(typeof(BookModel), nameof(BookModel.GetCardListByIndex), new[] { typeof(int) })]
    [HarmonyPriority(Priority.Low)]
    internal static class ReplaceGetCardListByIndex
    {
        private static bool Prefix(BookModel __instance, int index, ref List<DiceCardXmlInfo> __result, bool __runOriginal) =>
            BookCardLists.Replace(nameof(BookModel.GetCardListByIndex), __instance, ref __result, __runOriginal, book => BookCardLists.DeckCardsByIndex(book, index));
    }

    [HarmonyPatch(typeof(BookModel), nameof(BookModel.GetCardListFromAllDeck))]
    [HarmonyPriority(Priority.Low)]
    internal static class ReplaceGetCardListFromAllDeck
    {
        private static bool Prefix(BookModel __instance, ref List<DiceCardXmlInfo> __result, bool __runOriginal) =>
            BookCardLists.Replace(nameof(BookModel.GetCardListFromAllDeck), __instance, ref __result, __runOriginal, BookCardLists.AllDeckCards);
    }
}
