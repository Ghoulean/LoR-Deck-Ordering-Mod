using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GameSave;
using HarmonyLib;
using UnityEngine;

namespace DeckOrdering
{
    // Purple Tear stance save layout
	// Saved under CustomStorage["Gh-LoRDeckOrderingMod-OpeningStance"] as
    //   [ { "book": <key page instanceId>, "stance": 0-3 }, ... ]
	// (pretty sure only PT will use this but this is keypage ID agnostic)
	// If unset, defaults to random stance on Act start

    internal static class OpeningStanceStore
    {
        private const string SaveKey = "OpeningStance";

        private static IReadOnlyDictionary<int, int> Read()
        {
            return Parse(ModSave.Get(SaveKey));
        }

        private static IReadOnlyDictionary<int, int> Parse(SaveData root)
        {
            return ModSave.Dictionaries(root)
                .Select(entry => new { Book = ModSave.IntOf(entry.GetData("book")), Stance = ModSave.IntOf(entry.GetData("stance")) })
                .Where(entry => entry.Book.HasValue && IsValidStance(entry.Stance))
                .GroupBy(entry => entry.Book.Value)
                .ToDictionary(group => group.Key, group => group.Last().Stance.Value);
        }

        private static bool IsValidStance(int? stance)
        {
            return stance.HasValue && stance.Value >= 0 && stance.Value <= 3;
        }

        private static SaveData Serialize(IReadOnlyDictionary<int, int> stances)
        {
            return ModSave.NonEmptyListOf(stances.Select(kv => ModSave.DictOf(
                ModSave.Entry("book", new SaveData(kv.Key)),
                ModSave.Entry("stance", new SaveData(kv.Value)))));
        }

        private static IReadOnlyDictionary<int, int> With(IReadOnlyDictionary<int, int> stances, int book, int? stance)
        {
            return stances.Where(kv => kv.Key != book)
                .Concat(stance.HasValue ? new[] { new KeyValuePair<int, int>(book, stance.Value) } : Enumerable.Empty<KeyValuePair<int, int>>())
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        internal static int? Get(BookModel book)
        {
            return book != null && Read().TryGetValue(book.instanceId, out int stance) ? stance : (int?)null;
        }

        internal static void Set(BookModel book, int? stance)
        {
            if (book == null)
            {
                return;
            }

            ModSave.Set(SaveKey, Serialize(With(Read(), book.instanceId, stance)));
        }
    }

    internal static class PurpleTear
    {
        internal static readonly LorId StancePassiveId = new LorId(250127);

        internal static bool IsPurpleTear(BookModel book)
        {
            return book != null && book.IsMultiDeck() && book.ClassInfo?.EquipEffect?.PassiveList != null
            && book.ClassInfo.EquipEffect.PassiveList.Exists(id => id == StancePassiveId);
        }
    }

    [HarmonyPatch(typeof(PassiveAbility_250127), "InitPurple")]
    internal static class PurpleTearOpeningStance
    {
        private static readonly MethodInfo RandomRange = AccessTools.Method(typeof(RandomUtil), nameof(RandomUtil.Range), new[] { typeof(int), typeof(int) });
        private static readonly MethodInfo Pick = AccessTools.Method(typeof(PurpleTearOpeningStance), nameof(PickStance));

        private static BattleUnitModel current;

        private static bool IsPlayer(BattleUnitModel unit)
        {
            return unit != null && unit.faction == Faction.Player;
        }

        private static void Prefix(PassiveAbility_250127 __instance, out bool __state)
        {
            BattleUnitModel owner = __instance.Owner;
            __state = IsPlayer(owner);
            if (!__state)
            {
                return;
            }

            current = owner;
            OpeningDeck.ScopeDepth++;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                current = null;
                OpeningDeck.ScopeDepth--;
            }
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return Transpilers.ReplaceCalls(instructions, ci => ci.opcode == OpCodes.Call && ci.operand is MethodInfo m && m == RandomRange, OpCodes.Call, Pick,
                "No stance roll found in PassiveAbility_250127.InitPurple");
        }

        private static int? Within(int? chosen, int min, int max)
        {
            return chosen.HasValue && chosen.Value >= min && chosen.Value <= max ? chosen : null;
        }

        private static int PickStance(int min, int max)
        {
            return Within(OpeningStanceStore.Get(current?.UnitData?.unitData?.bookItem), min, max) ?? RandomUtil.Range(min, max);
        }
    }
}
