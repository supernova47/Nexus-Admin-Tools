# Valheim Nexus v1.5.0 — Test Plan (manual)

**Prerequisites:** Back up the 1.4.0 Build Fix 5 DLL and these files from the active r2modman profile: `BepInEx/config/SuperNova.ValheimNexus.Advanced.json`, its `.bak`, and `com.supernova.valheimspawner.cfg`. Compile v1.5.0 with `BuildMod.bat`. Test in a disposable world, not the main save.

1. Launch the game with the *same mod profile*. Existing Kits, History and Radar should appear unchanged. Check favorites and RU/EN preference.
2. Create a new Kit and add a one-stack weapon with an individual name/description. Click **Edit** for that row; change count, quality, name and description; click **Save Item**. Verify the summary and apply it. Check item tooltip and full durability.
3. Press **Duplicate Kit**. Confirm another independent kit appears, with a different name; editing/removing rows in the copy must not touch the original.
4. Use **Rename** and persist a new name. Exit completely, restart the same mod profile and verify both kits and all edited fields. Check History and Radar unchanged.
5. **Copy Kit JSON** to clipboard. Switch to another kit or restart and click **Import Kit JSON**. Import must create a *new* kit; existing kits must not be replaced. Use the clipboard text in a second profile to validate portability (the item prefab must exist in that profile).
6. Replace clipboard contents with malformed JSON and attempt import; verify the operation reports an error and does not alter stored presets. A complete Nexus state containing History or Radar must also be rejected by the *single kit* importer.
7. Press **Verify Saved Data**. It should confirm the on-disk file matches memory after successful saves. Verify `Format:2` with explicit `Kits`, `History`, `Snapshot` arrays (the actual JSON uses `"Format":2`).
8. Use History search and switch filters All/Items/Creatures/Kits. Check Reuse only fills fields and does not spawn anything automatically.
9. Test editing an unavailable modded prefab row (should preserve the ID). Applying it should fail with an explicit message rather than delete the row.
10. Install 1.4.0 Build Fix 5 again in the same profile and confirm it can still load the state saved by v1.5.0 (Format 2 unchanged); return to v1.5.0 afterward.

**Acceptance:** no lost Kits/History/Radar, no changes to base game files or owned DLC checks, no crashes, and no silent failure on bad clipboard input. If something fails, attach `LogOutput.log` and the saved JSON after backing them up.

**Not automated in this environment:** Valheim runtime or DLL build. The provided .NET 8 smoke test checks source-level codec/transfer logic once run on the user's development PC.
