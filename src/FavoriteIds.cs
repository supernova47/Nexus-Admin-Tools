using System;
using System.Collections.Generic;
using System.Text;

namespace SuperNova.ValheimSpawner
{
    /// <summary>
    /// Persistent prefab IDs. Missing IDs are retained when optional mods are disabled.
    /// The comma-separated Base64 UTF-8 format safely handles arbitrary prefab names.
    /// </summary>
    internal sealed class FavoriteIds
    {
        private const int MaxEntries = 1000;
        private const int MaxIdLength = 256;
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int Count { get { return _ids.Count; } }

        public bool Contains(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && _ids.Contains(id);
        }

        public bool Toggle(string id)
        {
            if (!IsValidId(id)) return false;
            if (_ids.Remove(id)) return false;
            if (_ids.Count >= MaxEntries) return false;
            _ids.Add(id);
            return true;
        }

        public string ToEncodedString()
        {
            List<string> sorted = new List<string>(_ids);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            List<string> encoded = new List<string>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
                encoded.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(sorted[i])));
            return string.Join(",", encoded.ToArray());
        }

        public static FavoriteIds FromEncodedString(string data)
        {
            FavoriteIds result = new FavoriteIds();
            if (string.IsNullOrWhiteSpace(data)) return result;
            string[] tokens = data.Split(',');
            for (int i = 0; i < tokens.Length && result._ids.Count < MaxEntries; i++)
            {
                try
                {
                    string id = Encoding.UTF8.GetString(Convert.FromBase64String(tokens[i]));
                    if (IsValidId(id)) result._ids.Add(id);
                }
                catch (FormatException) { /* Ignore corrupt config entries. */ }
            }
            return result;
        }

        private static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength) return false;
            for (int i = 0; i < id.Length; i++)
                if (char.IsControl(id[i])) return false;
            return true;
        }
    }
}
