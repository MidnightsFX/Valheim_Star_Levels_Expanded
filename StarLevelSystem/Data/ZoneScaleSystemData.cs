using StarLevelSystem.common;
using StarLevelSystem.modules;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using YamlDotNet.Core;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data {
    internal static class ZoneScaleSystemData {

        // What a zone-data load found. NoData and Unreadable must stay distinct: only the first is
        // safe for the rebuild to overwrite. Collapsing them is how a single parse failure used to
        // destroy a world's zone progression permanently.
        internal enum ZoneLoadResult { Loaded, NoData, Unreadable }

        internal static List<ZoneData> Zones = new List<ZoneData>();
        // Spatial index: world is partitioned into IndexCellSize buckets; each bucket lists the
        // (few) zones whose bounding box overlaps it. Point lookup hashes to one bucket and scans
        // its small list instead of every zone. Geometry is fixed once zones are finalized, so the
        // index is rebuilt only when the Zones list is fully (re)assigned.
        private const float IndexCellSize = 500f;
        private static Dictionary<long, List<ZoneData>> zoneIndex = new Dictionary<long, List<ZoneData>>();
        private static Dictionary<int, ZoneData> zoneById = new Dictionary<int, ZoneData>();
        private static ZoneData lastZone = null;

        // Zone-level updates that arrived from the authority before this (client) peer finished building
        // its zone geometry. Applied once BuildZoneIndex runs.
        private static readonly Dictionary<int, int> pendingLevelUpdates = new Dictionary<int, int>();

        // flush data signal
        private static bool zonesDirty = false;
        internal static bool zonesBuilt = false;
        internal static bool buildingZones = false;
        private static bool decayRunning = false;
        internal static bool overlayAvailable = false;
        // How often the decay coroutine polls. Decay magnitude is time-based, so this only
        // controls check frequency, not the effective rate (see ZoneDecayLevelsPerHour).
        private const float DecayTickIntervalSeconds = 900f; // 15 minutes
        internal static int overlayUpdates = 0;
        // Handle for the running decay loop so it can be stopped on world unload (TaskRunner is
        // DontDestroyOnLoad, so a running loop would otherwise leak across worlds).
        private static Coroutine decayCoroutine = null;

        // The clock the in-memory LastDecayTimestamp values are expressed in. NOT the same thing as
        // the configured clock: the two differ between a config change and the re-base that follows
        // it, and writing the configured value into the save file while the stamps are still in the
        // other clock is exactly what would make the next load trust them and floor every zone.
        private static ZoneDecayClockSource stampedClock = ZoneDecayClockSource.RealTime;
        // Wall-clock stamps are unix seconds (>1e9 since 2001); net time counts seconds the world has
        // actually been played and would need ~32 years of continuous play to reach that. A stamp's
        // magnitude therefore identifies the clock that wrote it on its own, which is what lets a
        // hand-edited, missing or stale DecayClock marker be caught instead of taken at face value.
        private const double WallClockFloor = 1000000000d;

        // Zone identity is derived from grid-cell coordinates so it is stable across independent
        // builds (authority load vs. client rebuild) and network syncs line up. Sequential build
        // order is non-deterministic between peers; cell coordinates are a pure function of world
        // position + MaxZoneSize. Cell indices are tiny (worldSize 10000 / MaxZoneSize >= 1000 ->
        // <= ~20 per axis), so this packing never overflows an int.
        private const int ZoneIdStride = 100000;

        internal static int ZoneIdForCell(int cellX, int cellZ) => cellX * ZoneIdStride + cellZ;

        // Recovers the cell coordinates from a zone's min corner (MinX = -worldSize + cellX * cellSize)
        // and packs them into the same id used at build time. Used to normalize ids loaded from disk.
        internal static int ZoneIdForBounds(float minX, float minZ) {
            float cellSize = ValConfig.MaxZoneSize.Value;
            int cellX = Mathf.RoundToInt((minX + WorldGenerator.worldSize) / cellSize);
            int cellZ = Mathf.RoundToInt((minZ + WorldGenerator.worldSize) / cellSize);
            return ZoneIdForCell(cellX, cellZ);
        }


        // Authority-only: aggregate a batch of deaths into zone kill counts and levels. Level-ups are
        // computed by threshold crossing so a batch that jumps past an exact multiple still levels up.
        internal static void ApplyDeaths(List<SerializableVector3> deaths) {
            if (!zonesBuilt) { return; }
            // StampNow dereferences ZNet in GameTime mode. Both callers already run with a live ZNet,
            // but this is the one entrypoint where that is not visible from here.
            if (ZNet.instance == null) { return; }
            int threshold = ValConfig.ZoneKillsPerLevelUp.Value;
            HashSet<ZoneData> leveledZones = new HashSet<ZoneData>();
            foreach (var pos in deaths) {
                ZoneData zone = ZoneScaleSystemData.GetZoneForPosition(pos);
                if (zone == null) { continue; }
                int oldKills = zone.TotalKills;
                zone.TotalKills = oldKills + 1;
                if (threshold > 0) {
                    int levelsGained = (zone.TotalKills / threshold) - (oldKills / threshold);
                    if (levelsGained > 0) {
                        zone.ZoneLevel += levelsGained;
                        // Restart the decay countdown: a kill-driven level-up is recent activity, so
                        // decay should measure inactivity from here (otherwise a stale build-time
                        // timestamp would let the next decay pass instantly undo the level-up).
                        zone.LastDecayTimestamp = StampNow();
                        leveledZones.Add(zone);
                    }
                }
            }
            if (deaths.Count > 0) { ZoneScaleSystemData.zonesDirty = true; }
            if (leveledZones.Count > 0) {
                Logger.LogDebug($"{leveledZones.Count} zone(s) leveled up from a batch of {deaths.Count} kills.");
                ZoneScaleSystemData.BroadcastZoneLevels(leveledZones);
                ZoneScaleSystem.DrawMinimapOverlay();
            }
        }

        internal static void StartDecayCoroutine() {
            if (decayRunning) { return; }
            // Decay and persistence are authority-only; clients receive level changes via sync.
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            decayRunning = true;
            decayCoroutine = TaskRunner.Run().StartCoroutine(DecayZoneLevels());
        }

        // Tears down all in-memory zone state and stops the decay loop so a world/server switch
        // starts clean. Flushes any unsaved authority changes first (no-op off the server).
        internal static void ResetState() {
            FlushPendingSave();
            if (decayCoroutine != null) { TaskRunner.Run().StopCoroutine(decayCoroutine); decayCoroutine = null; }
            Zones = new List<ZoneData>();
            zoneIndex = new Dictionary<long, List<ZoneData>>();
            zoneById = new Dictionary<int, ZoneData>();
            pendingLevelUpdates.Clear();
            lastZone = null;
            zonesBuilt = false;
            buildingZones = false;
            overlayAvailable = false;
            decayRunning = false;
            zonesDirty = false;
            stampedClock = ZoneDecayClockSource.RealTime;
        }

        // Configured clock, resolved defensively. AcceptableValueList constrains the local file, but
        // this value also arrives over the config sync from another peer's build, so an unrecognised
        // string falls back to the legacy wall clock rather than throwing.
        private static ZoneDecayClockSource ConfiguredClock() {
            return ValConfig.ZoneDecayClock.Value == ZoneDecayClockSource.GameTime.ToString()
                ? ZoneDecayClockSource.GameTime : ZoneDecayClockSource.RealTime;
        }

        // ZNet.GetTimeSeconds is the world's net time: real seconds, but only counted while the world
        // is being played (ZNet.UpdateNetTime returns early on a server with no players), and
        // persisted with the world save. So an hour of it is an hour somebody was actually online for.
        //
        // Deliberately no null-ZNet fallback: every caller runs with a live ZNet, and a silent 0 here
        // would collide with the "no usable stamp" sentinel the decay loop keys on.
        private static double ClockNow(ZoneDecayClockSource clock) {
            return clock == ZoneDecayClockSource.GameTime
                ? ZNet.instance.GetTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        // Stamps are always written in whatever clock the rest of the set is already in. The decay
        // loop is the only place that changes clocks, and it re-bases every zone at once when it does.
        internal static double StampNow() { return ClockNow(stampedClock); }

        // Fresh geometry carries no history, so it simply adopts whatever clock is configured now.
        internal static void StampNewZones(List<ZoneData> zones) {
            stampedClock = ConfiguredClock();
            double now = ClockNow(stampedClock);
            foreach (var zone in zones) { zone.LastDecayTimestamp = now; }
        }

        // What the stamps actually are, preferring observed magnitude over the persisted marker so a
        // hand-edited, missing or stale DecayClock cannot be trusted blindly. Any single stamped zone
        // is evidence: within a session every stamp shares a clock, and the only window where they
        // are mixed is between a config change and the re-base, where either answer re-bases anyway.
        private static ZoneDecayClockSource DetectStampedClock() {
            foreach (var zone in Zones) {
                if (zone.LastDecayTimestamp <= 0d) { continue; } // never stamped, carries no evidence
                return zone.LastDecayTimestamp >= WallClockFloor
                    ? ZoneDecayClockSource.RealTime : ZoneDecayClockSource.GameTime;
            }
            return stampedClock; // nothing stamped yet - trust the marker
        }

        // Keeps the in-memory decay stamps and the configured clock in the same units, re-basing every
        // zone when they diverge. Returns true when it re-based, in which case the caller must skip
        // decay for this pass.
        //
        // This is the whole safety net for the two clocks being numerically incompatible: wall stamps
        // are ~1.7e9 and net-time stamps ~1e3-1e6, so measuring across an unhandled switch either
        // floors every zone to level 1 (game -> real) or freezes decay forever (real -> game). Zone
        // levels are never touched here; only the countdown restarts.
        private static bool EnsureClockMode() {
            ZoneDecayClockSource configured = ConfiguredClock();
            ZoneDecayClockSource actual = DetectStampedClock();
            if (actual == configured) { stampedClock = configured; return false; }
            stampedClock = configured;
            double now = ClockNow(configured);
            foreach (var zone in Zones) { zone.LastDecayTimestamp = now; }
            zonesDirty = true;
            Logger.LogInfo($"Zone decay clock changed ({actual} -> {configured}). Re-based {Zones.Count} zone decay timers to {now:F0}; zone levels are unchanged.");
            return true;
        }

        // The decay loop re-bases on its own next tick, so this only makes it happen now rather than
        // up to DecayTickIntervalSeconds later. Off the authority there is nothing to re-base: clients
        // hold levels only and never read their stamps. Guarded on zonesBuilt because Jotunn's
        // SynchronizationManager restores every synced entry to its local value from a ZNet.OnDestroy
        // prefix on world unload, raising SettingChanged after zone state has already been torn down.
        internal static void OnDecayClockChanged(object sender, EventArgs e) {
            if (!zonesBuilt || ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            if (EnsureClockMode()) { SaveZoneData(); zonesDirty = false; }
        }

        private static IEnumerator DecayZoneLevels() {
            while (true) {
                yield return new WaitForSeconds(DecayTickIntervalSeconds);
                // TaskRunner is DontDestroyOnLoad, so this loop can outlive a teardown path that
                // missed ResetState. ClockNow dereferences ZNet in GameTime mode.
                if (ZNet.instance == null) { continue; }
                if (!zonesBuilt || ZoneScaleSystemData.Zones.Count == 0) {
                    if (zonesDirty) { SaveZoneData(); zonesDirty = false; }
                    continue;
                }
                // Re-base before measuring anything: a pass that spanned a clock change would either
                // wipe every zone or stop decaying entirely. Persist straight away so a crash before
                // the next tick cannot leave the file's marker disagreeing with its stamps.
                if (EnsureClockMode()) { SaveZoneData(); zonesDirty = false; continue; }

                double now = ClockNow(stampedClock);
                float decayRate = ValConfig.ZoneDecayLevelsPerHour.Value; // zone levels lost per hour of the configured clock
                if (decayRate <= 0f) {
                    // Decay disabled: keep each zone's decay clock current so toggling it back on
                    // doesn't cause a sudden catch-up drop. Persisted rather than in-memory only, or
                    // a restart reinstates that drop from the stale stamps still on disk.
                    foreach (var zone in ZoneScaleSystemData.Zones) { zone.LastDecayTimestamp = now; }
                    SaveZoneData();
                    zonesDirty = false;
                    continue;
                }
                List<ZoneData> changedZones = new List<ZoneData>();
                bool clockRewound = false;
                foreach (var zone in ZoneScaleSystemData.Zones) {
                    if (zone.ZoneLevel <= 1) { continue; }
                    // No usable stamp (OmitDefaults drops the field when it is 0, and files written
                    // before it existed have none). Measuring elapsed time from the epoch would floor
                    // the zone on the first tick, so adopt now and persist the correction.
                    if (zone.LastDecayTimestamp <= 0) {
                        zone.LastDecayTimestamp = now;
                        zonesDirty = true;
                        continue;
                    }
                    double elapsed = now - zone.LastDecayTimestamp;
                    if (elapsed < 0d) {
                        // Clock ran backwards: a world restored from a backup (net time lives in the
                        // world save), a hand-edited stamp, or a system clock correction. Pull the
                        // stamp back to now so the zone is not frozen forever; its level is untouched.
                        // Safe to clamp rather than skip because EnsureClockMode already ran, so this
                        // cannot be a units mismatch.
                        zone.LastDecayTimestamp = now;
                        clockRewound = true;
                        continue;
                    }
                    double levelsToDecay = (elapsed / 3600.0) * decayRate;
                    if (levelsToDecay >= 1.0) {
                        int decayAmount = (int)levelsToDecay;
                        zone.ZoneLevel = Mathf.Max(1, zone.ZoneLevel - decayAmount);
                        // Advance the clock only by the time we actually consumed so the sub-level
                        // remainder carries over to the next pass.
                        zone.LastDecayTimestamp += (decayAmount / decayRate) * 3600.0;
                        changedZones.Add(zone);
                    }
                }
                if (clockRewound) {
                    Logger.LogWarning("Some zone decay stamps were ahead of the current clock and were re-based to now. No zone levels were changed.");
                    zonesDirty = true;
                }
                if (changedZones.Count > 0) {
                    Logger.LogDebug("Zone levels decayed.");
                    zonesDirty = true;
                    ZoneScaleSystemData.BroadcastZoneLevels(changedZones);
                    ZoneScaleSystem.DrawMinimapOverlay();
                }
                // Flush any kill/decay changes accumulated since the last save.
                if (zonesDirty) { SaveZoneData(); zonesDirty = false; }
            }
        }

        internal static ZoneData GetZoneForPosition(Vector3 pos) {
            // Temporal locality: consecutive kills/spawns usually fall in the same zone.
            if (lastZone != null && lastZone.ContainsPosition(pos)) { return lastZone; }
            if (zoneIndex.TryGetValue(CellKey(pos.x, pos.z), out var bucket)) {
                foreach (var zone in bucket) {
                    if (zone.ContainsPosition(pos)) {
                        lastZone = zone;
                        return zone;
                    }
                }
            }
            return null;
        }

        // Packs an IndexCellSize-grid cell coordinate into a single long key. The uint cast keeps
        // negative coordinates (the world spans [-worldSize, +worldSize]) deterministic.
        private static long CellKey(float x, float z) {
            int cx = Mathf.FloorToInt(x / IndexCellSize);
            int cz = Mathf.FloorToInt(z / IndexCellSize);
            return ((long)(uint)cx << 32) | (uint)cz;
        }

        // Rebuilds the spatial index and id lookup from the current Zones list. Call after any full
        // (re)assignment of Zones; safe to call repeatedly.
        internal static void BuildZoneIndex() {
            zoneIndex = new Dictionary<long, List<ZoneData>>();
            zoneById = new Dictionary<int, ZoneData>(Zones.Count);
            lastZone = null;
            foreach (var zone in Zones) {
                zoneById[zone.ZoneId] = zone;
                int minCx = Mathf.FloorToInt(zone.MinX / IndexCellSize);
                int maxCx = Mathf.FloorToInt(zone.MaxX / IndexCellSize);
                int minCz = Mathf.FloorToInt(zone.MinZ / IndexCellSize);
                int maxCz = Mathf.FloorToInt(zone.MaxZ / IndexCellSize);
                for (int cx = minCx; cx <= maxCx; cx++) {
                    for (int cz = minCz; cz <= maxCz; cz++) {
                        long key = ((long)(uint)cx << 32) | (uint)cz;
                        if (!zoneIndex.TryGetValue(key, out var bucket)) {
                            bucket = new List<ZoneData>();
                            zoneIndex[key] = bucket;
                        }
                        bucket.Add(zone);
                    }
                }
            }
            // Apply any zone-level updates that arrived from the authority before geometry was ready.
            if (pendingLevelUpdates.Count > 0) {
                foreach (var kv in pendingLevelUpdates) {
                    if (zoneById.TryGetValue(kv.Key, out var zone)) { zone.ZoneLevel = kv.Value; }
                }
                pendingLevelUpdates.Clear();
            }
        }

        // Authority-only. Zone levels live on the server and reach clients over ZoneLevelSyncRPC,
        // which sends only zones above level 1 -- so a client writing this file persists a partial
        // view and then loads it back as authoritative next session, with no path that ever corrects
        // a stale level downward. Guarded here rather than at each call site because BuildZoneMap
        // runs on clients too (they need the geometry for the minimap overlay) and would otherwise
        // write a zone file of their own.
        internal static void SaveZoneData() {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                var saveData = new ZoneSystemSaveData {
                    Zones = ZoneScaleSystemData.Zones,
                    // Left null when the name is not available yet (m_world is populated a little
                    // after ZNet comes up). OmitDefaults then drops the key, and LoadZoneData skips
                    // its world check on an empty name, so the file still loads. A placeholder like
                    // "unknown" would match no world at all and force a rebuild instead.
                    WorldName = ZNet.instance.GetWorldName(),
                    // What the stamps in this file ARE. Writing the configured clock here instead
                    // would mislabel a file saved between a config change and the re-base tick, and
                    // the next load would trust the label over the stamps.
                    DecayClock = stampedClock.ToString()
                };
                File.WriteAllText(ValConfig.zoneDataSavedDataPath, DataObjects.yamlSerializer.Serialize(saveData));
            } catch (Exception e) {
                Logger.LogWarning($"Failed to save zone data: {e.Message}");
            }
        }

        // Flushes pending zone data to disk if dirty; invoked on world save (see ZoneSavePatches).
        internal static void FlushPendingSave() {
            if (!zonesDirty) { return; }
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            SaveZoneData();
            zonesDirty = false;
        }

        internal static ZoneLoadResult LoadZoneData() {
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                string path = ValConfig.zoneDataSavedDataPath;
                if (!File.Exists(path)) { return ZoneLoadResult.NoData; }
                string yaml = File.ReadAllText(path);
                ZoneSystemSaveData loaded = DeserializeSaveData(yaml, Path.GetFileName(path));
                if (loaded?.Zones == null || loaded.Zones.Count == 0) { return ZoneLoadResult.NoData; }
                string currentWorld = ZNet.instance?.GetWorldName() ?? "";
                if (!string.IsNullOrEmpty(loaded.WorldName) && loaded.WorldName != currentWorld) {
                    // Another world's file sitting at this world's path (usually the legacy-file
                    // migration). Nothing here belongs to this world, so it is safe to overwrite.
                    Logger.LogInfo($"Zone data is for a different world ({loaded.WorldName} vs {currentWorld}), rebuilding.");
                    return ZoneLoadResult.NoData;
                }
                Zones = loaded.Zones;
                // Normalize persisted ids to the deterministic cell-based scheme so authority ids
                // match what clients derive from a fresh build (ignores any stale build-order id).
                foreach (var z in Zones) { z.ZoneId = ZoneIdForBounds(z.MinX, z.MinZ); }
                BuildZoneIndex();
                // Record the file's clock only. Deliberately no re-base here: on a dedicated server
                // Initialize runs from a ZoneSystem.Start postfix, and ZoneSystem.Start runs before
                // ZNet.Start loads the world (it is what populates the location list ServerLoadWorld
                // immediately consumes), so ZNet.GetTimeSeconds() is still its 2040 initialiser at
                // this point. Re-basing to that would set the world's decay clock days in the past
                // and floor every zone on the next tick. EnsureClockMode does it from the decay
                // coroutine instead, whose first pass is a full tick interval later.
                stampedClock = loaded.DecayClock == ZoneDecayClockSource.GameTime.ToString()
                    ? ZoneDecayClockSource.GameTime : ZoneDecayClockSource.RealTime;
                return ZoneLoadResult.Loaded;
            } catch (Exception e) {
                Logger.LogWarning($"Failed to load zone data: {e.Message}");
                return ZoneLoadResult.Unreadable;
            }
        }

        // Strict first so an unrecognised key is reported, tolerant second so it costs one key
        // rather than the whole file -- the same two-pass idiom as YamlConfigFile.Deserialize. Files
        // written before CenterX/CenterZ were removed from ZoneData still carry those keys, and only
        // the tolerant pass can read them; the next save rewrites the file clean.
        private static ZoneSystemSaveData DeserializeSaveData(string yaml, string fileName) {
            try {
                return YamlFormat.Default.Deserializer.Deserialize<ZoneSystemSaveData>(yaml);
            } catch (YamlException strictError) {
                ZoneSystemSaveData tolerant = YamlFormat.Default.TolerantDeserializer.Deserialize<ZoneSystemSaveData>(yaml);
                Logger.LogInfo($"{fileName} contains keys this version does not recognise ({strictError.Message}). They were ignored and the rest of the file loaded; it will be rewritten cleanly on the next save.");
                return tolerant;
            }
        }

        // Moves a zone file we could not parse out of the way so the rebuild that follows cannot
        // overwrite it, leaving an admin something to recover levels from. An older backup is never
        // replaced: if one already exists, the current file is itself rebuild output and worthless.
        internal static void PreserveUnreadableZoneData() {
            try {
                string path = ValConfig.zoneDataSavedDataPath;
                if (!File.Exists(path)) { return; }
                string backup = path + ".corrupt";
                if (File.Exists(backup)) {
                    Logger.LogError($"Could not read {Path.GetFileName(path)}. An earlier copy is already preserved as {Path.GetFileName(backup)}; building a fresh zone map.");
                    return;
                }
                File.Move(path, backup);
                Logger.LogError($"Could not read {Path.GetFileName(path)}; it has been kept as {Path.GetFileName(backup)} and a fresh zone map will be built. Zone levels in that file were NOT loaded.");
            } catch (Exception e) {
                Logger.LogWarning($"Failed to preserve unreadable zone data: {e.Message}");
            }
        }

        internal static ZPackage SerializeDeaths(List<SerializableVector3> deaths) {
            ZPackage pkg = new ZPackage();
            pkg.Write(DataObjects.yamlSerializerJsonCompat.Serialize(deaths));
            return pkg;
        }

        // Authority -> clients: push the given zones' current levels so client overlays / level
        // bonuses stay in sync. Sends only the changed zones.
        internal static void BroadcastZoneLevels(ICollection<ZoneData> zones) {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            if (zones.Count == 0 || ZNet.instance.m_peers == null || ZNet.instance.m_peers.Count == 0) { return; }
            ValConfig.ZoneLevelSyncRPC.SendPackage(ZNet.instance.m_peers, SerializeZoneLevels(zones));
        }

        private static ZPackage SerializeZoneLevels(ICollection<ZoneData> zones) {
            ZPackage pkg = new ZPackage();
            pkg.Write(zones.Count);
            foreach (var z in zones) {
                pkg.Write(z.ZoneId);
                pkg.Write(z.ZoneLevel);
            }
            return pkg;
        }

        // Initial-sync payload sent to a joining client: only zones above the default level 1 (clients
        // default every zone to level 1, so this stays compact even with tens of thousands of zones).
        internal static ZPackage SerializeLeveledZonesForSync() {
            List<ZoneData> leveled = Zones.Where(z => z.ZoneLevel > 1).ToList();
            return SerializeZoneLevels(leveled);
        }

        // Client handler for ZoneLevelSyncRPC. Updates from the server may arrive before this client
        // has built its zone geometry; in that case they are buffered and applied by BuildZoneIndex.
        internal static IEnumerator OnClientReceiveZoneLevels(long sender, ZPackage package) {
            int count = package.ReadInt();
            for (int i = 0; i < count; i++) {
                int id = package.ReadInt();
                int level = package.ReadInt();
                if (zoneById.TryGetValue(id, out var zone)) { zone.ZoneLevel = level; } else { pendingLevelUpdates[id] = level; }
            }
            if (count > 0 && zonesBuilt) { ZoneScaleSystem.DrawMinimapOverlay(); }
            yield return null;
        }
    }
}
