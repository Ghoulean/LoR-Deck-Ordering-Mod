using System;
using System.Collections.Generic;
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

        private static Dictionary<int, int> Read()
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            SaveData root = ModSave.Get(SaveKey);
            if (root == null || root.GetDataType() != SaveDataType.List)
                return result;
            foreach (SaveData entry in root)
            {
                if (entry.GetDataType() != SaveDataType.Dictionary)
                    continue;
                SaveData book = entry.GetData("book");
                SaveData stance = entry.GetData("stance");
                if (book?.GetDataType() == SaveDataType.Int && stance?.GetDataType() == SaveDataType.Int
                    && stance.GetIntSelf() >= 0 && stance.GetIntSelf() <= 3)
                    result[book.GetIntSelf()] = stance.GetIntSelf();
            }
            return result;
        }

        private static void Write(Dictionary<int, int> stances)
        {
            SaveData root = new SaveData();
            foreach (KeyValuePair<int, int> kv in stances)
            {
                SaveData entry = new SaveData();
                entry.AddData("book", new SaveData(kv.Key));
                entry.AddData("stance", new SaveData(kv.Value));
                root.AddToList(entry);
            }
            ModSave.Set(SaveKey, root);
        }

        internal static int? Get(BookModel book)
        {
            if (book != null && Read().TryGetValue(book.instanceId, out int stance))
                return stance;
            return null;
        }

        internal static void Set(BookModel book, int? stance)
        {
            if (book == null)
                return;
            Dictionary<int, int> stances = Read();
            if (stance.HasValue)
                stances[book.instanceId] = stance.Value;
            else
                stances.Remove(book.instanceId);
            Write(stances);
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
        private static readonly AccessTools.FieldRef<PassiveAbilityBase, BattleUnitModel> Owner =
            AccessTools.FieldRefAccess<PassiveAbilityBase, BattleUnitModel>("owner");

        private static readonly MethodInfo RandomRange = AccessTools.Method(typeof(RandomUtil), nameof(RandomUtil.Range), new[] { typeof(int), typeof(int) });
        private static readonly MethodInfo Pick = AccessTools.Method(typeof(PurpleTearOpeningStance), nameof(PickStance));

        private static BattleUnitModel current;

        private static void Prefix(PassiveAbility_250127 __instance, out bool __state)
        {
            BattleUnitModel owner = Owner(__instance);
            __state = owner != null && owner.faction == Faction.Player;
            if (!__state)
                return;
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
            int replaced = 0;
            foreach (CodeInstruction ci in instructions)
            {
                if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo m && m == RandomRange)
                {
                    ci.operand = Pick;
                    replaced++;
                }
                yield return ci;
            }
            if (replaced == 0)
                Debug.Log(Initializer.LogPrefix + "No stance roll found in PassiveAbility_250127.InitPurple");
        }

        private static int PickStance(int min, int max)
        {
            int? chosen = OpeningStanceStore.Get(current?.UnitData?.unitData?.bookItem);
            if (chosen.HasValue && chosen.Value >= min && chosen.Value <= max)
                return chosen.Value;
            return RandomUtil.Range(min, max);
        }
    }
}
