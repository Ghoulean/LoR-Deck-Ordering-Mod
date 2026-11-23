using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeckOrdering
{
    public class Initializer : ModInitializer
    {
        public const string HarmonyId = "ghoulean.deckordering";
        public const string LogPrefix = "[DeckOrdering] ";

        public override void OnInitializeMod()
        {
            try
            {
                new Harmony(HarmonyId).PatchAll(Assembly.GetExecutingAssembly());
                Debug.Log(LogPrefix + "Patched.");
            }
            catch (Exception e)
            {
                Debug.Log(LogPrefix + "Failed to patch: " + e);
            }
        }
    }
}
