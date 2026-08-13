using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // The three reset tiers.
    //
    //  Tier 1 (RefreshZoneInPlace) mutates surviving ZDOs and never loads a zone or destroys
    //         anything, so it can never touch player property.
    //  Tier 2 (vegetation) and Tier 3 (locations) run inside RegenerateZone, which poke-loads the
    //         zone because PlaceVegetation and SpawnLocation need a live Heightmap and colliders.
    internal static class ResetTargets {

        private static readonly List<ZDO> zdoBuffer = new List<ZDO>();

        // -----------------------------------------------------------------------------------
        // Tier 1 - in-place ZDO refresh
        // -----------------------------------------------------------------------------------
        //
        // Everything here is a ZDO write that vanilla's own Awake path picks up the next time the
        // object loads. No zone is loaded, nothing is destroyed and nothing is created, so this
        // cannot duplicate items or damage a build. It is also where most of the throughput comes
        // from: harvested-but-still-present content is the common case on a busy server.
        internal static void RefreshZoneInPlace(Vector2i zone, LocationResetConfigSnapshot cfg, bool force, ZoneResetReport report) {
            if (ZDOMan.instance == null) { return; }
            if (cfg.RefreshPickables == false && cfg.RefreshMineRocks == false && cfg.RefreshContainerLoot == false) { return; }

            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(zone, zdoBuffer);

            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                int prefab = zdo.m_prefab;

                if (cfg.RefreshPickables && ZoneProtectionScan.PickableHashes.Contains(prefab)) {
                    if (DueForRefresh(zone, prefab, cfg, force, report.RateMultiplier) == false) { report.PickablesNotDue++; continue; }
                    if (RefreshPickable(zdo)) { report.PickablesRefreshed++; }
                    continue;
                }
                if (cfg.RefreshMineRocks && ZoneProtectionScan.MineRock5Hashes.Contains(prefab)) {
                    if (DueForRefresh(zone, prefab, cfg, force, report.RateMultiplier) == false) { report.MineRocksNotDue++; continue; }
                    if (RefreshMineRock5(zdo)) { report.MineRocksRefreshed++; }
                    continue;
                }
                if (cfg.RefreshMineRocks && ZoneProtectionScan.MineRockAreaCounts.ContainsKey(prefab)) {
                    if (DueForRefresh(zone, prefab, cfg, force, report.RateMultiplier) == false) { report.MineRocksNotDue++; continue; }
                    if (RefreshMineRock(zdo, prefab)) { report.MineRocksRefreshed++; }
                    continue;
                }
                if (cfg.RefreshContainerLoot && ZoneProtectionScan.containerHashes.Contains(prefab)) {
                    if (DueForRefresh(zone, prefab, cfg, force, report.RateMultiplier) == false) { report.ContainersNotDue++; continue; }
                    if (RefreshContainerLoot(zdo)) { report.ContainersRefreshed++; }
                }
            }

            zdoBuffer.Clear();
        }

        // A prefab with its own Vegetation config entry uses that entry's timer and opt-in flag;
        // anything else falls back to the global default interval measured from the zone stamp.
        //
        // force skips the timers entirely. An admin asking for a reset now means now, and without this
        // SLS-loc-reset-here would still silently honour every per-prefab timestamp.
        private static bool DueForRefresh(Vector2i zone, int prefabHash, LocationResetConfigSnapshot cfg, bool force, float rate) {
            if (force) { return true; }
            if (LocationResetData.TryGetVegetationEntry(prefabHash, out LocationResetData.ResolvedResetEntry entry)) {
                // A distance-scoped group can override the timer for this chunk only.
                entry = entry.ForDistance(ZoneRates.DistanceFor(zone));
                if (entry.Enabled == false) { return false; }
                if (LocationResetState.TryGetEntry(zone, prefabHash, out LocationResetState.EntryRecord record) && record.Stamp > 0) {
                    return entry.IsDue(record.Stamp, LocationResetState.Now, rate);
                }
                return true;
            }
            if (LocationResetState.TryGetZone(zone, out LocationResetState.ZoneRecord zoneRecord) == false) { return false; }
            // No config entry of its own, so this one rides on Defaults - which can itself be a cron
            // schedule, hence the snapshot carrying both forms.
            if (cfg.DefaultSchedule != null) {
                // The interval branch below gets this for free from ScaleSeconds; state it here so
                // both halves agree that an excluded chunk is never due.
                if (rate <= ZoneRates.Excluded) { return false; }
                return cfg.DefaultSchedule.HasElapsedSince(zoneRecord.ZoneStamp, LocationResetState.Now);
            }
            return LocationResetState.Now - zoneRecord.ZoneStamp >= ZoneRates.ScaleSeconds(cfg.DefaultIntervalSeconds, rate);
        }

        // Pickables that hid instead of despawning keep their ZDO with picked=true. Clearing it is a
        // complete restore: Pickable.Awake reads both keys and re-enables m_hideWhenPicked itself.
        // Pickables with no m_hideWhenPicked destroy their ZDO on pick, so they are never seen here
        // and are handled by Tier 2 instead.
        private static bool RefreshPickable(ZDO zdo) {
            if (zdo.GetBool(ZDOVars.s_picked, false) == false) { return false; }
            TakeOwnership(zdo);
            zdo.Set(ZDOVars.s_picked, false);
            zdo.Set(ZDOVars.s_pickedTime, 0L);
            return true;
        }

        // MineRock5 packs every hit area's health into one base64 string. LoadHealth only applies it
        // when the string is non-empty, so blanking it restores each area to its prefab default.
        private static bool RefreshMineRock5(ZDO zdo) {
            string health = zdo.GetString(ZDOVars.s_health, "");
            if (string.IsNullOrEmpty(health)) { return false; }
            TakeOwnership(zdo);
            zdo.Set(ZDOVars.s_health, "");
            return true;
        }

        // Old-style MineRock keeps one float per hit area. Write explicit full health rather than
        // removing the keys, because ZDO.RemoveFloat does not bump the data revision.
        private static bool RefreshMineRock(ZDO zdo, int prefabHash) {
            if (ZoneProtectionScan.MineRockAreaCounts.TryGetValue(prefabHash, out int areas) == false) { return false; }
            ZoneProtectionScan.MineRockBaseHealth.TryGetValue(prefabHash, out float baseHealth);
            if (baseHealth <= 0f) { return false; }

            float fullHealth = baseHealth;
            if (Game.instance != null) {
                fullHealth += Game.m_worldLevel * baseHealth * Game.instance.m_worldLevelMineHPMultiplier;
            }

            bool changed = false;
            for (int area = 0; area < areas; area++) {
                string key = "Health" + area.ToString();
                if (zdo.GetFloat(key, fullHealth) >= fullHealth) { continue; }
                if (changed == false) { TakeOwnership(zdo); changed = true; }
                zdo.Set(key, fullHealth);
            }
            return changed;
        }

        // Re-roll a container's default loot. Only ever applied to containers with no creator, so a
        // player's chest is never touched even if the category action was relaxed.
        private static bool RefreshContainerLoot(ZDO zdo) {
            if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L) { return false; }
            if (zdo.GetBool(ZDOVars.s_addedDefaultItems, false) == false) { return false; }
            TakeOwnership(zdo);
            zdo.Set(ZDOVars.s_addedDefaultItems, false);
            return true;
        }

        // Vanilla gates replication on ownership, so a write from a non-owner is a local-only edit
        // another peer will overwrite. Claim the ZDO first.
        internal static void TakeOwnership(ZDO zdo) {
            if (zdo.IsOwner()) { return; }
            zdo.SetOwner(ZDOMan.GetSessionID());
        }

        // -----------------------------------------------------------------------------------
        // Tiers 3 and 2 - regeneration (requires a loaded zone)
        // -----------------------------------------------------------------------------------

        // onComplete reports whether the regeneration finished cleanly. A failure must NOT be
        // stamped as done: the clear and the respawn are one operation, and abandoning it in the
        // middle would leave the location permanently empty.
        internal static IEnumerator RegenerateZone(Vector2i zone, LocationResetConfigSnapshot cfg,
                                                   bool force, ZoneResetReport report, System.Action<bool> onComplete) {
            if (ZoneSystem.instance == null || ZDOMan.instance == null) { onComplete?.Invoke(false); yield break; }

            bool loaded = false;
            yield return ZoneLoader.Load(zone, cfg.MaxZoneLoadWaitSeconds, force, (ok) => { loaded = ok; });
            if (loaded == false) {
                // A load timeout is usually transient (the server was busy), so retry shortly. It
                // MUST defer one way or the other: this path used to return before the backoff block
                // below, leaving ZoneStamp untouched, so the zone stayed permanently due and burned
                // MaxZoneLoadWaitSeconds of slow-lane budget on every single cursor lap, forever.
                if (LocationResetState.TryScheduleRetry(zone, out int attempt, out float delay)) {
                    report.SkipReason = $"zone did not finish loading in {cfg.MaxZoneLoadWaitSeconds:0}s; " +
                        $"retry {attempt}/{LocationResetState.MaxTransientRetries} in {delay / 60f:0.#} min";
                } else {
                    LocationResetState.BackoffZone(zone, ZoneRates.ScaleSeconds(cfg.MinIntervalSeconds, report.RateMultiplier));
                    report.SkipReason = $"zone did not finish loading in {cfg.MaxZoneLoadWaitSeconds:0}s; " +
                        $"{LocationResetState.MaxTransientRetries} retries spent, deferred to the next cycle";
                }
                onComplete?.Invoke(false);
                yield break;
            }
            // Load only registers zones it actually poked, so a zone that came back loaded without being
            // registered is one we adopted while it was already live.
            report.ZoneAdopted = ZoneLoader.WasManuallyLoaded(zone) == false;

            // A location configured with ExtraTerrainRadius can reach past its own chunk, and terrain
            // only resets where a heightmap is live, so those neighbours have to come up too. Loading
            // is hoisted here because the regeneration tiers below are synchronous and cannot yield.
            List<Vector2i> extraZones = ExtraTerrainZones(zone);
            if (extraZones != null) {
                for (int i = 0; i < extraZones.Count; i++) {
                    yield return ZoneLoader.Load(extraZones[i], cfg.MaxZoneLoadWaitSeconds, force, null);
                    // Loading a neighbour can span several frames, and a poked zone with no live
                    // instances is reaped after 4s. Keep the one we already have.
                    ZoneLoader.KeepAlive(zone);
                }
            }

            // The location prefab must be fully loaded BEFORE the synchronous section. In Full mode the
            // LocationProxy client-spawns the prefab immediately, but LocationProxy.SpawnLocation bails
            // and retries next frame when ShouldDelayProxyLocationSpawning is true -- driven by an
            // async asset load that is typically incomplete the first time a given location type is
            // reset. A deferral means the terrain shaping misses SnapToGround.SnappAll entirely, which
            // is precisely the floating-contents bug. Waiting has to happen here; RegenerateLocation
            // cannot yield.
            yield return WaitForLocationPrefab(zone, cfg.MaxZoneLoadWaitSeconds);

            // Sampled here rather than at the top of the method: poke-loading a neighbour that was
            // never generated GENERATES it, and with a 3x3 footprint all of that vegetation would land
            // in the sample as growth this reset did not cause. Taking it after every load narrows the
            // window to exactly the regeneration below.
            //
            // The per-prefab breakdown is only collected under the detail flag. It has to be captured
            // up front to be able to diff it later, so this is the one place the flag has to be
            // consulted before anything has gone wrong -- a normal sweep passes null and pays nothing.
            Dictionary<int, int> prefabsBefore = report.Verbose ? new Dictionary<int, int>() : null;
            Dictionary<int, int> interiorPrefabsBefore = report.Verbose ? new Dictionary<int, int>() : null;
            int zdosBefore = ZoneProtectionScan.BlockZdoCount(zone, out int interiorBefore, prefabsBefore, interiorPrefabsBefore);

            bool succeeded = true;
            try {
                // Vanilla's own ordering: locations first so vegetation sees the fresh clear areas.
                RegenerateLocation(zone, cfg, force, report);
                RegenerateVegetation(zone, cfg, force, report);
            } catch (System.Exception e) {
                succeeded = false;
                Logger.LogLocationResetError($"Reset of zone {zone.x},{zone.y} failed and will be retried: {e}");
            } finally {
                // Release is a no-op for chunks this class did not load, so a neighbour that was
                // already live (a player standing in it) is never torn down.
                if (extraZones != null) {
                    for (int i = 0; i < extraZones.Count; i++) { ZoneLoader.Release(extraZones[i]); }
                }
                ZoneLoader.Release(zone);
            }

            // Balanced accounting. A faithful restore returns the block to its original ZDO count;
            // sustained growth is how a reset system silently bloats a world save over months.
            //
            // Draining the destroy queue first is what makes the number mean anything. ZDOMan.DestroyZDO
            // only appends to m_destroySendList; a ZDO leaves m_objectsBySector -- the list the count
            // reads -- in ZDOMan.Update -> SendDestroyed -> HandleDestroyedZDO -> RemoveFromSector, on
            // the NEXT frame. Creation is synchronous. Sampling without this counted every cleared
            // object as if it were still there while every spawned one already counted, so growth came
            // out as exactly "objects spawned" on every single reset and no zone could ever pass.
            // Calling it here rather than yielding a frame keeps unrelated world churn out of the
            // measurement; for the Everybody target it dispatches locally and synchronously.
            ZDOMan.instance.SendDestroyed();
            Dictionary<int, int> prefabsAfter = report.Verbose ? new Dictionary<int, int>() : null;
            Dictionary<int, int> interiorPrefabsAfter = report.Verbose ? new Dictionary<int, int>() : null;
            int zdosAfter = ZoneProtectionScan.BlockZdoCount(zone, out int interiorAfter, prefabsAfter, interiorPrefabsAfter);

            // Surface only. A regenerated dungeon comes back with a different room layout and a
            // legitimately different object count, so folding the interior into this reported every
            // chunk containing a dungeon as leaking. The interior is still recorded and shown, just
            // never warned on -- with the interior clear fixed, a DOUBLING interior is a real bug and
            // these numbers are the only place it would surface.
            int growth = zdosAfter - zdosBefore;
            report.ZdoBefore = zdosBefore;
            report.ZdoAfter = zdosAfter;
            report.ZdoInteriorBefore = interiorBefore;
            report.ZdoInteriorAfter = interiorAfter;
            report.ZdoCounted = true;

            // Which prefabs actually grew. This is the question every ZDO-growth investigation starts
            // with, and answering it from the chunk log beats re-deriving it from a save dump.
            if (growth > 0) { ReportGrowthByPrefab(report, "surface", growth, prefabsBefore, prefabsAfter); }
            if (interiorAfter > interiorBefore) {
                ReportGrowthByPrefab(report, "interior", interiorAfter - interiorBefore, interiorPrefabsBefore, interiorPrefabsAfter);
            }

            // An adopted zone is live: creatures spawn, players drop things, and items despawn between
            // the two samples. That noise is not our drift, so it neither counts towards the global
            // total nor is reported below.
            if (growth != 0 && report.ZoneAdopted == false) { LocationResetManager.ZdoGrowthTotal += growth; }

            if (succeeded == false) {
                // Clear and respawn are one operation. Retry soon rather than leaving a location
                // cleared but not rebuilt.
                LocationResetState.BackoffZone(zone, 60f);
            } else if (report.ZoneAdopted == false && growth > cfg.ZdoGrowthTolerance) {
                // Observational only. The reset itself completed, so it stays a success: the caller
                // goes on to stamp the zone and re-record its census, which must reflect the world
                // that now exists. Deferring the zone here (this once backed it off for a day and
                // reported failure) meant a drift report suppressed the very bookkeeping that would
                // have kept the next pass correct.
                Logger.LogLocationResetWarning($"Zone {zone.x},{zone.y} gained {growth} ZDOs during a reset " +
                    $"(before {zdosBefore}, after {zdosAfter}). Reset kept; check SLS-loc-reset-audit if this persists.");
            }

            // Reported last so this method owns every backoff decision; the caller only stamps the
            // zone as done when we report success, and never overwrites a backoff we just applied.
            onComplete?.Invoke(succeeded);
        }

        // Per-prefab before/after for a chunk that came out heavier than it went in, so the log names
        // what grew instead of just how much. Debug-only: both censuses are null unless
        // EnableDebugLocationResetDetails is on, and this returns immediately without them.
        //
        // Only CHANGED prefabs are listed. A populated chunk carries dozens of prefab types and
        // almost none of them move during a reset; printing the unchanged ones would bury the two or
        // three lines that matter under hundreds that do not. The unchanged count is reported so
        // nothing is silently hidden.
        private const int MaxGrowthBreakdownLines = 30;

        private static void ReportGrowthByPrefab(ZoneResetReport report, string scope, int growth,
                                                 Dictionary<int, int> before, Dictionary<int, int> after) {
            if (before == null || after == null) { return; }

            List<KeyValuePair<int, int>> changed = new List<KeyValuePair<int, int>>();
            int unchanged = 0;
            foreach (KeyValuePair<int, int> kvp in after) {
                before.TryGetValue(kvp.Key, out int was);
                if (kvp.Value == was) { unchanged++; continue; }
                changed.Add(new KeyValuePair<int, int>(kvp.Key, kvp.Value - was));
            }
            // A prefab cleared down to nothing is absent from `after` entirely, so it has to be picked
            // up from the other side or a reset that swapped one prefab for another would look
            // one-sided.
            foreach (KeyValuePair<int, int> kvp in before) {
                if (after.ContainsKey(kvp.Key)) { continue; }
                changed.Add(new KeyValuePair<int, int>(kvp.Key, -kvp.Value));
            }

            // Biggest gain first: the culprit should be the first line under the summary.
            changed.Sort((a, b) => b.Value.CompareTo(a.Value));

            report.Detail($"{scope} ZDO growth +{growth} across the 3x3 block, by prefab " +
                $"({changed.Count} types changed, {unchanged} unchanged):");
            for (int i = 0; i < changed.Count && i < MaxGrowthBreakdownLines; i++) {
                int hash = changed[i].Key;
                before.TryGetValue(hash, out int was);
                after.TryGetValue(hash, out int now);
                report.Detail($"    {ZoneProtectionScan.PrefabNameFor(hash),-34} {was,5} -> {now,-5} ({changed[i].Value:+#;-#;0})");
            }
            if (changed.Count > MaxGrowthBreakdownLines) {
                report.Detail($"    ... and {changed.Count - MaxGrowthBreakdownLines} more changed prefab types");
            }
        }

        // -----------------------------------------------------------------------------------
        // Tier 3 - location reset
        // -----------------------------------------------------------------------------------
        //
        // Vanilla PlaceLocations rolls a location's rotation from the LIVE Random state before it
        // seeds anything, so replaying it gives a different orientation every time -- which is why
        // Upgrade World's resets visibly rotate buildings. We bypass PlaceLocations entirely:
        // capture the existing LocationProxy's transform, clear, then call SpawnLocation directly
        // with that exact position and rotation. The result is a restore rather than a re-roll.
        //
        // SpawnLocation also re-runs DungeonGenerator.Generate internally, and Generate seeds
        // Random.InitState(seed) before laying out rooms, so interiors come back deterministically
        // too. (Radial camps still vary slightly: their wall placement collision-tests against live
        // colliders, which differ between runs.)
        private static void RegenerateLocation(Vector2i zone, LocationResetConfigSnapshot cfg, bool force, ZoneResetReport report) {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { return; }
            if (instance.m_location == null) { return; }

            int locationHash = instance.m_location.Hash;
            string locationName = instance.m_location.m_prefabName;
            if (LocationResetData.TryGetLocationEntry(locationHash, out LocationResetData.ResolvedResetEntry entry) == false) {
                report.Detail($"location '{locationName}' is not in the reset configuration");
                return;
            }
            entry = entry.ForDistance(ZoneRates.DistanceFor(zone));
            report.GroupName = entry.GroupName;
            if (entry.Enabled == false) {
                report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.Disabled);
                return;
            }
            // Hard-blocked locations are never resettable, no matter the config or a force command.
            if (LocationResetData.HardBlockedLocations.Contains(entry.Name)) {
                report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.HardBlocked);
                return;
            }

            ZDO proxy = FindLocationProxy(zone, locationHash);
            if (proxy == null) {
                report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.NoProxy);
                return;
            }

            if (force == false) {
                // Per-location timer rides on the proxy ZDO so it survives even if the state file is lost.
                long lastReset = proxy.GetLong(DataObjects.SLS_LOC_RESET, 0L);
                long now = LocationResetState.Now;
                if (lastReset > 0 && entry.IsDue(lastReset, now, report.RateMultiplier) == false) {
                    report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.NotDue);
                    if (report.Verbose) {
                        float elapsedHours = (now - lastReset) / 3600f;
                        report.Detail($"location '{entry.Name}' not due ({elapsedHours:0.#}h elapsed, " +
                            $"schedule {entry.DescribeSchedule(now, report.RateMultiplier)})");
                    }
                    return;
                }
                if (lastReset == 0) {
                    // First sight of this location: stamp it and let the next cycle do the work, so
                    // installing the mod never resets everything at once.
                    TakeOwnership(proxy);
                    proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
                    report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.FirstSightStamped);
                    return;
                }
            }

            Vector3 position = proxy.GetPosition();
            Quaternion rotation = proxy.GetRotation();
            float exteriorRadius = instance.m_location.m_exteriorRadius;
            float terrainRadius = TerrainRadiusFor(entry, exteriorRadius);
            report.TerrainRadius = terrainRadius;

            // Boss altars and similar: undo the crater players dug, leave the location itself alone.
            if (entry.Mode == LocationResetMode.TerrainOnly) {
                int undone = ResetTerrainLive(zone, position, terrainRadius);
                TakeOwnership(proxy);
                proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
                report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.TerrainOnly);
                report.LocationCleared = undone;
                return;
            }

            bool hasInterior = HasSkyInterior(zone);

            // A location whose interior we are told to leave alone cannot be rebuilt at all. Vanilla's
            // SpawnLocation always re-runs DungeonGenerator.Generate, so skipping only the clear would
            // stack a fresh interior on the old one every cycle -- which is what this flag used to do.
            // Leave the location alone, and stamp it so the sweep does not reconsider it every pass.
            //
            // Terrain is still honoured per config, unlike the TerrainOnly branch above which resets
            // it unconditionally: the admin asked to leave the dungeon alone, not to reshape ground.
            if (entry.ResetInterior == false && hasInterior) {
                if (entry.ResetTerrain) { report.TerrainModificationsUndone += ResetTerrainLive(zone, position, terrainRadius); }
                TakeOwnership(proxy);
                proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
                report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.InteriorPreserved);
                return;
            }

            ZDOID oldProxyId = proxy.m_uid;
            int cleared = ClearLocation(zone, position, rotation, exteriorRadius, instance.m_location, entry, report);
            if (entry.ResetTerrain) { report.TerrainModificationsUndone += ResetTerrainLive(zone, position, terrainRadius); }

            int seed = WorldGenerator.instance.GetSeed() + (zone.x * 4271) + (zone.y * 9187);

            // SpawnMode.Full, NOT Ghost. Ghost mode instantiates only the location's ZNetView-bearing
            // children, so a prefab whose terrain shaping lives on non-networked TerrainModifier
            // children -- a tarpit's depression, for one -- never carves its hole. SpawnLocation then
            // unconditionally runs SnapToGround.SnappAll(), which snaps children to the UN-shaped
            // ground and writes that height straight into their persistent ZDOs. The pit reappears
            // later when a client spawns the real prefab, and its contents are left hanging in the air.
            //
            // Full mode is what world generation itself uses: it creates the child ZDOs, then a
            // LocationProxy with spawnNow=true, whose inner Client-mode spawn brings the whole prefab
            // up so the terrain modifiers poke the heightmap BEFORE that inner call's own SnappAll
            // flushes and snaps. Faithful restore rather than a workaround.
            //
            // The trade is that these are real objects, not ghosts, so we own their teardown.
            HashSet<ZDO> before = SnapshotInstances();
            try {
                instance.m_location.m_prefab.Load();
                zs.SpawnLocation(instance.m_location, seed, position, rotation, ZoneSystem.SpawnMode.Full, null);
            } finally {
                // Nothing here should be ghost-initing, but the flag is a global static and a throw
                // inside vanilla would otherwise turn every subsequent spawn in the game into a ghost.
                ZNetView.FinishGhostInit();
                report.LocationSpawned = DestroyNewInstances(before);
                instance.m_location.m_prefab.Release();
            }

            // SpawnLocation always creates its own LocationProxy, and ClearLocation deliberately
            // preserves the existing one (IsStructural), so without this every reset would leave two
            // proxies behind -- each of which client-spawns a full copy of the location. Carry the
            // timestamp onto the new one and retire the old, and only now that the spawn has
            // succeeded: failing with the old proxy still in place leaves the location identifiable
            // and retryable rather than invisible.
            ZDO newProxy = FindLocationProxy(zone, locationHash, oldProxyId);
            if (newProxy != null) {
                TakeOwnership(newProxy);
                newProxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
                DestroyZdo(proxy);
            } else {
                // No replacement appeared; keep the original as the timestamp carrier.
                Logger.LogLocationResetWarning($"Zone {zone.x},{zone.y}: '{entry.Name}' respawned without a new " +
                    $"LocationProxy; keeping the existing one.");
                TakeOwnership(proxy);
                proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
            }

            report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.Rebuilt);
            report.LocationCleared = cleared;
        }

        // ZDOs that currently have a live GameObject. Used to bracket a Full-mode spawn so exactly the
        // objects it created can be torn down again -- a location's children routinely land in
        // neighbouring sectors that ZoneLoader.Release never visits, and blanket-releasing those would
        // risk destroying live instances around a player in an adopted chunk.
        private static HashSet<ZDO> SnapshotInstances() {
            HashSet<ZDO> snapshot = new HashSet<ZDO>();
            if (ZNetScene.instance == null) { return snapshot; }
            foreach (KeyValuePair<ZDO, ZNetView> kvp in ZNetScene.instance.m_instances) {
                snapshot.Add(kvp.Key);
            }
            return snapshot;
        }

        // Destroy the GameObjects created since the snapshot, keeping their ZDOs. Returns the count,
        // which is what the location actually respawned.
        private static int DestroyNewInstances(HashSet<ZDO> before) {
            if (ZNetScene.instance == null) { return 0; }

            List<ZNetView> fresh = new List<ZNetView>();
            foreach (KeyValuePair<ZDO, ZNetView> kvp in ZNetScene.instance.m_instances) {
                if (before.Contains(kvp.Key)) { continue; }
                if (kvp.Value != null) { fresh.Add(kvp.Value); }
            }

            for (int i = 0; i < fresh.Count; i++) {
                ZNetView view = fresh[i];
                ZDO zdo = view.GetZDO();
                // Vanilla's unload sequence: detach so the ZDO (and the position SnappAll just wrote
                // into it) survives, then destroy the GameObject. The LocationProxy parents its
                // client-spawned prefab tree, so that whole tree goes with it.
                view.ResetZDO();
                Object.Destroy(view.gameObject);
                if (zdo != null) { ZNetScene.instance.m_instances.Remove(zdo); }
            }
            return fresh.Count;
        }

        // Poll vanilla's own readiness gate for this chunk's location prefab. PokeCanSpawnLocation both
        // reports readiness and registers the load request, so calling it repeatedly is what drives the
        // load to completion. No-op for chunks with no configured location.
        private static IEnumerator WaitForLocationPrefab(Vector2i zone, float maxWaitSeconds) {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) { yield break; }
            if (zs.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { yield break; }
            if (instance.m_location == null) { yield break; }
            if (LocationResetData.TryGetLocationEntry(instance.m_location.Hash, out LocationResetData.ResolvedResetEntry entry) == false) { yield break; }
            if (entry.Enabled == false) { yield break; }

            float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, maxWaitSeconds);
            while (Time.realtimeSinceStartup < deadline) {
                if (zs.PokeCanSpawnLocation(instance.m_location, true)) { yield break; }
                // This wait can outlast the 4s TTL on a poked zone that has no live instances yet.
                ZoneLoader.KeepAlive(zone);
                yield return null;
            }
            // Fall through on timeout: the reset still runs, and a deferred proxy spawn is a cosmetic
            // placement problem rather than a reason to abandon a half-cleared location.
            Logger.LogLocationResetWarning($"Zone {zone.x},{zone.y}: location prefab '{instance.m_location.m_prefabName}' " +
                $"did not finish loading within {maxWaitSeconds}s; its contents may be placed against unshaped terrain.");
        }

        // Every terrain reset has to run against LIVE TerrainComp/TerrainModifier components, which do
        // not exist in a chunk we merely poke-loaded (see ZoneLoader.CreateTerrainObjects). This is the
        // only sanctioned way to call TerrainResetter from the sweep. Synchronous by design: yielding
        // between create and destroy would let ZNetScene's 30Hz reaper tear the objects out from under
        // us mid-reset.
        private static int ResetTerrainLive(Vector2i zone, Vector3 position, float radius) {
            List<Vector2i> zones = TerrainZonesFor(zone);
            for (int i = 0; i < zones.Count; i++) { ZoneLoader.KeepAlive(zones[i]); }

            List<ZNetView> terrainObjects = ZoneLoader.CreateTerrainObjects(zones);
            try {
                return TerrainResetter.Reset(position, radius);
            } finally {
                ZoneLoader.DestroyTerrainObjects(terrainObjects);
            }
        }

        // The chunk itself plus any neighbour an extra terrain radius reaches into.
        private static List<Vector2i> TerrainZonesFor(Vector2i zone) {
            List<Vector2i> zones = new List<Vector2i>() { zone };
            List<Vector2i> extra = ExtraTerrainZones(zone);
            if (extra != null) { zones.AddRange(extra); }
            return zones;
        }

        // Base radius plus the configured extra, clamped.
        //
        // The clamp is not arbitrary: ScanZone covers the chunk and its 8 neighbours (+/-96m from the
        // chunk centre) and a location sits within 32m of that centre, so 64m is the furthest reach
        // the protection scan provably checked for player property. Past it we would be flattening
        // ground nobody looked at.
        private static float TerrainRadiusFor(LocationResetData.ResolvedResetEntry entry, float exteriorRadius) {
            float baseRadius = entry.TerrainRadius > 0f ? entry.TerrainRadius : exteriorRadius;
            float extra = Mathf.Clamp(entry.ExtraTerrainRadius, 0f, LocationResetData.MaxExtraTerrainRadius);
            return baseRadius + extra;
        }

        // Chunks a location's terrain reset reaches into beyond its own, so RegenerateZone can
        // poke-load them first. TerrainResetter only touches heightmaps that are actually loaded
        // (Heightmap.FindHeightmap and TerrainComp.FindTerrainCompiler both need live components), so
        // without this an extra radius that crosses a chunk boundary silently does nothing on the far
        // side. Empty in the common case, since ExtraTerrainRadius defaults to 0.
        internal static List<Vector2i> ExtraTerrainZones(Vector2i zone) {
            List<Vector2i> extra = null;
            if (ZoneSystem.instance == null) { return null; }
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { return null; }
            if (instance.m_location == null) { return null; }
            if (LocationResetData.TryGetLocationEntry(instance.m_location.Hash, out LocationResetData.ResolvedResetEntry entry) == false) { return null; }
            if (entry.Enabled == false) { return null; }
            // TerrainOnly always resets terrain; every other mode only does so when asked.
            if (entry.Mode != LocationResetMode.TerrainOnly && entry.ResetTerrain == false) { return null; }
            if (entry.ExtraTerrainRadius <= 0f) { return null; }

            float radius = TerrainRadiusFor(entry, instance.m_location.m_exteriorRadius);
            Vector3 position = instance.m_position;
            Vector2i min = ZoneSystem.GetZone(new Vector3(position.x - radius, 0f, position.z - radius));
            Vector2i max = ZoneSystem.GetZone(new Vector3(position.x + radius, 0f, position.z + radius));

            for (int x = min.x; x <= max.x; x++) {
                for (int y = min.y; y <= max.y; y++) {
                    if (x == zone.x && y == zone.y) { continue; }
                    if (extra == null) { extra = new List<Vector2i>(); }
                    extra.Add(new Vector2i(x, y));
                }
            }
            return extra;
        }

        internal static ZDO FindLocationProxy(Vector2i zone, int locationHash) {
            return FindLocationProxy(zone, locationHash, ZDOID.None);
        }

        // exclude lets the caller skip a known proxy, which is how the freshly-spawned one is picked
        // out from the one being retired.
        internal static ZDO FindLocationProxy(Vector2i zone, int locationHash, ZDOID exclude) {
            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(zone, zdoBuffer);
            ZDO found = null;
            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (zdo.m_prefab != ZoneProtectionScan.LocationProxyHash) { continue; }
                if (zdo.GetInt(ZDOVars.s_location, 0) != locationHash) { continue; }
                if (exclude != ZDOID.None && zdo.m_uid == exclude) { continue; }
                found = zdo;
                break;
            }
            zdoBuffer.Clear();
            return found;
        }

        // Destroy the location's contents so SpawnLocation can lay them down fresh.
        //
        // The exterior scan covers the zone AND its 8 neighbours, because a location's radius
        // routinely crosses a zone boundary -- Upgrade World's single-sector scan is a documented
        // source of leftover objects on the far side of the line.
        //
        // The interior of a sky dungeon shares its entrance's sector (ZoneSystem.GetZone is xz-only
        // and vanilla parks the interior directly overhead). It is cleared too, since SpawnLocation
        // regenerates it via DungeonGenerator.Generate -- and vanilla's own DungeonGenerator.Clear
        // only destroys the generator's children, while the interior's contents are instantiated
        // unparented, so this clear is the ONLY thing that removes the previous interior.
        private static int ClearLocation(Vector2i zone, Vector3 center, Quaternion rotation,
                                         float exteriorRadius, ZoneSystem.ZoneLocation location,
                                         LocationResetData.ResolvedResetEntry entry, ZoneResetReport report) {
            // ShouldPreserve classifies against these, and it must not depend on the zone scan two
            // modules away having run first. Idempotent; early-outs on a bool once built.
            ZoneProtectionScan.BuildPrefabSets();

            List<ZDO> doomed = new List<ZDO>();
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    CollectClearable(new Vector2i(zone.x + dx, zone.y + dy), zone, center, exteriorRadius,
                        entry.ResetInterior, entry, doomed);
                }
            }

            // Before CollectSpawnedCreatures, so a spawner picked up here has its creature taken with
            // it by that pass rather than being orphaned.
            report.SpawnersRemoved = CollectStraySpawners(center, rotation, location, doomed);

            CollectSpawnedCreatures(doomed);

            for (int i = 0; i < doomed.Count; i++) { DestroyZdo(doomed[i]); }
            return doomed.Count;
        }

        // Spawners the radius sweep above could not reach.
        //
        // A location's children are placed at locationPos + locationRot * childOffset, so we know
        // exactly where each one went and can match the ZDO sitting at that spot. That precision is
        // the point: an earlier version instead widened the clear to the prefab's furthest child,
        // which is outlier-driven -- one distant child inflated the disc and the reset destroyed
        // everything else standing near the entrance along with it.
        //
        // Only spawners, because only they compound. A leftover spawner beside a fresh one doubles
        // the spawn rate, and a one-shot CreatureSpawner keeps its "already fired" state on its own
        // ZDO, so the replacement fires again while the original's creature is still standing.
        private static int CollectStraySpawners(Vector3 center, Quaternion rotation,
                                                ZoneSystem.ZoneLocation location, List<ZDO> doomed) {
            List<SpawnerChild> children = SpawnerChildrenFor(location);
            if (children == null || children.Count == 0) { return 0; }

            int found = 0;
            for (int i = 0; i < children.Count; i++) {
                Vector3 expected = center + (rotation * children[i].LocalOffset);
                ZDO hit = FindZdoAt(expected, children[i].PrefabHash);
                if (hit == null || doomed.Contains(hit)) { continue; }
                doomed.Add(hit);
                found++;
            }
            return found;
        }

        // The ZDO of a given prefab standing at a given spot, or null.
        //
        // XZ only: SnapToGround and StaticPhysics both rewrite a spawned object's Y into its ZDO
        // after placement, while X and Z are exactly what SpawnLocation computed. One sector is
        // enough -- the expected position determines which sector it must be in.
        private static ZDO FindZdoAt(Vector3 expected, int prefabHash) {
            if (ZDOMan.instance == null) { return null; }

            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(ZoneSystem.GetZone(expected), zdoBuffer);

            ZDO best = null;
            float bestSqr = DuplicateNodeEpsilon * DuplicateNodeEpsilon;
            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (zdo.m_prefab != prefabHash) { continue; }
                Vector3 pos = zdo.GetPosition();
                float dx = pos.x - expected.x;
                float dz = pos.z - expected.z;
                float sqr = (dx * dx) + (dz * dz);
                if (sqr > bestSqr) { continue; }
                bestSqr = sqr;
                best = zdo;
            }

            zdoBuffer.Clear();
            return best;
        }

        // One spawner in a location's prefab: which prefab it is, and where it sits relative to the
        // location root. Enough to reconstruct exactly where SpawnLocation put it.
        private struct SpawnerChild {
            internal int PrefabHash;
            internal Vector3 LocalOffset;
        }

        // Components whose leftovers compound. A stale one of these beside a freshly spawned one
        // doubles the spawn rate, which is why they are worth chasing outside the clear radius while
        // ordinary scenery is not.
        //
        // Deliberately not included: EggHatch and EggGrow destroy themselves, SpawnOnDamaged rides on
        // a destructible the normal clear already handles, DungeonGenerator is covered by the sky
        // clear, and SoftReferencePrefabSpawner renames and reparents itself at Awake so its world
        // object cannot be matched by position at all.
        private static bool IsSpawnerChild(GameObject child) {
            return child.GetComponent<CreatureSpawner>() != null
                || child.GetComponent<SpawnArea>() != null
                || child.GetComponent<TriggerSpawner>() != null
                || child.GetComponent<SpawnPrefab>() != null
                || child.GetComponent<LootSpawner>() != null
                || child.GetComponent<WispSpawner>() != null;
        }

        // A location prefab's spawner children, cached per location type -- a prefab's layout cannot
        // change within a session, and loading the asset to walk it is not free.
        private static readonly Dictionary<int, List<SpawnerChild>> spawnerChildrenByLocation = new Dictionary<int, List<SpawnerChild>>();

        private static List<SpawnerChild> SpawnerChildrenFor(ZoneSystem.ZoneLocation location) {
            int hash;
            // ZoneLocation.Hash resolves m_prefab.Name, which throws for an unassigned soft reference.
            try { hash = location.Hash; }
            catch (System.Exception) { return null; }

            if (spawnerChildrenByLocation.TryGetValue(hash, out List<SpawnerChild> cached)) { return cached; }

            List<SpawnerChild> children = new List<SpawnerChild>();
            bool loaded = false;
            try {
                location.m_prefab.Load();
                loaded = true;
                GameObject asset = location.m_prefab.Asset;
                if (asset != null) {
                    // Inactive children included: RandomSpawn culls children per spawn without ever
                    // moving them, so one disabled in the asset right now can still be placed on the
                    // next rebuild and has to be in this list.
                    ZNetView[] views = asset.GetComponentsInChildren<ZNetView>(true);
                    for (int i = 0; i < views.Length; i++) {
                        if (views[i] == null) { continue; }
                        GameObject child = views[i].gameObject;
                        if (IsSpawnerChild(child) == false) { continue; }
                        children.Add(new SpawnerChild() {
                            // Utils.GetPrefabName is what ZNetView.Awake hashes into the ZDO, so this
                            // matches whatever the spawned object ends up carrying. It truncates at
                            // the first '(' or ' ', which collapses Unity's "Foo (1)" duplicates onto
                            // one hash -- harmless here because the position match disambiguates.
                            PrefabHash = Utils.GetPrefabName(child.name).GetStableHashCode(),
                            // Relative to the root: the asset sits wherever the loader left it, and
                            // vanilla zeroes the root transform before placing children.
                            LocalOffset = asset.transform.InverseTransformPoint(child.transform.position),
                        });
                    }
                }
            } catch (System.Exception e) {
                // An empty list rather than a retry: the clear still runs on its radius, and a
                // location whose asset will not load is not one we can reason about anyway.
                Logger.LogLocationResetWarning($"Could not read the spawner layout of '{location.m_prefabName}'; " +
                    $"stray spawners outside its radius will not be cleared: {e.Message}");
                children.Clear();
            } finally {
                if (loaded) { location.m_prefab.Release(); }
            }

            spawnerChildrenByLocation[hash] = children;
            return children;
        }

        // Does this zone have a sky interior at all? Any ZDO parked above the sky threshold in the
        // location's own sector is one: a zone hosts at most one location, and vanilla confines an
        // interior to its entrance's zone.
        //
        // Deliberately NOT keyed on finding a DungeonGenerator. Only procedurally generated dungeons
        // have one, so troll caves and other hand-built interiors were never detected -- their sky
        // contents were skipped entirely while SpawnLocation kept laying down another copy, which is
        // how one troll cave ended up with 18 treasure chests and 9 one-shot Spawner_Troll.
        private static bool HasSkyInterior(Vector2i zone) {
            if (ZDOMan.instance == null) { return false; }

            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(zone, zdoBuffer);
            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (zdo.GetPosition().y <= ZoneProtectionScan.SkyThreshold) { continue; }
                zdoBuffer.Clear();
                return true;
            }
            zdoBuffer.Clear();
            return false;
        }

        private static void CollectClearable(Vector2i sector, Vector2i locationZone, Vector3 center,
                                             float exteriorRadius, bool clearInterior,
                                             LocationResetData.ResolvedResetEntry entry, List<ZDO> doomed) {
            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(sector, zdoBuffer);

            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (IsStructural(zdo) || IsPlayer(zdo) || IsTamed(zdo)) { continue; }

                Vector3 origin = OriginOf(zdo);
                bool inSky = origin.y > ZoneProtectionScan.SkyThreshold;

                if (inSky) {
                    if (clearInterior == false) { continue; }
                    // The whole sky column of the location's OWN zone, which is the footprint vanilla
                    // confines an interior to. Scoped to that zone rather than the swept 3x3 on
                    // purpose: the neighbours' sky belongs to their own locations.
                    if (ZoneSystem.GetZone(origin) != locationZone) { continue; }
                } else {
                    if (Utils.DistanceXZ(origin, center) > exteriorRadius) { continue; }
                }

                if (ShouldPreserve(zdo, entry)) { continue; }

                doomed.Add(zdo);
            }

            zdoBuffer.Clear();
        }

        // Where a ZDO came from rather than where it is now. Only creatures differ: BaseAI writes its
        // spawn point once on first spawn and never moves it, so this is the location that placed the
        // creature even after it has wandered. Static content has no spawn point and reads back its
        // own position, so nothing about props or terrain changes.
        //
        // Testing the live position instead is why creatures accumulated: a location's creatures are
        // direct children of its prefab, so every SpawnLocation lays down a fresh set, while the old
        // ones had already strayed past a 20m m_exteriorRadius and survived the clear.
        private static Vector3 OriginOf(ZDO zdo) {
            return zdo.GetVec3(ZDOVars.s_spawnPoint, zdo.GetPosition());
        }

        // Pets never get cleared, including one part-way through being tamed. A tamed creature also
        // blocks the whole zone via the TamedCreature protection category, so this is a second line of
        // defence rather than the primary guard.
        //
        // TameTimeLeft is a FLOAT on the ZDO (Tameable.cs writes it with Set(float) and reads it with
        // GetFloat) -- reading it as a long compiles but always returns the default, silently. It is
        // only written while taming is under way, so absent means "not being tamed".
        private static bool IsTamed(ZDO zdo) {
            return zdo.GetBool(ZDOVars.s_tamed, false) || zdo.GetFloat(ZDOVars.s_tameTimeLeft, 0f) > 0f;
        }

        // Take each doomed spawner's creature with it. A one-shot CreatureSpawner records what it
        // spawned as a ZDO connection rather than a key, and that connection is its entire "already
        // fired" memory -- so destroying the spawner alone re-arms the location: the replacement has
        // no connection and spawns again while the original creature is still standing.
        //
        // This also reaches creatures that wandered clean out of the swept block, which the
        // spawn-point test cannot.
        private static void CollectSpawnedCreatures(List<ZDO> doomed) {
            if (ZDOMan.instance == null) { return; }

            // Snapshot the count: the loop appends, and a spawned creature is never itself a spawner.
            int spawnerCount = doomed.Count;
            for (int i = 0; i < spawnerCount; i++) {
                ZDO zdo = doomed[i];
                if (zdo == null || ZoneProtectionScan.CreatureSpawnerHashes.Contains(zdo.m_prefab) == false) { continue; }

                ZDOID spawnedId = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                if (spawnedId == ZDOID.None) { continue; }

                ZDO spawned = ZDOMan.instance.GetZDO(spawnedId);
                if (spawned == null || spawned.IsValid() == false) { continue; }
                if (IsPlayer(spawned) || IsTamed(spawned)) { continue; }
                if (doomed.Contains(spawned)) { continue; }

                doomed.Add(spawned);
            }
        }

        // The zone controller and terrain compiler ARE the zone, and the location proxy is the
        // identity and timestamp carrier for the location we are resetting.
        private static bool IsStructural(ZDO zdo) {
            return zdo.m_prefab == ZoneProtectionScan.ZoneCtrlHash
                || zdo.m_prefab == ZoneProtectionScan.TerrainCompilerHash
                || zdo.m_prefab == ZoneProtectionScan.LocationProxyHash;
        }

        private static bool IsPlayer(ZDO zdo) {
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetZDOID() == zdo.m_uid) { return true; }
            if (ZNet.instance == null || ZNet.instance.m_peers == null) { return false; }
            for (int i = 0; i < ZNet.instance.m_peers.Count; i++) {
                if (ZNet.instance.m_peers[i].m_characterID == zdo.m_uid) { return true; }
            }
            return false;
        }

        // Whether the protection policy says this object survives the clear.
        //
        // Classifies properly rather than asking only about PlayerBuiltPiece behind a creator check,
        // which is why Preserve never worked: TryClassify recognises Tombstone, Ward and DroppedItem
        // with NO creator test, and a dropped item's creator is always 0, so every one of them fell
        // through and was destroyed despite DroppedItem shipping as Preserve.
        //
        // This is also the only place a per-entry or per-group Protection override takes effect. The
        // zone scan runs with entry: null (it is zone-wide, before any location is chosen), so it can
        // only ever consult Defaults.
        private static bool ShouldPreserve(ZDO zdo, LocationResetData.ResolvedResetEntry entry) {
            // Fails closed ahead of everything else, matching WarnOnProtectionConflicts' promise that
            // ProtectedPrefabs beats an ignore list.
            if (LocationResetData.ExtraProtectedPrefabHashes.Contains(zdo.m_prefab)) { return true; }
            if (entry == null) { return false; }
            if (ZoneProtectionScan.TryClassify(zdo, out ProtectionCategory category) == false) { return false; }

            // An ignore list means "treat this as ordinary content", so it beats the category action.
            if (entry.Ignores(category, zdo.m_prefab)) { return false; }

            // Block and Preserve both mean "do not destroy this". A Block should have aborted the zone
            // long before the clear, but the scan judged it against Defaults while this judges it
            // against the entry's own rules, so the two can legitimately disagree -- and the safe side
            // of that disagreement is keeping the object.
            return entry.ActionFor(category) != ProtectionAction.Ignore;
        }

        // Seizing ownership first is what makes the delete network-authoritative. Vanilla's
        // ZNetScene.Destroy only tears down the ZDO when the caller owns it; without this the object
        // merely despawns locally and another peer re-broadcasts it.
        private static void DestroyZdo(ZDO zdo) {
            if (zdo == null || zdo.IsValid() == false) { return; }
            TakeOwnership(zdo);
            if (ZNetScene.instance != null && ZNetScene.instance.m_instances.TryGetValue(zdo, out ZNetView view) && view != null) {
                ZNetScene.instance.Destroy(view.gameObject);
                return;
            }
            ZDOMan.instance.DestroyZDO(zdo);
        }

        // Tier 2. Re-runs vanilla PlaceVegetation with only the due prefabs enabled.
        //
        // PlaceVegetation seeds each entry with
        //   worldSeed + zoneX*4271 + zoneY*9187 + prefabName.GetStableHashCode()
        // so replaying it for one prefab in one zone reproduces that prefab's ORIGINAL positions,
        // rotations and scales exactly. That determinism is what makes a restore possible, and it is
        // also why surviving instances must NOT be pre-deleted.
        //
        // It does mean the replay re-places EVERY node, including the ones still standing, so the
        // duplicates have to be rejected afterwards -- see RejectDuplicateGhosts. Vanilla's own
        // defence is ZoneSystem.IsBlocked, a downward Physics.Raycast, and that cannot work here:
        // ZoneLoader poke-loads the chunk, which for an already-generated zone runs SpawnMode.Client
        // and creates only the zone root and its Heightmap. Existing ZDOs get GameObjects solely from
        // ZNetScene.CreateObjectsAll, around ZNet.GetReferencePosition() -- Vector3.zero on a
        // dedicated server. So a poke-loaded chunk has no vegetation colliders, IsBlocked is always
        // false, and m_blockCheck is a no-op no matter what it is set to.
        private static void RegenerateVegetation(Vector2i zone, LocationResetConfigSnapshot cfg, bool force, ZoneResetReport report) {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs.m_vegetation == null || zs.m_vegetation.Count == 0) { return; }
            if (zs.m_zones.TryGetValue(zone, out ZoneSystem.ZoneData zoneData) == false || zoneData?.m_root == null) { return; }

            Heightmap heightmap = zoneData.m_root.GetComponentInChildren<Heightmap>();
            if (heightmap == null) { return; }

            List<ZoneSystem.ZoneVegetation> due = SelectDueVegetation(zone, force, report, out List<int> dueHashes);
            if (due.Count == 0) { return; }

            // Clear ignored pieces before placing anything. PlaceVegetation's block check treats any
            // live collider as an obstruction, so a campfire parked on an ore spawn silently stops
            // that node from ever returning -- the chunk resets, the node does not. Deliberately only
            // runs when something is actually about to regenerate here.
            report.IgnoredPiecesCleared += SweepIgnoredPieces(zone);

            List<ZoneSystem.ZoneVegetation> original = zs.m_vegetation;
            List<GameObject> ghosts = new List<GameObject>();
            Vector3 zonePos = ZoneSystem.GetZonePos(zone);

            // Captured BEFORE placing: a ghost's ZDO joins the sector index the moment it is created,
            // so a snapshot taken afterwards would match every ghost against itself.
            Dictionary<int, List<Vector2>> surviving = ZoneProtectionScan.TrackedVegetationPositions(zone);
            List<GameObject> kept = new List<GameObject>();

            try {
                zs.m_vegetation = due;
                zs.m_tempClearAreas.Clear();
                AddLocationClearArea(zone, zs.m_tempClearAreas);

                // Ghost mode creates the ZDOs but leaves the GameObjects unregistered, which is how
                // vanilla pre-generates zones. The objects are throwaway; the ZDOs are the result.
                zs.PlaceVegetation(zone, zonePos, zoneData.m_root.transform, heightmap,
                    zs.m_tempClearAreas, ZoneSystem.SpawnMode.Ghost, ghosts);

                // Inside the try on purpose: the finally below destroys the ghost GameObjects, and
                // reaching a ghost's ZDO needs its ZNetView.
                report.VegetationDuplicatesRejected += RejectDuplicateGhosts(ghosts, surviving, kept);
            } finally {
                // Restoring the shared vegetation list is not optional: leaving the filtered list in
                // place would break normal world generation for the rest of the session.
                zs.m_vegetation = original;
                zs.m_tempClearAreas.Clear();
                // Destroy in finally so a throw mid-loop cannot leak orphan GameObjects holding
                // live ZDOs.
                for (int i = 0; i < ghosts.Count; i++) {
                    if (ghosts[i] != null) { Object.Destroy(ghosts[i]); }
                }
            }

            if (kept.Count > 0) {
                report.VegetationObjects += kept.Count;
                report.VegetationEntriesReset += due.Count;
                // Only the kept ghosts: a rejected duplicate sits on a node that never went away, so
                // re-flattening the ground under it would undo terrain nobody touched.
                ApplyVegetationTerrainReset(zone, dueHashes, kept, report);
            }

            // Stamp only the time. The real per-prefab counts come from RecordBaseline once the reset
            // is known to have finished; writing a baseline here would be guessing, and writing 0
            // (as this once did) reads downstream as "nothing missing" and freezes the entry out.
            for (int i = 0; i < dueHashes.Count; i++) {
                LocationResetState.StampEntryTime(zone, dueHashes[i]);
            }
        }

        // Vegetation placement is seeded per prefab per zone, so replaying it re-places every node --
        // including the ones that never went away. Anything landing on a surviving node is a
        // duplicate and its ZDO is dropped again immediately.
        //
        // Matching is XZ only: the replayed X/Z come straight from the seeded RNG and are identical
        // to the original, while y is re-snapped to terrain that may have changed since.
        //
        // A match CONSUMES the survivor it paired with, which matters because PlaceVegetation puts
        // some prefabs down in tight groups: two distinct nodes of one prefab really can sit within
        // the epsilon of each other. Without consuming, a single survivor could absorb both its own
        // replay and its destroyed neighbour's, and that neighbour would never come back. Pairing
        // greedily bounds rejections by the number of survivors, so the worst case is a duplicate
        // slipping through rather than a node going missing forever.
        private const float DuplicateNodeEpsilon = 0.25f;

        private static int RejectDuplicateGhosts(List<GameObject> ghosts,
                                                 Dictionary<int, List<Vector2>> surviving,
                                                 List<GameObject> kept) {
            int rejected = 0;
            float sqrEpsilon = DuplicateNodeEpsilon * DuplicateNodeEpsilon;

            for (int i = 0; i < ghosts.Count; i++) {
                GameObject ghost = ghosts[i];
                if (ghost == null) { continue; }
                ZNetView view = ghost.GetComponent<ZNetView>();
                ZDO zdo = view != null ? view.GetZDO() : null;
                if (zdo == null) { kept.Add(ghost); continue; }

                Vector3 position = zdo.GetPosition();
                if (TryConsumeSurvivingNodeAt(surviving, zdo.m_prefab, position, sqrEpsilon) == false) {
                    kept.Add(ghost);
                    continue;
                }

                // Ghost-mode objects are never registered with ZNetScene, so this takes the
                // ZDOMan.DestroyZDO branch. The GameObject itself is torn down by the caller's
                // finally along with every other ghost.
                DestroyZdo(zdo);
                rejected++;
            }
            return rejected;
        }

        // Claims the nearest surviving node within the epsilon and removes it from the pool, so no two
        // replayed nodes can pair with the same survivor.
        private static bool TryConsumeSurvivingNodeAt(Dictionary<int, List<Vector2>> surviving, int prefabHash,
                                                      Vector3 position, float sqrEpsilon) {
            if (surviving.TryGetValue(prefabHash, out List<Vector2> nodes) == false) { return false; }

            Vector2 xz = new Vector2(position.x, position.z);
            int best = -1;
            float bestSqr = sqrEpsilon;
            for (int i = 0; i < nodes.Count; i++) {
                float sqr = (nodes[i] - xz).sqrMagnitude;
                if (sqr <= bestSqr) { bestSqr = sqr; best = i; }
            }
            if (best < 0) { return false; }

            // Order is irrelevant here, so swap-remove rather than shuffling the tail down.
            nodes[best] = nodes[nodes.Count - 1];
            nodes.RemoveAt(nodes.Count - 1);
            return true;
        }

        // Destroy every ignored prefab in a chunk's own sector. Ignored prefabs are by definition not
        // player property worth keeping, and leaving them standing defeats the regeneration.
        private static int SweepIgnoredPieces(Vector2i zone) {
            if (ZDOMan.instance == null) { return 0; }

            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(zone, zdoBuffer);

            // Collected first, destroyed after: DestroyZdo mutates ZDOMan's sector index, so deleting
            // while iterating the buffer it just filled would skip entries.
            List<ZDO> doomed = null;
            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                // An explicitly protected prefab still wins, matching the fail-closed rule in TryClassify.
                if (LocationResetData.ExtraProtectedPrefabHashes.Contains(zdo.m_prefab)) { continue; }
                // Ignoring a creature prefab must not extend to somebody's pet. This sweep has no
                // radius limit, so without the guard an ignore list could clear tames chunk-wide.
                if (IsTamed(zdo)) { continue; }
                if (LocationResetData.AnyCategoryIgnores(zdo.m_prefab) == false) { continue; }
                if (doomed == null) { doomed = new List<ZDO>(); }
                doomed.Add(zdo);
            }
            zdoBuffer.Clear();

            if (doomed == null) { return 0; }
            for (int i = 0; i < doomed.Count; i++) { DestroyZdo(doomed[i]); }
            return doomed.Count;
        }

        // Clone the vegetation list with only the due entries enabled. Cloning matters because
        // PlaceVegetation reads m_enable off the shared entries; mutating the originals in place
        // would corrupt world generation.
        private static List<ZoneSystem.ZoneVegetation> SelectDueVegetation(Vector2i zone, bool force, ZoneResetReport report, out List<int> dueHashes) {
            List<ZoneSystem.ZoneVegetation> due = new List<ZoneSystem.ZoneVegetation>();
            dueHashes = new List<int>();

            foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation) {
                if (veg?.m_prefab == null) { continue; }
                int hash = veg.m_prefab.name.GetStableHashCode();
                if (LocationResetData.TryGetVegetationEntry(hash, out LocationResetData.ResolvedResetEntry entry) == false) { continue; }
                entry = entry.ForDistance(ZoneRates.DistanceFor(zone));
                if (entry.Enabled == false) { continue; }

                if (force == false && LocationResetState.TryGetEntry(zone, hash, out LocationResetState.EntryRecord record)) {
                    long now = LocationResetState.Now;
                    if (record.Stamp > 0 && entry.IsDue(record.Stamp, now, report.RateMultiplier) == false) {
                        report.VegetationEntriesSkipped++;
                        if (report.Verbose) {
                            float elapsedHours = (now - record.Stamp) / 3600f;
                            report.Detail($"vegetation '{entry.Name}' skipped - not due ({elapsedHours:0.#}h elapsed, " +
                                $"schedule {entry.DescribeSchedule(now, report.RateMultiplier)})");
                        }
                        continue;
                    }
                    // Nothing missing here, nothing to regenerate.
                    if (record.Baseline == 0) {
                        report.VegetationEntriesSkipped++;
                        report.Detail($"vegetation '{entry.Name}' skipped - nothing missing (baseline 0)");
                        continue;
                    }
                }

                ZoneSystem.ZoneVegetation clone = veg.Clone();
                clone.m_enable = true;
                // Without the block check vanilla would happily stack a fresh copy on top of the
                // surviving one every single reset. Force it on rather than growing the world.
                if (clone.m_blockCheck == false) {
                    Logger.LogLocationReset($"Vegetation '{veg.m_prefab.name}' has no block check; forcing it on to prevent duplicate placement.");
                    clone.m_blockCheck = true;
                }
                due.Add(clone);
                dueHashes.Add(hash);
            }

            return due;
        }

        // Mining leaves a crater. For entries configured with ResetTerrain, flatten it back around
        // each regenerated node.
        private static void ApplyVegetationTerrainReset(Vector2i zone, List<int> dueHashes, List<GameObject> ghosts, ZoneResetReport report) {
            bool anyTerrain = false;
            for (int i = 0; i < dueHashes.Count; i++) {
                if (LocationResetData.TryGetVegetationEntry(dueHashes[i], out LocationResetData.ResolvedResetEntry entry) && entry.ResetTerrain) {
                    anyTerrain = true;
                    break;
                }
            }
            if (anyTerrain == false) { return; }

            // One create/destroy around the whole loop rather than per node: the terrain objects are
            // the same for every crater in this chunk, and the bracket has to stay yield-free anyway.
            List<Vector2i> zones = TerrainZonesFor(zone);
            for (int i = 0; i < zones.Count; i++) { ZoneLoader.KeepAlive(zones[i]); }
            List<ZNetView> terrainObjects = ZoneLoader.CreateTerrainObjects(zones);
            try {
                for (int i = 0; i < ghosts.Count; i++) {
                    GameObject ghost = ghosts[i];
                    if (ghost == null) { continue; }
                    int hash = Utils.GetPrefabName(ghost).GetStableHashCode();
                    if (LocationResetData.TryGetVegetationEntry(hash, out LocationResetData.ResolvedResetEntry entry) == false) { continue; }
                    if (entry.ResetTerrain == false) { continue; }
                    float radius = entry.TerrainRadius > 0f ? entry.TerrainRadius : 8f;
                    report.TerrainModificationsUndone += TerrainResetter.Reset(ghost.transform.position, radius);
                }
            } finally {
                ZoneLoader.DestroyTerrainObjects(terrainObjects);
            }
        }

        // Vegetation must not spawn inside a location footprint. Vanilla builds these clear areas
        // during PlaceLocations; since we are calling PlaceVegetation on its own, rebuild them.
        private static void AddLocationClearArea(Vector2i zone, List<ZoneSystem.ClearArea> clearAreas) {
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { return; }
            if (instance.m_location == null || instance.m_location.m_clearArea == false) { return; }
            clearAreas.Add(new ZoneSystem.ClearArea(instance.m_position, instance.m_location.m_exteriorRadius));
        }
    }
}
