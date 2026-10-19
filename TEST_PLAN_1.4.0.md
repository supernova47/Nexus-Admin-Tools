# v1.4.0 manual acceptance plan

Use a test world and backed-up character; keep the tested v1.3.0 DLL available for rollback.

1. **Build and load:** BuildMod.bat succeeds with real Valheim + BepInEx references. Startup BepInEx log contains the Nexus plugin at 1.4.0. No Harmony patch errors on loading. UI F6 opens, and previous admin/catalogue tabs still work.
2. **Doctor:** Enter world, refresh catalogues, open Doctor, filter `SwordIron` or any known item. Click Run Diagnostics, copy report, compare renderer/material counts with prefab, equip item manually. Verify no shared textures/materials are modified. Repeat with a modded item and a missing item (after disabling that content mod).
3. **Custom name:** With an **unstackable** item, set custom name, spawn 1, verify game item, toolbar/tooltip/hover labels where available, re-enter world and verify custom metadata persistence. Verify empty name gives original game name. Verify named stackable item is blocked. Test RU/EN and special characters; ensure no Unity rich text injection. Verify a previously created unmodified item retains its original name.
4. **Kits:** Create kit; add two items; test custom name on an unstackable item. Restart game, verify kit remains. Apply in empty inventory, verify exact amounts/quality/durability. Fill inventory and verify partial insertion message. Test missing Prefab after disabling a content mod; kit must remain but abort before insertion. Test with exactly 50 items, and with >50 total units (must block).
5. **History:** Create item; spawn a creature as host; apply a kit. Inspect entries, restart game. Press Reuse on an item/creature and verify the form fills but no automatic object is created. Clear history and restart to confirm persistence.
6. **Spawn Preview:** Creature tab; preview count, level, custom name. Verify preview creates no object. Confirm host restriction and warnings for bosses/large counts. Verify actual spawning still requires the original Spawn button.
7. **Radar:** Save baseline, compare with unchanged catalogues (expect zero changes). Disable/enable a content mod in a disposable profile and compare before replacing snapshot. New or missing IDs must be reported. Compare with no baseline should display an explanatory message.
8. **Regression:** Check favorites, RU/EN search, icon drawing, kill radius/tamed protection, input focus, default quantity 1, 9999 validation, and equipment rendering for a representative variety of items.
9. **Multiplayer:** For item names, verify behavior with another client both with and without the Nexus plugin, and with custom item mods. Do not advertise full multiplayer visual synchronization before this passes.
10. **Save safety:** Check that the original v1.3.0 DLL can replace 1.4.0 without altering built-in items or corrupting the test character/world.


### buildfix3: Custom item names
- [ ] Check startup log names all three display hooks (`InventoryGrid.CreateItemTooltip`, `ItemDrop.GetHoverText`, `ItemDrop.GetHoverName`).
- [ ] Spawn a stack-1 item with a nonempty Custom Name; its inventory tooltip **title** is custom, body is vanilla.
- [ ] Drop the item. Pickup prompt first line is custom, the interaction key remains.
- [ ] Pick it up and restart the game: custom title persists.
- [ ] Spawn the same prefab without a name; ordinary instance retains the vanilla title.
- [ ] Add a named item via Equipment Kit and repeat the inventory/drop checks.
- [ ] Confirm no renamed values propagate into other instances of the same prefab.
- [ ] Check BepInEx log shows `Custom item name persisted in inventory: True` after named insertion.


## buildfix4 — restart and custom description regression

- [ ] Set a unique item custom description, verify vanilla description is replaced but durability/damage/stats remain.
- [ ] Custom name + description appear together in inventory; item picked from ground retains both after game save/restart.
- [ ] Add named/described item to Kit, restart, verify both fields remain and the kit can be applied.
- [ ] History item reuse restores both name and description after restart.
- [ ] Radar snapshot survives game restart and mod update in the same r2modman profile.
- [ ] Favorite item and creature, change language, restart; verify values are retained.
- [ ] Check `SuperNova.ValheimNexus.Advanced.json` and `.bak` in BepInEx/config.
- [ ] Upgrade from a prior 1.4.0 build with old `[NexusAdvanced] Data`, verify migration and legacy value preserved.
- [ ] For saved-profile copy ONLY, corrupt main JSON and confirm backup recovery and `.corrupt` preservation.
