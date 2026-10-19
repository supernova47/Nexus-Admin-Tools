using System;
using System.Globalization;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private NexusKit _workshopKit;
        private int _editingKitPart = -1;
        private bool _showKitRename;
        private string _kitRenameInput = string.Empty;
        private string _kitPartCountInput = "1";
        private string _kitPartQualityInput = "1";
        private string _kitPartNameInput = string.Empty;
        private string _kitPartDescriptionInput = string.Empty;
        private string _historyFilter = string.Empty;
        private int _historyKindFilter; // all / item / creature / kit

        private void SyncKitWorkshop(NexusKit kit)
        {
            if (ReferenceEquals(_workshopKit, kit)) return;
            _workshopKit = kit;
            _editingKitPart = -1;
            _showKitRename = false;
            _kitRenameInput = kit == null ? string.Empty : kit.Name;
        }

        private void BeginEditKitPart(NexusKit kit, int index)
        {
            SyncKitWorkshop(kit);
            if (index < 0 || index >= kit.Items.Count) return;
            NexusKitPart part = kit.Items[index];
            if (part == null) return;
            _editingKitPart = index;
            _kitPartCountInput = part.Count.ToString(CultureInfo.InvariantCulture);
            _kitPartQualityInput = part.Quality.ToString(CultureInfo.InvariantCulture);
            _kitPartNameInput = part.CustomName ?? string.Empty;
            _kitPartDescriptionInput = part.CustomDescription ?? string.Empty;
        }

        // Returns true when the selected kit changed; drawing must restart next frame.
        private bool DrawKitWorkshopActions(NexusKit selected)
        {
            SyncKitWorkshop(selected);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Duplicate Kit", "Копировать набор"), GUILayout.Height(28f)))
            {
                if (_advancedSaveBlocked || _advanced.Kits.Count >= NexusKitWorkshop.MaxKits)
                    _status = T("Saving is blocked or 25 kits reached.", "Сохранение заблокировано или достигнут лимит 25 наборов.");
                else
                {
                    try
                    {
                        NexusKit clone = NexusKitWorkshop.DeepCopy(selected);
                        clone.Name = NexusKitWorkshop.UniqueName(selected.Name, _advanced.Kits);
                        _advanced.Kits.Add(clone);
                        if (SaveNexusAdvanced())
                        {
                            _activeKitIndex = _advanced.Kits.Count - 1;
                            _status = T("Kit duplicated.", "Набор скопирован.");
                            GUILayout.EndHorizontal();
                            return true;
                        }
                        _advanced.Kits.Remove(clone);
                    }
                    catch (Exception ex) { _status = T("Could not duplicate kit: ", "Не удалось скопировать набор: ") + ex.Message; }
                }
            }
            if (GUILayout.Button(T("Rename", "Переименовать"), GUILayout.Height(28f)))
            {
                _showKitRename = !_showKitRename;
                _kitRenameInput = selected.Name;
            }
            if (GUILayout.Button(T("Copy Kit JSON", "Копировать JSON"), GUILayout.Height(28f)))
            {
                try
                {
                    GUIUtility.systemCopyBuffer = NexusKitWorkshop.Export(selected);
                    _status = T("One kit copied as JSON. History/Radar not included.",
                        "JSON одного набора скопирован. История и Радар не включены.");
                }
                catch (Exception ex) { _status = T("Kit export failed: ", "Ошибка экспорта: ") + ex.Message; }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Import Kit JSON", "Импорт JSON"), GUILayout.Height(28f)))
            {
                if (_advancedSaveBlocked || _advanced.Kits.Count >= NexusKitWorkshop.MaxKits)
                    _status = T("Saving is blocked or 25 kits reached.", "Сохранение заблокировано или достигнут лимит 25 наборов.");
                else
                {
                    try
                    {
                        NexusKit imported;
                        string reason;
                        if (!NexusKitWorkshop.TryImport(GUIUtility.systemCopyBuffer, out imported, out reason))
                            _status = T("Kit import rejected: ", "Импорт набора отклонён: ") + reason;
                        else
                        {
                            imported.Name = NexusKitWorkshop.UniqueName(imported.Name, _advanced.Kits);
                            _advanced.Kits.Add(imported);
                            if (SaveNexusAdvanced())
                            {
                                _activeKitIndex = _advanced.Kits.Count - 1;
                                _status = T("Imported one kit. Verify its Prefab IDs before applying.",
                                    "Набор импортирован. Проверь Prefab ID перед добавлением.");
                                GUILayout.EndHorizontal();
                                return true;
                            }
                            _advanced.Kits.Remove(imported);
                        }
                    }
                    catch (Exception ex) { _status = T("Kit import failed: ", "Ошибка импорта: ") + ex.Message; }
                }
            }
            if (GUILayout.Button(T("Verify Saved Data", "Проверить сохранение"), GUILayout.Height(28f)))
            {
                NexusAdvancedData saved;
                string reason;
                if (TryLoadAdvancedFile(_advancedDataPath, out saved, out reason) && _advanced.SamePersistedContent(saved))
                    _status = T("Kits, History and Radar match the file on disk.",
                        "Наборы, История и Радар совпадают с файлом на диске.");
                else _status = T("Stored state differs or cannot be read. Check the BepInEx log.",
                    "Данные на диске отличаются или не читаются. Проверь журнал BepInEx.");
            }
            GUILayout.EndHorizontal();

            if (_showKitRename)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(T("Kit name:", "Имя набора:"), GUILayout.Width(110f));
                GUI.SetNextControlName("ValheimNexus.KitRename");
                _kitRenameInput = GUILayout.TextField(_kitRenameInput, 40, GUILayout.ExpandWidth(true));
                _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimNexus.KitRename";
                if (GUILayout.Button(T("Save", "Сохранить"), GUILayout.Width(100f)))
                {
                    string name = NexusAdvancedData.CleanName(_kitRenameInput);
                    if (string.IsNullOrEmpty(name)) _status = T("Enter a valid kit name.", "Введи корректное имя набора.");
                    else if (_advancedSaveBlocked) _status = T("Saving is blocked.", "Сохранение заблокировано.");
                    else
                    {
                        NexusKit candidate = NexusKitWorkshop.DeepCopy(selected);
                        candidate.Name = name;
                        string reason;
                        if (!NexusKitWorkshop.Validate(candidate, out reason)) _status = reason;
                        else
                        {
                            int index = _activeKitIndex;
                            _advanced.Kits[index] = candidate;
                            if (!SaveNexusAdvanced()) _advanced.Kits[index] = selected;
                            else
                            {
                                _showKitRename = false;
                                _status = T("Kit renamed.", "Набор переименован.");
                                GUILayout.EndHorizontal();
                                return true;
                            }
                        }
                    }
                }
                if (GUILayout.Button(T("Cancel", "Отмена"), GUILayout.Width(80f))) _showKitRename = false;
                GUILayout.EndHorizontal();
            }
            return false;
        }

        private void DrawKitPartEditor(NexusKit kit)
        {
            SyncKitWorkshop(kit);
            if (_editingKitPart < 0 || _editingKitPart >= kit.Items.Count) return;
            NexusKitPart part = kit.Items[_editingKitPart];
            if (part == null) { _editingKitPart = -1; return; }
            GUILayout.BeginVertical("box");
            GUILayout.Label(T("Edit item: ", "Редактировать предмет: ") + part.PrefabId);
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Count (1–9999):", "Количество (1–9999):"), GUILayout.Width(140f));
            GUI.SetNextControlName("ValheimNexus.KitPartCount");
            _kitPartCountInput = GUILayout.TextField(_kitPartCountInput, 4, GUILayout.Width(75f));
            _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimNexus.KitPartCount";
            GUILayout.Label(T("Quality (1–10):", "Качество (1–10):"), GUILayout.Width(145f));
            GUI.SetNextControlName("ValheimNexus.KitPartQuality");
            _kitPartQualityInput = GUILayout.TextField(_kitPartQualityInput, 2, GUILayout.Width(65f));
            _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimNexus.KitPartQuality";
            GUILayout.EndHorizontal();
            GUILayout.Label(T("Custom name (empty = default):", "Своё имя (пустое = обычное):"));
            GUI.SetNextControlName("ValheimNexus.KitPartName");
            _kitPartNameInput = GUILayout.TextField(_kitPartNameInput, 40, GUILayout.ExpandWidth(true));
            _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimNexus.KitPartName";
            GUILayout.Label(T("Custom description (empty = default):", "Своё описание (пустое = обычное):"));
            GUI.SetNextControlName("ValheimNexus.KitPartDescription");
            _kitPartDescriptionInput = GUILayout.TextArea(_kitPartDescriptionInput, 480, GUILayout.MinHeight(62f));
            _nameHasFocus = GUI.GetNameOfFocusedControl() == "ValheimNexus.KitPartDescription";
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Save Item", "Сохранить предмет"), GUILayout.Height(30f)))
            {
                int count, quality;
                if (!int.TryParse(_kitPartCountInput, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1 || count > 9999 ||
                    !int.TryParse(_kitPartQualityInput, NumberStyles.None, CultureInfo.InvariantCulture, out quality) || quality < 1 || quality > 10)
                    _status = T("Invalid count or quality.", "Недопустимое количество или качество.");
                else if (_advancedSaveBlocked) _status = T("Saving is blocked.", "Сохранение заблокировано.");
                else
                {
                    string name = NexusAdvancedData.CleanName(_kitPartNameInput);
                    string description = NexusAdvancedData.CleanDescription(_kitPartDescriptionInput);
                    ItemEntry entry = FindItemById(part.PrefabId);
                    if (entry != null && ((name.Length > 0 || description.Length > 0) && entry.MaxStack > 1 || quality > entry.MaxQuality))
                        _status = T("Item stack/quality settings are incompatible with this Prefab.",
                            "Размер стака или качество несовместимы с этим Prefab.");
                    else
                    {
                        NexusKit candidate = NexusKitWorkshop.DeepCopy(kit);
                        NexusKitPart edited = candidate.Items[_editingKitPart];
                        edited.Count = count;
                        edited.Quality = quality;
                        edited.CustomName = name;
                        edited.CustomDescription = description;
                        string reason;
                        if (!NexusKitWorkshop.Validate(candidate, out reason)) _status = reason;
                        else
                        {
                            int index = _activeKitIndex;
                            _advanced.Kits[index] = candidate;
                            if (!SaveNexusAdvanced()) _advanced.Kits[index] = kit;
                            else
                            {
                                _status = T("Kit entry updated.", "Предмет в наборе обновлён.");
                                _workshopKit = candidate;
                                _editingKitPart = -1;
                            }
                        }
                    }
                }
            }
            if (GUILayout.Button(T("Cancel", "Отмена"), GUILayout.Height(30f))) _editingKitPart = -1;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
