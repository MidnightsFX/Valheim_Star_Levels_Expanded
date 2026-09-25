using StarLevelSystem.common;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.modules {

    // Deletes networked objects (creatures, and whatever else a raid spawned) without this peer taking ownership of
    // them. Deleting takes the owner: ZNetScene.Destroy only deletes the ZDO on the owner, and ZDOMan.DestroyZDO checks
    // the same, so SLS used to claim an object right before deleting it - a client-side ownership change of something
    // another player may be simulating, or one the server had just released.
    //
    // A peer that owns the object deletes it at once. Anything else goes to the server, the ownership authority,
    // which knows the current owner: it forwards the request to that owner, or deletes the object itself when nobody
    // owns it (taking it first, the way ZDOMan.ReleaseNearbyZDOS hands out owners). Routed through the server because
    // a Jotunn RPC only reaches directly connected peers, and a client's only one is the server.
    internal static class OwnerRoutedDestroy {

        // Deletes the object now if this peer owns it, otherwise asks its owner to.
        internal static void Request(ZNetView nview) {
            if (nview == null || nview.IsValid() == false) { return; }
            Request(new List<ZDOID>() { nview.GetZDO().m_uid });
        }

        internal static void Request(IEnumerable<ZDOID> ids) {
            if (ZDOMan.instance == null || ZNet.instance == null || ids == null) { return; }
            List<ZDOID> notOwned = null;
            foreach (ZDOID id in ids) {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                // Unknown here means already deleted, or never in this peer's area; neither is ours to chase.
                if (zdo == null) { continue; }
                if (zdo.IsOwner()) {
                    DestroyOwned(zdo);
                    continue;
                }
                if (notOwned == null) { notOwned = new List<ZDOID>(); }
                notOwned.Add(id);
            }
            if (notOwned == null) { return; }

            if (ZNet.instance.IsServer()) {
                RouteFromServer(notOwned);
                return;
            }
            ZNetPeer server = ZNet.instance.GetServerPeer();
            if (server == null) { return; }
            ValConfig.DestroyViaOwnerRPC.SendPackage(server.m_uid, Pack(notOwned));
        }

        // A client asked the server to delete objects it does not own.
        internal static IEnumerator OnServerReceive(long sender, ZPackage package) {
            RouteFromServer(Unpack(package));
            yield break;
        }

        // The server forwarded a request to this peer as the owner. If ownership moved while it was in flight this peer
        // is no longer the one to do it, and the request is dropped rather than bounced back: the new owner keeps the
        // object, which for a raid straggler means vanilla's event-creature despawn takes it once the raid is over.
        internal static IEnumerator OnClientReceive(long sender, ZPackage package) {
            if (ZDOMan.instance == null) { yield break; }
            foreach (ZDOID id in Unpack(package)) {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo != null && zdo.IsOwner()) { DestroyOwned(zdo); }
            }
        }

        private static void RouteFromServer(List<ZDOID> ids) {
            if (ZDOMan.instance == null || ZNet.instance == null) { return; }
            Dictionary<long, List<ZDOID>> byOwner = null;
            foreach (ZDOID id in ids) {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null) { continue; }
                // This is reachable by any client, so never delete a player's character through it.
                if (IsPlayer(zdo)) { continue; }
                if (zdo.IsOwner()) {
                    DestroyOwned(zdo);
                    continue;
                }
                long owner = zdo.GetOwner();
                // Unowned, or still stamped with a peer that has left: nobody simulates it, so the server takes it
                // and deletes it in the same call. There is no owner to fight and nothing left to hand back.
                if (zdo.HasOwner() == false || ZNet.instance.GetPeer(owner) == null) {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    DestroyOwned(zdo);
                    continue;
                }
                if (byOwner == null) { byOwner = new Dictionary<long, List<ZDOID>>(); }
                if (byOwner.TryGetValue(owner, out List<ZDOID> owned) == false) {
                    owned = new List<ZDOID>();
                    byOwner.Add(owner, owned);
                }
                owned.Add(id);
            }
            if (byOwner == null) { return; }
            foreach (KeyValuePair<long, List<ZDOID>> request in byOwner) {
                ValConfig.DestroyViaOwnerRPC.SendPackage(request.Key, Pack(request.Value));
            }
        }

        // The caller owns the ZDO. Through ZNetScene when this peer has an instance, so it goes the way every other
        // SLS deletion does; straight through ZDOMan otherwise (a dedicated server usually holds no instance).
        private static void DestroyOwned(ZDO zdo) {
            ZNetView nview = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
            if (nview != null) {
                ZNetScene.instance.Destroy(nview.gameObject);
                return;
            }
            ZDOMan.instance.DestroyZDO(zdo);
        }

        private static bool IsPlayer(ZDO zdo) {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            return prefab != null && prefab.GetComponent<Player>() != null;
        }

        private static ZPackage Pack(List<ZDOID> ids) {
            ZPackage package = new ZPackage();
            package.Write(ids.Count);
            foreach (ZDOID id in ids) { package.Write(id); }
            return package;
        }

        private static List<ZDOID> Unpack(ZPackage package) {
            int count = package.ReadInt();
            List<ZDOID> ids = new List<ZDOID>(count);
            for (int i = 0; i < count; i++) { ids.Add(package.ReadZDOID()); }
            return ids;
        }
    }
}
