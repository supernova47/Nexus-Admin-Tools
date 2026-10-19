// Runs the production cleanup handler against deterministic game/transport fakes.
// This verifies protocol behavior; it is not a running Unity/multiplayer session.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SuperNova.ValheimSpawner;

internal static class CleanupTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static ZPackage Request(params ZDOID[] ids)
    {
        var packet = new ZPackage(); packet.Write(30f); packet.Write(false); packet.Write(false); packet.Write(ids.Length);
        foreach (var id in ids) packet.Write(id);
        return packet;
    }
    private static ZDO Add(int id, bool player = false, bool boss = false, bool tamed = false, float distance = 4, long owner = 22)
    {
        var prefab = new GameObject();
        var character = player ? (Character)new Player() : new Character();
        character.player = player; character.boss = boss; prefab.Add(character);
        ZNetScene.instance.prefabs[id] = prefab;
        var zdo = new ZDO { m_uid = new ZDOID(id), prefab = id, tamed = tamed, position = new Vector3(distance, 0, 0), owner = owner };
        ZDOMan.instance.objects[zdo.m_uid] = zdo;
        return zdo;
    }
    private static Character Instantiate(ZDO zdo, bool owned)
    {
        var prefab = ZNetScene.instance.prefabs[zdo.prefab].GetComponent<Character>();
        var character = prefab.player ? (Character)new Player() : new Character();
        character.player = prefab.player; character.boss = prefab.boss; character.tamed = zdo.tamed;
        character.transform.position = zdo.position;
        var go = new GameObject(); go.Add(character); go.Add(new ZNetView { zdo = zdo, owned = owned });
        ZNetScene.instance.instances[zdo.m_uid] = go;
        return character;
    }
    private static void Main()
    {
        var mod = new ValheimSpawnerPlugin();
        ZNet.instance = new ZNet { server = true }; ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        var owner = new ZNetPeer { m_uid = 22 }; ZNet.instance.peers.Add(owner);
        var npc = Add(1); var player = Add(2, player: true); var boss = Add(3, boss: true);
        var tame = Add(4, tamed: true); var far = Add(5, distance: 31); var unknown = new ZDOID(999);
        Check(mod.Execute(Request(npc.m_uid, npc.m_uid, player.m_uid, boss.m_uid, tame.m_uid, far.m_uid, unknown), out var message), "Dedicated cleanup accepted");
        Check(Character.characters.Count == 0, "Dedicated server has no loaded characters");
        Check(owner.m_rpc.sent.Count == 1, "Dispatch works without server-side scene instances");
        Check(message == "cleanup|1|5", "Duplicates removed and protected/unknown/out-of-range IDs excluded");
        var command = owner.m_rpc.sent[0];
        ZNet.instance.server = false; ZNet.instance.serverPeer = new ZNetPeer();
        var loaded = Instantiate(npc, true);
        mod.Receive(new ZRpc(), command.Copy()); Check(loaded.health == 100, "An ordinary client cannot send owner cleanup");
        mod.Receive(ZNet.instance.serverPeer.m_rpc, command.Copy()); Check(loaded.health == 0, "Trusted server dispatch reaches owner");
        Check(loaded.healthWrites == 1, "Duplicate target is applied once");
        loaded.health = 100; loaded.healthWrites = 0;
        loaded.gameObject.GetComponent<ZNetView>().owned = false;
        mod.Receive(ZNet.instance.serverPeer.m_rpc, command.Copy()); Check(loaded.health == 100, "Ownership migration does not steal ownership or write health");
        loaded.gameObject.GetComponent<ZNetView>().owned = true; npc.position = new Vector3(200, 0, 0);
        mod.Receive(ZNet.instance.serverPeer.m_rpc, command.Copy()); Check(loaded.health == 100, "Owner rechecks current authoritative distance");
        npc.position = new Vector3(4, 0, 0); loaded.tamed = true;
        mod.Receive(ZNet.instance.serverPeer.m_rpc, command.Copy()); Check(loaded.health == 100, "Owner rechecks newly tamed targets");
        loaded.tamed = false;
        var truncated = command.Copy(); truncated.values.RemoveAt(truncated.values.Count - 1);
        mod.Receive(ZNet.instance.serverPeer.m_rpc, truncated); Check(loaded.health == 100, "Truncated order has no partial side effects");
        var badProtocol = command.Copy(); badProtocol.values[0] = AdminRules.Protocol - 1;
        mod.Receive(ZNet.instance.serverPeer.m_rpc, badProtocol); Check(loaded.health == 100, "Incompatible protocol rejected");
        ZNet.instance.server = true;
        mod.Receive(ZNet.instance.serverPeer.m_rpc, command.Copy()); Check(loaded.health == 100, "Server refuses client-supplied owner commands");
        npc.owner = ZNet.GetUID(); var serverTarget = Instantiate(npc, true); Character.characters.Add(serverTarget);
        Check(mod.Execute(Request(), out message) && serverTarget.health == 0, "Host/server-owned creatures use the same owner path");
        var oversized = Request(); oversized.values[3] = 201;
        Check(!mod.Execute(oversized, out _), "Target count is bounded on server");
        foreach (var delta in new[] { .01f, .1f, 1f, -1f, -.1f })
            Check(NexusInteractionRules.WheelDelta(delta) == Math.Sign(delta), "Fractional wheel notch normalized without duplicate event");
        Check(NexusInteractionRules.WheelDelta(float.NaN) == 0 && NexusInteractionRules.WheelDelta(0) == 0, "Invalid/idle wheel ignored");
        for (int mask = 0; mask < 32; mask++)
        {
            bool p = (mask & 1) != 0, b = (mask & 2) != 0, t = (mask & 4) != 0, ib = (mask & 8) != 0, it = (mask & 16) != 0;
            Check(NexusInteractionRules.CleanupTarget(p, b, t, ib, it, 25, 5) == (!p && (!b || ib) && (!t || it)), "All player/boss/tamed policy combinations");
        }
        foreach (double d in new[] { -1, double.NaN, double.PositiveInfinity, 10001 })
            Check(!NexusInteractionRules.CleanupTarget(false, false, false, true, true, d, 100), "Invalid/outside distance rejected");
        Console.WriteLine("PASS " + checks + " cleanup routing, ownership, protected targets, malformed transport and wheel checks");
    }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z; public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z);
    }
    public class Transform { public Vector3 position; }
    public class Component
    {
        public GameObject gameObject; public Transform transform = new Transform();
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
    public class GameObject
    {
        private readonly List<Component> components = new List<Component>();
        public void Add(Component c) { c.gameObject = this; components.Add(c); }
        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();
    }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public int id; public ZDOID(int n) { id = n; }
    public bool Equals(ZDOID other) => id == other.id;
    public override bool Equals(object o) => o is ZDOID z && Equals(z);
    public override int GetHashCode() => id;
}
public static class ZDOVars { public const int s_health = 1, s_tamed = 2; }
public class ZDO
{
    public ZDOID m_uid; public int prefab; public bool tamed; public Vector3 position; public long owner;
    public float GetFloat(int key, float fallback) => 100;
    public bool GetBool(int key, bool fallback) => tamed;
    public Vector3 GetPosition() => position; public int GetPrefab() => prefab; public long GetOwner() => owner;
}
public class ZDOMan { public static ZDOMan instance; public Dictionary<ZDOID, ZDO> objects = new Dictionary<ZDOID, ZDO>(); public ZDO GetZDO(ZDOID id) => objects.TryGetValue(id, out var value) ? value : null; }
public class ZNetScene
{
    public static ZNetScene instance; public Dictionary<int, GameObject> prefabs = new Dictionary<int, GameObject>();
    public Dictionary<ZDOID, GameObject> instances = new Dictionary<ZDOID, GameObject>();
    public GameObject GetPrefab(int id) => prefabs.TryGetValue(id, out var p) ? p : null;
    public GameObject FindInstance(ZDOID id) => instances.TryGetValue(id, out var p) ? p : null;
}
public class Character : Component
{
    public static List<Character> characters = new List<Character>(); public bool player, boss, tamed, dead;
    public float health = 100; public int healthWrites;
    public static List<Character> GetAllCharacters() => characters;
    public bool IsPlayer() => player; public bool IsBoss() => boss; public bool IsTamed() => tamed; public bool IsDead() => dead;
    public void SetHealth(float value) { health = value; healthWrites++; }
}
public class Player : Character { }
public class ZNetView : Component { public ZDO zdo; public bool owned; public bool IsValid() => zdo != null; public bool IsOwner() => owned; public ZDO GetZDO() => zdo; }
public class ZPackage
{
    public List<object> values = new List<object>(); private int position;
    public void Write(object value) => values.Add(value); public int Size() => values.Count * 24;
    public ZPackage Copy() => new ZPackage { values = values.ToList() };
    public int ReadInt() => (int)values[position++]; public bool ReadBool() => (bool)values[position++];
    public float ReadSingle() => (float)values[position++]; public Vector3 ReadVector3() => (Vector3)values[position++]; public ZDOID ReadZDOID() => (ZDOID)values[position++];
}
public class ZRpc { public List<ZPackage> sent = new List<ZPackage>(); public void Invoke(string name, ZPackage packet) => sent.Add(packet.Copy()); }
public class SocketFake { public bool IsConnected() => true; }
public class ZNetPeer { public long m_uid; public ZRpc m_rpc = new ZRpc(); public SocketFake m_socket = new SocketFake(); public bool IsReady() => true; }
public class ZNet
{
    public static ZNet instance; public bool server; public ZNetPeer serverPeer; public List<ZNetPeer> peers = new List<ZNetPeer>();
    public bool IsServer() => server; public List<ZNetPeer> GetPeers() => peers; public ZNetPeer GetServerPeer() => serverPeer; public static long GetUID() => 11;
}
namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        public bool Execute(ZPackage packet, out string message) => ExecuteCleanup(packet, new Vector3(), out message);
        public void Receive(ZRpc rpc, ZPackage packet) => ReceiveOwnerCleanup(rpc, packet);
        private static bool FinitePosition(Vector3 p) => AdminRules.Finite(p.x) && AdminRules.Finite(p.y) && AdminRules.Finite(p.z);
        private string T(string en, string ru) => ru;
        private readonly LoggerFake Logger = new LoggerFake();
    }
    public class LoggerFake { public void LogInfo(string message) { } public void LogWarning(string message) { } }
}
