# SuperNova Nexus v1.6.0 — Gameplay acceptance plan

**Baseline:** user-tested v1.5.0. Perform in a disposable world; back up `BepInEx/config`, the working DLL, and character/world saves before changing admin options.

## Build and load
- [ ] Run `BuildMod.bat` on a PC with Valheim 1.0.20 (or your target version), BepInEx and the .NET SDK; resolve *real* compiler errors before using a DLL.
- [ ] Confirm `SuperNova Valheim Nexus 1.6.0` loads; inspect `Nexus Admin hooks: adrenaline=... outgoing damage=...` in `LogOutput.log`.
- [ ] Verify existing `SuperNova.ValheimNexus.Advanced.json` Kits, History and Radar survive restart and rollback to 1.5.0.

## HUD
- [ ] At 1920x1080 and 1280x720 (and at non-default UI scale), check all tab buttons, Wiki, EN/RU, Close and pagination.
- [ ] Open/close advanced tabs and confirm contents are still scrollable; no repeated GUI handlers.
- [ ] Expand/collapse item diagnostics and custom description; ensure nonempty hidden description is preserved for creation.
- [ ] Expand/collapse the creatures' **destructive** tools; verify kill preview and confirm protections from v1.2.1.
- [ ] Verify text input does not trigger player controls or spawn shortcuts.

## 3D markers
- [ ] In Creatures, select a creature with amount 1, then 3 and 50. Enable 3D markers. Expect at most 12 cyan discs/orbs, without collisions or actual creatures.
- [ ] Switch from **In front** to **At crosshair**. If no surface is targeted, markers disappear rather than showing stale positions.
- [ ] Compare marker positions to actual spawns, allowing for creature model dimensions / animations and terrain interactions.
- [ ] Ensure markers do not block crosshair raycasts or player movement. Disable, change tab, close HUD, change scene and exit: no remnants.
- [ ] Restart game: marker enable/mode preference should be restored, but objects appear only in an active Creatures HUD.

## Admin Corner
- [ ] Infinite health/stamina/eitr and flight/building remain functional.
- [ ] Infinite adrenaline should only be enabled if API hook is reported available. Use a trinket and watch for no decay and refill toward 80% of maximum; disable and ensure normal decay returns.
- [ ] With the host in a test world, test NPC attacks x1, x2, x5, x10, x25. Ensure damage is **not** boosted against a second player, tamed allies or yourself. Disable and verify regular values.
- [ ] Verify super damage is disabled if RPC patch is unavailable or you are not the host; effects should not leak to other players.
- [ ] Repair all, Fill stacks, skill XP multiplier, inventory catalogue, Kits and custom names/descriptions still work.

## Data protection
- [ ] Change EN/RU, marker mode, marker toggle. Restart game and verify settings.
- [ ] Save/duplicate/edit a Kit, snapshot Radar, add History, restart and verify the old Format 2 files load unchanged.
- [ ] Confirm admin power toggles reset on restart by design; marker preference persists.

**Important:** Source inspection and lexical tests do not prove a successful C# build or behavior in live Valheim. Report all game test results explicitly.
