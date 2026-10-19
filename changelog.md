# Changelog

## 1.8.7 — October 19, 2026

- Rendered the cursor-held item above the Nexus HUD while keeping the trash drop target reachable.
- Made trash removal immediate after the recent server-issued admin grant; the audit entry is sent asynchronously so repeated drops are not delayed by a round trip.
- Kept network protocol 185 unchanged.

## 1.8.6 — October 19, 2026

- Made the Nexus HUD and Valheim inventory independent. With both open, the inventory remains interactive and the HUD can be dragged above it; Tab opens the inventory and Escape closes it before a second Escape closes the HUD.
- Kept the HUD toggle key and close button available while the inventory is open.
- Kept the return-point notice above other windows, enabled its buttons for the affected player without requiring admin access, and remove the saved point after the server confirms dismissal.
- Preserved the network protocol because this update changes local UI input and ordering only.

## 1.8.5 — October 19, 2026

- The native inventory can now be opened and closed while the Nexus HUD is active. The inventory shortcut and Escape key preserve the HUD, so players can reposition the window and drag items into the trash.
- Added a close control to the administrator return-point notification. Closing it dismisses and removes that player's saved return point on the server.
- Fixed dropped-item cleanup on dedicated servers by transferring each matching network object to the server before queuing its replicated deletion. The HUD now reports the number queued instead of claiming that every item has already disappeared.
- Bumped the network protocol to 185. Install Nexus Admin Tools 1.8.5 on the server and every client.

## 1.8.4 — October 19, 2026

- Added a personal maximum carry weight setting. Its value and enabled state are saved per world and character and restored only after administrator access is confirmed.
- Added an **Admin → Players** page with an online player list, teleport-to-player and bring-player actions, kick, permanent bans, timed bans in hours, unban, and freeze/unfreeze.
- When an administrator brings a player to them, the server saves that player's previous position and shows a return prompt with an administrator notification. Teleporting to a player or a world location also keeps the administrator's return action available.
- Timed bans use Valheim's native ban list and are automatically removed by the server after their configured expiry. Frozen movement ends when an administrator unfreezes the player or the player disconnects.
- Added **Admin → World → Clear dropped items**. The server removes dropped item entities across its world while leaving inventory and container contents intact.
- Tightened server command delivery and malformed-expiry handling for player moderation, and reset temporary moderation and return states when the active world changes.
- Added the MIT license to the source and Thunderstore package. README and changelog content is in English.
- Bumped the network protocol to 184. Install Nexus Admin Tools 1.8.4 on the server and every client.

## 1.8.3 — October 19, 2026

- Added a one-slot undo trash area to **Admin → Inventory**. It accepts a whole stack from the player's inventory; discarding another stack replaces the saved item.
- Added a restore button. Restored items keep their item data and custom names; when no safe inventory space is available, the item remains in the trash slot.
- Persisted the undo item on the administrator's client per world and character, so it remains available after restarting the game.
- Connected the HUD trash area to Valheim's native inventory discard action. The native discard button routes to the undo slot only while the trash page is open.
- Restricted the feature to the local administrator's own inventory. Container items are not accepted; remote clients must receive the server's one-use admin approval before the local inventory changes.
- Kept network protocol 181 because the change reuses the existing authenticated local-action request.

## 1.8.2 — October 19, 2026

- Renamed the user-facing mod to Nexus Admin Tools in the HUD, mod list, documentation, and packages. The plugin GUID and DLL path remain unchanged so existing installations can be updated in place.
- Added world-shared teleport points at an administrator's current position, Nexus-only location-name overrides, and deletion for custom points. Original in-world location names are unchanged; the server stores these settings per world.
- The server authenticates point creation, renaming, and deletion as admin commands and records them in the shared audit log. It validates the location key and coordinates again before teleporting.
- Bumped the network protocol to 181. Install Nexus Admin Tools 1.8.2 on the server and every client.

## 1.8.1 — October 19, 2026

- Fixed weather selection so each arrow press uses the server's current environment. The full weather list can now be traversed in either direction and returned to dynamic weather.
- Fixed the unique-location list to include coordinates from all generated zones, including zones the player has not loaded or visited. The previous `m_placed` filter hid these locations.
- Locations without a localized name now display their prefab name, and an invalid name no longer aborts loading the page. The HUD also reports an empty result separately.
- Added server and client log diagnostics for the number of locations found and sent.
- Kept network protocol 180. Install Nexus Admin Tools 1.8.1 on the server and all clients to receive both fixes.

## 1.8.0 — October 19, 2026

- Added the Locations and Log admin pages, weather selection, and raid controls to the large HUD. The location list includes all generated unique locations, even those the player has not discovered; teleport saves the starting position and provides a return action.
- Weather and raid settings are applied by the server, saved per world, and synchronized to clients. Disabling raids ends an active raid and blocks future raids until re-enabled.
- Added a shared server command log with the administrator, time, world day, command, arguments, and result; the log is also available in the HUD.
- Added infinite cooldown for Forsaken powers. An administrator can use one power, switch to another, and use it immediately; power duration is unchanged.
- Fixed reading Valheim Namekeeper renames when saving inventory into a kit, including subsequent renames and resetting an item's name to its default.
- Bumped the network protocol to 180. Install the same 1.8.0 version on the server and all clients.
- Preserved the 1.7.0 structure, wide movable HUD, 1,000-item kit application limit, admin settings, plugin GUID, and Advanced Format 2.
- The candidate was built and passed automated checks. The game and a live dedicated-server session were not tested.

## 1.7.2 — October 19, 2026

- Restored the large movable window with the wide catalogue above a fixed parameter area. Admin controls no longer require scrolling; reports and history use pages.
- Increased scrolling speed and normalized fractional wheel input. Catalogue selection now preserves scroll position, and duplicate button sound handlers were removed.
- Removed the “Kill selected type” command. On dedicated servers, creature removal validates ZDO IDs on the server and sends the command to current owners.
- Skills can be set directly in either direction without extra scrolling or confirmation. Time changes, skill changes, and cleanup no longer ask for redundant confirmation. F7/F8 work for creatures selected through global search.
- Fixed the kit editor and added quantity, quality, name, description, copy, and JSON controls. The user-selected 1,000-item application limit remains.
- Preserved settings, Format 2 data, and plugin GUID. Network protocol 172 requires the same mod version on the server and every client.

## 1.7.1 — Movable workspace and admin tools

- Enlarged the main window, added drag-to-move support on the title bar, and saved the window position.
- Added global catalogue search across creatures and every item category.
- Added numeric quality and creature-star input with the current maximum shown.
- Time and skills can be set in one action; skills can be raised or lowered.
- Added a nearby selected-creature-type removal command and fixed server-side cleanup handling.
- Moved equipment kits into the main window and added saving inventory together with equipped items. Increased the kit limit to 100 entries.
- Replaced repeated standard UI sounds with a single soft button sound and increased scroll sensitivity.
- Added network protocol 171. The server and every client must use the same version.
- The candidate compiled; in-game and live multiplayer verification were not performed.

## 1.7.0 — Native admin panel and authenticated multiplayer

- Added a compact native Valheim/Jötunn HUD, searchable paged catalogues, and a separate detailed workbench.
- Added the server's survival day and solar clock, forward-only HH:MM time changes with confirmation, and world- and character-scoped admin preferences.
- Added network-aware creature invisibility, selected-skill controls, set-all-skills-to-100, and self-healing.
- Replaced host-only and unguarded command paths with authenticated server admin checks, local single-use approvals, and server-side world actions.
- Added request size, rate, and replay guards; disconnect and permission-revocation cleanup; strict client/server compatibility; and audit/test documentation.
- Reused stable per-item metadata and Advanced Format 2 persistence. Existing favorites, kits, history, and radar data identifiers remain unchanged.
- Compiled a development candidate. Live multiplayer and native in-game layout verification remained pending.

## 1.6.0 — Build Fix 1

- Added the `UnityEngine.TextRenderingModule.dll` reference required for `GUIStyle.fontStyle` and `FontStyle.Bold` in `src/NexusHudAdvanced.cs` (CS0012/CS0103).
- Added a preflight check for the Unity text-rendering module to `BuildMod.ps1`.
- No gameplay or HUD behavior, plugin GUID, data serialization, or persistent configuration changed.
- This is a source-only package and requires compilation on a machine with the Valheim assemblies and .NET SDK.

## 1.6.0 — HUD and Admin Upgrade (development candidate)

- Reorganized navigation into a utility toolbar, primary catalogue tabs, and a separate Nexus Advanced row.
- Added collapsible destructive creature tools, expandable item metadata and descriptions, and concise Admin Corner sections.
- Added opt-in infinite adrenaline with a reflective API availability check. It suppresses negative gain and refills the local player to an 80% reserve.
- Added x1/x2/x5/x10/x25 super-damage settings through available `Character.RPC_Damage` calls, with attacker identification. It excludes local/self/player/tamed targets and requires host authority.
- Added local-only, collider-free cyan 3D preview markers, limited to 12 instances. Preview and actual spawn use the same base-position helper.
- Saved marker mode and enabled state in BepInEx config. Format 2 JSON, plugin GUID, and DLC access remain unchanged.
- This is a source-only candidate; Unity/game compilation and integration testing require Valheim managed assemblies.

## 1.5.0 — Kit Workshop (development candidate)

- Added explicit Save Item, Rename, and Duplicate actions to the kit editor.
- Added single-kit JSON clipboard export/import with validation and non-destructive duplicate naming.
- Added on-demand disk-persistence verification and history search/type filters.
- Preserved the existing Format 2 advanced-state file, favorites, language setting, and plugin GUID.
- No DLC unlock logic was added.

## 1.4.0 — Build Fix 5 (Nexus Advanced storage)

- Replaced Unity `JsonUtility` for Kits, History, and Radar with an explicit versioned JSON codec (`Format:2`). In the affected Unity 6000 environment, earlier state files contained only `Format` and `SnapshotUtc` and omitted all three collections.
- Writes all three arrays explicitly, including empty arrays, and rejects malformed or truncated version-2 data.
- Verifies complete content in memory, in a temporary file, and again after committing the primary file.
- Creates a `.bak` on the first successful save and retains migration support for legacy `Format:1` data.
- Logs the active storage path and saved/loaded record counts. Radar reports success only when its save succeeds.
- Added standalone .NET smoke tests for Unicode, custom descriptions, Kits, History, Radar, legacy input, and malformed files.
- Collection contents previously omitted from an incomplete file cannot be reconstructed from that file alone. Keep copies of older `.cfg` and `.json` files for possible recovery.

## 1.4.0 — Build Fix 4 (persistence and descriptions)

- Added an instance-specific custom item description, separate from the custom name. Original item stats and prefab shared data are unchanged.
- Kits and History now preserve each item's custom description.
- Moved Kits, History, and Radar snapshots to a dedicated JSON file in the active BepInEx config directory.
- Migrates existing `[NexusAdvanced] Data` from the legacy `.cfg` without removing the original.
- Writes through a temporary file, validates serialization, maintains a `.bak`, recovers unreadable primary JSON when a backup exists, and blocks unsafe writes.
- Favorites and language changes now explicitly call `Config.Save()`.
- Added restart, profile, backup, and update test instructions in `SAVE_DATA.md`.

## 1.4.0 — Build Fix 3 (custom-name display)

- Hooked `InventoryGrid.CreateItemTooltip` to set the displayed title for named item instances while preserving the original description.
- Hooked `ItemDrop.GetHoverText` to update the first line of the world pickup prompt and retained `GetHoverName`.
- Removed ineffective `ItemData.GetTooltip` title rewriting; that method supplies the description rather than the inventory heading.
- Added a post-insertion diagnostic to check that custom-name data survives inventory insertion, including Equipment Kits.
- Preserved the plugin GUID, plugin version, prefab metadata, stable base, and all prior features.

## 1.4.0 — Build Fix 2

- Expanded the Nexus Doctor panel downward to fit longer reports.
- Routed custom item names through the displayed item-title path rather than the description body.
- Preserved per-instance custom-name storage in `ItemData.m_customData` and kit serialization.

## 1.4.0 — Nexus Advanced (development candidate)

- Added Nexus Doctor prefab inspection and clipboard reports.
- Added persistent Equipment Kits and bounded, verified kit insertion.
- Added an operation history with explicit form-prefill reuse.
- Added informational creature Spawn Preview.
- Added persistent catalogue snapshots and Compatibility Radar diffs.
- Added experimental custom item names stored on individual `ItemData` through `m_customData`, with tooltip and hover integration limited to unstackable items.
- Retained the 1.3.0 GUID, catalogues, favorites, admin tools, and safe inventory insertion.
- Build Fix 1 (October 16, 2026) added the missing `UnityEngine.JSONSerializeModule.dll` reference required by `JsonUtility` in `NexusAdvancedData.cs` (CS0103 at lines 59 and 80), plus a `BuildMod.ps1` preflight check and regression guard. The plugin version and functionality did not change.
- No runtime DLL build or Valheim integration test was performed in the authoring environment.

## 1.3.0 — Catalogue and inventory UX

- Pinned the 1.2.1 source package as the project baseline while retaining the original plugin GUID and existing player, admin, and inventory commands.
- Added independent persistent favorites for item and creature prefabs, bilingual `☆`/`★` controls, and favorites-only filters. Favorites remain in config if external mods are disabled.
- Added search across all five item categories. Empty global queries do not render the entire ObjectDB unless favorites-only is enabled.
- Added manual creature/item catalogue reload while preserving selection by prefab ID when possible.
- Added cached item-sprite UVs without runtime icon-texture allocations and safe placeholders for unsupported atlas layouts.
- Added selected-item diagnostics for category, type, max stack, selectable max quality, durability, and drawable-icon status.
- Added dependency-free C# favorites serialization tests, extended Python source-level regression checks, and a 1.3.0 in-game test plan. These checks were not run at the time.
- Preserved 1.2.1 kill-radius and confirmation behavior, checked inventory insertion limits, skill/admin controls, and RU/EN category assignments.
- Release candidate only; no DLL was compiled and no in-game verification was performed.

## 1.2.1 — Stability and safety

- Limited `killall` to a configurable 5–100 m radius (30 m by default), excluding players and, by default, tamed creatures and bosses. Confirmation previews a frozen set of candidate targets.
- Routed all item categories through checked `ItemData` insertion, preserving `m_dropPrefab`, shared properties, and full quality-scaled durability when available.
- Each insertion measures inventory quantity before and after, reports actual progress, and leaves any unfulfilled remainder selected. Missing game APIs fail closed rather than reporting false success.
- Respects stack size from `m_maxStackSize` and caps per-click work at 100 individual items or 200 stack operations to reduce stalls.
- Validates item quantity (1–9999) when inserting. Invalid text disables Add instead of reusing a stale number.
- Supports Russian aliases and item-type search regardless of the active game language.
- Persists HUD language; English remains the default. Creature count from config is no longer overwritten at startup.
- Uses a clean temporary staging directory for packaging; manifest and source version are 1.2.1.
- This source-only release was not compiled or tested in Valheim in the provided environment.

## 1.0.1 — Item amount reset

- Resets amount to 1 after successful inventory addition for food, body armor, materials, and other items.
- Resets amount to 1 when switching item-category tabs to avoid carrying a bulk amount into another category.
- Weapon and shield insertion logic and quality limits are unchanged.

## 0.3.6 — Body armor inventory fixes

- Adds body armor (helmets, chest armor, leg armor, gloves, and capes) as cloned `ItemData` with an explicit `m_dropPrefab` reference so equipped visuals can resolve the correct prefab.
- Initializes body armor durability to the quality-scaled maximum using the game `ItemData` method where available, with a shared-data fallback.
- Existing weapon and shield item paths are unchanged.

## 0.3.5 — Weapons tab and wiki shortcut

- Restores the Weapons item tab, including its Russian localization.
- Stops filtering weapon and ammunition prefabs out of the runtime ObjectDB catalogue; weapon types are classified separately from Armor, Food, and Materials.
- Adds a Valheim Wiki button that opens the official community wiki in the default browser.
- Keeps English as the startup language and the initial creature/item count at 1.

## 0.3.4 — English startup and safe English item labels

- Starts the HUD in English on every launch, regardless of an older `DisplayLanguage` setting. The RU/EN button still changes the current session.
- Initializes creature count and item amount to 1 on each launch.
- In English mode, uses English item localization when available and falls back to a readable English-style prefab identifier if the active game localization returns Cyrillic.
- Sanitizes item-type labels containing unexpected Cyrillic strings in English mode.
- Updates build output, manifest, README, and build status to 0.3.4.

## 0.3.3 — Trinket classification

- Moves ItemType `Trinket`/`Trinkets` into the Armor tab.
- Adds `Trinkets` as the English type-filter label and its localized Russian display label.

## 0.3.2 — Item quantity, filters, and food handling

- Adds direct numeric amount input alongside +/- controls in the remaining item tabs.
- Adds dynamic type filters for Armor, Food, Materials, and Other Items.
- Requires actual food health values for food catalogue classification, avoiding mislabeling meads and potions as meals.
- Clones food as `ItemData` and assigns an explicit `m_dropPrefab` before inventory insertion to support normal consumption.

## 0.3.1 — Unified item catalogue

- Retains runtime creature discovery and spawn controls.
- Adds runtime item discovery and bilingual cataloguing.

## 0.3.0 — Bilingual item catalogue

- Renames the plugin to ValheimSpawner.
- Adds a single RU/EN interface with automatic language detection and an in-HUD language switch.
- Adds runtime item/creature discovery, filters, amount/quality controls, and host/server-only item insertion.
