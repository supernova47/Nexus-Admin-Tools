using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SuperNova.ValheimSpawner
{
    // Stable, explicit wire format for the three Nexus Advanced collections.
    // This is deliberately independent of Unity's serializer: Unity 6000 can
    // silently omit the List<T> members of these managed model types.
    internal static class NexusAdvancedJson
    {
        private const int MaxJsonLength = 4 * 1024 * 1024;

        internal static string Serialize(NexusAdvancedData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var sb = new StringBuilder(4096);
            sb.Append("{\"Format\":2,\"Kits\":[");
            for (int i = 0; i < data.Kits.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NexusKit kit = data.Kits[i];
                if (kit == null) throw new InvalidOperationException("Null kit at " + i);
                sb.Append("{\"Name\":"); WriteString(sb, kit.Name);
                sb.Append(",\"Items\":[");
                for (int j = 0; j < kit.Items.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    NexusKitPart part = kit.Items[j];
                    if (part == null) throw new InvalidOperationException("Null kit part at " + i + ":" + j);
                    sb.Append("{\"PrefabId\":"); WriteString(sb, part.PrefabId);
                    sb.Append(",\"Count\":").Append(part.Count.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"Quality\":").Append(part.Quality.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"CustomName\":"); WriteString(sb, part.CustomName);
                    sb.Append(",\"CustomDescription\":"); WriteString(sb, part.CustomDescription);
                    sb.Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("],\"History\":[");
            for (int i = 0; i < data.History.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NexusHistoryRecord record = data.History[i];
                if (record == null) throw new InvalidOperationException("Null history entry at " + i);
                sb.Append("{\"Kind\":"); WriteString(sb, record.Kind);
                sb.Append(",\"PrefabId\":"); WriteString(sb, record.PrefabId);
                sb.Append(",\"CustomName\":"); WriteString(sb, record.CustomName);
                sb.Append(",\"CustomDescription\":"); WriteString(sb, record.CustomDescription);
                sb.Append(",\"Utc\":"); WriteString(sb, record.Utc);
                sb.Append(",\"Count\":").Append(record.Count.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"Quality\":").Append(record.Quality.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"Level\":").Append(record.Level.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append("],\"Snapshot\":[");
            for (int i = 0; i < data.Snapshot.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NexusCatalogueRecord item = data.Snapshot[i];
                if (item == null) throw new InvalidOperationException("Null snapshot entry at " + i);
                sb.Append("{\"Id\":"); WriteString(sb, item.Id);
                sb.Append(",\"Signature\":"); WriteString(sb, item.Signature);
                sb.Append('}');
            }
            sb.Append("],\"SnapshotUtc\":"); WriteString(sb, data.SnapshotUtc);
            sb.Append('}');
            if (sb.Length > MaxJsonLength) throw new InvalidOperationException("Nexus state exceeds 4 MiB.");
            return sb.ToString();
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (value != null)
                foreach (char c in value)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
            sb.Append('"');
        }

        internal static bool TryDeserialize(string json, out NexusAdvancedData data, out string reason)
        {
            data = null;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength)
            {
                reason = "State is empty or exceeds 4 MiB.";
                return false;
            }
            try
            {
                var reader = new JsonReader(json);
                var root = reader.Parse() as Dictionary<string, object>;
                if (root == null) throw new FormatException("Expected a JSON object.");
                int version = Number(root, "Format", 0);
                if (version != 1 && version != 2) throw new FormatException("Unsupported state Format " + version);
                // Version 2 mandates explicit arrays. Version 1 may omit them
                // because Unity JsonUtility failed to serialize these lists.
                var result = new NexusAdvancedData { Format = 2, SnapshotUtc = Text(root, "SnapshotUtc") };
                LoadList(root, "Kits", version == 2, 25, obj =>
                {
                    var kit = new NexusKit { Name = Text(obj, "Name") };
                    LoadList(obj, "Items", false, 100, part =>
                    {
                        kit.Items.Add(new NexusKitPart
                        {
                            PrefabId = Text(part, "PrefabId"),
                            Count = Number(part, "Count", 1),
                            Quality = Number(part, "Quality", 1),
                            CustomName = Text(part, "CustomName"),
                            CustomDescription = Text(part, "CustomDescription")
                        });
                    });
                    result.Kits.Add(kit);
                });
                LoadList(root, "History", version == 2, 100, obj => result.History.Add(new NexusHistoryRecord
                {
                    Kind = Text(obj, "Kind"),
                    PrefabId = Text(obj, "PrefabId"),
                    CustomName = Text(obj, "CustomName"),
                    CustomDescription = Text(obj, "CustomDescription"),
                    Utc = Text(obj, "Utc"),
                    Count = Number(obj, "Count", 1),
                    Quality = Number(obj, "Quality", 1),
                    Level = Number(obj, "Level", 1)
                }));
                LoadList(root, "Snapshot", version == 2, 4500, obj => result.Snapshot.Add(new NexusCatalogueRecord
                {
                    Id = Text(obj, "Id"), Signature = Text(obj, "Signature")
                }));
                // Old JSON can carry a stale snapshot time despite losing the
                // entire Snapshot array. Do not advertise it as a valid baseline.
                if (version == 1 && !root.ContainsKey("Snapshot"))
                    result.SnapshotUtc = string.Empty;
                data = result;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static string Text(Dictionary<string, object> obj, string key)
        {
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return string.Empty;
            string result = value as string;
            if (result == null) throw new FormatException("Expected string: " + key);
            return result;
        }

        private static int Number(Dictionary<string, object> obj, string key, int fallback)
        {
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return fallback;
            if (!(value is int)) throw new FormatException("Expected integer: " + key);
            return (int)value;
        }

        private static void LoadList(Dictionary<string, object> obj, string key, bool required,
            int limit, Action<Dictionary<string, object>> add)
        {
            object value;
            if (!obj.TryGetValue(key, out value))
            {
                if (required) throw new FormatException("Missing collection " + key);
                return;
            }
            var list = value as List<object>;
            if (list == null) throw new FormatException("Expected array: " + key);
            if (list.Count > limit) throw new FormatException("Collection limit exceeded: " + key);
            foreach (object entry in list)
            {
                var record = entry as Dictionary<string, object>;
                if (record == null) throw new FormatException("Invalid record in " + key);
                add(record);
            }
        }

        // Minimal strict JSON reader, with no dependency on game assemblies or NuGet.
        // Unknown properties are retained only while parsing and ignored by the schema.
        private sealed class JsonReader
        {
            private readonly string _json;
            private int _index;
            internal JsonReader(string json) { _json = json; }
            internal object Parse()
            {
                Skip();
                object result = ReadValue(0);
                Skip();
                if (_index != _json.Length) throw new FormatException("Trailing JSON content.");
                return result;
            }
            private void Skip()
            {
                while (_index < _json.Length && char.IsWhiteSpace(_json[_index])) _index++;
            }
            private char Peek()
            {
                if (_index >= _json.Length) throw new FormatException("Unexpected end of JSON.");
                return _json[_index];
            }
            private bool Consume(char c)
            {
                Skip();
                if (_index < _json.Length && _json[_index] == c) { _index++; return true; }
                return false;
            }
            private void Expect(char c)
            {
                if (!Consume(c)) throw new FormatException("Expected '" + c + "' at position " + _index);
            }
            private object ReadValue(int depth)
            {
                if (depth > 12) throw new FormatException("JSON nesting limit exceeded.");
                Skip();
                switch (Peek())
                {
                    case '{':
                        _index++;
                        var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                        if (Consume('}')) return obj;
                        do
                        {
                            Skip();
                            string key = ReadString();
                            Expect(':');
                            object value = ReadValue(depth + 1);
                            if (obj.ContainsKey(key)) throw new FormatException("Duplicate property: " + key);
                            obj.Add(key, value);
                            if (Consume('}')) return obj;
                            Expect(',');
                        } while (true);
                    case '[':
                        _index++;
                        var list = new List<object>();
                        if (Consume(']')) return list;
                        do
                        {
                            list.Add(ReadValue(depth + 1));
                            if (Consume(']')) return list;
                            Expect(',');
                        } while (true);
                    case '"': return ReadString();
                    case 'n': ReadLiteral("null"); return null;
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    default: return ReadNumber();
                }
            }
            private void ReadLiteral(string literal)
            {
                for (int i = 0; i < literal.Length; i++)
                    if (Peek() != literal[i]) throw new FormatException("Invalid JSON literal.");
                    else _index++;
            }
            private int ReadNumber()
            {
                int start = _index;
                if (Peek() == '-') _index++;
                if (_index >= _json.Length || !char.IsDigit(_json[_index])) throw new FormatException("Invalid number.");
                while (_index < _json.Length && _json[_index] >= '0' && _json[_index] <= '9') _index++;
                // The schema contains only integers; reject fractions and exponents.
                if (_index < _json.Length && (_json[_index] == '.' || _json[_index] == 'e' || _json[_index] == 'E'))
                    throw new FormatException("Non-integer number in state.");
                int result;
                if (!int.TryParse(_json.Substring(start, _index - start), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out result)) throw new FormatException("Integer out of range.");
                return result;
            }
            private string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Peek(); _index++;
                    if (c == '"') return sb.ToString();
                    if (c < 32) throw new FormatException("Unescaped control character.");
                    if (c != '\\') { sb.Append(c); continue; }
                    c = Peek(); _index++;
                    switch (c)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _json.Length) throw new FormatException("Incomplete Unicode escape.");
                            int n;
                            if (!int.TryParse(_json.Substring(_index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out n)) throw new FormatException("Invalid Unicode escape.");
                            sb.Append((char)n); _index += 4; break;
                        default: throw new FormatException("Invalid JSON escape.");
                    }
                }
            }
        }
    }
}
