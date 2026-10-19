# Kit Workshop — SuperNova Valheim Nexus v1.5.0

The Kit Workshop extends **Kits** in the second row of the Nexus HUD (`F6`).

## Edit an item inside a Kit

1. Select a saved Kit using the `◀` / `▶` arrows.
2. Click **Edit** next to an item.
3. Update Count (1–50), Quality (1–10, subject to prefab support), custom name, or custom description.
4. Click **Save Item** to persist. **Cancel** discards your uncommitted edits.
5. The prefab ID is intentionally read-only. To change it, add a different item from a category tab.

## Rename or duplicate a Kit

- **Rename** toggles a name field with **Save** and **Cancel**. Maximum 40 characters.
- **Duplicate Kit** clones every entry (including custom name and description) into a new Kit, selecting it immediately. Each preset is stored independently.
- Maximum 25 kits, maximum 12 rows per kit. Existing Kit application safety (up to 50 total units per action) remains unchanged.

## Portable import / export

- **Copy Kit JSON** places one kit's data in the system clipboard. It does not include History or Compatibility Radar records.
- **Import Kit JSON** validates a one-kit payload, adds it without replacing existing kits and persists the result. A collision-free name is generated if necessary.
- Importing unsupported prefabs does *not* spawn them or delete entries; an item must exist in the current ObjectDB when the kit is applied.
- Never use the full `SuperNova.ValheimNexus.Advanced.json` state file as a one-kit import. It is deliberately rejected when it includes history or Radar data.

## Storage diagnostics

- **Verify Saved Data** compares Kits, History and Radar held by the mod against the active JSON on disk, without saving anything.
- Data is still kept in `BepInEx/config/SuperNova.ValheimNexus.Advanced.json` (Format 2) and `.bak`; configuration for favorites, language and hotkeys stays in `com.supernova.valheimspawner.cfg`.
- **History** now supports text search and All / Items / Creatures / Kits filters. Filters are session-only and do not remove stored operations.

## Update and validation

Make a copy of the active r2modman profile's config files and its working 1.4.0 Build Fix 5 DLL before upgrading. Use `BuildMod.bat` on Windows with Valheim, BepInEx, and compatible .NET SDK installed. Run `TEST_PLAN_1.5.0.md` in a test world.
