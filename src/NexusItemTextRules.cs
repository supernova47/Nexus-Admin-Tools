using System.Collections.Generic;

namespace SuperNova.ValheimSpawner
{
    // Namekeeper intentionally keeps Nexus's original metadata as a fallback.
    // These rules mirror the effective-text precedence used by that mod.
    internal static class NexusItemTextRules
    {
        internal const string NexusNameKey = "com.supernova.valheimnexus.itemname.v1";
        internal const string NexusDescriptionKey = "com.supernova.valheimnexus.itemdescription.v1";
        internal const string NamekeeperNameKey = "com.supernova.namekeeper.name";
        internal const string NamekeeperDescriptionKey = "com.supernova.namekeeper.description";
        internal const string NamekeeperResetNameKey = "com.supernova.namekeeper.original-name";
        internal const string NamekeeperResetDescriptionKey = "com.supernova.namekeeper.original-description";

        internal static string ReadName(IDictionary<string, string> data, string nexusName)
        {
            if (data != null)
            {
                if (data.TryGetValue(NamekeeperNameKey, out string name) && !string.IsNullOrEmpty(name)) return name;
                if (data.TryGetValue(NamekeeperResetNameKey, out string reset) && reset == "1") return string.Empty;
            }
            return nexusName ?? string.Empty;
        }

        internal static string ReadDescription(IDictionary<string, string> data, string nexusDescription)
        {
            if (data != null)
            {
                if (data.TryGetValue(NamekeeperDescriptionKey, out string description)) return description ?? string.Empty;
                if (data.TryGetValue(NamekeeperResetDescriptionKey, out string reset) && reset == "1") return string.Empty;
            }
            return nexusDescription ?? string.Empty;
        }

        internal static void Stamp(IDictionary<string, string> data, string name, string description)
        {
            if (data == null) return;
            // Clear Namekeeper overrides/reset markers so the new kit's chosen text
            // is authoritative, including when it intentionally uses the default.
            data.Remove(NamekeeperNameKey);
            data.Remove(NamekeeperDescriptionKey);
            data.Remove(NamekeeperResetNameKey);
            data.Remove(NamekeeperResetDescriptionKey);
            if (string.IsNullOrEmpty(name)) data.Remove(NexusNameKey);
            else data[NexusNameKey] = name;
            if (string.IsNullOrEmpty(description)) data.Remove(NexusDescriptionKey);
            else data[NexusDescriptionKey] = description;
        }
    }
}
