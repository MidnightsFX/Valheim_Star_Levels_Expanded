using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Server-side sweep driver. Attached to RandEventSystem, which exists only on a loaded world and
    // on dedicated servers alike.
    //
    // Throughput model (see the two lanes below):
    //   Fast lane  - pure ZDO work. No Heightmap, no instantiation, no zone loading. Sustains
    //                hundreds of zones/second, and handles picked bushes and part-mined ore.
    //   Slow lane  - requires PokeLocalZone so PlaceVegetation/SpawnLocation have a live Heightmap
    //                and colliders. Gated behind the census so only zones where something was
    //                actually destroyed pay this cost.
    //
    // Zone selection is a rotating cursor over a snapshot of m_generatedZones rather than a sorted
    // due-queue. With a 64m grid a full world is ~84k zones; at the default fast-lane rate a
    // complete pass takes minutes, while reset intervals are hours, so the cursor always comes back
    // around long before anything is overdue. The per-zone due check is a dictionary lookup and an
    // integer compare, so scanning zones that are not due is effectively free, and the rate limits
    // only count zones that did real work.
    public class LocationResetManager : MonoBehaviour {

        private const float TickIntervalSeconds = 5f;

        private double nextTickTime = 0d;
        private bool sweepRunning = false;

        // Rotating cursor over the generated-zone snapshot.
        private readonly List<Vector2i> zoneSnapshot = new List<Vector2i>();
        private int cursor = 0;

        // Rolling server frame time, drives the adaptive backoff.
        private float smoothedFrameMs = 0f;

        // ---- statistics, surfaced by SLS-loc-reset-status ----
        internal static long ZonesExamined = 0;
        internal static long FastLaneZones = 0;
        internal static long SlowLaneZones = 0;
        internal static long BlockedZones = 0;
        internal static long FirstSightZones = 0;
        internal static long ZdoGrowthTotal = 0;
        internal static double SweepStartedAt = 0d;
        internal static string LastAction = "idle";

        public void Awake() {
            SweepStartedAt = Time.realtimeSinceStartupAsDouble;
        }

        public void Update() {
            smoothedFrameMs = Mathf.Lerp(smoothedFrameMs, Time.unscaledDeltaTime * 1000f, 0.05f);

            if (sweepRunning) { return; }
            if (Time.realtimeSinceStartupAsDouble < nextTickTime) { return; }
            nextTickTime = Time.realtimeSinceStartupAsDouble + TickIntervalSeconds;

            if (LocationResetControl.SweepAllowed == false) { return; }
            if (ZoneSystem.instance == null || ZDOMan.instance == null) { return; }

            sweepRunning = true;
            StartCoroutine(SweepTick());
        }

        private IEnumerator SweepTick() {
            try {
                yield return RunSweep();
            } finally {
                sweepRunning = false;
            }
        }

        private IEnumerator RunSweep() {
            LocationResetConfigSnapshot cfg = LocationResetConfigSnapshot.Capture();
            if (cfg.AnythingEnabled == false) { yield break; }

            float budgetMs = LocationResetControl.SweepBudgetMs;
            // Back off when the server is already struggling; the sweep should always yield to
            // actual gameplay rather than compete with it.
            if (smoothedFrameMs > cfg.AdaptiveBackoffFrameMs) { budgetMs *= 0.5f; }

            int fastBudget = Mathf.Max(1, Mathf.RoundToInt(cfg.MaxZonesPerSecondFastLane * TickIntervalSeconds));
            int slowBudget = Mathf.Max(0, Mathf.RoundToInt(cfg.MaxZonesPerSecondSlowLane * TickIntervalSeconds));
            int fastUsed = 0;
            int slowUsed = 0;

            float sliceStart = Time.realtimeSinceStartup;
            int scannedThisPass = 0;

            while (fastUsed < fastBudget) {
                if (zoneSnapshot.Count == 0 || cursor >= zoneSnapshot.Count) {
                    RefreshSnapshot();
                    if (zoneSnapshot.Count == 0) { yield break; }
                }

                Vector2i zone = zoneSnapshot[cursor];
                cursor++;
                scannedThisPass++;
                ZonesExamined++;

                ZoneWork work = EvaluateZone(zone, cfg);
                if (work != ZoneWork.None) {
                    fastUsed++;
                    bool allowSlow = slowUsed < slowBudget;
                    yield return ProcessZone(zone, cfg, work, allowSlow, (didSlow) => { if (didSlow) { slowUsed++; } });
                }

                // Spend the frame budget, then hand the frame back.
                if (Time.realtimeSinceStartup - sliceStart >= budgetMs / 1000f) {
                    yield return null;
                    sliceStart = Time.realtimeSinceStartup;
                }

                // A full lap with nothing to do means the world is settled; stop early rather than
                // spinning the cursor for the rest of the tick.
                if (scannedThisPass >= zoneSnapshot.Count) { break; }
            }
        }

        private void RefreshSnapshot() {
            zoneSnapshot.Clear();
            if (ZoneSystem.instance?.m_generatedZones != null) {
                zoneSnapshot.AddRange(ZoneSystem.instance.m_generatedZones);
            }
            cursor = 0;
        }

        internal enum ZoneWork {
            None = 0,
            // Never seen before: record the census baseline and stamp it, but do not reset.
            FirstSight = 1,
            // Due for evaluation.
            Due = 2,
        }

        // The cheap gate that runs against every zone in the world. It executes tens of thousands of
        // times per lap, so it must stay allocation-free, must not touch ZDOMan, and must not
        // re-read the wall clock (Now is captured once per tick).
        private ZoneWork EvaluateZone(Vector2i zone, LocationResetConfigSnapshot cfg) {
            if (LocationResetState.TryGetZone(zone, out LocationResetState.ZoneRecord record) == false) {
                return cfg.StampOnFirstSight ? ZoneWork.FirstSight : ZoneWork.Due;
            }
            long elapsed = cfg.Now - record.ZoneStamp;
            if (elapsed < cfg.MinIntervalSeconds) { return ZoneWork.None; }
            return ZoneWork.Due;
        }

        private IEnumerator ProcessZone(Vector2i zone, LocationResetConfigSnapshot cfg, ZoneWork work, bool allowSlow, System.Action<bool> onSlowUsed) {
            if (work == ZoneWork.FirstSight) {
                LocationResetState.StampZone(zone);
                ZoneProtectionScan.RecordBaseline(zone);
                FirstSightZones++;
                LastAction = $"stamped new zone {zone.x},{zone.y}";
                yield break;
            }

            // Never reset a zone somebody is standing in or near. Also covers the case where the
            // zone is already loaded because a player is there.
            if (PlayersNearby(zone, cfg.PlayerSafeRadius) || ZoneSystem.instance.IsZoneLoaded(zone)) {
                LocationResetState.BackoffZone(zone, 300f);
                yield break;
            }

            // Zone-wide protection sweep. A blocked zone is stamped forward so it is not retried
            // every cycle; a base built over a crypt simply keeps it.
            ZoneProtectionScan.ProtectionResult protection = ZoneProtectionScan.ScanZone(zone, null, true);
            if (protection.Blocked) {
                BlockedZones++;
                LocationResetState.BackoffZone(zone, cfg.MinIntervalSeconds);
                LastAction = $"zone {zone.x},{zone.y} blocked by {protection.BlockingCategory}";
                yield break;
            }

            // ---- Fast lane: pure ZDO refresh, no loading ----
            FastLaneZones++;
            ResetTargets.RefreshZoneInPlace(zone, cfg);

            // ---- Slow lane: only if the census says something was destroyed here ----
            bool needsSlow = allowSlow && NeedsRegeneration(zone);
            if (needsSlow == false) {
                LocationResetState.StampZone(zone);
                onSlowUsed?.Invoke(false);
                yield break;
            }

            SlowLaneZones++;
            onSlowUsed?.Invoke(true);
            bool succeeded = false;
            yield return ResetTargets.RegenerateZone(zone, cfg, protection, false, (ok) => { succeeded = ok; });

            if (succeeded == false) {
                // RegenerateZone has already applied the appropriate backoff. Stamping here would
                // overwrite it, so just leave the zone alone.
                LastAction = $"zone {zone.x},{zone.y} reset did not complete, backed off";
                yield break;
            }

            LocationResetState.StampZone(zone);
            ZoneProtectionScan.RecordBaseline(zone);
            LastAction = $"reset zone {zone.x},{zone.y}";
        }

        // Does this zone need the expensive poke-load path? Either a due location lives here, or a
        // tracked prefab's live count has fallen below its recorded baseline. This is the gate that
        // keeps the majority of a world out of the expensive path.
        private bool NeedsRegeneration(Vector2i zone) {
            if (NeedsLocationReset(zone)) { return true; }
            if (LocationResetData.VegetationByPrefabHash.Count == 0) { return false; }
            Dictionary<int, ushort> live = ZoneProtectionScan.CensusZone(zone);

            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> tracked in LocationResetData.VegetationByPrefabHash) {
                if (tracked.Value.Enabled == false) { continue; }
                if (LocationResetState.TryGetEntry(zone, tracked.Key, out LocationResetState.EntryRecord record) == false) { continue; }
                if (record.Baseline == 0) { continue; }
                long elapsed = LocationResetState.Now - record.Stamp;
                if (elapsed < tracked.Value.ResetSeconds) { continue; }

                live.TryGetValue(tracked.Key, out ushort present);
                if (present < record.Baseline) { return true; }
            }
            return false;
        }

        // Locations have no census -- a looted crypt still contains all its objects, they are just
        // empty. Their timer lives on the surviving LocationProxy ZDO instead, which is readable
        // without loading the zone.
        private bool NeedsLocationReset(Vector2i zone) {
            if (LocationResetData.LocationsByHash.Count == 0) { return false; }
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { return false; }
            if (instance.m_location == null) { return false; }
            if (LocationResetData.TryGetLocationEntry(instance.m_location.Hash, out LocationResetData.ResolvedResetEntry entry) == false) { return false; }
            if (entry.Enabled == false) { return false; }

            ZDO proxy = ResetTargets.FindLocationProxy(zone, instance.m_location.Hash);
            if (proxy == null) { return false; }

            long lastReset = proxy.GetLong(DataObjects.SLS_LOC_RESET, 0L);
            // Never stamped: the first pass stamps it and the next one resets it, so a fresh install
            // never wipes every location at once.
            if (lastReset == 0) { return true; }
            return LocationResetState.Now - lastReset >= entry.ResetSeconds;
        }

        private static bool PlayersNearby(Vector2i zone, float radius) {
            if (ZNet.instance == null) { return false; }
            Vector3 center = ZoneSystem.GetZonePos(zone);
            float sqr = radius * radius;
            // Read the live list rather than GetPlayerList(), which builds a copy on every call.
            List<ZNet.PlayerInfo> players = ZNet.instance.m_players;
            if (players == null) { return false; }
            for (int i = 0; i < players.Count; i++) {
                Vector3 delta = players[i].m_position - center;
                delta.y = 0f;
                if (delta.sqrMagnitude <= sqr) { return true; }
            }
            return false;
        }
    }

    // Immutable per-tick view of the settings, so a config reload mid-sweep cannot change the rules
    // halfway through a zone.
    internal class LocationResetConfigSnapshot {
        internal bool AnythingEnabled;
        // Wall clock captured once per tick. Calling DateTimeOffset.UtcNow per zone would cost more
        // than the due check itself across a full-world lap.
        internal long Now;
        internal bool StampOnFirstSight;
        internal float PlayerSafeRadius;
        internal float MaxZoneLoadWaitSeconds;
        internal long MinIntervalSeconds;
        internal long DefaultIntervalSeconds;
        internal int MaxZonesPerSecondFastLane;
        internal int MaxZonesPerSecondSlowLane;
        internal float AdaptiveBackoffFrameMs;
        internal int ZdoGrowthTolerance;
        internal bool RefreshPickables;
        internal bool RefreshMineRocks;
        internal bool RefreshContainerLoot;

        internal static LocationResetConfigSnapshot Capture() {
            LocationResetConfiguration cfg = LocationResetData.SLE_LocationReset_Settings;
            LocationResetConfigSnapshot snap = new LocationResetConfigSnapshot();

            snap.Now = LocationResetState.Now;
            snap.StampOnFirstSight = cfg.StampOnFirstSight;
            snap.PlayerSafeRadius = cfg.PlayerSafeRadius;
            snap.MaxZoneLoadWaitSeconds = cfg.MaxZoneLoadWaitSeconds;
            snap.MinIntervalSeconds = (long)LocationResetData.MinEnabledIntervalSeconds;
            snap.DefaultIntervalSeconds = (long)(cfg.Defaults.ResetHours * 3600f);
            snap.MaxZonesPerSecondFastLane = cfg.Throughput.MaxZonesPerSecondFastLane;
            snap.MaxZonesPerSecondSlowLane = cfg.Throughput.MaxZonesPerSecondSlowLane;
            snap.AdaptiveBackoffFrameMs = cfg.Throughput.AdaptiveBackoffFrameMs;
            snap.ZdoGrowthTolerance = cfg.Throughput.ZdoGrowthTolerance;
            snap.RefreshPickables = cfg.InPlaceRefresh.Pickables;
            snap.RefreshMineRocks = cfg.InPlaceRefresh.MineRocks;
            snap.RefreshContainerLoot = cfg.InPlaceRefresh.ContainerDefaultLoot;

            bool anyLocation = false;
            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> kvp in LocationResetData.LocationsByHash) {
                if (kvp.Value.Enabled) { anyLocation = true; break; }
            }
            bool anyVegetation = false;
            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> kvp in LocationResetData.VegetationByPrefabHash) {
                if (kvp.Value.Enabled) { anyVegetation = true; break; }
            }
            // The in-place refresh tiers are useful on their own even with no regeneration targets.
            snap.AnythingEnabled = anyLocation || anyVegetation
                || snap.RefreshPickables || snap.RefreshMineRocks || snap.RefreshContainerLoot;

            // With no per-entry timers enabled the minimum collapses to zero, which would make every
            // zone permanently due and spin the cursor over the whole world every tick. Fall back to
            // the default interval so an in-place-refresh-only setup still paces itself.
            if (snap.MinIntervalSeconds <= 0) { snap.MinIntervalSeconds = snap.DefaultIntervalSeconds; }
            if (snap.MinIntervalSeconds <= 0) { snap.MinIntervalSeconds = 3600; }

            return snap;
        }
    }
}
