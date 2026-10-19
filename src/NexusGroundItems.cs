using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private bool ExecuteClearDroppedItems(out string message, out string detail)
        {
            message = "Ground item cleanup is available only on the server";
            detail = "clear all dropped ground items";
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return false;
            var manager = ZDOMan.instance;
            var scene = ZNetScene.instance;
            if (manager == null || scene == null)
            { message = "The server world object list is unavailable"; return false; }
            var objectsField = AccessTools.Field(typeof(ZDOMan), "m_objectsByID");
            var prefabsField = AccessTools.Field(typeof(ZNetScene), "m_prefabs");
            var objects = objectsField?.GetValue(manager) as Dictionary<ZDOID, ZDO>;
            var prefabs = prefabsField?.GetValue(scene) as List<GameObject>;
            if (objects == null || prefabs == null)
            { message = "The server world object list is unavailable"; return false; }

            var itemPrefabHashes = new HashSet<int>();
            foreach (var prefab in prefabs)
            {
                if (prefab == null || prefab.GetComponent<ItemDrop>() == null || prefab.GetComponent<ZNetView>() == null ||
                    prefab.GetComponent<Character>() != null || prefab.GetComponent<Container>() != null || prefab.GetComponent<Piece>() != null) continue;
                int prefabHash = scene.GetPrefabHash(prefab);
                if (prefabHash != 0) itemPrefabHashes.Add(prefabHash);
            }

            int queued = 0, failed = 0;
            long serverSession = ZDOMan.GetSessionID();
            foreach (var zdo in objects.Values.ToArray())
            {
                if (zdo == null || !itemPrefabHashes.Contains(zdo.GetPrefab())) continue;
                try
                {
                    // DestroyZDO silently ignores objects owned by another peer. Claim them
                    // for the authoritative server first so the deletion is replicated.
                    if (!zdo.IsOwner()) zdo.SetOwner(serverSession);
                    if (!zdo.IsOwner()) { failed++; continue; }
                    manager.DestroyZDO(zdo);
                    queued++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.LogDebug("Could not remove a dropped ground item ZDO: " + ex.Message);
                }
            }
            detail = "queue dropped ground item removal count=" + queued + (failed > 0 ? " failed=" + failed : string.Empty);
            message = "ground-items|" + queued.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                failed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Logger.LogInfo("Nexus ground item cleanup: queued=" + queued + ", failed=" + failed + ".");
            return true;
        }
    }
}
