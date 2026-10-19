# Manual acceptance plan — SuperNova Valheim Nexus 1.3.0

All steps below **require a game test** and are **not yet verified**. Use a disposable world / save backup.

## Installation and regression

1. Build and install 1.3.0 with the same GUID as 1.2.1. Confirm single plugin instance and no repeated Harmony patches in `BepInEx/LogOutput.log`.
2. Open the HUD with F6; navigate every existing tab, including Admin Corner. Verify English and Russian UI and no duplicate buttons after reopening or relogging.
3. Verify 1.2.1 safe `killall`: radius, player exclusion, protection of tamed creatures/bosses, frozen preview and cancellation.
4. Verify quantity validation, stack limits, partial inventory messages, full durability and equipped visuals for representative weapons, shields, armor and trinkets.

## New favorite functionality

5. Star an item and a creature; activate Favorites only separately in each panel. Check they remain visible while other entries disappear.
6. Restart Valheim; both starred entries remain after loading the world. Unstar, restart again and verify they are gone.
7. Star an item from an optional mod, disable that mod, restart; no crash. Re-enable it and verify the star is retained.
8. Star items in different tabs. Verify item favorites show only inside the current tab unless Global search is enabled.
9. RU/EN toggle should not change which prefabs are starred or break Russian alias search.

## Global search

10. Enter a prefab fragment belonging to a *different* category, enable Global search, and verify the matching item appears regardless of current item tab.
11. Turn Global search OFF. Verify only current-tab entries reappear, including item-type filters. Check category names in global results (Armor / Броня etc.).
12. Clear search while Global search is ON and Favorites only is OFF: all-category bulk list should remain hidden with a hint. Activate Favorites only: cross-category favorites should appear even with empty search.
13. Ensure selection is cleared when no longer visible; adding an item from another category works only when selected in global mode.

## Icons, item details and refresh

14. Confirm representative vanilla icons are visible without cropping errors; verify missing/packed modded icons show placeholders without repeated exceptions in log.
15. Select food, stackable material, one-handed item, chest armor and a trinket. Check category, type, maximum stack, selectable quality, and durability indicator against game's actual ItemData.
16. Add a mod that registers items after world load. Press Refresh catalogues; verify newly registered entries are visible, old selected prefabs remain selected if still present, and favorites are unaffected.
17. Press Refresh before ObjectDB/ZNetScene is ready; UI should show an incomplete warning without a crash.

## Performance / input

18. Search a large content set, toggle Favorites only, switch categories and refresh repeatedly. Watch for severe frame stalls and `GUILayout` errors.
19. Test at 1920×1080, 1280×720 and high UI scale. Verify icon rows, filters and bottom Add button remain usable.
20. Type in search and amount fields; keyboard shortcuts should not trigger unintended character actions while the HUD is open.

Release promotion only after a successful local build and a game test of this plan. Report failures with `LogOutput.log` and the prefab ID.
