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
    // One key per book and deck version:
    //   CustomStorage["Gh-LoRDeckOrderingMod-FixedDeckOrder:<book>:<base hash>"] = [ <int>, ... ]
    // <book> is the key page's LorId ("<packageId>/<id>", packageId empty for vanilla).
    // <base hash> fingerprints the original deck's card list with the original ordering
    // The value is a permutation of that list's indexes: slot i shows base[order[i]].
    internal static class FixedDeckOrderStore
    {
        private const string SaveKey = "FixedDeckOrder";

        private static readonly Dictionary<LorId, IReadOnlyList<LorId>> LastBase = new Dictionary<LorId, IReadOnlyList<LorId>>();

        private static string BookName(LorId book)
        {
            return SaveKey + ":" + book.packageId + "/" + book.id + ":";
        }

        private static string Name(LorId book, IEnumerable<LorId> baseIds)
        {
            return BookName(book) + Fingerprint(baseIds);
        }

        // FNV-1a 64-bit over card IDs
        // string.GetHashCode differs between runs
        private static string Fingerprint(IEnumerable<LorId> ids)
        {
            return string.Join(",", ids.Select(id => id.packageId + "/" + id.id))
                .Aggregate(14695981039346656037UL, (hash, c) => (hash ^ c) * 1099511628211UL)
                .ToString("x16");
        }

        private static Permutation Stored(LorId book, IReadOnlyList<LorId> baseIds)
        {
            Permutation order = Permutation.From(ModSave.Items(ModSave.Get(Name(book, baseIds))).Select(ModSave.IntOf).ToList());
            return order != null && order.Count == baseIds.Count ? order : null;
        }

        internal static IReadOnlyList<DiceCardXmlInfo> Apply(BookModel book, IReadOnlyList<DiceCardXmlInfo> cards)
        {
            if (cards == null || book?.ClassInfo == null)
            {
                return cards;
            }

            IReadOnlyList<LorId> baseIds = cards.Select(card => card.id).ToList();
            LastBase[book.ClassInfo.id] = baseIds;
            Permutation order = Stored(book.ClassInfo.id, baseIds);
            return order == null ? cards : order.ApplyTo(cards);
        }

        internal static void Swap(BookModel book, IReadOnlyList<DiceCardXmlInfo> shown, int a, int b)
        {
            if (book?.ClassInfo == null || shown == null)
            {
                return;
            }

            LorId bookId = book.ClassInfo.id;
            IReadOnlyList<LorId> shownIds = shown.Select(card => card.id).ToList();
            KeyValuePair<IReadOnlyList<LorId>, Permutation> current = CurrentOrder(bookId, shownIds);
            Permutation next = current.Value.Swapped(a, b);

            foreach (string name in ModSave.Names(BookName(bookId)))
            {
                ModSave.Set(name, null);
            }

            if (!next.IsIdentity)
            {
                ModSave.Set(Name(bookId, current.Key), ModSave.ListOf(next.Indexes.Select(index => new SaveData(index))));
            }
        }

        private static KeyValuePair<IReadOnlyList<LorId>, Permutation> CurrentOrder(LorId book, IReadOnlyList<LorId> shownIds)
        {
            Permutation order = LastBase.TryGetValue(book, out IReadOnlyList<LorId> baseIds) ? Stored(book, baseIds) : null;
            return order != null && order.ApplyTo(baseIds).SequenceEqual(shownIds)
                ? new KeyValuePair<IReadOnlyList<LorId>, Permutation>(baseIds, order)
                : new KeyValuePair<IReadOnlyList<LorId>, Permutation>(shownIds, Permutation.Identity(shownIds.Count));
        }
    }

    // Overwrites the following BookModel methods:
	// GetCardListFromCurrentDeck, GetCardListByIndex, and GetCardListFromAllDeck.
	// Overwritten logic is identical to vanilla except it uses custom sorting logic instead of vanilla sort
    internal static class BookCardLists
    {
        internal static readonly Func<BookModel, DeckModel> CurrentDeck = Reflect.FieldGetter<BookModel, DeckModel>("_deck");

        private static List<DeckModel> DeckList(BookModel book)
        {
            return book.GetDeckAll_nocopy();
        }

        private static readonly IComparer<DiceCardXmlInfo> ByCost = Comparer<DiceCardXmlInfo>.Create(SortUtil.CardInfoCompByCost);

        internal static IEnumerable<DiceCardXmlInfo> FixedDeck(BookModel book)
        {
            return FixedDeckOrderStore.Apply(book, XmlCards(Singleton<DeckXmlList>.Instance.GetData(book.ClassInfo.id)));
        }

        private static IReadOnlyList<DiceCardXmlInfo> XmlCards(DeckXmlInfo data)
        {
            return (data?.cardIdList ?? Enumerable.Empty<LorId>())
                .Select(cardId => ItemXmlDataList.instance.GetCardItem(cardId))
                .Where(card => card != null)
                .ToList();
        }

        internal static IEnumerable<DiceCardXmlInfo> CurrentDeckCards(BookModel book)
        {
            return book.IsFixedDeck() ? FixedDeck(book) : CurrentDeck(book).GetAllCardList();
        }

        internal static IEnumerable<DiceCardXmlInfo> DeckCardsByIndex(BookModel book, int index)
        {
            return book.IsFixedDeck() ? FixedDeck(book) : DeckList(book)[DeckIndex(index, book.IsMultiDeck(), DeckList(book).Count)].GetAllCardList();
        }

        private static int DeckIndex(int requested, bool multiDeck, int deckCount)
        {
            return multiDeck && requested >= 0 && requested < deckCount ? requested : 0;
        }

        internal static IEnumerable<DiceCardXmlInfo> AllDeckCards(BookModel book)
        {
            return book.IsFixedDeck() ? FixedDeck(book) : DeckList(book).SelectMany(deck => deck.GetAllCardList()).OrderBy(card => card, ByCost);
        }

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
        private static bool Prefix(BookModel __instance, ref List<DiceCardXmlInfo> __result, bool __runOriginal)
        {
            return BookCardLists.Replace(nameof(BookModel.GetCardListFromCurrentDeck), __instance, ref __result, __runOriginal, BookCardLists.CurrentDeckCards);
        }
    }

    [HarmonyPatch(typeof(BookModel), nameof(BookModel.GetCardListByIndex), new[] { typeof(int) })]
    [HarmonyPriority(Priority.Low)]
    internal static class ReplaceGetCardListByIndex
    {
        private static bool Prefix(BookModel __instance, int index, ref List<DiceCardXmlInfo> __result, bool __runOriginal)
        {
            return BookCardLists.Replace(nameof(BookModel.GetCardListByIndex), __instance, ref __result, __runOriginal, book => BookCardLists.DeckCardsByIndex(book, index));
        }
    }

    [HarmonyPatch(typeof(BookModel), nameof(BookModel.GetCardListFromAllDeck))]
    [HarmonyPriority(Priority.Low)]
    internal static class ReplaceGetCardListFromAllDeck
    {
        private static bool Prefix(BookModel __instance, ref List<DiceCardXmlInfo> __result, bool __runOriginal)
        {
            return BookCardLists.Replace(nameof(BookModel.GetCardListFromAllDeck), __instance, ref __result, __runOriginal, BookCardLists.AllDeckCards);
        }
    }
}
