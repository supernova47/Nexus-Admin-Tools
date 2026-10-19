using System;
using System.Collections.Generic;

namespace SuperNova.ValheimSpawner
{
    // JsonUtility serializes public fields, not properties. All persisted identifiers use
    // prefab IDs; missing content is preserved to support temporarily disabled mods.
    [Serializable]
    internal sealed class NexusKitPart
    {
        public string PrefabId = string.Empty;
        public int Count = 1;
        public int Quality = 1;
        public string CustomName = string.Empty;
        public string CustomDescription = string.Empty;
    }

    [Serializable]
    internal sealed class NexusKit
    {
        public string Name = string.Empty;
        public List<NexusKitPart> Items = new List<NexusKitPart>();
    }

    [Serializable]
    internal sealed class NexusHistoryRecord
    {
        public string Kind = string.Empty; // item / creature / kit
        public string PrefabId = string.Empty;
        public string CustomName = string.Empty;
        public string CustomDescription = string.Empty;
        public string Utc = string.Empty;
        public int Count = 1;
        public int Quality = 1;
        public int Level = 1;
    }

    [Serializable]
    internal sealed class NexusCatalogueRecord
    {
        public string Id = string.Empty;
        public string Signature = string.Empty;
    }

    [Serializable]
    internal sealed class NexusAdvancedData
    {
        public int Format = 2;
        public List<NexusKit> Kits = new List<NexusKit>();
        public List<NexusHistoryRecord> History = new List<NexusHistoryRecord>();
        public List<NexusCatalogueRecord> Snapshot = new List<NexusCatalogueRecord>();
        public string SnapshotUtc = string.Empty;

        public static bool TryLoad(string json, out NexusAdvancedData data, out string reason)
        {
            return NexusAdvancedJson.TryDeserialize(json, out data, out reason);
        }

        public static NexusAdvancedData Load(string json)
        {
            NexusAdvancedData data;
            string reason;
            return TryLoad(json, out data, out reason) ? data : new NexusAdvancedData();
        }

        // Verify important payloads after a JSON round-trip, not merely list sizes.
        // An incompatible serializer should never silently replace a preset or history.
        public bool SamePersistedContent(NexusAdvancedData other)
        {
            if (other == null || Format != other.Format || SnapshotUtc != other.SnapshotUtc ||
                Kits.Count != other.Kits.Count || History.Count != other.History.Count ||
                Snapshot.Count != other.Snapshot.Count) return false;
            for (int i = 0; i < Kits.Count; i++)
            {
                NexusKit a = Kits[i], b = other.Kits[i];
                if (a == null || b == null || a.Name != b.Name || a.Items.Count != b.Items.Count) return false;
                for (int j = 0; j < a.Items.Count; j++)
                {
                    NexusKitPart x = a.Items[j], y = b.Items[j];
                    if (x == null || y == null || x.PrefabId != y.PrefabId || x.Count != y.Count ||
                        x.Quality != y.Quality || x.CustomName != y.CustomName ||
                        x.CustomDescription != y.CustomDescription) return false;
                }
            }
            for (int i = 0; i < History.Count; i++)
            {
                NexusHistoryRecord a = History[i], b = other.History[i];
                if (a == null || b == null || a.Kind != b.Kind || a.PrefabId != b.PrefabId ||
                    a.CustomName != b.CustomName || a.CustomDescription != b.CustomDescription ||
                    a.Utc != b.Utc || a.Count != b.Count || a.Quality != b.Quality || a.Level != b.Level)
                    return false;
            }
            for (int i = 0; i < Snapshot.Count; i++)
            {
                NexusCatalogueRecord a = Snapshot[i], b = other.Snapshot[i];
                if (a == null || b == null || a.Id != b.Id || a.Signature != b.Signature) return false;
            }
            return true;
        }

        public string Save()
        {
            return NexusAdvancedJson.Serialize(this);
        }

        public static string CleanName(string name, int limit = 40)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            name = name.Trim();
            var result = new System.Text.StringBuilder(Math.Min(limit, name.Length));
            for (int i = 0; i < name.Length && result.Length < limit; i++)
            {
                char c = name[i];
                // Rich text, line breaks, and control characters must not inject UI markup.
                if (!char.IsControl(c) && c != '<' && c != '>') result.Append(c);
            }
            return result.ToString().Trim();
        }

        public static string CleanDescription(string description, int limit = 480)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            var result = new System.Text.StringBuilder(Math.Min(limit, description.Length));
            for (int i = 0; i < description.Length && result.Length < limit; i++)
            {
                char c = description[i];
                if (c == '\r') continue;
                if (c == '\n' || (!char.IsControl(c) && c != '<' && c != '>'))
                    result.Append(c);
            }
            return result.ToString().Trim();
        }

        public void AddHistory(string kind, string prefab, int count, int quality, int level, string customName, string customDescription = "")
        {
            History.Insert(0, new NexusHistoryRecord
            {
                Kind = kind ?? string.Empty,
                PrefabId = prefab ?? string.Empty,
                Count = Math.Max(1, count),
                Quality = Math.Max(1, quality),
                Level = Math.Max(1, level),
                CustomName = CleanName(customName),
                CustomDescription = CleanDescription(customDescription),
                Utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture)
            });
            if (History.Count > 100) History.RemoveRange(100, History.Count - 100);
        }
    }
}
