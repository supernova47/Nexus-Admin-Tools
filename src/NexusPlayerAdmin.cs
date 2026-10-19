using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private const int AdminPlayerPageSize = 6;
        private const int MaxTimedBanHours = 8760;
        private const byte TargetCommandTeleport = 1, TargetCommandFreeze = 2, TargetCommandUnfreeze = 3;
        private readonly HashSet<long> _serverFrozenPlayers = new HashSet<long>();
        private readonly Dictionary<string, long> _timedBans = new Dictionary<string, long>(StringComparer.Ordinal);
        private ConfigEntry<string> _savedTimedBanEntries;
        private string _moderationScope;
        private float _nextTimedBanCheck;
        private int _onlinePlayerPage, _bannedPlayerPage, _onlinePlayerTotal, _bannedPlayerTotal, _timedBanHours = 24;
        private int _loadedOnlinePlayerPage = -1, _loadedBannedPlayerPage = -1;
        private long _selectedAdminPlayerUid;
        private string _selectedAdminPlayerName = string.Empty;
        private string _selectedBannedIdentifier = string.Empty;
        private bool _targetLocalFrozen, _selectedAdminPlayerFrozen;
        private List<AdminOnlinePlayer> _adminOnlinePlayers = new List<AdminOnlinePlayer>();
        private List<AdminBanEntry> _adminBannedPlayers = new List<AdminBanEntry>();
        private GameObject _playerReturnPrompt;

        private sealed class AdminOnlinePlayer
        {
            internal long Uid;
            internal string Name;
            internal bool Frozen;
        }

        private sealed class AdminBanEntry
        {
            internal string Identifier;
            internal long ExpiresAt;
        }

        private void BuildPlayersAdminPage(Transform left, Transform right)
        {
            float y = 0;
            HudSection(left, T("ONLINE PLAYERS", "ИГРОКИ В СЕТИ"), ref y);
            var onlineCount = HudLabel(left, T("Loading…", "Загрузка…"), 15);
            HudPlace(onlineCount, 0, y, 280, 30);
            HudButton(left, T("Refresh", "Обновить"), 294, y, 140, 32, RefreshAdminPlayerPages);
            _hudDynamic.Add(() => onlineCount.text = _loadedOnlinePlayerPage < 0 ? T("Loading…", "Загрузка…") :
                T("Online players: ", "Игроков онлайн: ") + _onlinePlayerTotal);
            y += 38;
            for (int i = 0; i < AdminPlayerPageSize; i++)
            {
                int row = i;
                var player = row < _adminOnlinePlayers.Count ? _adminOnlinePlayers[row] : null;
                var button = HudButton(left, player == null ? "—" :
                    player.Name + (player.Frozen ? T("  ·  FROZEN", "  ·  ЗАМОРОЖЕН") : string.Empty),
                    0, y + i * 38, 434, 34, () =>
                    {
                        if (row >= _adminOnlinePlayers.Count) return;
                        _selectedAdminPlayerUid = _adminOnlinePlayers[row].Uid;
                        _selectedAdminPlayerName = _adminOnlinePlayers[row].Name;
                        _selectedAdminPlayerFrozen = _adminOnlinePlayers[row].Frozen;
                        RebuildNativeHud();
                    });
                button.interactable = player != null;
                HudHighlight(button, player != null && player.Uid == _selectedAdminPlayerUid);
            }
            y += AdminPlayerPageSize * 38 + 2;
            int onlinePages = Math.Max(1, (_onlinePlayerTotal + AdminPlayerPageSize - 1) / AdminPlayerPageSize);
            HudButton(left, "◀", 0, y, 60, 32, () => MoveAdminPlayerPage(-1, false)).interactable = _onlinePlayerPage > 0;
            var onlinePage = HudLabel(left, (_onlinePlayerPage + 1) + "/" + onlinePages, 15);
            onlinePage.alignment = TextAnchor.MiddleCenter; HudPlace(onlinePage, 70, y, 294, 30);
            HudButton(left, "▶", 374, y, 60, 32, () => MoveAdminPlayerPage(1, false)).interactable = _onlinePlayerPage + 1 < onlinePages;
            y += 38;
            var selectedLabel = HudLabel(left, string.IsNullOrWhiteSpace(_selectedAdminPlayerName) ?
                T("Select an online player", "Выберите игрока онлайн") : T("Selected: ", "Выбран: ") + _selectedAdminPlayerName, 15, HudGold);
            HudPlace(selectedLabel, 0, y, 434, 28); y += 30;
            HudButton(left, T("Teleport to player", "Телепорт к игроку"), 0, y, 212, 36, TeleportToSelectedPlayer).interactable = CanTargetSelectedPlayer();
            HudButton(left, T("Bring player to me", "Телепортировать ко мне"), 222, y, 212, 36, TeleportSelectedPlayerHere).interactable = CanTargetSelectedPlayer();

            y = 0;
            HudSection(right, T("MODERATION", "МОДЕРАЦИЯ"), ref y);
            var targetName = HudLabel(right, string.IsNullOrWhiteSpace(_selectedAdminPlayerName) ?
                T("Select a player on the left", "Выберите игрока слева") : _selectedAdminPlayerName, 16, HudGold);
            HudPlace(targetName, 0, y, 434, 30); y += 34;
            HudButton(right, T("Kick", "Кикнуть"), 0, y, 104, 36, () => ModerateSelectedPlayer(AdminAction.KickPlayer))
                .interactable = CanTargetSelectedPlayer();
            HudButton(right, T("Permanent ban", "Постоянный бан"), 112, y, 322, 36, () => ModerateSelectedPlayer(AdminAction.BanPlayer, true))
                .interactable = CanTargetSelectedPlayer(); y += 42;
            HudPlace(HudLabel(right, T("Timed ban (hours)", "Временный бан (часы)"), 14), 0, y, 190, 30);
            HudIntegerInput(right, 194, y, 100, 1, MaxTimedBanHours, () => _timedBanHours, value => _timedBanHours = value);
            HudButton(right, T("Ban", "Забанить"), 304, y, 130, 34, () => ModerateSelectedPlayer(AdminAction.BanPlayer, false))
                .interactable = CanTargetSelectedPlayer(); y += 42;
            bool selectedFrozen = _selectedAdminPlayerFrozen;
            HudButton(right, selectedFrozen ? T("Unfreeze selected player", "Разморозить игрока") : T("Freeze selected player", "Заморозить игрока"),
                0, y, 434, 32, () =>
                {
                    long uid = _selectedAdminPlayerUid; bool freeze = !selectedFrozen;
                    RequestWorldAction(AdminAction.FreezePlayer, packet => { packet.Write(uid); packet.Write(freeze); }, _ => RefreshAdminPlayerPages());
                }).interactable = CanTargetSelectedPlayer(); y += 38;

            HudSection(right, T("BANNED PLAYERS", "ЗАБАНЕННЫЕ ИГРОКИ"), ref y);
            var banCount = HudLabel(right, _loadedBannedPlayerPage < 0 ? T("Loading…", "Загрузка…") :
                T("Banned entries: ", "Записей в бан-листе: ") + _bannedPlayerTotal, 14);
            HudPlace(banCount, 0, y, 434, 22); y += 24;
            var ban = _adminBannedPlayers.FirstOrDefault(entry => entry.Identifier == _selectedBannedIdentifier);
            string banText = ban == null ? T("No ban selected", "Запись не выбрана") :
                ban.Identifier + (ban.ExpiresAt <= 0 ? T("  ·  permanent", "  ·  постоянно") :
                    T("  ·  expires ", "  ·  истекает ") + DateTimeOffset.FromUnixTimeSeconds(ban.ExpiresAt).ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            var banLabel = HudLabel(right, banText, 13); banLabel.resizeTextForBestFit = true;
            HudPlace(banLabel, 0, y, 434, 30); y += 32;
            int banPages = Math.Max(1, (_bannedPlayerTotal + AdminPlayerPageSize - 1) / AdminPlayerPageSize);
            HudButton(right, "◀", 0, y, 60, 26, () => MoveAdminPlayerPage(-1, true)).interactable = _bannedPlayerPage > 0;
            var banPage = HudLabel(right, (_bannedPlayerPage + 1) + "/" + banPages, 14);
            banPage.alignment = TextAnchor.MiddleCenter; HudPlace(banPage, 70, y, 294, 24);
            HudButton(right, "▶", 374, y, 60, 26, () => MoveAdminPlayerPage(1, true)).interactable = _bannedPlayerPage + 1 < banPages;
            y += 28;
            for (int i = 0; i < AdminPlayerPageSize; i++)
            {
                int row = i;
                var entry = row < _adminBannedPlayers.Count ? _adminBannedPlayers[row] : null;
                var button = HudButton(right, entry == null ? "—" : entry.Identifier, 0, y + i * 26, 434, 24,
                    () => { if (row < _adminBannedPlayers.Count) { _selectedBannedIdentifier = _adminBannedPlayers[row].Identifier; RebuildNativeHud(); } });
                button.interactable = entry != null;
                HudHighlight(button, entry != null && entry.Identifier == _selectedBannedIdentifier);
                if (entry != null) button.GetComponentInChildren<Text>().resizeTextForBestFit = true;
            }
            HudButton(right, T("Unban selected", "Снять выбранный бан"), 0, y + AdminPlayerPageSize * 26 + 2, 434, 32, () =>
            {
                string identifier = _selectedBannedIdentifier;
                if (string.IsNullOrWhiteSpace(identifier)) return;
                RequestWorldAction(AdminAction.UnbanPlayer, packet => packet.Write(identifier), _ => RefreshAdminPlayerPages());
            }).interactable = ban != null;
            if (_loadedOnlinePlayerPage != _onlinePlayerPage || _loadedBannedPlayerPage != _bannedPlayerPage) RequestAdminPlayerPage();
        }

        private bool CanTargetSelectedPlayer() => _selectedAdminPlayerUid != 0 && _selectedAdminPlayerUid != CurrentAdminPlayerUid();

        private long CurrentAdminPlayerUid() => ZNet.instance != null && ZNet.instance.IsServer() && Player.m_localPlayer != null ?
            Player.m_localPlayer.GetPlayerID() : ZNet.GetUID();

        private void RefreshAdminPlayerPages()
        {
            _loadedOnlinePlayerPage = _loadedBannedPlayerPage = -1;
            _adminOnlinePlayers.Clear(); _adminBannedPlayers.Clear();
            RequestAdminPlayerPage(); RebuildNativeHud();
        }

        private void MoveAdminPlayerPage(int delta, bool banned)
        {
            int total = banned ? _bannedPlayerTotal : _onlinePlayerTotal;
            int pages = Math.Max(1, (total + AdminPlayerPageSize - 1) / AdminPlayerPageSize);
            int current = banned ? _bannedPlayerPage : _onlinePlayerPage;
            int next = Mathf.Clamp(current + delta, 0, pages - 1);
            if (next == current) return;
            if (banned) { _bannedPlayerPage = next; _loadedBannedPlayerPage = -1; _adminBannedPlayers.Clear(); }
            else { _onlinePlayerPage = next; _loadedOnlinePlayerPage = -1; _adminOnlinePlayers.Clear(); }
            RebuildNativeHud();
        }

        private void RequestAdminPlayerPage()
        {
            if (_pendingActions.Values.Any(action => action.Kind != AdminAction.Poll)) return;
            int onlinePage = _onlinePlayerPage, bannedPage = _bannedPlayerPage;
            RequestWorldAction(AdminAction.PlayerRoster, packet => { packet.Write(onlinePage); packet.Write(bannedPage); }, response =>
            {
                int receivedOnlinePage = response.ReadInt(), onlineTotal = response.ReadInt(), onlineCount = response.ReadInt();
                if (receivedOnlinePage < 0 || receivedOnlinePage > 1000 || onlineTotal < 0 || onlineTotal > 128 || onlineCount < 0 || onlineCount > AdminPlayerPageSize)
                { Logger.LogWarning("Rejected invalid Nexus online player page."); return; }
                var players = new List<AdminOnlinePlayer>(onlineCount);
                for (int i = 0; i < onlineCount; i++)
                {
                    long uid = response.ReadLong(); string name = response.ReadString(); bool frozen = response.ReadBool();
                    if (uid == 0 || string.IsNullOrWhiteSpace(name) || name.Length > 80 || players.Any(player => player.Uid == uid)) continue;
                    players.Add(new AdminOnlinePlayer { Uid = uid, Name = name, Frozen = frozen });
                }
                int receivedBanPage = response.ReadInt(), bannedTotal = response.ReadInt(), bannedCount = response.ReadInt();
                if (receivedBanPage < 0 || receivedBanPage > 1000 || bannedTotal < 0 || bannedTotal > 512 || bannedCount < 0 || bannedCount > AdminPlayerPageSize)
                { Logger.LogWarning("Rejected invalid Nexus banned player page."); return; }
                var bans = new List<AdminBanEntry>(bannedCount);
                for (int i = 0; i < bannedCount; i++)
                {
                    string identifier = response.ReadString(); long expiresAt = response.ReadLong();
                    if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 256 || expiresAt < 0 ||
                        expiresAt > DateTimeOffset.MaxValue.ToUnixTimeSeconds() || bans.Any(entry => entry.Identifier == identifier)) continue;
                    bans.Add(new AdminBanEntry { Identifier = identifier, ExpiresAt = expiresAt });
                }
                _onlinePlayerPage = receivedOnlinePage; _onlinePlayerTotal = onlineTotal; _adminOnlinePlayers = players; _loadedOnlinePlayerPage = receivedOnlinePage;
                _bannedPlayerPage = receivedBanPage; _bannedPlayerTotal = bannedTotal; _adminBannedPlayers = bans; _loadedBannedPlayerPage = receivedBanPage;
                var selected = players.FirstOrDefault(player => player.Uid == _selectedAdminPlayerUid);
                if (selected != null) { _selectedAdminPlayerName = selected.Name; _selectedAdminPlayerFrozen = selected.Frozen; }
                if (!bans.Any(entry => entry.Identifier == _selectedBannedIdentifier) && bans.Count > 0)
                    _selectedBannedIdentifier = bans[0].Identifier;
                else if (bans.Count == 0) _selectedBannedIdentifier = string.Empty;
                RebuildNativeHud();
            });
        }

        private void TeleportToSelectedPlayer()
        {
            if (!CanTargetSelectedPlayer()) return;
            long uid = _selectedAdminPlayerUid;
            RequestWorldAction(AdminAction.TeleportToPlayer, packet => packet.Write(uid), response =>
            {
                Vector3 destination = response.ReadVector3();
                if (TeleportLocalPlayer(destination)) { _returnAvailable = _playerReturnAvailable = true; RebuildNativeHud(); }
            });
        }

        private void TeleportSelectedPlayerHere()
        {
            if (!CanTargetSelectedPlayer()) return;
            long uid = _selectedAdminPlayerUid;
            RequestWorldAction(AdminAction.TeleportPlayerHere, packet => packet.Write(uid), _ => RefreshAdminPlayerPages());
        }

        private void ModerateSelectedPlayer(AdminAction action, bool permanent = false)
        {
            if (!CanTargetSelectedPlayer()) return;
            long uid = _selectedAdminPlayerUid;
            int hours = permanent ? 0 : _timedBanHours;
            RequestWorldAction(action, packet =>
            {
                packet.Write(uid);
                if (action == AdminAction.BanPlayer) { packet.Write(permanent); packet.Write(hours); }
            }, _ => RefreshAdminPlayerPages());
        }

        private void TickPlayerReturnPrompt()
        {
            if (!HasPlayerReturnPoint())
            {
                if (_playerReturnPrompt != null) _playerReturnPrompt.SetActive(false);
                return;
            }
            if (GUIManager.IsHeadless() || GUIManager.Instance == null || GUIManager.CustomGUIBack == null) return;
            if (_playerReturnPrompt == null)
            {
                _playerReturnPrompt = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIBack.transform,
                    new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 146f), 440, 82, false);
                _playerReturnPrompt.name = "Nexus_PlayerReturnPrompt";
                var promptGroup = _playerReturnPrompt.GetComponent<CanvasGroup>();
                if (promptGroup == null) promptGroup = _playerReturnPrompt.AddComponent<CanvasGroup>();
                promptGroup.interactable = true;
                promptGroup.blocksRaycasts = true;
                var promptCanvas = _playerReturnPrompt.GetComponent<Canvas>();
                if (promptCanvas == null) promptCanvas = _playerReturnPrompt.AddComponent<Canvas>();
                promptCanvas.overrideSorting = true;
                promptCanvas.sortingOrder = 1100;
                if (_playerReturnPrompt.GetComponent<GraphicRaycaster>() == null)
                    _playerReturnPrompt.AddComponent<GraphicRaycaster>();
                foreach (var image in _playerReturnPrompt.GetComponentsInChildren<Image>(true)) image.raycastTarget = false;
                HudPlace(HudLabel(_playerReturnPrompt.transform, T("An admin saved your previous position", "Администратор сохранил вашу прежнюю точку"), 15, HudGold),
                    12, 8, 378, 28);
                HudButton(_playerReturnPrompt.transform, "×", 398, 5, 30, 28, DismissPlayerReturnPrompt, requireAdmin: false);
                HudButton(_playerReturnPrompt.transform, T("Return to previous position", "Вернуться на прежнее место"),
                    12, 40, 416, 34, RequestPlayerReturn, requireAdmin: false);
            }
            _playerReturnPrompt.SetActive(true);
        }

        private void DestroyPlayerReturnPrompt()
        {
            if (_playerReturnPrompt == null) return;
            UnityEngine.Object.Destroy(_playerReturnPrompt);
            _playerReturnPrompt = null;
        }

        private void EnsureServerModerationState()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            string scope = "ServerModeration." + ZNet.instance.GetWorldUID().ToString("X16");
            if (_moderationScope == scope && _savedTimedBanEntries != null) return;
            _moderationScope = scope;
            _savedTimedBanEntries = Config.Bind(scope, "TimedBans", "v1",
                "Nexus timed ban expiry records for this world; native Valheim bans remain the authority.");
            LoadTimedBans(_savedTimedBanEntries.Value);
        }

        private void LoadTimedBans(string serialized)
        {
            _timedBans.Clear();
            if (string.IsNullOrWhiteSpace(serialized)) return;
            foreach (string row in serialized.Split(';').Skip(1).Take(512))
            {
                string[] fields = row.Split(',');
                if (fields.Length != 2 || !TryDecodeBanIdentifier(fields[0], out string identifier) ||
                    !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long expiry) ||
                    expiry <= 0 || expiry > DateTimeOffset.MaxValue.ToUnixTimeSeconds()) continue;
                _timedBans[identifier] = expiry;
            }
        }

        private void SaveTimedBans()
        {
            if (_savedTimedBanEntries == null) return;
            _savedTimedBanEntries.Value = "v1;" + string.Join(";", _timedBans.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pair.Key)) + "," +
                    pair.Value.ToString(CultureInfo.InvariantCulture)));
            TrySavePreferences();
        }

        private static bool TryDecodeBanIdentifier(string encoded, out string identifier)
        {
            identifier = string.Empty;
            if (string.IsNullOrEmpty(encoded) || encoded.Length > 512) return false;
            try
            {
                identifier = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                return !string.IsNullOrWhiteSpace(identifier) && identifier.Length <= 256;
            }
            catch { return false; }
        }

        private void TickTimedBans()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || Time.unscaledTime < _nextTimedBanCheck) return;
            _nextTimedBanCheck = Time.unscaledTime + 5f;
            EnsureServerModerationState();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string[] expired = _timedBans.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray();
            foreach (string identifier in expired)
            {
                try
                {
                    if (ZNet.instance.Banned != null && ZNet.instance.Banned.Contains(identifier)) ZNet.instance.Unban(identifier);
                    _timedBans.Remove(identifier);
                }
                catch (Exception ex) { Logger.LogWarning("Could not expire timed Nexus ban: " + ex.Message); }
            }
            if (expired.Length > 0) SaveTimedBans();
        }

        private List<AdminOnlinePlayer> GetOnlineAdminPlayers()
        {
            var result = new List<AdminOnlinePlayer>();
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return result;
            foreach (var peer in ZNet.instance.GetPeers().Where(peer => peer != null && peer.IsReady() && peer.m_socket != null && peer.m_socket.IsConnected())
                .OrderBy(peer => peer.m_playerName, StringComparer.OrdinalIgnoreCase).Take(128))
            {
                result.Add(new AdminOnlinePlayer { Uid = peer.m_uid, Name = CleanAuditPart(peer.m_playerName, 80), Frozen = _serverFrozenPlayers.Contains(peer.m_uid) });
            }
            var local = Player.m_localPlayer;
            if (ZNet.instance.IsServer() && local != null)
            {
                long uid = local.GetPlayerID();
                if (result.All(player => player.Uid != uid))
                    result.Add(new AdminOnlinePlayer { Uid = uid, Name = CleanAuditPart(local.GetPlayerName(), 80), Frozen = _serverFrozenPlayers.Contains(uid) });
            }
            return result.OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase).Take(128).ToList();
        }

        private bool WriteAdminPlayerPage(ZPackage request, out string message, out Action<ZPackage> reply)
        {
            int playerPage = request.ReadInt(), banPage = request.ReadInt();
            message = "Invalid player list page"; reply = null;
            if (playerPage < 0 || playerPage > 1000 || banPage < 0 || banPage > 1000 || ZNet.instance == null || !ZNet.instance.IsServer()) return false;
            EnsureServerModerationState();
            var players = GetOnlineAdminPlayers();
            var banned = (ZNet.instance.Banned ?? new List<string>()).Where(identifier => !string.IsNullOrWhiteSpace(identifier) && identifier.Length <= 256)
                .Distinct(StringComparer.Ordinal).OrderBy(identifier => identifier, StringComparer.OrdinalIgnoreCase).Take(512)
                .Select(identifier => new AdminBanEntry { Identifier = identifier,
                    ExpiresAt = _timedBans.TryGetValue(identifier, out long expiry) ? expiry : 0 }).ToList();
            int playerPages = Math.Max(1, (players.Count + AdminPlayerPageSize - 1) / AdminPlayerPageSize);
            int banPages = Math.Max(1, (banned.Count + AdminPlayerPageSize - 1) / AdminPlayerPageSize);
            playerPage = Mathf.Clamp(playerPage, 0, playerPages - 1);
            banPage = Mathf.Clamp(banPage, 0, banPages - 1);
            var playerRows = players.Skip(playerPage * AdminPlayerPageSize).Take(AdminPlayerPageSize).ToArray();
            var banRows = banned.Skip(banPage * AdminPlayerPageSize).Take(AdminPlayerPageSize).ToArray();
            reply = response =>
            {
                response.Write(playerPage); response.Write(players.Count); response.Write(playerRows.Length);
                foreach (var player in playerRows) { response.Write(player.Uid); response.Write(player.Name); response.Write(player.Frozen); }
                response.Write(banPage); response.Write(banned.Count); response.Write(banRows.Length);
                foreach (var entry in banRows) { response.Write(entry.Identifier); response.Write(entry.ExpiresAt); }
            };
            message = string.Empty;
            return true;
        }

        private ZNetPeer FindOnlinePeer(long uid) => ZNet.instance?.GetPeers().FirstOrDefault(peer => peer != null && peer.m_uid == uid &&
            peer.IsReady() && peer.m_socket != null && peer.m_socket.IsConnected());

        private bool TryGetOnlineTarget(long uid, out ZNetPeer peer, out bool localServerPlayer, out Vector3 position, out string name)
        {
            peer = FindOnlinePeer(uid);
            localServerPlayer = false; position = Vector3.zero; name = string.Empty;
            if (peer != null)
            {
                var zdo = ZDOMan.instance?.GetZDO(peer.m_characterID);
                position = zdo != null ? zdo.GetPosition() : peer.GetRefPos();
                name = peer.m_playerName ?? "player";
                return FinitePosition(position);
            }
            var local = Player.m_localPlayer;
            if (ZNet.instance != null && ZNet.instance.IsServer() && local != null && local.GetPlayerID() == uid)
            {
                localServerPlayer = true; position = local.transform.position; name = local.GetPlayerName();
                return FinitePosition(position);
            }
            return false;
        }

        private bool ExecuteTeleportToPlayer(ZPackage request, Vector3 origin, long adminUid, out string message,
            out string detail, out Action<ZPackage> reply)
        {
            long targetUid = request.ReadLong();
            message = "Online player not found"; detail = "teleport to player"; reply = null;
            if (targetUid == adminUid || !AdminRules.WorldPosition(origin.x, origin.y, origin.z) ||
                !TryGetOnlineTarget(targetUid, out _, out _, out Vector3 position, out string name)) return false;
            if (!_adminReturnPositions.ContainsKey(adminUid)) _adminReturnPositions[adminUid] = origin;
            Vector3 destination = position + Vector3.up * 1.5f;
            reply = response => response.Write(destination);
            detail = "teleport to player " + CleanAuditPart(name, 80);
            message = string.Empty;
            return true;
        }

        private bool ExecuteTeleportPlayerHere(ZPackage request, Vector3 origin, long adminUid, out string message, out string detail)
        {
            long targetUid = request.ReadLong();
            message = "Online player not found"; detail = "teleport player to administrator";
            if (targetUid == adminUid || !AdminRules.WorldPosition(origin.x, origin.y, origin.z) ||
                !TryGetOnlineTarget(targetUid, out var peer, out bool localTarget, out Vector3 oldPosition, out string name)) return false;
            _adminReturnPositions[targetUid] = oldPosition;
            Vector3 destination = origin + Vector3.up * 1.5f;
            string adminName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "server administrator";
            if (localTarget)
            {
                if (!TeleportLocalPlayer(destination)) { _adminReturnPositions.Remove(targetUid); message = "Target teleport could not start"; return false; }
                _returnAvailable = _playerReturnAvailable = true;
                ShowPlayerNotification(T("You were teleported by ", "Вас телепортировал администратор ") + CleanAuditPart(adminName, 80) + ".");
            }
            else if (!SendTargetCommand(peer, TargetCommandTeleport, targetUid, destination, adminName))
            { _adminReturnPositions.Remove(targetUid); message = "Target teleport command could not be sent"; return false; }
            detail = "teleport player " + CleanAuditPart(name, 80) + " to administrator";
            message = string.Empty;
            return true;
        }

        private bool ExecuteKickPlayer(ZPackage request, long adminUid, out string message, out string detail)
        {
            long targetUid = request.ReadLong();
            message = "Online player not found"; detail = "kick player";
            var peer = FindOnlinePeer(targetUid);
            if (peer == null || peer.m_uid == adminUid || string.IsNullOrWhiteSpace(peer.m_socket.GetHostName())) return false;
            string name = CleanAuditPart(peer.m_playerName, 80);
            ZNet.instance.Kick(peer.m_socket.GetHostName());
            _serverFrozenPlayers.Remove(targetUid);
            detail = "kick " + name;
            message = string.Empty;
            return true;
        }

        private bool ExecuteBanPlayer(ZPackage request, long adminUid, out string message, out string detail)
        {
            long targetUid = request.ReadLong(); bool permanent = request.ReadBool(); int hours = request.ReadInt();
            message = "Online player not found"; detail = "ban player";
            if ((!permanent && (hours < 1 || hours > MaxTimedBanHours)) || (permanent && hours != 0)) return false;
            var peer = FindOnlinePeer(targetUid);
            if (peer == null || peer.m_uid == adminUid || string.IsNullOrWhiteSpace(peer.m_socket.GetHostName())) return false;
            string identifier = peer.m_socket.GetHostName();
            string name = CleanAuditPart(peer.m_playerName, 80);
            ZNet.instance.Ban(identifier);
            if (permanent) _timedBans.Remove(identifier);
            else _timedBans[identifier] = DateTimeOffset.UtcNow.AddHours(hours).ToUnixTimeSeconds();
            SaveTimedBans();
            _serverFrozenPlayers.Remove(targetUid);
            detail = permanent ? "permanent ban " + name : "timed ban " + name + " for " + hours + " hours";
            message = string.Empty;
            return true;
        }

        private bool ExecuteUnbanPlayer(ZPackage request, out string message, out string detail)
        {
            string identifier = request.ReadString();
            message = "Ban entry not found"; detail = "unban player";
            if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 256 || ZNet.instance.Banned == null ||
                !ZNet.instance.Banned.Contains(identifier)) return false;
            ZNet.instance.Unban(identifier);
            _timedBans.Remove(identifier); SaveTimedBans();
            detail = "unban " + CleanAuditPart(identifier, 120);
            message = string.Empty;
            return true;
        }

        private bool ExecuteFreezePlayer(ZPackage request, long adminUid, out string message, out string detail)
        {
            long targetUid = request.ReadLong(); bool freeze = request.ReadBool();
            message = "Online player not found"; detail = freeze ? "freeze player" : "unfreeze player";
            if (targetUid == adminUid || !TryGetOnlineTarget(targetUid, out var peer, out bool localTarget, out _, out string name)) return false;
            if (freeze) _serverFrozenPlayers.Add(targetUid); else _serverFrozenPlayers.Remove(targetUid);
            string adminName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "server administrator";
            if (localTarget)
            {
                _targetLocalFrozen = freeze;
                ShowPlayerNotification(freeze ? T("You were frozen by ", "Вас заморозил администратор ") + CleanAuditPart(adminName, 80) + "." :
                    T("Your movement was unfrozen by ", "Администратор разморозил вас: ") + CleanAuditPart(adminName, 80) + ".");
            }
            else if (!SendTargetCommand(peer, freeze ? TargetCommandFreeze : TargetCommandUnfreeze, targetUid, Vector3.zero, adminName))
            {
                if (freeze) _serverFrozenPlayers.Remove(targetUid); else _serverFrozenPlayers.Add(targetUid);
                message = "Player command could not be sent";
                return false;
            }
            detail = (freeze ? "freeze " : "unfreeze ") + CleanAuditPart(name, 80);
            message = string.Empty;
            return true;
        }

        private bool ExecutePlayerReturn(long playerUid, out string message, out string detail, out Action<ZPackage> reply)
        {
            message = "No saved return point"; detail = "return from administrator teleport"; reply = null;
            if (!_adminReturnPositions.TryGetValue(playerUid, out Vector3 position) || !AdminRules.WorldPosition(position.x, position.y, position.z)) return false;
            reply = response => response.Write(position + Vector3.up);
            _adminReturnPositions.Remove(playerUid);
            message = string.Empty;
            return true;
        }

        private bool ExecuteDismissPlayerReturn(long playerUid, out string message, out string detail)
        {
            message = "No saved return point";
            detail = "dismiss return point";
            if (playerUid == 0 || !_adminReturnPositions.Remove(playerUid)) return false;
            message = string.Empty;
            return true;
        }

        private bool SendTargetCommand(ZNetPeer peer, byte command, long targetUid, Vector3 destination, string adminName)
        {
            if (peer?.m_rpc == null || !peer.IsReady()) return false;
            try
            {
                var packet = new ZPackage(); packet.Write(AdminRules.Protocol); packet.Write(command); packet.Write(targetUid);
                packet.Write(destination); packet.Write(CleanAuditPart(adminName, 80));
                peer.m_rpc.Invoke(TargetCommandRpc, packet);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not send Nexus command to target player: " + ex.Message);
                return false;
            }
        }

        private void ReceiveTargetCommand(ZRpc rpc, ZPackage packet)
        {
            var server = ZNet.instance?.GetServerPeer();
            if (ZNet.instance == null || ZNet.instance.IsServer() || server == null || !ReferenceEquals(server.m_rpc, rpc) ||
                !server.IsReady() || packet == null || packet.Size() > AdminRules.MaxPacketBytes) return;
            try
            {
                if (packet.ReadInt() != AdminRules.Protocol) return;
                byte command = packet.ReadByte(); long targetUid = packet.ReadLong(); Vector3 destination = packet.ReadVector3(); string adminName = packet.ReadString();
                if (targetUid != ZNet.GetUID() || adminName == null || adminName.Length > 80 ||
                    (command != TargetCommandTeleport && command != TargetCommandFreeze && command != TargetCommandUnfreeze)) return;
                if (command == TargetCommandTeleport)
                {
                    if (!FinitePosition(destination) || !TeleportLocalPlayer(destination)) return;
                    _returnAvailable = _playerReturnAvailable = true;
                    ShowPlayerNotification(T("You were teleported by ", "Вас телепортировал администратор ") + adminName + ".");
                    RebuildNativeHud();
                }
                else
                {
                    _targetLocalFrozen = command == TargetCommandFreeze;
                    ShowPlayerNotification(_targetLocalFrozen ? T("You were frozen by ", "Вас заморозил администратор ") + adminName + "." :
                        T("Your movement was unfrozen by ", "Администратор разморозил вас: ") + adminName + ".");
                }
            }
            catch (Exception ex) { Logger.LogWarning("Rejected Nexus player command from server: " + ex.Message); }
        }

        private void ShowPlayerNotification(string message)
        {
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message, 0, null, false, false);
        }

        private void PruneDisconnectedFrozenPlayers()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var connected = new HashSet<long>(ZNet.instance.GetPeers().Where(peer => peer != null && peer.IsReady() &&
                peer.m_socket != null && peer.m_socket.IsConnected()).Select(peer => peer.m_uid));
            if (Player.m_localPlayer != null) connected.Add(Player.m_localPlayer.GetPlayerID());
            _serverFrozenPlayers.RemoveWhere(uid => !connected.Contains(uid));
        }

        private List<ZDOID> GetFrozenCharacterIds()
        {
            var result = new HashSet<ZDOID>();
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return result.ToList();
            foreach (long uid in _serverFrozenPlayers)
            {
                var peer = FindOnlinePeer(uid);
                if (peer != null && peer.m_characterID != ZDOID.None) result.Add(peer.m_characterID);
                else if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == uid)
                {
                    var view = Player.m_localPlayer.GetComponent<ZNetView>();
                    if (view != null && view.IsValid()) result.Add(view.GetZDO().m_uid);
                }
            }
            return result.Take(256).ToList();
        }

        private bool IsNexusFrozen(Character character)
        {
            if (!(character is Player)) return false;
            var view = character.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            ZDOID id = view.GetZDO().m_uid;
            if (ZNet.instance != null && ZNet.instance.IsServer()) return GetFrozenCharacterIds().Contains(id);
            return ReferenceEquals(character, Player.m_localPlayer) && _targetLocalFrozen || _networkFrozenCharacters.Contains(id);
        }

        private void RequestPlayerReturn()
        {
            if (!HasPlayerReturnPoint()) return;
            long playerUid = ZNet.instance != null && ZNet.instance.IsServer() && Player.m_localPlayer != null ?
                Player.m_localPlayer.GetPlayerID() : ZNet.GetUID();
            if (ZNet.instance.IsServer())
            {
                var response = new ZPackage();
                if (ExecutePlayerReturn(playerUid, out _, out _, out var extra) && extra != null)
                {
                    extra(response); response.SetPos(0); Vector3 destination = response.ReadVector3();
                    if (TeleportLocalPlayer(destination)) { _playerReturnAvailable = _returnAvailable = false; DestroyPlayerReturnPrompt(); RebuildNativeHud(); }
                }
                else ShowPlayerNotification(T("No return point is saved.", "Точка возврата не найдена."));
                return;
            }
            SendRequest(AdminAction.PlayerReturn, null, response =>
            {
                Vector3 destination = response.ReadVector3();
                if (TeleportLocalPlayer(destination)) { _playerReturnAvailable = _returnAvailable = false; DestroyPlayerReturnPrompt(); RebuildNativeHud(); }
            });
        }

        private void DismissPlayerReturnPrompt()
        {
            if (!HasPlayerReturnPoint()) return;
            long playerUid = ZNet.instance != null && ZNet.instance.IsServer() && Player.m_localPlayer != null ?
                Player.m_localPlayer.GetPlayerID() : ZNet.GetUID();
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                if (!ExecuteDismissPlayerReturn(playerUid, out string message, out _))
                {
                    Logger.LogDebug("Could not dismiss the saved return point: " + message);
                    return;
                }
                _playerReturnAvailable = _returnAvailable = false;
                DestroyPlayerReturnPrompt();
                return;
            }
            if (!SendRequest(AdminAction.PlayerReturnDismiss, null, _ =>
            {
                _playerReturnAvailable = _returnAvailable = false;
                DestroyPlayerReturnPrompt();
            }))
            {
                _status = T("Could not contact the server to dismiss the return point.",
                    "Не удалось связаться с сервером для удаления точки возврата.");
            }
        }

        private bool HasPlayerReturnPoint() => _playerReturnAvailable || _returnAvailable;
    }
}
