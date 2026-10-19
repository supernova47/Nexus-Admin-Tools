using System;
using System.IO;
using System.Text;
using SuperNova.ValheimSpawner;

internal static class NexusAdvancedPersistenceSmoke
{
    private static void Check(bool valid, string description)
    {
        if (!valid) throw new InvalidOperationException("FAIL: " + description);
    }

    private static void Main()
    {
        var data = new NexusAdvancedData { SnapshotUtc = "2026-10-17 00:00:00 UTC" };
        data.Kits.Add(new NexusKit { Name = "Рыцарь 🔥" });
        data.Kits[0].Items.Add(new NexusKitPart
        {
            PrefabId = "SwordIron", Count = 1, Quality = 10,
            CustomName = "Меч Северного Стража",
            CustomDescription = "Line 1\nLine 2: \\\" quoted"
        });
        data.History.Add(new NexusHistoryRecord
        {
            Kind = "item", PrefabId = "SwordIron", Count = 1, Quality = 10, Level = 1,
            CustomName = "Меч Северного Стража", CustomDescription = "Из истории!",
            Utc = "2026-10-17 00:00:00 UTC"
        });
        data.Snapshot.Add(new NexusCatalogueRecord { Id = "item:SwordIron", Signature = "weapon/1/10" });

        string json = data.Save();
        Check(json.Contains("\"Kits\":["), "kits explicitly serialized");
        Check(json.Contains("\"History\":["), "history explicitly serialized");
        Check(json.Contains("\"Snapshot\":["), "snapshot explicitly serialized");
        NexusAdvancedData loaded;
        string reason;
        Check(NexusAdvancedData.TryLoad(json, out loaded, out reason), "load JSON: " + reason);
        Check(data.SamePersistedContent(loaded), "complete in-memory round-trip");

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Check(NexusAdvancedData.TryLoad(File.ReadAllText(path), out loaded, out reason), "read from disk");
            Check(data.SamePersistedContent(loaded), "disk round-trip");
        }
        finally { File.Delete(path); }

        // User-submitted buildfix4 file: valid Format=1 but all arrays omitted.
        string legacy = "{\"Format\":1,\"SnapshotUtc\":\"2026-10-16 21:46:29 UTC\"}";
        Check(NexusAdvancedData.TryLoad(legacy, out loaded, out reason), "legacy incomplete JSON still readable");
        Check(loaded.Kits.Count == 0 && loaded.Snapshot.Count == 0, "missing legacy lists cannot be invented");
        Check(loaded.Format == 2, "legacy version normalized to new format");
        Check(!NexusAdvancedData.TryLoad("{\"Format\":2,\"SnapshotUtc\":\"X\"}", out loaded, out reason),
            "v2 missing arrays must be rejected");
        Check(!NexusAdvancedData.TryLoad("{\"Format\":2,\"Kits\":[", out loaded, out reason),
            "truncated JSON must be rejected");
        Check(!NexusAdvancedData.TryLoad("{\"Format\":2,\"Kits\":[],\"History\":[],\"Snapshot\":null}",
            out loaded, out reason), "null array must be rejected");

        // v1.5.0 portable, non-destructive kit transfer.
        var sourceKit = data.Kits[0];
        string kitJson = NexusKitWorkshop.Export(sourceKit);
        Check(kitJson.Contains("\"Kits\":["), "kit transfer contains a kit array");
        Check(kitJson.Contains("\"History\":[]"), "history excluded from export");
        Check(kitJson.Contains("\"Snapshot\":[]"), "radar excluded from export");
        NexusKit imported;
        Check(NexusKitWorkshop.TryImport(kitJson, out imported, out reason), "import one kit: " + reason);
        Check(imported.Name == sourceKit.Name && imported.Items.Count == 1 &&
            imported.Items[0].CustomDescription == sourceKit.Items[0].CustomDescription,
            "import preserves Unicode names and descriptions");
        imported.Items[0].CustomName = "Separate instance";
        Check(sourceKit.Items[0].CustomName != imported.Items[0].CustomName,
            "deep copy must not mutate saved source kit");
        Check(!NexusKitWorkshop.TryImport(json, out imported, out reason),
            "full advanced state must never import as one kit");
        Check(!NexusKitWorkshop.TryImport("{broken", out imported, out reason),
            "malformed clipboard JSON is rejected");
        Check(NexusKitWorkshop.UniqueName("Рыцарь 🔥", data.Kits) == "Рыцарь 🔥 (2)",
            "imported kit gets a collision-free name");
        var invalidKit = NexusKitWorkshop.DeepCopy(sourceKit);
        invalidKit.Items[0].Count = 9999;
        Check(NexusKitWorkshop.Validate(invalidKit, out reason), "9999 items per row are supported");
        invalidKit.Items[0].Count = 10000;
        Check(!NexusKitWorkshop.Validate(invalidKit, out reason),
            "invalid per-row quantity is rejected");
        var large = new NexusKit { Name = "100 строк" };
        for (int i = 0; i < 100; i++) large.Items.Add(new NexusKitPart { PrefabId = "Item" + i, Count = 9999, Quality = 1 });
        Check(NexusKitWorkshop.Validate(large, out reason), "100 kit rows supported");
        Check(NexusKitWorkshop.TryImport(NexusKitWorkshop.Export(large), out imported, out reason) && imported.Items.Count == 100,
            "100-row kit survives export/import without truncation");
        large.Items.Add(new NexusKitPart { PrefabId = "Overflow" });
        Check(!NexusKitWorkshop.Validate(large, out reason), "101 rows rejected without mutating stored state");
        // Old on-disk Format 2 state stays byte-for-byte parseable by v1.5.0.
        Check(NexusAdvancedData.TryLoad(json, out loaded, out reason) &&
            data.SamePersistedContent(loaded), "v1.4.0 state still round-trips");
        Console.WriteLine("Tests passed: v1.4.0 persistence, v1.5.0 kit clipboard export/import, deep copies, invalid-data rejection and compatibility.");
    }
}
