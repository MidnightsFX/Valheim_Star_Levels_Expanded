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

        // ---- statistics, surfaced by sls-loc-status ----
        internal static long ZonesExamined = 0;
        internal static long FastLaneZones = 0;
        internal static long SlowLaneZones = 0;
        // Blocked by a player-built structure. Named for what it counts: it was BlockedZones and was
        // reported as "Blocked by players", which it never was.
        internal static long ProtectionBlockedZones = 0;
        // Blocked by a player standing nearby, or by the zone already being loaded.
        internal static long PlayerBlockedZones = 0;
        // Transient blocks that got a short retry, and the ones that used the budget up.
        internal static long RetriedZones = 0;
        internal static long RetriesExhaustedZones = 0;
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
                // One file append per tick rather than one per chunk.
                LocationResetLog.Flush();
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

        // Beyond this from the origin a zone is not terrain. Vanilla's world ends at
        // WorldGenerator.waterEdge (10,500m), but this is deliberately an order of magnitude looser so
        // a world-expansion mod is not silently cut off -- the thing it exists to exclude sits at
        // 1,000,000m, another order of magnitude further out again.
        private const float OffWorldDistance = 100000f;

        private void RefreshSnapshot() {
            zoneSnapshot.Clear();
            if (ZoneSystem.instance?.m_generatedZones != null) {
                foreach (Vector2i zone in ZoneSystem.instance.m_generatedZones) {
                    // m_generatedZones includes the sector Valheim parks position-less ZDOs in, at
                    // x=1,000,000 z=1,000,000 (zones ~15623-15629). It is not terrain, but it IS
                    // permanently "loaded", so every lap it produced a run of chunk evaluations that
                    // could only ever end in "zone is already loaded" -- 337 evaluations and 100% of
                    // that skip class in one 28h log, each retried twice before being written off.
                    if (IsOffWorld(zone)) { continue; }
                    zoneSnapshot.Add(zone);
                }
            }
            cursor = 0;
        }

        private static bool IsOffWorld(Vector2i zone) {
            Vector3 center = ZoneSystem.GetZonePos(zone);
            return Mathf.Abs(center.x) > OffWorldDistance || Mathf.Abs(center.z) > OffWorldDistance;
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
            // Rate lookup first: an excluded biome or band costs one dictionary hit per lap and
            // nothing else, which is the throughput win of narrowing the sweep to the areas that
            // actually get depleted.
            float rate = ZoneRates.MultiplierFor(zone, cfg);
            if (rate <= ZoneRates.Excluded) { return ZoneWork.None; }

            if (LocationResetState.TryGetZone(zone, out LocationResetState.ZoneRecord record) == false) {
                return cfg.StampOnFirstSight ? ZoneWork.FirstSight : ZoneWork.Due;
            }

            // A pending retry replaces the interval floor rather than stacking with it. This is the
            // one case where a zone is deliberately due SOONER than MinIntervalSeconds: it was
            // blocked by something transient and we want another look in minutes, not a full cycle.
            if (record.RetryAt > 0L) {
                return cfg.Now >= record.RetryAt ? ZoneWork.Due : ZoneWork.None;
            }

            long elapsed = cfg.Now - record.ZoneStamp;
            // Scaling the global floor rather than leaving it fixed is what makes the rates real: a
            // biome at 0.25 has to clear a quarter of the floor to be considered, or it would sit
            // behind the unscaled gate and never come due early.
            if (elapsed < ZoneRates.ScaleSeconds(cfg.MinIntervalSeconds, rate)) { return ZoneWork.None; }
            return ZoneWork.Due;
        }

        private IEnumerator ProcessZone(Vector2i zone, LocationResetConfigSnapshot cfg, ZoneWork work, bool allowSlow, System.Action<bool> onSlowUsed) {
            // First sight is pure bookkeeping and fires once for every zone in the world, so it is
            // deliberately kept out of the chunk log; a fresh world would otherwise write ~84k records
            // that say nothing was reset.
            if (work == ZoneWork.FirstSight) {
                LocationResetState.StampZone(zone);
                ZoneProtectionScan.RecordBaseline(zone);
                FirstSightZones++;
                LastAction = $"stamped new zone {zone.x},{zone.y}";
                Logger.LogLocationReset($"Zone {zone.x},{zone.y}: first sight, baseline recorded, no reset.");
                yield break;
            }

            ZoneResetReport report = ZoneResetReport.For(zone, false);
            report.RateMultiplier = ZoneRates.MultiplierFor(zone, cfg);
            report.RateDescription = ZoneRates.Describe(zone, cfg);
            // finally rather than an emit at each exit: every path through this method produces a
            // record, including the ones that decide to do nothing.
            try {
                // Never reset a zone somebody is standing in or near. Also covers the case where the
                // zone is already loaded because a player is there.
                //
                // This block is transient by nature -- somebody walking past a chunk should not cost
                // it a whole cycle -- so it gets a couple of short retries before being written off.
                bool playerNear = PlayersNearby(zone, cfg.PlayerSafeRadius);
                if (playerNear || ZoneSystem.instance.IsZoneLoaded(zone)) {
                    PlayerBlockedZones++;
                    // Reported separately: "somebody is standing here" and "this chunk happens to be
                    // loaded" have very different causes and used to share one message.
                    string why = playerNear ? $"player within {cfg.PlayerSafeRadius:0}m" : "zone is already loaded";
                    if (LocationResetState.TryScheduleRetry(zone, out int attempt, out float delay)) {
                        RetriedZones++;
                        report.SkipReason = $"{why}; retry {attempt}/{LocationResetState.MaxTransientRetries} in {delay / 60f:0.#} min";
                    } else {
                        RetriesExhaustedZones++;
                        // Clears the retry state as well, so the next cycle starts with a full budget.
                        LocationResetState.BackoffZone(zone, 300f);
                        report.SkipReason = $"{why}; {LocationResetState.MaxTransientRetries} retries spent, deferred to the next cycle";
                    }
                    yield break;
                }

                // Zone-wide protection sweep. A blocked zone is stamped forward so it is not retried
                // every cycle; a base built over a crypt simply keeps it. No short retry here on
                // purpose: a structure is not going to move in fifteen minutes, and this scan is the
                // expensive part of a tick.
                //
                // Judged against the zone's own governing entries rather than bare Defaults, so a
                // reset group's Protection overrides -- "player builds do not block ore resets" --
                // decide blocking for chunks holding that group's content.
                ZoneProtectionScan.ProtectionResult protection =
                    ZoneProtectionScan.ScanZone(zone, ZoneProtectionScan.GoverningEntries(zone), true);
                if (protection.Blocked) {
                    ProtectionBlockedZones++;
                    LocationResetState.BackoffZone(zone, ZoneRates.ScaleSeconds(cfg.MinIntervalSeconds, report.RateMultiplier));
                    report.SkipReason = ZoneProtectionScan.DescribeBlock(protection);
                    yield break;
                }

                // ---- Fast lane: pure ZDO refresh, no loading ----
                FastLaneZones++;
                ResetTargets.RefreshZoneInPlace(zone, cfg, false, report);

                // ---- Slow lane: only if the census says something was destroyed here ----
                bool needsSlow = allowSlow && NeedsRegeneration(zone, report.RateMultiplier);
                if (needsSlow == false) {
                    LocationResetState.StampZone(zone);
                    onSlowUsed?.Invoke(false);
                    report.Detail(allowSlow
                        ? "no regeneration needed - nothing tracked is missing and no location is due"
                        : "regeneration skipped - slow lane budget spent for this tick");
                    yield break;
                }

                SlowLaneZones++;
                onSlowUsed?.Invoke(true);
                bool succeeded = false;
                yield return ResetTargets.RegenerateZone(zone, cfg, false, report, (ok) => { succeeded = ok; });

                if (succeeded == false) {
                    // RegenerateZone owns the deferral decision on every one of its failure paths --
                    // a short retry for a load timeout, a backoff for anything else. Stamping here
                    // would overwrite whichever it chose, so leave the zone alone.
                    report.SkipReason = report.SkipReason ?? "reset did not complete";
                    yield break;
                }

                LocationResetState.StampZone(zone);
                ZoneProtectionScan.RecordBaseline(zone);
            } finally {
                EmitZoneReport(report);
            }
        }

        // One record per worked chunk: to the dedicated Location Reset log always, and to the BepInEx
        // log only when the detail flag is on, since the sweep runs continuously.
        private static void EmitZoneReport(ZoneResetReport report) {
            string record = report.ToRecord();
            LocationResetLog.Record(record, "sweep");
            Logger.LogLocationReset(record);
            LastAction = report.ToSummaryLine();
        }

        // Does this zone need the expensive poke-load path? Either a due location lives here, or a
        // tracked prefab's live count has fallen below its recorded baseline. This is the gate that
        // keeps the majority of a world out of the expensive path.
        private bool NeedsRegeneration(Vector2i zone, float rate) {
            if (NeedsLocationReset(zone, rate)) { return true; }
            if (LocationResetData.VegetationByPrefabHash.Count == 0) { return false; }
            Dictionary<int, ushort> live = ZoneProtectionScan.CensusZone(zone);

            float distance = ZoneRates.DistanceFor(zone);
            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> tracked in LocationResetData.VegetationByPrefabHash) {
                LocationResetData.ResolvedResetEntry entry = tracked.Value.ForDistance(distance);
                if (entry.Enabled == false) { continue; }
                if (LocationResetState.TryGetEntry(zone, tracked.Key, out LocationResetState.EntryRecord record) == false) { continue; }
                if (record.Baseline == 0) { continue; }
                if (entry.IsDue(record.Stamp, LocationResetState.Now, rate) == false) { continue; }

                live.TryGetValue(tracked.Key, out ushort present);
                if (present < record.Baseline) { return true; }
            }
            return false;
        }

        // Locations have no census -- a looted crypt still contains all its objects, they are just
        // empty. Their timer lives on the surviving LocationProxy ZDO instead, which is readable
        // without loading the zone.
        private bool NeedsLocationReset(Vector2i zone, float rate) {
            if (LocationResetData.LocationsByHash.Count == 0) { return false; }
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { return false; }
            if (instance.m_location == null) { return false; }
            if (LocationResetData.TryGetLocationEntry(instance.m_location.Hash, out LocationResetData.ResolvedResetEntry entry) == false) { return false; }
            entry = entry.ForDistance(ZoneRates.DistanceFor(zone));
            if (entry.Enabled == false) { return false; }

            ZDO proxy = ResetTargets.FindLocationProxy(zone, instance.m_location.Hash);
            if (proxy == null) { return false; }

            long lastReset = proxy.GetLong(DataObjects.SLS_LOC_RESET, 0L);
            // Never stamped: the first pass stamps it and the next one resets it, so a fresh install
            // never wipes every location at once.
            if (lastReset == 0) { return true; }
            return entry.IsDue(lastReset, LocationResetState.Now, rate);
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
        // Set when Defaults carries a ResetSchedule instead of ResetHours. Only the fallback path in
        // DueForRefresh needs it; every configured target resolves its own schedule.
        internal CronSchedule DefaultSchedule;
        internal int MaxZonesPerSecondFastLane;
        internal int MaxZonesPerSecondSlowLane;
        internal float AdaptiveBackoffFrameMs;
        internal int ZdoGrowthTolerance;
        internal bool RefreshPickables;
        internal bool RefreshMineRocks;
        internal bool RefreshContainerLoot;
        // Rate targeting, captured by reference. Snapshotting them keeps a mid-sweep yaml reload from
        // resetting one chunk under two different rates.
        internal Dictionary<Heightmap.Biome, float> BiomeRates;
        internal List<LocationResetBand> DistanceBands;
        // False when every configured rate is 1.0, letting the per-chunk lookup skip its work
        // entirely. Computed here rather than per chunk because Capture runs once per tick.
        internal bool RatesActive;

        internal static LocationResetConfigSnapshot Capture() {
            LocationResetConfiguration cfg = LocationResetData.SLE_LocationReset_Settings;
            LocationResetConfigSnapshot snap = new LocationResetConfigSnapshot();

            snap.Now = LocationResetState.Now;
            snap.StampOnFirstSight = cfg.StampOnFirstSight;
            snap.PlayerSafeRadius = cfg.PlayerSafeRadius;
            snap.MaxZoneLoadWaitSeconds = cfg.MaxZoneLoadWaitSeconds;
            snap.MinIntervalSeconds = (long)LocationResetData.MinEnabledIntervalSeconds;
            snap.DefaultIntervalSeconds = (long)(cfg.Defaults.ResetHours * 3600f);
            snap.DefaultSchedule = LocationResetData.DefaultSchedule;
            // Both sections are omitted from the generated config, so read them through the
            // accessors rather than off cfg -- they are null on any file that never mentioned them.
            LocationResetThroughput throughput = LocationResetData.Throughput;
            LocationResetInPlace inPlace = LocationResetData.InPlaceRefresh;
            snap.MaxZonesPerSecondFastLane = throughput.MaxZonesPerSecondFastLane;
            snap.MaxZonesPerSecondSlowLane = throughput.MaxZonesPerSecondSlowLane;
            snap.AdaptiveBackoffFrameMs = throughput.AdaptiveBackoffFrameMs;
            snap.ZdoGrowthTolerance = throughput.ZdoGrowthTolerance;
            snap.RefreshPickables = inPlace.Pickables;
            snap.RefreshMineRocks = inPlace.MineRocks;
            snap.RefreshContainerLoot = inPlace.ContainerDefaultLoot;
            snap.BiomeRates = cfg.BiomeRates;
            snap.DistanceBands = cfg.DistanceBands;
            snap.RatesActive = AnyRateActive(cfg);

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
            if (snap.MinIntervalSeconds <= 0 && snap.DefaultSchedule != null) {
                snap.MinIntervalSeconds = snap.DefaultSchedule.MinGapSeconds;
            }
            if (snap.MinIntervalSeconds <= 0) { snap.MinIntervalSeconds = snap.DefaultIntervalSeconds; }
            if (snap.MinIntervalSeconds <= 0) { snap.MinIntervalSeconds = 3600; }

            return snap;
        }

        // A config that lists every biome at 1.0 -- which is exactly what the generated default file
        // contains -- is targeting nothing, and should cost nothing.
        private static bool AnyRateActive(LocationResetConfiguration cfg) {
            if (cfg.BiomeRates != null) {
                foreach (KeyValuePair<Heightmap.Biome, float> kvp in cfg.BiomeRates) {
                    if (Mathf.Approximately(kvp.Value, 1f) == false) { return true; }
                }
            }
            if (cfg.DistanceBands != null) {
                foreach (LocationResetBand band in cfg.DistanceBands) {
                    if (band != null && Mathf.Approximately(band.Multiplier, 1f) == false) { return true; }
                }
            }
            return false;
        }
    }
}
