using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private const int LocationPageSize = 9;
        private const int AuditPageSize = 8;
        private const int AuditMaxEntries = 1000;
        private const long AuditMaxBytes = 1024 * 1024;
        private const int MaxSavedTeleportPoints = 100;
        private const int MaxLocationNameLength = 48;
        private readonly List<string> _serverAuditEntries = new List<string>();
        private readonly Dictionary<long, Vector3> _adminReturnPositions = new Dictionary<long, Vector3>();
        private ConfigEntry<bool> _savedRaidSuppression;
        private ConfigEntry<string> _savedForcedWeather;
        private ConfigEntry<string> _savedTeleportLocations;
        private string _serverStateScope, _auditFilePath, _lastAppliedWeather;
        private string _serverForcedWeather = string.Empty;
        private bool _serverRaidsDisabled, _auditLoaded;
        private readonly List<SavedTeleportPoint> _customTeleportPoints = new List<SavedTeleportPoint>();
        private readonly Dictionary<string, string> _locationNameOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

        private sealed class SavedTeleportPoint
        {
            internal string Id, Name;
            internal Vector3 Position;
        }

        private sealed class NexusWorldLocation
        {
            internal string Key, Name, NameOverride, Prefab;
            internal Vector3 Position;
            internal bool IsCustom;
        }

        private void InstallWorldAdminPatches()
        {
            var getEvents = AccessTools.Method(typeof(RandEventSystem), "GetPossibleRandomEvents");
            var forcedUpdate = AccessTools.Method(typeof(RandEventSystem), "UpdateForcedEvents");
            var startRandom = AccessTools.Method(typeof(RandEventSystem), "StartRandomEvent");
            if (getEvents != null) _harmony.Patch(getEvents, postfix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(FilterSuppressedRaidChoices)));
            if (forcedUpdate != null) _harmony.Patch(forcedUpdate, prefix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(SuppressForcedRaidUpdate)));
            if (startRandom != null) _harmony.Patch(startRandom, prefix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(SuppressRandomRaidStart)));
        }

        private static void FilterSuppressedRaidChoices(ref List<KeyValuePair<RandomEvent, Vector3>> __result)
        {
            var mod = _activeInstance;
            if (__result != null && mod != null && mod._serverRaidsDisabled && ZNet.instance != null && ZNet.instance.IsServer())
                __result.Clear();
        }

        private static bool SuppressForcedRaidUpdate(RandEventSystem __instance)
        {
            var mod = _activeInstance;
            if (mod == null || !mod._serverRaidsDisabled || ZNet.instance == null || !ZNet.instance.IsServer()) return true;
            try
            {
                string forcedName = AccessTools.Method(typeof(RandEventSystem), "GetForcedEvent")?.Invoke(__instance, null) as string;
                var events = AccessTools.Field(typeof(RandEventSystem), "m_events")?.GetValue(__instance) as List<RandomEvent>;
                bool isRandomRaid = !string.IsNullOrEmpty(forcedName) && events != null && events.Any(e => e != null && e.m_name == forcedName && e.m_random);
                return !isRandomRaid;
            }
            catch { return true; }
        }

        private static bool SuppressRandomRaidStart(RandEventSystem __instance)
        {
            var mod = _activeInstance;
            return mod == null || !mod._serverRaidsDisabled || ZNet.instance == null || !ZNet.instance.IsServer();
        }

        private void EnsureServerAdminState()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            string scope = "ServerAdmin." + ZNet.instance.GetWorldUID().ToString("X16");
            if (scope != _serverStateScope)
            {
                _serverStateScope = scope;
                _savedRaidSuppression = Config.Bind(scope, "RaidsDisabled", false,
                    "Stops the active raid and suppresses future raids for this world.");
                _savedForcedWeather = Config.Bind(scope, "ForcedWeather", string.Empty,
                    "Empty means dynamic weather; otherwise the selected Valheim environment name.");
                _savedTeleportLocations = Config.Bind(scope, "TeleportLocations", "v1",
                    "Shared Nexus teleport points and display-name overrides for this world.");
                _serverRaidsDisabled = _savedRaidSuppression.Value;
                _serverForcedWeather = _savedForcedWeather.Value ?? string.Empty;
                LoadTeleportLocations(_savedTeleportLocations.Value);
                _lastAppliedWeather = null;
                _adminReturnPositions.Clear();
                ApplyRaidSuppression();
            }
            if (!_auditLoaded)
            {
                _auditLoaded = true;
                _auditFilePath = Path.Combine(Paths.ConfigPath, "SuperNova.ValheimNexus.AdminAudit.log");
                try
                {
                    if (File.Exists(_auditFilePath))
                        _serverAuditEntries.AddRange(File.ReadAllLines(_auditFilePath).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(AuditMaxEntries));
                }
                catch (Exception ex) { Logger.LogWarning("Could not load Nexus admin audit log: " + ex.Message); }
            }
            ApplyServerWeather();
        }

        private void ApplySyncedWorldAdminState(string weather, bool raidsDisabled)
        {
            _serverForcedWeather = weather ?? string.Empty;
            _serverRaidsDisabled = raidsDisabled;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) ApplyServerWeather();
        }

        private void ApplyServerWeather()
        {
            if (EnvMan.instance == null || _lastAppliedWeather == _serverForcedWeather) return;
            try
            {
                if (_serverForcedWeather.Length == 0 || EnvMan.instance.m_environments.Any(e => e != null && e.m_name == _serverForcedWeather))
                {
                    EnvMan.instance.SetForceEnvironment(_serverForcedWeather);
                    _lastAppliedWeather = _serverForcedWeather;
                }
            }
            catch (Exception ex) { Logger.LogWarning("Could not apply Nexus weather setting: " + ex.Message); }
        }

        private IEnumerable<string> WeatherChoices()
        {
            var names = EnvMan.instance?.m_environments?.Where(e => e != null && !string.IsNullOrWhiteSpace(e.m_name))
                .Select(e => e.m_name).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                ?? new List<string>();
            names.Insert(0, string.Empty);
            return names;
        }

        private bool ExecuteWeatherAction(ZPackage packet, out string message, out string detail)
        {
            message = "Invalid weather"; detail = "weather";
            string weather = packet.ReadString();
            if (weather == null || weather.Length > 128 || EnvMan.instance == null) return false;
            if (weather.Length > 0 && !EnvMan.instance.m_environments.Any(e => e != null && e.m_name == weather))
            { message = "Weather is unavailable on the server"; return false; }
            _serverForcedWeather = weather;
            if (_savedForcedWeather != null) _savedForcedWeather.Value = weather;
            ApplyServerWeather();
            TrySavePreferences();
            detail = "weather " + (weather.Length == 0 ? "dynamic" : weather);
            message = "";
            return true;
        }

        private bool ExecuteRaidAction(ZPackage packet, out string message, out string detail)
        {
            message = "Invalid raid setting"; detail = "raids";
            bool disabled = packet.ReadBool();
            _serverRaidsDisabled = disabled;
            if (_savedRaidSuppression != null) _savedRaidSuppression.Value = disabled;
            if (disabled) ApplyRaidSuppression();
            TrySavePreferences();
            detail = disabled ? "stop current raid and disable future raids" : "enable raids";
            message = "";
            return true;
        }

        private void ApplyRaidSuppression()
        {
            if (!_serverRaidsDisabled || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var events = RandEventSystem.instance;
            if (events == null) return;
            try
            {
                AccessTools.Method(typeof(RandEventSystem), "SetForcedEvent", new[] { typeof(string) })?.Invoke(events, new object[] { string.Empty });
                events.ResetRandomEvent();
            }
            catch (Exception ex) { Logger.LogWarning("Could not stop the active Valheim raid: " + ex.Message); }
        }

        private List<NexusWorldLocation> GetUniqueWorldLocations()
        {
            var zones = ZoneSystem.instance;
            if (zones == null || !zones.LocationsGenerated) return null;
            try
            {
                // GetLocationList contains the world-wide generated positions. m_placed only
                // means a zone has been loaded/spawned locally, so filtering it hides undiscovered
                // unique locations on a dedicated server.
                return zones.GetLocationList().Where(location => location.m_location != null &&
                        location.m_location.m_unique && !string.IsNullOrWhiteSpace(location.m_location.m_prefabName) &&
                        AdminRules.WorldPosition(location.m_position.x, location.m_position.y, location.m_position.z))
                    .GroupBy(location => location.m_location.m_prefabName, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .Select(location => new NexusWorldLocation
                    {
                        Key = "world:" + location.m_location.m_prefabName,
                        Name = _locationNameOverrides.TryGetValue(location.m_location.m_prefabName, out string customName)
                            ? customName : AdminRules.LocationName(location.m_location.m_name, location.m_location.m_prefabName),
                        NameOverride = _locationNameOverrides.TryGetValue(location.m_location.m_prefabName, out string nameOverride)
                            ? nameOverride : string.Empty,
                        Prefab = location.m_location.m_prefabName,
                        Position = location.m_position
                    })
                    .OrderBy(location => location.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(location => location.Prefab, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not read unique world locations: " + ex.Message);
                return null;
            }
        }

        private List<NexusWorldLocation> GetAllNexusLocations()
        {
            var generated = GetUniqueWorldLocations();
            if (generated == null) return null;
            var result = new List<NexusWorldLocation>(generated);
            result.AddRange(_customTeleportPoints.Select(point => new NexusWorldLocation
            {
                Key = "custom:" + point.Id,
                Name = point.Name,
                NameOverride = point.Name,
                Prefab = "Nexus teleport point",
                Position = point.Position,
                IsCustom = true
            }));
            // Keep generated locations first and custom points in creation order so a newly
            // added point is reachable immediately on the last page.
            return result.OrderBy(location => location.IsCustom ? 1 : 0)
                .ThenBy(location => location.IsCustom ? string.Empty : location.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string EncodeLocationText(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static bool TryDecodeLocationText(string value, int maxEncodedLength, int maxDecodedLength, out string decoded)
        {
            decoded = string.Empty;
            if (string.IsNullOrEmpty(value) || value.Length > maxEncodedLength) return false;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                return decoded.Length <= maxDecodedLength;
            }
            catch { return false; }
        }

        private void LoadTeleportLocations(string value)
        {
            _customTeleportPoints.Clear(); _locationNameOverrides.Clear();
            if (string.IsNullOrWhiteSpace(value)) return;
            var records = value.Split(';');
            if (records.Length > 512) { Logger.LogWarning("Nexus teleport settings contain too many records; excess entries were ignored."); }
            foreach (var record in records.Take(512))
            {
                var fields = record.Split(',');
                if (fields.Length == 6 && fields[0] == "C" && _customTeleportPoints.Count < MaxSavedTeleportPoints &&
                    Guid.TryParseExact(fields[1], "N", out Guid id) &&
                    TryDecodeLocationText(fields[2], 256, MaxLocationNameLength, out string pointName) && AdminRules.LocationNameInput(pointName, false) &&
                    float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                    AdminRules.WorldPosition(x, y, z) && _customTeleportPoints.All(point => point.Id != id.ToString("N")))
                {
                    _customTeleportPoints.Add(new SavedTeleportPoint { Id = id.ToString("N"), Name = pointName.Trim(), Position = new Vector3(x, y, z) });
                }
                else if (fields.Length == 3 && fields[0] == "N" &&
                    TryDecodeLocationText(fields[1], 684, 128, out string prefab) && !string.IsNullOrWhiteSpace(prefab) && prefab.Length <= 128 &&
                    TryDecodeLocationText(fields[2], 256, MaxLocationNameLength, out string alias) && AdminRules.LocationNameInput(alias, false))
                {
                    _locationNameOverrides[prefab] = alias.Trim();
                }
            }
        }

        private void SaveTeleportLocations()
        {
            if (_savedTeleportLocations == null) return;
            var records = new List<string> { "v1" };
            records.AddRange(_customTeleportPoints.Take(MaxSavedTeleportPoints).Where(point => point != null &&
                Guid.TryParseExact(point.Id, "N", out _) && AdminRules.LocationNameInput(point.Name, false) &&
                AdminRules.WorldPosition(point.Position.x, point.Position.y, point.Position.z)).Select(point =>
                    "C," + point.Id + "," + EncodeLocationText(point.Name.Trim()) + "," +
                    point.Position.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    point.Position.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                    point.Position.z.ToString("R", CultureInfo.InvariantCulture)));
            records.AddRange(_locationNameOverrides.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 128 &&
                    AdminRules.LocationNameInput(pair.Value, false))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => "N," + EncodeLocationText(pair.Key) + "," + EncodeLocationText(pair.Value.Trim())));
            _savedTeleportLocations.Value = string.Join(";", records);
            TrySavePreferences();
        }

        private bool ExecuteAddTeleportPoint(Vector3 origin, ZPackage request, out string message, out string detail)
        {
            message = "Invalid teleport point name"; detail = "add teleport point";
            string name = request.ReadString();
            if (!AdminRules.LocationNameInput(name, false) || !AdminRules.WorldPosition(origin.x, origin.y, origin.z)) return false;
            if (_customTeleportPoints.Count >= MaxSavedTeleportPoints) { message = "Teleport point limit reached"; return false; }
            string trimmed = name.Trim();
            var point = new SavedTeleportPoint { Id = Guid.NewGuid().ToString("N"), Name = trimmed, Position = origin };
            _customTeleportPoints.Add(point);
            SaveTeleportLocations();
            detail = "add teleport point " + trimmed + " at " + FormatPosition(origin);
            message = "";
            return true;
        }

        private bool ExecuteRenameTeleportPoint(ZPackage request, out string message, out string detail)
        {
            message = "Location not found"; detail = "rename teleport location";
            string key = request.ReadString(), name = request.ReadString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || name == null || name.Length > MaxLocationNameLength) return false;
            name = name.Trim();
            if (key.StartsWith("world:", StringComparison.Ordinal))
            {
                string prefab = key.Substring("world:".Length);
                var generated = GetUniqueWorldLocations();
                if (string.IsNullOrWhiteSpace(prefab) || prefab.Length > 128 || generated == null || generated.All(location => location.Prefab != prefab)) return false;
                if (name.Length == 0) _locationNameOverrides.Remove(prefab); // Empty resets the Nexus label to the game's name.
                else
                {
                    if (!AdminRules.LocationNameInput(name, false)) { message = "Invalid location name"; return false; }
                    _locationNameOverrides[prefab] = name;
                }
                SaveTeleportLocations();
                detail = "rename generated location " + prefab + " to " + (name.Length == 0 ? "default" : name);
                message = ""; return true;
            }
            if (!key.StartsWith("custom:", StringComparison.Ordinal) || !Guid.TryParseExact(key.Substring("custom:".Length), "N", out Guid id)) return false;
            var point = _customTeleportPoints.FirstOrDefault(entry => entry.Id == id.ToString("N"));
            if (point == null) return false;
            if (!AdminRules.LocationNameInput(name, false)) { message = "Invalid location name"; return false; }
            point.Name = name;
            SaveTeleportLocations();
            detail = "rename teleport point " + id.ToString("N") + " to " + name;
            message = ""; return true;
        }

        private bool ExecuteDeleteTeleportPoint(ZPackage request, out string message, out string detail)
        {
            message = "User-created teleport point not found"; detail = "delete teleport point";
            string key = request.ReadString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || !key.StartsWith("custom:", StringComparison.Ordinal) ||
                !Guid.TryParseExact(key.Substring("custom:".Length), "N", out Guid id)) return false;
            var point = _customTeleportPoints.FirstOrDefault(entry => entry.Id == id.ToString("N"));
            if (point == null) return false;
            detail = "delete teleport point " + point.Name;
            _customTeleportPoints.Remove(point);
            SaveTeleportLocations();
            message = ""; return true;
        }

        private static string FormatPosition(Vector3 position) => position.x.ToString("0.0", CultureInfo.InvariantCulture) + "," +
            position.y.ToString("0.0", CultureInfo.InvariantCulture) + "," + position.z.ToString("0.0", CultureInfo.InvariantCulture);

        private bool WriteLocationPage(ZPackage request, out string message, out Action<ZPackage> reply)
        {
            int page = request.ReadInt();
            message = "Invalid location page"; reply = null;
            if (page < 0 || page > 1000) return false;
            var locations = GetAllNexusLocations();
            if (locations == null) { message = "locations-generating"; return false; }
            int pages = Math.Max(1, (locations.Count + LocationPageSize - 1) / LocationPageSize);
            if (page >= pages) page = pages - 1;
            var entries = locations.Skip(page * LocationPageSize).Take(LocationPageSize).ToArray();
            Logger.LogInfo("Nexus locations page prepared: page=" + page + ", total=" + locations.Count + ", returned=" + entries.Length + ".");
            reply = response =>
            {
                response.Write(page); response.Write(locations.Count); response.Write(entries.Length);
                foreach (var entry in entries)
                { response.Write(entry.Key); response.Write(entry.Name); response.Write(entry.Prefab); response.Write(entry.Position);
                    response.Write(entry.IsCustom); response.Write(entry.NameOverride ?? string.Empty); }
            };
            message = "";
            return true;
        }

        private bool ExecuteLocationTeleport(ZPackage request, Vector3 origin, long adminUid, out string message,
            out string detail, out Action<ZPackage> reply)
        {
            message = "Location not found"; detail = "teleport to unique location"; reply = null;
            string key = request.ReadString(); Vector3 position = request.ReadVector3();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || !AdminRules.WorldPosition(position.x, position.y, position.z) ||
                !AdminRules.WorldPosition(origin.x, origin.y, origin.z)) return false;
            var locations = GetAllNexusLocations();
            var match = locations?.FirstOrDefault(location => location.Key == key &&
                (location.Position - position).sqrMagnitude <= .25f);
            if (match == null) return false;
            if (!_adminReturnPositions.ContainsKey(adminUid)) _adminReturnPositions[adminUid] = origin;
            Vector3 destination = match.Position + Vector3.up * 2f;
            reply = response => response.Write(destination);
            detail = "teleport " + match.Name + " [" + match.Prefab + "]";
            message = "";
            return true;
        }

        private bool ExecuteReturnTeleport(long adminUid, out string message, out string detail, out Action<ZPackage> reply)
        {
            message = "No saved return point"; detail = "return teleport"; reply = null;
            if (!_adminReturnPositions.TryGetValue(adminUid, out Vector3 position) || !AdminRules.WorldPosition(position.x, position.y, position.z)) return false;
            reply = response => response.Write(position + Vector3.up * 1f);
            _adminReturnPositions.Remove(adminUid);
            message = "";
            return true;
        }

        private bool WriteAuditPage(ZPackage request, out string message, out Action<ZPackage> reply)
        {
            int page = request.ReadInt();
            message = "Invalid audit page"; reply = null;
            if (page < 0 || page > 1000) return false;
            int total = _serverAuditEntries.Count;
            int pages = Math.Max(1, (total + AuditPageSize - 1) / AuditPageSize);
            if (page >= pages) page = pages - 1;
            var entries = _serverAuditEntries.AsEnumerable().Reverse().Skip(page * AuditPageSize).Take(AuditPageSize).ToArray();
            reply = response =>
            {
                response.Write(page); response.Write(total); response.Write(entries.Length);
                foreach (string entry in entries) response.Write(entry.Length > 700 ? entry.Substring(0, 700) : entry);
            };
            message = "";
            return true;
        }

        private void AppendAdminAudit(string adminName, long uid, string action, string result)
        {
            if (_auditFilePath == null) EnsureServerAdminState();
            string name = CleanAuditPart(adminName, 40);
            string detail = CleanAuditPart(action, 220);
            string status = CleanAuditPart(result, 90);
            int day = EnvMan.instance == null ? 0 : EnvMan.instance.GetDay();
            string entry = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture) +
                " | day=" + day + " | admin=" + name + " (" + uid.ToString(CultureInfo.InvariantCulture) + ") | " + detail + " | " + status;
            _serverAuditEntries.Add(entry);
            while (_serverAuditEntries.Count > AuditMaxEntries) _serverAuditEntries.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(Paths.ConfigPath);
                if (File.Exists(_auditFilePath) && new FileInfo(_auditFilePath).Length > AuditMaxBytes)
                {
                    File.Copy(_auditFilePath, _auditFilePath + ".1", true);
                    File.WriteAllText(_auditFilePath, string.Empty);
                }
                File.AppendAllText(_auditFilePath, entry + Environment.NewLine);
            }
            catch (Exception ex) { Logger.LogWarning("Could not write Nexus admin audit log: " + ex.Message); }
        }

        private static string CleanAuditPart(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/').Trim();
            return clean.Length <= maxLength ? clean : clean.Substring(0, maxLength);
        }

        private void AuditPeerChanges(ZNetPeer peer, int before, int after, float oldDamage, float damage, float oldXp, float xp)
        {
            for (int bit = 0; bit < 10; bit++)
            {
                int mask = 1 << bit;
                if (((before ^ after) & mask) == 0) continue;
                AppendAdminAudit(peer.m_playerName, peer.m_uid, AdminModeName(bit) + (((after & mask) != 0) ? " enabled" : " disabled"), "OK");
            }
            if (Math.Abs(oldDamage - damage) > .001f) AppendAdminAudit(peer.m_playerName, peer.m_uid,
                "damage multiplier ×" + damage.ToString("0.##", CultureInfo.InvariantCulture), "OK");
            if (Math.Abs(oldXp - xp) > .001f) AppendAdminAudit(peer.m_playerName, peer.m_uid,
                "skill XP multiplier ×" + xp.ToString("0.##", CultureInfo.InvariantCulture), "OK");
        }

        private static string AdminModeName(int bit)
        {
            switch (bit)
            {
                case 0: return "infinite health";
                case 1: return "infinite stamina";
                case 2: return "infinite eitr";
                case 3: return "infinite adrenaline";
                case 4: return "super damage";
                case 5: return "flight";
                case 6: return "free building";
                case 7: return "creature stealth";
                case 8: return "admin hotkeys";
                case 9: return "infinite guardian power";
                default: return "admin mode";
            }
        }

        private bool TeleportLocalPlayer(Vector3 position)
        {
            var player = Player.m_localPlayer;
            if (player == null || !AdminRules.WorldPosition(position.x, position.y, position.z))
            { _status = T("Teleport target is unavailable.", "Точка телепорта недоступна."); return false; }
            TrySetFloatMember(player, "m_teleportCooldown", 2f);
            bool started = player.TeleportTo(position, player.transform.rotation, true);
            _status = started ? T("Teleport started. Use Return to go back.", "Телепорт запущен. Кнопка «Вернуться» перенесёт обратно.") :
                T("Teleport could not start; try again shortly.", "Не удалось начать телепорт; повторите через секунду.");
            return started;
        }
    }
}
