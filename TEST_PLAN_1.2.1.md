# v1.2.1 — manual acceptance tests (Valheim required)

Record actual Valheim, BepInEx and Jötunn versions before starting. Back up the world and character.

1. **Build:** run `BuildMod.ps1`, confirm exit code zero, DLL in output and that the installable archive contains only manifest, readme, icon and `BepInEx/plugins/ValheimSpawner/ValheimSpawner.dll`.
2. **Load:** open F6 in a disposable world, inspect `BepInEx/LogOutput.log` for plugin load and errors.
3. **Inventory — simple:** create a material with quantity 1, stack maximum, maximum + 1, then large quantity (9999). Compare actual added units to HUD receipt. No stack may exceed `m_maxStackSize`.
4. **Inventory — full:** create food while inventory is empty, nearly full, and full. Reject or partially insert safely; the remaining amount field must match unfulfilled quantity. Verify food is consumable after creation.
5. **Equipment:** create and equip sword, shield, bow, chest, helmet, cape, accessory and trinket at quality 1 and native max, and optionally quality 10 in a test save. Check equipped visuals, attachment points, and full durability.
6. **Text validation:** enter blank, alphabetic, negative, zero, 10000 and `1e3` amounts. Add is disabled; previous valid quantity must not be reused. Test + / − changes.
7. **Language/search:** EN HUD and RU HUD with EN and RU game language, find mapped weapons by both names and category aliases. Restart after switching to RU to verify persistence.
8. **Kill nearby:** in a test world place hostile creature inside/outside 30 m, and tamed creature and boss inside radius. Open preview, confirm; only eligible previewed nearby creatures may be affected. Repeat with intentional opt-in toggles. Never test in a valuable world.
9. **Kill confirmation race:** open confirmation, spawn a new creature after preview; confirming must not target the new creature. Cancel/timeout should clear preview.
10. **Permissions:** test single-player host, remote client, dedicated server (UI unavailable on headless). Unprivileged clients must not trigger spawning/kill operations.
11. **Regression:** F6/F7/F8, item tabs, Armor trinkets and shields, creature catalogue/filter, stamina/health/eitr, repair, fill stacks, fly/build toggles, XP multiplier. Check console and other HUD mods.

**Status:** All of the above **require manual verification** in Valheim. Static source checks are not a substitute.
