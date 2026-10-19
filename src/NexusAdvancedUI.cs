using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    // Isolated from the v1.3.0 catalogue and spawning paths. All operations reuse
    // existing validated inventory and host-side spawning logic.
    public sealed partial class ValheimSpawnerPlugin
    {
        private const string CustomItemNameKey = NexusItemTextRules.NexusNameKey;
        private const string CustomItemDescriptionKey = NexusItemTextRules.NexusDescriptionKey;
        private ConfigEntry<string> _advancedDataConfig;
        private NexusAdvancedData _advanced = new NexusAdvancedData();
        private string _itemCustomName = string.Empty;
        private string _itemCustomDescription = string.Empty;
        private string _advancedDataPath;
        private bool _advancedSaveBlocked;
        private bool _advancedSaveError;
        private string _newKitName = string.Empty;
        private int _activeKitIndex;
        private int _doctorItemIndex;
        private string _doctorSearch = string.Empty;
        private string _doctorReport = string.Empty;
        private string _radarReport = string.Empty;
        private Vector2 _advancedScroll;
        private bool _showSpawnPreview;

        // The original JSON lived inside a BepInEx ConfigEntry. Keep it as a
        // read-only migration source; do not erase it after upgrading. New state
        // lives in an independent file, alongside the BepInEx configuration.
        private void InitializeNexusAdvanced()
        {
            _advancedDataConfig = Config.Bind("NexusAdvanced", "Data", string.Empty,
                "Legacy v1.4.0 state; retained read-only for migration to SuperNova.ValheimNexus.Advanced.json.");
            _advancedDataPath = Path.Combine(Paths.ConfigPath, "SuperNova.ValheimNexus.Advanced.json");
            Logger.LogInfo("Nexus Advanced storage path: " + _advancedDataPath);
            _advancedSaveBlocked = false;
            _advancedSaveError = false;
            _advanced = new NexusAdvancedData();

            if (File.Exists(_advancedDataPath))
            {
                NexusAdvancedData loaded;
                string reason;
                if (TryLoadAdvancedFile(_advancedDataPath, out loaded, out reason))
                {
                    _advanced = loaded;
                    // Format 1 written by the old Unity serializer might contain
                    // only Format/SnapshotUtc and no collections whatsoever.
                    // Preserve the original file and warn rather than pretending
                    // that a previous Radar snapshot survived migration.
                    string rawState = File.ReadAllText(_advancedDataPath, Encoding.UTF8);
                    if (rawState.IndexOf("\"Format\":1", StringComparison.Ordinal) >= 0 &&
                        (rawState.IndexOf("\"Kits\"", StringComparison.Ordinal) < 0 ||
                         rawState.IndexOf("\"History\"", StringComparison.Ordinal) < 0 ||
                         rawState.IndexOf("\"Snapshot\"", StringComparison.Ordinal) < 0))
                        Logger.LogWarning("Legacy Nexus JSON contains missing collections (Unity JsonUtility data loss). " +
                            "Existing omitted entries cannot be restored from this file; original file kept until next save.");
                    Logger.LogInfo("Nexus Advanced state loaded: " + _advanced.Kits.Count + " kits, " +
                        _advanced.History.Count + " history entries, " + _advanced.Snapshot.Count + " snapshot records.");
                    return;
                }
                Logger.LogWarning("Nexus Advanced primary state is unreadable: " + reason);
                string backup = _advancedDataPath + ".bak";
                if (TryLoadAdvancedFile(backup, out loaded, out reason))
                {
                    _advanced = loaded;
                    try
                    {
                        File.Copy(_advancedDataPath, _advancedDataPath + ".corrupt", true);
                        File.Copy(backup, _advancedDataPath, true);
                        Logger.LogWarning("Restored Nexus Advanced state from .bak; damaged main file preserved as .corrupt.");
                    }
                    catch (Exception ex)
                    {
                        _advancedSaveBlocked = true;
                        Logger.LogError("Nexus state recovered in memory but cannot restore primary file; writes disabled: " + ex.Message);
                    }
                    return;
                }
                NexusAdvancedData legacyRecovery;
                string legacyReason;
                if (!string.IsNullOrWhiteSpace(_advancedDataConfig.Value) &&
                    NexusAdvancedData.TryLoad(_advancedDataConfig.Value, out legacyRecovery, out legacyReason))
                {
                    _advanced = legacyRecovery;
                    _advancedSaveBlocked = true; // Do not overwrite either damaged file automatically.
                    Logger.LogWarning("Recovered legacy Nexus data in memory, but existing file is corrupt. Writes disabled until backup is made manually.");
                    return;
                }
                _advancedSaveBlocked = true;
                Logger.LogError("Nexus Advanced state cannot be read and no usable backup exists. Writes disabled to protect existing data. " + reason);
                return;
            }

            // If the main state was deleted but its backup remains, restore from
            // the backup before trying the legacy ConfigEntry value.
            NexusAdvancedData orphanBackup;
            string orphanReason;
            if (TryLoadAdvancedFile(_advancedDataPath + ".bak", out orphanBackup, out orphanReason))
            {
                _advanced = orphanBackup;
                try
                {
                    File.Copy(_advancedDataPath + ".bak", _advancedDataPath);
                    Logger.LogWarning("Nexus Advanced primary JSON was missing; restored from .bak.");
                }
                catch (Exception ex)
                {
                    _advancedSaveBlocked = true;
                    Logger.LogError("Backup recovered in memory, but could not be copied to main state. Writes disabled: " + ex.Message);
                }
                return;
            }

            NexusAdvancedData oldData;
            string legacyError;
            string legacy = _advancedDataConfig.Value;
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                if (!NexusAdvancedData.TryLoad(legacy, out oldData, out legacyError))
                {
                    _advancedSaveBlocked = true;
                    Logger.LogError("Legacy Nexus Advanced state could not be decoded; refusing to overwrite it. " + legacyError);
                    return;
                }
                _advanced = oldData;
                Logger.LogInfo("Migrating legacy Nexus Advanced configuration: " + _advanced.Kits.Count + " kits.");
                SaveNexusAdvanced();
            }
        }

        private static bool TryLoadAdvancedFile(string path, out NexusAdvancedData value, out string reason)
        {
            value = null;
            reason = "File does not exist.";
            if (!File.Exists(path)) return false;
            try
            {
                return NexusAdvancedData.TryLoad(File.ReadAllText(path, Encoding.UTF8), out value, out reason);
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool SaveNexusAdvanced()
        {
            if (_advancedSaveBlocked)
            {
                _status = T("Nexus data could not be read; saving is disabled to protect your old data.",
                    "Данные Nexus не прочитаны; сохранение отключено для защиты старых данных.");
                return false;
            }
            if (_advancedDataPath == null || _advanced == null) return false;
            string staging = _advancedDataPath + ".tmp";
            try
            {
                string json = _advanced.Save();
                NexusAdvancedData verified;
                string reason;
                if (!NexusAdvancedData.TryLoad(json, out verified, out reason) ||
                    !_advanced.SamePersistedContent(verified))
                    throw new InvalidDataException("State JSON serialization failed verification: " + reason);
                Directory.CreateDirectory(Path.GetDirectoryName(_advancedDataPath));
                File.WriteAllText(staging, json, new UTF8Encoding(false));
                if (!TryLoadAdvancedFile(staging, out verified, out reason) ||
                    !_advanced.SamePersistedContent(verified))
                    throw new InvalidDataException("Staged state file failed verification: " + reason);

                // Keep the pre-migration Format 1 source separately. In affected
                // Unity builds it may contain a timestamp but no serialized lists.
                bool migratedFromFormat1 = false;
                if (File.Exists(_advancedDataPath))
                {
                    string previousJson = File.ReadAllText(_advancedDataPath, Encoding.UTF8);
                    NexusAdvancedData previousData;
                    string previousReason;
                    if (!NexusAdvancedData.TryLoad(previousJson, out previousData, out previousReason))
                        throw new InvalidDataException("Existing state changed or became invalid before save: " + previousReason);
                    migratedFromFormat1 = previousJson.IndexOf("\"Format\":1", StringComparison.Ordinal) >= 0;
                    if (migratedFromFormat1)
                    {
                        string legacyBackup = _advancedDataPath + ".legacy-format1.json";
                        if (!File.Exists(legacyBackup)) File.Copy(_advancedDataPath, legacyBackup);
                    }
                }
                if (!File.Exists(_advancedDataPath))
                {
                    File.Move(staging, _advancedDataPath);
                    // Preserve a first-generation recovery point immediately,
                    // not only after a second write (which may never happen).
                    File.Copy(_advancedDataPath, _advancedDataPath + ".bak", false);
                }
                else
                {
                    // Windows supports atomic replacement while retaining the previous
                    // good version. If unavailable, preserve the old copy first.
                    try { File.Replace(staging, _advancedDataPath, _advancedDataPath + ".bak"); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(_advancedDataPath, _advancedDataPath + ".bak", true);
                        File.Delete(_advancedDataPath);
                        File.Move(staging, _advancedDataPath);
                    }
                }
                if (!TryLoadAdvancedFile(_advancedDataPath, out verified, out reason) ||
                    !_advanced.SamePersistedContent(verified))
                    throw new InvalidDataException("Committed Nexus state differs from memory: " + reason);
                // A first migration should have a useful recovery copy immediately;
                // the original v1 source remains in .legacy-format1.json.
                if (migratedFromFormat1)
                    File.Copy(_advancedDataPath, _advancedDataPath + ".bak", true);
                _advancedSaveError = false;
                Logger.LogInfo("Nexus Advanced state saved: " + _advanced.Kits.Count + " kits, " +
                    _advanced.History.Count + " history entries, " +
                    _advanced.Snapshot.Count + " snapshot records to " + _advancedDataPath);
                return true;
            }
            catch (Exception ex)
            {
                _advancedSaveError = true;
                Logger.LogError("Nexus Advanced state save failed: " + ex);
                _status = T("Could not save Nexus data. Check BepInEx log and folder permissions.",
                    "Не удалось сохранить данные Nexus. Проверь журнал BepInEx и права на папку.");
                return false;
            }
            finally
            {
                try { if (File.Exists(staging)) File.Delete(staging); } catch { }
            }
        }

        private void RecordNexusHistory(string kind, string prefab, int count, int quality, int level, string name, string description = "")
        {
            if (count <= 0 || _advanced == null) return;
            _advanced.AddHistory(kind, prefab, count, quality, level, name, description);
            SaveNexusAdvanced();
        }

        private void DrawNexusAdvancedTabs()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("NEXUS ADVANCED", GUILayout.Width(142f));
            DrawMainTabButton(T("Doctor", "Диагностика"), 7);
            DrawMainTabButton(T("Kits", "Наборы"), 8);
            DrawMainTabButton(T("History", "История"), 9);
            DrawMainTabButton(T("Radar", "Радар"), 10);
            GUILayout.EndHorizontal();
            if (_advancedSaveBlocked || _advancedSaveError)
                GUILayout.Label(T("WARNING: Nexus data was not saved. Check BepInEx log; do not delete old configuration files.",
                    "ВНИМАНИЕ: данные Nexus не сохранены. Проверь лог BepInEx; не удаляй старые конфиги."));
        }

        private void DrawNexusAdvancedPanel()
        {
            _advancedScroll = GUILayout.BeginScrollView(_advancedScroll, GUILayout.ExpandHeight(true));
            switch (_mainTab)
            {
                case 7: DrawDoctorPanel(); break;
                case 8: DrawKitsPanel(); break;
                case 9: DrawHistoryPanel(); break;
                case 10: DrawRadarPanel(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.Label(_status, GUILayout.ExpandWidth(true));
        }

        private void DrawDoctorPanel()
        {
            GUILayout.Label(T("Nexus Doctor — inspect registered item prefabs. Results are advisory, not an in-game equipment test.",
                "Nexus Doctor — проверка зарегистрированных префабов. Результат не заменяет тест экипировки в игре."));
            if (_itemEntries.Count == 0)
            {
                GUILayout.Label(T("Enter a world and refresh catalogues first.", "Войди в мир и обнови каталоги."));
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Filter ID:", "Поиск ID:"), GUILayout.Width(85f));
            GUI.SetNextControlName("ValheimSpawner.DoctorSearch");
            _doctorSearch = GUILayout.TextField(_doctorSearch, GUILayout.ExpandWidth(true));
            _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimSpawner.DoctorSearch";
            GUILayout.EndHorizontal();
            var matches = new List<ItemEntry>();
            for (int i = 0; i < _itemEntries.Count && matches.Count < 250; i++)
                if (string.IsNullOrWhiteSpace(_doctorSearch) ||
                    _itemEntries[i].PrefabName.IndexOf(_doctorSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    matches.Add(_itemEntries[i]);
            if (matches.Count == 0) { GUILayout.Label(T("No matching items.", "Предметы не найдены.")); return; }
            _doctorItemIndex = Mathf.Clamp(_doctorItemIndex, 0, matches.Count - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(34f))) _doctorItemIndex = (_doctorItemIndex + matches.Count - 1) % matches.Count;
            GUILayout.Label(matches[_doctorItemIndex].PrefabName + " — " + GetItemDisplayName(matches[_doctorItemIndex]));
            if (GUILayout.Button("▶", GUILayout.Width(34f))) _doctorItemIndex = (_doctorItemIndex + 1) % matches.Count;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Run Diagnostics", "Проверить"), GUILayout.Height(34f)))
                _doctorReport = InspectItemPrefab(matches[_doctorItemIndex]);
            GUI.enabled = !string.IsNullOrEmpty(_doctorReport);
            if (GUILayout.Button(T("Copy Report", "Копировать отчёт"), GUILayout.Height(34f)))
            {
                GUIUtility.systemCopyBuffer = _doctorReport;
                _status = T("Diagnostic report copied.", "Диагностический отчёт скопирован.");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_doctorReport)) GUILayout.TextArea(_doctorReport, GUILayout.MinHeight(280f));
        }

        private string InspectItemPrefab(ItemEntry entry)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SuperNova Nexus Doctor v" + PluginVersion);
            sb.AppendLine("Prefab ID: " + entry.PrefabName);
            sb.AppendLine("Category: " + entry.Category + " | Type: " + entry.ItemTypeLabel);
            sb.AppendLine("Max stack: " + entry.MaxStack + " | UI quality limit: " + entry.MaxQuality);
            sb.AppendLine("Durability: " + (entry.UsesDurability ? "yes" : "no"));
            sb.AppendLine("Icon: " + (entry.IconTexture != null ? "present" : "MISSING"));
            GameObject prefab = entry.Prefab;
            if (prefab == null) { sb.AppendLine("[FAIL] Prefab reference is missing."); return sb.ToString(); }
            sb.AppendLine("[OK] Prefab: " + prefab.name);
            Component itemDrop = _itemDropType == null ? null : prefab.GetComponent(_itemDropType);
            object itemData = ReadMember(itemDrop, "m_itemData");
            object shared = ReadMember(itemData, "m_shared");
            sb.AppendLine(itemDrop == null ? "[FAIL] ItemDrop missing" : "[OK] ItemDrop present");
            sb.AppendLine(itemData == null ? "[FAIL] ItemData missing" : "[OK] ItemData present");
            sb.AppendLine(shared == null ? "[FAIL] SharedData missing" : "[OK] SharedData present");
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            int validRenderers = 0;
            int missingMaterials = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                validRenderers++;
                Material[] materials = renderers[i].sharedMaterials;
                if (materials == null || materials.Length == 0) missingMaterials++;
                else for (int j = 0; j < materials.Length; j++) if (materials[j] == null) missingMaterials++;
            }
            sb.AppendLine("Renderer components on item prefab: " + validRenderers);
            sb.AppendLine("Missing renderer material slots: " + missingMaterials);
            int attachments = 0;
            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i] != null && transforms[i].name.StartsWith("attach", StringComparison.OrdinalIgnoreCase)) attachments++;
            sb.AppendLine("Attachment-like transforms: " + attachments);
            sb.AppendLine("Important: an equipped visual may use separate attach prefabs and skeleton bones.");
            sb.AppendLine("If the model is invisible, verify the result by equipping the item in-game.");
            return sb.ToString();
        }

        private void AddSelectedItemToKit()
        {
            if (_selectedItem == null || _advanced.Kits.Count == 0)
            {
                _status = T("Select an item and create a kit first.", "Выбери предмет и сначала создай набор.");
                return;
            }
            _activeKitIndex = Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1);
            NexusKit kit = _advanced.Kits[_activeKitIndex];
            if (kit == null || kit.Items == null || kit.Items.Count >= NexusKitWorkshop.MaxParts)
            {
                _status = T("Kit limit: 100 item rows.", "Ограничение набора: 100 строк предметов.");
                return;
            }
            int count;
            if (!TryParseItemAmount(_itemCountInput, out count))
            {
                _status = T("Enter a valid count first.", "Сначала введи корректное количество.");
                return;
            }
            string name = NexusAdvancedData.CleanName(_itemCustomName);
            string description = NexusAdvancedData.CleanDescription(_itemCustomDescription);
            if ((name.Length > 0 || description.Length > 0) && _selectedItem.MaxStack > 1)
            {
                _status = T("Custom names require max stack 1.", "Своё имя требует размера стака 1.");
                return;
            }
            var part = new NexusKitPart
            {
                PrefabId = _selectedItem.PrefabName,
                Count = Mathf.Clamp(count, 1, 9999),
                Quality = Mathf.Clamp(_itemQuality, 1, _selectedItem.MaxQuality),
                CustomName = name,
                CustomDescription = description
            };
            kit.Items.Add(part);
            if (SaveNexusAdvanced())
                _status = T("Item added to kit: ", "Предмет добавлен в набор: ") + kit.Name;
            else kit.Items.Remove(part);
        }

        private void DrawKitsPanel()
        {
            GUILayout.Label(T("Equipment Kits — save item IDs, counts, quality, custom names and descriptions.",
                "Наборы — хранение ID, количества, качества, собственных имён и описаний."));
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("ValheimSpawner.KitName");
            _newKitName = GUILayout.TextField(_newKitName, 48, GUILayout.MinWidth(150f), GUILayout.ExpandWidth(true));
            _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimSpawner.KitName";
            if (GUILayout.Button(T("New Kit", "Создать набор"), GUILayout.Width(130f)))
            {
                string name = NexusAdvancedData.CleanName(_newKitName);
                if (name.Length == 0) _status = T("Enter a kit name.", "Введи название набора.");
                else if (_advanced.Kits.Count >= 25) _status = T("Kit limit reached (25).", "Лимит наборов: 25.");
                else
                {
                    _advanced.Kits.Add(new NexusKit { Name = name });
                    _activeKitIndex = _advanced.Kits.Count - 1;
                    _newKitName = string.Empty;
                    SaveNexusAdvanced();
                }
            }
            GUILayout.EndHorizontal();
            if (_advanced.Kits.Count == 0)
            {
                GUILayout.Label(T("Create a kit, then select an item in its category and click Add to kit.",
                    "Создай набор, затем выбери предмет в каталоге и нажми «В набор»."));
                return;
            }
            _activeKitIndex = Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(34f))) _activeKitIndex = (_activeKitIndex + _advanced.Kits.Count - 1) % _advanced.Kits.Count;
            NexusKit selected = _advanced.Kits[_activeKitIndex];
            GUILayout.Label(selected.Name + " (" + selected.Items.Count + "/" + NexusKitWorkshop.MaxParts + ")");
            if (GUILayout.Button("▶", GUILayout.Width(34f))) _activeKitIndex = (_activeKitIndex + 1) % _advanced.Kits.Count;
            if (GUILayout.Button(T("Delete Kit", "Удалить набор"), GUILayout.Width(110f)))
            {
                _advanced.Kits.RemoveAt(_activeKitIndex);
                _activeKitIndex = Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1);
                SaveNexusAdvanced();
                return;
            }
            GUILayout.EndHorizontal();
            if (DrawKitWorkshopActions(selected)) return;
            for (int i = selected.Items.Count - 1; i >= 0; i--)
            {
                NexusKitPart part = selected.Items[i];
                if (part == null) continue;
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(part.PrefabId + " ×" + part.Count + "  Q" + part.Quality +
                    (string.IsNullOrEmpty(part.CustomName) ? "" : "  [" + part.CustomName + "]") +
                    (string.IsNullOrEmpty(part.CustomDescription) ? "" : "  [custom description]"));
                if (GUILayout.Button(T("Edit", "Изменить"), GUILayout.Width(90f))) BeginEditKitPart(selected, i);
                if (GUILayout.Button(T("Remove", "Убрать"), GUILayout.Width(90f)))
                {
                    selected.Items.RemoveAt(i);
                    _editingKitPart = -1;
                    SaveNexusAdvanced();
                }
                GUILayout.EndHorizontal();
            }
            DrawKitPartEditor(selected);
            if (GUILayout.Button(T("Apply Kit to Inventory", "Добавить набор в инвентарь"), GUILayout.Height(36f)))
                ApplySelectedKit(selected);
            GUILayout.Label(T("Safety: at most 1,000 total units per application; incomplete insertion is reported. No auto-equip.",
                "Ограничение: максимум 1 000 предметов за применение; неполное добавление отображается. Без автоэкипировки."));
        }

        private void ApplySelectedKit(NexusKit kit)
        {
            if (!NexusKitWorkshop.Validate(kit, out string reason)) { _status = reason; return; }
            kit = NexusKitWorkshop.DeepCopy(kit);
            if (!AuthorizeLocal(() => ApplySelectedKit(kit), "apply kit " + kit.Name)) return;
            RefreshGameReferences();
            if (!HasAdminAccess || _playerComponent == null) { _status = T("Kit insertion requires a host player in a world.", "Для набора нужен игрок-хост в мире."); return; }
            object inventory = GetPlayerInventory();
            if (inventory == null) { _status = T("Inventory unavailable.", "Инвентарь недоступен."); return; }
            int total = 0;
            var entries = new List<ItemEntry>();
            foreach (NexusKitPart part in kit.Items)
            {
                if (part == null || part.Count < 1 || part.Count > 9999) { _status = T("Invalid kit item amount.", "Недопустимое количество в наборе."); return; }
                total += part.Count;
                if (total > 1000) { _status = T("Kit exceeds 1,000 items per action.", "Набор превышает 1 000 предметов за одно действие."); return; }
                ItemEntry entry = FindItemById(part.PrefabId);
                if (entry == null) { _status = T("Missing prefab: ", "Нет префаба: ") + part.PrefabId; return; }
                if ((!string.IsNullOrEmpty(part.CustomName) || !string.IsNullOrEmpty(part.CustomDescription)) && entry.MaxStack > 1)
                { _status = T("Named stack is unsupported: ", "Именованный стак не поддерживается: ") + part.PrefabId; return; }
                entries.Add(entry);
            }
            int added = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                NexusKitPart part = kit.Items[i];
                InventoryAddResult result = AddItemBatches(inventory, entries[i], part.Count,
                    Mathf.Clamp(part.Quality, 1, entries[i].MaxQuality), NexusAdvancedData.CleanName(part.CustomName),
                    NexusAdvancedData.CleanDescription(part.CustomDescription));
                added += result.Added;
                if (result.Added != part.Count)
                {
                    _status = T("Kit partially applied: ", "Набор добавлен частично: ") + added + "/" + total + ". " + result.Reason;
                    if (added > 0) RecordNexusHistory("kit", kit.Name, added, 1, 1, string.Empty);
                    return;
                }
            }
            if (added > 0) RecordNexusHistory("kit", kit.Name, added, 1, 1, string.Empty);
            _status = T("Kit applied: ", "Набор добавлен: ") + kit.Name + " ×" + added;
        }

        private ItemEntry FindItemById(string prefabId)
        {
            for (int i = 0; i < _itemEntries.Count; i++)
                if (string.Equals(_itemEntries[i].PrefabName, prefabId, StringComparison.OrdinalIgnoreCase))
                    return _itemEntries[i];
            return null;
        }

        private void DrawHistoryPanel()
        {
            GUILayout.Label(T("Recent Nexus operations (last 100). Reuse fills controls; it never automatically repeats a spawn.",
                "Последние 100 операций Nexus. Повтор заполняет поля, но не создаёт объекты автоматически."));
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Search:", "Поиск:"), GUILayout.Width(65f));
            GUI.SetNextControlName("ValheimNexus.HistorySearch");
            _historyFilter = GUILayout.TextField(_historyFilter, GUILayout.ExpandWidth(true));
            _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimNexus.HistorySearch";
            if (GUILayout.Button(T("Clear History", "Очистить историю"), GUILayout.Width(160f)))
            {
                _advanced.History.Clear();
                SaveNexusAdvanced();
            }
            GUILayout.EndHorizontal();
            _historyKindFilter = GUILayout.Toolbar(_historyKindFilter,
                new[] { T("All", "Все"), T("Items", "Предметы"), T("Creatures", "Существа"), T("Kits", "Наборы") });
            int matched = 0;
            for (int i = 0; i < _advanced.History.Count; i++)
            {
                NexusHistoryRecord record = _advanced.History[i];
                if (record == null) continue;
                if (_historyKindFilter == 1 && record.Kind != "item" ||
                    _historyKindFilter == 2 && record.Kind != "creature" ||
                    _historyKindFilter == 3 && record.Kind != "kit") continue;
                if (!string.IsNullOrWhiteSpace(_historyFilter) &&
                    (record.PrefabId ?? string.Empty).IndexOf(_historyFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (record.CustomName ?? string.Empty).IndexOf(_historyFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (record.CustomDescription ?? string.Empty).IndexOf(_historyFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                matched++;
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(record.Utc + " | " + record.Kind + " | " + record.PrefabId + " ×" + record.Count +
                    (string.IsNullOrEmpty(record.CustomName) ? "" : " [" + record.CustomName + "]"));
                if (record.Kind != "kit" && GUILayout.Button(T("Reuse", "Повторить"), GUILayout.Width(95f)))
                    LoadHistoryControls(record);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(T("Matching entries: ", "Найдено записей: ") + matched.ToString());
        }

        private void LoadHistoryControls(NexusHistoryRecord record)
        {
            if (record.Kind == "item")
            {
                ItemEntry entry = FindItemById(record.PrefabId);
                if (entry == null) { _status = T("Item is no longer registered.", "Предмет больше не зарегистрирован."); return; }
                _selectedItem = entry;
                _mainTab = 1 + (int)entry.Category;
                _searchAllItems = true;
                _itemFavoritesOnly = false;
                _itemSearch = record.PrefabId;
                _itemCount = Mathf.Clamp(record.Count, 1, 9999);
                _itemCountInput = _itemCount.ToString();
                _itemQuality = Mathf.Clamp(record.Quality, 1, entry.MaxQuality);
                _itemCustomName = NexusAdvancedData.CleanName(record.CustomName);
                _itemCustomDescription = NexusAdvancedData.CleanDescription(record.CustomDescription);
            }
            else if (record.Kind == "creature")
            {
                CreatureEntry entry = null;
                for (int i = 0; i < _entries.Count; i++)
                    if (string.Equals(_entries[i].PrefabName, record.PrefabId, StringComparison.OrdinalIgnoreCase)) { entry = _entries[i]; break; }
                if (entry == null) { _status = T("Creature is no longer registered.", "Существо больше не зарегистрировано."); return; }
                _selected = entry;
                _mainTab = 0;
                _creatureFavoritesOnly = false;
                _categoryFilter = 0;
                _biomeFilter = 0;
                _search = record.PrefabId;
                _count = Mathf.Clamp(record.Count, 1, 50);
                _level = Mathf.Clamp(record.Level, 1, 3);
                _customName = NexusAdvancedData.CleanName(record.CustomName);
            }
            _status = T("Settings restored. Review them before spawning.", "Параметры восстановлены. Проверь перед созданием.");
        }

        private void DrawRadarPanel()
        {
            GUILayout.Label(T("Compatibility Radar — compare runtime prefab catalogues with a saved snapshot.",
                "Радар совместимости — сравнение игровых каталогов с сохранённым снимком."));
            GUILayout.Label(T("Baseline: ", "Базовый снимок: ") +
                (string.IsNullOrEmpty(_advanced.SnapshotUtc) ? T("none", "отсутствует") : _advanced.SnapshotUtc));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Compare Catalogues", "Сравнить каталоги"), GUILayout.Height(36f)))
                _radarReport = CompareCatalogues();
            if (GUILayout.Button(T("Save Snapshot", "Сохранить снимок"), GUILayout.Height(36f)))
            {
                _advanced.Snapshot = BuildCatalogueSnapshot();
                _advanced.SnapshotUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                if (SaveNexusAdvanced())
                    _radarReport = T("Snapshot saved. Compare again after a game or mod update.",
                        "Снимок сохранён. Сравни после обновления игры или модов.");
                else
                    _radarReport = T("Snapshot save failed! Check BepInEx log.",
                        "Снимок не сохранён! Проверь журнал BepInEx.");
            }
            GUILayout.EndHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(_radarReport);
            if (GUILayout.Button(T("Copy Comparison", "Копировать сравнение"), GUILayout.Width(190f)))
                GUIUtility.systemCopyBuffer = _radarReport;
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(_radarReport)) GUILayout.TextArea(_radarReport, GUILayout.MinHeight(150f));
        }

        private List<NexusCatalogueRecord> BuildCatalogueSnapshot()
        {
            var list = new List<NexusCatalogueRecord>();
            for (int i = 0; i < _itemEntries.Count && list.Count < 4500; i++)
            {
                ItemEntry e = _itemEntries[i];
                list.Add(new NexusCatalogueRecord
                {
                    Id = "item:" + e.PrefabName,
                    Signature = e.ItemTypeLabel + "/" + e.Category + "/" + e.MaxStack + "/" + e.MaxQuality + "/" + e.UsesDurability + "/" + (e.IconTexture != null)
                });
            }
            for (int i = 0; i < _entries.Count && list.Count < 4500; i++)
                list.Add(new NexusCatalogueRecord { Id = "creature:" + _entries[i].PrefabName,
                    Signature = _entries[i].Category.ToString() });
            return list;
        }

        private string CompareCatalogues()
        {
            if (_advanced.Snapshot.Count == 0) return T("No baseline. Save a snapshot first.", "Нет базового снимка. Сначала сохрани его.");
            if (_itemEntries.Count == 0 || _entries.Count == 0) return T("Load both catalogues in a world first.", "Сначала загрузи оба каталога в мире.");
            var oldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (NexusCatalogueRecord e in _advanced.Snapshot)
                if (e != null && !string.IsNullOrEmpty(e.Id) && !oldMap.ContainsKey(e.Id)) oldMap[e.Id] = e.Signature;
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (NexusCatalogueRecord e in BuildCatalogueSnapshot())
                if (!current.ContainsKey(e.Id)) current[e.Id] = e.Signature;
            var diff = new StringBuilder();
            int added = 0, missing = 0, changed = 0;
            foreach (KeyValuePair<string, string> e in current)
            {
                string previous;
                if (!oldMap.TryGetValue(e.Key, out previous)) { added++; if (added <= 25) diff.AppendLine("+ " + e.Key); }
                else if (previous != e.Value) { changed++; if (changed <= 25) diff.AppendLine("~ " + e.Key); }
            }
            foreach (KeyValuePair<string, string> e in oldMap)
                if (!current.ContainsKey(e.Key)) { missing++; if (missing <= 25) diff.AppendLine("- " + e.Key); }
            return "Nexus Radar: +" + added + " / -" + missing + " / changed " + changed + "\n" +
                "Baseline: " + _advanced.SnapshotUtc + "\n" + diff +
                "Note: registry changes do not prove mod incompatibility.";
        }

        private void DrawSpawnPreview()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_showSpawnPreview ? T("Hide details", "Скрыть данные") :
                T("Preview details", "Параметры спавна"), GUILayout.Height(28f))) _showSpawnPreview = !_showSpawnPreview;
            bool wantMarkers = GUILayout.Toggle(_spawnMarkerConfig != null && _spawnMarkerConfig.Value,
                T("3D markers", "3D-маркеры"), GUILayout.Width(122f));
            if (_spawnMarkerConfig != null && wantMarkers != _spawnMarkerConfig.Value)
            {
                _spawnMarkerConfig.Value = wantMarkers;
                try { Config.Save(); } catch (Exception ex) { Logger.LogWarning("Could not save marker preference: " + ex.Message); }
                if (!wantMarkers) DestroyWorldSpawnMarkers();
            }
            if (_spawnMarkerConfig != null && _spawnMarkerConfig.Value && _spawnMarkerCrosshairConfig != null)
            {
                bool mode = _spawnMarkerCrosshairConfig.Value;
                if (GUILayout.Button(mode ? T("At crosshair", "Под прицелом") : T("In front", "Перед собой"), GUILayout.Width(142f), GUILayout.Height(28f)))
                {
                    _spawnMarkerCrosshairConfig.Value = !mode;
                    try { Config.Save(); } catch (Exception ex) { Logger.LogWarning("Could not save marker mode: " + ex.Message); }
                }
            }
            GUILayout.EndHorizontal();
            if (!_showSpawnPreview) return;
            GUILayout.BeginVertical("box");
            if (_selected == null) GUILayout.Label(T("Choose a creature to preview.", "Выбери существо для предпросмотра."));
            else
            {
                GUILayout.Label("Prefab ID: " + _selected.PrefabName);
                GUILayout.Label(T("Quantity: ", "Количество: ") + _count + T("  |  Level: ", "  |  Уровень: ") + _level);
                GUILayout.Label(T("Name: ", "Имя: ") + (string.IsNullOrWhiteSpace(_customName) ? T("default", "по умолчанию") : NexusAdvancedData.CleanName(_customName)));
                GUILayout.Label(T("Placement: 3 m ahead (front) or at crosshair; nearby instances are offset.",
                    "Размещение: 3 м впереди или под прицелом; дополнительные существа смещаются."));
                if (!HasAdminAccess || _playerComponent == null)
                    GUILayout.Label(T("Warning: only the host/server may spawn these creatures.",
                        "Внимание: создание доступно только хосту/серверу."));
                if (_selected.Category == CreatureCategory.Boss || _count > 10)
                    GUILayout.Label(T("Caution: boss or large group selected.", "Внимание: выбран босс или большая группа."));
                GUILayout.Label(T("Preview is informational. No objects are created until you press Spawn.",
                    "Это информационный предпросмотр; объекты создаются только по нажатию Spawn."));
            }
            GUILayout.EndVertical();
        }

        // Serialized on the item instance. Never rename the prefab or mutate shared
        // ItemDrop.SharedData: that can break unrelated items and game lookups.
        private static bool TryStampCustomItemName(object item, string rawName, string rawDescription = "")
        {
            string name = NexusAdvancedData.CleanName(rawName);
            string description = NexusAdvancedData.CleanDescription(rawDescription);
            // ItemData.Clone can shallow-copy dictionary references. Always detach the
            // dictionary before stamping, or renaming an instance may rename prefab data.
            // Remove Namekeeper metadata too: its explicit reset marker has precedence
            // over Nexus metadata and would otherwise resurrect an old displayed name.
            IDictionary<string, string> inherited = ReadMember(item, "m_customData") as IDictionary<string, string>;
            var customData = inherited == null ? new Dictionary<string, string>() :
                new Dictionary<string, string>(inherited);
            NexusItemTextRules.Stamp(customData, name, description);
            return TrySetReferenceMember(item, "m_customData", customData);
        }

        // Namekeeper shares Nexus's item metadata and uses its own keys for edits.
        // Its reset marker intentionally overrides the old Nexus value. Capture the
        // effective name/description so a kit receives the text currently shown in game.
        private static string ReadEffectiveItemNameForKit(object item)
        {
            IDictionary<string, string> customData = ReadMember(item, "m_customData") as IDictionary<string, string>;
            return NexusAdvancedData.CleanName(NexusItemTextRules.ReadName(customData, ReadCustomItemName(item)));
        }

        private static string ReadEffectiveItemDescriptionForKit(object item)
        {
            IDictionary<string, string> customData = ReadMember(item, "m_customData") as IDictionary<string, string>;
            return NexusAdvancedData.CleanDescription(NexusItemTextRules.ReadDescription(customData, ReadCustomItemDescription(item)));
        }

        private static string ReadCustomItemName(object item)
        {
            IDictionary<string, string> customData = ReadMember(item, "m_customData") as IDictionary<string, string>;
            string value;
            return customData != null && customData.TryGetValue(CustomItemNameKey, out value)
                ? NexusAdvancedData.CleanName(value) : string.Empty;
        }

        private static string ReadCustomItemDescription(object item)
        {
            IDictionary<string, string> customData = ReadMember(item, "m_customData") as IDictionary<string, string>;
            string value;
            return customData != null && customData.TryGetValue(CustomItemDescriptionKey, out value)
                ? NexusAdvancedData.CleanDescription(value) : string.Empty;
        }

        // Valheim 1.0.20 builds the inventory header independently from
        // ItemData.GetTooltip (which contains only the *description*).
        // Do not patch that description: patch the inventory tooltip widget
        // and both world-hover display paths instead.
        private void InstallCustomItemTooltipPatch()
        {
            if (_harmony == null || _itemDropType == null) return;
            Type itemDataType = _itemDropType.GetNestedType("ItemData", BindingFlags.Public | BindingFlags.NonPublic);
            if (itemDataType == null)
            {
                Logger.LogWarning("Custom item names unavailable: ItemDrop.ItemData type was not found.");
                return;
            }
            var installed = new List<string>();
            PatchCustomNameMethod(_itemDropType, "GetHoverName", nameof(CustomNameHoverPostfix), installed);
            PatchCustomNameMethod(_itemDropType, "GetHoverText", nameof(CustomNameHoverTextPostfix), installed);

            Type inventoryGridType = FindType("InventoryGrid");
            if (inventoryGridType != null)
            {
                MethodInfo handler = typeof(ValheimSpawnerPlugin).GetMethod(nameof(CustomInventoryTooltipPostfix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                foreach (MethodInfo method in inventoryGridType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (method.Name != "CreateItemTooltip") continue;
                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length < 2 || args[0].ParameterType != itemDataType ||
                        args[1].ParameterType.Name != "UITooltip") continue;
                    try
                    {
                        _harmony.Patch(method, postfix: new HarmonyMethod(handler));
                        installed.Add("InventoryGrid.CreateItemTooltip");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Failed to hook InventoryGrid.CreateItemTooltip: " + ex.Message);
                    }
                }
            }
            else Logger.LogWarning("Custom inventory item names unavailable: InventoryGrid type not found.");
            // Replace the vanilla description inside the generated tooltip, never
            // modify shared data and never discard stat lines.
            MethodInfo descriptionPostfix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(CustomDescriptionTooltipPostfix),
                BindingFlags.NonPublic | BindingFlags.Static);
            foreach (MethodInfo method in itemDataType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.Name != "GetTooltip" || method.ReturnType != typeof(string)) continue;
                ParameterInfo[] p = method.GetParameters();
                if (p.Length < 1 || p[0].ParameterType != itemDataType) continue;
                try
                {
                    _harmony.Patch(method, postfix: new HarmonyMethod(descriptionPostfix));
                    installed.Add("ItemData.GetTooltip(description)");
                }
                catch (Exception ex) { Logger.LogWarning("Custom description tooltip hook failed: " + ex.Message); }
            }
            Logger.LogInfo("Custom item name hooks: " + installed.Count +
                (installed.Count == 0 ? " (none)" : " => " + string.Join(", ", installed.ToArray())));
        }

        private void PatchCustomNameMethod(Type type, string methodName, string handlerName, List<string> installed)
        {
            MethodInfo target = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (target == null || target.ReturnType != typeof(string))
            {
                Logger.LogWarning("Custom item name hook not found: ItemDrop." + methodName);
                return;
            }
            try
            {
                MethodInfo handler = typeof(ValheimSpawnerPlugin).GetMethod(handlerName,
                    BindingFlags.NonPublic | BindingFlags.Static);
                _harmony.Patch(target, postfix: new HarmonyMethod(handler));
                installed.Add("ItemDrop." + methodName);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to hook ItemDrop." + methodName + ": " + ex.Message);
            }
        }

        // Used by the game's hover-name API (not by the on-screen pickup prompt).
        private static void CustomNameHoverPostfix(object __instance, ref string __result)
        {
            string name = ReadCustomItemName(ReadMember(__instance, "m_itemData"));
            if (name.Length > 0) __result = name;
        }

        // The pickup prompt uses GetHoverText, not GetHoverName. Replace only
        // its first line; keep the pickup key and all other instructions.
        private static void CustomNameHoverTextPostfix(object __instance, ref string __result)
        {
            object item = ReadMember(__instance, "m_itemData");
            string name = ReadCustomItemName(item);
            if (name.Length == 0 || string.IsNullOrEmpty(__result)) return;
            object shared = ReadMember(item, "m_shared");
            string token = ReadMember(shared, "m_name") as string;
            string originalName = _activeInstance == null ? token : _activeInstance.LocalizeName(token, token);
            int lineBreak = __result.IndexOf('\n');
            string firstLine = lineBreak >= 0 ? __result.Substring(0, lineBreak) : __result;
            string suffix = !string.IsNullOrEmpty(originalName) &&
                firstLine.StartsWith(originalName, StringComparison.Ordinal)
                ? firstLine.Substring(originalName.Length) : string.Empty;
            __result = name + suffix + (lineBreak >= 0 ? __result.Substring(lineBreak) : string.Empty);
        }

        // InventoryGrid.CreateItemTooltip calls UITooltip.Set with the vanilla
        // m_shared.m_name as the title. Preserve its generated description
        // and call Set again with the instance-specific title ONLY for a named
        // item. We never modify m_shared or suppress the game's original code.
        // Vanilla GetTooltip returns the body (description + item stats). Replace
        // only the original localized description fragment; preserve all game
        // statistics. An unrecognized layout is not edited to avoid corruption.
        private static void CustomDescriptionTooltipPostfix(object[] __args, ref string __result)
        {
            if (__args == null || __args.Length == 0 || __args[0] == null || string.IsNullOrEmpty(__result)) return;
            string custom = ReadCustomItemDescription(__args[0]);
            if (custom.Length == 0) return;
            object shared = ReadMember(__args[0], "m_shared");
            string description = ReadMember(shared, "m_description") as string;
            if (string.IsNullOrEmpty(description))
            {
                __result = custom + "\n" + __result;
                return;
            }
            string localized = _activeInstance == null ? description : _activeInstance.LocalizeName(description, description);
            int index = __result.IndexOf(localized, StringComparison.Ordinal);
            if (index < 0 && !string.Equals(localized, description, StringComparison.Ordinal))
            {
                localized = description;
                index = __result.IndexOf(localized, StringComparison.Ordinal);
            }
            if (index >= 0 && localized.Length > 0)
                __result = __result.Substring(0, index) + custom + __result.Substring(index + localized.Length);
            else if (_activeInstance != null)
                _activeInstance.Logger.LogDebug("Custom description not applied: original localized text missing from tooltip body.");
        }

        private static void CustomInventoryTooltipPostfix(object __instance, object[] __args)
        {
            if (__args == null || __args.Length < 2) return;
            object item = __args[0];
            object tooltip = __args[1];
            string name = ReadCustomItemName(item);
            if (name.Length == 0 || tooltip == null) return;

            string body = ReadMember(tooltip, "m_text") as string;
            object anchor = ReadMember(__instance, "m_tooltipAnchor");
            if (body == null)
            {
                // Fallback changes the tooltip topic for subsequent refreshes;
                // never erase the description by passing an empty string.
                TrySetReferenceMember(tooltip, "m_topic", name);
                return;
            }
            try
            {
                foreach (MethodInfo method in tooltip.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name != "Set") continue;
                    ParameterInfo[] p = method.GetParameters();
                    if (p.Length != 4 || p[0].ParameterType != typeof(string) ||
                        p[1].ParameterType != typeof(string) || p[3].ParameterType != typeof(Vector2)) continue;
                    if (anchor != null && !p[2].ParameterType.IsInstanceOfType(anchor)) continue;
                    if (anchor == null && p[2].ParameterType.IsValueType) continue;
                    method.Invoke(tooltip, new object[] { name, body, anchor, Vector2.zero });
                    return;
                }
                TrySetReferenceMember(tooltip, "m_topic", name);
                if (_activeInstance != null)
                    _activeInstance.Logger.LogWarning("UITooltip.Set(string,string,anchor,Vector2) not found; used m_topic fallback.");
            }
            catch (Exception ex)
            {
                TrySetReferenceMember(tooltip, "m_topic", name);
                if (_activeInstance != null)
                    _activeInstance.Logger.LogWarning("Custom inventory item title update failed: " + ex.Message);
            }
        }

    }
}
