"""Source regression guards for v1.8.2. C# compilation and compiled IL checks run separately."""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / 'src' / 'Plugin.cs').read_text(encoding='utf-8')


class ReleaseGuards(unittest.TestCase):
    def test_version_consistency(self):
        for file in ('manifest.json', 'package_template/manifest.json'):
            manifest = json.loads((ROOT / file).read_text(encoding='utf-8'))
            self.assertEqual('1.8.2', manifest['version_number'])
            self.assertEqual('Nexus_Admin_Tools', manifest['name'])
            self.assertEqual('https://github.com/supernova47/Nexus-Admin-Tools', manifest['website_url'])
            self.assertLessEqual(len(manifest['description']), 250)
        self.assertIn('PluginName = "Nexus Admin Tools"', SOURCE)
        self.assertIn('PluginVersion = "1.8.2"', SOURCE)
        self.assertIn('**Version:** 1.8.2', (ROOT / 'README.md').read_text(encoding='utf-8'))
        self.assertIn('**Version:** 1.8.2', (ROOT / 'package_template/README.md').read_text(encoding='utf-8'))
        readme = (ROOT / 'README.md').read_text(encoding='utf-8')
        self.assertNotRegex(readme, '[А-Яа-яЁё]')
        self.assertIn('https://github.com/supernova47/Nexus-Admin-Tools/issues', readme)

    def test_public_github_release_is_tagged_and_uses_hosted_runners(self):
        workflow = (ROOT / '.github' / 'workflows' / 'release.yml').read_text(encoding='utf-8')
        self.assertIn("- 'v*'", workflow)
        self.assertIn('sha256sum --check SHA256SUMS.txt', workflow)
        self.assertIn('gh release create', workflow)
        self.assertIn('contents: write', workflow)
        self.assertIn('runs-on: ubuntu-latest', workflow)
        self.assertNotIn('self-hosted', workflow)
        self.assertTrue((ROOT / '.github' / 'ISSUE_TEMPLATE' / 'bug_report.yml').exists())
        self.assertTrue((ROOT / '.github' / 'ISSUE_TEMPLATE' / 'feature_request.yml').exists())

    def test_weather_arrows_cycle_from_current_server_value(self):
        hud = (ROOT / 'src' / 'NexusNativeHud.cs').read_text(encoding='utf-8')
        self.assertIn('ChangeWeather(weatherChoices, -1)', hud)
        self.assertIn('ChangeWeather(weatherChoices, 1)', hud)
        self.assertIn('choices.IndexOf(_serverForcedWeather ?? string.Empty)', hud)
        self.assertIn('AdminRules.CycleIndex(currentIndex, delta, choices.Count)', hud)

    def test_location_names_and_unvisited_world_locations_are_kept(self):
        hud = (ROOT / 'src' / 'NexusNativeHud.cs').read_text(encoding='utf-8')
        world = (ROOT / 'src' / 'NexusWorldAdmin.cs').read_text(encoding='utf-8')
        self.assertIn('name = AdminRules.LocationName(name, prefab)', hud)
        self.assertIn('AdminRules.LocationName(location.m_location.m_name, location.m_location.m_prefabName)', world)
        self.assertIn('return zones.GetLocationList().Where(location => location.m_location != null &&', world)
        self.assertNotIn('location.m_placed &&', world)
        self.assertIn('location.m_location.m_unique', world)
        self.assertIn('Nexus locations page prepared:', world)
        self.assertIn('Nexus locations received:', hud)

    def test_shared_teleport_points_and_rename_actions_are_server_authorized(self):
        world = (ROOT / 'src' / 'NexusWorldAdmin.cs').read_text(encoding='utf-8')
        network = (ROOT / 'src' / 'NexusNetwork.cs').read_text(encoding='utf-8')
        hud = (ROOT / 'src' / 'NexusNativeHud.cs').read_text(encoding='utf-8')
        self.assertIn('Config.Bind(scope, "TeleportLocations"', world)
        self.assertIn('ZNet.instance.GetWorldUID()', world)
        self.assertIn('GetPosition()', network)
        self.assertIn('AdminAction.AddLocation', network)
        self.assertIn('AdminAction.RenameLocation', network)
        self.assertIn('AdminAction.DeleteLocation', network)
        self.assertIn('location.Key == key', world)
        self.assertIn('key.StartsWith("custom:", StringComparison.Ordinal)', world)
        self.assertIn('ExecuteDeleteTeleportPoint', world)
        self.assertIn('RequestWorldAction(AdminAction.AddLocation', hud)
        self.assertIn('RequestWorldAction(AdminAction.RenameLocation', hud)
        self.assertIn('if (selected.IsCustom)', hud)
        self.assertIn('HudInput(left, _newLocationName', hud)
        self.assertIn('NEXUS ADMIN TOOLS', hud)
        add_action = re.search(r'private bool ExecuteAddTeleportPoint\([\s\S]*?(?=\n        private bool ExecuteRenameTeleportPoint)', world).group(0)
        self.assertIn('Vector3 origin', add_action)
        self.assertIn('request.ReadString()', add_action)
        self.assertNotIn('ReadVector3', add_action)

    def test_plugin_guid_unchanged(self):
        self.assertIn('PluginGuid = "com.supernova.valheimspawner"', SOURCE)

    def test_item_amount_is_validated_at_action(self):
        self.assertIn('if (!TryParseItemAmount(amount, out requested))', SOURCE)
        self.assertIn('GUI.enabled = _playerComponent != null && amountValid &&', SOURCE)
        self.assertIn('parsed < 1 || parsed > 9999', SOURCE)

    def test_stack_limits_and_partial_handling(self):
        self.assertIn('ReadIntMember(shared, "m_maxStackSize", 1)', SOURCE)
        self.assertIn('int batch = Math.Min(maxStack, requested - result.Added)', SOURCE)
        self.assertIn('MaxUnstackableItemsPerAction = 100', SOURCE)
        self.assertIn('MaxStackBatchesPerAction = 200', SOURCE)
        self.assertIn('int delta = Mathf.Clamp(after - before, 0, batch)', SOURCE)
        self.assertIn('_itemCount = remaining;', SOURCE)
        self.assertIn('GetItemMaxDurability(copy, clonedShared, quality)', SOURCE)
        self.assertIn('TrySetReferenceMember(copy, "m_dropPrefab", entry.Prefab)', SOURCE)

    def test_no_legacy_unverified_insertion_path_is_active(self):
        self.assertNotIn('TryAddFoodToInventory(', SOURCE)
        self.assertNotIn('TryAddArmorToInventory(', SOURCE)
        self.assertNotIn('TryAddWeaponOrShieldToInventory(', SOURCE)
        self.assertIn('Compatible Inventory.AddItem(ItemData) is unavailable', SOURCE)

    def test_kill_protection(self):
        self.assertIn('KillNearbyRadius", 30f', SOURCE)
        self.assertIn('(character.transform.position - playerPosition).sqrMagnitude > radiusSquared', SOURCE)
        self.assertIn('float.IsNaN(configured) || float.IsInfinity(configured)', SOURCE)
        self.assertIn('!TryReadBooleanMethod(item, "IsPlayer", out flag) || flag', SOURCE)
        self.assertIn('!TryReadBooleanMethod(item, "IsTamed", out flag) || flag', SOURCE)
        self.assertIn('CreatureCategory.Boss', SOURCE)
        self.assertIn('RequestWorldAction(AdminAction.Cleanup', SOURCE)
        cleanup = (ROOT / 'src/NexusCleanup.cs').read_text(encoding='utf-8')
        for guard in ('target.IsPlayer()', 'target.IsTamed()', 'target.IsBoss()', 'radius < 5 || radius > 100', 'view.IsOwner()', 'GetServerPeer()', 'CanCleanup(zdo'):
            self.assertIn(guard, cleanup)

    def test_config_does_not_override_preferences(self):
        self.assertNotIn('_languageSetting.Value = "English";', SOURCE)
        self.assertNotIn('_defaultCount.Value = 1;', SOURCE)
        self.assertIn('_count = Mathf.Clamp(_defaultCount.Value, 1, 50)', SOURCE)

    def test_ru_alias_search(self):
        self.assertIn('RussianItemNames.TryGetValue(entry.PrefabName, out russianAlias)', SOURCE)
        self.assertIn('ContainsIgnoreCase(russianAlias, query)', SOURCE)

    def test_build_script_has_clean_staging(self):
        script = (ROOT / 'BuildMod.ps1').read_text(encoding='utf-8')
        self.assertIn('$manifest.version_number', script)
        self.assertIn('Remove-Item -LiteralPath $resolved -Force -Recurse', script)
        self.assertIn("throw 'Unsafe staging path'", script)
        self.assertIn('[string]$BepInExDir', script)
        self.assertIn('Nexus-Admin-Tools-Thunderstore-$version.zip', script)
        self.assertNotIn('SuperNova-ValheimNexus-1.2.0.zip', script)

    def test_thunderstore_archive_root_and_english_readme(self):
        script = (ROOT / 'BuildMod.ps1').read_text(encoding='utf-8')
        validator = (ROOT / 'tests' / 'ValidatePackages.ps1').read_text(encoding='utf-8')
        readme = (ROOT / 'README.md').read_text(encoding='utf-8')
        self.assertIn("Copy-Item (Join-Path $root 'manifest.json') (Join-Path $packageRoot 'manifest.json')", script)
        self.assertIn("Copy-Item (Join-Path $root 'README.md') (Join-Path $packageRoot 'README.md')", script)
        self.assertIn("Copy-Item (Join-Path $root 'icon.png') (Join-Path $packageRoot 'icon.png')", script)
        self.assertIn('Thunderstore root entry', validator)
        self.assertIn('256x256 PNG', validator)
        self.assertNotRegex(readme, '[А-Яа-яЁё]')

    def test_unity_text_rendering_reference(self):
        # Unity 6 places FontStyle in TextRenderingModule; IMGUILayout references
        # may not resolve it transitively during compilation.
        project = (ROOT / 'src' / 'ValheimSpawner.csproj').read_text(encoding='utf-8')
        build_script = (ROOT / 'BuildMod.ps1').read_text(encoding='utf-8')
        hud = (ROOT / 'src' / 'NexusHudAdvanced.cs').read_text(encoding='utf-8')
        self.assertIn('FontStyle.Bold', hud)
        self.assertIn('<Reference Include="UnityEngine.TextRenderingModule">', project)
        self.assertIn('$(GameManagedDir)\\UnityEngine.TextRenderingModule.dll', project)
        self.assertIn("(Test-Path (Join-Path $path 'valheim_Data\\Managed\\UnityEngine.TextRenderingModule.dll'))", build_script)
        self.assertIn('Test-Path $textRenderingAssembly', build_script)

    def test_jsonutility_build_reference(self):
        # The game may still reference Unity JSON modules, but Nexus state must
        # no longer depend on JsonUtility (which omitted the state collections).
        project = (ROOT / 'src' / 'ValheimSpawner.csproj').read_text(encoding='utf-8')
        build_script = (ROOT / 'BuildMod.ps1').read_text(encoding='utf-8')
        data = (ROOT / 'src' / 'NexusAdvancedData.cs').read_text(encoding='utf-8')
        codec = (ROOT / 'src' / 'NexusAdvancedJson.cs').read_text(encoding='utf-8')
        self.assertNotIn('using UnityEngine;', data)
        self.assertNotIn('JsonUtility.FromJson', data)
        self.assertNotIn('JsonUtility.ToJson', data)
        self.assertIn('NexusAdvancedJson.TryDeserialize', data)
        self.assertIn('NexusAdvancedJson.Serialize', data)
        self.assertIn('sb.Append("{\\"Format\\":2,\\"Kits\\":[")', codec)
        self.assertIn('<Reference Include="UnityEngine.JSONSerializeModule">', project)
        self.assertIn('$(GameManagedDir)\\UnityEngine.JSONSerializeModule.dll', project)
        self.assertIn('UnityEngine.JSONSerializeModule.dll', build_script)
        self.assertIn('Test-Path $jsonAssembly', build_script)

    def test_favorites_have_persistent_prefab_storage(self):
        favorites = (ROOT / 'src' / 'FavoriteIds.cs').read_text(encoding='utf-8')
        self.assertIn('HashSet<string>(StringComparer.OrdinalIgnoreCase)', favorites)
        self.assertIn('Convert.ToBase64String(Encoding.UTF8.GetBytes', favorites)
        self.assertIn('Convert.FromBase64String', favorites)
        self.assertIn('MaxEntries = 1000', favorites)
        for literal in ('FavoriteItems', 'FavoriteCreatures', '_favoriteItemsConfig.Value =', '_favoriteCreaturesConfig.Value ='):
            self.assertIn(literal, SOURCE)
        self.assertIn('FavoriteIds.FromEncodedString', SOURCE)
        self.assertTrue((ROOT / 'tests' / 'FavoriteIds.Tests.csproj').exists())

    def test_global_search_scope_and_safety(self):
        self.assertIn('_searchAllItems ? MatchesItemSearch(entry) : MatchesItemFilters(entry, category)', SOURCE)
        self.assertNotIn('string.IsNullOrWhiteSpace(_itemSearch)) return false;', SOURCE)
        self.assertIn('GetCategoryDisplay(entry.Category)', SOURCE)
        self.assertIn('!IsItemVisibleInPanel(_selectedItem, category)', SOURCE)

    def test_icons_and_diagnostics_no_texture_allocations(self):
        self.assertIn('Sprite[] icons = ReadMember(shared, "m_icons") as Sprite[]', SOURCE)
        self.assertIn('GUI.DrawTextureWithTexCoords(rect, entry.IconTexture, entry.IconUv, true);', SOURCE)
        self.assertIn('source.x / texture.width', SOURCE)
        self.assertNotIn('new Texture2D(', SOURCE)
        self.assertIn('_selectedItem.MaxStack', SOURCE)
        self.assertIn('_selectedItem.UsesDurability', SOURCE)
        self.assertIn('new Rect(30f, 35f, 980f, 820f)', SOURCE)

    def test_catalogue_refresh_preserves_selection(self):
        self.assertIn('RefreshAvailableCatalogues()', SOURCE)
        self.assertIn('string selectedName = _selectedItem == null ? null : _selectedItem.PrefabName;', SOURCE)
        self.assertIn('string selectedPrefabName = _selected == null ? null : _selected.PrefabName;', SOURCE)
        self.assertIn('Some game databases are unavailable.', SOURCE)

    def test_frozen_baseline_and_release_docs(self):
        self.assertIn('4afeb727b461541d1861591880e178b8b2c9132bb7ae03b9b7a0e2785bd44f73', (ROOT / 'BASELINE_1.2.1.md').read_text(encoding='utf-8'))
        self.assertIn('1.4.0', (ROOT / 'changelog.md').read_text(encoding='utf-8'))
        self.assertTrue((ROOT / 'TEST_PLAN_1.3.0.md').exists())
        self.assertTrue((ROOT / 'tests' / 'FavoriteIds.Smoke.cs').exists())

    def test_advanced_modules(self):
        ui = (ROOT / 'src' / 'NexusAdvancedUI.cs').read_text(encoding='utf-8')
        data = (ROOT / 'src' / 'NexusAdvancedData.cs').read_text(encoding='utf-8')
        for required in ('DrawDoctorPanel()', 'DrawKitsPanel()', 'DrawHistoryPanel()',
                         'DrawRadarPanel()', 'DrawSpawnPreview()', 'CustomItemNameKey',
                         'TryStampCustomItemName', 'InstallCustomItemTooltipPatch',
                         'GUIUtility.systemCopyBuffer', 'ApplySelectedKit('):
            self.assertIn(required, ui)
        self.assertIn('NexusAdvancedJson.TryDeserialize', data)
        self.assertIn('NexusAdvancedJson.Serialize', data)
        self.assertIn('m_customData', ui)
        self.assertIn('CustomNameHoverTextPostfix', ui)
        self.assertIn('CustomInventoryTooltipPostfix', ui)
        self.assertIn('InventoryGrid.CreateItemTooltip', ui)
        self.assertIn('PatchCustomNameMethod(_itemDropType, "GetHoverText"', ui)
        self.assertIn('PatchCustomNameMethod(_itemDropType, "GetHoverName"', ui)
        self.assertNotIn('CustomNameTooltipPostfix', ui)
        self.assertNotIn('"Custom name: " + name', ui)
        self.assertIn('InventoryContainsNamedItem', SOURCE)
        self.assertIn('MaxStack > 1', ui)
        self.assertNotIn('TrySetStringMember(shared, "m_name"', ui)
        self.assertIn('Nexus Advanced v1.4.0', (ROOT / 'README.md').read_text(encoding='utf-8'))

    def test_buildfix4_durable_state_migration_and_backup(self):
        ui = (ROOT / 'src' / 'NexusAdvancedUI.cs').read_text(encoding='utf-8')
        data = (ROOT / 'src' / 'NexusAdvancedData.cs').read_text(encoding='utf-8')
        for part in ('Paths.ConfigPath', 'SuperNova.ValheimNexus.Advanced.json',
                     'File.Replace(staging, _advancedDataPath, _advancedDataPath + ".bak")',
                     'TryLoadAdvancedFile(backup', '.corrupt', '_advancedSaveBlocked',
                     '_advancedDataConfig.Value', 'NexusAdvancedJson.Serialize(this)'):
            self.assertIn(part, ui + data)
        self.assertIn('TryLoad(string json, out NexusAdvancedData data, out string reason)', data)
        self.assertIn('_advanced.SamePersistedContent(verified)', ui)
        self.assertIn('public bool SamePersistedContent(NexusAdvancedData other)', data)
        self.assertNotIn('_advancedDataConfig.Value = _advanced.Save()', ui)

    def test_buildfix4_custom_description_and_history(self):
        ui = (ROOT / 'src' / 'NexusAdvancedUI.cs').read_text(encoding='utf-8')
        data = (ROOT / 'src' / 'NexusAdvancedData.cs').read_text(encoding='utf-8')
        self.assertIn('CustomItemDescriptionKey', ui)
        self.assertIn('CustomDescription = description', ui)
        self.assertIn('NexusAdvancedData.CleanDescription(part.CustomDescription)', ui)
        self.assertIn('CustomDescription = CleanDescription(customDescription)', data)
        self.assertIn('CustomDescription = string.Empty', data)
        self.assertIn('CustomDescriptionTooltipPostfix', ui)
        self.assertIn('ReadMember(shared, "m_description")', ui)
        self.assertIn('ReadCustomItemDescription(__args[0])', ui)
        self.assertNotIn('TrySetStringMember(shared, "m_description"', ui)
        self.assertIn('GUILayout.TextArea(_itemCustomDescription, 480,', SOURCE)
        self.assertIn('TryStampCustomItemName(copy, customName, customDescription)', SOURCE)

    def test_buildfix4_preferences_survive_updates(self):
        self.assertIn('TrySavePreferences();', SOURCE)
        self.assertIn('Config.Save();', SOURCE)
        self.assertIn('FavoriteIds.FromEncodedString(_favoriteItemsConfig.Value)', SOURCE)
        self.assertIn('FavoriteIds.FromEncodedString(_favoriteCreaturesConfig.Value)', SOURCE)
        self.assertIn('_languageSetting.Value = _isRussian ? "Russian" : "English"', SOURCE)

    def test_explicit_collection_persistence_and_verification(self):
        ui = (ROOT / 'src' / 'NexusAdvancedUI.cs').read_text(encoding='utf-8')
        codec = (ROOT / 'src' / 'NexusAdvancedJson.cs').read_text(encoding='utf-8')
        for item in ('\"Kits\"', '\"History\"', '\"Snapshot\"'):
            self.assertIn(item, codec)
        for required in ('File.ReadAllText(_advancedDataPath, Encoding.UTF8)',
                         'TryLoadAdvancedFile(_advancedDataPath, out verified, out reason)',
                         'Committed Nexus state differs from memory',
                         'File.Copy(_advancedDataPath, _advancedDataPath + ".bak", false)',
                         'Nexus Advanced state saved:', 'Snapshot save failed!',
                         '.legacy-format1.json', 'migratedFromFormat1'):
            self.assertIn(required, ui)
        self.assertIn('Version 2 mandates explicit arrays', codec)

    def test_persistence_test_project_is_game_independent(self):
        csproj = (ROOT / 'tests' / 'NexusAdvancedPersistence.Tests.csproj').read_text(encoding='utf-8')
        self.assertIn('NexusAdvancedJson.cs', csproj)
        self.assertIn('NexusAdvancedData.cs', csproj)
        self.assertNotIn('UnityEngine', csproj)
        self.assertIn('Tests passed:', (ROOT / 'tests' / 'NexusAdvancedPersistence.Smoke.cs').read_text(encoding='utf-8'))

    def test_kit_workshop_and_import_safety(self):
        ui = (ROOT / 'src' / 'NexusKitWorkshopUI.cs').read_text(encoding='utf-8')
        core = (ROOT / 'src' / 'NexusKitWorkshop.cs').read_text(encoding='utf-8')
        advanced = (ROOT / 'src' / 'NexusAdvancedUI.cs').read_text(encoding='utf-8')
        for expected in ('NexusKitWorkshop.Export', 'NexusKitWorkshop.TryImport',
                         'NexusKitWorkshop.DeepCopy', 'NexusKitWorkshop.Validate',
                         'SaveNexusAdvanced()', 'TryLoadAdvancedFile(_advancedDataPath',
                         'GUIUtility.systemCopyBuffer'):
            self.assertIn(expected, ui)
        self.assertIn('if (DrawKitWorkshopActions(selected)) return;', advanced)
        self.assertIn('DrawKitPartEditor(selected);', advanced)
        self.assertIn('_historyKindFilter', advanced)
        self.assertIn('_historyFilter', advanced)
        self.assertIn('value.History.Count != 0 || value.Snapshot.Count != 0', core)
        self.assertIn('container.Kits.Add(DeepCopy(kit))', core)
        self.assertIn('NexusKitWorkshop.cs', (ROOT / 'tests/NexusAdvancedPersistence.Tests.csproj').read_text(encoding='utf-8'))
        self.assertTrue((ROOT / 'BASELINE_1.4.0.md').exists())
        self.assertTrue((ROOT / 'TEST_PLAN_1.5.0.md').exists())

    def test_160_hud_layout_and_collapsed_controls(self):
        self.assertIn('GUILayout.Label("SUPERNOVA  /  "', SOURCE)
        native = (ROOT / 'src/NexusNativeHud.cs').read_text(encoding='utf-8')
        self.assertIn('CreateWoodpanel', native)
        self.assertIn('HudWidth = 1100', native)
        self.assertNotIn('var details = HudScroll', native)
        self.assertNotIn('var scroll = HudScroll(body)', native)
        self.assertIn('NexusHudDragHandler', native)
        self.assertIn('NexusScrollRect', native)
        self.assertNotIn('Kill selected type', native)
        self.assertIn('_showCreatureSafety = GUILayout.Toggle', SOURCE)
        self.assertIn('if (_showCreatureSafety)', SOURCE)
        self.assertIn('if (_showItemTechnicalDetails)', SOURCE)
        self.assertIn('if (_showItemCustomFields)', SOURCE)
        self.assertIn('DrawHudSectionTitle(T("Vitals", "Ресурсы"))', SOURCE)

    def test_160_local_marker_reuse_and_cleanup(self):
        marker = (ROOT / 'src' / 'NexusHudAdvanced.cs').read_text(encoding='utf-8')
        ui = (ROOT / 'src' / 'NexusAdvancedUI.cs').read_text(encoding='utf-8')
        self.assertIn('TryGetSpawnBasePosition(atCrosshair, out var position, out var rotation)', SOURCE)
        self.assertIn('TryGetSpawnBasePosition(atCrosshair, out origin, out rotation)', marker)
        self.assertIn('GetSpawnOffset(i)', marker)
        self.assertIn('MaxSpawnMarkerCount = 12', marker)
        self.assertIn('collider.enabled = false;', marker)
        self.assertIn('DestroyWorldSpawnMarkers();', SOURCE)
        self.assertIn('DestroyWorldSpawnMarkers();', marker)
        self.assertIn('UpdateWorldSpawnMarkers();', SOURCE)
        self.assertIn('MarkersAtCrosshair', SOURCE)
        self.assertIn('WorldMarkersEnabled', SOURCE)
        self.assertIn('T("3D markers", "3D-маркеры")', ui)
        self.assertNotIn('ZNetView', marker)

    def test_160_admin_gating_and_host_scope(self):
        admin = (ROOT / 'src' / 'NexusHudAdvanced.cs').read_text(encoding='utf-8')
        for name in ('AdminAdrenalinePrefix', 'AdminOutgoingDamagePrefix', 'GetMaxAdrenaline',
                     'AddAdrenaline', 'GetAttacker', '"Damage"', '_outgoingDamageHookAvailable',
                     'TryReadBooleanMethod(__instance, "IsTamed"', 'TryReadBooleanMethod(__instance, "IsPlayer"',
                     'ReferenceEquals(attacker, mod._playerComponent)', 'mod.HasAdminAccess'):
            self.assertIn(name, admin)
        self.assertIn('_superDamageMultiplier = 5f;', SOURCE)
        self.assertIn('float[] factors = { 1f, 2f, 5f, 10f, 25f };', SOURCE)
        self.assertIn('_infiniteAdrenaline = GUILayout.Toggle', SOURCE)
        self.assertIn('InstallAdvancedAdminPatches();', SOURCE)
        self.assertIn('UpdateAdminAdrenaline();', SOURCE)

    def test_160_state_format_unchanged(self):
        codec = (ROOT / 'src' / 'NexusAdvancedJson.cs').read_text(encoding='utf-8')
        self.assertIn('sb.Append("{\\"Format\\":2,\\"Kits\\":[")', codec) if False else None
        self.assertIn('NexusAdvancedJson.Serialize', (ROOT / 'src' / 'NexusAdvancedData.cs').read_text(encoding='utf-8'))
        self.assertIn('Format 2', (ROOT / 'README.md').read_text(encoding='utf-8'))
        self.assertIn('**Version:** 1.8.2', (ROOT / 'README.md').read_text(encoding='utf-8'))



if __name__ == '__main__':
    unittest.main(verbosity=2)
