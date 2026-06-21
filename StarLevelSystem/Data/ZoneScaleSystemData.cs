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
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data {
    internal static class ZoneScaleSystemData {

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
        internal static int overlayUpdates = 0;


        // Authority-only: aggregate a batch of deaths into zone kill counts and levels. Level-ups are
        // computed by threshold crossing so a batch that jumps past an exact multiple still levels up.
        internal static void ApplyDeaths(List<SerializableVector3> deaths) {
            if (!zonesBuilt) { return; }
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
            TaskRunner.Run().StartCoroutine(DecayZoneLevels());
        }

        private static IEnumerator DecayZoneLevels() {
            while (true) {
                yield return new WaitForSeconds(60f);
                if (!zonesBuilt || ZoneScaleSystemData.Zones.Count == 0) {
                    if (zonesDirty) { SaveZoneData(); zonesDirty = false; }
                    continue;
                }
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                List<ZoneData> changedZones = new List<ZoneData>();
                foreach (var zone in ZoneScaleSystemData.Zones) {
                    if (zone.ZoneLevel <= 1) { continue; }
                    double elapsedHours = (now - zone.LastDecayTimestamp) / 3600.0;
                    if (elapsedHours >= 1.0) {
                        int hoursDecayed = (int)elapsedHours;
                        zone.ZoneLevel = Mathf.Max(1, zone.ZoneLevel - hoursDecayed);
                        zone.LastDecayTimestamp = now;
                        changedZones.Add(zone);
                    }
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

        internal static void SaveZoneData() {
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                var saveData = new ZoneSystemSaveData {
                    Zones = ZoneScaleSystemData.Zones,
                    WorldName = ZNet.instance?.GetWorldName() ?? "unknown"
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

        internal static bool LoadZoneData() {
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                if (!File.Exists(ValConfig.zoneDataSavedDataPath)) { return false; }
                string yaml = File.ReadAllText(ValConfig.zoneDataSavedDataPath);
                var loaded = DataObjects.yamlDeserializer.Deserialize<ZoneSystemSaveData>(yaml);
                if (loaded?.Zones == null || loaded.Zones.Count == 0) { return false; }
                string currentWorld = ZNet.instance?.GetWorldName() ?? "";
                if (!string.IsNullOrEmpty(loaded.WorldName) && loaded.WorldName != currentWorld) {
                    Logger.LogInfo($"Zone data is for a different world ({loaded.WorldName} vs {currentWorld}), rebuilding.");
                    return false;
                }
                Zones = loaded.Zones;
                BuildZoneIndex();
                return true;
            } catch (Exception e) {
                Logger.LogWarning($"Failed to load zone data: {e.Message}");
                return false;
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
