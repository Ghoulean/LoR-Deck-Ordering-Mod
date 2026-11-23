using System;
using System.Collections.Generic;
using HarmonyLib;
using LOR_DiceSystem;

namespace DeckOrdering
{
    internal static class OpeningDeck
    {
        internal static int ScopeDepth;
        internal static bool InInit;

        internal static readonly AccessTools.FieldRef<BattleAllyCardDetail, List<BattleDiceCardModel>> CardInDeck =
            AccessTools.FieldRefAccess<BattleAllyCardDetail, List<BattleDiceCardModel>>("_cardInDeck");
    }

    [HarmonyPatch(typeof(BattleUnitModel), nameof(BattleUnitModel.SetUnitData))]
    internal static class OpeningDeckScopeForLibrarians
    {
        private static void Prefix(BattleUnitModel __instance, UnitBattleDataModel unitBattleData, out bool __state)
        {
            __state = __instance.faction == Faction.Player && unitBattleData?.unitData != null && unitBattleData.unitData.EnemyUnitId == -1;
            if (__state)
                OpeningDeck.ScopeDepth++;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                OpeningDeck.ScopeDepth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(BattleAllyCardDetail), nameof(BattleAllyCardDetail.Init))]
    internal static class TrackInit
    {
        private static void Prefix(List<DiceCardXmlInfo> deck) => OpeningDeck.InInit = OpeningDeck.ScopeDepth > 0;

        private static Exception Finalizer(Exception __exception)
        {
            OpeningDeck.InInit = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(BattleAllyCardDetail), nameof(BattleAllyCardDetail.Shuffle))]
    internal static class KeepEditorOrderInsteadOfShuffle
    {
        private static bool Prefix(BattleAllyCardDetail __instance)
        {
            if (!OpeningDeck.InInit)
                return true;
            OpeningDeck.CardInDeck(__instance).Reverse();
            return false;
        }
    }
}
