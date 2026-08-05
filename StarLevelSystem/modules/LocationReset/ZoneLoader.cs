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
        internal static IEnumerator Load(Vector2i zone, float maxWaitSeconds, System.Action<bool> onResult) {
            if (ZoneSystem.instance == null) { onResult?.Invoke(false); yield break; }

            if (ZoneSystem.instance.IsZoneLoaded(zone)) {
                // Already live for some other reason; do not adopt it, and do not release it later.
                onResult?.Invoke(false);
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

        // World teardown: forget everything without touching the scene, which is already going away.
        internal static void Clear() {
            manuallyLoaded.Clear();
        }
    }
}
