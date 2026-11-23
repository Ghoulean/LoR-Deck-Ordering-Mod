using System;
using System.Collections.Generic;
using System.Linq;
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

        internal static SaveData Get(string name) =>
            Dict.TryGetValue(KeyPrefix + name, out SaveData data) ? data : null;

        internal static void Set(string name, SaveData data)
        {
            if (IsEmpty(data))
                Dict.Remove(KeyPrefix + name);
            else
                Dict[KeyPrefix + name] = data;
        }

        internal static void ClearOwnKeys()
        {
            Dictionary<string, SaveData> dict = Dict;
            foreach (string key in OwnKeys(dict.Keys).ToList())
                dict.Remove(key);
        }

        private static bool IsEmpty(SaveData data) => data == null || data.GetDataType() == SaveDataType.None;

        private static IEnumerable<string> OwnKeys(IEnumerable<string> keys) =>
            keys.Where(key => key.StartsWith(KeyPrefix, StringComparison.Ordinal));

        internal static IEnumerable<SaveData> Items(SaveData data)
        {
            if (data?.GetDataType() != SaveDataType.List)
                yield break;
            foreach (SaveData item in data)
                yield return item;
        }

        internal static IEnumerable<SaveData> Dictionaries(SaveData data) =>
            Items(data).Where(item => item.GetDataType() == SaveDataType.Dictionary);

        internal static int? IntOf(SaveData data) =>
            data?.GetDataType() == SaveDataType.Int ? data.GetIntSelf() : (int?)null;

        internal static string StringOf(SaveData data) =>
            data?.GetDataType() == SaveDataType.String ? data.GetStringSelf() : null;

        internal static SaveData ListOf(IEnumerable<SaveData> items) =>
            items.Aggregate(new SaveData(SaveDataType.List), (list, item) =>
            {
                list.AddToList(item);
                return list;
            });

        internal static SaveData NonEmptyListOf(IEnumerable<SaveData> items)
        {
            List<SaveData> all = items.ToList();
            return all.Count == 0 ? null : ListOf(all);
        }

        internal static SaveData DictOf(params KeyValuePair<string, SaveData>[] entries) =>
            entries.Aggregate(new SaveData(), (dict, entry) =>
            {
                dict.AddData(entry.Key, entry.Value);
                return dict;
            });

        internal static KeyValuePair<string, SaveData> Entry(string key, SaveData value) =>
            new KeyValuePair<string, SaveData>(key, value);

        internal static SaveData FromLorId(LorId id) =>
            string.IsNullOrEmpty(id.packageId)
                ? new SaveData(id.id)
                : DictOf(Entry("pid", new SaveData(id.packageId)), Entry("id", new SaveData(id.id)));

        internal static LorId ToLorId(SaveData data)
        {
            if (data == null)
                return null;
            if (data.GetDataType() == SaveDataType.Int)
                return new LorId(data.GetIntSelf());
            if (data.GetDataType() != SaveDataType.Dictionary)
                return null;
            string pid = StringOf(data.GetData("pid"));
            int? id = IntOf(data.GetData("id"));
            return pid != null && id.HasValue ? new LorId(pid, id.Value) : null;
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
