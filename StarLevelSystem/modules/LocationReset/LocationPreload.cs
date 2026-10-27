using StarLevelSystem.common;
using StarLevelSystem.Data;
using SoftReferenceableAssets;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Loads a location's prefab, and every room its dungeon can place, before a reset rebuilds it,
    // and keeps them in memory for every later rebuild of the same location.
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
    // How the loader behaves decides the rest of the design. SoftReferenceableAssets hands every
    // LoadAsync to Unity's single background loading pipeline, which serves requests in order and
    // integrates only a few milliseconds of each frame, so a frost cave's eighty-odd rooms plus the
    // mesh and material bundles they share take tens of seconds on a dedicated server -- well past a
    // chunk's load wait. And releasing a reference that has not answered cancels nothing: the queued
    // loads still run to completion and are then followed by an async unload of every bundle, all
    // through the same pipeline. The first version of this class released its loads when the wait ran
    // out, and a server with frost caves showed what that does: each cave queued eighty loads, timed
    // out, and queued eighty unloads behind them; the chunks after it queued their prefabs behind that
    // backlog and timed out too; and the next cave started from scratch behind all of it. No cave or
    // dvergr town on that server ever rebuilt, and each attempt logged one error per room.
    //
    // So a load is never released early and never repeated. One entry per location prefab lives in a
    // cache keyed by the prefab's asset id. A chunk whose location is still loading when its wait runs
    // out is reported as a transient hold and comes back on the sweep's short retry, by which time the
    // loads are in; every later chunk with the same location finds them ready. Loads are requested one
    // location at a time, so the pipeline never carries more than one dungeon's queue, and an entry is
    // released once nothing has used it for IdleSeconds.
    //
    // A location whose prefab or rooms did not all load is not rebuilt; RegenerateLocationWith
    // checks Ready right before the clear and logs what held it back.
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

        // ---- The cache ----

        private static readonly Dictionary<AssetID, LocationPreload> cache = new Dictionary<AssetID, LocationPreload>();

        // How long an entry outlives its last use. Past the sweep's first short retry (five minutes),
        // so the chunk that found its location still loading comes back to a loaded one, and well
        // short of a cycle, so an idle server does not hold every dungeon it ever rebuilt.
        internal const float IdleSeconds = 15f * 60f;

        // A load that has not answered after this long is not slow, it is stuck: a room whose bundle
        // is missing never calls back at all. Such an entry stops holding the pipeline for other
        // locations, and what it is missing is reported as a fault rather than as still loading. Kept
        // under the first retry delay, so the retry finds the verdict already made.
        internal const float StuckSeconds = 4f * 60f;

        internal static int CachedCount { get { return cache.Count; } }

        // The entry whose loads are in the pipeline right now, if any.
        private static LocationPreload Busy() {
            foreach (LocationPreload entry in cache.Values) {
                if (entry.Loading) { return entry; }
            }
            return null;
        }

        // One entry per prefab. An unassigned reference has no id to key on -- every such location
        // would share one entry -- and RequestPrefab fails it on the spot, so it gets a throwaway.
        private static LocationPreload Acquire(ZoneSystem.ZoneLocation location, float now) {
            if (location.m_prefab.IsValid == false) { return new LocationPreload(location, now); }
            AssetID key = location.m_prefab.m_assetID;
            if (cache.TryGetValue(key, out LocationPreload preload) == false) {
                preload = new LocationPreload(location, now);
                cache[key] = preload;
            }
            preload.lastUsed = now;
            return preload;
        }

        // Drops entries nothing has used for IdleSeconds. Run at the start of every Prepare, the only
        // place entries are made, so the cache cannot grow without being trimmed.
        private static void Trim(float now) {
            List<AssetID> idle = null;
            foreach (KeyValuePair<AssetID, LocationPreload> kvp in cache) {
                if (now - kvp.Value.lastUsed < IdleSeconds) { continue; }
                if (idle == null) { idle = new List<AssetID>(); }
                idle.Add(kvp.Key);
            }
            if (idle == null) { return; }
            for (int i = 0; i < idle.Count; i++) {
                LocationPreload entry = cache[idle[i]];
                cache.Remove(idle[i]);
                entry.Release();
                Logger.LogLocationReset($"Released the assets of '{entry.Name}' after {IdleSeconds / 60f:0} min unused.");
            }
        }

        // World teardown: drop every reference. The loader outlives the world, so anything still held
        // here would be carried into the next one.
        internal static void Clear() {
            foreach (LocationPreload entry in cache.Values) {
                try { entry.Release(); }
                catch (System.Exception e) { Logger.LogDebug($"Could not release the assets of '{entry.Name}' on world unload: {e.Message}"); }
            }
            cache.Clear();
        }

        // ---- One location ----

        private readonly ZoneSystem.ZoneLocation location;
        private readonly List<RoomLoad> rooms = new List<RoomLoad>();

        private float lastUsed;
        // When the prefab's load was requested, for the stuck check.
        private float requestedAt;
        // The wait the latest Prepare allowed, for the log and the chunk record.
        private float waitSeconds;

        private bool prefabRequested;
        private bool prefabAnswered;
        private LoadResult prefabResult;
        // Rooms are only discoverable from the loaded prefab, so they are requested once it is in.
        private bool roomsRequested;
        // The prefab answered Succeeded but there was no asset to look into.
        private bool prefabEmpty;
        // ShouldDelayProxyLocationSpawning, as it read on the last poll.
        private bool proxyGateOpen;
        // Whether the "still loading" line and the fault lines have been written already, so a
        // location shared by twenty chunks reports each once rather than once per chunk.
        private bool loadingReported;
        private bool faultsReported;

        private LocationPreload(ZoneSystem.ZoneLocation location, float now) {
            this.location = location;
            lastUsed = now;
        }

        private string Name {
            get {
                if (string.IsNullOrEmpty(location.m_prefabName) == false) { return location.m_prefabName; }
                return location.m_prefab.m_assetID.ToString();
            }
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

        // Every load that was asked for has called back, one way or the other.
        private bool AllAnswered {
            get {
                if (prefabAnswered == false) { return false; }
                // A prefab that failed or came back empty asked for nothing more.
                if (PrefabLoaded == false) { return true; }
                if (roomsRequested == false) { return false; }
                for (int i = 0; i < rooms.Count; i++) {
                    if (rooms[i].Answered == false) { return false; }
                }
                return true;
            }
        }

        private bool AnyRoomFailed {
            get {
                for (int i = 0; i < rooms.Count; i++) {
                    if (rooms[i].Answered && rooms[i].Result != LoadResult.Succeeded) { return true; }
                }
                return false;
            }
        }

        // Nothing left worth waiting for: either everything is in, or something has already failed
        // and every load has answered, so the log can name all of them at once.
        private bool Settled {
            get {
                if (Ready) { return true; }
                if (AllAnswered == false) { return false; }
                return PrefabLoaded == false || AnyRoomFailed;
            }
        }

        private float Age { get { return Time.realtimeSinceStartup - requestedAt; } }

        // In the pipeline and not yet written off, which is what holds other locations' requests back.
        private bool Loading { get { return prefabRequested && AllAnswered == false && Age < StuckSeconds; } }

        private bool Stuck { get { return prefabRequested && AllAnswered == false && Age >= StuckSeconds; } }

        // Something answered with a failure, or never answered at all. Each is a fault in whichever
        // mod supplies the asset, and every rebuild of this location is skipped until it is fixed.
        private bool Faulted {
            get { return (prefabAnswered && PrefabLoaded == false) || AnyRoomFailed || Stuck; }
        }

        // Whether what held the rebuild back clears on its own: loads still in flight, or a pipeline
        // busy with another location's. The chunk then gets the sweep's short retry rather than a
        // cycle's backoff. Meaningless when Ready.
        internal bool Transient { get { return Ready == false && Faulted == false; } }

        // Loads what the chunk's governing location needs for a rebuild. onResult receives the entry
        // as soon as it is acquired; it receives nothing when no rebuild of this chunk's location can
        // happen this pass. The entry stays alive after this returns -- see the cache above -- so the
        // caller releases nothing.
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

            float now = Time.realtimeSinceStartup;
            Trim(now);
            LocationPreload preload = Acquire(instance.m_location, now);
            preload.waitSeconds = Mathf.Max(1f, maxWaitSeconds);
            onResult?.Invoke(preload);

            float deadline = now + preload.waitSeconds;
            while (true) {
                // One location's loads in the pipeline at a time. A request made behind a cave's
                // eighty rooms waits for all of them anyway, and a second dungeon's rooms on top would
                // only push both past every wait; held back, it goes in the moment the pipeline is
                // free, and until then this chunk reports a transient hold like any other.
                if (preload.prefabRequested == false && preload.prefabAnswered == false
                        && (preload.location.m_prefab.IsValid == false || Busy() == null)) {
                    preload.RequestPrefab();
                }
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
            requestedAt = Time.realtimeSinceStartup;
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

        // Drops every reference this entry took. Only the cache calls it, on trim and teardown. A load
        // still in flight may answer afterwards; its callback only writes to this object.
        private void Release() {
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
                if (prefabRequested == false && prefabAnswered == false) {
                    LocationPreload busy = Busy();
                    string with = busy != null ? $"'{busy.Name}'" : "another location's assets";
                    return $"its assets were not requested: the loader was still busy with {with} for {waitSeconds:0}s";
                }
                if (prefabAnswered == false) {
                    return Stuck ? $"its prefab did not answer its load within {StuckSeconds / 60f:0} min"
                                 : $"its prefab is still loading ({Age:0}s since it was requested)";
                }
                if (prefabEmpty) { return "its prefab loaded with no asset"; }
                if (prefabResult != LoadResult.Succeeded) { return $"its prefab failed to load ({prefabResult})"; }

                List<string> failed = new List<string>();
                int pending = 0;
                for (int i = 0; i < rooms.Count; i++) {
                    if (rooms[i].Answered == false) { pending++; }
                    else if (rooms[i].Result != LoadResult.Succeeded) { failed.Add($"'{rooms[i].Name}'"); }
                }
                if (failed.Count > 0) {
                    const int shown = 3;
                    string more = failed.Count > shown ? $" and {failed.Count - shown} more" : "";
                    string rest = pending > 0 ? $", {pending} still loading" : "";
                    return $"{failed.Count} rooms failed to load: {string.Join(", ", failed.GetRange(0, Mathf.Min(shown, failed.Count)))}{more}{rest}";
                }
                if (pending > 0) {
                    return Stuck ? $"{pending} of {rooms.Count} rooms did not answer their load within {StuckSeconds / 60f:0} min"
                                 : $"its rooms are still loading ({rooms.Count - pending} of {rooms.Count} in, {Age:0}s since they were requested)";
                }
                return $"its spawn gate stayed closed for {waitSeconds:0}s";
            }
        }

        // What held the rebuild back, to the BepInEx log. A location still loading is routine on a
        // server with large dungeons: one plain line the first time, and under the detail flag after
        // that. A fault is named once in full, as errors, when it is first found; the chunks that
        // follow get one warning each rather than the whole list again.
        internal void LogProblems(Vector2s zone, string locationName) {
            string where = $"Zone {zone.x},{zone.y}";
            if (Transient) {
                string line = $"{where}: location '{locationName}' was not rebuilt yet: {Problem}. " +
                    "The loads continue in the background, and the chunk is retried once they are in.";
                // A request held back by another location's loads is that location's news, not this one's.
                if (loadingReported || prefabRequested == false) { Logger.LogLocationReset(line); return; }
                loadingReported = true;
                Logger.LogLocationResetAlways(line);
                return;
            }

            if (faultsReported) {
                Logger.LogLocationResetWarning($"{where}: location '{locationName}' was not rebuilt: {Problem} (reported in full when first found).");
                return;
            }
            faultsReported = true;
            if (PrefabLoaded == false) {
                string what = prefabAnswered
                    ? $"failed to load ({(prefabEmpty ? "no asset" : prefabResult.ToString())})"
                    : $"did not answer its load within {StuckSeconds / 60f:0} min";
                Logger.LogLocationResetError($"{where}: location prefab '{locationName}' {what}, so it was not rebuilt.");
                return;
            }
            for (int i = 0; i < rooms.Count; i++) {
                RoomLoad room = rooms[i];
                if (room.Answered && room.Result == LoadResult.Succeeded) { continue; }
                string what = room.Answered ? $"failed to load ({room.Result})" : $"did not answer its load within {StuckSeconds / 60f:0} min";
                Logger.LogLocationResetError($"{where}: room '{room.Name}' of '{room.Generator}' {what}, " +
                    $"so location '{locationName}' was not rebuilt.");
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
