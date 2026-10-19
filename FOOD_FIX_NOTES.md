# Food item fix notes — ValheimSpawner 0.3.2

The previous generic item insertion path attempted several `Inventory.AddItem` overloads through reflection. The food path now clones the prefab's `ItemDrop.ItemData`, explicitly assigns the source prefab to `m_dropPrefab`, preserves the prefab's shared food data, sets the requested stack and quality, and calls a compatible `Inventory.AddItem(ItemData)` overload.

This follows the essential Valheim item-data relationship: `m_dropPrefab` identifies the actual item prefab when the item is later used or saved. If food still cannot be consumed after this change, attach `BepInEx/LogOutput.log` and note one example food prefab ID. The package is source-only; the local game assembly version may expose different overloads.
