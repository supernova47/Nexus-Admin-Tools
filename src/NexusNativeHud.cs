using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private static readonly Vector2 HudTopLeft = new Vector2(0, 1);
        private static readonly Color HudGold = new Color(1f, .78f, .36f);
        private static AudioClip _nativeClickClip;
        private GameObject _nativeHud, _nativeBody;
        private Canvas _nativeCanvas;
        private Text _nativeStatus, _nativeClock;
        private RectTransform _nativeRect;
        private ScrollRect _nativeList;
        private readonly List<Action> _hudDynamic = new List<Action>();
        private bool _hudDirty = true, _listDirty = true, _legacyToolsOpen, _nativeInputBlocked;
        private int _nativeMode, _adminSection, _catalogPage, _catalogTotal, _catalogSelectionKind, _toolsSection = 1;
        private string _globalSearch = string.Empty;
        private bool _globalFavoritesOnly;
        private float _nextHudRefresh;
        private string _nativeListKey;
        private float _catalogScrollPosition = 1f;
        private int _skillPage;
        private int _locationPage, _locationTotal, _locationSelection, _loadedLocationPage = -1;
        private bool _playerReturnAvailable;
        private string _newLocationName = string.Empty, _locationRenameInput = string.Empty;
        private int _auditPage, _auditTotal, _auditSelection, _loadedAuditPage = -1;
        private List<NexusWorldLocation> _adminLocations = new List<NexusWorldLocation>();
        private List<string> _adminAuditPage = new List<string>();
        private bool _returnAvailable;
        private string CatalogueViewKey => _mainTab + ":" + _catalogPage + ":" + _search + ":" + _itemSearch + ":" + _globalSearch +
            ":" + _categoryFilter + ":" + _biomeFilter + ":" + _searchAllItems + ":" + _globalFavoritesOnly + ":" + _creatureFavoritesOnly + ":" + _itemFavoritesOnly;
        private const float HudWidth = 1100, HudHeight = 860, BodyWidth = 1052;

        private bool OtherTextInputActive()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<InputField>()?.isFocused == true) return true;
            return Chat.instance != null && Chat.instance.HasFocus() || global::Console.IsVisible() || TextInput.IsVisible();
        }

        private static bool OtherGuiInputBlocked()
        {
            var requests = typeof(GUIManager).GetField("InputBlockRequests",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return requests?.GetValue(null) is int count && count > 0;
        }

        private void SetNativeHudOpen(bool open)
        {
            if (!open)
            {
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null && _nativeHud != null &&
                    EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_nativeHud.transform))
                    EventSystem.current.SetSelectedGameObject(null);
                if (_nativeHud != null) _nativeHud.SetActive(false);
                _legacyToolsOpen = false;
            }
            if (_nativeInputBlocked != open)
            {
                GUIManager.BlockInput(open);
                _nativeInputBlocked = open;
            }
            _hudDirty = true;
        }

        private void RebuildNativeHud() { _hudDirty = true; _listDirty = true; }

        private bool NativeInventoryIsVisible()
        {
            try { return InventoryGui.IsVisible(); }
            catch (Exception ex)
            {
                Logger.LogDebug("Could not read the native inventory visibility: " + ex.Message);
                return false;
            }
        }

        private bool ShouldAllowHudInventoryInput(MethodBase originalMethod, object[] args)
        {
            if (originalMethod?.DeclaringType == null || originalMethod.DeclaringType.Name != "ZInput") return false;

            if (NativeInventoryIsVisible())
            {
                // Keep the native inventory open until Escape. Its other keyboard,
                // gamepad and mouse queries remain available for selection and dragging.
                if (IsInventoryCloseShortcut(originalMethod, args)) return false;
                return true;
            }

            // Let Valheim open its own inventory when Tab/the controller binding is
            // pressed; all other gameplay input remains blocked by the HUD.
            if (NexusTextEditorFocused() || originalMethod.Name != "GetButtonDown" || args == null || args.Length == 0 ||
                !(args[0] is string openButton)) return false;
            return openButton == "Inventory" || openButton == "JoyInventory" || openButton == "JoyButtonY";
        }

        private static bool IsInventoryCloseShortcut(MethodBase originalMethod, object[] args)
        {
            if (originalMethod?.DeclaringType == null || originalMethod.DeclaringType.Name != "ZInput" ||
                originalMethod.Name != "GetButtonDown" || args == null || args.Length == 0 || !(args[0] is string button)) return false;
            return button == "Inventory" || button == "JoyInventory" || button == "JoyButtonB" ||
                button == "JoyButtonY" || button == "Use";
        }

        private bool NexusTextEditorFocused()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<InputField>()?.isFocused == true) return true;
            return Chat.instance != null && Chat.instance.HasFocus() || global::Console.IsVisible();
        }

        private void HideNativeInventory()
        {
            try
            {
                var inventory = InventoryGui.instance;
                if (inventory != null) inventory.Hide();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not close inventory while Nexus HUD is active: " + ex.Message);
                _status = T("Could not close the inventory.", "Не удалось закрыть инвентарь.");
            }
        }

        private void TickNativeHud()
        {
            TickPlayerReturnPrompt();
            if (_isOpen && (!HasAdminAccess || Player.m_localPlayer == null || Player.m_localPlayer.IsDead())) SetOpen(false);
            if (!_isOpen || GUIManager.IsHeadless()) return;
            if (GUIManager.Instance == null || GUIManager.CustomGUIBack == null) return;
            try
            {
                if (_nativeHud == null) BuildNativeHud();
                _nativeHud.SetActive(!_legacyToolsOpen);
                if (_legacyToolsOpen) return;
                ApplyNativeHudCanvasOrder(NativeInventoryIsVisible());
                _nativeHud.GetComponent<CanvasGroup>().interactable = !_pendingActions.Values.Any(p => p.Kind != AdminAction.Poll);
                var parent = _nativeRect.parent as RectTransform;
                float width = parent != null && parent.rect.width > 1 ? parent.rect.width : Screen.width;
                float height = parent != null && parent.rect.height > 1 ? parent.rect.height : Screen.height;
                float scale = Mathf.Min(1, (width - 24) / HudWidth, (height - 32) / HudHeight);
                _nativeRect.localScale = Vector3.one * Mathf.Max(.25f, scale);
                _nativeRect.anchoredPosition = NexusHudDragHandler.Clamp(_nativeRect, _nativeRect.anchoredPosition);
                if (_hudDirty) BuildNativePage();
                if (_listDirty && _nativeMode == 0) RefreshNativeCatalogue();
                if (Time.unscaledTime >= _nextHudRefresh)
                {
                    _nextHudRefresh = Time.unscaledTime + .25f;
                    _nativeStatus.text = _status;
                    _nativeClock.text = WorldClock();
                    foreach (var refresh in _hudDynamic) refresh();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Native Nexus HUD failed: " + ex);
                SetOpen(false);
                DisposeNativeHud();
                ShowAdminToast(T("Could not open Nexus HUD; check the log.", "Не удалось открыть Nexus; см. журнал."));
            }
        }

        private void BuildNativeHud()
        {
            _nativeHud = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIBack.transform,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, HudWidth, HudHeight, false);
            _nativeHud.name = "SN_Nexus_WorkspaceHUD";
            if (_nativeHud.GetComponent<CanvasGroup>() == null) _nativeHud.AddComponent<CanvasGroup>();
            _nativeCanvas = _nativeHud.GetComponent<Canvas>();
            if (_nativeCanvas == null) _nativeCanvas = _nativeHud.AddComponent<Canvas>();
            if (_nativeHud.GetComponent<GraphicRaycaster>() == null) _nativeHud.AddComponent<GraphicRaycaster>();
            _nativeRect = _nativeHud.GetComponent<RectTransform>();
            _nativeRect.anchorMin = _nativeRect.anchorMax = _nativeRect.pivot = new Vector2(.5f, .5f);
            _nativeRect.anchoredPosition = new Vector2(_hudPositionXConfig?.Value ?? 0f, _hudPositionYConfig?.Value ?? 0f);
            // Let clicks pass through decorative wood textures when the inventory is open
            // behind the movable panel; buttons and the trash drop slot add their own raycasters.
            foreach (var image in _nativeHud.GetComponentsInChildren<Image>(true)) image.raycastTarget = false;
            _hudDirty = true;
        }

        private void BuildNativePage()
        {
            _catalogScrollPosition = _nativeList != null && _nativeListKey == CatalogueViewKey ? _nativeList.verticalNormalizedPosition : 1f;
            _hudDirty = false;
            _hudDynamic.Clear();
            foreach (Transform child in _nativeHud.transform.Cast<Transform>().ToArray())
            {
                // Preserve the wood panel's own decorative hierarchy.
                if (child.name.StartsWith("Nexus_", StringComparison.Ordinal))
                { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            }
            _trashDropTarget = _trashRestoreButton = null;
            _trashItemIcon = null;
            _trashItemLabel = null;
            HudDragSurface(_nativeHud.transform, 24, 12, 790, 40);
            var title = HudLabel(_nativeHud.transform, "NEXUS ADMIN TOOLS", 24, HudGold);
            title.font = GUIManager.Instance.AveriaSerifBold;
            HudPlace(title, 24, 18, 600, 32);
            HudButton(_nativeHud.transform, T("Wiki ↗", "Вики ↗"), 686, 20, 120, 30,
                () => Application.OpenURL("https://valheim.fandom.com/wiki/Valheim_Wiki"));
            HudButton(_nativeHud.transform, _isRussian ? "RU / EN" : "EN / RU", 822, 20, 118, 30, () =>
            {
                _isRussian = !_isRussian; _languageSetting.Value = _isRussian ? "Russian" : "English";
                TrySavePreferences(); RebuildNativeHud();
            });
            HudButton(_nativeHud.transform, T("Close [F6]", "Закрыть [F6]"), 956, 20, 120, 30, () => SetOpen(false));
            _nativeClock = HudLabel(_nativeHud.transform, WorldClock(), 17);
            HudPlace(_nativeClock, 24, 54, 460, 26);
            string[] tabs = { T("Catalogue", "Каталог"), T("Admin", "Админ"), T("Tools", "Инструменты") };
            for (int i = 0; i < tabs.Length; i++)
            {
                int mode = i;
                var button = HudButton(_nativeHud.transform, tabs[i], 24 + 354 * i, 88, 344, 34, () =>
                { _nativeMode = mode; _catalogPage = 0; RebuildNativeHud(); });
                HudHighlight(button, i == _nativeMode);
            }
            _nativeBody = new GameObject("Nexus_Body", typeof(RectTransform));
            _nativeBody.layer = GUIManager.UILayer; _nativeBody.transform.SetParent(_nativeHud.transform, false);
            HudPlace(_nativeBody.GetComponent<RectTransform>(), 24, 138, BodyWidth, 654);
            _nativeStatus = HudLabel(_nativeHud.transform, _status, 15, HudGold);
            HudPlace(_nativeStatus, 24, 810, BodyWidth, 36);
            if (_nativeMode == 0) BuildCataloguePage();
            else if (_nativeMode == 1) BuildAdminPage();
            else BuildToolsPage();
        }

        private void BuildCataloguePage()
        {
            Transform body = _nativeBody.transform;
            string[] categories = { T("Creatures", "Существа"), T("Weapons", "Оружие"), T("Armor", "Броня"),
                T("Food", "Еда"), T("Materials", "Материалы"), T("Other", "Прочее"), T("All", "Все") };
            if (_mainTab < 0 || _mainTab > 6) _mainTab = 0;
            for (int i = 0; i < categories.Length; i++)
            {
                int tab = i;
                var button = HudButton(body, categories[i], i * 151, 0, 146, 32, () =>
                { _mainTab = tab; _catalogPage = 0; _selected = null; _selectedItem = null; _catalogSelectionKind = 0; _itemQuality = 1; ResetItemAmountAfterSuccessfulAdd(); RebuildNativeHud(); });
                HudHighlight(button, i == _mainTab);
            }
            bool creatures = _mainTab == 0;
            bool allCatalogues = _mainTab == 6;
            var filter = HudInput(body, allCatalogues ? _globalSearch : creatures ? _search : _itemSearch, 0, 44, 676, 34, 100, value =>
            {
                if (allCatalogues) _globalSearch = value;
                else if (creatures) _search = value;
                else _itemSearch = value;
                _catalogPage = 0; _listDirty = true;
            });
            if (filter.placeholder is Text placeholder) placeholder.text = allCatalogues
                ? T("Search all catalogues…", "Поиск по всем каталогам…") : T("Search name or prefab…", "Название или prefab…");
            HudButton(body, T("Favorites ", "Избранное ") + (allCatalogues ? (_globalFavoritesOnly ? "★" : "☆") : creatures ? (_creatureFavoritesOnly ? "★" : "☆") : (_itemFavoritesOnly ? "★" : "☆")), 684, 44, 178, 34, () =>
            {
                if (allCatalogues) _globalFavoritesOnly = !_globalFavoritesOnly;
                else if (creatures) _creatureFavoritesOnly = !_creatureFavoritesOnly;
                else _itemFavoritesOnly = !_itemFavoritesOnly;
                _catalogPage = 0; _listDirty = true;
            });
            HudButton(body, T("Refresh catalogues", "Обновить каталоги"), 870, 44, 182, 34, () => { RefreshAvailableCatalogues(); _listDirty = true; });
            if (allCatalogues)
            {
                var hint = HudLabel(body, T("Search includes creatures and items from every category.", "Поиск охватывает существ и предметы всех категорий."), 16);
                HudPlace(hint, 0, 88, BodyWidth, 30);
            }
            else if (creatures)
            {
                string[] kinds = { T("All creatures", "Все существа"), T("Regular", "Обычные"), T("Bosses", "Боссы") };
                HudButton(body, kinds[_categoryFilter], 0, 88, 400, 30, () => { _categoryFilter = (_categoryFilter + 1) % 3; _catalogPage = 0; RebuildNativeHud(); });
                HudDropdown(body, _biomeOptions.Select(b => T(b, TranslateBiome(b))).ToArray(), _biomeFilter, 420, 86, 632,
                    value => { _biomeFilter = value; _catalogPage = 0; _listDirty = true; });
            }
            else
            {
                HudToggle(body, T("All categories", "Все категории"), 0, 86, 400, () => _searchAllItems,
                    value => { _searchAllItems = value; _catalogPage = 0; _listDirty = true; });
                var category = GetItemCategoryForTab(_mainTab);
                _itemTypeFilters.TryGetValue(category, out string type);
                var types = new[] { "" }.Concat(_itemEntries.Where(e => e.Category == category).Select(e => e.ItemTypeLabel)
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t)).ToArray();
                HudDropdown(body, types.Select(t => string.IsNullOrEmpty(t) ? T("All types", "Все типы") : GetItemTypeDisplay(t)).ToArray(),
                    Math.Max(0, Array.IndexOf(types, type ?? "")), 420, 86, 632, value =>
                    { _itemTypeFilters[category] = types[value]; _catalogPage = 0; _listDirty = true; });
            }
            _nativeList = HudScroll(body); HudPlace(_nativeList, 0, 126, BodyWidth, 236);
            HudButton(body, "◀", 0, 370, 44, 28, () => { _catalogPage = Math.Max(0, _catalogPage - 1); _listDirty = true; });
            var pageLabel = HudLabel(body, "", 15); HudPlace(pageLabel, 56, 372, 920, 28);
            _hudDynamic.Add(() => pageLabel.text = T("Found: ", "Найдено: ") + _catalogTotal + "  ·  " + (_catalogPage + 1) + "/" + Math.Max(1, (_catalogTotal + 39) / 40));
            HudButton(body, "▶", 1008, 370, 44, 28, () => { _catalogPage++; _listDirty = true; });
            Transform details = HudPanel(body, 0, 410, BodyWidth, 190);
            BuildCatalogueDetails(details, creatures || (allCatalogues && _catalogSelectionKind == 1));
            if (creatures || (allCatalogues && _catalogSelectionKind == 1))
            {
                HudButton(body, T("In front [F7]", "Создать перед собой [F7]"), 0, 610, 518, 36, () => SpawnSelected(false));
                HudButton(body, T("At crosshair [F8]", "Создать под прицелом [F8]"), 534, 610, 518, 36, () => SpawnSelected(true));
            }
            else
            {
                HudButton(body, T("Add to inventory", "В инвентарь"), 0, 610, 518, 36, AddSelectedItemToInventory);
                HudButton(body, T("Add to kit", "В набор"), 534, 610, 518, 36, AddSelectedItemToKit);
            }
            _listDirty = true;
        }

        private void RefreshNativeCatalogue()
        {
            _listDirty = false;
            if (_nativeList == null) return;
            if (_nativeListKey != CatalogueViewKey) _catalogScrollPosition = 1f;
            else if (!_hudDirty && _nativeList.content.childCount > 0) _catalogScrollPosition = _nativeList.verticalNormalizedPosition;
            _nativeListKey = CatalogueViewKey;
            foreach (Transform child in _nativeList.content.Cast<Transform>().ToArray())
            { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            const int pageSize = 40;
            if (_mainTab == 0)
            {
                var entries = _entries.Where(MatchesFilters).ToList();
                _catalogPage = Mathf.Clamp(_catalogPage, 0, Math.Max(0, (entries.Count - 1) / pageSize));
                var page = entries.Skip(_catalogPage * pageSize).Take(pageSize).ToList();
                for (int i = 0; i < page.Count; i++)
                {
                    var entry = page[i];
                    var row = HudButton(_nativeList.content, GetCreatureDisplayName(entry) + "\n" + entry.PrefabName + " · " +
                        T(entry.BiomeLabel, TranslateBiomeList(entry.Biomes)), 0, i * 50, 974, 46,
                        () => { _selected = entry; RebuildNativeHud(); });
                    row.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                    HudHighlight(row, ReferenceEquals(entry, _selected));
                    HudButton(_nativeList.content, _favoriteCreatures.Contains(entry.PrefabName) ? "★" : "☆", 980, i * 50, 42, 46,
                        () => { ToggleCreatureFavorite(entry.PrefabName); _listDirty = true; });
                }
                CatalogueEmpty(page.Count, entries.Count);
            }
            else if (_mainTab == 6)
            {
                var creatureEntries = _entries.Where(e => MatchesGlobalCreatureSearch(e) &&
                    (!_globalFavoritesOnly || _favoriteCreatures.Contains(e.PrefabName))).Cast<object>();
                var itemEntries = _itemEntries.Where(e => MatchesGlobalItemSearch(e) &&
                    (!_globalFavoritesOnly || _favoriteItems.Contains(e.PrefabName))).Cast<object>();
                var entries = creatureEntries.Concat(itemEntries).OrderBy(e =>
                    e is CreatureEntry c ? GetCreatureDisplayName(c) : GetItemDisplayName((ItemEntry)e),
                    StringComparer.CurrentCultureIgnoreCase).ToList();
                _catalogPage = Mathf.Clamp(_catalogPage, 0, Math.Max(0, (entries.Count - 1) / pageSize));
                var page = entries.Skip(_catalogPage * pageSize).Take(pageSize).ToList();
                for (int i = 0; i < page.Count; i++)
                {
                    if (page[i] is CreatureEntry creature)
                    {
                        var row = HudButton(_nativeList.content, T("Creature · ", "Существо · ") + GetCreatureDisplayName(creature) + "\n" + creature.PrefabName,
                            0, i * 50, 974, 46, () => { _selected = creature; _selectedItem = null; _catalogSelectionKind = 1; RebuildNativeHud(); });
                        row.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                        HudHighlight(row, ReferenceEquals(creature, _selected));
                        HudButton(_nativeList.content, _favoriteCreatures.Contains(creature.PrefabName) ? "★" : "☆", 980, i * 50, 42, 46,
                            () => { ToggleCreatureFavorite(creature.PrefabName); _listDirty = true; });
                    }
                    else if (page[i] is ItemEntry item)
                    {
                        var row = HudButton(_nativeList.content, GetCategoryDisplay(item.Category) + " · " + GetItemDisplayName(item) + "\n" + item.PrefabName,
                            0, i * 50, 974, 46, () => { _selectedItem = item; _selected = null; _catalogSelectionKind = 2; _itemQuality = 1; RebuildNativeHud(); });
                        row.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                        HudHighlight(row, ReferenceEquals(item, _selectedItem));
                        HudButton(_nativeList.content, _favoriteItems.Contains(item.PrefabName) ? "★" : "☆", 980, i * 50, 42, 46,
                            () => { ToggleItemFavorite(item.PrefabName); _listDirty = true; });
                    }
                }
                CatalogueEmpty(page.Count, entries.Count);
            }
            else
            {
                var category = GetItemCategoryForTab(_mainTab);
                var entries = _itemEntries.Where(e => IsItemVisibleInPanel(e, category)).ToList();
                _catalogPage = Mathf.Clamp(_catalogPage, 0, Math.Max(0, (entries.Count - 1) / pageSize));
                var page = entries.Skip(_catalogPage * pageSize).Take(pageSize).ToList();
                for (int i = 0; i < page.Count; i++)
                {
                    var entry = page[i];
                    var row = HudButton(_nativeList.content, GetItemDisplayName(entry) + "\n" + entry.PrefabName, 0, i * 50, 974, 46,
                        () => { _selectedItem = entry; _itemQuality = 1; RebuildNativeHud(); });
                    row.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                    HudHighlight(row, ReferenceEquals(entry, _selectedItem));
                    HudButton(_nativeList.content, _favoriteItems.Contains(entry.PrefabName) ? "★" : "☆", 980, i * 50, 42, 46,
                        () => { ToggleItemFavorite(entry.PrefabName); _listDirty = true; });
                }
                CatalogueEmpty(page.Count, entries.Count);
            }
        }

        private void CatalogueEmpty(int pageCount, int total)
        {
            _catalogTotal = total;
            if (total == 0)
            { var empty = HudLabel(_nativeList.content, T("Nothing found", "Ничего не найдено"), 17); HudPlace(empty, 4, 8, 974, 46); }
            _nativeList.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(60, pageCount * 50));
            Canvas.ForceUpdateCanvases();
            _nativeList.verticalNormalizedPosition = Mathf.Clamp01(_catalogScrollPosition);
        }

        private bool MatchesGlobalCreatureSearch(CreatureEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(_globalSearch)) return entry != null;
            string query = _globalSearch.Trim();
            return ContainsIgnoreCase(entry.PrefabName, query) || ContainsIgnoreCase(entry.EnglishName, query) ||
                ContainsIgnoreCase(entry.RussianName, query) || ContainsIgnoreCase(entry.LocalizedName, query);
        }

        private bool MatchesGlobalItemSearch(ItemEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(_globalSearch)) return entry != null;
            string query = _globalSearch.Trim(); string alias;
            RussianItemNames.TryGetValue(entry.PrefabName, out alias);
            return ContainsIgnoreCase(entry.PrefabName, query) || ContainsIgnoreCase(entry.EnglishName, query) ||
                ContainsIgnoreCase(entry.LocalizedName, query) || ContainsIgnoreCase(GetItemDisplayName(entry), query) ||
                ContainsIgnoreCase(entry.ItemTypeLabel, query) || ContainsIgnoreCase(GetItemTypeDisplay(entry.ItemTypeLabel), query) ||
                ContainsIgnoreCase(alias, query);
        }

        private void BuildCatalogueDetails(Transform body, bool creatures)
        {
            string name = creatures ? (_selected == null ? "" : GetCreatureDisplayName(_selected)) :
                (_selectedItem == null ? "" : GetItemDisplayName(_selectedItem));
            HudPlace(HudLabel(body, string.IsNullOrEmpty(name) ? T("Select from the list", "Выберите из списка") : name, 20, HudGold), 0, 0, BodyWidth, 28);
            string prefab = creatures ? _selected?.PrefabName : _selectedItem?.PrefabName;
            HudPlace(HudLabel(body, "Prefab ID: " + (prefab ?? "—"), 15), 0, 32, BodyWidth, 24);
            HudPlace(HudLabel(body, T("Count", "Количество"), 17), 0, 72, 130, 28);
            HudInput(body, creatures ? _count.ToString() : _itemCountInput, 140, 66, 110, 34, 4, value =>
            {
                if (creatures) { if (int.TryParse(value, out int n)) _count = Mathf.Clamp(n, 1, 50); }
                else _itemCountInput = value;
            });
            HudPlace(HudLabel(body, creatures ? T("Stars", "Звёзды") : T("Quality", "Качество"), 17), 290, 72, 130, 28);
            int max = creatures ? 2 : Math.Max(1, _selectedItem?.MaxQuality ?? 1);
            HudIntegerInput(body, 430, 66, 100, creatures ? 0 : 1, max,
                () => creatures ? _level - 1 : _itemQuality, n => { if (creatures) _level = n + 1; else _itemQuality = n; });
            var limit = HudLabel(body, "", 17); HudPlace(limit, 542, 72, 180, 28);
            _hudDynamic.Add(() => limit.text = (creatures ? _level - 1 : _itemQuality) + T(" of ", " из ") + max);
            HudInput(body, creatures ? _customName : _itemCustomName, 0, 110, 510, 34, 48,
                v => { if (creatures) _customName = v; else _itemCustomName = v; }, T("Custom name (optional)", "Своё имя (необязательно)"));
            if (creatures)
            {
                HudToggle(body, T("Spawn tamed", "Призывать приручённым"), 542, 110, 510, () => _spawnTamed, v => _spawnTamed = v);
                HudToggle(body, T("3D spawn markers", "3D-маркеры спавна"), 0, 154, 510, () => _spawnMarkerConfig.Value,
                    v => { _spawnMarkerConfig.Value = v; TrySavePreferences(); });
                HudToggle(body, T("Markers at crosshair", "Маркеры под прицелом"), 542, 154, 510, () => _spawnMarkerCrosshairConfig.Value,
                    v => { _spawnMarkerCrosshairConfig.Value = v; TrySavePreferences(); });
            }
            else HudInput(body, _itemCustomDescription, 542, 110, 510, 76, 500, v => _itemCustomDescription = v,
                T("Custom description (unstackable items)", "Описание (для предметов со стаком 1)"), true);
        }

        private void BuildAdminPage()
        {
            Transform body = _nativeBody.transform;
            string[] sections = { T("World", "Мир"), T("Player", "Игрок"), T("Skills", "Навыки"),
                T("Inventory", "Инвентарь"), T("Locations", "Локации"), T("Players", "Игроки"), T("Audit log", "Журнал") };
            for (int i = 0; i < sections.Length; i++)
            {
                int section = i;
                var button = HudButton(body, sections[i], i * 150, 0, 146, 34,
                () => { _adminSection = section; SaveAdminPreferences(); RebuildNativeHud(); });
                HudHighlight(button, i == _adminSection);
            }
            Transform form = HudPanel(body, 0, 58, 510, 520);
            Transform right = HudPanel(body, 542, 58, 510, 520);
            float y = 0;
            if (_adminSection == 0)
            {
                HudSection(form, T("WORLD TIME", "ВРЕМЯ МИРА"), ref y);
                var clock = HudLabel(form, WorldClock(), 22, HudGold); HudPlace(clock, 0, y, 434, 34); y += 44;
                _hudDynamic.Add(() => clock.text = WorldClock());
                string[] presets = { "06:00", "12:00", "18:00", "00:00" };
                for (int i = 0; i < presets.Length; i++)
                {
                    int hour = new[] { 6, 12, 18, 0 }[i];
                    HudButton(form, presets[i], i % 2 * 222, y + i / 2 * 40, 212, 34, () =>
                    { _timeHour = hour; _timeMinute = 0; SaveAdminPreferences(); RebuildNativeHud(); });
                }
                y += 88;
                HudIntegerInput(form, 0, y, 84, 0, 23, () => _timeHour, n => { _timeHour = n; SaveAdminPreferences(); });
                var colon = HudLabel(form, ":", 22); HudPlace(colon, 92, y, 24, 34);
                HudIntegerInput(form, 122, y, 84, 0, 59, () => _timeMinute, n => { _timeMinute = n; SaveAdminPreferences(); });
                HudButton(form, T("Set time", "Установить"), 222, y, 212, 34, () =>
                {
                    int hour = _timeHour, minute = _timeMinute;
                    RequestWorldAction(AdminAction.Time, p => { p.Write(hour); p.Write(minute); });
                });
                y += 48;
                HudParagraph(form, T("Time advances to the next occurrence of HH:MM for the entire world. Passing midnight increases the day number.",
                    "Время для всего мира перейдёт вперёд до ближайших ЧЧ:ММ. При переходе через полночь номер дня увеличится."), ref y, 90);
                form = right; y = 0;
                HudSection(form, T("WEATHER", "ПОГОДА"), ref y);
                var weatherChoices = WeatherChoices().ToList();
                HudButton(form, "◀", 0, y, 70, 36, () => ChangeWeather(weatherChoices, -1));
                var weatherLabel = HudLabel(form, WeatherDisplayName(_serverForcedWeather), 16, HudGold);
                HudPlace(weatherLabel, 78, y, 278, 36);
                _hudDynamic.Add(() => weatherLabel.text = WeatherDisplayName(_serverForcedWeather));
                HudButton(form, "▶", 364, y, 70, 36, () => ChangeWeather(weatherChoices, 1));
                y += 46;
                HudParagraph(form, T("The selected weather is applied to everyone. Dynamic restores the world's normal weather cycle.",
                    "Выбранная погода применяется ко всем игрокам. «Динамическая» возвращает обычную смену погоды."), ref y, 76);
                HudSection(form, T("RAIDS", "НАБЕГИ"), ref y);
                var raidButton = HudButton(form, _serverRaidsDisabled ? T("Enable future raids", "Включить набеги") :
                    T("Stop raid and disable raids", "Остановить набег и отключить набеги"), 0, y, 434, 38,
                    () => RequestWorldAction(AdminAction.Raids, p => p.Write(!_serverRaidsDisabled)));
                _hudDynamic.Add(() => raidButton.GetComponentInChildren<Text>().text = _serverRaidsDisabled ?
                    T("Enable future raids", "Включить набеги") : T("Stop raid and disable raids", "Остановить набег и отключить набеги"));
                y += 46;
                HudParagraph(form, T("Disabling stops an active raid and blocks new raids until enabled again.",
                    "Отключение завершает текущий набег и блокирует новые, пока вы снова не включите их."), ref y, 72);
                HudButton(right, T("Clear all dropped items on the ground", "Очистить все выброшенные предметы"),
                    0, 244, 434, 40, () => RequestWorldAction(AdminAction.ClearDroppedItems, null));
                float dropsInfoY = 292;
                HudParagraph(right, T("Removes networked item drops throughout the world. Containers and player inventories are untouched.",
                    "Удаляет лежащие в мире сетевые предметы. Содержимое контейнеров и инвентарь игроков не затрагиваются."), ref dropsInfoY, 88);
            }
            if (_adminSection == 1)
            {
                HudSection(form, T("VITALS", "РЕСУРСЫ"), ref y);
                AdminToggle(form, T("∞ Health", "∞ Здоровье"), ref y, () => _infiniteHealth, v => _infiniteHealth = v);
                AdminToggle(form, T("∞ Stamina", "∞ Выносливость"), ref y, () => _infiniteStamina, v => _infiniteStamina = v);
                AdminToggle(form, T("∞ Eitr", "∞ Эйтр"), ref y, () => _infiniteEitr, v => _infiniteEitr = v);
                AdminToggle(form, T("∞ Adrenaline", "∞ Адреналин"), ref y, () => _infiniteAdrenaline, v => _infiniteAdrenaline = v, _adrenalineHookAvailable);
                AdminToggle(form, T("∞ Forsaken power cooldown", "∞ Перезарядка силы боссов"), ref y,
                    () => _infiniteGuardianPower, v => _infiniteGuardianPower = v);
                float infoY = y + 12;
                HudParagraph(form, T("Close Nexus before moving. Space: up; Ctrl: down; Shift: faster. Z/B shortcuts require the option on the right.",
                    "Для передвижения закройте Nexus. Пробел: вверх; Ctrl: вниз; Shift: быстрее. Горячие клавиши Z/B включаются справа."), ref infoY, 120);
                form = right; y = 0;
                HudSection(form, T("MOVEMENT & COMBAT", "ПЕРЕДВИЖЕНИЕ И БОЙ"), ref y);
                AdminToggle(form, T("Flight [Z]", "Полёт [Z]"), ref y, () => _adminFly, SetAdminFly);
                AdminToggle(form, T("Free building [B]", "Строительство без затрат [B]"), ref y, () => _adminCreative, v => _adminCreative = v);
                AdminToggle(form, T("Mobs ignore me", "Мобы меня не замечают"), ref y, () => _adminGhost, v => _adminGhost = v);
                AdminToggle(form, T("Z/B while panel is closed", "Z/B при закрытой панели"), ref y, () => _adminHotkeys, v => _adminHotkeys = v);
                AdminToggle(form, T("Super damage (hostile mobs)", "Суперурон (враждебные мобы)"), ref y, () => _superDamage, v => _superDamage = v, _outgoingDamageHookAvailable);
                var damage = HudLabel(form, T("Damage ×", "Урон ×") + _superDamageMultiplier, 17); HudPlace(damage, 0, y, 200, 32);
                HudInput(form, _superDamageMultiplier.ToString(CultureInfo.InvariantCulture), 222, y, 212, 32, 6, v =>
                {
                    if (float.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) && AdminRules.Finite(n) && n >= 1 && n <= 25)
                    { _superDamageMultiplier = n; SaveAdminPreferences(); damage.text = T("Damage ×", "Урон ×") + n; }
                }); y += 46;
                HudSection(form, T("CARRY WEIGHT", "ПЕРЕНОСИМЫЙ ВЕС"), ref y);
                var carryWeightStatus = HudLabel(form, "", 15);
                HudPlace(carryWeightStatus, 0, y, 434, 28);
                _hudDynamic.Add(() =>
                {
                    var player = Player.m_localPlayer;
                    carryWeightStatus.text = T("Current effective limit: ", "Текущий лимит с бонусами: ") +
                        (player == null ? "—" : player.GetMaxCarryWeight().ToString("0.##", CultureInfo.InvariantCulture)) + " kg";
                });
                y += 32;
                HudInput(form, _carryWeightInput, 0, y, 180, 34, 7, value => _carryWeightInput = value,
                    T("Base carry weight (kg)", "Базовый вес (кг)"));
                HudButton(form, T("Set", "Установить"), 190, y, 118, 34, () =>
                {
                    if (float.TryParse((_carryWeightInput ?? string.Empty).Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float weight)) SetMaxCarryWeight(weight);
                    else _status = T("Enter a valid carry weight.", "Введите корректное значение веса.");
                });
                HudButton(form, T("Restore default", "По умолчанию"), 316, y, 118, 34, ResetMaxCarryWeight);
                y += 42;
                HudParagraph(form, T("Stored for this world and character. Status and equipment bonuses still apply.",
                    "Сохраняется для этого мира и персонажа. Бонусы от экипировки и эффектов сохраняются."), ref y, 62);
            }
            if (_adminSection == 2)
            {
                HudSection(form, T("SKILL LEVELS", "УРОВНИ НАВЫКОВ"), ref y);
                var choices = SkillChoices();
                int pages = Math.Max(1, (choices.Length + 35) / 36);
                _skillPage = Mathf.Clamp(_skillPage, 0, pages - 1);
                int skillIndex = 0;
                foreach (var choice in choices.Skip(_skillPage * 36).Take(36))
                {
                    var button = HudButton(right, SkillName(choice), skillIndex % 3 * 172, skillIndex / 3 * 38, 166, 34,
                        () => { _skillChoice = choice; SaveAdminPreferences(); RebuildNativeHud(); });
                    button.GetComponentInChildren<Text>().fontSize = 14;
                    HudHighlight(button, choice == _skillChoice); skillIndex++;
                }
                if (pages > 1)
                {
                    HudButton(right, "◀", 0, 470, 60, 34, () => { _skillPage--; RebuildNativeHud(); });
                    HudPlace(HudLabel(right, (_skillPage + 1) + "/" + pages, 17), 80, 474, 340, 30);
                    HudButton(right, "▶", 450, 470, 60, 34, () => { _skillPage++; RebuildNativeHud(); });
                }
                var current = HudLabel(form, "", 17); HudPlace(current, 0, y, 434, 32); y += 38;
                _hudDynamic.Add(() => current.text = SkillName(_skillChoice) + " · " +
                    (Player.m_localPlayer?.GetSkills()?.GetSkillLevel(_skillChoice) ?? 0).ToString("0.##"));
                var target = HudLabel(form, T("Set level (0–100)", "Установить уровень (0–100)"), 17); HudPlace(target, 0, y, 240, 32);
                HudIntegerInput(form, 282, y, 152, 0, 100, () => _skillTarget, n => { _skillTarget = n; SaveAdminPreferences(); }); y += 44;
                HudButton(form, T("Set selected skill", "Установить выбранный навык"), 0, y, 434, 36, () =>
                { var choice = _skillChoice; int level = _skillTarget; RaiseSkills(false, choice, level); }); y += 46;
                HudButton(form, T("All skills → 100", "Все навыки → 100"), 0, y, 434, 36,
                    () => RaiseSkills(true, _skillChoice, 100)); y += 48;
                HudParagraph(form, T("Sets the chosen level exactly, including lowering it. Changes are saved with your character.",
                    "Устанавливает точный уровень, в том числе ниже текущего. Игра сохраняет навык вместе с персонажем."), ref y, 84);
                HudSection(form, T("SKILL EXPERIENCE", "ОПЫТ НАВЫКОВ"), ref y);
                HudInput(form, _skillXpInput, 0, y, 150, 34, 8, v => _skillXpInput = v);
                HudButton(form, T("Apply XP ×1–1000", "Применить XP ×1–1000"), 160, y, 274, 34, () =>
                {
                    if (float.TryParse(_skillXpInput.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) &&
                        AdminRules.Finite(n) && n >= 1 && n <= 1000) { _skillXpMultiplier = n; SaveAdminPreferences(); }
                    else _status = T("XP multiplier must be 1–1000.", "Множитель XP должен быть от 1 до 1000.");
                }); y += 44;
            }
            if (_adminSection == 3)
            {
                HudSection(form, T("MAINTENANCE", "ОБСЛУЖИВАНИЕ"), ref y);
                HudButton(form, T("Heal myself", "Восстановить здоровье"), 0, y, 434, 36, HealSelf); y += 46;
                HudButton(form, T("Repair inventory", "Починить инвентарь"), 0, y, 434, 36, RepairInventory); y += 46;
                HudButton(form, T("Fill stacks", "Заполнить стаки"), 0, y, 434, 36, MaximizeInventoryStacks); y += 52;
                BuildTrashControls(form, ref y);
                form = right; y = 0;
                HudSection(form, T("NEARBY CREATURE CLEANUP", "ОЧИСТКА СУЩЕСТВ РЯДОМ"), ref y);
                HudParagraph(form, T("Radius: ", "Радиус: ") + GetKillRadius().ToString("0") + T(" m. Players are excluded.", " м. Игроки исключены."), ref y, 52);
                HudToggle(form, T("Include tamed creatures", "Включить приручённых"), 0, y, 434, () => _killIncludeTamed, v => { _killIncludeTamed = v; SaveAdminPreferences(); }); y += 42;
                HudToggle(form, T("Include bosses", "Включить боссов"), 0, y, 434, () => _killIncludeBosses, v => { _killIncludeBosses = v; SaveAdminPreferences(); }); y += 42;
                HudButton(form, T("Kill nearby creatures", "Убить существ поблизости"), 0, y, 434, 36, () => KillNearbyCreatures()); y += 48;
            }
            if (_adminSection == 4) BuildLocationsAdminPage(form, right);
            if (_adminSection == 5) BuildPlayersAdminPage(form, right);
            if (_adminSection == 6) BuildAuditAdminPage(body);
            y = 582;
            HudParagraph(body, T("Choices are saved for this world and character. Administrator rights are checked again on login.",
                "Выбор сохраняется для этого мира и персонажа. При входе права администратора проверяются заново."), ref y, 68);
        }

        private void ChangeWeather(List<string> choices, int delta)
        {
            if (choices == null || choices.Count == 0) return;
            int currentIndex = choices.IndexOf(_serverForcedWeather ?? string.Empty);
            int nextIndex = AdminRules.CycleIndex(currentIndex, delta, choices.Count);
            if (nextIndex < 0) return;
            string selected = choices[nextIndex];
            RequestWorldAction(AdminAction.Weather, packet => packet.Write(selected));
        }

        private string WeatherDisplayName(string weather)
        {
            if (string.IsNullOrEmpty(weather)) return T("Dynamic weather", "Погода по миру");
            string localized = Localization.instance?.Localize(weather);
            return string.IsNullOrEmpty(localized) || localized == weather ? weather : localized;
        }

        private void BuildLocationsAdminPage(Transform left, Transform right)
        {
            float y = 0;
            HudSection(left, T("UNIQUE WORLD LOCATIONS", "УНИКАЛЬНЫЕ ЛОКАЦИИ МИРА"), ref y);
            var count = HudLabel(left, T("Loading locations…", "Загрузка локаций…"), 15);
            HudPlace(count, 0, y, 274, 32);
            HudButton(left, T("Refresh", "Обновить"), 294, y, 140, 32, () =>
            { _loadedLocationPage = -1; RequestLocationPage(); });
            _hudDynamic.Add(() => count.text = _loadedLocationPage < 0 ?
                T("Loading locations…", "Загрузка локаций…") : _locationTotal > 0 ?
                T("Unique locations: ", "Уникальных локаций: ") + _locationTotal :
                T("No unique locations found", "Уникальные локации не найдены"));
            y += 38;
            HudInput(left, _newLocationName, 0, y, 288, 32, 48, value => _newLocationName = value,
                T("Point name", "Имя точки"));
            HudButton(left, T("Save current position", "Сохранить текущую точку"), 294, y, 140, 32, AddCurrentTeleportPoint);
            y += 38;
            for (int i = 0; i < LocationPageSize; i++)
            {
                int row = i;
                var location = row < _adminLocations.Count ? _adminLocations[row] : null;
                string label = location == null ? "—" : LocationDisplayName(location);
                var button = HudButton(left, label, 0, y + i * 40, 434, 36,
                    () => { if (row < _adminLocations.Count) { _locationSelection = row;
                        var choice = _adminLocations[row]; _locationRenameInput = choice.IsCustom ? choice.Name : choice.NameOverride ?? string.Empty;
                        RebuildNativeHud(); } });
                button.interactable = location != null;
                HudHighlight(button, location != null && row == _locationSelection);
                button.GetComponentInChildren<Text>().fontSize = 14;
            }
            int pages = Math.Max(1, (_locationTotal + LocationPageSize - 1) / LocationPageSize);
            var pageLabel = HudLabel(left, (_locationPage + 1) + " / " + pages, 16);
            HudPlace(pageLabel, 80, y + LocationPageSize * 40 + 6, 260, 30);
            var previous = HudButton(left, "◀", 0, y + LocationPageSize * 40, 64, 34, () => MoveLocationPage(-1));
            previous.interactable = _locationPage > 0;
            var next = HudButton(left, "▶", 370, y + LocationPageSize * 40, 64, 34, () => MoveLocationPage(1));
            next.interactable = _locationPage + 1 < pages;

            y = 0;
            HudSection(right, T("SELECTED LOCATION", "ВЫБРАННАЯ ЛОКАЦИЯ"), ref y);
            var selected = _locationSelection >= 0 && _locationSelection < _adminLocations.Count ? _adminLocations[_locationSelection] : null;
            if (selected == null)
            {
                HudParagraph(right, T("Select a location from the list. The server includes every generated unique location, even places you have not discovered.",
                    "Выберите локацию в списке. Сервер показывает все сгенерированные уникальные места, включая ещё не открытые."), ref y, 110);
            }
            else
            {
                HudPlace(HudLabel(right, LocationDisplayName(selected), 20, HudGold), 0, y, 434, 44); y += 48;
                HudPlace(HudLabel(right, (selected.IsCustom ? T("Nexus point", "Точка Nexus") : "Prefab") + ": " + selected.Prefab, 15), 0, y, 434, 30); y += 34;
                HudPlace(HudLabel(right, "X " + selected.Position.x.ToString("0.0") + "   Y " + selected.Position.y.ToString("0.0") +
                    "   Z " + selected.Position.z.ToString("0.0"), 15), 0, y, 434, 30); y += 34;
                var renameLabel = HudLabel(right, T("Nexus display name (empty resets generated name)", "Имя в Nexus (пустое сбросит игровое имя)"), 14);
                HudPlace(renameLabel, 0, y, 434, 28); y += 28;
                if (string.IsNullOrEmpty(_locationRenameInput))
                    _locationRenameInput = selected.IsCustom ? selected.Name : selected.NameOverride ?? string.Empty;
                HudInput(right, _locationRenameInput, 0, y, 434, 34, 48, value => _locationRenameInput = value,
                    T("Name in Nexus", "Имя в Nexus")); y += 40;
                HudButton(right, T("Save name", "Сохранить имя"), 0, y, 434, 36,
                    () => RenameSelectedLocation(selected)); y += 42;
                HudButton(right, T("Teleport to location", "Телепортироваться"), 0, y, 434, 36,
                    () => TeleportToSelectedLocation(selected)); y += 42;
                if (selected.IsCustom)
                {
                    HudButton(right, T("Delete Nexus point", "Удалить точку Nexus"), 0, y, 434, 36,
                        () => DeleteSelectedTeleportPoint(selected)); y += 42;
                }
            }
            float returnY = Math.Max(y + 8, 440);
            var back = HudButton(right, T("Return to saved point", "Вернуться в исходную точку"), 0, returnY, 434, 36, ReturnFromLocation);
            back.interactable = _returnAvailable;
            var returnStatus = HudLabel(right, _returnAvailable ? T("Return point saved", "Исходная точка сохранена") :
                T("Return point appears after teleport", "Точка возврата появится после телепорта"), 14);
            HudPlace(returnStatus, 0, returnY + 42, 434, 32);
            if (_loadedLocationPage != _locationPage) RequestLocationPage();
        }

        private string LocationDisplayName(NexusWorldLocation location)
        {
            string display = location.Name ?? location.Prefab;
            if (location.IsCustom || !string.IsNullOrWhiteSpace(location.NameOverride))
                return display + "  ·  " + T("Nexus point", "Точка Nexus");
            string localized = Localization.instance?.Localize(display);
            if (!string.IsNullOrEmpty(localized) && localized != display) display = localized;
            return display + "  ·  " + location.Prefab;
        }

        private void AddCurrentTeleportPoint()
        {
            string name = (_newLocationName ?? string.Empty).Trim();
            RequestWorldAction(AdminAction.AddLocation, packet => packet.Write(name), _ =>
            {
                _newLocationName = string.Empty;
                _locationPage = _locationTotal / LocationPageSize;
                RefreshLocationPage();
            });
        }

        private void RenameSelectedLocation(NexusWorldLocation location)
        {
            if (location == null) return;
            string name = _locationRenameInput ?? string.Empty;
            RequestWorldAction(AdminAction.RenameLocation, packet => { packet.Write(location.Key); packet.Write(name); }, _ =>
            {
                _locationRenameInput = name.Trim();
                RefreshLocationPage();
            });
        }

        private void DeleteSelectedTeleportPoint(NexusWorldLocation location)
        {
            if (location == null || !location.IsCustom) return;
            RequestWorldAction(AdminAction.DeleteLocation, packet => packet.Write(location.Key), _ => RefreshLocationPage());
        }

        private void RefreshLocationPage()
        {
            _loadedLocationPage = -1; _adminLocations.Clear(); _locationSelection = 0;
            _locationRenameInput = string.Empty;
            RequestLocationPage();
            RebuildNativeHud();
        }

        private void MoveLocationPage(int delta)
        {
            int pages = Math.Max(1, (_locationTotal + LocationPageSize - 1) / LocationPageSize);
            int next = Mathf.Clamp(_locationPage + delta, 0, pages - 1);
            if (next == _locationPage) return;
            _locationPage = next; _loadedLocationPage = -1; _locationSelection = 0; _adminLocations.Clear(); RebuildNativeHud();
        }

        private void RequestLocationPage()
        {
            if (_pendingActions.Values.Any(p => p.Kind != AdminAction.Poll)) return;
            int page = _locationPage;
            RequestWorldAction(AdminAction.Locations, packet => packet.Write(page), response =>
            {
                int receivedPage = response.ReadInt(), total = response.ReadInt(), count = response.ReadInt();
                if (receivedPage < 0 || receivedPage > 1000 || total < 0 || count < 0 || count > LocationPageSize)
                {
                    Logger.LogWarning("Rejected invalid Nexus location page header.");
                    return;
                }
                var entries = new List<NexusWorldLocation>(count);
                for (int i = 0; i < count; i++)
                {
                    string key = response.ReadString(), name = response.ReadString(), prefab = response.ReadString();
                    Vector3 position = response.ReadVector3(); bool isCustom = response.ReadBool(); string nameOverride = response.ReadString();
                    bool validKey = isCustom ? key != null && key.StartsWith("custom:", StringComparison.Ordinal) &&
                        Guid.TryParseExact(key.Substring("custom:".Length), "N", out _) :
                        key != null && key.StartsWith("world:", StringComparison.Ordinal) && key.Length <= 134;
                    if (!validKey || string.IsNullOrWhiteSpace(prefab) || prefab.Length > 128 || name == null || name.Length > 256 ||
                        nameOverride == null || nameOverride.Length > 48 || (nameOverride.Length > 0 && !AdminRules.LocationNameInput(nameOverride, false)) ||
                        !AdminRules.WorldPosition(position.x, position.y, position.z))
                    {
                        Logger.LogWarning("Skipped an invalid Nexus location entry on page " + receivedPage + ".");
                        continue;
                    }
                    name = AdminRules.LocationName(name, prefab);
                    entries.Add(new NexusWorldLocation { Key = key, Name = name, NameOverride = nameOverride,
                        Prefab = prefab, Position = position, IsCustom = isCustom });
                }
                _locationPage = receivedPage; _locationTotal = total; _adminLocations = entries;
                _locationSelection = Mathf.Clamp(_locationSelection, 0, Math.Max(0, entries.Count - 1));
                _locationRenameInput = entries.Count == 0 ? string.Empty :
                    (entries[_locationSelection].IsCustom ? entries[_locationSelection].Name : entries[_locationSelection].NameOverride ?? string.Empty);
                _loadedLocationPage = receivedPage;
                Logger.LogInfo("Nexus locations received: page=" + receivedPage + ", valid=" + entries.Count + ", total=" + total + ".");
                RebuildNativeHud();
            });
        }

        private void TeleportToSelectedLocation(NexusWorldLocation location)
        {
            if (location == null) return;
            RequestWorldAction(AdminAction.Teleport, packet => { packet.Write(location.Key); packet.Write(location.Position); }, response =>
            {
                Vector3 destination = response.ReadVector3();
                if (TeleportLocalPlayer(destination)) { _returnAvailable = _playerReturnAvailable = true; RebuildNativeHud(); }
            });
        }

        private void ReturnFromLocation()
        {
            if (!_returnAvailable) return;
            RequestWorldAction(AdminAction.Return, null, response =>
            {
                Vector3 destination = response.ReadVector3();
                if (TeleportLocalPlayer(destination))
                { _returnAvailable = _playerReturnAvailable = false; DestroyPlayerReturnPrompt(); RebuildNativeHud(); }
            });
        }

        private void BuildAuditAdminPage(Transform body)
        {
            float y = 0;
            HudSection(body, T("SHARED SERVER ADMIN LOG", "ОБЩИЙ ЖУРНАЛ АДМИНИСТРАТОРОВ"), ref y);
            var count = HudLabel(body, T("Loading…", "Загрузка…"), 15);
            HudPlace(count, 0, y, 740, 30);
            HudButton(body, T("Refresh", "Обновить"), 892, y, 142, 32, () =>
            { _loadedAuditPage = -1; RequestAuditPage(); });
            _hudDynamic.Add(() => count.text = T("Entries: ", "Записей: ") + _auditTotal);
            y += 38;
            for (int i = 0; i < 8; i++)
            {
                int row = i;
                string entry = row < _adminAuditPage.Count ? _adminAuditPage[row] : "—";
                var button = HudButton(body, entry, 0, y + row * 42, 1034, 38,
                    () => { if (row < _adminAuditPage.Count) { _auditSelection = row; RebuildNativeHud(); } });
                button.interactable = row < _adminAuditPage.Count;
                HudHighlight(button, row == _auditSelection && row < _adminAuditPage.Count);
                button.GetComponentInChildren<Text>().fontSize = 12;
            }
            int pages = Math.Max(1, (_auditTotal + AuditPageSize - 1) / AuditPageSize);
            var pageLabel = HudLabel(body, (_auditPage + 1) + " / " + pages, 16);
            HudPlace(pageLabel, 375, y + 8 * 42 + 2, 280, 30);
            var previous = HudButton(body, "◀", 0, y + 8 * 42, 64, 34, () => MoveAuditPage(-1));
            previous.interactable = _auditPage + 1 < pages;
            var next = HudButton(body, "▶", 970, y + 8 * 42, 64, 34, () => MoveAuditPage(1));
            next.interactable = _auditPage > 0;
            var detail = HudLabel(body, _auditSelection >= 0 && _auditSelection < _adminAuditPage.Count ?
                _adminAuditPage[_auditSelection] : T("Select a record to view it.", "Выберите запись для просмотра."), 14);
            detail.alignment = TextAnchor.MiddleLeft; detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            HudPlace(detail, 0, y + 8 * 42 + 40, 1034, 42);
            if (_loadedAuditPage != _auditPage) RequestAuditPage();
        }

        private void MoveAuditPage(int delta)
        {
            int pages = Math.Max(1, (_auditTotal + AuditPageSize - 1) / AuditPageSize);
            int next = Mathf.Clamp(_auditPage + delta, 0, pages - 1);
            if (next == _auditPage) return;
            _auditPage = next; _loadedAuditPage = -1; _auditSelection = 0; _adminAuditPage.Clear(); RebuildNativeHud();
        }

        private void RequestAuditPage()
        {
            if (_pendingActions.Values.Any(p => p.Kind != AdminAction.Poll)) return;
            int page = _auditPage;
            RequestWorldAction(AdminAction.Audit, packet => packet.Write(page), response =>
            {
                int receivedPage = response.ReadInt(), total = response.ReadInt(), count = response.ReadInt();
                if (receivedPage < 0 || total < 0 || count < 0 || count > AuditPageSize) return;
                var entries = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    string entry = response.ReadString();
                    if (entry == null || entry.Length > 700) return;
                    entries.Add(entry);
                }
                _auditPage = receivedPage; _auditTotal = total; _adminAuditPage = entries;
                _auditSelection = Mathf.Clamp(_auditSelection, 0, Math.Max(0, entries.Count - 1));
                _loadedAuditPage = receivedPage; RebuildNativeHud();
            });
        }

        private void CreateNativeKit()
        {
            string name = NexusAdvancedData.CleanName(_newKitName);
            if (name.Length == 0) { _status = T("Enter a kit name.", "Введите название набора."); return; }
            if (_advancedSaveBlocked || _advanced.Kits.Count >= NexusKitWorkshop.MaxKits)
            { _status = T("Kit limit reached or saving is blocked.", "Достигнут лимит наборов или сохранение заблокировано."); return; }
            var kit = new NexusKit { Name = NexusKitWorkshop.UniqueName(name, _advanced.Kits) };
            _advanced.Kits.Add(kit);
            if (!SaveNexusAdvanced()) { _advanced.Kits.Remove(kit); return; }
            _activeKitIndex = _advanced.Kits.Count - 1; _newKitName = string.Empty; _kitRenameInput = kit.Name;
            _status = T("Kit created.", "Набор создан."); RebuildNativeHud();
        }

        private void DeleteNativeKit()
        {
            if (_advanced.Kits.Count == 0 || _advancedSaveBlocked) return;
            int index = Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1);
            NexusKit removed = _advanced.Kits[index]; _advanced.Kits.RemoveAt(index);
            if (!SaveNexusAdvanced()) _advanced.Kits.Insert(index, removed);
            _activeKitIndex = Mathf.Clamp(index, 0, _advanced.Kits.Count - 1); RebuildNativeHud();
        }

        private void RenameNativeKit()
        {
            if (_advanced.Kits.Count == 0 || _advancedSaveBlocked) return;
            string name = NexusAdvancedData.CleanName(_kitRenameInput);
            if (name.Length == 0) { _status = T("Enter a valid kit name.", "Введите корректное имя набора."); return; }
            NexusKit kit = _advanced.Kits[Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1)];
            string old = kit.Name; kit.Name = name;
            if (!SaveNexusAdvanced()) kit.Name = old;
            else { _status = T("Kit renamed.", "Набор переименован."); RebuildNativeHud(); }
        }

        private void RemoveNativeKitPart(NexusKit kit, int index)
        {
            if (_advancedSaveBlocked || kit == null || index < 0 || index >= kit.Items.Count) return;
            NexusKitPart removed = kit.Items[index]; kit.Items.RemoveAt(index);
            if (!SaveNexusAdvanced()) kit.Items.Insert(index, removed);
            else _editingKitPart = -1;
            RebuildNativeHud();
        }

        private void SaveNativeKitPart()
        {
            if (_advancedSaveBlocked || _editingKitPart < 0 || _advanced.Kits.Count == 0) return;
            NexusKit kit = _advanced.Kits[Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1)];
            if (_editingKitPart >= kit.Items.Count) { _editingKitPart = -1; return; }
            if (!int.TryParse(_kitPartCountInput, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 9999 ||
                !int.TryParse(_kitPartQualityInput, NumberStyles.None, CultureInfo.InvariantCulture, out int quality) || quality < 1 || quality > 10)
            { _status = T("Enter a valid count and quality.", "Введите допустимые количество и качество."); return; }
            NexusKitPart part = kit.Items[_editingKitPart]; int oldCount = part.Count, oldQuality = part.Quality;
            int maximum = Math.Max(1, FindItemById(part.PrefabId)?.MaxQuality ?? 1);
            if (quality > maximum) { _status = T("Quality exceeds the item limit.", "Качество превышает максимум предмета."); return; }
            string oldName = part.CustomName, oldDescription = part.CustomDescription;
            part.Count = count; part.Quality = quality;
            part.CustomName = NexusAdvancedData.CleanName(_kitPartNameInput);
            part.CustomDescription = NexusAdvancedData.CleanDescription(_kitPartDescriptionInput);
            var entry = FindItemById(part.PrefabId);
            if (entry != null && entry.MaxStack > 1 && (part.CustomName.Length > 0 || part.CustomDescription.Length > 0))
            {
                part.Count = oldCount; part.Quality = oldQuality; part.CustomName = oldName; part.CustomDescription = oldDescription;
                _status = T("Custom names and descriptions require max stack 1.", "Свои имя и описание доступны для предметов со стаком 1."); return;
            }
            string reason;
            if (!NexusKitWorkshop.Validate(kit, out reason))
            { part.Count = oldCount; part.Quality = oldQuality; part.CustomName = oldName; part.CustomDescription = oldDescription; _status = reason; return; }
            if (!SaveNexusAdvanced()) { part.Count = oldCount; part.Quality = oldQuality; part.CustomName = oldName; part.CustomDescription = oldDescription; }
            else { _editingKitPart = -1; _status = T("Kit item updated.", "Предмет набора обновлён."); RebuildNativeHud(); }
        }

        private void CaptureCurrentInventoryToKit()
        {
            if (!AuthorizeLocal(CaptureCurrentInventoryToKit, "save current inventory to kit")) return;
            if (_advancedSaveBlocked || _advanced.Kits.Count == 0)
            { _status = T("Create or select a kit before capturing inventory.", "Сначала создайте или выберите набор."); return; }
            object inventory = GetPlayerInventory();
            if (inventory == null) { _status = T("Inventory is unavailable.", "Инвентарь недоступен."); return; }
            var captured = new List<NexusKitPart>();
            var items = GetInventoryItems(inventory).Cast<object>().ToList();
            foreach (string slot in new[] { "m_rightItem", "m_leftItem", "m_ammoItem", "m_helmetItem", "m_chestItem",
                "m_legItem", "m_shoulderItem", "m_utilityItem", "m_trinketItem" })
            {
                object equipped = ReadMember(Player.m_localPlayer, slot);
                if (equipped != null && !items.Any(i => ReferenceEquals(i, equipped))) items.Add(equipped);
            }
            foreach (object item in items)
            {
                int count = ReadIntMember(item, "m_stack", 1);
                if (count <= 0) continue;
                var prefab = ReadMember(item, "m_dropPrefab") as GameObject;
                if (prefab == null)
                { _status = T("An item has no prefab; current kit was not changed.", "У предмета нет префаба; набор не изменён."); return; }
                ItemEntry entry = FindItemById(prefab.name);
                if (entry == null)
                { _status = T("Refresh catalogues before capturing: ", "Обновите каталоги перед сохранением: ") + prefab.name; return; }
                int quality = ReadIntMember(item, "m_quality", 1);
                if (quality < 1 || quality > entry.MaxQuality)
                { _status = T("Unsupported item quality; current kit was not changed: ", "Качество не поддерживается; набор не изменён: ") + prefab.name; return; }
                string name = ReadEffectiveItemNameForKit(item), description = ReadEffectiveItemDescriptionForKit(item);
                while (count > 0)
                {
                    var existing = captured.FirstOrDefault(p => p.Count < 9999 && p.PrefabId == entry.PrefabName && p.Quality == quality &&
                        p.CustomName == name && p.CustomDescription == description);
                    if (existing == null)
                    {
                        existing = new NexusKitPart { PrefabId = entry.PrefabName, Count = 0, Quality = quality,
                            CustomName = name, CustomDescription = description };
                        captured.Add(existing);
                    }
                    int merged = Math.Min(9999 - existing.Count, count); existing.Count += merged; count -= merged;
                    if (captured.Count > NexusKitWorkshop.MaxParts)
                    { _status = T("Inventory has over 100 distinct item rows; current kit was not changed.", "В инвентаре больше 100 строк; набор не изменён."); return; }
                }
            }
            NexusKit kit = _advanced.Kits[Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1)];
            List<NexusKitPart> previous = kit.Items; kit.Items = captured;
            if (!SaveNexusAdvanced()) kit.Items = previous;
            else { _editingKitPart = -1; _status = T("Captured ", "Сохранено предметов: ") + captured.Sum(p => p.Count); RebuildNativeHud(); }
        }

        private void HudDragSurface(Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject("Nexus_DragSurface", typeof(RectTransform), typeof(Image), typeof(NexusHudDragHandler));
            go.layer = GUIManager.UILayer; go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.color = new Color(0, 0, 0, .005f); image.raycastTarget = true;
            HudPlace(go.GetComponent<RectTransform>(), x, y, width, height);
            go.GetComponent<NexusHudDragHandler>().Initialize(_nativeRect, position =>
            {
                if (_hudPositionXConfig == null || _hudPositionYConfig == null) return;
                _hudPositionXConfig.Value = position.x; _hudPositionYConfig.Value = position.y; TrySavePreferences();
            });
        }

        private Transform HudPanel(Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject("Nexus_Panel", typeof(RectTransform));
            go.layer = GUIManager.UILayer; go.transform.SetParent(parent, false);
            HudPlace(go.GetComponent<RectTransform>(), x, y, width, height);
            return go.transform;
        }

        private void AdminToggle(Transform parent, string title, ref float y, Func<bool> read, Action<bool> set, bool available = true)
        {
            var button = HudToggle(parent, title, 0, y, 434, read, value => { set(value); SaveAdminPreferences(); });
            if (!available) button.interactable = false;
            y += 42;
        }
        private Button HudToggle(Transform parent, string title, float x, float y, float width, Func<bool> read, Action<bool> set)
        {
            var button = HudButton(parent, "", x, y, width, 34, () => set(!read()));
            _hudDynamic.Add(() => { HudCaption(button, (read() ? "[+]  " : "[ ]  ") + title); HudHighlight(button, read()); });
            HudCaption(button, (read() ? "[+]  " : "[ ]  ") + title);
            return button;
        }
        private void HudSection(Transform parent, string title, ref float y)
        { var text = HudLabel(parent, title, 19, HudGold); HudPlace(text, 0, y, 434, 28); y += 36; }
        private void HudParagraph(Transform parent, string value, ref float y, float height)
        { var text = HudLabel(parent, value, 16); HudPlace(text, 0, y, 434, height); y += height + 8; }
        private Text HudLabel(Transform parent, string value, int size, Color? color = null)
        {
            var go = GUIManager.Instance.CreateText(value, parent, HudTopLeft, HudTopLeft, Vector2.zero,
                GUIManager.Instance.AveriaSerif, size, color ?? GUIManager.Instance.ValheimBeige, false, Color.black, 200, 30, false);
            go.name = "Nexus_Label";
            var text = go.GetComponent<Text>(); text.supportRichText = false; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.alignment = TextAnchor.UpperLeft;
            return text;
        }
        private Button HudButton(Transform parent, string title, float x, float y, float width, float height,
            Action click, bool requireAdmin = true)
        {
            var go = GUIManager.Instance.CreateButton(title, parent, HudTopLeft, HudTopLeft, Vector2.zero, width, height);
            go.name = "Nexus_Button";
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            foreach (var component in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string componentName = component.GetType().Name;
                if (componentName == "ButtonSfx" || componentName == "ButtonSound" || componentName == "UIButtonSfx")
                    component.enabled = false;
            }
            var text = button.GetComponentInChildren<Text>(); text.fontSize = 16; text.supportRichText = false;
            text.raycastTarget = false;
            if (button.targetGraphic != null) button.targetGraphic.raycastTarget = true;
            button.onClick.AddListener(() =>
            {
                if (requireAdmin && !HasAdminAccess) return;
                PlayNativeButtonSound();
                try { click(); } catch (Exception ex) { Logger.LogError("Nexus action failed: " + ex); _status = T("Action failed; see log.", "Ошибка действия; см. журнал."); }
            });
            HudPlace(button, x, y, width, height);
            return button;
        }
        private InputField HudInput(Transform parent, string value, float x, float y, float width, float height, int limit,
            Action<string> change, string hint = "", bool multiline = false)
        {
            var go = GUIManager.Instance.CreateInputField(parent, HudTopLeft, HudTopLeft, Vector2.zero,
                InputField.ContentType.Standard, hint, 17, width, height);
            go.name = "Nexus_Input";
            var field = go.GetComponent<InputField>();
            field.characterLimit = limit; field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            field.textComponent.supportRichText = false; field.textComponent.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            field.SetTextWithoutNotify(value);
            field.onValueChanged.AddListener(v => { if (HasAdminAccess) change(v); });
            HudPlace(field, x, y, width, height);
            return field;
        }
        private void HudIntegerInput(Transform parent, float x, float y, float width, int minimum, int maximum, Func<int> read, Action<int> set)
        {
            var field = HudInput(parent, read().ToString(), x, y, width, 34, maximum > 999 ? 4 : 3, value =>
            { if (int.TryParse(value, out int n) && n >= minimum && n <= maximum) set(n); });
            field.onEndEdit.AddListener(value =>
            {
                if (HasAdminAccess && int.TryParse(value, out int n) && n >= minimum && n <= maximum) set(n);
                else _status = T("Enter a value in ", "Введите значение в диапазоне ") + minimum + "–" + maximum;
                field.SetTextWithoutNotify(read().ToString());
            });
        }
        private void HudDropdown(Transform parent, string[] options, int selected, float x, float y, float width, Action<int> change)
        {
            // A fixed choice selector avoids scrollable lists of filters or functions.
            int index = options.Length == 0 ? 0 : Mathf.Clamp(selected, 0, options.Length - 1);
            var caption = HudLabel(parent, options.Length == 0 ? "—" : options[index], 16);
            caption.alignment = TextAnchor.MiddleCenter;
            HudPlace(caption, x + 42, y, width - 84, 34);
            void Select(int delta)
            {
                if (options.Length == 0) return;
                index = (index + delta + options.Length) % options.Length;
                caption.text = options[index]; change(index);
            }
            HudButton(parent, "◀", x, y, 34, 34, () => Select(-1)).interactable = options.Length > 1;
            HudButton(parent, "▶", x + width - 34, y, 34, 34, () => Select(1)).interactable = options.Length > 1;
        }
        private static ScrollRect HudScroll(Transform parent)
        {
            var go = DefaultControls.CreateScrollView(GUIManager.Instance.ValheimControlResources);
            go.name = "Nexus_Scroll"; go.transform.SetParent(parent, false);
            var original = go.GetComponent<ScrollRect>();
            var content = original.content; var viewport = original.viewport;
            var verticalBar = original.verticalScrollbar; var horizontalBar = original.horizontalScrollbar;
            UnityEngine.Object.DestroyImmediate(original);
            var scroll = go.AddComponent<NexusScrollRect>();
            scroll.content = content; scroll.viewport = viewport;
            scroll.verticalScrollbar = verticalBar; scroll.horizontalScrollbar = horizontalBar;
            GUIManager.Instance.ApplyScrollRectStyle(scroll);
            if (scroll.horizontalScrollbar != null) { scroll.horizontalScrollbar.gameObject.SetActive(false); scroll.horizontalScrollbar = null; }
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 180; scroll.inertia = false;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.viewport.anchorMin = Vector2.zero; scroll.viewport.anchorMax = Vector2.one;
            scroll.viewport.offsetMin = Vector2.zero; scroll.viewport.offsetMax = new Vector2(-16, 0);
            scroll.content.anchorMin = HudTopLeft; scroll.content.anchorMax = Vector2.one;
            scroll.content.pivot = HudTopLeft; scroll.content.anchoredPosition = Vector2.zero;
            scroll.content.sizeDelta = new Vector2(-20, 500);
            return scroll;
        }
        private static void HudPlace(Component control, float x, float y, float width, float height)
        {
            var rect = control.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = HudTopLeft;
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
        private static void HudCaption(Button button, string value) { if (button != null) button.GetComponentInChildren<Text>().text = value; }
        private static void HudHighlight(Button button, bool active)
        { var colors = button.colors; colors.normalColor = active ? HudGold : Color.white; button.colors = colors; }

        private void DisposeNativeHud()
        {
            if (_nativeInputBlocked) { GUIManager.BlockInput(false); _nativeInputBlocked = false; }
            if (_nativeHud != null) UnityEngine.Object.Destroy(_nativeHud);
            _nativeHud = null; _nativeCanvas = null; _hudDynamic.Clear();
        }

        private void ApplyNativeHudCanvasOrder(bool inventoryVisible)
        {
            if (_nativeCanvas == null) return;
            _nativeCanvas.overrideSorting = inventoryVisible;
            if (inventoryVisible) _nativeCanvas.sortingOrder = 1000;
        }

        private void PlayNativeButtonSound()
        {
            if (_nativeHud == null) return;
            AudioSource source = _nativeHud.GetComponent<AudioSource>();
            if (source == null) source = _nativeHud.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.volume = .22f; source.ignoreListenerPause = true;
            if (_nativeClickClip == null)
            {
                const int frequency = 44100, samples = 1985;
                _nativeClickClip = AudioClip.Create("Nexus_SoftWoodClick", samples, 1, frequency, false);
                var data = new float[samples];
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)frequency;
                    float envelope = Mathf.Exp(-92f * t);
                    float frequencyAtTime = 1850f - 12000f * t;
                    data[i] = Mathf.Sin(2f * Mathf.PI * frequencyAtTime * t) * envelope * .32f;
                }
                _nativeClickClip.SetData(data, 0);
            }
            source.PlayOneShot(_nativeClickClip);
        }
    }

    internal sealed class NexusScrollRect : ScrollRect
    {
        public override void OnScroll(PointerEventData eventData)
        {
            if (eventData == null || !IsActive()) return;
            Vector2 original = eventData.scrollDelta;
            // Unity input modules can report one wheel notch as 0.1 rather than 1.
            // Normalize once in the only scroll handler; don't add a second listener.
            float wheel = original.y != 0 ? original.y : -original.x;
            eventData.scrollDelta = new Vector2(0, NexusInteractionRules.WheelDelta(wheel));
            base.OnScroll(eventData);
            eventData.scrollDelta = original;
        }
    }

    internal sealed class NexusHudDragHandler : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        private RectTransform _panel;
        private Action<Vector2> _onEndDrag;
        internal void Initialize(RectTransform panel, Action<Vector2> onEndDrag)
        { _panel = panel; _onEndDrag = onEndDrag; }
        public void OnDrag(PointerEventData eventData)
        {
            if (_panel == null || eventData == null) return;
            Canvas canvas = _panel.GetComponentInParent<Canvas>();
            float scale = canvas == null ? 1f : Mathf.Max(.01f, canvas.scaleFactor);
            _panel.anchoredPosition = Clamp(_panel, _panel.anchoredPosition + eventData.delta / scale);
        }
        public void OnEndDrag(PointerEventData eventData)
        { if (_panel != null) _onEndDrag?.Invoke(_panel.anchoredPosition); }
        internal static Vector2 Clamp(RectTransform panel, Vector2 position)
        {
            if (panel == null || !(panel.parent is RectTransform parent)) return position;
            float width = panel.rect.width * Mathf.Abs(panel.localScale.x);
            float height = panel.rect.height * Mathf.Abs(panel.localScale.y);
            float maxX = Mathf.Max(0, parent.rect.width * .5f - width * .5f);
            float maxY = Mathf.Max(0, parent.rect.height * .5f - height * .5f);
            return new Vector2(Mathf.Clamp(position.x, -maxX, maxX), Mathf.Clamp(position.y, -maxY, maxY));
        }
    }
}
