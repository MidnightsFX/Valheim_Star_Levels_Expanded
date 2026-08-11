using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.modules.LocationReset {
    // Temporarily instantiates a zone server-side so PlaceVegetation and SpawnLocation have a live
    // Heightmap and colliders to work against, then tears it back down.
    //
    // The critical invariant is that we only ever release zones WE loaded. Releasing a zone that is
    // genuinely active because a player is standing in it destroys the zone root out from under the
    // terrain compiler, which then loses its heightmap. Upgrade World hit exactly this and guards it
    // the same way.
    internal static class ZoneLoader {

        private static readonly HashSet<Vector2i> manuallyLoaded = new HashSet<Vector2i>();

        internal static bool WasManuallyLoaded(Vector2i zone) {
            return manuallyLoaded.Contains(zone);
        }

        // Poke the zone until vanilla reports it loaded. onResult receives whether it came up in time.
        //
        // adoptIfLoaded is for the forced admin reset. Valheim keeps the 3x3 zone block around every
        // player loaded, which is exactly what SLS-loc-reset-here targets at its default radius, so
        // refusing loaded zones outright would make the command a no-op where it is used most.
        internal static IEnumerator Load(Vector2i zone, float maxWaitSeconds, bool adoptIfLoaded, System.Action<bool> onResult) {
            if (ZoneSystem.instance == null) { onResult?.Invoke(false); yield break; }

            if (ZoneSystem.instance.IsZoneLoaded(zone)) {
                // Report success when adopting, but deliberately do NOT register the zone in
                // manuallyLoaded: Release only tears down zones this class loaded, so a live zone a
                // player is standing in is worked on in place and left standing afterwards.
                onResult?.Invoke(adoptIfLoaded);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, maxWaitSeconds);
            manuallyLoaded.Add(zone);

            while (Time.realtimeSinceStartup < deadline) {
                // PokeLocalZone uses SpawnMode.Client for an already-generated zone, so this
                // instantiates the zone root and its ZDOs without re-running world generation.
                ZoneSystem.instance.PokeLocalZone(zone);
                if (ZoneSystem.instance.IsZoneLoaded(zone)) {
                    onResult?.Invoke(true);
                    yield break;
                }
                // Terrain is built on a background thread; give it frames to finish.
                yield return null;
            }

            // Timed out. Release whatever partially came up so we do not leak a zone root.
            Release(zone);
            Logger.LogLocationReset($"Zone {zone.x},{zone.y} did not finish loading within {maxWaitSeconds}s; skipping.");
            onResult?.Invoke(false);
        }

        // Tear down a zone this class loaded. No-op for zones we did not load.
        internal static void Release(Vector2i zone) {
            if (manuallyLoaded.Remove(zone) == false) { return; }
            if (ZoneSystem.instance == null || ZNetScene.instance == null) { return; }

            List<ZDO> zdos = new List<ZDO>();
            if (ZDOMan.instance != null) { ZDOMan.instance.FindObjects(zone, zdos); }

            for (int i = 0; i < zdos.Count; i++) {
                ZDO zdo = zdos[i];
                if (zdo == null) { continue; }
                if (ZNetScene.instance.m_instances.TryGetValue(zdo, out ZNetView view) == false) { continue; }
                if (view == null) { continue; }
                // Vanilla's own unload sequence (ZNetScene.RemoveObjects): detach the view from its
                // ZDO so the data survives, then destroy the GAMEOBJECT. Destroying only the
                // ZNetView component -- as Upgrade World does here -- leaves the GameObject behind
                // as an orphan, which accumulates over a long-running sweep.
                view.ResetZDO();
                Object.Destroy(view.gameObject);
                ZNetScene.instance.m_instances.Remove(zdo);
            }

            if (ZoneSystem.instance.m_zones.TryGetValue(zone, out ZoneSystem.ZoneData data)) {
                if (data != null && data.m_root != null) { Object.Destroy(data.m_root); }
                ZoneSystem.instance.m_zones.Remove(zone);
            }
        }

        // Keep a zone we loaded from being reaped mid-operation. ZoneSystem destroys a poked zone root
        // once its TTL passes m_zoneTTL (4s) with no instances in the sector, which a multi-chunk
        // terrain reset can easily run past. PokeLocalZone resets that TTL to zero.
        internal static void KeepAlive(Vector2i zone) {
            if (ZoneSystem.instance == null) { return; }
            if (manuallyLoaded.Contains(zone) == false) { return; }
            ZoneSystem.instance.PokeLocalZone(zone);
        }

        // Bring a chunk's terrain objects to life so the terrain APIs can actually see them.
        //
        // ZNetScene only instantiates ZDOs near ZNet's reference position, and on a dedicated server
        // that is Vector3.zero forever -- nothing but local-player code ever moves it. So a chunk we
        // poke-loaded 3000m away has a live Heightmap but NO live _TerrainCompiler and no live
        // TerrainModifiers, and TerrainComp.FindTerrainCompiler / TerrainModifier.GetAllInstances --
        // both plain scans of static lists populated from Awake -- silently find nothing. That is why
        // terrain resets appeared to work only when a player happened to be standing nearby.
        //
        // The caller MUST finish its terrain work and call DestroyTerrainObjects WITHOUT yielding in
        // between: ZNetScene.RemoveObjects runs at 30Hz and would reap these as out-of-range.
        internal static List<ZNetView> CreateTerrainObjects(List<Vector2i> zones) {
            List<ZNetView> created = new List<ZNetView>();
            if (ZNetScene.instance == null || ZDOMan.instance == null || zones == null) { return created; }

            ZoneProtectionScan.BuildPrefabSets();
            List<ZDO> zdos = new List<ZDO>();

            for (int z = 0; z < zones.Count; z++) {
                zdos.Clear();
                ZDOMan.instance.FindObjects(zones[z], zdos);
                for (int i = 0; i < zdos.Count; i++) {
                    ZDO zdo = zdos[i];
                    if (zdo == null || zdo.IsValid() == false) { continue; }
                    if (zdo.m_prefab != ZoneProtectionScan.TerrainCompilerHash
                        && ZoneProtectionScan.TerrainModifierHashes.Contains(zdo.m_prefab) == false) { continue; }
                    // Already live because a player is nearby. Leave it alone and, critically, do not
                    // adopt it for teardown -- we only ever destroy what we created.
                    if (ZNetScene.instance.m_instances.ContainsKey(zdo)) { continue; }

                    GameObject go = ZNetScene.instance.CreateObject(zdo);
                    if (go == null) { continue; }
                    ZNetView view = go.GetComponent<ZNetView>();
                    if (view == null) { Object.Destroy(go); continue; }
                    created.Add(view);
                }
            }
            return created;
        }

        // Tear down what CreateTerrainObjects made, using vanilla's own unload sequence so the ZDOs
        // (including the terrain data TerrainComp.Save just wrote) survive.
        internal static void DestroyTerrainObjects(List<ZNetView> created) {
            if (created == null) { return; }
            for (int i = 0; i < created.Count; i++) {
                ZNetView view = created[i];
                if (view == null) { continue; }
                ZDO zdo = view.GetZDO();
                view.ResetZDO();
                Object.Destroy(view.gameObject);
                if (zdo != null && ZNetScene.instance != null) { ZNetScene.instance.m_instances.Remove(zdo); }
            }
            created.Clear();
        }

        // World teardown: forget everything without touching the scene, which is already going away.
        internal static void Clear() {
            manuallyLoaded.Clear();
        }
    }
}
