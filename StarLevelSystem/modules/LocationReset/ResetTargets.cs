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
                // A targeted reset refreshes only what it was aimed at. Without this a request to
                // reset one crypt would also re-grow every berry bush and re-fill every ore vein
                // sharing its chunk.
                if (cfg.TargetPrefabHash != 0 && prefab != cfg.TargetPrefabHash) { continue; }

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
        // sls-loc-reset would still silently honour every per-prefab timestamp.
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
            List<Vector2i> extraZones = ExtraTerrainZones(zone, cfg);
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
            yield return WaitForLocationPrefab(zone, cfg, cfg.MaxZoneLoadWaitSeconds);

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
                // What this chunk owes the vegetation replay, settled before anything is touched.
                // Selection reads the configuration and the state file only -- never the live world --
                // so resolving it up here cannot change what it picks, and it is what gates the
                // ignored-piece sweep below.
                VegetationPlan plan = PlanVegetation(zone, cfg, force, report);

                // BEFORE the rebuild, never after. This sweep exists to clear the chunk's ignored
                // litter out of PlaceVegetation's way; running it after RegenerateLocation meant every
                // piece the location had just re-spawned that happened to sit on an ignore list was
                // deleted again on the spot. A server ignoring wood_floor and woodwall so abandoned
                // decking could not freeze its chunks got every world house rebuilt without walls or
                // floors, once per cycle, for as long as the config stood.
                if (plan != null) { report.IgnoredPiecesCleared += SweepIgnoredPieces(zone); }

                // Vanilla's own ordering: locations first so vegetation sees the fresh clear areas.
                RegenerateLocation(zone, cfg, force, report);
                if (plan != null) { RegenerateVegetation(zone, cfg, plan, report); }
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
                    $"(before {zdosBefore}, after {zdosAfter}). Reset kept; check sls-loc-audit if this persists.");
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
            // No record for the miss above: most chunks in the world hold no location at all, and
            // saying so on every one of them is noise. A registered instance with no definition is a
            // different matter -- that is abnormal, and used to vanish without a trace.
            if (instance.m_location == null) {
                report.RecordLocation(null, ZoneResetReport.LocationOutcome.NoInstance);
                return;
            }

            int locationHash = instance.m_location.Hash;
            string locationName = instance.m_location.m_prefabName;

            // A targeted reset walks a square of chunks to find its location, so most of the chunks
            // it visits hold something else. Recorded rather than silent so the log shows the search
            // actually looked here.
            if (cfg.TargetPrefabHash != 0 && locationHash != cfg.TargetPrefabHash) {
                report.RecordLocation(locationName, ZoneResetReport.LocationOutcome.NotTargeted);
                return;
            }

            // An explicit request for this location by name overrides the configuration lookup. The
            // caller named it, so "no group covers it" is not an answer -- see TargetOverride.
            if (cfg.TargetPrefabHash == locationHash && cfg.TargetOverride != null) {
                RegenerateLocationWith(zone, cfg, force, report, instance, locationHash, cfg.TargetOverride);
                return;
            }

            if (LocationResetData.TryGetLocationEntry(locationHash, out LocationResetData.ResolvedResetEntry entry) == false) {
                // Recorded, not Detail'd. This was the one outcome that could hide behind the debug
                // flag, and the line an admin saw instead -- "no configured targets in this chunk" --
                // reads as "there is nothing here", which is the opposite of what it means. A forced
                // reset that does nothing to a named location has to say so.
                report.RecordLocation(locationName, ZoneResetReport.LocationOutcome.NotConfigured);
                return;
            }
            entry = entry.ForDistance(ZoneRates.DistanceFor(zone));
            if (entry.Enabled == false) {
                report.GroupName = entry.GroupName;
                report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.Disabled);
                return;
            }
            RegenerateLocationWith(zone, cfg, force, report, instance, locationHash, entry);
        }

        // The reset itself, once a governing entry has been settled on. Split out so an explicitly
        // targeted request can supply its own resolution (cfg.TargetOverride) and still run through
        // exactly this code -- the clear, the Full-mode respawn, the proxy carry-over and the ZDO
        // bookkeeping below are where every hard-won invariant in this system lives, and a second
        // copy of them for the targeted path would drift.
        private static void RegenerateLocationWith(Vector2i zone, LocationResetConfigSnapshot cfg, bool force,
                                                   ZoneResetReport report, ZoneSystem.LocationInstance instance,
                                                   int locationHash,
                                                   LocationResetData.ResolvedResetEntry entry) {
            ZoneSystem zs = ZoneSystem.instance;
            report.GroupName = entry.GroupName;

            // Hard-blocked locations are never resettable, no matter the config, a force command, or
            // an API caller naming one outright.
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
                int undone = ResetTerrainLive(zone, cfg, position, terrainRadius);
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
            //
            // Ownership stamps do NOT unlock a surface-only reset here, however precisely they could
            // now separate the interior from the exterior. The blocker was never identifying the
            // interior; it is that SpawnLocation re-runs DungeonGenerator.Generate unconditionally, so
            // any rebuild produces a second one whatever the clear did or did not touch.
            if (entry.ResetInterior == false && hasInterior) {
                if (entry.ResetTerrain) { report.TerrainModificationsUndone += ResetTerrainLive(zone, cfg, position, terrainRadius); }
                TakeOwnership(proxy);
                proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
                report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.InteriorPreserved);
                return;
            }

            ZDOID oldProxyId = proxy.m_uid;
            // A zone hosts at most one location and never moves, so its coordinates are this
            // location's identity -- the same key the spawn stamps onto everything it creates.
            long ownerKey = LocationOwnership.KeyFor(zone);
            int cleared = ClearLocation(zone, position, rotation, exteriorRadius, instance.m_location, entry, ownerKey, report);
            // A negative count means the clear refused and destroyed nothing (see ClearLocation).
            // Abandon before the terrain reset and the respawn, and leave the proxy un-stamped so the
            // next pass retries -- clearing and rebuilding are one operation, never two.
            if (cleared < 0) { return; }
            if (entry.ResetTerrain) { report.TerrainModificationsUndone += ResetTerrainLive(zone, cfg, position, terrainRadius); }

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
            List<ZDO> fresh = new List<ZDO>();
            try {
                instance.m_location.m_prefab.Load();
                zs.SpawnLocation(instance.m_location, seed, position, rotation, ZoneSystem.SpawnMode.Full, null);
            } finally {
                // Nothing here should be ghost-initing, but the flag is a global static and a throw
                // inside vanilla would otherwise turn every subsequent spawn in the game into a ghost.
                ZNetView.FinishGhostInit();
                report.LocationSpawned = DestroyNewInstances(before, fresh);
                instance.m_location.m_prefab.Release();
            }

            report.SparedDuplicatesRemoved = RejectSparedDuplicates(zone, fresh, ownerKey);

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
                // The proxy is created inside the bracketed spawn, so it should already carry the
                // stamp. If it does not, the ownership context and this reset disagree about which
                // zone the location is in -- and the consequence is silent: every future reset would
                // find nothing stamped and quietly fall back to the radius rule. Say so, then repair
                // it, so the failure shows up in the log rather than as tree loss creeping back.
                if (LocationOwnership.IsOwnedBy(newProxy, ownerKey) == false) {
                    Logger.LogLocationResetWarning($"Zone {zone.x},{zone.y}: '{entry.Name}' respawned without " +
                        "an ownership stamp; tagging it directly.");
                    LocationOwnership.Tag(newProxy, ownerKey);
                }
                DestroyZdo(proxy);
            } else {
                // No replacement appeared; keep the original as the timestamp carrier.
                Logger.LogLocationResetWarning($"Zone {zone.x},{zone.y}: '{entry.Name}' respawned without a new " +
                    $"LocationProxy; keeping the existing one.");
                TakeOwnership(proxy);
                proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
            }

            report.DoorsSealed = SealKeyedDoors(zone, position, exteriorRadius);
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
        //
        // fresh collects those surviving ZDOs. It is exactly the set the spawn produced, which is what
        // RejectSparedDuplicates needs and what nothing else can reconstruct afterwards -- a location's
        // children are indistinguishable from anything else in the sector index once the frame ends.
        private static int DestroyNewInstances(HashSet<ZDO> before, List<ZDO> fresh) {
            if (ZNetScene.instance == null) { return 0; }

            List<ZNetView> views = new List<ZNetView>();
            foreach (KeyValuePair<ZDO, ZNetView> kvp in ZNetScene.instance.m_instances) {
                if (before.Contains(kvp.Key)) { continue; }
                if (kvp.Value != null) { views.Add(kvp.Value); }
            }

            for (int i = 0; i < views.Count; i++) {
                ZNetView view = views[i];
                ZDO zdo = view.GetZDO();
                // Vanilla's unload sequence: detach so the ZDO (and the position SnappAll just wrote
                // into it) survives, then destroy the GameObject. The LocationProxy parents its
                // client-spawned prefab tree, so that whole tree goes with it.
                view.ResetZDO();
                Object.Destroy(view.gameObject);
                if (zdo != null) {
                    ZNetScene.instance.m_instances.Remove(zdo);
                    fresh.Add(zdo);
                }
            }
            return views.Count;
        }

        // A survivor the new rules spared can be one the rebuild puts straight back: a world tree
        // standing where the location's own tree child goes, or a stray the ownership stamp did not
        // cover. Left alone, that pair accumulates one copy per reset cycle, forever -- the same
        // failure mode RejectDuplicateGhosts exists to prevent for the vegetation replay.
        //
        // The FRESH copy wins here and the survivor is destroyed, which is the opposite of the
        // vegetation replay's rule. Deliberate: a location reset exists to restore pristine content,
        // so the pristine copy is the one to keep, while a vegetation replay is re-placing nodes that
        // never went away and must leave the originals standing.
        //
        // Same 0.25m XZ epsilon and the same consume-the-match rule as TryConsumeSurvivingNodeAt, for
        // the same reasons: SnapToGround rewrites y after placement while X and Z are exact, and
        // without consuming, one survivor could absorb two fresh copies and leave a real duplicate.
        private static int RejectSparedDuplicates(Vector2i zone, List<ZDO> fresh, long ownerKey) {
            if (ZDOMan.instance == null || fresh.Count == 0) { return 0; }

            HashSet<ZDO> spawned = new HashSet<ZDO>(fresh);
            Dictionary<int, List<ZDO>> survivors = new Dictionary<int, List<ZDO>>();

            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    zdoBuffer.Clear();
                    ZDOMan.instance.FindObjects(new Vector2i(zone.x + dx, zone.y + dy), zdoBuffer);

                    for (int i = 0; i < zdoBuffer.Count; i++) {
                        ZDO zdo = zdoBuffer[i];
                        if (zdo == null || zdo.IsValid() == false) { continue; }
                        if (spawned.Contains(zdo)) { continue; }
                        if (IsStructural(zdo) || IsPlayer(zdo) || IsTamed(zdo)) { continue; }
                        // A player's own copy is never a duplicate to collapse, whatever it sits on.
                        if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L) { continue; }
                        // Anything still carrying our stamp is a clear that failed, not a duplicate.
                        // Destroying it here would hide that; leave it for the ZDO drift accounting.
                        if (LocationOwnership.IsOwnedBy(zdo, ownerKey)) { continue; }

                        if (survivors.TryGetValue(zdo.m_prefab, out List<ZDO> list) == false) {
                            list = new List<ZDO>();
                            survivors[zdo.m_prefab] = list;
                        }
                        list.Add(zdo);
                    }
                }
            }
            zdoBuffer.Clear();
            if (survivors.Count == 0) { return 0; }

            int removed = 0;
            float sqrEpsilon = DuplicateNodeEpsilon * DuplicateNodeEpsilon;
            for (int i = 0; i < fresh.Count; i++) {
                ZDO copy = fresh[i];
                if (copy == null || copy.IsValid() == false) { continue; }
                if (survivors.TryGetValue(copy.m_prefab, out List<ZDO> nodes) == false) { continue; }

                Vector3 position = copy.GetPosition();
                int best = -1;
                float bestSqr = sqrEpsilon;
                for (int n = 0; n < nodes.Count; n++) {
                    Vector3 other = nodes[n].GetPosition();
                    float ox = other.x - position.x;
                    float oz = other.z - position.z;
                    float sqr = (ox * ox) + (oz * oz);
                    if (sqr <= bestSqr) { bestSqr = sqr; best = n; }
                }
                if (best < 0) { continue; }

                ZDO survivor = nodes[best];
                // Order is irrelevant, so swap-remove rather than shuffling the tail down.
                nodes[best] = nodes[nodes.Count - 1];
                nodes.RemoveAt(nodes.Count - 1);
                DestroyZdo(survivor);
                removed++;
            }
            return removed;
        }

        // Poll vanilla's own readiness gate for this chunk's location prefab. PokeCanSpawnLocation both
        // reports readiness and registers the load request, so calling it repeatedly is what drives the
        // load to completion. No-op for chunks with no configured location.
        // The entry that will actually govern this chunk's location on this pass, or false when
        // nothing will touch it.
        //
        // Every pre-flight step -- waiting for the prefab, loading terrain neighbours -- has to reach
        // the same verdict RegenerateLocation will, including the targeting filter and the ad-hoc
        // override an explicitly named request supplies. Resolving it independently in each of them
        // is how a targeted reset ends up skipping the prefab wait it needed, or poke-loading
        // neighbours for a location it is about to pass over.
        private static bool GoverningLocation(Vector2i zone, LocationResetConfigSnapshot cfg,
                                              out ZoneSystem.LocationInstance instance,
                                              out LocationResetData.ResolvedResetEntry entry) {
            instance = default(ZoneSystem.LocationInstance);
            entry = null;
            if (ZoneSystem.instance == null) { return false; }
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out instance) == false) { return false; }
            if (instance.m_location == null) { return false; }

            int hash = instance.m_location.Hash;
            if (cfg.TargetPrefabHash != 0 && hash != cfg.TargetPrefabHash) { return false; }
            if (cfg.TargetPrefabHash == hash && cfg.TargetOverride != null) {
                entry = cfg.TargetOverride;
                return true;
            }

            if (LocationResetData.TryGetLocationEntry(hash, out entry) == false) { return false; }
            entry = entry.ForDistance(ZoneRates.DistanceFor(zone));
            if (entry.Enabled == false) { entry = null; return false; }
            return true;
        }

        private static IEnumerator WaitForLocationPrefab(Vector2i zone, LocationResetConfigSnapshot cfg, float maxWaitSeconds) {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) { yield break; }
            if (GoverningLocation(zone, cfg, out ZoneSystem.LocationInstance instance, out _) == false) { yield break; }

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
        private static int ResetTerrainLive(Vector2i zone, LocationResetConfigSnapshot cfg, Vector3 position, float radius) {
            List<Vector2i> zones = TerrainZonesFor(zone, cfg);
            for (int i = 0; i < zones.Count; i++) { ZoneLoader.KeepAlive(zones[i]); }

            List<ZNetView> terrainObjects = ZoneLoader.CreateTerrainObjects(zones);
            try {
                return TerrainResetter.Reset(position, radius);
            } finally {
                ZoneLoader.DestroyTerrainObjects(terrainObjects);
            }
        }

        // The chunk itself plus any neighbour an extra terrain radius reaches into.
        private static List<Vector2i> TerrainZonesFor(Vector2i zone, LocationResetConfigSnapshot cfg) {
            List<Vector2i> zones = new List<Vector2i>() { zone };
            List<Vector2i> extra = ExtraTerrainZones(zone, cfg);
            if (extra != null) { zones.AddRange(extra); }
            return zones;
        }

        // Base radius plus the configured extra, clamped.
        //
        // The clamp is not arbitrary: a location sits within 32m of the chunk centre and the
        // protection scan checked out to Defaults.ProtectionRadius, so ProtectionRadius - 32m is the
        // furthest extra reach it provably covered. Past it we would be flattening ground nobody
        // looked at. Both move together by construction -- see LocationResetData.MaxExtraTerrainRadius
        // -- so tightening ProtectionRadius to recover reset coverage cannot silently outrun it.
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
        internal static List<Vector2i> ExtraTerrainZones(Vector2i zone, LocationResetConfigSnapshot cfg) {
            List<Vector2i> extra = null;
            if (GoverningLocation(zone, cfg, out ZoneSystem.LocationInstance instance,
                                  out LocationResetData.ResolvedResetEntry entry) == false) { return null; }
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
                                         LocationResetData.ResolvedResetEntry entry, long ownerKey,
                                         ZoneResetReport report) {
            // ShouldPreserve classifies against these, and it must not depend on the zone scan two
            // modules away having run first. Idempotent; early-outs on a bool once built.
            ZoneProtectionScan.BuildPrefabSets();

            // The vegetation guard below is only as good as the catalogue behind it, and an empty one
            // classifies every tree as "not vegetation" -- which is the blanket delete this guard
            // exists to end. Refuse the clear outright rather than proceed on a catalogue that cannot
            // answer. Returns before anything is destroyed, so the caller can abandon the whole
            // regeneration; a half-cleared location is the one outcome this system must never produce.
            if (LocationResetData.WorldCatalogReady == false) {
                report.RecordLocation(entry.Name, ZoneResetReport.LocationOutcome.CatalogNotReady);
                return -1;
            }

            List<ZDO> doomed = new List<ZDO>();
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    CollectClearable(new Vector2i(zone.x + dx, zone.y + dy), zone, center, exteriorRadius,
                        entry.ResetInterior, entry, ownerKey, report, doomed);
                }
            }

            // Before CollectSpawnedCreatures, so a spawner picked up here has its creature taken with
            // it by that pass rather than being orphaned.
            report.SpawnersRemoved = CollectStraySpawners(center, rotation, location, doomed);

            CollectSpawnedCreatures(zone, doomed, report);

            for (int i = 0; i < doomed.Count; i++) { DestroyZdo(doomed[i]); }
            return doomed.Count;
        }

        // Force every world-generated keyed entrance in the location's footprint back to sealed.
        //
        // A Door with an m_keyItem is the one piece of location state a rebuild can silently carry
        // forward. Door.CanInteract refuses every interaction once m_keyItem is set and state != 0,
        // so an opened Sunken Crypt gate or Queen's citadel door can never be closed again by any
        // in-game means -- only a fresh ZDO, or this write, re-seals it.
        //
        // Runs AFTER the respawn rather than in place of the clear, and is idempotent: a faithful
        // clear+respawn already leaves a fresh door carrying no "state" key at all, so a healthy
        // rebuild reports 0 here and any non-zero count is a stale door that outlived the clear.
        //
        // Same footprint rule as CollectClearable on purpose -- a door outside it belongs to
        // something this reset is not responsible for. The creator gate matches RefreshContainerLoot:
        // vanilla has no player-buildable keyed door, but a mod may, and a player's own lock is never
        // ours to change.
        private static int SealKeyedDoors(Vector2i zone, Vector3 center, float exteriorRadius) {
            if (ZDOMan.instance == null || ZoneProtectionScan.KeyedDoorHashes.Count == 0) { return 0; }

            int resealed = 0;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    zdoBuffer.Clear();
                    ZDOMan.instance.FindObjects(new Vector2i(zone.x + dx, zone.y + dy), zdoBuffer);

                    for (int i = 0; i < zdoBuffer.Count; i++) {
                        ZDO zdo = zdoBuffer[i];
                        if (zdo == null || zdo.IsValid() == false) { continue; }
                        if (ZoneProtectionScan.KeyedDoorHashes.Contains(zdo.m_prefab) == false) { continue; }
                        if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L) { continue; }

                        Vector3 origin = OriginOf(zdo);
                        if (origin.y > ZoneProtectionScan.SkyThreshold) {
                            if (ZoneSystem.GetZone(origin) != zone) { continue; }
                        } else if (Utils.DistanceXZ(origin, center) > exteriorRadius) {
                            continue;
                        }

                        if (zdo.GetInt(ZDOVars.s_state, 0) == 0) { continue; }
                        TakeOwnership(zdo);
                        zdo.Set(ZDOVars.s_state, 0);
                        resealed++;
                    }
                }
            }

            zdoBuffer.Clear();
            return resealed;
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
                                             LocationResetData.ResolvedResetEntry entry, long ownerKey,
                                             ZoneResetReport report, List<ZDO> doomed) {
            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(sector, zdoBuffer);

            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (IsStructural(zdo) || IsPlayer(zdo) || IsTamed(zdo)) { continue; }

                // Ownership first, because it is an answer rather than a guess. Everything below it
                // is the inference the stamp replaces, kept for content that predates the stamp.
                long owner = LocationOwnership.OwnerOf(zdo);
                if (owner == ownerKey) {
                    // Ours wherever it stands, with no radius test at all. This REACHES FURTHER than
                    // the old rule as well as narrower: DungeonGenerator lays CampRadial perimeter
                    // sections at a radius it picks, not the one ZoneLocation declares, so those
                    // routinely sat outside m_exteriorRadius -- surviving every clear while the
                    // rebuild laid down another set beside them, every cycle.
                    if (ShouldPreserve(zdo, entry)) { continue; }
                    doomed.Add(zdo);
                    report.OwnedCleared++;
                    continue;
                }
                if (owner != LocationOwnership.NoOwner) {
                    // Stamped, but by somebody else. Two locations' 3x3 blocks overlap routinely, and
                    // this reset cannot rebuild a neighbour's content -- destroying it would lose it
                    // for good, which is exactly what the radius rule has been doing.
                    report.ForeignOwnedSkipped++;
                    continue;
                }

                // Unstamped: content that predates the stamp, or debris a player or a client created
                // inside the footprint (chopped-tree stubbe, a LootSpawner's item piles, a wandering
                // SpawnSystem creature whose spawn point sits here). The radius rule still governs it.
                // Deliberately NOT dropped in favour of "stamped only": nothing else collects that
                // debris, and it would pile up in every location forever.
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

                    // World generation really does plant trees inside a location's radius. Vanilla
                    // only suppresses vegetation for the location registered in the zone being
                    // generated (ZoneSystem.PlaceLocations builds its ClearArea list from that zone's
                    // own LocationInstance, and InsideClearArea is a square test besides), so a
                    // NEIGHBOURING zone's vegetation pass has no idea our radius reaches into it and
                    // plants there anyway. Every location whose radius crosses a zone boundary has
                    // world trees inside it.
                    //
                    // Those trees are not the location's to remove: the rebuild does not re-place them
                    // (SpawnLocation only lays down the location's own children) and the Tier 2 replay
                    // will not either, because AddLocationClearArea now excludes this very disc. So
                    // clearing them destroyed them for good, one stand per reset cycle.
                    //
                    // Surface branch only. In the sky column there is no world vegetation to protect,
                    // while a dungeon room prefab may legitimately contain a rock or pickable that
                    // also appears in m_vegetation -- sparing one there would leave it standing while
                    // SpawnLocation's unconditional DungeonGenerator.Generate lays down another,
                    // stacking a second interior on the first.
                    if (LocationResetData.IsWorldVegetation(zdo.m_prefab)) {
                        report.VegetationPreserved++;
                        continue;
                    }
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
        private static void CollectSpawnedCreatures(Vector2i zone, List<ZDO> doomed, ZoneResetReport report) {
            if (ZDOMan.instance == null) { return; }

            // Snapshot the count: the loop appends, and a spawned creature is never itself a spawner.
            int spawnerCount = doomed.Count;
            List<ZDO> spawners = new List<ZDO>();

            for (int i = 0; i < spawnerCount; i++) {
                ZDO zdo = doomed[i];
                if (zdo == null) { continue; }
                // Every spawner type, for the SLS link below. The vanilla connection follow that
                // follows is narrower because only CreatureSpawner has one.
                if (ZoneProtectionScan.SpawnerHashes.Contains(zdo.m_prefab)) { spawners.Add(zdo); }
                if (ZoneProtectionScan.CreatureSpawnerHashes.Contains(zdo.m_prefab) == false) { continue; }

                ZDOID spawnedId = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                if (spawnedId == ZDOID.None) { continue; }

                ZDO spawned = ZDOMan.instance.GetZDO(spawnedId);
                if (spawned == null || spawned.IsValid() == false) { continue; }
                if (IsPlayer(spawned) || IsTamed(spawned)) { continue; }
                if (doomed.Contains(spawned)) { continue; }

                doomed.Add(spawned);
            }

            // Everything vanilla's single connection cannot express: a SpawnArea nest's whole brood, a
            // TriggerSpawner's ambush, and any of them that wandered out of the swept block. See
            // SpawnerLinks -- this runs after the vanilla follow so the two cannot double-count.
            report.LinkedCreaturesRemoved = SpawnerLinks.CollectLinked(spawners, zone, doomed);
        }

        // IsPlayer and IsTamed together, for callers outside this file. SpawnerLinks follows a link to
        // a creature that can be anywhere in the world, so it needs the same two guards the radius
        // sweep applies to everything it finds.
        internal static bool IsProtectedLiving(ZDO zdo) {
            return IsPlayer(zdo) || IsTamed(zdo);
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
        // which is why Preserve never worked: TryClassify recognises Tombstone and DroppedItem with
        // NO creator test, and a dropped item's creator is always 0, so every one of them fell
        // through and was destroyed despite DroppedItem shipping as Preserve.
        // (Ward used to be creator-free here too, which is what let world-generated
        // dverger_guardstone block resets; it is gated on a creator now.)
        //
        // Per-entry and per-group Protection overrides apply here AND at the zone gate: the zone scan
        // judges against the zone's governing entries (ZoneProtectionScan.GoverningEntries), while
        // this judges each object against the specific entry being cleared. The gate combines entries
        // fail-closed, so the two can still disagree -- and the safe side wins there too.
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

        // Everything the vegetation replay needs, resolved before the location rebuild touches the
        // chunk. Split out from RegenerateVegetation so the ignored-piece sweep can be gated on
        // "something really is about to regenerate here" while still running ahead of the rebuild.
        private sealed class VegetationPlan {
            internal ZoneSystem.ZoneData ZoneData;
            internal Heightmap Heightmap;
            internal List<ZoneSystem.ZoneVegetation> Due;
            internal List<int> DueHashes;
        }

        // null when this chunk has no vegetation work, which is also the signal that neither the
        // sweep nor the replay should run.
        //
        // SelectDueVegetation reports skipped entries into the report as it goes, so this must be
        // called exactly once per pass -- hence the plan being carried rather than re-derived.
        private static VegetationPlan PlanVegetation(Vector2i zone, LocationResetConfigSnapshot cfg, bool force, ZoneResetReport report) {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null || zs.m_vegetation == null || zs.m_vegetation.Count == 0) { return null; }
            if (zs.m_zones.TryGetValue(zone, out ZoneSystem.ZoneData zoneData) == false || zoneData?.m_root == null) { return null; }

            Heightmap heightmap = zoneData.m_root.GetComponentInChildren<Heightmap>();
            if (heightmap == null) { return null; }

            List<ZoneSystem.ZoneVegetation> due = SelectDueVegetation(zone, cfg, force, report, out List<int> dueHashes);
            if (due.Count == 0) { return null; }

            return new VegetationPlan() { ZoneData = zoneData, Heightmap = heightmap, Due = due, DueHashes = dueHashes };
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
        private static void RegenerateVegetation(Vector2i zone, LocationResetConfigSnapshot cfg, VegetationPlan plan, ZoneResetReport report) {
            ZoneSystem zs = ZoneSystem.instance;
            // Re-validated rather than trusted. The plan is resolved before the location rebuild runs,
            // and a chunk that lost its zone root in between would take PlaceVegetation down with it.
            if (zs == null || plan == null || plan.ZoneData?.m_root == null || plan.Heightmap == null) { return; }

            ZoneSystem.ZoneData zoneData = plan.ZoneData;
            Heightmap heightmap = plan.Heightmap;
            List<ZoneSystem.ZoneVegetation> due = plan.Due;
            List<int> dueHashes = plan.DueHashes;

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
                ApplyVegetationTerrainReset(zone, cfg, dueHashes, kept, report);
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

        // Destroy the chunk's ignored litter, so PlaceVegetation is not blocked by a campfire parked
        // on a spawn node. Runs ahead of the location rebuild -- see the call site.
        //
        // "Ignored" is an exemption from a PROTECTION category, so an object only qualifies here if it
        // actually classifies as the category that names it, which for every player category means it
        // carries a creator. Matching on the bare prefab hash instead -- which is what this did, via
        // AnyCategoryIgnores -- applied a PlayerBuiltPiece exemption to objects that were never player
        // built: a server ignoring wood_floor and woodwall so abandoned decking could not freeze its
        // chunks had the floors and walls stripped out of every world-generated house in range as
        // well, and they stayed gone unless the location itself happened to be a configured, due
        // reset target.
        //
        // Location-owned content is skipped ahead of that on its own terms. It is not litter by
        // definition, and it is the rebuild's to manage -- ClearLocation destroys exactly the stamped
        // set and puts it straight back.
        private static int SweepIgnoredPieces(Vector2i zone) {
            if (ZDOMan.instance == null) { return 0; }
            // TryClassify reads these, and this now runs in chunks with no location at all, so it
            // cannot ride on ClearLocation having built them first. Idempotent.
            ZoneProtectionScan.BuildPrefabSets();

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
                // A location's own content, wherever it stands. The clear owns it, not this.
                if (LocationOwnership.OwnerOf(zdo) != LocationOwnership.NoOwner) { continue; }
                // Unclassified means it is not player property under any category, so no ignore list
                // can be exempting it from one -- world-generated scenery, in other words.
                if (ZoneProtectionScan.TryClassify(zdo, out ProtectionCategory category) == false) { continue; }
                // Per category, matching ShouldPreserve: listing a prefab under PlayerBuiltPiece must
                // not make it sweepable as a Tombstone.
                if (LocationResetData.DefaultIgnores(category, zdo.m_prefab) == false) { continue; }
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
        // Prefab hashes already reported as dormant, so the explanation is logged once per world rather
        // than once per chunk across an eighty-thousand-chunk sweep.
        private static readonly HashSet<int> LoggedDisabledVegetation = new HashSet<int>();

        internal static void ResetVegetationDiagnostics() {
            LoggedDisabledVegetation.Clear();
        }

        private static List<ZoneSystem.ZoneVegetation> SelectDueVegetation(Vector2i zone, LocationResetConfigSnapshot cfg,
                                                                           bool force, ZoneResetReport report, out List<int> dueHashes) {
            List<ZoneSystem.ZoneVegetation> due = new List<ZoneSystem.ZoneVegetation>();
            dueHashes = new List<int>();

            foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation) {
                if (veg?.m_prefab == null) { continue; }
                int hash = veg.m_prefab.name.GetStableHashCode();
                // A targeted reset regrows only what it named. Filtering here rather than skipping
                // RegenerateVegetation wholesale is what lets a mod target a single vegetation
                // prefab -- a berry bush, one ore type -- and not just a location.
                if (cfg.TargetPrefabHash != 0 && hash != cfg.TargetPrefabHash) { continue; }

                // An entry vanilla ships DISABLED is one world generation never places, so there is
                // nothing here to restore and anything we place is something this world has never had.
                // ZoneSystem keeps such entries in m_vegetation as dormant data -- cut content, or
                // content that moved into a location prefab -- and PlaceVegetation skips them on
                // exactly this flag.
                //
                // This used to be overridden a few lines down (clone.m_enable = true), which turned
                // every reset into a generator of prefabs the world was never meant to contain:
                // GlowingMushroom scattered across the Mistlands, one full quota per chunk, in blocks
                // that held none at all beforehand.
                if (veg.m_enable == false) {
                    if (LoggedDisabledVegetation.Add(hash)) {
                        Logger.LogLocationReset($"Vegetation '{veg.m_prefab.name}' is disabled in this world's " +
                            $"ZoneSystem, so world generation never places it; skipping it rather than creating it.");
                    }
                    continue;
                }

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

                // Enabled by definition now -- only enabled entries reach here -- so the clone needs
                // no m_enable write. It used to get one unconditionally, which is what resurrected
                // vanilla's dormant entries; see the guard above.
                ZoneSystem.ZoneVegetation clone = veg.Clone();
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
        private static void ApplyVegetationTerrainReset(Vector2i zone, LocationResetConfigSnapshot cfg, List<int> dueHashes, List<GameObject> ghosts, ZoneResetReport report) {
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
            List<Vector2i> zones = TerrainZonesFor(zone, cfg);
            for (int i = 0; i < zones.Count; i++) { ZoneLoader.KeepAlive(zones[i]); }
            List<ZNetView> terrainObjects = ZoneLoader.CreateTerrainObjects(zones);
            try {
                // Batch every crater into one TerrainResetter pass: the per-node form re-ran the full
                // per-compiler vertex sweep and TerrainComp.Save() once per node, all in this frame.
                List<Vector3> resetCenters = new List<Vector3>();
                List<float> resetRadii = new List<float>();
                for (int i = 0; i < ghosts.Count; i++) {
                    GameObject ghost = ghosts[i];
                    if (ghost == null) { continue; }
                    int hash = Utils.GetPrefabName(ghost).GetStableHashCode();
                    if (LocationResetData.TryGetVegetationEntry(hash, out LocationResetData.ResolvedResetEntry entry) == false) { continue; }
                    if (entry.ResetTerrain == false) { continue; }
                    float radius = entry.TerrainRadius > 0f ? entry.TerrainRadius : 8f;
                    resetCenters.Add(ghost.transform.position);
                    resetRadii.Add(radius);
                }
                report.TerrainModificationsUndone += TerrainResetter.ResetBatch(resetCenters, resetRadii);
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
