# Nexus Advanced persistence — Build Fix 5

**Root cause confirmed from user JSON:** the Unity `JsonUtility` serializer emitted `Format` and `SnapshotUtc` but omitted `Kits`, `History` and `Snapshot`. Such files cannot restore any missing contents.

Build Fix 5 writes explicit **Format 2** JSON including `Kits`, `History` and `Snapshot` arrays, verifies their contents after saving, and creates a backup on the **first** successful write. The old Format 1 file is retained as `.legacy-format1.json` before migration, while `.bak` contains a valid Format 2 state after migration. This data lives at:

`BepInEx/config/SuperNova.ValheimNexus.Advanced.json`

On a managed mod profile (e.g. r2modman), `BepInEx/config` belongs to the active profile. Check `LogOutput.log` for `Nexus Advanced storage path:` to identify the effective location.

Do not delete existing `.json`, `.bak` or `com.supernova.valheimspawner.cfg` files. Back up the complete config directory before installing. Legacy `Format 1` JSON and old `[NexusAdvanced] Data` configuration can be read; records absent from old serialized files cannot be reconstructed automatically.

### Verify persistence
1. Build and install Fix 5 from the full source archive. Ensure only one installed DLL is active.
2. Open the same test world and create a Kit with a named item and description.
3. Add a new entry to History and save a Radar snapshot.
4. Inspect the log: `Nexus Advanced state saved: N kits, M history entries, K snapshot records`.
5. Open the JSON. It must explicitly contain `"Format":2`, `"Kits":[...]`, `"History":[...]` and `"Snapshot":[...]`.
6. Exit the entire game, relaunch using the **same mod profile**, and verify each record is present.
7. Check that a `.bak` file was created after the first successful write.

### Isolated persistence test (on your PC)
`dotnet run --project tests/NexusAdvancedPersistence.Tests.csproj`

The test uses the game-independent `NexusAdvancedData.cs`, `NexusAdvancedJson.cs` and `NexusKitWorkshop.cs`; it does not need Valheim or BepInEx game assemblies. It requires .NET 8 SDK/runtime.

# Nexus v1.4.0 — persistent data and migration (buildfix4)

## Where data lives

- `BepInEx/config/SuperNova.ValheimNexus.Advanced.json`: equipment kits, recent history (100 maximum), compatibility radar snapshot. This file is **NOT part of the mod ZIP**.
- `BepInEx/config/SuperNova.ValheimNexus.Advanced.json.bak`: previous valid version of that JSON, created on the first successful write (Build Fix 5 and later).
- `BepInEx/config/com.supernova.valheimspawner.cfg`: original BepInEx settings: language, hotkeys, default creature count/level, kill radius, item/creature favorites, **legacy** `[NexusAdvanced] Data` JSON.
- `ItemData.m_customData`: custom per-item name and description, serialized with game item data (subject to Valheim's character/world saving and multiplayer rules).

The saved JSON is stored in the active r2modman profile's BepInEx/config. **Changing r2modman profiles changes the data directory**. When updating a mod, keep the same profile and do not delete the BepInEx/config directory. Before updating, copy both the `.cfg` and `.json` (including `.bak`) to a safe place.

## Migration from 1.4.0 builds 1–3

When the new JSON file does not exist, the mod first checks for a valid `.bak`, then attempts to load the previous `[NexusAdvanced] Data` value from the existing BepInEx config. If decoding succeeds, it creates the new JSON file and **does not delete or blank** the original config value. If a file exists but cannot be decoded, the mod tries `.bak`. Bad primary data is copied to `.corrupt` before restoring `.bak`. If both are broken, it blocks further writes and logs the issue rather than erasing anything. If only old config can be recovered, it is loaded into memory, but writing remains blocked until manual inspection.

## Scope of persistence

- Kits (name, item IDs, quantity, quality, custom name and description): saved on each edit.
- History: saved on each operation, up to 100 events; reusable details include both name and description.
- Radar snapshots: saved when clicking Save Snapshot; comparison report text is not persisted.
- Favorites and UI language: saved by BepInEx, with explicit Config.Save after changes.
- Custom names/descriptions of created **items**: stored in ItemData.m_customData; *not* kept in the plugin JSON unless added to Kits or History.
- HUD position, open tab, searches, latest Doctor report: session-only by design.

## How to test restart and update

1. Back up the active r2modman profile's `.cfg`, `.json`, and any `.bak`.
2. Make a named kit with an item containing a custom description; add item to kit, close game fully.
3. Check `BepInEx/config/SuperNova.ValheimNexus.Advanced.json` exists; restart the same profile and verify kit remains, including per-item description.
4. Add the kit, verify custom title and description in inventory, save the character, restart, check again.
5. Spawn a creature, verify History survives restart; use a record to restore parameters.
6. Create Radar snapshot and verify its timestamp persists after restart and refresh.
7. Favorite an item and creature, select Russian language, restart and verify both settings.
8. Upgrade in the same r2modman profile while preserving BepInEx/config; confirm all data remains.
9. Test fallback on a **copy of the profile only**: break JSON while keeping `.bak`; check log and recovery, and verify a `.corrupt` copy exists.

If data disappeared before installing buildfix4, this code cannot recreate it without a saved copy of the old config or data file. If that happens, inspect `com.supernova.valheimspawner.cfg` for `[NexusAdvanced] Data` before making changes.

## v1.5.0 portable Kits and backup guidance

**Kit import/export does not touch the main state file format or locations.** `Copy Kit JSON` exports a single entry in a valid Format 2 envelope, with `History:[]` and `Snapshot:[]`. `Import Kit JSON` accepts only an envelope with one kit and empty History/Radar, assigns a unique name, appends the kit, saves using the existing atomic path, and verifies it. It never replaces the entire state.

Keep both the main file and `.bak` when updating; the `.bak` preserves the *previous* valid saved state on later writes (not necessarily the same state as the current main file). A failed import should not alter existing kits.
