using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    [BepInEx.BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    public sealed partial class ValheimSpawnerPlugin
    {
        private const string RequestRpc = "SN_Nexus_Request185", ReplyRpc = "SN_Nexus_Reply185";
        private const string TargetCommandRpc = "SN_Nexus_Target185";
        private enum AdminAction { Poll, Local, Time, Spawn, Cleanup, Weather, Raids, Locations, Teleport, Return, Audit, AddLocation, RenameLocation, DeleteLocation,
            PlayerRoster, TeleportToPlayer, TeleportPlayerHere, KickPlayer, BanPlayer, UnbanPlayer, FreezePlayer, ClearDroppedItems, PlayerReturn,
            PlayerReturnDismiss }
        private sealed class PeerState
        {
            internal readonly AdminRequestGate Gate = new AdminRequestGate();
            internal int Modes;
            internal float Damage, Xp;
            internal float LastSeen;
        }
        private sealed class PendingAction
        {
            internal ZRpc Rpc;
            internal float Until;
            internal AdminAction Kind;
            internal Action<ZPackage> Callback;
        }
        private readonly Dictionary<ZRpc, PeerState> _peerStates = new Dictionary<ZRpc, PeerState>();
        private readonly Dictionary<int, PendingAction> _pendingActions = new Dictionary<int, PendingAction>();
        private readonly HashSet<ZDOID> _networkGhosts = new HashSet<ZDOID>();
        private readonly HashSet<ZDOID> _networkFrozenCharacters = new HashSet<ZDOID>();
        private ZNet _networkSession;
        private ZRpc _serverConnection;
        private float _nextNetworkPoll, _accessUntil, _ghostSnapshotUntil;
        private int _nextRequestId;
        private bool _executingApprovedLocal;

        private bool HasAdminAccess => ZNet.instance != null && Player.m_localPlayer != null &&
            (ZNet.instance.IsServer() || (_serverConnection != null && Time.unscaledTime < _accessUntil &&
                ReferenceEquals(ZNet.instance.GetServerPeer()?.m_rpc, _serverConnection)));

        private void InstallNetworkPatches()
        {
            _harmony.Patch(AccessTools.Method(typeof(ZNet), "OnNewConnection"), postfix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(ConnectionPostfix)));
            _harmony.Patch(AccessTools.Method(typeof(Player), "InGhostMode"), postfix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(GhostModePostfix)));
            _harmony.Patch(AccessTools.Method(typeof(BaseAI), "IsEnemy", new[] { typeof(Character), typeof(Character) }), postfix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(GhostEnemyPostfix)));
            var walking = AccessTools.Method(typeof(Character), "UpdateWalking", new[] { typeof(float) });
            if (walking != null) _harmony.Patch(walking, prefix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(FrozenWalkingPrefix)));
            var jump = AccessTools.Method(typeof(Character), "Jump", new[] { typeof(bool) });
            if (jump != null) _harmony.Patch(jump, prefix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(FrozenJumpPrefix)));
            var dodge = AccessTools.Method(typeof(Player), "Dodge", new[] { typeof(Vector3) });
            if (dodge != null) _harmony.Patch(dodge, prefix:
                new HarmonyMethod(typeof(ValheimSpawnerPlugin), nameof(FrozenDodgePrefix)));
        }

        private static void ConnectionPostfix(ZNetPeer __0) => _activeInstance?.RegisterPeer(__0);
        private void RegisterPeer(ZNetPeer peer)
        {
            if (peer?.m_rpc == null) return;
            // Direct ZRpc binds the request to the authenticated transport connection.
            // Routed RPC sender IDs are deliberately not used for authorization.
            peer.m_rpc.Register<ZPackage>(RequestRpc, ReceiveRequest);
            peer.m_rpc.Register<ZPackage>(ReplyRpc, ReceiveReply);
            peer.m_rpc.Register<ZPackage>(CleanupOwnerRpc, ReceiveOwnerCleanup);
            peer.m_rpc.Register<ZPackage>(TargetCommandRpc, ReceiveTargetCommand);
        }

        private ZNetPeer AuthenticatedAdmin(ZRpc rpc)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || rpc == null) return null;
            var peer = ZNet.instance.GetPeers().Find(p => ReferenceEquals(p.m_rpc, rpc));
            if (peer == null || !peer.IsReady() || peer.m_socket == null || !peer.m_socket.IsConnected()) return null;
            return ZNet.instance.IsAdmin(peer.m_socket.GetHostName()) ? peer : null;
        }

        private void TickNetwork()
        {
            var net = ZNet.instance;
            if (!ReferenceEquals(net, _networkSession))
            {
                ResetNetwork();
                _networkSession = net;
                if (net != null) foreach (var peer in net.GetPeers()) RegisterPeer(peer);
            }
            if (net == null) { TickAdminPreferences(); return; }
            if (net.IsServer()) { EnsureServerAdminState(); PruneDisconnectedFrozenPlayers(); TickTimedBans(); }
            if (!net.IsServer() && Time.unscaledTime >= _ghostSnapshotUntil)
            { _networkGhosts.Clear(); _networkFrozenCharacters.Clear(); }
            var server = net.IsServer() ? null : net.GetServerPeer()?.m_rpc;
            if (!ReferenceEquals(server, _serverConnection))
            {
                _accessUntil = 0;
                _pendingActions.Clear();
                _networkGhosts.Clear();
                _networkFrozenCharacters.Clear();
                _serverConnection = server;
                _nextNetworkPoll = 0;
            }
            foreach (int id in _pendingActions.Where(p => p.Value.Until < Time.unscaledTime).Select(p => p.Key).ToArray())
            {
                if (_pendingActions[id].Kind != AdminAction.Poll)
                    _status = T("Server request timed out; no automatic retry.", "Сервер не ответил; автоматического повтора нет.");
                _pendingActions.Remove(id);
            }
            if (Time.unscaledTime >= _nextNetworkPoll)
            {
                _nextNetworkPoll = Time.unscaledTime + 1f;
                if (net.IsServer()) RefreshServerGhosts();
                else if (_serverConnection != null)
                {
                    SendRequest(AdminAction.Poll, p => { p.Write(HasAdminAccess && AdminContextMatches ? DesiredModeBits : 0);
                        p.Write(_superDamageMultiplier); p.Write(_skillXpMultiplier); }, null);
                }
            }
            TickAdminPreferences();
        }

        private void ResetNetwork()
        {
            _accessUntil = 0;
            _serverConnection = null;
            _pendingActions.Clear();
            _peerStates.Clear();
            _networkGhosts.Clear();
            _networkFrozenCharacters.Clear();
            _serverFrozenPlayers.Clear();
            _targetLocalFrozen = false;
            _playerReturnAvailable = _returnAvailable = false;
            DestroyPlayerReturnPrompt();
            _nextRequestId = 0;
            _nextNetworkPoll = 0;
            SuspendAdminEffects();
            ResetAdminContext();
        }

        private void RefreshServerGhosts()
        {
            _networkGhosts.Clear();
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            foreach (var pair in _peerStates.ToArray())
            {
                var peer = AuthenticatedAdmin(pair.Key);
                if (peer == null || Time.unscaledTime - pair.Value.LastSeen > 3f)
                { pair.Value.Modes = 0; continue; }
                if ((pair.Value.Modes & 128) != 0 && peer.m_characterID != ZDOID.None)
                    _networkGhosts.Add(peer.m_characterID);
            }
            if (HasAdminAccess && _adminGhost && Player.m_localPlayer != null)
            {
                var view = Player.m_localPlayer.GetComponent<ZNetView>();
                if (view != null && view.IsValid()) _networkGhosts.Add(view.GetZDO().m_uid);
            }
            var active = new HashSet<ZRpc>(ZNet.instance.GetPeers().Select(p => p.m_rpc));
            foreach (var rpc in _peerStates.Keys.Where(r => !active.Contains(r)).ToArray()) _peerStates.Remove(rpc);
        }

        private static void GhostModePostfix(Player __instance, ref bool __result)
        {
            var mod = _activeInstance;
            if (mod != null && mod.IsNexusGhost(__instance)) __result = true;
        }

        private bool IsNexusGhost(Character character)
        {
            if (!(character is Player)) return false;
            if (ReferenceEquals(character, Player.m_localPlayer)) return HasAdminAccess && AdminContextMatches && _adminGhost;
            if (ZNet.instance == null || (!ZNet.instance.IsServer() && Time.unscaledTime >= _ghostSnapshotUntil)) return false;
            var view = character.GetComponent<ZNetView>();
            return view != null && view.IsValid() && _networkGhosts.Contains(view.GetZDO().m_uid);
        }

        private static void GhostEnemyPostfix(Character __0, Character __1, ref bool __result)
        {
            var mod = _activeInstance;
            if (!__result || mod == null || __0 == null || __1 == null) return;
            // Existing aggro must clear too; sensory checks alone retain the previous target.
            // PvP relationships remain vanilla.
            // Keep the mob-to-player check suppressed, while leaving player-to-mob
            // hostility intact so a hidden admin can still attack and kill creatures.
            if (!__0.IsPlayer() && mod.IsNexusGhost(__1)) __result = false;
        }

        private static void FrozenWalkingPrefix(Character __instance)
        {
            var mod = _activeInstance;
            if (mod == null || !mod.IsNexusFrozen(__instance)) return;
            __instance.SetMoveDir(Vector3.zero);
            TrySetBoolMember(__instance, "m_run", false);
            TrySetBoolMember(__instance, "m_running", false);
            if (ReadMember(__instance, "m_body") is Rigidbody body)
            {
                Vector3 velocity = body.linearVelocity;
                body.linearVelocity = new Vector3(0f, velocity.y, 0f);
            }
        }

        private static bool FrozenJumpPrefix(Character __instance) =>
            _activeInstance == null || !_activeInstance.IsNexusFrozen(__instance);

        private static bool FrozenDodgePrefix(Player __instance) =>
            _activeInstance == null || !_activeInstance.IsNexusFrozen(__instance);

        private bool SendRequest(AdminAction kind, Action<ZPackage> write, Action<ZPackage> callback)
        {
            if (_serverConnection == null || _pendingActions.Count >= 8) return false;
            int id = ++_nextRequestId;
            var packet = new ZPackage(); packet.Write(AdminRules.Protocol); packet.Write(id); packet.Write((int)kind);
            write?.Invoke(packet);
            _pendingActions[id] = new PendingAction { Rpc = _serverConnection, Kind = kind,
                Until = Time.unscaledTime + (kind == AdminAction.Poll ? 2f : 6f), Callback = callback };
            _serverConnection.Invoke(RequestRpc, packet);
            if (kind != AdminAction.Poll) _status = T("Waiting for server…", "Ожидание сервера…");
            return true;
        }

        private void ReceiveRequest(ZRpc rpc, ZPackage packet)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || packet == null || packet.Size() > AdminRules.MaxPacketBytes) return;
            var connected = ZNet.instance.GetPeers().Find(p => ReferenceEquals(p.m_rpc, rpc));
            if (connected == null || !connected.IsReady()) return;
            try
            {
                if (packet.ReadInt() != AdminRules.Protocol) return;
                int id = packet.ReadInt(); var kind = (AdminAction)packet.ReadInt();
                if (!_peerStates.TryGetValue(rpc, out var state)) _peerStates[rpc] = state = new PeerState();
                if (!Enum.IsDefined(typeof(AdminAction), kind)) return;
                if (!state.Gate.TryAccept(id, kind == AdminAction.Poll, Time.unscaledTime))
                {
                    if (kind != AdminAction.Poll) ReplyToRequest(rpc, id, kind, AuthenticatedAdmin(rpc) != null, false, "busy");
                    return;
                }
                bool granted = AuthenticatedAdmin(rpc) != null;
                bool ok = false;
                string message = granted ? "" : "Administrator access required";
                Action<ZPackage> replyExtra = null;
                if (kind == AdminAction.Poll)
                {
                    int bits = packet.ReadInt(); float damage = packet.ReadSingle(), xp = packet.ReadSingle();
                    if (!AdminRules.Modes(bits, damage, xp)) return;
                    int oldBits = state.Modes; float oldDamage = state.Damage, oldXp = state.Xp;
                    if (state.LastSeen > 0 && (oldBits != bits || Math.Abs(oldDamage - damage) > .001f || Math.Abs(oldXp - xp) > .001f))
                        AuditPeerChanges(connected, oldBits, bits, oldDamage, damage, oldXp, xp);
                    state.Modes = granted ? bits : 0;
                    state.Damage = damage; state.Xp = xp;
                    state.LastSeen = Time.unscaledTime;
                    RefreshServerGhosts();
                    ok = granted;
                }
                else if (granted)
                {
                    var playerZdo = ZDOMan.instance?.GetZDO(connected.m_characterID);
                    string detail;
                    ok = ExecuteServerAction(kind, packet, playerZdo?.GetPosition() ?? connected.GetRefPos(), connected.m_uid, out message, out detail, out replyExtra);
                    if (kind != AdminAction.Locations && kind != AdminAction.Audit && kind != AdminAction.PlayerRoster)
                        AppendAdminAudit(connected.m_playerName, connected.m_uid, detail ?? kind.ToString(), ok ? "OK" : (message ?? "rejected"));
                    Logger.LogInfo("Nexus admin action: peer=" + connected.m_uid + " action=" + kind + " success=" + ok);
                }
                else if (kind == AdminAction.PlayerReturn || kind == AdminAction.PlayerReturnDismiss)
                {
                    var playerZdo = ZDOMan.instance?.GetZDO(connected.m_characterID);
                    string detail;
                    ok = ExecuteServerAction(kind, packet, playerZdo?.GetPosition() ?? connected.GetRefPos(), connected.m_uid,
                        out message, out detail, out replyExtra);
                }
                ReplyToRequest(rpc, id, kind, granted, ok, message, replyExtra);
            }
            catch (Exception ex) { Logger.LogDebug("Rejected malformed Nexus request: " + ex.Message); }
        }

        private void ReplyToRequest(ZRpc rpc, int id, AdminAction kind, bool granted, bool ok, string message, Action<ZPackage> replyExtra = null)
        {
            var reply = new ZPackage(); reply.Write(AdminRules.Protocol); reply.Write(id); reply.Write((int)kind);
            reply.Write(granted); reply.Write(ok); reply.Write(message);
            reply.Write(_networkGhosts.Count);
            foreach (var ghost in _networkGhosts) reply.Write(ghost);
            var frozenCharacters = GetFrozenCharacterIds();
            reply.Write(frozenCharacters.Count);
            foreach (var character in frozenCharacters) reply.Write(character);
            reply.Write(_serverForcedWeather ?? string.Empty); reply.Write(_serverRaidsDisabled);
            replyExtra?.Invoke(reply);
            rpc.Invoke(ReplyRpc, reply);
        }

        private void ReceiveReply(ZRpc rpc, ZPackage packet)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || !ReferenceEquals(rpc, _serverConnection) ||
                !ReferenceEquals(rpc, ZNet.instance.GetServerPeer()?.m_rpc) || packet == null || packet.Size() > AdminRules.MaxPacketBytes) return;
            try
            {
                if (packet.ReadInt() != AdminRules.Protocol) return;
                int id = packet.ReadInt(); var kind = (AdminAction)packet.ReadInt();
                if (!_pendingActions.TryGetValue(id, out var pending) || !ReferenceEquals(pending.Rpc, rpc) ||
                    pending.Kind != kind || pending.Until < Time.unscaledTime) return;
                bool granted = packet.ReadBool(), ok = packet.ReadBool(); string message = packet.ReadString();
                int count = packet.ReadInt(); if (count < 0 || count > 256) return;
                var ghosts = new HashSet<ZDOID>();
                for (int i = 0; i < count; i++) ghosts.Add(packet.ReadZDOID());
                int frozenCount = packet.ReadInt(); if (frozenCount < 0 || frozenCount > 256) return;
                var frozenCharacters = new HashSet<ZDOID>();
                for (int i = 0; i < frozenCount; i++) frozenCharacters.Add(packet.ReadZDOID());
                string weather = packet.ReadString(); bool raidsDisabled = packet.ReadBool();
                _pendingActions.Remove(id); // Consume before executing any callback.
                _accessUntil = granted ? Time.unscaledTime + 2.5f : 0;
                _ghostSnapshotUntil = Time.unscaledTime + 3f;
                _networkGhosts.Clear(); foreach (var ghost in ghosts) _networkGhosts.Add(ghost);
                _networkFrozenCharacters.Clear(); foreach (var character in frozenCharacters) _networkFrozenCharacters.Add(character);
                ApplySyncedWorldAdminState(weather, raidsDisabled);
                if (!granted && kind != AdminAction.PlayerReturn && kind != AdminAction.PlayerReturnDismiss)
                { _pendingActions.Clear(); SuspendAdminEffects(); _status = T("Administrator access required.", "Нужны права администратора сервера."); return; }
                if (kind != AdminAction.Poll)
                {
                    _status = string.IsNullOrEmpty(message) ? (ok ? T("Server action completed.", "Сервер выполнил действие.") :
                        T("Action rejected.", "Действие отклонено.")) : FormatServerMessage(message);
                    if (ok && pending.Callback != null)
                    {
                        _executingApprovedLocal = granted;
                        try { pending.Callback(packet); } finally { _executingApprovedLocal = false; }
                    }
                }
            }
            catch (Exception ex) { Logger.LogWarning("Invalid Nexus server reply: " + ex.Message); }
        }

        private bool AuthorizeLocal(Action retry, string detail = "local admin action")
        {
            if (!HasAdminAccess) { _status = T("Administrator access required.", "Нужны права администратора сервера."); return false; }
            if (ZNet.instance.IsServer() || _executingApprovedLocal)
            {
                if (ZNet.instance.IsServer() && !_executingApprovedLocal)
                    AppendAdminAudit(Player.m_localPlayer?.GetPlayerName(), Player.m_localPlayer?.GetPlayerID() ?? ZNet.GetUID(), detail, "OK");
                return true;
            }
            if (_pendingActions.Values.Any(p => p.Kind != AdminAction.Poll)) return false;
            SendRequest(AdminAction.Local, p => p.Write(detail ?? "local admin action"), _ => retry());
            return false;
        }

        private void RecordLocalAdminAction(string detail)
        {
            if (!HasAdminAccess || ZNet.instance == null || !AdminRules.AdminDetail(detail)) return;
            Player local = Player.m_localPlayer;
            if (local == null) return;
            if (ZNet.instance.IsServer())
            {
                AppendAdminAudit(local.GetPlayerName(), local.GetPlayerID(), detail, "OK");
                return;
            }
            if (_serverConnection == null) return;
            try
            {
                var packet = new ZPackage();
                packet.Write(AdminRules.Protocol);
                packet.Write(++_nextRequestId);
                packet.Write((int)AdminAction.Local);
                packet.Write(detail);
                _serverConnection.Invoke(RequestRpc, packet);
            }
            catch (Exception ex) { Logger.LogDebug("Could not send the local admin action audit entry: " + ex.Message); }
        }

        private bool ExecuteServerAction(AdminAction kind, ZPackage packet, Vector3 origin, long adminUid, out string message,
            out string auditDetail, out Action<ZPackage> replyExtra)
        {
            message = "Invalid action parameters";
            auditDetail = kind.ToString(); replyExtra = null;
            if (kind == AdminAction.Local)
            {
                string detail = packet.ReadString();
                if (!AdminRules.AdminDetail(detail)) return false;
                auditDetail = detail; message = ""; return true;
            }
            if (kind == AdminAction.Time)
            {
                int hour = packet.ReadInt(), minute = packet.ReadInt();
                if (!AdminRules.Clock(hour, minute) || EnvMan.instance == null) return false;
                // No debug time override: update the authoritative net clock, replicated by vanilla.
                if (ReadMember(EnvMan.instance, "m_skipTime") is bool skipping && skipping)
                { message = "Wait for the current sleep/time skip to finish"; return false; }
                double next = AdminRules.NextTime(ZNet.instance.GetTimeSeconds(), EnvMan.instance.m_dayLengthSec, hour, minute);
                ZNet.instance.SetNetTime(next); auditDetail = "time " + hour.ToString("00") + ":" + minute.ToString("00"); message = ""; return true;
            }
            if (kind == AdminAction.Spawn)
            {
                string prefab = packet.ReadString(); int count = packet.ReadInt(), level = packet.ReadInt();
                string name = packet.ReadString(); bool tamed = packet.ReadBool();
                Vector3 position = packet.ReadVector3(); float yaw = packet.ReadSingle();
                if (!AdminRules.Spawn(prefab, count, level, name) || !FinitePosition(position) || !AdminRules.Finite(yaw) ||
                    (position - origin).sqrMagnitude > 30f * 30f) return false;
                var template = ZNetScene.instance?.GetPrefab(prefab);
                if (template == null || template.GetComponent<Character>() == null || template.GetComponent<Player>() != null ||
                    template.GetComponent<ZNetView>() == null) return false;
                // Exactly one Instantiate per creature; ZNetView performs normal replication.
                for (int i = 0; i < count; i++)
                {
                    var created = UnityEngine.Object.Instantiate(template, position + GetSpawnOffset(i), Quaternion.Euler(0, yaw, 0));
                    ApplyLevel(created, level);
                    if (tamed) TryTameCreature(created);
                    if (!string.IsNullOrWhiteSpace(name)) ApplyCustomName(created, name);
                }
                auditDetail = "spawn " + prefab + " ×" + count + " level=" + level + (tamed ? " tamed" : "") +
                    (string.IsNullOrWhiteSpace(name) ? "" : " name=" + name);
                message = ""; return true;
            }
            if (kind == AdminAction.Cleanup)
            {
                bool result = ExecuteCleanup(packet, origin, out message);
                auditDetail = "kill nearby creatures";
                return result;
            }
            if (kind == AdminAction.Weather) return ExecuteWeatherAction(packet, out message, out auditDetail);
            if (kind == AdminAction.Raids) return ExecuteRaidAction(packet, out message, out auditDetail);
            if (kind == AdminAction.Locations) return WriteLocationPage(packet, out message, out replyExtra);
            if (kind == AdminAction.Teleport) return ExecuteLocationTeleport(packet, origin, adminUid, out message, out auditDetail, out replyExtra);
            if (kind == AdminAction.Return) return ExecuteReturnTeleport(adminUid, out message, out auditDetail, out replyExtra);
            if (kind == AdminAction.Audit) return WriteAuditPage(packet, out message, out replyExtra);
            if (kind == AdminAction.AddLocation) return ExecuteAddTeleportPoint(origin, packet, out message, out auditDetail);
            if (kind == AdminAction.RenameLocation) return ExecuteRenameTeleportPoint(packet, out message, out auditDetail);
            if (kind == AdminAction.DeleteLocation) return ExecuteDeleteTeleportPoint(packet, out message, out auditDetail);
            if (kind == AdminAction.PlayerRoster) return WriteAdminPlayerPage(packet, out message, out replyExtra);
            if (kind == AdminAction.TeleportToPlayer) return ExecuteTeleportToPlayer(packet, origin, adminUid, out message, out auditDetail, out replyExtra);
            if (kind == AdminAction.TeleportPlayerHere) return ExecuteTeleportPlayerHere(packet, origin, adminUid, out message, out auditDetail);
            if (kind == AdminAction.KickPlayer) return ExecuteKickPlayer(packet, adminUid, out message, out auditDetail);
            if (kind == AdminAction.BanPlayer) return ExecuteBanPlayer(packet, adminUid, out message, out auditDetail);
            if (kind == AdminAction.UnbanPlayer) return ExecuteUnbanPlayer(packet, out message, out auditDetail);
            if (kind == AdminAction.FreezePlayer) return ExecuteFreezePlayer(packet, adminUid, out message, out auditDetail);
            if (kind == AdminAction.ClearDroppedItems) return ExecuteClearDroppedItems(out message, out auditDetail);
            if (kind == AdminAction.PlayerReturn) return ExecutePlayerReturn(adminUid, out message, out auditDetail, out replyExtra);
            if (kind == AdminAction.PlayerReturnDismiss) return ExecuteDismissPlayerReturn(adminUid, out message, out auditDetail);
            return false;
        }

        private static bool FinitePosition(Vector3 position) => AdminRules.Finite(position.x) &&
            AdminRules.Finite(position.y) && AdminRules.Finite(position.z);

        private void RequestWorldAction(AdminAction kind, Action<ZPackage> write, Action<ZPackage> complete = null)
        {
            if (!HasAdminAccess) { _status = T("Administrator access required.", "Нужны права администратора сервера."); return; }
            if (!ZNet.instance.IsServer())
            {
                if (_pendingActions.Values.Any(p => p.Kind != AdminAction.Poll)) return;
                SendRequest(kind, write, complete); return;
            }
            var packet = new ZPackage(); write?.Invoke(packet); packet.SetPos(0);
            try
            {
                var local = Player.m_localPlayer;
                bool ok = ExecuteServerAction(kind, packet, local.transform.position, local.GetPlayerID(), out var message,
                    out var detail, out var extra);
                if (kind != AdminAction.Locations && kind != AdminAction.Audit)
                    AppendAdminAudit(local.GetPlayerName(), local.GetPlayerID(), detail ?? kind.ToString(), ok ? "OK" : (message ?? "rejected"));
                _status = string.IsNullOrEmpty(message) ? (ok ? T("Action completed.", "Действие выполнено.") :
                    T("Action rejected.", "Действие отклонено.")) : FormatServerMessage(message);
                if (ok && complete != null)
                {
                    var response = new ZPackage(); response.Write(_serverForcedWeather ?? string.Empty); response.Write(_serverRaidsDisabled);
                    extra?.Invoke(response); response.SetPos(0); response.ReadString(); response.ReadBool(); complete(response);
                }
            }
            catch (Exception ex) { Logger.LogError("Nexus server action failed: " + ex); _status = T("Action failed; check the log.", "Ошибка действия; см. журнал."); }
        }
    }
}
