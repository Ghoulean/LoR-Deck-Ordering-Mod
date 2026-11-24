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
    // One key per key page:
    //   CustomStorage["Gh-LoRDeckOrderingMod-OpeningStance:<key page instanceId>"] = 0-3
    // (pretty sure only PT will use this but this is keypage ID agnostic)
    // If unset, defaults to random stance on Act start
    internal static class OpeningStanceStore
    {
        private const string SaveKey = "OpeningStance";

        private static string Name(BookModel book)
        {
            return Name(book.instanceId);
        }

        private static string Name(int bookInstanceId)
        {
            return SaveKey + ":" + bookInstanceId;
        }

        private static bool IsValidStance(int? stance)
        {
            return stance.HasValue && stance.Value >= 0 && stance.Value <= 3;
        }

        internal static int? Get(BookModel book)
        {
            if (book == null)
            {
                return null;
            }

            int? stance = ModSave.IntOf(ModSave.Get(Name(book)));
            return IsValidStance(stance) ? stance : null;
        }

        internal static void Set(BookModel book, int? stance)
        {
            if (book == null)
            {
                return;
            }

            ModSave.Set(Name(book), IsValidStance(stance) ? new SaveData(stance.Value) : null);
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
