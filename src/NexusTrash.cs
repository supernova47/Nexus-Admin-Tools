using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private const string TrashSaveHeader = "NEXUS-ADMIN-TRASH-1";
        private const int MaxTrashSaveBytes = 512 * 1024;
        private ItemDrop.ItemData _trashedItem;
        private Vector2i _trashOriginalPosition;
        private string _trashScope;
        private string _trashLoadedScope;
        private Button _trashDropTarget, _trashRestoreButton;
        private Image _trashItemIcon;
        private Text _trashItemLabel;

        private bool TrashPageActive => _isOpen && !_legacyToolsOpen && _nativeMode == 1 && _adminSection == 3;

        private void InstallInventoryTrashPatch()
        {
            if (_harmony == null) return;
            Type guiType = FindType("InventoryGui");
            MethodInfo dropMethod = guiType == null ? null : AccessTools.Method(guiType, "OnDropOutside");
            MethodInfo prefix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(InventoryDropButtonPrefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (dropMethod == null || prefix == null)
            {
                Logger.LogWarning("Inventory trash integration unavailable: InventoryGui.OnDropOutside was not found.");
                return;
            }
            try
            {
                _harmony.Patch(dropMethod, prefix: new HarmonyMethod(prefix));
                Logger.LogInfo("Inventory trash integration installed.");
            }
            catch (Exception ex) { Logger.LogWarning("Inventory trash integration failed: " + ex.Message); }

            MethodInfo dragSetup = AccessTools.Method(guiType, "SetupDragItem",
                new[] { typeof(ItemDrop.ItemData), typeof(Inventory), typeof(int) });
            MethodInfo dragPostfix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(InventoryDragSetupPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (dragSetup != null && dragPostfix != null)
            {
                try
                {
                    _harmony.Patch(dragSetup, postfix: new HarmonyMethod(dragPostfix));
                    Logger.LogInfo("Inventory drag icon layering installed.");
                }
                catch (Exception ex) { Logger.LogWarning("Could not raise inventory drag icon above Nexus HUD: " + ex.Message); }
            }
            else Logger.LogWarning("Inventory drag icon layering unavailable: InventoryGui.SetupDragItem was not found.");
        }

        private static void InventoryDragSetupPostfix(object __instance, object[] __args)
        {
            ValheimSpawnerPlugin mod = _activeInstance;
            if (mod == null || !mod.TrashPageActive || !mod.HasAdminAccess || !mod.NativeInventoryIsVisible() ||
                __args == null || __args.Length == 0 || __args[0] == null || mod._nativeCanvas == null) return;
            GameObject dragObject = ReadMember(__instance, "m_dragGo") as GameObject;
            if (dragObject == null) return;

            // Valheim parents its cursor item to the lower inventory canvas. Raise only
            // that visual, leaving the inventory and trash target's raycasters ordered normally.
            Canvas dragCanvas = dragObject.GetComponent<Canvas>();
            if (dragCanvas == null) dragCanvas = dragObject.AddComponent<Canvas>();
            dragCanvas.overrideSorting = true;
            dragCanvas.sortingLayerID = mod._nativeCanvas.sortingLayerID;
            dragCanvas.sortingOrder = mod._nativeCanvas.sortingOrder + 1;
        }

        private static bool InventoryDropButtonPrefix(object __instance)
        {
            ValheimSpawnerPlugin mod = _activeInstance;
            if (mod == null || !mod.TrashPageActive || !mod.HasAdminAccess) return true;

            object source = ReadMember(__instance, "m_dragInventory");
            if (source == null || !ReferenceEquals(source, mod.GetPlayerInventory())) return true;

            // While the trash panel is open, Valheim's native discard button feeds the
            // same single-slot undo action instead of dropping the stack into the world.
            try { mod.TryTrashDraggedItem(__instance, "native discard button"); }
            catch (Exception ex)
            {
                mod.Logger.LogWarning("Could not move the dragged item to the trash: " + ex.Message);
                mod._status = mod.T("The item stayed in your inventory.", "Предмет остался в инвентаре.");
            }
            return false;
        }

        private void BuildTrashControls(Transform parent, ref float y)
        {
            HudSection(parent, T("TRASH CAN", "КОРЗИНА"), ref y);
            var hint = HudLabel(parent, T("Drop a full stack from your own inventory here. The next item replaces it.",
                "Перетащите сюда весь стак из своего инвентаря. Следующий предмет заменит этот."), 14);
            HudPlace(hint, 0, y, 434, 42); y += 46;

            _trashDropTarget = HudButton(parent, "", 0, y, 434, 104, () => { });
            _trashDropTarget.gameObject.name = "Nexus_TrashDropTarget";
            Text label = _trashDropTarget.GetComponentInChildren<Text>();
            label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleLeft;
            label.fontSize = 15;
            HudPlace(label, 106, 0, 314, 104);
            _trashItemLabel = label;

            var iconObject = new GameObject("Nexus_TrashItemIcon", typeof(RectTransform), typeof(Image));
            iconObject.layer = GUIManager.UILayer;
            iconObject.transform.SetParent(_trashDropTarget.transform, false);
            _trashItemIcon = iconObject.GetComponent<Image>();
            _trashItemIcon.raycastTarget = false;
            _trashItemIcon.preserveAspect = true;
            HudPlace(_trashItemIcon, 8, 8, 88, 88);

            UIDragHandler dragTarget = _trashDropTarget.GetComponent<UIDragHandler>();
            if (dragTarget == null) dragTarget = _trashDropTarget.gameObject.AddComponent<UIDragHandler>();
            dragTarget.m_onReleasedOn += OnTrashDropTargetReleased;

            y += 112;
            _trashRestoreButton = HudButton(parent, T("Return from trash", "Вернуть из корзины"),
                0, y, 434, 36, RestoreTrashedItem);
            y += 44;
            HudParagraph(parent, T("The held item stays here until you return it or discard another stack.",
                "Предмет хранится здесь, пока вы его не вернёте или не отправите в корзину следующий стак."), ref y, 48);

            _hudDynamic.Add(RefreshTrashControls);
            RefreshTrashControls();
        }

        private void OnTrashDropTargetReleased(UIDragHandler target)
        {
            if (!HasAdminAccess)
            {
                _status = T("Administrator access required.", "Нужны права администратора сервера.");
                return;
            }
            object gui = GetInventoryGuiInstance();
            if (gui == null)
            {
                _status = T("Open your inventory and drag a full stack into the trash slot.",
                    "Откройте инвентарь и перетащите полный стак в корзину.");
                return;
            }
            try { TryTrashDraggedItem(gui, "HUD trash slot"); }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not move the dragged item to the trash: " + ex.Message);
                _status = T("The item stayed in your inventory.", "Предмет остался в инвентаре.");
            }
        }

        private bool TryTrashDraggedItem(object gui, string source)
        {
            ItemDrop.ItemData item = ReadMember(gui, "m_dragItem") as ItemDrop.ItemData;
            Inventory inventory = ReadMember(gui, "m_dragInventory") as Inventory;
            int amount = ReadIntMember(gui, "m_dragAmount", 0);
            Inventory playerInventory = GetPlayerInventory() as Inventory;
            if (item == null || inventory == null)
            {
                _status = T("No item is being dragged.", "Сейчас предмет не перетаскивается.");
                return false;
            }
            if (playerInventory == null || !ReferenceEquals(inventory, playerInventory))
            {
                _status = T("Only items from your own inventory can go into this trash slot.",
                    "В корзину можно отправлять только предметы из своего инвентаря.");
                return false;
            }
            if (amount != item.m_stack || amount < 1)
            {
                _status = T("Drag the whole stack into the trash slot.",
                    "Перетащите в корзину весь стак целиком.");
                return false;
            }
            if (!HasAdminAccess)
            {
                _status = T("Administrator access required.", "Нужны права администратора сервера.");
                return false;
            }

            ItemDrop.ItemData expectedItem = item;
            Inventory expectedInventory = inventory;
            int expectedAmount = amount;
            Vector2i originalPosition = item.m_gridPos;
            string prefab = item.m_dropPrefab == null ? item.m_shared?.m_name : item.m_dropPrefab.name;
            string detail = "trash inventory item " + (prefab ?? "item") + " x" + amount;
            if (!HasAdminAccess)
            {
                _status = T("Administrator access required.", "Нужны права администратора сервера.");
                return false;
            }
            if (!MoveDraggedItemToTrash(gui, expectedInventory, expectedItem, expectedAmount, originalPosition)) return false;

            // Trash only changes this administrator's personal inventory. The recent
            // server permission poll authorizes it; audit the action asynchronously so
            // a network round trip cannot interrupt repeated drag-and-drop use.
            RecordLocalAdminAction(detail);
            return true;
        }

        private bool MoveDraggedItemToTrash(object gui, Inventory inventory, ItemDrop.ItemData item, int amount,
            Vector2i originalPosition)
        {
            if (!HasAdminAccess || item == null || inventory == null ||
                !ReferenceEquals(inventory, GetPlayerInventory()) || !inventory.GetAllItems().Contains(item) ||
                item.m_stack != amount || !ReferenceEquals(ReadMember(gui, "m_dragItem"), item) ||
                !ReferenceEquals(ReadMember(gui, "m_dragInventory"), inventory) || ReadIntMember(gui, "m_dragAmount", 0) != amount)
            {
                _status = T("Inventory changed before the item could be moved.",
                    "Инвентарь изменился до переноса предмета.");
                return false;
            }

            string currentScope;
            try { currentScope = AdminScope(); }
            catch
            {
                _status = T("Could not verify the current character and world.", "Не удалось проверить текущий мир и персонажа.");
                return false;
            }

            Player player = Player.m_localPlayer;
            bool wasEquipped;
            try
            {
                wasEquipped = player != null && player.IsItemEquiped(item);
                if (wasEquipped)
                {
                    player.RemoveEquipAction(item);
                    player.UnequipItem(item, false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not unequip the item before trashing it: " + ex.Message);
                _status = T("The item stayed in your inventory.", "Предмет остался в инвентаре.");
                return false;
            }

            if (!ClearInventoryGuiDrag(gui))
            {
                if (wasEquipped && player != null) player.EquipItem(item, false);
                _status = T("Could not safely finish the inventory drag.",
                    "Не удалось безопасно завершить перетаскивание.");
                return false;
            }
            ItemDrop.ItemData previousTrashItem = _trashedItem;
            Vector2i previousTrashPosition = _trashOriginalPosition;
            string previousTrashScope = _trashScope;
            if (!TrySaveTrashItem(item, currentScope))
            {
                if (wasEquipped && player != null) player.EquipItem(item, false);
                _status = T("Could not save the undo item. It stayed in your inventory.",
                    "Не удалось сохранить предмет для возврата. Он остался в инвентаре.");
                return false;
            }
            bool removed;
            try { removed = inventory.RemoveItem(item); }
            catch (Exception ex)
            {
                Logger.LogWarning("Inventory.RemoveItem raised while moving an item to trash: " + ex.Message);
                removed = !inventory.GetAllItems().Contains(item);
            }
            if (!removed)
            {
                bool rolledBack = previousTrashItem == null
                    ? TryDeleteTrashSave(currentScope)
                    : TrySaveTrashItem(previousTrashItem, previousTrashScope ?? currentScope);
                if (!rolledBack) Logger.LogWarning("Could not restore the previous trash save after an inventory removal failure.");
                if (wasEquipped && player != null) player.EquipItem(item, false);
                _status = T("The item stayed in your inventory.", "Предмет остался в инвентаре.");
                return false;
            }

            _trashedItem = item;
            _trashOriginalPosition = originalPosition;
            _trashScope = currentScope;
            _trashLoadedScope = currentScope;
            _status = T("Moved to trash. It can be returned until the next item replaces it.",
                "Предмет перемещён в корзину. Его можно вернуть, пока следующий стак не заменит его.");
            RefreshTrashControls();
            return true;
        }

        private void RestoreTrashedItem()
        {
            ItemDrop.ItemData item = _trashedItem;
            if (item == null)
            {
                _status = T("The trash slot is empty.", "Корзина пуста.");
                return;
            }
            if (!AuthorizeLocal(() => RestoreTrashedItemCore(item, _trashOriginalPosition),
                "restore item from trash " + (item.m_dropPrefab == null ? "item" : item.m_dropPrefab.name) + " x" + item.m_stack)) return;
            RestoreTrashedItemCore(item, _trashOriginalPosition);
        }

        private void RestoreTrashedItemCore(ItemDrop.ItemData item, Vector2i originalPosition)
        {
            Inventory inventory = GetPlayerInventory() as Inventory;
            if (!HasAdminAccess || (!IsServer() && !_executingApprovedLocal) || item == null ||
                !ReferenceEquals(item, _trashedItem) || inventory == null || item.m_stack < 1)
            {
                _status = T("The trash item is no longer available.", "Предмет из корзины больше недоступен.");
                return;
            }

            Vector2i destination = FindEmptyInventoryPosition(inventory, originalPosition);

            bool restored = false;
            if (destination.x >= 0 && destination.y >= 0)
            {
                int stack = item.m_stack;
                // Use the game's positional overload when an empty cell is available.
                // It clones the exact item data into the slot, preserving custom names and descriptions.
                restored = AddItemAtEmptyPosition(inventory, item, stack, destination);
            }
            else
            {
                IDictionary<string, string> customData = ReadMember(item, "m_customData") as IDictionary<string, string>;
                if (customData == null || customData.Count == 0)
                {
                    int stack = item.m_stack;
                    if (inventory.CanAddItem(item, stack)) restored = inventory.AddItem(item);
                }
            }

            if (!restored)
            {
                _status = T("No free inventory slot. The item remains in the trash.",
                    "В инвентаре нет свободного места. Предмет остался в корзине.");
                return;
            }

            _trashedItem = null;
            _trashScope = AdminScope();
            _trashLoadedScope = _trashScope;
            if (TryDeleteTrashSave(_trashScope))
                _status = T("Item returned to your inventory.", "Предмет возвращён в инвентарь.");
            else
                _status = T("Item returned, but its saved undo copy could not be cleared. Check the game log.",
                    "Предмет возвращён, но сохранённую копию корзины удалить не удалось. Проверьте игровой журнал.");
            RefreshTrashControls();
        }

        private void RefreshTrashControls()
        {
            if (_trashDropTarget == null || _trashItemLabel == null || _trashItemIcon == null) return;
            ItemDrop.ItemData item = _trashedItem;
            if (item == null)
            {
                _trashItemIcon.enabled = false;
                _trashItemIcon.sprite = null;
                _trashItemLabel.alignment = TextAnchor.MiddleCenter;
                _trashItemLabel.text = T("Drop a full stack here", "Перетащите сюда весь стак");
            }
            else
            {
                _trashItemIcon.enabled = true;
                _trashItemIcon.sprite = item.GetIcon();
                _trashItemLabel.alignment = TextAnchor.MiddleLeft;
                _trashItemLabel.text = TrashItemDisplayName(item) + "\n×" + item.m_stack + "  ·  " +
                    T("Return until replaced", "Можно вернуть до замены");
            }
            if (_trashRestoreButton != null) _trashRestoreButton.interactable = item != null;
        }

        private string TrashItemDisplayName(ItemDrop.ItemData item)
        {
            string custom = ReadCustomItemName(item);
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            string token = item?.m_shared?.m_name;
            if (string.IsNullOrWhiteSpace(token)) token = item?.m_dropPrefab == null ? "Item" : item.m_dropPrefab.name;
            string localized = Localization.instance == null ? null : Localization.instance.Localize(token);
            return string.IsNullOrWhiteSpace(localized) || localized == token ? token : localized;
        }

        private void TickTrashContext()
        {
            if (Player.m_localPlayer == null || ZNet.instance == null) return;
            string scope;
            try { scope = AdminScope(); } catch { return; }
            if (_trashScope != null && !string.Equals(_trashScope, scope, StringComparison.Ordinal))
                _trashedItem = null;
            _trashScope = scope;
            if (string.Equals(_trashLoadedScope, scope, StringComparison.Ordinal)) return;

            string path, worldId, playerId;
            if (!TryGetTrashSavePath(scope, out path, out worldId, out playerId))
            {
                _trashLoadedScope = scope;
                return;
            }
            if (!File.Exists(path))
            {
                _trashLoadedScope = scope;
                RefreshTrashControls();
                return;
            }
            if (ObjectDB.instance == null) return;

            ItemDrop.ItemData loadedItem;
            Vector2i loadedPosition;
            if (TryLoadTrashItem(path, worldId, playerId, out loadedItem, out loadedPosition))
            {
                _trashedItem = loadedItem;
                _trashOriginalPosition = loadedPosition;
                Logger.LogInfo("Restored the saved trash undo item for the current world and character.");
            }
            _trashLoadedScope = scope;
            RefreshTrashControls();
        }

        private bool TrySaveTrashItem(ItemDrop.ItemData item, string scope)
        {
            if (item == null || item.m_dropPrefab == null)
            {
                Logger.LogWarning("Trash persistence requires an item prefab; the item was not removed.");
                return false;
            }
            string path, worldId, playerId;
            if (!TryGetTrashSavePath(scope, out path, out worldId, out playerId)) return false;
            string temporaryPath = path + ".tmp";
            try
            {
                var package = new ZPackage();
                item.Save(package);
                byte[] itemData = package.GetArray();
                if (itemData == null || itemData.Length < 1 || itemData.Length > MaxTrashSaveBytes) return false;
                string[] lines =
                {
                    TrashSaveHeader,
                    worldId,
                    playerId,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(item.m_dropPrefab.name)),
                    Convert.ToBase64String(itemData)
                };
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporaryPath, string.Join("\n", lines), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not persist the trash undo item: " + ex.Message);
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                return false;
            }
        }

        private bool TryLoadTrashItem(string path, string expectedWorldId, string expectedPlayerId,
            out ItemDrop.ItemData item, out Vector2i originalPosition)
        {
            item = null;
            originalPosition = new Vector2i(-1, -1);
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length < 32 || file.Length > MaxTrashSaveBytes)
                    throw new InvalidDataException("Trash save has an invalid size.");
                string[] lines = File.ReadAllText(path, Encoding.UTF8).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length != 5 || lines[0] != TrashSaveHeader || lines[1] != expectedWorldId || lines[2] != expectedPlayerId)
                    throw new InvalidDataException("Trash save header or owner scope does not match.");
                string prefabName = Encoding.UTF8.GetString(Convert.FromBase64String(lines[3]));
                byte[] itemData = Convert.FromBase64String(lines[4]);
                if (string.IsNullOrWhiteSpace(prefabName) || itemData.Length < 1 || itemData.Length > MaxTrashSaveBytes)
                    throw new InvalidDataException("Trash save item data is invalid.");
                GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                ItemDrop itemDrop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
                if (itemDrop == null || itemDrop.m_itemData == null)
                    throw new InvalidDataException("The saved item prefab is not available in this game profile.");
                ItemDrop.ItemData restored = itemDrop.m_itemData.Clone();
                ItemDrop.ItemData.Load(new ZPackage(itemData), restored, Version.c_ItemDataVersion);
                restored.m_dropPrefab = prefab;
                if (restored.m_shared == null || restored.m_stack < 1)
                    throw new InvalidDataException("The saved item could not be reconstructed.");
                item = restored;
                originalPosition = restored.m_gridPos;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not load the saved trash undo item; the save file was kept: " + ex.Message);
                return false;
            }
        }

        private bool TryDeleteTrashSave(string scope)
        {
            string path, worldId, playerId;
            if (!TryGetTrashSavePath(scope, out path, out worldId, out playerId)) return false;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                string temporaryPath = path + ".tmp";
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                return !File.Exists(path);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not clear the saved trash undo item: " + ex.Message);
                return false;
            }
        }

        private static bool TryGetTrashSavePath(string scope, out string path, out string worldId, out string playerId)
        {
            path = null;
            worldId = null;
            playerId = null;
            string[] parts = (scope ?? string.Empty).Split('.');
            if (parts.Length != 3 || parts[0] != "Admin" || parts[1].Length != 16 || parts[2].Length != 16)
                return false;
            for (int i = 0; i < parts[1].Length; i++)
                if (!Uri.IsHexDigit(parts[1][i]) || !Uri.IsHexDigit(parts[2][i])) return false;
            worldId = parts[1];
            playerId = parts[2];
            path = Path.Combine(Paths.ConfigPath, "NexusAdminTrash", "Trash-" + worldId + "-" + playerId + ".dat");
            return true;
        }

        private static object GetInventoryGuiInstance()
        {
            Type type = FindType("InventoryGui");
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                FieldInfo field = type.GetField("instance", flags);
                if (field != null) return field.GetValue(null);
                PropertyInfo property = type.GetProperty("instance", flags);
                return property == null ? null : property.GetValue(null, null);
            }
            catch { return null; }
        }

        private static bool ClearInventoryGuiDrag(object gui)
        {
            if (gui == null) return false;
            MethodInfo setup = AccessTools.Method(gui.GetType(), "SetupDragItem",
                new[] { typeof(ItemDrop.ItemData), typeof(Inventory), typeof(int) });
            if (setup == null) return false;
            try
            {
                setup.Invoke(gui, new object[] { null, null, 1 });
                MethodInfo update = AccessTools.Method(gui.GetType(), "UpdateCraftingPanel", new[] { typeof(bool) });
                if (update != null) update.Invoke(gui, new object[] { false });
                return true;
            }
            catch { return false; }
        }

        private static Vector2i FindEmptyInventoryPosition(Inventory inventory, Vector2i preferred)
        {
            int inventoryWidth = ReadInventoryDimension(inventory, "GetWidth");
            int inventoryHeight = ReadInventoryDimension(inventory, "GetHeight");
            if (inventoryWidth <= 0 || inventoryHeight <= 0) return new Vector2i(-1, -1);

            if (preferred.x >= 0 && preferred.y >= 0 && preferred.x < inventoryWidth && preferred.y < inventoryHeight &&
                inventory.GetItemAt(preferred.x, preferred.y) == null)
                return preferred;
            for (int y = 0; y < inventoryHeight; y++)
                for (int x = 0; x < inventoryWidth; x++)
                    if (inventory.GetItemAt(x, y) == null) return new Vector2i(x, y);
            return new Vector2i(-1, -1);
        }

        private static int ReadInventoryDimension(Inventory inventory, string methodName)
        {
            MethodInfo method = AccessTools.Method(inventory.GetType(), methodName, Type.EmptyTypes);
            if (method == null) return 0;
            try { return Convert.ToInt32(method.Invoke(inventory, null)); }
            catch { return 0; }
        }

        private static bool AddItemAtEmptyPosition(Inventory inventory, ItemDrop.ItemData item, int amount, Vector2i position)
        {
            MethodInfo add = AccessTools.Method(inventory.GetType(), "AddItem",
                new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });
            if (add == null) return false;
            try { return Convert.ToBoolean(add.Invoke(inventory, new object[] { item, amount, position.x, position.y, false })); }
            catch { return item.m_stack <= 0; }
        }
    }
}
