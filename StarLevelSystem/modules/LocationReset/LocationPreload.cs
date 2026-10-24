using StarLevelSystem.common;
using StarLevelSystem.Data;
using SoftReferenceableAssets;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Loads a location's prefab, and every room its dungeon can place, before a reset rebuilds it,
    // and holds them in memory until the rebuild is over.
    //
    // The rebuild is synchronous from the clear to the respawn, and two things inside it need those
    // assets already loaded:
    //  - The LocationProxy's spawn. LocationProxy.SpawnLocation defers to a later frame while
    //    ShouldDelayProxyLocationSpawning is true, and a deferred spawn misses the location's terrain
    //    shaping, so its contents are snapped to unshaped ground.
    //  - DungeonGenerator.Generate. It Load()s every room its themes allow synchronously, which for a
    //    large dungeon is a long server stall when those rooms are not in memory yet.
    //
    // Vanilla's own preload, PokeCanSpawnLocation with isFirstSpawn, covers both, and the reset used
    // to wait on it. But vanilla keeps one readiness record per location prefab, and that record
    // counts a room only once it loads successfully: one room that fails, or never answers, and the
    // record is never ready. The proxy reads the same record, so its spawn was deferred as well, and
    // nothing said which room it was waiting on. So the proxy's gate is polled here in its
    // prefab-only form -- exactly the question the proxy itself asks -- and the rooms are loaded
    // here, each with its own result.
    //
    // A location whose prefab or rooms did not all load is not rebuilt; RegenerateLocationWith
    // checks Ready right before the clear and logs each room that held it back.
    internal sealed class LocationPreload {

        private sealed class RoomLoad {
            internal SoftReference<GameObject> Prefab;
            internal string Name;
            // The dungeon generator that would place it, for the log line.
            internal string Generator;
            // Whether LoadAsync was called, and so whether there is a reference to release.
            internal bool Requested;
            internal bool Answered;
            internal LoadResult Result;
        }

        private readonly ZoneSystem.ZoneLocation location;
        private readonly float maxWaitSeconds;
        private readonly List<RoomLoad> rooms = new List<RoomLoad>();

        private bool prefabRequested;
        private bool prefabAnswered;
        private LoadResult prefabResult;
        // Rooms are only discoverable from the loaded prefab, so they are requested once it is in.
        private bool roomsRequested;
        // The prefab answered Succeeded but there was no asset to look into.
        private bool prefabEmpty;
        // ShouldDelayProxyLocationSpawning, as it read on the last poll.
        private bool proxyGateOpen;

        private LocationPreload(ZoneSystem.ZoneLocation location, float maxWaitSeconds) {
            this.location = location;
            this.maxWaitSeconds = maxWaitSeconds;
        }

        internal bool Ready {
            get {
                if (PrefabLoaded == false || roomsRequested == false || proxyGateOpen == false) { return false; }
                for (int i = 0; i < rooms.Count; i++) {
                    if (rooms[i].Answered == false || rooms[i].Result != LoadResult.Succeeded) { return false; }
                }
                return true;
            }
        }

        private bool PrefabLoaded {
            get { return prefabAnswered && prefabResult == LoadResult.Succeeded && prefabEmpty == false; }
        }

        // Nothing left worth waiting for: either everything is in, or something has already failed
        // and every room has answered, so the log can name all of them at once.
        private bool Settled {
            get {
                if (Ready) { return true; }
                if (prefabAnswered && PrefabLoaded == false) { return true; }
                if (roomsRequested == false) { return false; }
                bool anyFailed = false;
                for (int i = 0; i < rooms.Count; i++) {
                    if (rooms[i].Answered == false) { return false; }
                    if (rooms[i].Result != LoadResult.Succeeded) { anyFailed = true; }
                }
                return anyFailed;
            }
        }

        // Loads what the chunk's governing location needs for a rebuild. onResult receives the preload
        // as soon as it holds any reference, and the caller must Release it once the rebuild is over;
        // it receives nothing when no rebuild of this chunk's location can happen this pass.
        //
        // keepAlive is every chunk the caller loaded, all of which must survive the wait: a poked zone
        // with no live instances is reaped after 4s, and this can run for the full maxWaitSeconds.
        internal static IEnumerator Prepare(Vector2s zone, LocationResetConfigSnapshot cfg, List<Vector2s> keepAlive,
                                            float maxWaitSeconds, System.Action<LocationPreload> onResult) {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) { yield break; }
            if (ResetTargets.GoverningLocation(zone, cfg, out ZoneSystem.LocationInstance instance,
                                               out LocationResetData.ResolvedResetEntry entry) == false) { yield break; }
            // Terrain-only never spawns the location, so it needs none of this.
            if (entry.Mode == LocationResetMode.TerrainOnly) { yield break; }

            LocationPreload preload = new LocationPreload(instance.m_location, Mathf.Max(1f, maxWaitSeconds));
            onResult?.Invoke(preload);
            preload.RequestPrefab();

            float deadline = Time.realtimeSinceStartup + preload.maxWaitSeconds;
            while (true) {
                // Polling is also what keeps vanilla's record from expiring and releasing the prefab.
                preload.proxyGateOpen = zs.PokeCanSpawnLocation(preload.location, false);
                if (preload.PrefabLoaded && preload.roomsRequested == false) { preload.RequestRooms(); }
                if (preload.Settled || Time.realtimeSinceStartup >= deadline) { break; }
                for (int i = 0; i < keepAlive.Count; i++) { ZoneLoader.KeepAlive(keepAlive[i]); }
                yield return null;
            }
        }

        private void RequestPrefab() {
            if (location.m_prefab.IsValid == false) {
                prefabAnswered = true;
                prefabResult = LoadResult.Failed;
                return;
            }
            prefabRequested = true;
            location.m_prefab.LoadAsync(new LoadedHandler((AssetID id, LoadResult result) => {
                prefabResult = result;
                prefabAnswered = true;
            }));
        }

        // Every room any of the location's dungeon generators could place, found the way vanilla's own
        // preload finds them. Vanilla handles only the first generator; a location with several gets
        // all of theirs, once each.
        private void RequestRooms() {
            roomsRequested = true;
            GameObject asset = location.m_prefab.Asset;
            if (asset == null) { prefabEmpty = true; return; }

            HashSet<AssetID> seen = new HashSet<AssetID>();
            DungeonGenerator[] generators = Utils.GetEnabledComponentsInChildren<DungeonGenerator>(asset);
            for (int g = 0; g < generators.Length; g++) {
                SoftReference<GameObject>[] available = generators[g].GetAvailableRoomPrefabs();
                for (int i = 0; i < available.Length; i++) {
                    if (seen.Add(available[i].m_assetID) == false) { continue; }
                    RoomLoad room = new RoomLoad() { Prefab = available[i], Name = SafeName(available[i]), Generator = generators[g].name };
                    rooms.Add(room);
                    if (room.Prefab.IsValid == false) {
                        room.Answered = true;
                        room.Result = LoadResult.Failed;
                        continue;
                    }
                    room.Requested = true;
                    room.Prefab.LoadAsync(new LoadedHandler((AssetID id, LoadResult result) => {
                        room.Result = result;
                        room.Answered = true;
                    }));
                }
            }
        }

        // Drops every reference this preload took. Safe to call more than once. A load still in flight
        // may answer afterwards; its callback only writes to this object.
        internal void Release() {
            if (prefabRequested) {
                location.m_prefab.Release();
                prefabRequested = false;
            }
            for (int i = 0; i < rooms.Count; i++) {
                if (rooms[i].Requested == false) { continue; }
                rooms[i].Prefab.Release();
                rooms[i].Requested = false;
            }
        }

        // Why the location cannot be rebuilt, for the chunk record. Null when it can.
        internal string Problem {
            get {
                if (Ready) { return null; }
                if (prefabAnswered == false) { return $"location prefab did not finish loading within {maxWaitSeconds:0}s"; }
                if (prefabEmpty) { return "location prefab loaded with no asset"; }
                if (prefabResult != LoadResult.Succeeded) { return $"location prefab failed to load ({prefabResult})"; }

                List<string> missing = new List<string>();
                for (int i = 0; i < rooms.Count; i++) {
                    if (rooms[i].Answered == false || rooms[i].Result != LoadResult.Succeeded) { missing.Add($"'{rooms[i].Name}'"); }
                }
                if (missing.Count == 1) { return $"room {missing[0]} did not load"; }
                if (missing.Count > 1) {
                    const int shown = 3;
                    string more = missing.Count > shown ? $" and {missing.Count - shown} more" : "";
                    return $"{missing.Count} rooms did not load: {string.Join(", ", missing.GetRange(0, Mathf.Min(shown, missing.Count)))}{more}";
                }
                return $"its spawn gate stayed closed for {maxWaitSeconds:0}s";
            }
        }

        // One line per room that held the rebuild back, as errors: each is a fault in whichever mod
        // supplies that room, and every rebuild of this location will be skipped until it is fixed.
        internal void LogProblems(Vector2s zone, string locationName) {
            string where = $"Zone {zone.x},{zone.y}";
            if (prefabAnswered == false) {
                Logger.LogLocationResetWarning($"{where}: location prefab '{locationName}' did not finish loading within " +
                    $"{maxWaitSeconds:0}s, so it was not rebuilt.");
                return;
            }
            if (PrefabLoaded == false) {
                Logger.LogLocationResetError($"{where}: location prefab '{locationName}' failed to load " +
                    $"({(prefabEmpty ? "no asset" : prefabResult.ToString())}), so it was not rebuilt.");
                return;
            }

            bool roomMissing = false;
            for (int i = 0; i < rooms.Count; i++) {
                RoomLoad room = rooms[i];
                if (room.Answered && room.Result == LoadResult.Succeeded) { continue; }
                roomMissing = true;
                string what = room.Answered ? $"failed to load ({room.Result})" : $"did not finish loading within {maxWaitSeconds:0}s";
                Logger.LogLocationResetError($"{where}: room '{room.Name}' of '{room.Generator}' {what}, " +
                    $"so location '{locationName}' was not rebuilt.");
            }
            if (roomMissing == false && proxyGateOpen == false) {
                Logger.LogLocationResetWarning($"{where}: location '{locationName}' loaded but its spawn gate stayed closed for " +
                    $"{maxWaitSeconds:0}s, so it was not rebuilt.");
            }
        }

        // SoftReference.Name throws for an unassigned reference; see LocationResetData.SafePrefabName.
        private static string SafeName(SoftReference<GameObject> prefab) {
            try {
                if (prefab.IsValid) {
                    string name = prefab.Name;
                    if (string.IsNullOrEmpty(name) == false) { return name; }
                }
            } catch (System.Exception e) {
                Logger.LogDebug($"Could not resolve a room name from its soft reference: {e.Message}");
            }
            return prefab.m_assetID.ToString();
        }
    }
}
