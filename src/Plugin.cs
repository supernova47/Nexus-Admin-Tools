using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed partial class ValheimSpawnerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.supernova.valheimspawner";
        public const string PluginName = "Nexus Admin Tools";
        public const string PluginVersion = "1.8.2";

        // Extended quality cap for upgradeable equipment, including weapons and shields.
        private const int ExtendedNonWeaponQualityCap = 10;
        // Bound reflection/inventory work per click; the remainder can be added on the next click.
        private const int MaxUnstackableItemsPerAction = 100;
        private const int MaxStackBatchesPerAction = 200;

        private ConfigEntry<KeyCode> _toggleKey;
        private ConfigEntry<KeyCode> _spawnFrontKey;
        private ConfigEntry<KeyCode> _spawnAimKey;
        private ConfigEntry<int> _defaultCount;
        private ConfigEntry<int> _defaultLevel;
        private ConfigEntry<string> _languageSetting;
        private ConfigEntry<float> _killRadius;
        private ConfigEntry<bool> _spawnMarkerConfig;
        private ConfigEntry<bool> _spawnMarkerCrosshairConfig;
        private ConfigEntry<string> _favoriteItemsConfig;
        private ConfigEntry<string> _favoriteCreaturesConfig;
        private ConfigEntry<float> _hudPositionXConfig;
        private ConfigEntry<float> _hudPositionYConfig;
        private FavoriteIds _favoriteItems = new FavoriteIds();
        private FavoriteIds _favoriteCreatures = new FavoriteIds();
        private bool _itemFavoritesOnly;
        private bool _creatureFavoritesOnly;
        private bool _searchAllItems;
        private Harmony _harmony;
        private static ValheimSpawnerPlugin _activeInstance;
        private float _suppressInputUntil;

        private readonly List<CreatureEntry> _entries = new List<CreatureEntry>();
        private readonly List<CreatureEntry> _filtered = new List<CreatureEntry>();
        private readonly List<ItemEntry> _itemEntries = new List<ItemEntry>();
        private readonly List<ItemEntry> _filteredItems = new List<ItemEntry>();
        private readonly string[] _biomeOptions =
        {
            "All Biomes", "Meadows", "Black Forest", "Swamp", "Mountains", "Plains", "Ocean", "Rivers",
            "Mistlands", "Ashlands", "Deep North", "Mountain Caves", "Any Biome", "Unknown"
        };

        // Explicit Russian names for common vanilla weapons, ammunition and shields.
        // If a prefab is not listed here, the active game's Russian localization is used
        // when it returns a Cyrillic display name; otherwise the English fallback remains.
        private static readonly Dictionary<string, string> RussianItemNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AxeStone", "Каменный топор" },
            { "AxeFlint", "Кремнёвый топор" },
            { "AxeBronze", "Бронзовый топор" },
            { "AxeIron", "Железный топор" },
            { "AxeBlackMetal", "Чернометаллический топор" },
            { "AxeJotunBane", "Топор йотунов" },
            { "AxeBer", "Бер" },
            { "AtgeirBronze", "Бронзовая алебарда" },
            { "AtgeirIron", "Железная алебарда" },
            { "AtgeirBlackmetal", "Чернометаллическая алебарда" },
            { "AtgeirHimminAfl", "Химмин-Афль" },
            { "Battleaxe", "Боевой топор" },
            { "BattleaxeCrystal", "Кристальный боевой топор" },
            { "BattleaxeBloodthirst", "Жажда крови" },
            { "Bow", "Лук" },
            { "BowFineWood", "Лук из ценной древесины" },
            { "BowHuntsman", "Лук охотника" },
            { "BowDraugrFang", "Клык драугра" },
            { "BowSpineSnap", "Хребтолом" },
            { "CrossbowArbalest", "Арбалет" },
            { "KnifeFlint", "Кремнёвый нож" },
            { "KnifeCopper", "Медный нож" },
            { "KnifeSilver", "Серебряный нож" },
            { "KnifeBlackMetal", "Чернометаллический нож" },
            { "KnifeSkollAndHati", "Сколль и Хати" },
            { "KnifeButcher", "Нож мясника" },
            { "MaceBronze", "Бронзовая булава" },
            { "MaceIron", "Железная булава" },
            { "MaceSilver", "Серебряная булава" },
            { "MaceNeedle", "Игла" },
            { "MacePorcupine", "Дикобраз" },
            { "MaceDemolisher", "Разрушитель" },
            { "SledgeStagbreaker", "Оленебой" },
            { "SledgeIron", "Железная кувалда" },
            { "SpearFlint", "Кремнёвое копьё" },
            { "SpearBronze", "Бронзовое копьё" },
            { "SpearElderbark", "Древеснокорое копьё" },
            { "SpearWolfFang", "Копьё с волчьим клыком" },
            { "SpearCarapace", "Карапасное копьё" },
            { "SpearChitin", "Гарпун бездны" },
            { "SwordBronze", "Бронзовый меч" },
            { "SwordIron", "Железный меч" },
            { "SwordSilver", "Серебряный меч" },
            { "SwordBlackmetal", "Чернометаллический меч" },
            { "SwordMistwalker", "Туманоход" },
            { "SwordNiedhogg", "Нидхёгг" },
            { "SwordKrom", "Кром" },
            { "SwordDyrnwyn", "Дюрнвин" },
            { "StaffFireball", "Посох огненного шара" },
            { "StaffIceShards", "Посох ледяных осколков" },
            { "StaffShield", "Посох защиты" },
            { "StaffSkeleton", "Посох скелетов" },
            { "StaffGreenRoots", "Посох зелёных корней" },
            { "StaffLightning", "Посох молнии" },
            { "Torch", "Факел" },
            { "ArrowWood", "Деревянная стрела" },
            { "ArrowFire", "Огненная стрела" },
            { "ArrowFlint", "Кремнёвая стрела" },
            { "ArrowBronze", "Бронзовая стрела" },
            { "ArrowIron", "Железная стрела" },
            { "ArrowSilver", "Серебряная стрела" },
            { "ArrowObsidian", "Обсидиановая стрела" },
            { "ArrowNeedle", "Игла" },
            { "ArrowCarapace", "Карапасная стрела" },
            { "ArrowCharred", "Обугленная стрела" },
            { "ArrowFrost", "Морозная стрела" },
            { "ArrowPoison", "Отравленная стрела" },
            { "BoltWood", "Деревянный болт" },
            { "BoltBone", "Костяной болт" },
            { "BoltIron", "Железный болт" },
            { "BoltBlackmetal", "Чернометаллический болт" },
            { "BoltCarapace", "Карапасный болт" },
            { "BoltCharred", "Обугленный болт" },
            { "ShieldWood", "Деревянный щит" },
            { "ShieldWoodTower", "Деревянный башенный щит" },
            { "ShieldBoneTower", "Костяной башенный щит" },
            { "ShieldBronzeBuckler", "Бронзовый круглый щит" },
            { "ShieldIronSquare", "Железный щит" },
            { "ShieldBanded", "Полосатый щит" },
            { "ShieldSerpentscale", "Щит из чешуи змея" },
            { "ShieldBlackmetal", "Чернометаллический щит" },
            { "ShieldBlackmetalTower", "Чернометаллический башенный щит" },
            { "ShieldCarapace", "Карапасный щит" },
            { "ShieldCarapaceBuckler", "Карапасный круглый щит" },
            { "ShieldFlametal", "Пламеннометаллический щит" }
        };

        private Rect _windowRect = new Rect(30f, 35f, 980f, 820f);
        private Vector2 _listScroll;
        private Vector2 _itemListScroll;
        private int _mainTab;
        private int _itemCount = 1;
        private string _itemCountInput = "1";
        private int _itemQuality = 1;
        private bool _spawnTamed;
        private bool _infiniteHealth, _infiniteStamina, _infiniteEitr;
        private bool _adminFly, _adminCreative, _adminHotkeys;
        private bool _infiniteAdrenaline, _superDamage;
        private float _superDamageMultiplier = 5f;
        private float _nextAdrenalineRefresh;
        private bool _showCreatureSafety, _showItemTechnicalDetails;
        private bool _showItemCustomFields = true;
        private bool _outgoingDamageHookAvailable, _adrenalineHookAvailable;
        private float _nextAdminRefresh;
        private float _skillXpMultiplier = 1f;
        private string _skillXpInput = "1";
        private string _adminToast = string.Empty;
        private float _adminToastUntil;
        private string _itemSearch = string.Empty;
        private readonly Dictionary<ItemCategory, string> _itemTypeFilters = new Dictionary<ItemCategory, string>();
        private bool _isRussian;
        private bool _itemCatalogReady;
        private object _objectDbInstance;
        private object _lastObjectDb;
        private bool _isOpen;
        private bool _searchHasFocus;
        private bool _nameHasFocus;
        private bool _killIncludeTamed;
        private bool _killIncludeBosses;
        private bool _cursorStateStored;
        private bool _oldCursorVisible;
        private CursorLockMode _oldCursorLockState;
        private bool _catalogReady;
        private float _nextRefresh;
        private string _search = string.Empty;
        private string _customName = string.Empty;
        private string _status = "Enter a world to load the catalogue.";
        private int _categoryFilter; // 0 = all, 1 = regular, 2 = boss
        private int _biomeFilter;
        private int _count = 1;
        private int _level = 1;
        private CreatureEntry _selected;
        private ItemEntry _selectedItem;
        private Type _objectDbType;
        private Type _itemDropType;
        private Type _characterType;
        private Type _zNetSceneType;
        private Type _zNetType;
        private Type _playerType;
        private Type _zNetViewType;
        private Component _playerComponent;
        private object _zNetSceneInstance;
        private object _lastCatalogScene;
        private object _zNetInstance;

        private void Awake()
        {
            _toggleKey = Config.Bind("Hotkeys", "ToggleHUD", KeyCode.F6, "Open or close the ValheimSpawner HUD.");
            _spawnFrontKey = Config.Bind("Hotkeys", "SpawnInFront", KeyCode.F7, "Spawn the selected creature in front of the player.");
            _spawnAimKey = Config.Bind("Hotkeys", "SpawnAtCrosshair", KeyCode.F8, "Spawn the selected creature at the crosshair target.");
            _defaultCount = Config.Bind("Spawner", "DefaultCreatureCount", 1, "Initial creature count (1-50; default 1).");
            _defaultLevel = Config.Bind("Spawner", "DefaultCreatureLevel", 1, "Creature level: 1 = no stars, 2 = one star, 3 = two stars.");
            _languageSetting = Config.Bind("General", "DisplayLanguage", "Russian", "HUD language: Russian (default) or English; the selection is saved.");
            _killRadius = Config.Bind("Safety", "KillNearbyRadius", 30f, "Kill nearby radius in metres; clamped to 5-100. Players are never targeted; tamed creatures and bosses are excluded by default.");
            _spawnMarkerConfig = Config.Bind("Preview", "WorldMarkersEnabled", false, "Show local-only 3D creature spawn markers while the HUD's Creatures tab is open.");
            _spawnMarkerCrosshairConfig = Config.Bind("Preview", "MarkersAtCrosshair", false, "Preview 3D spawn markers at the crosshair rather than in front of the player.");
            _favoriteItemsConfig = Config.Bind("Catalogue", "FavoriteItems", string.Empty, "Saved item prefab IDs (encoded, managed by the HUD).");
            _favoriteCreaturesConfig = Config.Bind("Catalogue", "FavoriteCreatures", string.Empty, "Saved creature prefab IDs (encoded, managed by the HUD).");
            _hudPositionXConfig = Config.Bind("Interface", "HudOffsetX", 0f, "Saved horizontal offset for the movable Nexus workspace.");
            _hudPositionYConfig = Config.Bind("Interface", "HudOffsetY", 0f, "Saved vertical offset for the movable Nexus workspace.");
            _favoriteItems = FavoriteIds.FromEncodedString(_favoriteItemsConfig.Value);
            _favoriteCreatures = FavoriteIds.FromEncodedString(_favoriteCreaturesConfig.Value);
            InitializeNexusAdvanced();
            _isRussian = string.Equals(_languageSetting.Value, "Russian", StringComparison.OrdinalIgnoreCase);
            _count = Mathf.Clamp(_defaultCount.Value, 1, 50);
            _level = Mathf.Clamp(_defaultLevel.Value, 1, 3);
            _itemCount = 1;
            _itemCountInput = "1";
            _itemQuality = 1;

            _characterType = FindType("Character");
            _zNetSceneType = FindType("ZNetScene");
            _objectDbType = FindType("ObjectDB");
            _itemDropType = FindType("ItemDrop");
            _zNetType = FindType("ZNet");
            _playerType = FindType("Player");
            _zNetViewType = FindType("ZNetView");
            _activeInstance = this;
            InstallInputGuards();
            InstallNetworkPatches();
            InstallWorldAdminPatches();
            InstallAdminPatches();
            InstallAdvancedAdminPatches();
            InstallCustomItemTooltipPatch();

            Logger.LogInfo("ValheimSpawner loaded. Press " + _toggleKey.Value + " to open HUD.");
            Logger.LogInfo("Creature and item prefabs are discovered from the active game database at runtime.");
        }

        private void Update()
        {
            RefreshGameReferences();
            TickNetwork();
            TickNativeHud();
            if (_toggleKey != null && _toggleKey.Value != KeyCode.None && Input.GetKeyDown(_toggleKey.Value))
                SetOpen(!_isOpen);

            if (HasAdminAccess && _adminHotkeys && !_isOpen && _playerComponent != null && !OtherTextInputActive())
            {
                if (Input.GetKeyDown(KeyCode.Z)) SetAdminFly(!_adminFly);
                if (Input.GetKeyDown(KeyCode.B)) SetAdminCreative(!_adminCreative);
            }
            if (_playerComponent != null && Time.unscaledTime >= _nextAdminRefresh)
            {
                _nextAdminRefresh = Time.unscaledTime + 0.1f;
                ApplyAdminVitals();
                UpdateAdminAdrenaline();
            }
            if (_isOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    SetOpen(false);
                    return;
                }

                if (_nativeMode == 0 && !_legacyToolsOpen && (_mainTab == 0 || _mainTab == 6 && _catalogSelectionKind == 1) && !_searchHasFocus && !_nameHasFocus && !OtherTextInputActive() && _spawnFrontKey.Value != KeyCode.None && Input.GetKeyDown(_spawnFrontKey.Value))
                    SpawnSelected(false);
                if (_nativeMode == 0 && !_legacyToolsOpen && (_mainTab == 0 || _mainTab == 6 && _catalogSelectionKind == 1) && !_searchHasFocus && !_nameHasFocus && !OtherTextInputActive() && _spawnAimKey.Value != KeyCode.None && Input.GetKeyDown(_spawnAimKey.Value))
                    SpawnSelected(true);
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 4f;
                RefreshGameReferences();
                if (!_catalogReady && _zNetSceneInstance != null)
                    RefreshCatalogue();
                if (!_itemCatalogReady && _objectDbInstance != null)
                    RefreshItemCatalogue();
            }
        }

        private void LateUpdate()
        {
            UpdateWorldSpawnMarkers();
            // Valheim's camera may recapture the mouse after Update. Keep it released while our HUD is open.
            if (_isOpen)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void OnGUI()
        {
            if (!_isOpen || !_legacyToolsOpen)
            {
                DrawAdminToast();
                return;
            }
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            float maxWidth = Mathf.Max(320f, Screen.width - 20f);
            float maxHeight = Mathf.Max(260f, Screen.height - 20f);
            _windowRect.width = Mathf.Min(980f, maxWidth);
            // Doctor keeps the taller report view; day-to-day tabs use a tighter layout.
            _windowRect.height = Mathf.Min(_mainTab == 7 ? 820f : 760f, maxHeight);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));
            _windowRect = GUI.Window(731906, _windowRect, DrawWindow, "NEXUS ADMIN TOOLS");
            DrawAdminToast();
        }

        private void DrawAdminToast()
        {
            if (string.IsNullOrEmpty(_adminToast) || Time.unscaledTime >= _adminToastUntil) return;
            Rect rect = new Rect(Mathf.Max(10f, (Screen.width - 380f) * 0.5f), 55f, 380f, 38f);
            GUI.Box(rect, _adminToast);
        }

        private void DrawWindow(int windowId)
        {
            if (_pendingActions.Values.Any(p => p.Kind != AdminAction.Poll))
            {
                GUILayout.Label(T("Waiting for the server. Escape closes the window.", "Ожидание сервера. Escape закрывает окно."));
                return;
            }
            GUILayout.BeginVertical();
            // Global actions occupy their own row, leaving consistent room for category tabs.
            GUILayout.BeginHorizontal();
            GUILayout.Label("SUPERNOVA  /  " + T("Tools", "Инструменты"), GUILayout.Width(260f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(T("Wiki ↗", "Вики ↗"), GUILayout.Width(76f), GUILayout.Height(26f)))
            {
                try
                {
                    Application.OpenURL("https://valheim.fandom.com/wiki/Valheim_Wiki");
                    _status = T("Opening Valheim Wiki in your browser.", "Открываю Valheim Wiki в браузере.");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Could not open Valheim Wiki: " + ex.Message);
                    _status = T("Could not open the wiki link. Open it manually: valheim.fandom.com/wiki/Valheim_Wiki", "Не удалось открыть ссылку. Открой вручную: valheim.fandom.com/wiki/Valheim_Wiki");
                }
            }
            if (GUILayout.Button(_isRussian ? "RU / EN" : "EN / RU", GUILayout.Width(82f), GUILayout.Height(26f)))
            {
                _isRussian = !_isRussian;
                // Persist language across launches; English remains the default for new installs.
                _languageSetting.Value = _isRussian ? "Russian" : "English";
                TrySavePreferences();
                _status = T("Language changed to English.", "Язык изменён на русский.");
            }
            if (GUILayout.Button(T("Back", "Назад"), GUILayout.Width(115f), GUILayout.Height(26f))) { _legacyToolsOpen = false; _mainTab = 0; RebuildNativeHud(); }
            GUILayout.EndHorizontal();
            DrawNexusAdvancedTabs();
            DrawNexusAdvancedPanel();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 24f));
        }

        private void DrawMainTabButton(string title, int tab)
        {
            bool selected = _mainTab == tab;
            if (GUILayout.Button(selected ? "[ " + title + " ]" : title, GUILayout.ExpandWidth(true), GUILayout.Height(30f)))
            {
                // Do not carry a large quantity from one item category into another.
                // This makes switching from bulk materials to a different item tab start at 1.
                if (_mainTab != tab && tab > 0 && _mainTab > 0)
                    ResetItemAmountAfterSuccessfulAdd();

                _mainTab = tab;
                if (_mainTab > 0 && _mainTab < 6 && !_searchAllItems && _selectedItem != null && _selectedItem.Category != GetItemCategoryForTab(_mainTab)) _selectedItem = null;
                _searchHasFocus = false;
                _nameHasFocus = false;
                GUI.FocusControl(string.Empty);
                if (_mainTab > 0 && _mainTab < 6 && !_itemCatalogReady && _objectDbInstance != null)
                    RefreshItemCatalogue();
            }
        }

        private void DrawCreaturePanel()
        {
            GUILayout.Label(T("Search by English name, Russian name, localized name or prefab ID.", "Поиск по английскому и русскому названию, локализованному имени или prefab ID."));

            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Search:", "Поиск:"), GUILayout.Width(62f));
            GUI.SetNextControlName("ValheimSpawner.CreatureSearch");
            _search = GUILayout.TextField(_search, GUILayout.MinWidth(220f), GUILayout.ExpandWidth(true));
            _searchHasFocus = GUI.GetNameOfFocusedControl() == "ValheimSpawner.CreatureSearch";
            if (GUILayout.Button(T("Clear", "Очистить"), GUILayout.Width(80f)))
            {
                _search = string.Empty;
                GUI.FocusControl("ValheimSpawner.CreatureSearch");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_creatureFavoritesOnly ? T("★ Favorites only", "★ Только избранное") : T("☆ Favorites only", "☆ Только избранное"), GUILayout.Width(178f)))
                _creatureFavoritesOnly = !_creatureFavoritesOnly;
            if (GUILayout.Button(T("Refresh catalogues", "Обновить каталоги"), GUILayout.Width(175f)))
                RefreshAvailableCatalogues();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawCategoryButton(T("All", "Все"), 0);
            DrawCategoryButton(T("Regular", "Обычные"), 1);
            DrawCategoryButton(T("Bosses", "Боссы"), 2);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Biome:", "Биом:"), GUILayout.Width(58f));
            if (GUILayout.Button("◀", GUILayout.Width(34f))) _biomeFilter = (_biomeFilter - 1 + _biomeOptions.Length) % _biomeOptions.Length;
            GUILayout.Label(T(_biomeOptions[_biomeFilter], TranslateBiome(_biomeOptions[_biomeFilter])), GUILayout.Width(210f));
            if (GUILayout.Button("▶", GUILayout.Width(34f))) _biomeFilter = (_biomeFilter + 1) % _biomeOptions.Length;
            GUILayout.Label(T("Found: ", "Найдено: ") + BuildFilteredList(), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            _spawnTamed = GUILayout.Toggle(_spawnTamed, T("Spawn tamed (where supported)", "Призывать приручёнными (если поддерживается)"));
            GUILayout.Space(5f);
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            _filtered.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                CreatureEntry entry = _entries[i];
                if (!MatchesFilters(entry)) continue;
                _filtered.Add(entry);
                GUILayout.BeginHorizontal("box");
                bool isFavorite = _favoriteCreatures.Contains(entry.PrefabName);
                if (GUILayout.Button(isFavorite ? "★" : "☆", GUILayout.Width(34f), GUILayout.Height(27f)))
                    ToggleCreatureFavorite(entry.PrefabName);
                string label = GetCreatureDisplayName(entry) + "  ·  " + entry.PrefabName;
                if (GUILayout.Button(label, GUILayout.MinWidth(240f), GUILayout.ExpandWidth(true))) _selected = entry;
                GUILayout.Label(entry.Category == CreatureCategory.Boss ? T("Boss", "Босс") : T("Regular", "Обычное"), GUILayout.Width(90f));
                GUILayout.Label(T(entry.BiomeLabel, TranslateBiomeList(entry.Biomes)), GUILayout.Width(180f));
                GUILayout.EndHorizontal();
                if (_selected == entry) GUILayout.Label(T("Prefab: ", "Prefab ID: ") + entry.PrefabName);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(5f);
            GUILayout.BeginVertical("box");
            if (_selected == null)
                GUILayout.Label(T("Select a creature from the list above.", "Выбери существо из списка выше."));
            else
            {
                GUILayout.Label(T("Selected: ", "Выбрано: ") + GetCreatureDisplayName(_selected));
                GUILayout.Label("Prefab ID: " + _selected.PrefabName);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Custom name (optional):", "Своё имя (необязательно):"), GUILayout.Width(205f));
            GUI.SetNextControlName("ValheimSpawner.CustomName");
            _customName = GUILayout.TextField(_customName, 48, GUILayout.MinWidth(220f), GUILayout.ExpandWidth(true));
            _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimSpawner.CustomName";
            if (GUILayout.Button(T("Clear", "Очистить"), GUILayout.Width(80f)))
            {
                _customName = string.Empty;
                GUI.FocusControl("ValheimSpawner.CustomName");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Count:", "Количество:"), GUILayout.Width(98f));
            if (GUILayout.Button("−", GUILayout.Width(34f))) _count = Mathf.Max(1, _count - 1);
            GUILayout.Label(_count.ToString(), GUILayout.Width(40f));
            if (GUILayout.Button("+", GUILayout.Width(34f))) _count = Mathf.Min(50, _count + 1);
            GUILayout.Space(18f);
            GUILayout.Label(T("Level / stars:", "Уровень / звёзды:"), GUILayout.Width(140f));
            if (GUILayout.Button("−", GUILayout.Width(34f))) _level = Mathf.Max(1, _level - 1);
            GUILayout.Label(_level + "  (" + (_level - 1) + "★)", GUILayout.Width(55f));
            if (GUILayout.Button("+", GUILayout.Width(34f))) _level = Mathf.Min(3, _level + 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = _selected != null;
            if (GUILayout.Button(T("Spawn in front", "Создать перед собой") + "  [" + _spawnFrontKey.Value + "]", GUILayout.Height(34f))) SpawnSelected(false);
            if (GUILayout.Button(T("Spawn at crosshair", "Создать под прицелом") + "  [" + _spawnAimKey.Value + "]", GUILayout.Height(34f))) SpawnSelected(true);
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            DrawSpawnPreview();
            GUILayout.Space(4f);
            // Destructive controls belong in a collapsed section below the safe spawn actions.
            _showCreatureSafety = GUILayout.Toggle(_showCreatureSafety,
                T("Show cleanup / destructive commands", "Показать очистку / опасные команды"));
            if (_showCreatureSafety)
            {
            GUILayout.BeginVertical("box");
            GUILayout.Label(T("Kill nearby radius: ", "Радиус уничтожения: ") + GetKillRadius().ToString("0") + " m");
            _killIncludeTamed = GUILayout.Toggle(_killIncludeTamed,
                T("Include tamed creatures (unsafe)", "Включить приручённых существ (опасно)"));
            _killIncludeBosses = GUILayout.Toggle(_killIncludeBosses,
                T("Include bosses (unsafe)", "Включить боссов (опасно)"));
            Color oldBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.62f, 0.24f, 0.20f, 1f);
            if (GUILayout.Button(T("Kill all nearby creatures  [killall]", "Убить всех существ поблизости  [killall]"), GUILayout.Height(32f)))
                KillNearbyCreatures();
            GUI.backgroundColor = oldBackground;
            GUILayout.EndVertical(); // Cleanup box.
            }
            GUILayout.EndVertical(); // Spawn controls.

            GUILayout.Label(_status, GUILayout.ExpandWidth(true));
            GUILayout.Label(T("F6: close HUD  ·  F7: spawn in front  ·  F8: spawn at crosshair. Player controls are disabled while the HUD is open.",
                              "F6: закрыть HUD  ·  F7: создать перед собой  ·  F8: создать под прицелом. Управление персонажем отключено, пока открыт HUD."));
        }

        private void DrawItemPanel(ItemCategory category)
        {
            GUILayout.Label(T("The list is read from the active ObjectDB. Items are added directly to your inventory.", "Список загружается из ObjectDB активной игры. Предметы добавляются прямо в инвентарь."));
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Search:", "Поиск:"), GUILayout.Width(62f));
            GUI.SetNextControlName("ValheimSpawner.ItemSearch");
            _itemSearch = GUILayout.TextField(_itemSearch, GUILayout.MinWidth(220f), GUILayout.ExpandWidth(true));
            _searchHasFocus = GUI.GetNameOfFocusedControl() == "ValheimSpawner.ItemSearch";
            if (GUILayout.Button(T("Clear", "Очистить"), GUILayout.Width(80f)))
            {
                _itemSearch = string.Empty;
                GUI.FocusControl("ValheimSpawner.ItemSearch");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_itemFavoritesOnly ? T("★ Favorites only", "★ Только избранное") : T("☆ Favorites only", "☆ Только избранное"), GUILayout.Width(178f)))
                _itemFavoritesOnly = !_itemFavoritesOnly;
            if (GUILayout.Button(_searchAllItems ? T("Global search: ON", "Общий поиск: ВКЛ") : T("Global search: OFF", "Общий поиск: ВЫКЛ"), GUILayout.Width(178f)))
            {
                _searchAllItems = !_searchAllItems;
                _itemListScroll = Vector2.zero;
            }
            if (GUILayout.Button(T("Refresh catalogues", "Обновить каталоги"), GUILayout.Width(175f)))
                RefreshAvailableCatalogues();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!_searchAllItems) DrawItemTypeFilters(category);
            else if (string.IsNullOrWhiteSpace(_itemSearch) && !_itemFavoritesOnly)
                GUILayout.Label(T("Enter a search term to search every item category.", "Введи запрос для поиска по всем категориям предметов."));
            if (_selectedItem != null && !IsItemVisibleInPanel(_selectedItem, category))
                _selectedItem = null;
            GUILayout.Label(T("Found: ", "Найдено: ") + CountFilteredItems(category));

            _itemListScroll = GUILayout.BeginScrollView(_itemListScroll, GUILayout.ExpandHeight(true));
            _filteredItems.Clear();
            for (int i = 0; i < _itemEntries.Count; i++)
            {
                ItemEntry entry = _itemEntries[i];
                if (!IsItemVisibleInPanel(entry, category)) continue;
                _filteredItems.Add(entry);
                GUILayout.BeginHorizontal("box");
                bool isFavorite = _favoriteItems.Contains(entry.PrefabName);
                if (GUILayout.Button(isFavorite ? "★" : "☆", GUILayout.Width(34f), GUILayout.Height(30f)))
                    ToggleItemFavorite(entry.PrefabName);
                DrawItemIcon(entry);
                if (GUILayout.Button(GetItemDisplayName(entry) + "  ·  " + GetVisiblePrefabId(entry.PrefabName), GUILayout.MinWidth(200f), GUILayout.ExpandWidth(true), GUILayout.Height(30f)))
                {
                    if (!ReferenceEquals(_selectedItem, entry)) _itemCustomName = string.Empty;
                    _selectedItem = entry;
                }
                GUILayout.Label(_searchAllItems ? GetCategoryDisplay(entry.Category) : GetItemTypeDisplay(entry.ItemTypeLabel), GUILayout.Width(135f));
                GUILayout.EndHorizontal();
                if (_selectedItem == entry) GUILayout.Label(T("Prefab ID: ", "Prefab ID: ") + entry.PrefabName);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginVertical("box");
            if (_selectedItem == null || !IsItemVisibleInPanel(_selectedItem, category))
            {
                GUILayout.Label(T("Select an item from the list above.", "Выбери предмет из списка выше."));
            }
            else
            {
                GUILayout.Label(T("Selected: ", "Выбрано: ") + GetItemDisplayName(_selectedItem));
                _showItemTechnicalDetails = GUILayout.Toggle(_showItemTechnicalDetails,
                    T("Show technical item details", "Показать технические данные"));
                if (_showItemTechnicalDetails)
                {
                GUILayout.Label("Prefab ID: " + GetVisiblePrefabId(_selectedItem.PrefabName));
                GUILayout.Label(T("Category: ", "Категория: ") + GetCategoryDisplay(_selectedItem.Category) +
                    "  ·  " + T("Type: ", "Тип: ") + GetItemTypeDisplay(_selectedItem.ItemTypeLabel));
                GUILayout.Label(T("Max stack: ", "Макс. стак: ") + _selectedItem.MaxStack +
                    "  ·  " + T("Max selectable quality: ", "Макс. выбранное качество: ") + _selectedItem.MaxQuality +
                    "  ·  " + (_selectedItem.UsesDurability ? T("Uses durability", "Использует прочность") : T("No durability", "Без прочности")) +
                    "  ·  " + (_selectedItem.IconTexture == null ? T("No drawable icon", "Нет отображаемой иконки") : T("Icon available", "Иконка доступна")));
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label(T("Amount:", "Количество:"), GUILayout.Width(100f));
                if (GUILayout.Button("−", GUILayout.Width(34f)))
                {
                    _itemCount = Mathf.Max(1, _itemCount - 1);
                    _itemCountInput = _itemCount.ToString();
                }
                GUI.SetNextControlName("ValheimSpawner.ItemCount");
                string enteredCount = GUILayout.TextField(_itemCountInput, 6, GUILayout.Width(78f));
                if (enteredCount != _itemCountInput)
                {
                    _itemCountInput = enteredCount;
                    int parsedCount;
                    if (TryParseItemAmount(_itemCountInput, out parsedCount))
                        _itemCount = parsedCount;
                }
                if (GUILayout.Button("+", GUILayout.Width(34f)))
                {
                    _itemCount = Mathf.Min(9999, _itemCount + 1);
                    _itemCountInput = _itemCount.ToString();
                }
                int verifiedAmount;
                bool amountValid = TryParseItemAmount(_itemCountInput, out verifiedAmount);
                if (!amountValid)
                    GUILayout.Label(T("Enter 1–9999", "Введи 1–9999"));
                GUILayout.Space(20f);
                // Clamp the shared quality value when the selected item changes to a type
                // with a lower supported limit.
                _itemQuality = Mathf.Clamp(_itemQuality, 1, Mathf.Max(1, _selectedItem.MaxQuality));
                GUILayout.Label(T("Quality:", "Качество:"), GUILayout.Width(80f));
                if (GUILayout.Button("−", GUILayout.Width(34f))) _itemQuality = Mathf.Max(1, _itemQuality - 1);
                GUILayout.Label(_itemQuality.ToString(), GUILayout.Width(34f));
                if (GUILayout.Button("+", GUILayout.Width(34f))) _itemQuality = Mathf.Min(Mathf.Max(1, _selectedItem.MaxQuality), _itemQuality + 1);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(T("Custom name:", "Своё имя:"), GUILayout.Width(100f));
                GUI.SetNextControlName("ValheimSpawner.ItemCustomName");
                _itemCustomName = GUILayout.TextField(_itemCustomName, 48, GUILayout.ExpandWidth(true));
                _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimSpawner.ItemCustomName";
                if (GUILayout.Button(T("Clear", "Очистить"), GUILayout.Width(80f))) _itemCustomName = string.Empty;
                GUILayout.EndHorizontal();
                _showItemCustomFields = GUILayout.Toggle(_showItemCustomFields,
                    T("Custom description", "Своё описание"));
                if (_showItemCustomFields)
                {
                GUILayout.Label(T("Custom description (replaces item description):", "Своё описание (заменяет описание предмета):"));
                GUI.SetNextControlName("ValheimSpawner.ItemCustomDescription");
                _itemCustomDescription = GUILayout.TextArea(_itemCustomDescription, 480, GUILayout.MinHeight(70f));
                _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimSpawner.ItemCustomDescription";
                if (!string.IsNullOrWhiteSpace(_itemCustomDescription) &&
                    GUILayout.Button(T("Clear description", "Очистить описание"), GUILayout.Width(160f)))
                    _itemCustomDescription = string.Empty;
                }
                if ((!string.IsNullOrWhiteSpace(_itemCustomName) || !string.IsNullOrWhiteSpace(_itemCustomDescription)) && _selectedItem.MaxStack > 1)
                    GUILayout.Label(T("Custom names/descriptions need max stack 1 to prevent merging and data loss.",
                        "Свои имена/описания требуют стака 1, чтобы предотвратить объединение и потерю данных."));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("Add to kit", "В набор"), GUILayout.Height(30f))) AddSelectedItemToKit();
                GUI.enabled = _playerComponent != null && amountValid &&
                    ((string.IsNullOrWhiteSpace(_itemCustomName) && string.IsNullOrWhiteSpace(_itemCustomDescription)) || _selectedItem.MaxStack == 1);
                if (GUILayout.Button(T("Add to inventory", "Добавить в инвентарь"), GUILayout.Height(36f)))
                {
                    _itemCount = verifiedAmount;
                    AddSelectedItemToInventory();
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.Label(_status, GUILayout.ExpandWidth(true));
            GUILayout.Label(T("Selected items are added directly to your inventory. Use the Creatures tab to spawn creatures into the world.",
                              "Выбранные предметы добавляются прямо в инвентарь. Для создания существ в мире используй вкладку «Существа»."));
        }

        private ItemCategory GetItemCategoryForTab(int tab)
        {
            switch (tab)
            {
                case 1: return ItemCategory.Weapons;
                case 2: return ItemCategory.Armor;
                case 3: return ItemCategory.Food;
                case 4: return ItemCategory.Materials;
                default: return ItemCategory.Other;
            }
        }

        private void DrawItemTypeFilters(ItemCategory category)
        {
            string selectedType;
            if (!_itemTypeFilters.TryGetValue(category, out selectedType)) selectedType = string.Empty;
            List<string> types = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _itemEntries.Count; i++)
            {
                ItemEntry entry = _itemEntries[i];
                if (entry.Category != category || string.IsNullOrWhiteSpace(entry.ItemTypeLabel)) continue;
                if (seen.Add(entry.ItemTypeLabel)) types.Add(entry.ItemTypeLabel);
            }
            types.Sort(StringComparer.CurrentCultureIgnoreCase);

            GUILayout.Label(T("Filter by type:", "Фильтр по типу:"));
            int columns = Mathf.Clamp(Mathf.FloorToInt((_windowRect.width - 45f) / 145f), 3, 6);
            int inRow = 0;
            GUILayout.BeginHorizontal();
            DrawItemTypeFilterButton(T("All types", "Все типы"), string.Empty, category, selectedType);
            inRow++;
            for (int i = 0; i < types.Count; i++)
            {
                if (inRow >= columns)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    inRow = 0;
                }
                DrawItemTypeFilterButton(GetItemTypeDisplay(types[i]), types[i], category, selectedType);
                inRow++;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawItemTypeFilterButton(string label, string value, ItemCategory category, string selectedType)
        {
            bool selected = string.Equals(value ?? string.Empty, selectedType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (GUILayout.Button(selected ? "[ " + label + " ]" : label, GUILayout.ExpandWidth(true), GUILayout.Height(27f)))
                _itemTypeFilters[category] = value ?? string.Empty;
        }

        private string GetCategoryDisplay(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Weapons: return T("Weapons", "Оружие");
                case ItemCategory.Armor: return T("Armor", "Броня");
                case ItemCategory.Food: return T("Food", "Еда");
                case ItemCategory.Materials: return T("Materials", "Материалы");
                default: return T("Other", "Прочее");
            }
        }

        // Draw the existing Sprite's UV region. Do not allocate a new Texture2D every GUI event.
        // Some modded icons use unsupported packed layouts; draw a placeholder in that case.
        private static void DrawItemIcon(ItemEntry entry)
        {
            Rect rect = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
            if (entry == null || entry.IconTexture == null)
            {
                GUI.Label(rect, "·");
                return;
            }
            try { GUI.DrawTextureWithTexCoords(rect, entry.IconTexture, entry.IconUv, true); }
            catch (Exception) { GUI.Label(rect, "·"); }
        }

        private void ToggleItemFavorite(string prefabId)
        {
            _favoriteItems.Toggle(prefabId);
            _favoriteItemsConfig.Value = _favoriteItems.ToEncodedString();
            TrySavePreferences();
        }

        private void ToggleCreatureFavorite(string prefabId)
        {
            _favoriteCreatures.Toggle(prefabId);
            _favoriteCreaturesConfig.Value = _favoriteCreatures.ToEncodedString();
            TrySavePreferences();
        }

        private void TrySavePreferences()
        {
            try { Config.Save(); }
            catch (Exception ex)
            {
                Logger.LogWarning("Nexus preferences could not be saved: " + ex.Message);
                _status = T("Could not save preferences. Check BepInEx log.",
                    "Не удалось сохранить настройки. Проверь журнал BepInEx.");
            }
        }

        private void RefreshAvailableCatalogues()
        {
            RefreshGameReferences();
            if (_zNetSceneInstance != null) RefreshCatalogue();
            if (_objectDbInstance != null) RefreshItemCatalogue();
            string details = _entries.Count + T(" creatures, ", " существ, ") + _itemEntries.Count + T(" items.", " предметов.");
            if (_zNetSceneInstance == null || _objectDbInstance == null)
                _status = T("Catalogue refresh incomplete: ", "Обновление каталогов неполное: ") + details +
                    T(" Some game databases are unavailable.", " Некоторые игровые базы недоступны.");
            else if (!_catalogReady || !_itemCatalogReady)
                _status = T("Catalogue refresh incomplete: ", "Обновление каталогов неполное: ") + details +
                    T(" See BepInEx logs.", " Проверь логи BepInEx.");
            else
                _status = T("Catalogues refreshed: ", "Каталоги обновлены: ") + details;
        }

        private string T(string english, string russian) { return _isRussian ? russian : english; }

        private string GetCreatureDisplayName(CreatureEntry entry)
        {
            if (entry == null) return string.Empty;
            if (_isRussian && !string.IsNullOrWhiteSpace(entry.RussianName)) return entry.RussianName;
            if (!string.IsNullOrWhiteSpace(entry.EnglishName)) return GetEnglishSafeName(entry.EnglishName, entry.PrefabName);
            return HumanizePrefabName(entry.PrefabName);
        }

        private static string TranslateBiome(string biome)
        {
            switch (biome)
            {
                case "All Biomes": return "Все биомы";
                case "Meadows": return "Луга";
                case "Black Forest": return "Чёрный лес";
                case "Swamp": return "Болото";
                case "Mountains": return "Горы";
                case "Plains": return "Равнины";
                case "Ocean": return "Океан";
                case "Rivers": return "Реки";
                case "Mistlands": return "Туманные земли";
                case "Ashlands": return "Пепельные земли";
                case "Deep North": return "Дальний Север";
                case "Mountain Caves": return "Горные пещеры";
                case "Any Biome": return "Любой биом";
                default: return "Неизвестно";
            }
        }

        private string TranslateBiomeList(string[] biomes)
        {
            if (biomes == null || biomes.Length == 0) return T("Unknown", "Неизвестно");
            string[] translated = new string[biomes.Length];
            for (int i = 0; i < biomes.Length; i++) translated[i] = TranslateBiome(biomes[i]);
            return string.Join(", ", translated);
        }

        private string GetVisiblePrefabId(string prefabName)
        {
            if (!_isRussian && ContainsCyrillic(prefabName)) return HumanizePrefabName(prefabName);
            return prefabName ?? string.Empty;
        }

        private string GetItemDisplayName(ItemEntry entry)
        {
            if (entry == null) return string.Empty;
            if (!_isRussian) return entry.EnglishName;

            string russianName;
            if (RussianItemNames.TryGetValue(entry.PrefabName, out russianName))
                return russianName;

            // Use the game's active Russian localization for names not covered by the
            // explicit vanilla weapon/shield dictionary.
            if (!string.IsNullOrWhiteSpace(entry.LocalizedName) && ContainsCyrillic(entry.LocalizedName))
                return entry.LocalizedName;

            return entry.EnglishName;
        }

        private static string GetEnglishSafeName(string localizedName, string prefabName)
        {
            string candidate = (localizedName ?? string.Empty).Trim();
            bool isPrefabFallback = !string.IsNullOrWhiteSpace(candidate) &&
                string.Equals(candidate, prefabName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(candidate) && candidate[0] != '$' && !ContainsCyrillic(candidate) && !isPrefabFallback)
                return candidate;
            return HumanizePrefabName(prefabName);
        }

        private static bool ContainsCyrillic(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= '\u0400' && c <= '\u052F') || (c >= '\u2DE0' && c <= '\u2DFF') || (c >= '\uA640' && c <= '\uA69F'))
                    return true;
            }
            return false;
        }

        // When the game is running in Russian, its active localization returns Russian item
        // names. English HUD mode falls back to the English-style prefab identifier rather
        // than leaking Cyrillic text into the item list.
        private static string HumanizePrefabName(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName)) return "Unknown Item";
            StringBuilder result = new StringBuilder(prefabName.Length + 12);
            for (int i = 0; i < prefabName.Length; i++)
            {
                char c = prefabName[i];
                if (c == '_' || c == '-' || char.IsWhiteSpace(c))
                {
                    if (result.Length > 0 && result[result.Length - 1] != ' ') result.Append(' ');
                    continue;
                }
                if ((c >= '\u0400' && c <= '\u052F') || (c >= '\u2DE0' && c <= '\u2DFF') || (c >= '\uA640' && c <= '\uA69F'))
                {
                    if (result.Length > 0 && result[result.Length - 1] != ' ') result.Append(' ');
                    continue;
                }
                bool previousIsLetterOrDigit = i > 0 && char.IsLetterOrDigit(prefabName[i - 1]);
                bool previousIsLowerOrDigit = i > 0 && (char.IsLower(prefabName[i - 1]) || char.IsDigit(prefabName[i - 1]));
                bool currentIsUpper = char.IsUpper(c);
                bool acronymBoundary = i > 0 && i + 1 < prefabName.Length && char.IsUpper(prefabName[i - 1]) && currentIsUpper && char.IsLower(prefabName[i + 1]);
                if (result.Length > 0 && result[result.Length - 1] != ' ' && previousIsLetterOrDigit && ((previousIsLowerOrDigit && currentIsUpper) || acronymBoundary))
                    result.Append(' ');
                result.Append(c);
            }
            string humanized = result.ToString().Trim();
            while (humanized.Contains("  ")) humanized = humanized.Replace("  ", " ");
            if (humanized.Length == 0) return "Unknown Item";
            // Capitalize each identifier token for readable English fallback names.
            string[] parts = humanized.Split(' ');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }

        private string GetItemTypeDisplay(string itemType)
        {
            switch ((itemType ?? string.Empty).Trim())
            {
                // Weapon-type filters shown in the Weapons tab.
                case "Ammo": return T("Ammo", "Боеприпасы");
                case "AmmoNonEquipable": return T("Ammunition", "Боеприпасы");
                case "Bow": return T("Bow", "Лук");
                case "OneHandedWeapon": return T("One-handed weapon", "Одноручное оружие");
                case "TwoHandedWeapon": return T("Two-handed weapon", "Двуручное оружие");
                case "TwoHandedWeaponLeft": return T("Two-handed weapon (left hand)", "Двуручное оружие (левая рука)");
                case "Torch": return T("Torch", "Факел");
                case "Attach_Atgeir": return T("Atgeir", "Алебарда");
                case "Weapon": return T("Weapon", "Оружие");
                case "Helmet": return T("Helmet", "Шлем");
                case "Chest": return T("Chest armor", "Нагрудник");
                case "Legs": return T("Leg armor", "Штаны");
                case "Hands": return T("Gloves", "Перчатки");
                case "Shoulder": return T("Cape", "Плащ");
                case "Utility": return T("Accessory", "Аксессуар");
                case "Trinket":
                case "Trinkets": return T("Trinkets", "Обереги");
                case "Shield": return T("Shield", "Щит");
                case "Tool": return T("Tool", "Инструмент");
                case "Consumable": return T("Consumable", "Расходуемое");
                case "Fish": return T("Fish", "Рыба");
                case "Misc": return T("Miscellaneous", "Разное");
                case "None": return T("Unspecified", "Не указано");
                case "Customization": return T("Customization", "Внешний вид");
                case "Material": return T("Material", "Материал");
                case "Trophie":
                case "Trophy": return T("Trophy", "Трофей");
                default: return string.IsNullOrEmpty(itemType) ? T("Other", "Прочее") : (!_isRussian && ContainsCyrillic(itemType) ? HumanizePrefabName(itemType) : itemType);
            }
        }

        private int CountFilteredItems(ItemCategory category)
        {
            int count = 0;
            for (int i = 0; i < _itemEntries.Count; i++)
                if (IsItemVisibleInPanel(_itemEntries[i], category)) count++;
            return count;
        }

        private bool IsItemVisibleInPanel(ItemEntry entry, ItemCategory category)
        {
            if (entry == null) return false;
            if (_itemFavoritesOnly && !_favoriteItems.Contains(entry.PrefabName)) return false;
            if (!_searchAllItems && entry.Category != category) return false;
            return _searchAllItems ? MatchesItemSearch(entry) : MatchesItemFilters(entry, category);
        }

        private bool MatchesItemFilters(ItemEntry entry, ItemCategory category)
        {
            if (!MatchesItemSearch(entry)) return false;
            string filter;
            if (!_itemTypeFilters.TryGetValue(category, out filter) || string.IsNullOrEmpty(filter)) return true;
            return string.Equals(entry.ItemTypeLabel, filter, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesItemSearch(ItemEntry entry)
        {
            if (entry == null) return false;
            if (string.IsNullOrWhiteSpace(_itemSearch)) return true;
            string query = _itemSearch.Trim();
            string russianAlias;
            RussianItemNames.TryGetValue(entry.PrefabName, out russianAlias);
            return ContainsIgnoreCase(entry.PrefabName, query) ||
                   ContainsIgnoreCase(entry.EnglishName, query) ||
                   ContainsIgnoreCase(entry.LocalizedName, query) ||
                   ContainsIgnoreCase(entry.ItemTypeLabel, query) ||
                   ContainsIgnoreCase(GetItemTypeDisplay(entry.ItemTypeLabel), query) ||
                   ContainsIgnoreCase(russianAlias, query);
        }

        private static bool TryParseItemAmount(string input, out int amount)
        {
            amount = 0;
            int parsed;
            if (string.IsNullOrWhiteSpace(input) ||
                !int.TryParse(input, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed)) return false;
            if (parsed < 1 || parsed > 9999) return false;
            amount = parsed;
            return true;
        }

        private void DrawCategoryButton(string title, int value)
        {
            bool selected = _categoryFilter == value;
            string shown = selected ? "[ " + title + " ]" : title;
            if (GUILayout.Button(shown, GUILayout.ExpandWidth(true)))
                _categoryFilter = value;
        }

        private bool MatchesFilters(CreatureEntry entry)
        {
            if (entry == null || (_creatureFavoritesOnly && !_favoriteCreatures.Contains(entry.PrefabName))) return false;
            if (_categoryFilter == 1 && entry.Category != CreatureCategory.Regular) return false;
            if (_categoryFilter == 2 && entry.Category != CreatureCategory.Boss) return false;

            string selectedBiome = _biomeOptions[_biomeFilter];
            if (selectedBiome != "All Biomes")
            {
                bool biomeMatch = false;
                for (int i = 0; i < entry.Biomes.Length; i++)
                {
                    if (entry.Biomes[i].Equals(selectedBiome, StringComparison.OrdinalIgnoreCase))
                    {
                        biomeMatch = true;
                        break;
                    }
                }
                if (!biomeMatch) return false;
            }

            if (string.IsNullOrWhiteSpace(_search)) return true;
            string query = _search.Trim();
            return ContainsIgnoreCase(entry.PrefabName, query) ||
                   ContainsIgnoreCase(entry.EnglishName, query) ||
                   ContainsIgnoreCase(entry.RussianName, query) ||
                   ContainsIgnoreCase(entry.LocalizedName, query);
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int BuildFilteredList()
        {
            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
                if (MatchesFilters(_entries[i])) count++;
            return count;
        }

        private void SetOpen(bool open)
        {
            if (_isOpen == open) return;
            if (open && !HasAdminAccess)
            {
                ShowAdminToast(T("Nexus requires server administrator access.", "Nexus доступен администраторам сервера."));
                return;
            }
            _isOpen = open;
            SetNativeHudOpen(open);
            _nameHasFocus = false;
            _searchHasFocus = false;
            if (open)
            {
                _cursorStateStored = true;
                _oldCursorVisible = Cursor.visible;
                _oldCursorLockState = Cursor.lockState;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));
                RefreshGameReferences();
                if (_zNetSceneInstance != null && !_catalogReady) RefreshCatalogue();
                if (_objectDbInstance != null && !_itemCatalogReady) RefreshItemCatalogue();
            }
            else
            {
                DestroyWorldSpawnMarkers();
                _searchHasFocus = false;
                _suppressInputUntil = Time.unscaledTime + 0.15f;
                if (_cursorStateStored)
                {
                    Cursor.visible = OtherGuiInputBlocked() || _oldCursorVisible;
                    Cursor.lockState = OtherGuiInputBlocked() ? CursorLockMode.None : _oldCursorLockState;
                    _cursorStateStored = false;
                }
            }
        }

        private void RefreshGameReferences()
        {
            if (_characterType == null) _characterType = FindType("Character");
            if (_zNetSceneType == null) _zNetSceneType = FindType("ZNetScene");
            if (_zNetType == null) _zNetType = FindType("ZNet");
            if (_playerType == null) _playerType = FindType("Player");
            if (_zNetViewType == null) _zNetViewType = FindType("ZNetView");
            if (_objectDbType == null) _objectDbType = FindType("ObjectDB");
            if (_itemDropType == null) _itemDropType = FindType("ItemDrop");
            _zNetSceneInstance = GetSingleton(_zNetSceneType, "instance");
            if (!ReferenceEquals(_zNetSceneInstance, _lastCatalogScene))
            {
                _lastCatalogScene = _zNetSceneInstance;
                _catalogReady = false;
                _entries.Clear();
                _selected = null;
            }
            _objectDbInstance = GetSingleton(_objectDbType, "instance");
            if (!ReferenceEquals(_objectDbInstance, _lastObjectDb))
            {
                _lastObjectDb = _objectDbInstance;
                _itemCatalogReady = false;
                _itemEntries.Clear();
                _selectedItem = null;
            }
            _zNetInstance = GetSingleton(_zNetType, "instance");
            object p = GetSingleton(_playerType, "m_localPlayer");
            _playerComponent = p as Component;
        }

        private void RefreshCatalogue()
        {
            if (_zNetSceneInstance == null || _characterType == null) return;
            try
            {
                List<GameObject> prefabs = ReadPrefabList(_zNetSceneInstance);
                if (prefabs.Count == 0) return;
                string selectedPrefabName = _selected == null ? null : _selected.PrefabName;

                Dictionary<string, CreatureEntry> byName = new Dictionary<string, CreatureEntry>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < prefabs.Count; i++)
                {
                    GameObject prefab = prefabs[i];
                    if (prefab == null) continue;

                    Component character = null;
                    try { character = prefab.GetComponent(_characterType); }
                    catch { }

                    CreatureMetadata meta;
                    bool hasMetadata = CreatureCatalog.TryGet(prefab.name, out meta);
                    if (character == null && !hasMetadata) continue;

                    string localName = character == null ? prefab.name : ReadLocalizedCharacterName(character, prefab.name);
                    string english = hasMetadata ? meta.English : prefab.name;
                    string russian = hasMetadata ? meta.Russian : localName;
                    CreatureCategory category = hasMetadata ? meta.Category : InferCategory(prefab.name);
                    string[] biomes = hasMetadata ? meta.Biomes : InferBiomes(prefab.name);
                    var entry = new CreatureEntry(prefab, prefab.name, english, russian, localName, category, biomes);
                    byName[prefab.name] = entry;
                }

                _entries.Clear();
                _entries.AddRange(byName.Values);
                _entries.Sort(delegate(CreatureEntry a, CreatureEntry b)
                {
                    int category = a.Category.CompareTo(b.Category);
                    if (category != 0) return category;
                    return string.Compare(a.EnglishName, b.EnglishName, StringComparison.CurrentCultureIgnoreCase);
                });

                _selected = null;
                if (!string.IsNullOrEmpty(selectedPrefabName))
                {
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        if (_entries[i].PrefabName.Equals(selectedPrefabName, StringComparison.OrdinalIgnoreCase))
                        {
                            _selected = _entries[i];
                            break;
                        }
                    }
                }
                if (_selected == null) _selected = _entries.Count > 0 ? _entries[0] : null;
                _catalogReady = _entries.Count > 0;
                _status = _catalogReady
                    ? T("Catalogue loaded: ", "Каталог существ загружен: ") + _entries.Count + T(" creatures. Spawning is available to the host/server only.", " существ. Создание доступно только хосту/серверу.")
                    : T("No creatures were found in the game prefabs. Check BepInEx/LogOutput.log.", "В игровых префабах не найдены существа. Проверь BepInEx/LogOutput.log.");
                Logger.LogInfo("Creature catalogue refreshed: " + _entries.Count + " character prefabs available.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not read the creature catalogue: " + ex);
                _status = T("Failed to load the creature catalogue. See BepInEx/LogOutput.log.", "Не удалось загрузить каталог существ. См. BepInEx/LogOutput.log.");
            }
        }

        private void RefreshItemCatalogue()
        {
            if (_objectDbInstance == null || _itemDropType == null) return;
            try
            {
                object collection = ReadMember(_objectDbInstance, "m_items");
                IEnumerable enumerable = collection as IEnumerable;
                if (enumerable == null) return;
                string selectedName = _selectedItem == null ? null : _selectedItem.PrefabName;
                Dictionary<string, ItemEntry> byName = new Dictionary<string, ItemEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (object obj in enumerable)
                {
                    GameObject prefab = obj as GameObject;
                    if (prefab == null) continue;
                    Component itemDrop = null;
                    try { itemDrop = prefab.GetComponent(_itemDropType); } catch { }
                    if (itemDrop == null) continue;
                    object itemData = ReadMember(itemDrop, "m_itemData");
                    object shared = ReadMember(itemData, "m_shared");
                    if (shared == null) continue;

                    string itemType = Convert.ToString(ReadMember(shared, "m_itemType")) ?? "Misc";
                    string rawName = ReadStringMember(shared, "m_name");
                    string localizedName = LocalizeName(rawName, prefab.name);
                    string englishName = GetEnglishSafeName(localizedName, prefab.name);
                    float food = ReadFloatMember(shared, "m_food");
                    ItemCategory category = ClassifyItem(itemType, food, prefab.name);
                    int declaredMaxQuality = Mathf.Clamp(ReadIntMember(shared, "m_maxQuality", 1), 1, ExtendedNonWeaponQualityCap);
                    int maxQuality = GetSelectableMaxQuality(category, itemType, prefab.name, declaredMaxQuality);
                    Sprite[] icons = ReadMember(shared, "m_icons") as Sprite[];
                    Sprite icon = icons != null && icons.Length > 0 ? icons[0] : null;
                    int maxStack = Mathf.Clamp(ReadIntMember(shared, "m_maxStackSize", 1), 1, 9999);
                    bool usesDurability = object.Equals(ReadMember(shared, "m_useDurability"), true);
                    ItemEntry entry = new ItemEntry(prefab, prefab.name, englishName, localizedName, itemType, category, maxQuality, maxStack, usesDurability, icon);
                    byName[prefab.name] = entry;
                }

                _itemEntries.Clear();
                _itemEntries.AddRange(byName.Values);
                _itemEntries.Sort(delegate(ItemEntry a, ItemEntry b)
                {
                    int category = a.Category.CompareTo(b.Category);
                    if (category != 0) return category;
                    return string.Compare(a.EnglishName, b.EnglishName, StringComparison.CurrentCultureIgnoreCase);
                });
                _selectedItem = null;
                if (!string.IsNullOrEmpty(selectedName))
                {
                    for (int i = 0; i < _itemEntries.Count; i++)
                        if (_itemEntries[i].PrefabName.Equals(selectedName, StringComparison.OrdinalIgnoreCase)) { _selectedItem = _itemEntries[i]; break; }
                }
                _itemCatalogReady = true;
                if (_itemEntries.Count == 0)
                    _status = T("No supported item prefabs were found in ObjectDB.", "В ObjectDB не найдены поддерживаемые предметы.");
                else
                    _status = T("Item catalogue loaded: ", "Каталог предметов загружен: ") + _itemEntries.Count;
                Logger.LogInfo("Item catalogue refreshed: " + _itemEntries.Count + " item prefabs available.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not read the item catalogue: " + ex);
                _status = T("Failed to load the item catalogue. See BepInEx/LogOutput.log.", "Не удалось загрузить каталог предметов. См. BepInEx/LogOutput.log.");
            }
        }

        private static ItemCategory ClassifyItem(string itemType, float foodValue, string prefabName)
        {
            string t = (itemType ?? string.Empty).Trim();
            string name = (prefabName ?? string.Empty).ToLowerInvariant();

            // Prefer the actual game item type over a prefab-name substring. Some weapons,
            // notably StaffShield (Staff of Protection), contain "shield" in the prefab ID
            // but are TwoHandedWeapon items and belong in the existing Weapons category.
            if (IsWeaponType(t, name))
                return ItemCategory.Weapons;

            // Real shields, wearable equipment, and Trinkets belong in the Armor tab.
            if (t.Equals("Shield", StringComparison.OrdinalIgnoreCase) || name.Contains("shield") ||
                t.Equals("Helmet", StringComparison.OrdinalIgnoreCase) || t.Equals("Chest", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Legs", StringComparison.OrdinalIgnoreCase) || t.Equals("Hands", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Shoulder", StringComparison.OrdinalIgnoreCase) || t.Equals("Utility", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Trinket", StringComparison.OrdinalIgnoreCase) || t.Equals("Trinkets", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("trinket"))
                return ItemCategory.Armor;

            // Only items with actual food health values belong in Food. Potions/meads with
            // ItemType=Consumable but no food stats remain in Other.
            if (foodValue > 0.01f)
                return ItemCategory.Food;

            if (t.Equals("Material", StringComparison.OrdinalIgnoreCase) || t.IndexOf("Trophie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("Trophy", StringComparison.OrdinalIgnoreCase) >= 0)
                return ItemCategory.Materials;

            return ItemCategory.Other;
        }

        private static bool IsWeaponType(string itemType, string lowerPrefabName)
        {
            string type = (itemType ?? string.Empty).Trim();
            string name = lowerPrefabName ?? string.Empty;
            // Valheim uses several enum names for weapons and ammunition. Catch the generic
            // weapon types plus bow/atgeir/ammo variants and the torch weapon type.
            if (type.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Ammo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.Equals("Bow", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Attach_Atgeir", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Torch", StringComparison.OrdinalIgnoreCase))
                return true;

            // Fallback for custom item prefabs whose ItemType is not exposed consistently.
            return name.Contains("arrow") || name.Contains("bolt") || name.Contains("spear") ||
                   name.Contains("atgeir") || name.Contains("crossbow") || name.Contains("bow_");
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null) return null;
            for (Type current = instance.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) { try { return field.GetValue(instance); } catch { } }
                PropertyInfo property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0) { try { return property.GetValue(instance, null); } catch { } }
            }
            return null;
        }

        private static float ReadFloatMember(object instance, string name)
        {
            object value = ReadMember(instance, name);
            try { return value == null ? 0f : Convert.ToSingle(value); } catch { return 0f; }
        }

        private static int ReadIntMember(object instance, string name, int fallback)
        {
            object value = ReadMember(instance, name);
            try { return value == null ? fallback : Convert.ToInt32(value); } catch { return fallback; }
        }

        private string LocalizeName(string raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (!raw.StartsWith("$", StringComparison.Ordinal)) return raw;
            Type localizationType = FindType("Localization");
            object localization = GetSingleton(localizationType, "instance");
            if (localization != null)
            {
                MethodInfo[] methods = localizationType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    ParameterInfo[] p = methods[i].GetParameters();
                    if (methods[i].Name == "Localize" && p.Length == 1 && p[0].ParameterType == typeof(string))
                    {
                        try
                        {
                            object localized = methods[i].Invoke(localization, new object[] { raw });
                            string value = localized as string;
                            if (!string.IsNullOrWhiteSpace(value) && value[0] != '$') return value;
                        }
                        catch { }
                    }
                }
            }
            return fallback;
        }

        // All item categories share one verified insertion path. Different ItemTypes keep
        // their own m_shared data, stack limits, visual prefab and optional durability.
        private void AddSelectedItemToInventory()
        {
            var selection = _selectedItem;
            string amount = _itemCountInput, name = _itemCustomName, description = _itemCustomDescription;
            int quality = _itemQuality;
            if (!AuthorizeLocal(() => AddInventorySelection(selection, amount, quality, name, description),
                "give item " + selection.PrefabName + " ×" + amount + " quality=" + quality +
                (string.IsNullOrWhiteSpace(name) ? "" : " name=" + name))) return;
            AddInventorySelection(selection, amount, quality, name, description);
        }

        private void AddInventorySelection(ItemEntry selection, string amount, int itemQuality, string name, string description)
        {
            if (!HasAdminAccess || (!IsServer() && !_executingApprovedLocal)) return;
            if (selection == null || selection.Prefab == null)
            {
                _status = T("Select an item first.", "Сначала выбери предмет.");
                return;
            }
            int requested;
            if (!TryParseItemAmount(amount, out requested))
            {
                _status = T("Enter a valid amount (1–9999).", "Введи допустимое количество (1–9999).");
                return;
            }
            RefreshGameReferences();
            if (_playerComponent == null)
            {
                _status = T("Player not found. Enter a world first.", "Игрок не найден. Сначала войди в мир.");
                return;
            }

            object inventory = GetPlayerInventory();
            if (inventory == null)
            {
                _status = T("Could not access the player's inventory.", "Не удалось получить доступ к инвентарю игрока.");
                return;
            }

            int quality = Mathf.Clamp(itemQuality, 1, Mathf.Max(1, selection.MaxQuality));
            string customName = NexusAdvancedData.CleanName(name);
            string customDescription = NexusAdvancedData.CleanDescription(description);
            if ((customName.Length > 0 || customDescription.Length > 0) && selection.MaxStack > 1)
            {
                _status = T("Custom names require an unstackable item (max stack 1).", "Своё имя поддерживается только для предметов со стаком 1.");
                return;
            }
            InventoryAddResult result = AddItemBatches(inventory, selection, requested, quality, customName, customDescription);
            int remaining = requested - result.Added;
            if (result.Added > 0)
            {
                if (remaining == 0)
                    ResetItemAmountAfterSuccessfulAdd();
                else
                {
                    // Keep only the remaining amount in the input, avoiding accidental duplicates.
                    _itemCount = remaining;
                    _itemCountInput = remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                RecordNexusHistory("item", selection.PrefabName, result.Added, quality, 1, customName, customDescription);
                _status = T("Added ", "Добавлено ") + result.Added + " / " + requested + " × " +
                    GetItemDisplayName(selection) +
                    (remaining > 0 ? T(". Remaining: ", ". Осталось: ") + remaining : string.Empty) +
                    (string.IsNullOrEmpty(result.Reason) ? string.Empty : " — " + result.Reason);
            }
            else
            {
                _status = T("Nothing added. ", "Ничего не добавлено. ") + result.Reason;
            }
            _hudDirty = true;
        }

        private sealed class InventoryAddResult
        {
            public int Added;
            public string Reason = string.Empty;
        }

        private InventoryAddResult AddItemBatches(object inventory, ItemEntry entry, int requested, int quality, string customName = "", string customDescription = "")
        {
            InventoryAddResult result = new InventoryAddResult();
            if (!HasAdminAccess || (!IsServer() && !_executingApprovedLocal))
            { result.Reason = T("Administrator authorization required.", "Нужны права администратора."); return result; }
            if (_itemDropType == null || entry == null || entry.Prefab == null)
            {
                result.Reason = T("Item prefab is unavailable.", "Префаб предмета недоступен.");
                return result;
            }
            Component drop = entry.Prefab.GetComponent(_itemDropType);
            object source = ReadMember(drop, "m_itemData");
            object shared = ReadMember(source, "m_shared");
            if (source == null || shared == null)
            {
                result.Reason = T("ItemData or shared properties are missing.", "Отсутствуют ItemData или общие свойства.");
                return result;
            }
            MethodInfo clone = source.GetType().GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (clone == null)
            {
                result.Reason = T("ItemData.Clone is unavailable.", "Недоступен ItemData.Clone.");
                return result;
            }
            MethodInfo insert = null;
            foreach (MethodInfo method in inventory.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "AddItem") continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(source.GetType()))
                {
                    insert = method;
                    break;
                }
            }
            if (insert == null)
            {
                // Fail closed rather than passing equipment through an overload that may
                // lose m_dropPrefab, quality, durability or other instance properties.
                result.Reason = T("Compatible Inventory.AddItem(ItemData) is unavailable; no items were created.",
                    "Нет совместимого Inventory.AddItem(ItemData); предметы не создавались.");
                Logger.LogWarning(result.Reason + " Prefab: " + entry.PrefabName);
                return result;
            }

            int before;
            if (!TryCountInventoryPrefab(inventory, entry.Prefab, out before))
            {
                result.Reason = T("Cannot verify inventory contents in this game version.",
                    "Не удаётся проверить содержимое инвентаря в этой версии игры.");
                return result;
            }

            int maxStack = Mathf.Clamp(ReadIntMember(shared, "m_maxStackSize", 1), 1, 9999);
            int maxOperations = maxStack == 1 ? MaxUnstackableItemsPerAction : MaxStackBatchesPerAction;
            int operations = 0;
            while (result.Added < requested && operations < maxOperations)
            {
                operations++;
                int batch = Math.Min(maxStack, requested - result.Added);
                object copy;
                try { copy = clone.Invoke(source, null); }
                catch (Exception ex)
                {
                    Logger.LogWarning("ItemData.Clone failed for " + entry.PrefabName + ": " + ex.Message);
                    result.Reason = T("Could not clone the item.", "Не удалось клонировать предмет.");
                    break;
                }
                if (copy == null || !TrySetIntMember(copy, "m_stack", batch) ||
                    !TrySetIntMember(copy, "m_quality", quality) ||
                    (!TrySetReferenceMember(copy, "m_dropPrefab", entry.Prefab) &&
                     !ReferenceEquals(ReadMember(copy, "m_dropPrefab"), entry.Prefab)))
                {
                    result.Reason = T("Could not initialize the item and prefab reference.",
                        "Не удалось подготовить предмет и ссылку на префаб.");
                    break;
                }
                if (ReadMember(copy, "m_shared") == null && !TrySetReferenceMember(copy, "m_shared", shared))
                {
                    result.Reason = T("Item shared properties are missing.", "Отсутствуют свойства предмета.");
                    break;
                }

                object clonedShared = ReadMember(copy, "m_shared");
                if (object.Equals(ReadMember(clonedShared, "m_useDurability"), true))
                {
                    float durability = GetItemMaxDurability(copy, clonedShared, quality);
                    if (durability <= 0f || !TrySetFloatMember(copy, "m_durability", durability))
                    {
                        result.Reason = T("Could not initialize full durability.", "Не удалось установить полную прочность.");
                        break;
                    }
                }

                if (!TryStampCustomItemName(copy, customName, customDescription))
                {
                    result.Reason = T("Could not store the custom item name.", "Не удалось сохранить имя экземпляра предмета.");
                    break;
                }
                object returned = null;
                try { returned = insert.Invoke(inventory, new object[] { copy }); }
                catch (Exception ex)
                {
                    Logger.LogWarning("Inventory.AddItem failed for " + entry.PrefabName + ": " + ex.Message);
                    result.Reason = T("Inventory rejected the item.", "Инвентарь отклонил предмет.");
                    break;
                }
                int after;
                if (!TryCountInventoryPrefab(inventory, entry.Prefab, out after))
                {
                    result.Reason = T("Inventory changed, but the result could not be verified; do not retry without checking inventory.",
                        "Инвентарь изменён, но результат не проверен; проверь содержимое перед повтором.");
                    break;
                }
                int delta = Mathf.Clamp(after - before, 0, batch);
                if (delta > 0 && !string.IsNullOrEmpty(customName))
                {
                    bool verifiedName = InventoryContainsNamedItem(inventory, entry.Prefab, customName);
                    Logger.LogInfo("Custom item name persisted in inventory: " + verifiedName + " (" + entry.PrefabName + ")");
                    if (!verifiedName)
                        Logger.LogWarning("Custom item name could not be found after insertion; inspect ItemData.m_customData.");
                }
                result.Added += delta;
                before = after;
                if (delta == 0)
                {
                    // Never treat bool false or non-null return values as evidence of an insertion.
                    result.Reason = T("No inventory space or insertion failed.", "Нет места или добавление не удалось.");
                    break;
                }
                if (delta < batch)
                {
                    result.Reason = T("Only part of the stack fit in inventory.", "В инвентарь поместилась только часть стака.");
                    break;
                }
                if (insert.ReturnType == typeof(bool) && !(returned is bool && (bool)returned))
                {
                    // Account for real changes even when an overload reported failure.
                    Logger.LogWarning("Inventory.AddItem returned false despite observed change for " + entry.PrefabName);
                }
            }
            if (result.Added < requested && string.IsNullOrEmpty(result.Reason))
                result.Reason = T("Per-action safety limit reached; repeat to add the remainder.",
                    "Достигнут лимит за одно действие; повтори для остатка.");
            return result;
        }

        private static bool TryCountInventoryPrefab(object inventory, GameObject prefab, out int count)
        {
            count = 0;
            if (inventory == null || prefab == null) return false;
            IEnumerable values = null;
            try
            {
                MethodInfo method = inventory.GetType().GetMethod("GetAllItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (method != null) values = method.Invoke(inventory, null) as IEnumerable;
                if (values == null) values = ReadMember(inventory, "m_inventory") as IEnumerable;
                if (values == null) return false;
                foreach (object item in values)
                {
                    GameObject itemPrefab = ReadMember(item, "m_dropPrefab") as GameObject;
                    if (itemPrefab == null) continue;
                    if (ReferenceEquals(itemPrefab, prefab) ||
                        string.Equals(itemPrefab.name, prefab.name, StringComparison.OrdinalIgnoreCase))
                    {
                        int stack = Mathf.Max(0, ReadIntMember(item, "m_stack", 0));
                        count = stack > int.MaxValue - count ? int.MaxValue : count + stack;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private static bool InventoryContainsNamedItem(object inventory, GameObject prefab, string expectedName)
        {
            if (inventory == null || prefab == null || string.IsNullOrEmpty(expectedName)) return false;
            try
            {
                MethodInfo get = inventory.GetType().GetMethod("GetAllItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                IEnumerable items = get == null ? null : get.Invoke(inventory, null) as IEnumerable;
                if (items == null) items = ReadMember(inventory, "m_inventory") as IEnumerable;
                if (items == null) return false;
                foreach (object item in items)
                {
                    GameObject current = ReadMember(item, "m_dropPrefab") as GameObject;
                    if (current == null || (current != prefab && current.name != prefab.name)) continue;
                    if (string.Equals(ReadCustomItemName(item), expectedName, StringComparison.Ordinal)) return true;
                }
            }
            catch { }
            return false;
        }

        private void ResetItemAmountAfterSuccessfulAdd()
        {
            _itemCount = 1;
            _itemCountInput = "1";
        }

        private static bool IsBodyArmorItem(ItemEntry entry)
        {
            if (entry == null || entry.Category != ItemCategory.Armor) return false;
            string type = (entry.ItemTypeLabel ?? string.Empty).Trim();
            string name = (entry.PrefabName ?? string.Empty).ToLowerInvariant();

            // Explicitly leave weapons and shields on their existing code path.
            if (type.Equals("Shield", StringComparison.OrdinalIgnoreCase) || name.Contains("shield")) return false;

            return type.Equals("Helmet", StringComparison.OrdinalIgnoreCase) ||
                   type.Equals("Chest", StringComparison.OrdinalIgnoreCase) ||
                   type.Equals("Legs", StringComparison.OrdinalIgnoreCase) ||
                   type.Equals("Hands", StringComparison.OrdinalIgnoreCase) ||
                   type.Equals("Shoulder", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWeaponOrShieldItem(ItemEntry entry)
        {
            if (entry == null) return false;

            string type = (entry.ItemTypeLabel ?? string.Empty).Trim();
            string name = (entry.PrefabName ?? string.Empty).ToLowerInvariant();

            // Shields are catalogued under Armor, so test them explicitly first.
            if (type.Equals("Shield", StringComparison.OrdinalIgnoreCase) || name.Contains("shield"))
                return true;

            return entry.Category == ItemCategory.Weapons || IsWeaponType(type, name);
        }

        private static int GetSelectableMaxQuality(ItemCategory category, string itemType, string prefabName, int declaredMaxQuality)
        {
            int declared = Mathf.Clamp(declaredMaxQuality, 1, ExtendedNonWeaponQualityCap);
            string type = (itemType ?? string.Empty).Trim();
            string name = (prefabName ?? string.Empty).ToLowerInvariant();

            // Weapons and shields follow the same extended-quality policy as armor.
            // Only items with native quality progression (m_maxQuality > 1) are
            // eligible. Ammo, bows without upgrades, and other fixed-quality items
            // retain their declared limit.
            bool weaponOrShield = category == ItemCategory.Weapons ||
                IsWeaponType(type, name) ||
                type.Equals("Shield", StringComparison.OrdinalIgnoreCase) ||
                (name.Contains("shield") && !IsWeaponType(type, name));
            if (weaponOrShield)
                return declared > 1 ? ExtendedNonWeaponQualityCap : declared;

            // Extended item quality is offered only for upgradeable non-weapon gear.
            // A native maxQuality of 1 usually means the item has no quality progression.
            bool armorOrAccessory =
                category == ItemCategory.Armor &&
                (type.Equals("Helmet", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Chest", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Legs", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Hands", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Shoulder", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Utility", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Trinket", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Trinkets", StringComparison.OrdinalIgnoreCase));

            bool nonCombatTool =
                category == ItemCategory.Other &&
                type.Equals("Tool", StringComparison.OrdinalIgnoreCase);

            if ((armorOrAccessory || nonCombatTool) && declared > 1)
                return ExtendedNonWeaponQualityCap;

            // Preserve higher native limits for any other non-weapon modded item,
            // without imposing a new limit above the configured safety cap.
            return declared;
        }

        private static bool IsExtendedQualityNonWeaponItem(ItemEntry entry)
        {
            if (entry == null) return false;

            string type = (entry.ItemTypeLabel ?? string.Empty).Trim();
            string name = (entry.PrefabName ?? string.Empty).ToLowerInvariant();

            if (entry.Category == ItemCategory.Weapons ||
                IsWeaponType(type, name) ||
                type.Equals("Shield", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("shield"))
                return false;

            if (entry.Category == ItemCategory.Armor)
            {
                return type.Equals("Helmet", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Chest", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Legs", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Hands", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Shoulder", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Utility", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Trinket", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Trinkets", StringComparison.OrdinalIgnoreCase);
            }

            return entry.Category == ItemCategory.Other &&
                   type.Equals("Tool", StringComparison.OrdinalIgnoreCase);
        }

        private static float GetItemMaxDurability(object itemData, object shared, int quality)
        {
            if (itemData != null)
            {
                Type itemDataType = itemData.GetType();
                MethodInfo noArgument = itemDataType.GetMethod("GetMaxDurability", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (noArgument != null)
                {
                    try
                    {
                        float result = Convert.ToSingle(noArgument.Invoke(itemData, null));
                        if (result > 0f) return result;
                    }
                    catch { }
                }

                MethodInfo withQuality = itemDataType.GetMethod("GetMaxDurability", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
                if (withQuality != null)
                {
                    try
                    {
                        float result = Convert.ToSingle(withQuality.Invoke(itemData, new object[] { quality }));
                        if (result > 0f) return result;
                    }
                    catch { }
                }
            }

            float baseDurability = ReadFloatMember(shared, "m_maxDurability");
            float durabilityPerLevel = ReadFloatMember(shared, "m_durabilityPerLevel");
            return Mathf.Max(0f, baseDurability + Mathf.Max(0, quality - 1) * durabilityPerLevel);
        }

        private static bool TrySetReferenceMember(object instance, string memberName, object value)
        {
            if (instance == null || value == null) return false;
            for (Type current = instance.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType.IsInstanceOfType(value))
                {
                    try { field.SetValue(instance, value); return true; } catch { }
                }
                PropertyInfo property = current.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.CanWrite && property.PropertyType.IsInstanceOfType(value))
                {
                    try { property.SetValue(instance, value, null); return true; } catch { }
                }
            }
            return false;
        }

        private static bool TrySetFloatMember(object instance, string memberName, float value)
        {
            if (instance == null) return false;
            for (Type current = instance.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(float)) { try { field.SetValue(instance, value); return true; } catch { } }
                PropertyInfo property = current.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.PropertyType == typeof(float) && property.CanWrite) { try { property.SetValue(instance, value, null); return true; } catch { } }
            }
            return false;
        }

        private static bool TrySetIntMember(object instance, string memberName, int value)
        {
            if (instance == null) return false;
            for (Type current = instance.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(int)) { try { field.SetValue(instance, value); return true; } catch { } }
                PropertyInfo property = current.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.PropertyType == typeof(int) && property.CanWrite) { try { property.SetValue(instance, value, null); return true; } catch { } }
            }
            return false;
        }

        private static List<GameObject> ReadPrefabList(object sceneInstance)
        {
            var result = new List<GameObject>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sceneInstance == null) return result;

            Type type = sceneInstance.GetType();
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].Name != "m_prefabs" && fields[i].Name != "m_namedPrefabs") continue;
                    object collection;
                    try { collection = fields[i].GetValue(sceneInstance); }
                    catch { continue; }
                    IEnumerable enumerable = collection as IEnumerable;
                    if (enumerable == null) continue;
                    foreach (object item in enumerable)
                    {
                        GameObject go = item as GameObject;
                        if (go == null && item != null)
                        {
                            PropertyInfo valueProp = item.GetType().GetProperty("Value");
                            if (valueProp != null)
                            {
                                try { go = valueProp.GetValue(item, null) as GameObject; }
                                catch { }
                            }
                        }
                        if (go != null && seen.Add(go.name)) result.Add(go);
                    }
                }
            }
            return result;
        }

        private static Type FindType(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(typeName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        private static object GetSingleton(Type type, string memberName)
        {
            if (type == null) return null;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    try { return field.GetValue(null); }
                    catch { }
                }
                PropertyInfo property = current.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    try { return property.GetValue(null, null); }
                    catch { }
                }
            }
            return null;
        }

        private string ReadLocalizedCharacterName(Component character, string prefabName)
        {
            string raw = ReadStringMember(character, "m_name");
            return LocalizeName(raw, prefabName);
        }

        private static string ReadStringMember(object instance, string name)
        {
            if (instance == null) return null;
            for (Type current = instance.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    try { return field.GetValue(instance) as string; }
                    catch { }
                }
                PropertyInfo property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    try { return property.GetValue(instance, null) as string; }
                    catch { }
                }
            }
            return null;
        }

        private static CreatureCategory InferCategory(string prefabName)
        {
            string n = (prefabName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("eikthyr") || n.Contains("gd_king") || n.Contains("bonemass") || n.Contains("dragon") ||
                n.Contains("goblinking") || n.Contains("seekerqueen") || n.Contains("fader") || n.Contains("frozenking") || n.Contains("kallfimbulbringer"))
                return CreatureCategory.Boss;
            if (n.Contains("brenna") || n.Contains("geirrhafa") || n == "zil" || n == "thungr" || n.Contains("dyrnwyn"))
                return CreatureCategory.Boss;
            return CreatureCategory.Regular;
        }

        private static string[] InferBiomes(string prefabName)
        {
            string n = (prefabName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("ash") || n.Contains("charred") || n.Contains("morgan") || n.Contains("volture")) return new[] { "Ashlands" };
            if (n.Contains("deepnorth") || n.Contains("frost") || n.Contains("jotun") || n.Contains("moose") || n.Contains("seal") || n.Contains("frysling")) return new[] { "Deep North" };
            if (n.Contains("seeker") || n.Contains("tick") || n.Contains("gjall") || n.Contains("hare") || n.Contains("dverger") || n.Contains("dvergr")) return new[] { "Mistlands" };
            if (n.Contains("fuling") || n.Contains("goblin") || n.Contains("lox") || n.Contains("deathsquito") || n.Contains("tar")) return new[] { "Plains" };
            if (n.Contains("draugr") || n.Contains("blob") || n.Contains("leech") || n.Contains("wraith") || n.Contains("abomination")) return new[] { "Swamp" };
            if (n.Contains("wolf") || n.Contains("fenring") || n.Contains("drake") || n.Contains("hatchling") || n.Contains("golem") || n.Contains("cultist") || n.Contains("ulv") || n.Contains("bat")) return new[] { "Mountains" };
            if (n.Contains("greydwarf") || n.Contains("troll") || n.Contains("skeleton") || n.Contains("ghost") || n.Contains("brenna")) return new[] { "Black Forest" };
            if (n.Contains("serpent") || n.Contains("fish") || n.Contains("gull") || n.Contains("leviathan")) return new[] { "Ocean" };
            if (n.Contains("boar") || n.Contains("deer") || n.Contains("neck") || n.Contains("greyling") || n.Contains("eikthyr")) return new[] { "Meadows" };
            return new[] { "Unknown" };
        }

        // Taming is only available for creatures with Valheim's Tameable component.
        private bool TryTameCreature(GameObject spawnedObject)
        {
            if (spawnedObject == null) return false;
            Type tameableType = FindType("Tameable");
            if (tameableType == null) return false;
            Component tameable = spawnedObject.GetComponent(tameableType);
            if (tameable == null) return false;
            MethodInfo tame = tameable.GetType().GetMethod("Tame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (tame == null) return false;
            try { tame.Invoke(tameable, null); return true; }
            catch (Exception ex) { Logger.LogWarning("Tame failed: " + ex.Message); return false; }
        }

        private static bool TrySetBoolMember(object instance, string name, bool value)
        {
            if (instance == null) return false;
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(bool))
                { try { field.SetValue(instance, value); return true; } catch { } }
            }
            return false;
        }

        private static bool TryInvokeFloat(object target, string method, float value)
        {
            if (target == null) return false;
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(float) }, null);
            if (info == null) return false;
            try { info.Invoke(target, new object[] { value }); return true; } catch { return false; }
        }

        private static float ReadFloatMethod(object target, string method)
        {
            if (target == null) return 0f;
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (info == null) return 0f;
            try { return Convert.ToSingle(info.Invoke(target, null)); } catch { return 0f; }
        }

        private object GetPlayerInventory()
        {
            RefreshGameReferences();
            if (_playerComponent == null) return null;
            MethodInfo info = _playerComponent.GetType().GetMethod("GetInventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            try { return info == null ? null : info.Invoke(_playerComponent, null); } catch { return null; }
        }

        private static List<object> GetInventoryItems(object inventory)
        {
            List<object> items = new List<object>();
            if (inventory == null) return items;
            MethodInfo info = inventory.GetType().GetMethod("GetAllItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            try
            {
                IEnumerable values = info == null ? null : info.Invoke(inventory, null) as IEnumerable;
                if (values != null) foreach (object item in values) if (item != null) items.Add(item);
            }
            catch { }
            return items;
        }

        private void ShowAdminToast(string message)
        {
            _adminToast = message;
            _adminToastUntil = Time.unscaledTime + 3.5f;
            _status = message;
        }

        // Harmony patches operate only on the local player and only while the corresponding
        // toggle is enabled. Keep the original game methods untouched for other entities.
        private static bool AdminDamagePrefix(object __instance)
        {
            ValheimSpawnerPlugin mod = _activeInstance;
            return mod == null || !mod.HasAdminAccess || !mod._infiniteHealth || !ReferenceEquals(__instance, mod._playerComponent);
        }

        private static bool AdminStaminaPrefix(object __instance)
        {
            ValheimSpawnerPlugin mod = _activeInstance;
            return mod == null || !mod.HasAdminAccess || !mod._infiniteStamina || !ReferenceEquals(__instance, mod._playerComponent);
        }

        private static bool AdminEitrPrefix(object __instance)
        {
            ValheimSpawnerPlugin mod = _activeInstance;
            return mod == null || !mod.HasAdminAccess || !mod._infiniteEitr || !ReferenceEquals(__instance, mod._playerComponent);
        }

        private static void AdminSkillXpPrefix(object __instance, ref float factor)
        {
            ValheimSpawnerPlugin mod = _activeInstance;
            if (mod == null || !mod.HasAdminAccess || mod._skillXpMultiplier <= 1f || mod._playerComponent == null) return;
            object skills = ReadMember(mod._playerComponent, "m_skills");
            if (skills == null)
            {
                MethodInfo getSkills = mod._playerComponent.GetType().GetMethod("GetSkills",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                try { if (getSkills != null) skills = getSkills.Invoke(mod._playerComponent, null); }
                catch { }
            }
            if (ReferenceEquals(__instance, skills))
                factor = Mathf.Min(1000000f, factor * mod._skillXpMultiplier);
        }

        private void InstallAdminPatches()
        {
            if (_harmony == null) return;
            PatchAdminMethod("Character", "Damage", nameof(AdminDamagePrefix), -1);
            PatchAdminMethod("Character", "RPC_Damage", nameof(AdminDamagePrefix), -1);
            PatchAdminMethod("Player", "UseStamina", nameof(AdminStaminaPrefix), -1);
            PatchAdminMethod("Player", "UseEitr", nameof(AdminEitrPrefix), -1);
            PatchAdminMethod("Skills", "RaiseSkill", nameof(AdminSkillXpPrefix), 2);
        }

        private void PatchAdminMethod(string typeName, string methodName, string prefixName, int parameterCount)
        {
            Type type = FindType(typeName);
            if (type == null) { Logger.LogWarning("Admin patch: missing type " + typeName); return; }
            MethodInfo prefix = typeof(ValheimSpawnerPlugin).GetMethod(prefixName,
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != methodName || (parameterCount >= 0 && method.GetParameters().Length != parameterCount))
                    continue;
                // RaiseSkill(SkillType, float) only: Harmony binds 'factor' by argument name.
                if (methodName == "RaiseSkill" &&
                    (method.GetParameters().Length != 2 || method.GetParameters()[1].ParameterType != typeof(float) ||
                     method.GetParameters()[1].Name != "factor"))
                    continue;
                try { _harmony.Patch(method, prefix: new HarmonyMethod(prefix)); }
                catch (Exception ex) { Logger.LogWarning("Admin patch " + typeName + "." + methodName + ": " + ex.Message); }
            }
        }

        private void ApplyAdminVitals()
        {
            if (!HasAdminAccess || _playerComponent == null) return;
            if (_infiniteHealth)
            {
                float maximum = ReadFloatMethod(_playerComponent, "GetMaxHealth");
                if (maximum > 0f)
                {
                    TryInvokeFloat(_playerComponent, "SetHealth", maximum);
                    TrySetFloatMember(_playerComponent, "m_health", maximum);
                }
            }
            if (_infiniteStamina)
            {
                float maximum = ReadFloatMethod(_playerComponent, "GetMaxStamina");
                if (maximum > 0f)
                {
                    TryInvokeFloat(_playerComponent, "SetStamina", maximum);
                    TrySetFloatMember(_playerComponent, "m_stamina", maximum);
                }
            }
            if (_infiniteEitr)
            {
                float maximum = ReadFloatMethod(_playerComponent, "GetMaxEitr");
                if (maximum > 0f)
                {
                    TryInvokeFloat(_playerComponent, "SetEitr", maximum);
                    TrySetFloatMember(_playerComponent, "m_eitr", maximum);
                }
            }
        }

        private void SetAdminFly(bool enabled)
        {
            if (!HasAdminAccess) return;
            _adminFly = enabled;
            SaveAdminPreferences();
            ShowAdminToast(enabled ? T("Flight enabled [Z]", "Полёт включён [Z]") : T("Flight disabled [Z]", "Полёт выключен [Z]"));
        }

        private void SetAdminCreative(bool enabled)
        {
            if (!HasAdminAccess) return;
            _adminCreative = enabled;
            SaveAdminPreferences();
            ShowAdminToast(enabled ? T("Free building enabled [B]", "Строительство без затрат [B]") : T("Free building disabled [B]", "Строительство без затрат выключено [B]"));
        }

        private void RepairInventory()
        {
            if (!AuthorizeLocal(RepairInventory, "repair inventory")) return;
            object inventory = GetPlayerInventory();
            if (inventory == null) { _status = T("Inventory unavailable.", "Инвентарь недоступен."); return; }
            int count = 0;
            foreach (object item in GetInventoryItems(inventory))
            {
                object shared = ReadMember(item, "m_shared");
                if (shared == null || !Convert.ToBoolean(ReadMember(shared, "m_useDurability") ?? false)) continue;
                int quality = ReadIntMember(item, "m_quality", 1);
                float maximum = GetItemMaxDurability(item, shared, quality);
                if (maximum > 0f && TrySetFloatMember(item, "m_durability", maximum)) count++;
            }
            _status = T("Repaired items: ", "Починено предметов: ") + count;
        }

        private void MaximizeInventoryStacks()
        {
            if (!AuthorizeLocal(MaximizeInventoryStacks, "fill inventory stacks")) return;
            object inventory = GetPlayerInventory();
            if (inventory == null) { _status = T("Inventory unavailable.", "Инвентарь недоступен."); return; }
            int count = 0;
            foreach (object item in GetInventoryItems(inventory))
            {
                object shared = ReadMember(item, "m_shared");
                int maximum = ReadIntMember(shared, "m_maxStackSize", 1);
                int current = ReadIntMember(item, "m_stack", 1);
                if (maximum > current && TrySetIntMember(item, "m_stack", maximum)) count++;
            }
            MethodInfo changed = inventory.GetType().GetMethod("Changed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            try { if (changed != null) changed.Invoke(inventory, null); } catch { }
            _status = T("Maximized stacks: ", "Заполнено стаков: ") + count;
        }

        private void DrawAdminPanel()
        {
            // Each group has one job and a fixed visual rhythm; avoid long explanatory labels.
            _adminScroll = GUILayout.BeginScrollView(_adminScroll, GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical("box");
            DrawHudSectionTitle(T("Vitals", "Ресурсы"));
            GUILayout.BeginHorizontal();
            _infiniteHealth = GUILayout.Toggle(_infiniteHealth, T("∞ Health", "∞ Здоровье"));
            _infiniteStamina = GUILayout.Toggle(_infiniteStamina, T("∞ Stamina", "∞ Выносливость"));
            _infiniteEitr = GUILayout.Toggle(_infiniteEitr, T("∞ Eitr", "∞ Эйтр"));
            GUILayout.EndHorizontal();
            GUI.enabled = _adrenalineHookAvailable;
            _infiniteAdrenaline = GUILayout.Toggle(_infiniteAdrenaline,
                T("∞ Adrenaline (no decay + reserve)", "∞ Адреналин (без спада + запас)"));
            GUI.enabled = true;
            if (!_adrenalineHookAvailable)
                GUILayout.Label(T("Adrenaline API not detected; option disabled.", "API адреналина не найдено; функция отключена."));
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            DrawHudSectionTitle(T("Combat & progression", "Бой и развитие"));
            GUI.enabled = _outgoingDamageHookAvailable && IsServer();
            _superDamage = GUILayout.Toggle(_superDamage,
                T("Super damage (host, hostile targets only)", "Суперурон (хост, только враги)"));
            GUI.enabled = true;
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Damage:", "Урон:"), GUILayout.Width(95f));
            float[] factors = { 1f, 2f, 5f, 10f, 25f };
            for (int i = 0; i < factors.Length; i++)
            {
                float factor = factors[i];
                if (GUILayout.Button((_superDamageMultiplier == factor ? "[x" : "x") + factor.ToString("0") +
                    (_superDamageMultiplier == factor ? "]" : ""), GUILayout.Height(28f))) _superDamageMultiplier = factor;
            }
            GUILayout.EndHorizontal();
            if (!_outgoingDamageHookAvailable)
                GUILayout.Label(T("Outgoing-damage hook unavailable in this version.", "Хук урона недоступен в этой версии."));
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Skill XP:", "Опыт навыков:"), GUILayout.Width(95f));
            _skillXpInput = GUILayout.TextField(_skillXpInput, 10, GUILayout.Width(96f));
            if (GUILayout.Button(T("Apply xXP", "Применить xXP"), GUILayout.Width(130f), GUILayout.Height(28f)))
            {
                float parsed;
                if (float.TryParse(_skillXpInput.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed) &&
                    !float.IsNaN(parsed) && !float.IsInfinity(parsed) && parsed >= 1f && parsed <= 1000f)
                {
                    _skillXpMultiplier = parsed;
                    _status = T("Skill XP multiplier: x", "Множитель опыта: x") + _skillXpMultiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                }
                else _status = T("Enter a multiplier from 1 to 1000.", "Введи множитель от 1 до 1000.");
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            DrawHudSectionTitle(T("Movement & building", "Передвижение и строительство"));
            GUILayout.BeginHorizontal();
            bool fly = GUILayout.Toggle(_adminFly, T("Flight [Z]", "Полёт [Z]"));
            if (fly != _adminFly) SetAdminFly(fly);
            bool creative = GUILayout.Toggle(_adminCreative, T("Free building [B]", "Строительство без затрат [B]"));
            if (creative != _adminCreative) SetAdminCreative(creative);
            GUILayout.EndHorizontal();
            _adminHotkeys = GUILayout.Toggle(_adminHotkeys,
                T("Enable Z/B shortcuts while HUD is closed", "Разрешить Z/B при закрытом HUD"));
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            DrawHudSectionTitle(T("Inventory maintenance", "Обслуживание инвентаря"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Repair all", "Починить всё"), GUILayout.Height(32f))) RepairInventory();
            if (GUILayout.Button(T("Fill stacks", "Заполнить стаки"), GUILayout.Height(32f))) MaximizeInventoryStacks();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Label(T("Admin effects are session-only; damaging effects apply to the host's local player, not other players or tamed creatures.",
                "Эффекты администратора действуют в текущем сеансе. Урон ограничен локальным игроком-хостом, без других игроков и приручённых существ."));
            GUILayout.EndScrollView();
            GUILayout.Label(_status);
        }

        private void SpawnSelected(bool atCrosshair)
        {
            if (_selected == null || _selected.Prefab == null || !HasAdminAccess) return;
            if (!TryGetSpawnBasePosition(atCrosshair, out var position, out var rotation))
            { _status = T("No valid target surface.", "Нет подходящей поверхности."); return; }
            string prefab = _selected.PrefabName, name = _customName.Trim();
            int count = _count, level = _level; bool tamed = _spawnTamed;
            RequestWorldAction(AdminAction.Spawn, p =>
            {
                p.Write(prefab); p.Write(count); p.Write(level); p.Write(name); p.Write(tamed);
                p.Write(position); p.Write(rotation.eulerAngles.y);
            }, _ => RecordNexusHistory("creature", prefab, count, 1, level, name));
        }

        private float GetKillRadius()
        {
            float configured = _killRadius == null ? 30f : _killRadius.Value;
            // A NaN radius must never bypass the distance comparison.
            if (float.IsNaN(configured) || float.IsInfinity(configured)) configured = 30f;
            return Mathf.Clamp(configured, 5f, 100f);
        }

        private List<object> GetNearbyKillTargets()
        {
            List<object> targets = new List<object>();
            RefreshGameReferences();
            if (!HasAdminAccess || _playerComponent == null || _characterType == null) return targets;
            MethodInfo all = _characterType.GetMethod("GetAllCharacters", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (all == null) return targets;
            IEnumerable characters;
            try { characters = all.Invoke(null, null) as IEnumerable; }
            catch { return targets; }
            if (characters == null) return targets;
            Vector3 playerPosition = _playerComponent.transform.position;
            float radius = GetKillRadius();
            float radiusSquared = radius * radius;
            foreach (object item in characters)
            {
                Component character = item as Component;
                if (character == null || character == _playerComponent) continue;
                bool flag;
                // Fail closed if the type cannot prove that a character is not a player.
                if (!TryReadBooleanMethod(item, "IsPlayer", out flag) || flag) continue;
                try
                {
                    if ((character.transform.position - playerPosition).sqrMagnitude > radiusSquared) continue;
                }
                catch { continue; }
                if (!_killIncludeTamed && (!TryReadBooleanMethod(item, "IsTamed", out flag) || flag)) continue;
                if (!_killIncludeBosses)
                {
                    bool isBoss;
                    bool hasBossFlag = TryReadBooleanMethod(item, "IsBoss", out isBoss);
                    if (hasBossFlag && isBoss) continue;
                    string prefabId = character.gameObject.name.Replace("(Clone)", string.Empty).Trim();
                    CreatureMetadata metadata;
                    bool isKnown = CreatureCatalog.TryGet(prefabId, out metadata);
                    if (isKnown && metadata.Category == CreatureCategory.Boss) continue;
                    // If no boss flag exists and the prefab is unknown, fail closed:
                    // a modded boss must never be assumed to be an ordinary creature.
                    if (!hasBossFlag && !isKnown) continue;
                }
                targets.Add(item);
            }
            return targets;
        }

        private void KillNearbyCreatures()
        {
            float radius = GetKillRadius(); bool tamed = _killIncludeTamed, bosses = _killIncludeBosses;
            var targets = Character.GetAllCharacters().Where(c => c != null && !c.IsPlayer() && !c.IsDead() &&
                NexusInteractionRules.CleanupTarget(c.IsPlayer(), c.IsBoss(), c.IsTamed(), bosses, tamed,
                    (c.transform.position - Player.m_localPlayer.transform.position).sqrMagnitude, radius))
                .Select(c => c.GetComponent<ZNetView>()).Where(v => v != null && v.IsValid())
                .Select(v => v.GetZDO().m_uid).Distinct().Take(200).ToArray();
            RequestWorldAction(AdminAction.Cleanup, p =>
            {
                p.Write(radius); p.Write(tamed); p.Write(bosses); p.Write(targets.Length);
                foreach (var id in targets) p.Write(id);
            });
        }

        private static bool TryReadBooleanMethod(object instance, string methodName, out bool value)
        {
            value = false;
            if (instance == null) return false;
            MethodInfo method = instance.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(bool)) return false;
            try
            {
                value = Convert.ToBoolean(method.Invoke(instance, null));
                return true;
            }
            catch { return false; }
        }

        private static object CreateLethalHitData(Type hitDataType)
        {
            if (hitDataType == null) return null;

            // Newer Valheim builds expose HitData(float damage), matching the game's killall command.
            try { return Activator.CreateInstance(hitDataType, new object[] { 10000000000f }); }
            catch { }

            // Reflection fallback for revisions that only expose a parameterless constructor.
            object hitData;
            try { hitData = Activator.CreateInstance(hitDataType, true); }
            catch { return null; }
            if (hitData == null) return null;

            FieldInfo damageTypesField = hitDataType.GetField("m_damage",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (damageTypesField == null) return null;
            try
            {
                object damageTypes = damageTypesField.GetValue(hitData);
                if (damageTypes == null) return null;
                Type damageTypesType = damageTypes.GetType();
                FieldInfo bluntField = damageTypesType.GetField("m_damage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (bluntField == null || bluntField.FieldType != typeof(float)) return null;
                bluntField.SetValue(damageTypes, 10000000000f);
                damageTypesField.SetValue(hitData, damageTypes);
                return hitData;
            }
            catch { return null; }
        }

        private bool ApplyCustomName(GameObject spawnedObject, string customName)
        {
            if (spawnedObject == null || string.IsNullOrWhiteSpace(customName)) return false;
            Component character = null;
            try { if (_characterType != null) character = spawnedObject.GetComponent(_characterType); }
            catch { }
            if (character == null) return false;

            bool applied = false;
            // Prefer the game's own API when present; some character types store their name in ZDO.
            MethodInfo[] methods = character.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "SetCustomName") continue;
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;
                try
                {
                    methods[i].Invoke(character, new object[] { customName });
                    applied = true;
                    break;
                }
                catch (Exception ex) { Logger.LogDebug("Character.SetCustomName failed: " + ex.Message); }
            }

            // Networked custom-name storage is the most reliable way to show the same name to other players.
            try
            {
                Component nview = _zNetViewType == null ? null : spawnedObject.GetComponent(_zNetViewType);
                if (nview != null)
                {
                    MethodInfo getZdo = nview.GetType().GetMethod("GetZDO", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                    object zdo = getZdo == null ? null : getZdo.Invoke(nview, null);
                    if (zdo != null)
                    {
                        MethodInfo[] zdoMethods = zdo.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        for (int i = 0; i < zdoMethods.Length; i++)
                        {
                            ParameterInfo[] p = zdoMethods[i].GetParameters();
                            if (zdoMethods[i].Name != "Set" || p.Length != 2 || p[0].ParameterType != typeof(string) || p[1].ParameterType != typeof(string)) continue;
                            try
                            {
                                zdoMethods[i].Invoke(zdo, new object[] { "customName", customName });
                                applied = true;
                            }
                            catch { }
                            try
                            {
                                zdoMethods[i].Invoke(zdo, new object[] { "VCS_CustomName", customName });
                                applied = true;
                            }
                            catch { }
                            if (applied) break;
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogDebug("Could not store custom name in ZDO: " + ex.Message); }

            // Local fallbacks also make the name visible immediately on game revisions without native support.
            applied |= TrySetStringMember(character, "m_customName", customName);
            applied |= TrySetStringMember(character, "m_name", customName);
            return applied;
        }

        private static bool TrySetStringMember(object instance, string memberName, string value)
        {
            if (instance == null) return false;
            for (Type current = instance.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(string))
                {
                    try { field.SetValue(instance, value); return true; }
                    catch { }
                }
                PropertyInfo property = current.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.PropertyType == typeof(string) && property.CanWrite)
                {
                    try { property.SetValue(instance, value, null); return true; }
                    catch { }
                }
            }
            return false;
        }

        private bool IsServer()
        {
            if (_zNetInstance == null) return false;
            Type type = _zNetInstance.GetType();
            MethodInfo method = type.GetMethod("IsServer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method == null) return false;
            try { return Convert.ToBoolean(method.Invoke(_zNetInstance, null)); }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not determine server authority: " + ex.Message);
                return false;
            }
        }

        private static Vector3 GetSpawnOffset(int index)
        {
            if (index == 0) return Vector3.zero;
            // Spiral placement prevents a group from appearing at exactly the same point.
            float angle = index * 2.39996323f;
            float radius = 1.1f * Mathf.Sqrt(index);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private bool TryGetCrosshairPosition(out Vector3 position)
        {
            position = Vector3.zero;
            Camera camera = Camera.main;
            if (camera == null) return false;
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray, 250f);
            Array.Sort(hits, delegate(RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); });
            Transform playerTransform = _playerComponent == null ? null : _playerComponent.transform;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null) continue;
                if (playerTransform != null && (hits[i].collider.transform == playerTransform || hits[i].collider.transform.IsChildOf(playerTransform)))
                    continue;
                position = hits[i].point + hits[i].normal * 0.12f;
                return true;
            }
            return false;
        }

        private bool ApplyLevel(GameObject spawnedObject, int level)
        {
            if (spawnedObject == null || _characterType == null) return false;
            Component character = null;
            try { character = spawnedObject.GetComponent(_characterType); }
            catch { }
            if (character == null) return false;
            MethodInfo[] methods = character.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] p = methods[i].GetParameters();
                if (methods[i].Name != "SetLevel" || p.Length < 1 || p[0].ParameterType != typeof(int))
                    continue;

                // Some game versions expose optional parameters on SetLevel. Support these without
                // depending on a specific overload signature.
                object[] args = new object[p.Length];
                args[0] = level;
                bool compatible = true;
                for (int j = 1; j < p.Length; j++)
                {
                    if (!p[j].IsOptional)
                    {
                        compatible = false;
                        break;
                    }
                    args[j] = p[j].DefaultValue;
                }
                if (!compatible) continue;
                try
                {
                    methods[i].Invoke(character, args);
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.LogDebug("Could not apply creature level: " + ex.Message);
                    return false;
                }
            }
            return false;
        }

        private void InstallInputGuards()
        {
            try
            {
                _harmony = new Harmony(PluginGuid + ".InputGuard");
                Type zInputType = FindType("ZInput");
                if (zInputType != null)
                {
                    MethodInfo prefix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(BlockBooleanInputPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                    MethodInfo[] methods = zInputType.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        MethodInfo method = methods[i];
                        if (method.ReturnType != typeof(bool)) continue;
                        string name = method.Name;
                        ParameterInfo[] p = method.GetParameters();
                        bool supported = false;
                        if (name == "GetButton" || name == "GetButtonDown" || name == "GetButtonUp")
                            supported = p.Length == 1 && p[0].ParameterType == typeof(string);
                        else if (name == "GetMouseButton" || name == "GetMouseButtonDown" || name == "GetMouseButtonUp")
                            supported = p.Length == 1 && p[0].ParameterType == typeof(int);
                        else if (name == "GetKey" || name == "GetKeyDown" || name == "GetKeyUp")
                            supported = p.Length >= 1 && p.Length <= 2 && p[0].ParameterType == typeof(KeyCode) &&
                                (p.Length == 1 || p[1].ParameterType == typeof(bool));
                        if (!supported) continue;
                        try { _harmony.Patch(method, prefix: new HarmonyMethod(prefix)); }
                        catch (Exception ex) { Logger.LogDebug("Could not guard " + method.Name + ": " + ex.Message); }
                    }
                }

                // Valheim camera zoom reads ZInput.GetMouseScrollWheel(). Suppress that game input
                // while the IMGUI HUD is open; IMGUI ScrollView still consumes its own scroll events.
                MethodInfo scrollPrefix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(BlockMouseScrollWheelPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo[] scrollMethods = (zInputType ?? FindType("ZInput"))?.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (scrollMethods != null)
                {
                    for (int i = 0; i < scrollMethods.Length; i++)
                    {
                        MethodInfo method = scrollMethods[i];
                        if (method.Name != "GetMouseScrollWheel" || method.ReturnType != typeof(float) || method.GetParameters().Length != 0) continue;
                        try
                        {
                            _harmony.Patch(method, prefix: new HarmonyMethod(scrollPrefix));
                            Logger.LogInfo("Mouse wheel input guard installed for the open spawner HUD.");
                        }
                        catch (Exception ex) { Logger.LogWarning("Could not guard camera zoom scroll input: " + ex.Message); }
                    }
                }

                PatchBooleanInputMethod(FindType("Player"), "TakeInput");
                PatchBooleanInputMethod(FindType("PlayerController"), "TakeInput");

                Type characterType = FindType("Character");
                if (characterType != null)
                {
                    MethodInfo getHoverName = characterType.GetMethod("GetHoverName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                    if (getHoverName != null && getHoverName.ReturnType == typeof(string))
                    {
                        MethodInfo namePostfix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(OverrideCustomNamePostfix), BindingFlags.Static | BindingFlags.NonPublic);
                        try { _harmony.Patch(getHoverName, postfix: new HarmonyMethod(namePostfix)); }
                        catch (Exception ex) { Logger.LogDebug("Could not install custom-name display patch: " + ex.Message); }
                    }
                }

                Type gameCameraType = FindType("GameCamera");
                if (gameCameraType != null)
                {
                    MethodInfo cameraPrefix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(BlockCameraMouseCapturePrefix), BindingFlags.Static | BindingFlags.NonPublic);
                    MethodInfo[] methods = gameCameraType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        if (methods[i].Name != "UpdateMouseCapture" || methods[i].GetParameters().Length != 0) continue;
                        try { _harmony.Patch(methods[i], prefix: new HarmonyMethod(cameraPrefix)); }
                        catch (Exception ex) { Logger.LogDebug("Could not guard camera cursor capture: " + ex.Message); }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Some input guards could not be installed. Cursor forcing remains active. " + ex.Message);
            }
        }

        private void PatchBooleanInputMethod(Type type, string name)
        {
            if (type == null) return;
            MethodInfo prefix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(BlockBooleanInputPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != name || methods[i].ReturnType != typeof(bool)) continue;
                try { _harmony.Patch(methods[i], prefix: new HarmonyMethod(prefix)); }
                catch (Exception ex) { Logger.LogDebug("Could not block " + type.Name + "." + name + ": " + ex.Message); }
            }
        }

        private static bool ShouldBlockGameInput()
        {
            return _activeInstance != null && (_activeInstance._isOpen || Time.unscaledTime < _activeInstance._suppressInputUntil);
        }

        private static bool BlockBooleanInputPrefix(ref bool __result)
        {
            if (!ShouldBlockGameInput()) return true;
            __result = false;
            return false;
        }

        private static bool BlockCameraMouseCapturePrefix()
        {
            return !ShouldBlockGameInput();
        }

        private static bool BlockMouseScrollWheelPrefix(ref float __result)
        {
            if (!ShouldBlockGameInput()) return true;
            __result = 0f;
            return false;
        }

        private static void OverrideCustomNamePostfix(object __instance, ref string __result)
        {
            ValheimSpawnerPlugin plugin = _activeInstance;
            Component character = __instance as Component;
            if (plugin == null || character == null || plugin._zNetViewType == null) return;
            try
            {
                Component nview = character.GetComponent(plugin._zNetViewType);
                if (nview == null) return;
                MethodInfo getZdo = nview.GetType().GetMethod("GetZDO", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                object zdo = getZdo == null ? null : getZdo.Invoke(nview, null);
                if (zdo == null) return;
                MethodInfo[] methods = zdo.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name != "GetString") continue;
                    ParameterInfo[] p = methods[i].GetParameters();
                    object value = null;
                    try
                    {
                        if (p.Length == 1 && p[0].ParameterType == typeof(string))
                            value = methods[i].Invoke(zdo, new object[] { "VCS_CustomName" });
                        else if (p.Length == 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string))
                            value = methods[i].Invoke(zdo, new object[] { "VCS_CustomName", string.Empty });
                    }
                    catch { }
                    string customName = value as string;
                    if (!string.IsNullOrWhiteSpace(customName))
                    {
                        __result = customName;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                if (plugin != null) plugin.Logger.LogDebug("Could not read custom creature name: " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            DisposeNativeHud();
            ResetNetwork();
            DestroyWorldSpawnMarkers();
            try { if (Config != null) Config.Save(); }
            catch (Exception ex) { Logger.LogWarning("BepInEx preferences save failed: " + ex.Message); }
            if (_harmony != null)
            {
                try { _harmony.UnpatchSelf(); }
                catch { }
                _harmony = null;
            }
            if (ReferenceEquals(_activeInstance, this)) _activeInstance = null;
            if (_cursorStateStored)
            {
                Cursor.visible = OtherGuiInputBlocked() || _oldCursorVisible;
                Cursor.lockState = OtherGuiInputBlocked() ? CursorLockMode.None : _oldCursorLockState;
                _cursorStateStored = false;
            }
        }

        private enum ItemCategory
        {
            Weapons = 0,
            Armor = 1,
            Food = 2,
            Materials = 3,
            Other = 4
        }

        private sealed class ItemEntry
        {
            public readonly GameObject Prefab;
            public readonly string PrefabName;
            public readonly string DisplayName;
            public readonly string EnglishName;
            public readonly string LocalizedName;
            public readonly string ItemTypeLabel;
            public readonly ItemCategory Category;
            public readonly int MaxQuality;
            public readonly int MaxStack;
            public readonly bool UsesDurability;
            public readonly Texture2D IconTexture;
            public readonly Rect IconUv;

            public ItemEntry(GameObject prefab, string prefabName, string englishName, string localizedName, string itemTypeLabel, ItemCategory category, int maxQuality, int maxStack, bool usesDurability, Sprite icon)
            {
                Prefab = prefab;
                PrefabName = prefabName ?? string.Empty;
                EnglishName = string.IsNullOrWhiteSpace(englishName) ? HumanizePrefabName(PrefabName) : englishName;
                LocalizedName = string.IsNullOrWhiteSpace(localizedName) ? EnglishName : localizedName;
                DisplayName = EnglishName;
                ItemTypeLabel = itemTypeLabel ?? string.Empty;
                Category = category;
                MaxQuality = Mathf.Clamp(maxQuality, 1, ExtendedNonWeaponQualityCap);
                MaxStack = Mathf.Clamp(maxStack, 1, 9999);
                UsesDurability = usesDurability;
                if (icon != null)
                {
                    try
                    {
                        Texture2D texture = icon.texture;
                        if (texture != null && texture.width > 0 && texture.height > 0)
                        {
                            Rect source = icon.textureRect;
                            IconTexture = texture;
                            IconUv = new Rect(source.x / texture.width, source.y / texture.height,
                                source.width / texture.width, source.height / texture.height);
                        }
                    }
                    catch (Exception) { /* Some atlased sprites are tightly packed. */ }
                }
            }
        }

        private sealed class CreatureEntry
        {
            public readonly GameObject Prefab;
            public readonly string PrefabName;
            public readonly string EnglishName;
            public readonly string RussianName;
            public readonly string LocalizedName;
            public readonly CreatureCategory Category;
            public readonly string[] Biomes;
            public string CategoryLabel { get { return Category == CreatureCategory.Boss ? "Boss" : "Regular"; } }
            public string BiomeLabel { get { return Biomes == null || Biomes.Length == 0 ? "Unknown" : string.Join(", ", Biomes); } }

            public CreatureEntry(GameObject prefab, string prefabName, string englishName, string russianName,
                string localizedName, CreatureCategory category, string[] biomes)
            {
                Prefab = prefab;
                PrefabName = prefabName ?? string.Empty;
                EnglishName = string.IsNullOrWhiteSpace(englishName) ? PrefabName : englishName;
                RussianName = string.IsNullOrWhiteSpace(russianName) ? PrefabName : russianName;
                LocalizedName = localizedName ?? string.Empty;
                Category = category;
                Biomes = biomes ?? new string[] { "Unknown" };
            }
        }
    }
}
