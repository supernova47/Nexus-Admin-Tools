using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private int _historyPage, _reportPage;

        private void BuildToolsPage()
        {
            var body = _nativeBody.transform;
            string[] titles = { T("Diagnostics", "Диагностика"), T("Equipment kits", "Наборы экипировки"),
                T("History", "История"), T("Radar", "Радар") };
            for (int i = 0; i < titles.Length; i++)
            {
                int section = i;
                var button = HudButton(body, titles[i], i * 266, 0, 254, 34,
                    () => { _toolsSection = section; _reportPage = 0; RebuildNativeHud(); });
                HudHighlight(button, i == _toolsSection);
            }
            if (_toolsSection == 1) BuildKitsPage(body);
            else if (_toolsSection == 0) BuildNativeDoctor(body);
            else if (_toolsSection == 2) BuildNativeHistory(body);
            else BuildNativeRadar(body);
        }

        private void BuildKitsPage(Transform body)
        {
            HudInput(body, _newKitName, 0, 48, 270, 34, 40, v => _newKitName = v, T("New kit name", "Название нового набора"));
            HudButton(body, T("Create kit", "Создать набор"), 278, 48, 140, 34, CreateNativeKit);
            NexusKit selected = null;
            if (_advanced.Kits.Count > 0)
            {
                _activeKitIndex = Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1);
                selected = _advanced.Kits[_activeKitIndex];
                HudDropdown(body, _advanced.Kits.Select(k => k.Name + " · " + k.Items.Count + "/100").ToArray(),
                    _activeKitIndex, 440, 48, 612, index => { _activeKitIndex = index; RebuildNativeHud(); });
            }
            SyncKitWorkshop(selected);
            HudButton(body, T("Capture inventory + equipped", "Сохранить инвентарь + экипировку"), 0, 92, 370, 36, CaptureCurrentInventoryToKit).interactable = selected != null;
            HudButton(body, T("Apply kit", "Применить набор"), 380, 92, 210, 36, () => ApplySelectedKit(selected)).interactable = selected != null;
            HudButton(body, T("Duplicate kit", "Копировать набор"), 600, 92, 220, 36, DuplicateNativeKit).interactable = selected != null;
            HudButton(body, T("Delete kit", "Удалить набор"), 830, 92, 222, 36, DeleteNativeKit).interactable = selected != null;
            HudInput(body, _kitRenameInput, 0, 138, 330, 34, 40, v => _kitRenameInput = v, T("Kit name", "Название набора"));
            HudButton(body, T("Rename", "Переименовать"), 340, 138, 180, 34, RenameNativeKit).interactable = selected != null;
            HudButton(body, T("Copy JSON", "Копировать JSON"), 540, 138, 160, 34,
                () => { GUIUtility.systemCopyBuffer = NexusKitWorkshop.Export(selected); _status = T("Kit JSON copied.", "JSON набора скопирован."); }).interactable = selected != null;
            HudButton(body, T("Import JSON", "Импорт JSON"), 710, 138, 160, 34, ImportNativeKit);
            HudButton(body, T("Verify save", "Проверить файл"), 880, 138, 172, 34, VerifyNativeSave);
            if (selected == null)
            {
                HudPlace(HudLabel(body, T("Create a kit or import its JSON from the clipboard.", "Создайте набор или импортируйте его JSON из буфера обмена."), 19), 0, 208, BodyWidth, 80);
                return;
            }
            var list = HudScroll(body); HudPlace(list, 0, 190, 670, 400);
            for (int i = 0; i < selected.Items.Count; i++)
            {
                int index = i; NexusKitPart part = selected.Items[i];
                string label = (FindItemById(part.PrefabId) is ItemEntry entry ? GetItemDisplayName(entry) : part.PrefabId) +
                    " ×" + part.Count + " · Q" + part.Quality + (string.IsNullOrEmpty(part.CustomName) ? "" : " [" + part.CustomName + "]");
                var row = HudButton(list.content, label, 0, i * 44, 590, 40,
                    () => { BeginEditKitPart(selected, index); RebuildNativeHud(); });
                row.GetComponentInChildren<UnityEngine.UI.Text>().alignment = TextAnchor.MiddleLeft;
                HudHighlight(row, _editingKitPart == index);
                HudButton(list.content, "×", 596, i * 44, 42, 40, () => RemoveNativeKitPart(selected, index));
            }
            list.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(60, selected.Items.Count * 44));
            var editor = HudPanel(body, 700, 190, 352, 400);
            if (_editingKitPart >= 0 && _editingKitPart < selected.Items.Count)
            {
                HudPlace(HudLabel(editor, T("Count", "Количество"), 17), 0, 4, 120, 28);
                HudIntegerInput(editor, 130, 0, 110, 1, 9999, () => int.TryParse(_kitPartCountInput, out int n) ? n : 1,
                    n => _kitPartCountInput = n.ToString(CultureInfo.InvariantCulture));
                int max = Math.Max(1, FindItemById(selected.Items[_editingKitPart].PrefabId)?.MaxQuality ?? 1);
                HudPlace(HudLabel(editor, T("Quality", "Качество"), 17), 0, 50, 120, 28);
                HudIntegerInput(editor, 130, 46, 110, 1, max, () => int.TryParse(_kitPartQualityInput, out int n) ? n : 1,
                    n => _kitPartQualityInput = n.ToString(CultureInfo.InvariantCulture));
                var limit = HudLabel(editor, "", 16); HudPlace(limit, 252, 50, 100, 28);
                _hudDynamic.Add(() => limit.text = _kitPartQualityInput + T(" of ", " из ") + max);
                HudInput(editor, _kitPartNameInput, 0, 94, 352, 34, 48, v => _kitPartNameInput = v, T("Custom name", "Своё имя"));
                HudInput(editor, _kitPartDescriptionInput, 0, 138, 352, 126, 500, v => _kitPartDescriptionInput = v,
                    T("Custom description", "Своё описание"), true);
                HudButton(editor, T("Save item", "Сохранить предмет"), 0, 280, 352, 36, SaveNativeKitPart);
            }
            else HudPlace(HudLabel(editor, T("Select an item to edit count, quality, name and description.",
                "Выберите предмет для изменения количества, качества, имени и описания."), 18), 0, 0, 352, 150);
            HudPlace(HudLabel(body, T("Up to 100 item rows. Capture replaces the selected kit; equipped items are included once.",
                "До 100 строк. Сохранение инвентаря заменяет содержимое выбранного набора; экипировка записывается один раз."), 16), 0, 608, BodyWidth, 42);
        }

        private void DuplicateNativeKit()
        {
            if (_advancedSaveBlocked || _advanced.Kits.Count == 0 || _advanced.Kits.Count >= NexusKitWorkshop.MaxKits) return;
            var kit = NexusKitWorkshop.DeepCopy(_advanced.Kits[Mathf.Clamp(_activeKitIndex, 0, _advanced.Kits.Count - 1)]);
            kit.Name = NexusKitWorkshop.UniqueName(kit.Name, _advanced.Kits);
            _advanced.Kits.Add(kit);
            if (!SaveNexusAdvanced()) { _advanced.Kits.Remove(kit); return; }
            _activeKitIndex = _advanced.Kits.Count - 1; RebuildNativeHud();
        }

        private void ImportNativeKit()
        {
            if (_advancedSaveBlocked || _advanced.Kits.Count >= NexusKitWorkshop.MaxKits) return;
            if (!NexusKitWorkshop.TryImport(GUIUtility.systemCopyBuffer, out var kit, out var reason))
            { _status = T("Kit import rejected: ", "Импорт отклонён: ") + reason; return; }
            kit.Name = NexusKitWorkshop.UniqueName(kit.Name, _advanced.Kits);
            _advanced.Kits.Add(kit);
            if (!SaveNexusAdvanced()) { _advanced.Kits.Remove(kit); return; }
            _activeKitIndex = _advanced.Kits.Count - 1; RebuildNativeHud();
        }

        private void VerifyNativeSave()
        {
            _status = TryLoadAdvancedFile(_advancedDataPath, out var saved, out var reason) && _advanced.SamePersistedContent(saved)
                ? T("Saved data matches.", "Данные совпадают с сохранённым файлом.") : T("Save verification failed: ", "Ошибка проверки: ") + reason;
        }

        private void BuildNativeDoctor(Transform body)
        {
            HudInput(body, _doctorSearch, 0, 52, 430, 34, 128, v => { _doctorSearch = v; _doctorItemIndex = 0; }, T("Filter prefab ID", "Поиск prefab ID"));
            HudButton(body, T("Find", "Найти"), 440, 52, 140, 34, RebuildNativeHud);
            var matches = _itemEntries.Where(e => ContainsIgnoreCase(e.PrefabName, _doctorSearch ?? "")).ToArray();
            if (matches.Length > 0)
            {
                _doctorItemIndex = Mathf.Clamp(_doctorItemIndex, 0, matches.Length - 1);
                HudDropdown(body, matches.Select(e => e.PrefabName).ToArray(), _doctorItemIndex, 600, 52, 452,
                    n => { _doctorItemIndex = n; });
            }
            HudButton(body, T("Run diagnostics", "Проверить"), 0, 100, 510, 36, () =>
            {
                _doctorReport = matches.Length == 0 ? T("Nothing found.", "Ничего не найдено.") : InspectItemPrefab(matches[_doctorItemIndex]);
                _reportPage = 0; RebuildNativeHud();
            });
            HudButton(body, T("Copy report", "Копировать отчёт"), 542, 100, 510, 36, () => GUIUtility.systemCopyBuffer = _doctorReport);
            BuildNativeReport(body, _doctorReport, 158);
        }

        private void BuildNativeHistory(Transform body)
        {
            HudInput(body, _historyFilter, 0, 52, 510, 34, 128, v => { _historyFilter = v; _historyPage = 0; }, T("Filter history", "Поиск в истории"));
            HudButton(body, T("Find", "Найти"), 520, 52, 140, 34, RebuildNativeHud);
            HudDropdown(body, new[] { T("All", "Все"), T("Items", "Предметы"), T("Creatures", "Существа"), T("Kits", "Наборы") },
                _historyKindFilter, 680, 52, 372, n => { _historyKindFilter = n; _historyPage = 0; RebuildNativeHud(); });
            string[] kinds = { "", "item", "creature", "kit" };
            var records = _advanced.History.Where(r => r != null && (_historyKindFilter == 0 || r.Kind == kinds[_historyKindFilter]) &&
                (string.IsNullOrWhiteSpace(_historyFilter) || ContainsIgnoreCase(r.PrefabId, _historyFilter) ||
                 ContainsIgnoreCase(r.CustomName, _historyFilter) || ContainsIgnoreCase(r.CustomDescription, _historyFilter))).ToArray();
            int pages = Math.Max(1, (records.Length + 7) / 8); _historyPage = Mathf.Clamp(_historyPage, 0, pages - 1);
            int i = 0;
            foreach (var record in records.Skip(_historyPage * 8).Take(8))
            {
                HudPlace(HudLabel(body, record.Utc + " · " + record.Kind + " · " + record.PrefabId + " ×" + record.Count, 17), 0, 110 + i * 56, 850, 48);
                if (record.Kind != "kit") HudButton(body, T("Reuse", "Повторить"), 880, 110 + i * 56, 172, 36,
                    () => { LoadHistoryControls(record); _nativeMode = 0; RebuildNativeHud(); });
                i++;
            }
            HudButton(body, "◀", 0, 594, 52, 34, () => { _historyPage--; RebuildNativeHud(); });
            HudPlace(HudLabel(body, T("Entries: ", "Записей: ") + records.Length + " · " + (_historyPage + 1) + "/" + pages, 17), 70, 600, 900, 30);
            HudButton(body, "▶", 1000, 594, 52, 34, () => { _historyPage++; RebuildNativeHud(); });
        }

        private void BuildNativeRadar(Transform body)
        {
            HudPlace(HudLabel(body, T("Baseline: ", "Базовый снимок: ") + (_advanced.SnapshotUtc ?? "—"), 18, HudGold), 0, 52, BodyWidth, 32);
            HudButton(body, T("Compare catalogues", "Сравнить каталоги"), 0, 100, 340, 36,
                () => { _radarReport = CompareCatalogues(); _reportPage = 0; RebuildNativeHud(); });
            HudButton(body, T("Save snapshot", "Сохранить снимок"), 356, 100, 340, 36, () =>
            {
                var old = _advanced.Snapshot; string utc = _advanced.SnapshotUtc;
                _advanced.Snapshot = BuildCatalogueSnapshot(); _advanced.SnapshotUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                if (!SaveNexusAdvanced()) { _advanced.Snapshot = old; _advanced.SnapshotUtc = utc; return; }
                _radarReport = T("Snapshot saved.", "Снимок сохранён."); _reportPage = 0; RebuildNativeHud();
            });
            HudButton(body, T("Copy comparison", "Копировать сравнение"), 712, 100, 340, 36, () => GUIUtility.systemCopyBuffer = _radarReport);
            BuildNativeReport(body, _radarReport, 158);
        }

        private void BuildNativeReport(Transform body, string report, float y)
        {
            // Text reports use explicit pages; their commands never scroll out of view.
            string[] lines = (report ?? "").Replace("\r", "").Split('\n');
            int pages = Math.Max(1, (lines.Length + 15) / 16); _reportPage = Mathf.Clamp(_reportPage, 0, pages - 1);
            var text = HudLabel(body, string.Join("\n", lines.Skip(_reportPage * 16).Take(16)), 17);
            HudPlace(text, 0, y, BodyWidth, 420);
            HudButton(body, "◀", 0, 594, 52, 34, () => { _reportPage--; RebuildNativeHud(); });
            HudPlace(HudLabel(body, (_reportPage + 1) + "/" + pages, 17), 70, 600, 900, 30);
            HudButton(body, "▶", 1000, 594, 52, 34, () => { _reportPage++; RebuildNativeHud(); });
        }
    }
}
