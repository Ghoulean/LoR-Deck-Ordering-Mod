using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LOR_DiceSystem;
using UnityEngine;

namespace DeckOrdering
{
    [HarmonyPatch]
    internal static class NoAutoSort
    {
        private static readonly MethodInfo ListSort = AccessTools.Method(typeof(List<DiceCardXmlInfo>), nameof(List<DiceCardXmlInfo>.Sort), new[] { typeof(Comparison<DiceCardXmlInfo>) });
        private static readonly MethodInfo Skip = AccessTools.Method(typeof(NoAutoSort), nameof(SkipSort));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UnitDataModel), nameof(UnitDataModel.GetDeckAll));
            yield return AccessTools.Method(typeof(UnitDataModel), "GetCardList", new[] { typeof(int) });
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replaced = 0;
            foreach (CodeInstruction ci in instructions)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && ci.operand is MethodInfo m && m == ListSort)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = Skip;
                    replaced++;
                }
                yield return ci;
            }
            if (replaced == 0)
                Debug.Log(Initializer.LogPrefix + "No deck sort found in " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name);
        }

        private static void SkipSort(List<DiceCardXmlInfo> list, Comparison<DiceCardXmlInfo> comparison)
        {
        }
    }
}
