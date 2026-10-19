using System;
using System.Collections.Generic;
using SuperNova.ValheimSpawner;

internal static class Program
{
    private static int _checks;
    private static string ReadKitName(IDictionary<string, string> data) =>
        NexusItemTextRules.ReadName(data, data.TryGetValue(NexusItemTextRules.NexusNameKey, out string value) ? value : string.Empty);

    private static string ReadKitDescription(IDictionary<string, string> data) =>
        NexusItemTextRules.ReadDescription(data, data.TryGetValue(NexusItemTextRules.NexusDescriptionKey, out string value) ? value : string.Empty);

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        var data = new Dictionary<string, string>
        {
            [NexusItemTextRules.NexusNameKey] = "Nexus original",
            [NexusItemTextRules.NexusDescriptionKey] = "Nexus description"
        };
        Check(ReadKitName(data) == "Nexus original", "Nexus metadata is initial fallback");
        Check(ReadKitDescription(data) == "Nexus description", "Nexus description is initial fallback");

        data[NexusItemTextRules.NamekeeperNameKey] = "Namekeeper first";
        data[NexusItemTextRules.NamekeeperDescriptionKey] = "First description";
        Check(ReadKitName(data) == "Namekeeper first", "Namekeeper rename takes precedence");
        Check(ReadKitDescription(data) == "First description", "Namekeeper description takes precedence");

        data[NexusItemTextRules.NamekeeperNameKey] = "Namekeeper second";
        data[NexusItemTextRules.NamekeeperDescriptionKey] = "Second description";
        Check(ReadKitName(data) == "Namekeeper second", "Later rename replaces the first rename");
        Check(ReadKitDescription(data) == "Second description", "Later description replaces the first");

        data.Remove(NexusItemTextRules.NamekeeperNameKey);
        data[NexusItemTextRules.NamekeeperResetNameKey] = "1";
        data.Remove(NexusItemTextRules.NamekeeperDescriptionKey);
        data[NexusItemTextRules.NamekeeperResetDescriptionKey] = "1";
        Check(ReadKitName(data) == string.Empty, "Reset name means use vanilla prefab name");
        Check(ReadKitDescription(data) == string.Empty, "Reset description means use vanilla description");

        NexusItemTextRules.Stamp(data, string.Empty, string.Empty);
        Check(!data.ContainsKey(NexusItemTextRules.NamekeeperNameKey) && !data.ContainsKey(NexusItemTextRules.NamekeeperResetNameKey), "Kit stamp removes stale Namekeeper name markers");
        Check(!data.ContainsKey(NexusItemTextRules.NexusNameKey), "Kit stamp clears custom Nexus name when using default");
        Check(ReadKitName(data) == string.Empty, "Captured default remains the default after kit application");
        Check(!data.ContainsKey(NexusItemTextRules.NamekeeperDescriptionKey) && !data.ContainsKey(NexusItemTextRules.NamekeeperResetDescriptionKey), "Kit stamp removes stale Namekeeper description markers");
        Check(!data.ContainsKey(NexusItemTextRules.NexusDescriptionKey), "Kit stamp clears custom description when using default");

        NexusItemTextRules.Stamp(data, "Namekeeper second", "Second description");
        Check(ReadKitName(data) == "Namekeeper second", "Kit custom name is retained");
        Check(ReadKitDescription(data) == "Second description", "Kit custom description is retained");
        Console.WriteLine("PASS " + _checks + " Namekeeper item name and kit capture checks");
    }
}



