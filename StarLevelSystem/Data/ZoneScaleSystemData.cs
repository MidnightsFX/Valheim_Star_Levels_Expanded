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

        internal static bool zonesBuilt = false;
        internal static bool buildingZones = false;
        internal static bool overlayAvailable = false;
        internal static int overlayUpdates = 0;

        // Kills, decay and the decay clock, which regions have too.
        internal static readonly KillDecayLedger<ZoneData> Ledger = new KillDecayLedger<ZoneData>(
            "Zone",
            () => Zones,
            () => ValConfig.ZoneKillsPerLevelUp.Value,
            () => ValConfig.ZoneDecayLevelsPerHour.Value,
            changed => {
                BroadcastZoneLevels(changed);
                ZoneScaleSystem.DrawMinimapOverlay();
            },
            SaveZoneData);

        // Zone identity is derived from grid-cell coordinates so it is stable across independent
        // builds (authority load vs. client rebuild) and network syncs line up. Sequential build
        // order is non-deterministic between peers; cell coordinates are a pure function of world
        // position + MaxZoneSize. Cell indices are small (a zone is at least 1000m, and a world past vanilla's
        // adds negative cells at the low edge), so this packing neither overflows an int nor collides.
        private const int ZoneIdStride = 100000;

        internal static int ZoneIdForCell(int cellX, int cellZ) => cellX * ZoneIdStride + cellZ;

        // What the current map is cut with. A map's sizes only change when it is rebuilt, which resets its levels, so these are
        // not simply the settings: a server's come from its file until a rebuild, and a client's come from the server, so a
        // setting changed since the server's map was cut can't give the client different zones with different ids.
        internal static float BuildCellSize { get; private set; }
        internal static float BuildMinSize { get; private set; }
        internal static int MapGeneration { get; private set; }
        private static bool sizesFromServer;

        internal static void UseConfiguredSizes(bool newGeneration) {
            BuildCellSize = ValConfig.MaxZoneSize.Value;
            BuildMinSize = ValConfig.MinZoneSize.Value;
            if (newGeneration) { MapGeneration++; }
        }

        // A client that has heard from the server keeps what it was told.
        internal static void UseConfiguredSizesUnlessToldOtherwise() {
            if (sizesFromServer == false) { UseConfiguredSizes(false); }
        }

        // Set by LoadZoneData when the file holds a map built for a smaller or larger world.
        internal static bool LoadedForAnotherWorldSize { get; private set; }

        // Progress from a map built for another world size, until the map for this one is finished.
        private static Dictionary<int, ZoneData> heldZoneProgress;
        private static string heldZoneClock;

        internal static void HoldLoadedZonesForRebuild() {
            heldZoneProgress = new Dictionary<int, ZoneData>(Zones.Count);
            foreach (var zone in Zones) { heldZoneProgress[zone.ZoneId] = zone; }
            heldZoneClock = Ledger.StampedClockName;
            Zones = new List<ZoneData>();
            BuildZoneIndex();
        }

        // Ids come from the cell, and the cell grid does not move with the world's size, so an id is the same land. Decay
        // stamps only carry over in the clock the fresh ones were just given; StampNewZones has left the rest current.
        internal static void AdoptHeldZoneProgress(List<ZoneData> built) {
            if (heldZoneProgress == null) { return; }
            foreach (var zone in built) {
                if (heldZoneProgress.TryGetValue(zone.ZoneId, out ZoneData held) == false) { continue; }
                zone.ZoneLevel = held.ZoneLevel;
                zone.TotalKills = held.TotalKills;
                if (heldZoneClock == Ledger.StampedClockName) { zone.LastDecayTimestamp = held.LastDecayTimestamp; }
            }
            heldZoneProgress = null;
        }

        // Recovers the cell coordinates from a zone's min corner (MinX = -worldSize + cellX * cellSize)
        // and packs them into the same id used at build time. Used to normalize ids loaded from disk.
        internal static int ZoneIdForBounds(float minX, float minZ, float cellSize) {
            int cellX = Mathf.RoundToInt((minX + WorldGenerator.worldSize) / cellSize);
            int cellZ = Mathf.RoundToInt((minZ + WorldGenerator.worldSize) / cellSize);
            return ZoneIdForCell(cellX, cellZ);
        }


        // Authority-only: aggregate a batch of deaths into zone kill counts and levels. Level-ups are
        // computed by threshold crossing so a batch that jumps past an exact multiple still levels up.
        internal static void ApplyDeaths(List<SerializableVector3> deaths) {
            if (!zonesBuilt) { return; }
            // Stamping a level-up dereferences ZNet in GameTime mode. Both callers already run with a live ZNet,
            // but this is the one entrypoint where that is not visible from here.
            if (ZNet.instance == null) { return; }
            HashSet<ZoneData> leveledZones = Ledger.ApplyKills(deaths.Select(death => GetZoneForPosition(death)).Where(zone => zone != null));
            if (leveledZones.Count > 0) {
                Logger.LogDebug($"{leveledZones.Count} zone(s) leveled up from a batch of {deaths.Count} kills.");
                ZoneScaleSystemData.BroadcastZoneLevels(leveledZones);
                ZoneScaleSystem.DrawMinimapOverlay();
            }
        }

        internal static void StartDecayCoroutine() {
            Ledger.Start();
        }

        // Tears down all in-memory zone state and stops the decay loop so a world/server switch
        // starts clean. Flushes any unsaved authority changes first (no-op off the server).
        internal static void ResetState() {
            FlushPendingSave();
            Ledger.Reset();
            Zones = new List<ZoneData>();
            zoneIndex = new Dictionary<long, List<ZoneData>>();
            zoneById = new Dictionary<int, ZoneData>();
            heldZoneProgress = null;
            pendingLevelUpdates.Clear();
            lastZone = null;
            zonesBuilt = false;
            buildingZones = false;
            overlayAvailable = false;
            BuildCellSize = 0f;
            BuildMinSize = 0f;
            MapGeneration = 0;
            sizesFromServer = false;
        }

        // Fresh geometry carries no history, so it simply adopts whatever clock is configured now.
        internal static void StampNewZones(List<ZoneData> zones) {
            Ledger.StampNew(zones);
        }

        // Guarded on zonesBuilt because Jotunn's SynchronizationManager restores every synced entry to its local value from a
        // ZNet.OnDestroy prefix on world unload, raising SettingChanged after zone state has already been torn down.
        internal static void OnDecayClockChanged(object sender, EventArgs e) {
            if (zonesBuilt) { Ledger.OnClockSettingChanged(); }
            RegionScaleSystemData.OnDecayClockChanged();
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
        // Returns whether the file was actually written, so a caller can keep the dirty flag and try
        // again rather than dropping the changes it was about to persist.
        internal static bool SaveZoneData() {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return false; }
            // Without a resolved world there is no world-specific file to write: PerWorldStatePath falls
            // back to the shared one, and a zone file carrying no world of its own is a file that every
            // world loaded afterwards adopts as its own progression. Not reachable on a server in
            // practice (the world is set before the main scene loads); skipping costs one pass worth of
            // changes, while writing it is the cross-world bleed itself.
            string worldName = ZNet.instance.GetWorldName();
            if (string.IsNullOrEmpty(worldName)) {
                Logger.LogWarning("Not saving zone data: no world is resolved yet, so it cannot be written to this world's own file.");
                return false;
            }
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                var saveData = new ZoneSystemSaveData {
                    Zones = ZoneScaleSystemData.Zones,
                    WorldName = worldName,
                    // The identity LoadZoneData actually checks. 0 only on a world old enough to carry
                    // no id of its own, where the name is the best answer available.
                    WorldUid = ZNet.instance.GetWorld()?.m_uid ?? 0L,
                    // What the stamps in this file ARE. Writing the configured clock here instead
                    // would mislabel a file saved between a config change and the re-base tick, and
                    // the next load would trust the label over the stamps.
                    DecayClock = Ledger.StampedClockName,
                    BuildRadius = WorldSize.IsResized ? WorldSize.Radius : 0f,
                    BuildMinSize = BuildMinSize,
                    Generation = MapGeneration,
                };
                File.WriteAllText(ValConfig.zoneDataSavedDataPath, DataObjects.yamlSerializer.Serialize(saveData));
                return true;
            } catch (Exception e) {
                Logger.LogWarning($"Failed to save zone data: {e.Message}");
                return false;
            }
        }

        // Flushes pending zone data to disk if dirty; invoked on world save (see ZoneSavePatches).
        internal static void FlushPendingSave() {
            Ledger.FlushIfDirty();
        }

        internal static ZoneLoadResult LoadZoneData() {
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                string path = ValConfig.zoneDataSavedDataPath;
                if (!File.Exists(path)) { return ZoneLoadResult.NoData; }
                string yaml = File.ReadAllText(path);
                ZoneSystemSaveData loaded = DeserializeSaveData(yaml, Path.GetFileName(path));
                if (loaded?.Zones == null || loaded.Zones.Count == 0) { return ZoneLoadResult.NoData; }
                if (!IsCurrentWorld(loaded.WorldUid, loaded.WorldName, "Zone")) { return ZoneLoadResult.NoData; }
                Zones = loaded.Zones;
                LoadedForAnotherWorldSize = (loaded.BuildRadius > 0f ? loaded.BuildRadius : WorldGenerator.worldSize) != WorldSize.Radius;
                // The sizes this map was cut with, whatever the settings say now. A world grown since keeps them too, or the
                // rebuild's ids would not line up with the progress it is carrying over.
                BuildCellSize = Zones[0].MaxX - Zones[0].MinX;
                BuildMinSize = loaded.BuildMinSize > 0f ? loaded.BuildMinSize : ValConfig.MinZoneSize.Value;
                MapGeneration = loaded.Generation;
                if (Mathf.Approximately(BuildCellSize, ValConfig.MaxZoneSize.Value) == false || Mathf.Approximately(BuildMinSize, ValConfig.MinZoneSize.Value) == false) {
                    Logger.LogWarning("The zone size settings have changed since this map was built. It stays as it is, keeping its levels, until sls-zone-rebuild is run.");
                }
                // Normalize persisted ids to the deterministic cell-based scheme so authority ids
                // match what clients derive from a fresh build (ignores any stale build-order id).
                foreach (var z in Zones) { z.ZoneId = ZoneIdForBounds(z.MinX, z.MinZ, BuildCellSize); }
                BuildZoneIndex();
                Ledger.AdoptSavedClock(loaded.DecayClock);
                return ZoneLoadResult.Loaded;
            } catch (Exception e) {
                Logger.LogWarning($"Failed to load zone data: {e.Message}");
                return ZoneLoadResult.Unreadable;
            }
        }

        // Whether a loaded file is this world's progression. Checked on top of the per-world file name
        // because that name is only ever as unique as the world's name is, and because the file may have
        // just been carried into this world's slot by the migration in ValConfig.PerWorldStatePath.
        // Anything that fails here is another world's file sitting at this world's path, so it is safe
        // to overwrite with a fresh build.
        internal static bool IsCurrentWorld(long loadedUid, string loadedWorld, string what) {
            long currentUid = ZNet.instance?.GetWorld()?.m_uid ?? 0L;
            string currentWorld = ZNet.instance?.GetWorldName() ?? "";
            if (loadedUid != 0L && currentUid != 0L) {
                if (loadedUid == currentUid) { return true; }
                Logger.LogInfo($"{what} data belongs to a different world (id {loadedUid:x} vs {currentUid:x}), rebuilding.");
                return false;
            }
            // Written before world ids were recorded (or read on a world old enough not to have one):
            // the name is the only identity either side can offer. The next save adds the id.
            if (!string.IsNullOrEmpty(loadedWorld)) {
                if (loadedWorld == currentWorld) { return true; }
                Logger.LogInfo($"{what} data is for a different world ({loadedWorld} vs {currentWorld}), rebuilding.");
                return false;
            }
            // No world recorded at all. This used to be taken as close enough and loaded, which is
            // exactly how one unidentified file could become every world's starting zone levels.
            Logger.LogInfo($"{what} data names no world of its own, so it cannot be matched to this one; rebuilding.");
            return false;
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

        // Authority -> clients: what the map is cut with. Null for a joining client while there is no map yet: it hears when
        // that is done, from BroadcastGeometry.
        internal static ZPackage SerializeGeometryForSync() {
            return zonesBuilt ? SerializeGeometry() : null;
        }

        private static ZPackage SerializeGeometry() {
            ZPackage pkg = new ZPackage();
            pkg.Write(BuildCellSize);
            pkg.Write(BuildMinSize);
            pkg.Write(MapGeneration);
            return pkg;
        }

        internal static void BroadcastGeometry() {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.m_peers == null || ZNet.instance.m_peers.Count == 0) { return; }
            ValConfig.ZoneGeometryRPC.SendPackage(ZNet.instance.m_peers, SerializeGeometry());
        }

        // Anything other than what this client built with means its zones are the wrong ones, and it builds them again.
        internal static IEnumerator OnClientReceiveGeometry(long sender, ZPackage package) {
            yield return null;
            if (ZNet.instance == null || ZNet.instance.IsServer()) { yield break; }
            float cellSize = package.ReadSingle();
            float minSize = package.ReadSingle();
            int generation = package.ReadInt();
            if (cellSize <= 0f || minSize < 0f) { yield break; }
            bool changed = cellSize != BuildCellSize || minSize != BuildMinSize || generation != MapGeneration;
            sizesFromServer = true;
            BuildCellSize = cellSize;
            BuildMinSize = minSize;
            MapGeneration = generation;
            if (changed && (zonesBuilt || buildingZones)) {
                ZoneScaleSystem.RebuildForServerGeometry();
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
