# Catalogue research references

The mod reads live prefab and item metadata from the active Valheim build. These pages were used as organizational references for the main category tabs; they are not a hard-coded complete item database.

- Creatures: https://valheim.fandom.com/wiki/Creatures
- Armor: https://valheim.fandom.com/wiki/Armor
- Category: Armor: https://valheim.fandom.com/wiki/Category:Armor
- Food: https://valheim.fandom.com/wiki/Food
- Category: Food: https://valheim.fandom.com/wiki/Category:Food
- Materials: https://valheim.fandom.com/wiki/Materials
- Category: Materials: https://valheim.fandom.com/wiki/Category:Materials

## Runtime data

- Creature prefabs: active `ZNetScene` catalogue.
- Item prefabs: active `ObjectDB.m_items` list and attached `ItemDrop` data.
- Display item names are localized through Valheim's `Localization` API when possible; internal prefab IDs remain unchanged.
- Item classification is inferred from `ItemDrop.ItemData.SharedData` type/food metadata. Custom mod items can land in **Other Items** if their fields do not match the known categories.
