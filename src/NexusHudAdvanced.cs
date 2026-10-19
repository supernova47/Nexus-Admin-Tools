using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    // HUD v1.6: local-only spawn preview, opt-in admin abilities and layout helpers.
    // Never changes the game's prefab registries or the persisted Nexus Advanced format.
    public sealed partial class ValheimSpawnerPlugin
    {
        private const int MaxSpawnMarkerCount = 12;
        private readonly List<GameObject> _spawnMarkerGroups = new List<GameObject>();
        private GameObject _spawnMarkerRoot;
        private Material _spawnMarkerMaterial;
        private float _nextMarkerRefresh;
        private Vector2 _adminScroll;
        private GUIStyle _hudSectionHeader;

        private void DrawHudSectionTitle(string name)
        {
            if (_hudSectionHeader == null)
            {
                _hudSectionHeader = new GUIStyle(GUI.skin.label);
                _hudSectionHeader.fontStyle = FontStyle.Bold;
                _hudSectionHeader.fontSize = 14;
            }
            GUILayout.Label(name, _hudSectionHeader, GUILayout.Height(22f));
        }

        // Match the real spawn's origin and rotation. Only changes rendering of local markers.
        private bool TryGetSpawnBasePosition(bool atCrosshair, out Vector3 point, out Quaternion rotation)
        {
            point = Vector3.zero;
            rotation = Quaternion.identity;
            if (_playerComponent == null || _playerComponent.gameObject == null) return false;
            if (atCrosshair) return TryGetCrosshairPosition(out point);

            Transform playerTransform = _playerComponent.transform;
            Vector3 desired = playerTransform.position + playerTransform.forward * 3f;
            Ray downRay = new Ray(desired + Vector3.up * 12f, Vector3.down);
            RaycastHit hit;
            if (Physics.Raycast(downRay, out hit, 40f)) point = hit.point + Vector3.up * 0.12f;
            else point = desired + Vector3.up * 0.5f;
            rotation = Quaternion.Euler(0f, playerTransform.eulerAngles.y, 0f);
            return true;
        }

        private bool CanShowWorldSpawnMarkers()
        {
            return HasAdminAccess && _isOpen && !_legacyToolsOpen && _nativeMode == 0 &&
                (_mainTab == 0 || _mainTab == 6 && _catalogSelectionKind == 1) && _spawnMarkerConfig != null && _spawnMarkerConfig.Value &&
                _selected != null && _selected.Prefab != null && _playerComponent != null && _zNetSceneInstance != null;
        }

        private bool EnsureWorldSpawnMarkers(int wanted)
        {
            if (_spawnMarkerRoot != null && _spawnMarkerGroups.Count == wanted) return true;
            DestroyWorldSpawnMarkers();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Logger.LogWarning("3D spawn markers disabled: no compatible unlit shader found.");
                return false;
            }
            _spawnMarkerMaterial = new Material(shader);
            _spawnMarkerMaterial.name = "SuperNova Nexus Spawn Preview (local)";
            _spawnMarkerMaterial.color = new Color(0.25f, 0.85f, 0.85f, 0.58f);
            _spawnMarkerMaterial.renderQueue = 3000;
            _spawnMarkerRoot = new GameObject("SuperNova Nexus Spawn Preview (LOCAL ONLY)");
            _spawnMarkerRoot.layer = 2; // Ignore Raycast; never affects spawn surface selection.
            for (int i = 0; i < wanted; i++)
            {
                GameObject group = new GameObject("Spawn preview " + (i + 1));
                group.layer = 2;
                group.transform.SetParent(_spawnMarkerRoot.transform, false);
                CreatePreviewPrimitive(group.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.75f, 0.02f, 0.75f));
                CreatePreviewPrimitive(group.transform, PrimitiveType.Sphere, new Vector3(0f, 0.44f, 0f), new Vector3(0.18f, 0.18f, 0.18f));
                _spawnMarkerGroups.Add(group);
            }
            return true;
        }

        private void CreatePreviewPrimitive(Transform parent, PrimitiveType primitive, Vector3 localPosition, Vector3 scale)
        {
            GameObject mesh = GameObject.CreatePrimitive(primitive);
            mesh.name = primitive == PrimitiveType.Cylinder ? "Position disc" : "Position orb";
            mesh.layer = 2;
            Collider collider = mesh.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }
            mesh.transform.SetParent(parent, false);
            mesh.transform.localPosition = localPosition;
            mesh.transform.localScale = scale;
            Renderer renderer = mesh.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = _spawnMarkerMaterial;
        }

        private void UpdateWorldSpawnMarkers()
        {
            if (!CanShowWorldSpawnMarkers())
            {
                if (_spawnMarkerRoot != null) DestroyWorldSpawnMarkers();
                return;
            }
            if (Time.unscaledTime < _nextMarkerRefresh) return;
            _nextMarkerRefresh = Time.unscaledTime + 0.16f;
            Vector3 origin;
            Quaternion rotation;
            bool atCrosshair = _spawnMarkerCrosshairConfig != null && _spawnMarkerCrosshairConfig.Value;
            if (!TryGetSpawnBasePosition(atCrosshair, out origin, out rotation))
            {
                DestroyWorldSpawnMarkers();
                return;
            }
            int count = Mathf.Min(MaxSpawnMarkerCount, Mathf.Clamp(_count, 1, 50));
            if (!EnsureWorldSpawnMarkers(count)) return;
            for (int i = 0; i < count; i++)
                _spawnMarkerGroups[i].transform.position = origin + GetSpawnOffset(i);
        }

        private void DestroyWorldSpawnMarkers()
        {
            _spawnMarkerGroups.Clear();
            if (_spawnMarkerRoot != null) UnityEngine.Object.Destroy(_spawnMarkerRoot);
            if (_spawnMarkerMaterial != null) UnityEngine.Object.Destroy(_spawnMarkerMaterial);
            _spawnMarkerRoot = null;
            _spawnMarkerMaterial = null;
        }

        private static void AdminAdrenalinePrefix(object __instance, ref float v)
        {
            ValheimSpawnerPlugin mod = _activeInstance;
            if (mod == null || !mod.HasAdminAccess || !mod._infiniteAdrenaline || !ReferenceEquals(__instance, mod._playerComponent)) return;
            // Ignore decay; leave positive adrenaline and the trinket activation path intact.
            if (v < 0f) v = 0f;
        }

        private void UpdateAdminAdrenaline()
        {
            if (!HasAdminAccess || !_infiniteAdrenaline || !_adrenalineHookAvailable || _playerComponent == null ||
                Time.unscaledTime < _nextAdrenalineRefresh) return;
            _nextAdrenalineRefresh = Time.unscaledTime + 0.5f;
            float maximum = ReadFloatMethod(_playerComponent, "GetMaxAdrenaline");
            if (maximum <= 0f || float.IsNaN(maximum) || float.IsInfinity(maximum)) return;
            float current = ReadFloatMethod(_playerComponent, "GetAdrenaline");
            if (current <= 0f) current = ReadFloatMember(_playerComponent, "m_adrenaline");
            // Reserve stays below full to avoid an uncontrollable repeated trinket trigger.
            float reserve = maximum * 0.80f;
            if (current < reserve)
                TryInvokeFloat(_playerComponent, "AddAdrenaline", Mathf.Min(1000f, reserve - current));
        }

        private void InstallAdvancedAdminPatches()
        {
            if (_harmony == null) return;
            Type playerType = FindType("Player");
            if (playerType != null)
            {
                MethodInfo add = playerType.GetMethod("AddAdrenaline",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new Type[] { typeof(float) }, null);
                if (add != null && add.GetParameters()[0].Name == "v")
                {
                    try
                    {
                        _harmony.Patch(add, prefix: new HarmonyMethod(typeof(ValheimSpawnerPlugin).GetMethod(
                            nameof(AdminAdrenalinePrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                        _adrenalineHookAvailable = true;
                    }
                    catch (Exception ex) { Logger.LogWarning("Adrenaline hook could not be installed: " + ex.Message); }
                }
            }

            Type characterType = FindType("Character");
            if (characterType != null)
            {
                MethodInfo prefix = typeof(ValheimSpawnerPlugin).GetMethod(nameof(AdminOutgoingDamagePrefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo restore = typeof(ValheimSpawnerPlugin).GetMethod(nameof(RestoreOutgoingDamage),
                    BindingFlags.Static | BindingFlags.NonPublic);
                foreach (MethodInfo method in characterType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (method.Name != "Damage") continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    bool hasHit = false;
                    for (int i = 0; i < parameters.Length; i++)
                        if (parameters[i].ParameterType.Name == "HitData") hasHit = true;
                    if (!hasHit) continue;
                    try
                    {
                        _harmony.Patch(method, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(restore));
                        _outgoingDamageHookAvailable = true;
                    }
                    catch (Exception ex) { Logger.LogWarning("Outgoing damage hook failed: " + ex.Message); }
                }
            }
            Logger.LogInfo("Nexus Admin hooks: adrenaline=" + _adrenalineHookAvailable +
                ", outgoing damage=" + _outgoingDamageHookAvailable + " (authorized local attacker, friendly targets excluded)");
        }

        private sealed class OutgoingHitState
        {
            internal HitData Hit;
            internal HitData.DamageTypes Original;
        }

        private static void AdminOutgoingDamagePrefix(object __instance, object[] __args, out OutgoingHitState __state)
        {
            __state = null;
            ValheimSpawnerPlugin mod = _activeInstance;
            if (mod == null || !mod.HasAdminAccess || !mod._superDamage || !mod._outgoingDamageHookAvailable ||
                mod._playerComponent == null || ReferenceEquals(__instance, mod._playerComponent) ||
                __args == null) return;

            // Conservative: no players, no tameable allies, and no ambiguous targets.
            bool flag;
            if (!TryReadBooleanMethod(__instance, "IsPlayer", out flag) || flag) return;
            if (!TryReadBooleanMethod(__instance, "IsTamed", out flag) || flag) return;
            object hit = null;
            for (int i = 0; i < __args.Length; i++)
                if (__args[i] != null && __args[i].GetType().Name == "HitData") { hit = __args[i]; break; }
            if (hit == null) return;
            MethodInfo getAttacker = hit.GetType().GetMethod("GetAttacker",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (getAttacker == null) return;
            object attacker;
            try { attacker = getAttacker.Invoke(hit, null); }
            catch { return; }
            if (!ReferenceEquals(attacker, mod._playerComponent)) return;

            if (!(hit is HitData data)) return;
            __state = new OutgoingHitState { Hit = data, Original = data.m_damage };
            data.m_damage.Modify(Mathf.Clamp(mod._superDamageMultiplier, 1f, 25f));
        }

        private static void RestoreOutgoingDamage(OutgoingHitState __state)
        {
            // Character.Damage serializes/routes the hit synchronously. Restore its input on
            // normal or exceptional exit so a reused HitData cannot compound the multiplier.
            if (__state != null && __state.Hit != null) __state.Hit.m_damage = __state.Original;
        }
    }
}
