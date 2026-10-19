# Nexus Admin Tools

**Version:** 1.8.7

Nexus Admin Tools is a movable Valheim administration workspace with a native large-window interface. It provides server-authorized world controls, saved teleports, item and creature catalogs, player tools, and equipment kits.

Source code and releases: [GitHub](https://github.com/supernova47/Nexus-Admin-Tools). Report bugs or request features on [GitHub Issues](https://github.com/supernova47/Nexus-Admin-Tools/issues). Share reviews, questions, and comments on [GitHub Discussions](https://github.com/supernova47/Nexus-Admin-Tools/discussions).

## Installation

1. Install Nexus Admin Tools from Thunderstore. The package manifest declares BepInExPack Valheim and Jötunn as dependencies, so a compatible mod manager installs them automatically. For local testing, import `Nexus-Admin-Tools-Thunderstore-1.8.7.zip` into your Valheim profile.
2. Install the same Nexus Admin Tools version on the dedicated server and every client.
3. Add administrators to the server's Valheim admin list. The server checks permissions for every world action.
4. Press **F6** to open or close the window. Drag the title bar to move it; the window position is saved. Use **RU / EN** to change the interface language.

## Features

- **Admin → World:** view the survival day and world time, set the time, force an environment or restore dynamic weather, and stop the current raid while disabling future raids. Weather and raid settings are saved per world on the server.
- **Admin → Locations:** browse generated unique locations across the world, including locations in zones that have not been visited. Teleport to a location and return to the saved starting point.
- Add a Nexus teleport point at your current position. Teleport points and Nexus-only display-name overrides are shared by the world. Rename generated and user-created points; delete user-created points. Generated names in the game world are not changed. Leaving a generated location's Nexus name empty restores its default display name.
- **Admin → Log:** view the shared server administrator log. Entries include UTC time, world day, administrator name and UID, action details, and result. The server stores the log at `BepInEx/config/SuperNova.ValheimNexus.AdminAudit.log` and rotates it when it exceeds 1 MiB.
- **Admin → Player:** restore resources, fly, build without costs, hide from creatures, increase damage, and use hotkeys. Infinite Forsaken power cooldown lets you use one power, switch powers, and use another immediately; active power duration remains unchanged.
- Set your personal maximum carry weight. The setting is saved for the current world and character and applies only while admin access is available.
- **Admin → Players:** teleport to an online player or bring that player to you. The server saves the moved player's previous position and gives them a return action with a notification. Kick players, issue permanent or timed bans in hours, unban entries, and freeze movement until unfrozen or the player disconnects.
- **Admin → World:** clear dropped item entities from the ground across the server world. Items inside inventories and containers are not removed.
- **Admin → Skills:** set an individual skill to any level from 0 to 100, including lowering it, or set every skill to 100. No extra confirmation is required.
- **Admin → Inventory:** restore health, repair items, fill stacks, and kill nearby creatures. The trash slot accepts a full stack from your personal inventory and keeps one item for undo; the next discarded stack replaces it. The undo item is saved on your client per world and character, including across game restarts. Use the HUD slot or Valheim's discard button while this page is open. Items from containers are not accepted, and an item stays in the slot if your inventory has no free space when you restore it. On a dedicated server, admin permission is checked before inventory changes. The server validates creature cleanup targets and sends approved instructions to their current owners. Players are always protected; bosses and tamed creatures can be included separately.
- **Equipment kits:** save the current inventory and equipped items. Captured item names follow the effective Valheim Namekeeper name, including later renames and resets to the default name. Applying a kit is limited to 1,000 total items.
- Only item, creature, and kit-item lists use scrolling. Administrative commands and settings fit in the selected workspace page.

Local player settings are scoped to the world and character and are restored only after permissions are checked. Shared weather, raid, teleport-point, display-name, and moderation data are stored by the server. World actions are authorized and executed by the server; install the same release on the dedicated server and every client.

Equipment kits continue to use the existing Format 2 data format, and the plugin GUID remains compatible with earlier Nexus releases.

Nexus Advanced v1.4.0 continues to store equipment kits, history, and radar snapshots in its existing data format.

See the full [changelog](https://github.com/supernova47/Nexus-Admin-Tools/blob/main/changelog.md) for release history.

The Thunderstore package is a ZIP with `manifest.json`, `README.md`, `changelog.md`, `LICENSE`, and a 256×256 `icon.png` at its root. The plugin DLL is installed under `BepInEx/plugins/ValheimSpawner`.

## License

Nexus Admin Tools is distributed under the [MIT License](LICENSE).
