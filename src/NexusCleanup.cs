using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private const string CleanupOwnerRpc = "SN_Nexus_CleanupOwner185";

        private bool ExecuteCleanup(ZPackage packet, Vector3 origin, out string message)
        {
            message = "Invalid cleanup parameters";
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !FinitePosition(origin)) return false;
            float radius = packet.ReadSingle(); bool tame = packet.ReadBool(), bosses = packet.ReadBool();
            int count = packet.ReadInt();
            if (!AdminRules.Finite(radius) || radius < 5 || radius > 100 || count < 0 || count > 200) return false;
            var candidates = new HashSet<ZDOID>();
            for (int i = 0; i < count; i++) candidates.Add(packet.ReadZDOID());
            // A dedicated server need not instantiate the creatures around a client.
            // IDs are hints only. The server checks its own ZDO state and prefab registry.
            foreach (var character in Character.GetAllCharacters())
            {
                if (character == null || (character.transform.position - origin).sqrMagnitude > radius * radius) continue;
                var view = character.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && candidates.Count < 200) candidates.Add(view.GetZDO().m_uid);
            }
            var owners = new Dictionary<long, List<ZDOID>>();
            int skipped = 0;
            foreach (var id in candidates)
            {
                var zdo = ZDOMan.instance?.GetZDO(id);
                if (!CanCleanup(zdo, origin, radius, tame, bosses)) { skipped++; continue; }
                long owner = zdo.GetOwner();
                if (owner == 0) { skipped++; continue; }
                if (!owners.TryGetValue(owner, out var ids)) owners[owner] = ids = new List<ZDOID>();
                ids.Add(id);
            }
            int sent = 0;
            foreach (var owner in owners)
            {
                if (owner.Key == ZNet.GetUID())
                {
                    sent += ApplyOwnerCleanup(owner.Value, origin, radius, tame, bosses);
                    continue;
                }
                var peer = ZNet.instance.GetPeers().Find(p => p.m_uid == owner.Key && p.IsReady() &&
                    p.m_socket != null && p.m_socket.IsConnected());
                if (peer == null) { skipped += owner.Value.Count; continue; }
                var order = new ZPackage(); order.Write(AdminRules.Protocol); order.Write(origin); order.Write(radius);
                order.Write(tame); order.Write(bosses); order.Write(owner.Value.Count);
                foreach (var id in owner.Value) order.Write(id);
                peer.m_rpc.Invoke(CleanupOwnerRpc, order);
                sent += owner.Value.Count;
            }
            Logger.LogInfo("Nexus cleanup: candidates=" + candidates.Count + ", dispatched=" + sent + ", excluded=" + skipped);
            message = "cleanup|" + sent + "|" + skipped;
            return true;
        }

        private static bool CanCleanup(ZDO zdo, Vector3 origin, float radius, bool tame, bool bosses)
        {
            if (zdo == null || ZNetScene.instance == null) return false;
            var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            var character = prefab == null ? null : prefab.GetComponent<Character>();
            if (character == null || prefab.GetComponent<Player>() != null || zdo.GetFloat(ZDOVars.s_health, 1) <= 0) return false;
            return NexusInteractionRules.CleanupTarget(character.IsPlayer(), character.IsBoss(),
                zdo.GetBool(ZDOVars.s_tamed, false), bosses, tame, (zdo.GetPosition() - origin).sqrMagnitude, radius);
        }

        private void ReceiveOwnerCleanup(ZRpc rpc, ZPackage packet)
        {
            // Only a direct command from our authenticated game server may change an
            // owned creature. Ordinary clients and routed sender IDs confer no authority.
            var server = ZNet.instance?.GetServerPeer();
            if (ZNet.instance == null || ZNet.instance.IsServer() || server == null ||
                !ReferenceEquals(server.m_rpc, rpc) || !server.IsReady() || packet == null || packet.Size() > AdminRules.MaxPacketBytes) return;
            try
            {
                if (packet.ReadInt() != AdminRules.Protocol) return;
                Vector3 origin = packet.ReadVector3(); float radius = packet.ReadSingle();
                bool tame = packet.ReadBool(), bosses = packet.ReadBool(); int count = packet.ReadInt();
                if (!FinitePosition(origin) || !AdminRules.Finite(radius) || radius < 5 || radius > 100 || count < 0 || count > 200) return;
                var ids = new HashSet<ZDOID>();
                for (int i = 0; i < count; i++) ids.Add(packet.ReadZDOID());
                int applied = ApplyOwnerCleanup(ids, origin, radius, tame, bosses);
                Logger.LogInfo("Nexus cleanup from server: applied=" + applied + ", requested=" + count);
            }
            catch (Exception ex) { Logger.LogWarning("Rejected Nexus cleanup order: " + ex.Message); }
        }

        private int ApplyOwnerCleanup(IEnumerable<ZDOID> ids, Vector3 origin, float radius, bool tame, bool bosses)
        {
            int applied = 0;
            foreach (var id in ids)
            {
                var zdo = ZDOMan.instance?.GetZDO(id);
                if (!CanCleanup(zdo, origin, radius, tame, bosses)) continue;
                var instance = ZNetScene.instance?.FindInstance(id);
                var view = instance == null ? null : instance.GetComponent<ZNetView>();
                var target = instance == null ? null : instance.GetComponent<Character>();
                if (view == null || !view.IsValid() || !view.IsOwner() || target == null || target.IsPlayer() || target.IsDead() ||
                    !NexusInteractionRules.CleanupTarget(target.IsPlayer(), target.IsBoss(), target.IsTamed(), bosses, tame,
                        (target.transform.position - origin).sqrMagnitude, radius)) continue;
                // Apply on the current owner. Vanilla CheckDeath performs the normal death, loot and replication; no object destruction or ownership stealing.
                target.SetHealth(0f);
                applied++;
            }
            return applied;
        }

        private string FormatServerMessage(string message)
        {
            if (message.StartsWith("ground-items|", StringComparison.Ordinal))
            {
                string[] drops = message.Split('|');
                if (drops.Length == 3 && int.TryParse(drops[1], out int queued) && int.TryParse(drops[2], out int failed))
                    return T("Removal queued for dropped items: ", "Поставлено в очередь удаление выброшенных предметов: ") + queued +
                        (failed > 0 ? T(". Failed: ", ". Не удалось удалить: ") + failed : string.Empty);
            }
            if (message == "locations-generating") return T("World locations are still generating. Try refreshing in a moment.",
                "Локации мира ещё генерируются. Попробуйте обновить список чуть позже.");
            if (message == "busy") return T("Too many commands; try again in a moment.", "Слишком частые команды; повторите через мгновение.");
            if (!message.StartsWith("cleanup|", StringComparison.Ordinal)) return message;
            string[] values = message.Split('|');
            if (values.Length == 3 && values[1] == "0") return T("No eligible creatures found nearby. Excluded: ",
                "Подходящие существа рядом не найдены. Исключено: ") + values[2];
            return values.Length == 3 ? T("Creature cleanup sent: ", "Команда убийства отправлена: ") + values[1] +
                T(". Excluded: ", ". Исключено: ") + values[2] : message;
        }
    }
}
