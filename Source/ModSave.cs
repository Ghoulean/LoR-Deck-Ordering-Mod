using System;
using System.Collections.Generic;
using GameSave;
using HarmonyLib;
using UnityEngine;

namespace DeckOrdering
{
    // Save persistent data into LibraryModel.CustomStorage
    internal static class ModSave
    {
        internal const string KeyPrefix = "Gh-LoRDeckOrderingMod-";

        private static readonly AccessTools.FieldRef<CustomSaveStorageModel, Dictionary<string, SaveData>> Storage =
            AccessTools.FieldRefAccess<CustomSaveStorageModel, Dictionary<string, SaveData>>("_storage");

        private static Dictionary<string, SaveData> Dict => Storage(LibraryModel.Instance.CustomStorage);

        internal static SaveData Get(string name)
        {
            return Dict.TryGetValue(KeyPrefix + name, out SaveData data) ? data : null;
        }

        internal static void Set(string name, SaveData data)
        {
            if (data == null || data.GetDataType() == SaveDataType.None)
                Dict.Remove(KeyPrefix + name);
            else
                Dict[KeyPrefix + name] = data;
        }

        internal static void ClearOwnKeys()
        {
            Dictionary<string, SaveData> dict = Dict;
            List<string> own = new List<string>();
            foreach (string key in dict.Keys)
            {
                if (key.StartsWith(KeyPrefix, StringComparison.Ordinal))
                    own.Add(key);
            }
            foreach (string key in own)
                dict.Remove(key);
        }

        internal static SaveData FromLorId(LorId id)
        {
            if (string.IsNullOrEmpty(id.packageId))
                return new SaveData(id.id);
            SaveData data = new SaveData();
            data.AddData("pid", new SaveData(id.packageId));
            data.AddData("id", new SaveData(id.id));
            return data;
        }

        internal static LorId ToLorId(SaveData data)
        {
            if (data == null)
                return null;
            if (data.GetDataType() == SaveDataType.Int)
                return new LorId(data.GetIntSelf());
            SaveData pid = data.GetData("pid");
            SaveData id = data.GetData("id");
            if (data.GetDataType() != SaveDataType.Dictionary || id == null || id.GetDataType() != SaveDataType.Int
                || pid == null || pid.GetDataType() != SaveDataType.String)
                return null;
            return new LorId(pid.GetStringSelf(), id.GetIntSelf());
        }
    }

    [HarmonyPatch(typeof(LibraryModel), nameof(LibraryModel.Init))]
    internal static class ClearModSaveOnLibraryInit
    {
        private static void Prefix()
        {
            try
            {
                ModSave.ClearOwnKeys();
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + "Failed to clear mod save data: " + e);
            }
        }
    }
}
