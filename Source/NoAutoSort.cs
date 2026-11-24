using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LOR_DiceSystem;
using UnityEngine;

namespace DeckOrdering
{
    internal static class Transpilers
    {
        internal static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> instructions,
            Func<CodeInstruction, bool> matches, OpCode opcode, MethodInfo replacement, string missingMessage)
        {
            List<CodeInstruction> result = instructions.Select(ci => matches(ci) ? Replaced(ci, opcode, replacement) : ci).ToList();
            if (!result.Any(ci => ReferenceEquals(ci.operand, replacement)))
            {
                Debug.Log(Initializer.LogPrefix + missingMessage);
            }

            return result;
        }

        internal static bool Calls(CodeInstruction ci, MethodInfo method)
        {
            return (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && ci.operand is MethodInfo m && m == method;
        }

        private static CodeInstruction Replaced(CodeInstruction original, OpCode opcode, object operand)
        {
            return new CodeInstruction(opcode, operand) { labels = original.labels.ToList(), blocks = original.blocks.ToList() };
        }
    }

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
            return Transpilers.ReplaceCalls(instructions, ci => Transpilers.Calls(ci, ListSort), OpCodes.Call, Skip,
                "No deck sort found in " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name);
        }

        private static void SkipSort(List<DiceCardXmlInfo> list, Comparison<DiceCardXmlInfo> comparison)
        {
        }
    }
}
