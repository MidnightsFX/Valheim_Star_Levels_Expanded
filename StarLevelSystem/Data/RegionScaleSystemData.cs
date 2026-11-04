using StarLevelSystem.common;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using YamlDotNet.Core;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data {
    // The region map and its levels. The server builds the map once and keeps it in a per-world file; clients are sent it when
    // they connect, because a client owns creatures and rolls their levels, and draws the map from it.
    internal static class RegionScaleSystemData {
        // A client refuses anything past these. CustomRPC's sender can be forged, so what arrives is not taken on trust.
        private const int MaxGridDim = 4096;
        private const int MaxRegionCount = ushort.MaxValue;

        internal static List<RegionData> Regions = new List<RegionData>();
        internal static bool Building;

        private static ushort[] grid;
        private static int gridDim;
        private static float gridCellSize;
        private static float gridHalfExtent;
        // The grid as it is saved and sent. Kept because a joining client's package is built on the spot, on the frame it joins.
        private static byte[] compressedGrid;
        private static int mapHash;
        // Level updates that got here ahead of the map they belong to: a small package can overtake a large fragmented one.
        private static readonly Dictionary<int, int> pendingLevels = new Dictionary<int, int>();

        internal static readonly KillDecayLedger<RegionData> Ledger = new KillDecayLedger<RegionData>(
            "Region",
            () => Regions,
            () => ValConfig.RegionKillsPerLevelUp.Value,
            () => ValConfig.RegionDecayLevelsPerHour.Value,
            changed => {
                BroadcastLevels(changed);
                RegionScaleSystem.DrawMinimapOverlay();
            },
            SaveRegionData);

        internal static bool MapReady => grid != null;
        internal static int MapHash => mapHash;
        internal static int GridDim => gridDim;
        internal static float GridCellSize => gridCellSize;
        internal static float GridHalfExtent => gridHalfExtent;
        internal static ushort[] Grid => grid;

        internal static RegionData GetRegionForPosition(Vector3 pos) {
            if (grid == null) { return null; }
            int x = Mathf.FloorToInt((pos.x + gridHalfExtent) / gridCellSize);
            int z = Mathf.FloorToInt((pos.z + gridHalfExtent) / gridCellSize);
            if (x < 0 || z < 0 || x >= gridDim || z >= gridDim) { return null; }
            int id = grid[z * gridDim + x];
            return id == 0 || id > Regions.Count ? null : Regions[id - 1];
        }

        internal static float LevelBonusAt(Vector3 pos) {
            if (ValConfig.EnableRegionScalingBonus.Value == false) { return 1f; }
            RegionData region = GetRegionForPosition(pos);
            return region == null ? 1f : LeveledAreas.BonusFor(region.Level, ValConfig.RegionLevelBonusPerLevel.Value);
        }

        // Authority-only: a finished build becomes the map, with every region starting over at level 1.
        internal static void AdoptBuilt(RegionBuildResult built, int dim, float cellSize) {
            List<RegionData> regions = new List<RegionData>(built.Regions.Count);
            foreach (BuiltRegion region in built.Regions) {
                regions.Add(new RegionData { Id = region.Id, Mountain = region.Kind == RegionKind.Mountain, Cells = region.CellCount });
            }
            Ledger.StampNew(regions);
            SetMap(built.Cells, dim, cellSize, regions);
            Ledger.Dirty = true;
            SaveRegionData();
        }

        private static void SetMap(ushort[] cells, int dim, float cellSize, List<RegionData> regions) {
            grid = cells;
            gridDim = dim;
            gridCellSize = cellSize;
            gridHalfExtent = dim * cellSize * 0.5f;
            Regions = regions;
            compressedGrid = Deflate(cells);
            mapHash = HashOf(compressedGrid);
            ApplyPendingLevels();
            Logger.LogInfo($"Region map ready: {regions.Count} regions on a {dim}x{dim} grid at {cellSize:0.#} m per cell, {compressedGrid.Length / 1024f:0.#} KB, hash {mapHash:x8}.");
        }

        private static void ApplyPendingLevels() {
            foreach (KeyValuePair<int, int> pending in pendingLevels) {
                if (pending.Key >= 1 && pending.Key <= Regions.Count) { Regions[pending.Key - 1].Level = pending.Value; }
            }
            pendingLevels.Clear();
        }

        internal static void ApplyDeaths(List<SerializableVector3> deaths) {
            if (grid == null || ZNet.instance == null) { return; }
            HashSet<RegionData> leveled = Ledger.ApplyKills(deaths.Select(death => GetRegionForPosition(death)).Where(region => region != null));
            if (leveled.Count == 0) { return; }
            Logger.LogDebug($"{leveled.Count} region(s) leveled up from a batch of {deaths.Count} kills.");
            BroadcastLevels(leveled);
            RegionScaleSystem.DrawMinimapOverlay();
        }

        // Tears down all in-memory state so a world or server switch starts clean. Flushes unsaved authority changes first.
        internal static void ResetState() {
            Ledger.FlushIfDirty();
            Ledger.Reset();
            ClearMap();
            Building = false;
        }

        internal static void ClearMap() {
            Regions = new List<RegionData>();
            grid = null;
            gridDim = 0;
            gridCellSize = 0f;
            gridHalfExtent = 0f;
            compressedGrid = null;
            mapHash = 0;
            pendingLevels.Clear();
        }

        internal static void OnDecayClockChanged() {
            if (grid != null) { Ledger.OnClockSettingChanged(); }
        }

        // --- persistence ---

        // Authority-only, and only once the world is resolved: without one, PerWorldStatePath falls back to the shared path, and
        // a file there is read as their own by every world loaded afterwards.
        internal static bool SaveRegionData() {
            if (grid == null || ZNet.instance == null || !ZNet.instance.IsServer()) { return false; }
            string worldName = ZNet.instance.GetWorldName();
            if (string.IsNullOrEmpty(worldName)) {
                Logger.LogWarning("Not saving region data: no world is resolved yet, so it cannot be written to this world's own file.");
                return false;
            }
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                RegionSystemSaveData save = new RegionSystemSaveData {
                    Regions = Regions,
                    Grid = Convert.ToBase64String(compressedGrid),
                    GridDim = gridDim,
                    CellSize = gridCellSize,
                    BuildRadius = WorldSize.TotalRadius,
                    BuildTargetSize = ValConfig.RegionTargetSize.Value,
                    BuildMinSize = ValConfig.RegionMinSize.Value,
                    BuildMountainMinSize = ValConfig.RegionMountainMinSize.Value,
                    WorldName = worldName,
                    WorldUid = ZNet.instance.GetWorld()?.m_uid ?? 0L,
                    DecayClock = Ledger.StampedClockName,
                };
                File.WriteAllText(ValConfig.regionDataSavedDataPath, yamlSerializer.Serialize(save));
                return true;
            } catch (Exception e) {
                Logger.LogWarning($"Failed to save region data: {e.Message}");
                return false;
            }
        }

        // True when the file gave this world its map. Only a missing, foreign or differently sized map is ever rebuilt on its
        // own: a rebuild resets every level, so a changed size setting waits for sls-region-rebuild.
        internal static bool LoadRegionData() {
            string path = ValConfig.regionDataSavedDataPath;
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                if (File.Exists(path) == false) { return false; }
                RegionSystemSaveData loaded = DeserializeSaveData(File.ReadAllText(path), Path.GetFileName(path));
                if (loaded?.Regions == null || loaded.Regions.Count == 0 || string.IsNullOrEmpty(loaded.Grid)) { return false; }
                if (ZoneScaleSystemData.IsCurrentWorld(loaded.WorldUid, loaded.WorldName, "Region") == false) { return false; }
                if (Mathf.Approximately(loaded.BuildRadius, WorldSize.TotalRadius) == false) {
                    Logger.LogInfo($"Region data was built for a world of radius {loaded.BuildRadius:0} m and this one is {WorldSize.TotalRadius:0} m, so it will be built again. Region levels start over.");
                    return false;
                }
                ushort[] cells = Inflate(Convert.FromBase64String(loaded.Grid), loaded.GridDim);
                if (cells == null) {
                    Logger.LogWarning("Region data holds a grid that cannot be read, so it will be built again.");
                    return false;
                }
                Ledger.AdoptSavedClock(loaded.DecayClock);
                SetMap(cells, loaded.GridDim, loaded.CellSize, loaded.Regions);
                if (loaded.BuildTargetSize != ValConfig.RegionTargetSize.Value || loaded.BuildMinSize != ValConfig.RegionMinSize.Value || loaded.BuildMountainMinSize != ValConfig.RegionMountainMinSize.Value) {
                    Logger.LogWarning("The region size settings have changed since this map was built. It stays as it is, keeping its levels, until sls-region-rebuild is run.");
                }
                return true;
            } catch (Exception e) {
                PreserveUnreadable(path, e);
                return false;
            }
        }

        // The file's levels would otherwise be overwritten by the build that follows, leaving nothing to recover them from.
        private static void PreserveUnreadable(string path, Exception cause) {
            Logger.LogError($"Could not read {Path.GetFileName(path)}: {cause.Message}");
            try {
                string backup = path + ".corrupt";
                if (File.Exists(path) && File.Exists(backup) == false) { File.Move(path, backup); }
            } catch (Exception e) {
                Logger.LogWarning($"Failed to preserve unreadable region data: {e.Message}");
            }
        }

        // Strict first so an unrecognised key is reported, tolerant second so it costs one key rather than the whole file.
        private static RegionSystemSaveData DeserializeSaveData(string yaml, string fileName) {
            try {
                return YamlFormat.Default.Deserializer.Deserialize<RegionSystemSaveData>(yaml);
            } catch (YamlException strictError) {
                RegionSystemSaveData tolerant = YamlFormat.Default.TolerantDeserializer.Deserialize<RegionSystemSaveData>(yaml);
                Logger.LogInfo($"{fileName} contains keys this version does not recognise ({strictError.Message}). They were ignored and the rest of the file loaded; it will be rewritten cleanly on the next save.");
                return tolerant;
            }
        }

        // --- the wire ---

        private static byte[] Deflate(ushort[] cells) {
            byte[] raw = new byte[cells.Length * sizeof(ushort)];
            Buffer.BlockCopy(cells, 0, raw, 0, raw.Length);
            using (MemoryStream packed = new MemoryStream()) {
                using (DeflateStream deflate = new DeflateStream(packed, CompressionMode.Compress, true)) {
                    deflate.Write(raw, 0, raw.Length);
                }
                return packed.ToArray();
            }
        }

        // Null unless it unpacks to exactly a dim x dim grid. Stops reading there, so a package that unpacks to far more cannot
        // take the client's memory with it.
        private static ushort[] Inflate(byte[] packed, int dim) {
            if (dim <= 0 || dim > MaxGridDim) { return null; }
            int expected = dim * dim * sizeof(ushort);
            byte[] raw = new byte[expected];
            using (MemoryStream source = new MemoryStream(packed))
            using (DeflateStream inflate = new DeflateStream(source, CompressionMode.Decompress)) {
                int read = 0;
                while (read < expected) {
                    int got = inflate.Read(raw, read, expected - read);
                    if (got <= 0) { return null; }
                    read += got;
                }
                if (inflate.ReadByte() != -1) { return null; }
            }
            ushort[] cells = new ushort[dim * dim];
            Buffer.BlockCopy(raw, 0, cells, 0, expected);
            return cells;
        }

        // FNV-1a, to tell one map from another and to see in the logs that a client holds the server's.
        private static int HashOf(byte[] bytes) {
            unchecked {
                uint hash = 2166136261u;
                foreach (byte b in bytes) { hash = (hash ^ b) * 16777619u; }
                return (int)hash;
            }
        }

        // Authority -> a joining client, or null while there is nothing to send. Jotunn generates this on the frame the peer
        // joins and holds its other traffic until it is delivered, so a client owns no creatures before it has the map; only a
        // map still being built reaches it later, from BroadcastMap.
        internal static ZPackage SerializeMapForSync() {
            if (ValConfig.EnableRegionScalingBonus.Value == false || grid == null) { return null; }
            return SerializeMap();
        }

        private static ZPackage SerializeMap() {
            ZPackage package = new ZPackage();
            package.Write(mapHash);
            package.Write(gridDim);
            package.Write(gridCellSize);
            package.Write(Regions.Count);
            foreach (RegionData region in Regions) {
                package.Write(region.Mountain);
                package.Write(region.Level);
            }
            package.Write(compressedGrid);
            return package;
        }

        internal static void BroadcastMap() {
            if (grid == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.m_peers == null || ZNet.instance.m_peers.Count == 0) { return; }
            ValConfig.RegionMapRPC.SendPackage(ZNet.instance.m_peers, SerializeMap());
        }

        internal static IEnumerator OnClientReceiveMap(long sender, ZPackage package) {
            yield return null;
            if (ZNet.instance == null || ZNet.instance.IsServer()) { yield break; }
            try {
                int hash = package.ReadInt();
                int dim = package.ReadInt();
                float cellSize = package.ReadSingle();
                int count = package.ReadInt();
                if (dim <= 0 || dim > MaxGridDim || cellSize <= 0f || count < 0 || count > MaxRegionCount) {
                    Logger.LogWarning($"Ignored a region map with sizes that cannot be right ({dim} cells, {cellSize} m, {count} regions).");
                    yield break;
                }
                List<RegionData> regions = new List<RegionData>(count);
                for (int i = 0; i < count; i++) {
                    regions.Add(new RegionData { Id = i + 1, Mountain = package.ReadBool(), Level = package.ReadInt() });
                }
                ushort[] cells = Inflate(package.ReadByteArray(), dim);
                if (cells == null) {
                    Logger.LogWarning("Ignored a region map whose grid could not be read.");
                    yield break;
                }
                SetMap(cells, dim, cellSize, regions);
                if (mapHash != hash) { Logger.LogWarning($"The region map arrived with hash {hash:x8} but holds {mapHash:x8}."); }
            } catch (Exception e) {
                Logger.LogWarning($"Ignored a region map that could not be read: {e.Message}");
                yield break;
            }
            RegionScaleSystem.DrawMinimapOverlay();
        }

        // Authority -> clients: the given regions' current levels.
        internal static void BroadcastLevels(ICollection<RegionData> regions) {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            if (regions.Count == 0 || ZNet.instance.m_peers == null || ZNet.instance.m_peers.Count == 0) { return; }
            ZPackage package = new ZPackage();
            package.Write(regions.Count);
            foreach (RegionData region in regions) {
                package.Write(region.Id);
                package.Write(region.Level);
            }
            ValConfig.RegionLevelRPC.SendPackage(ZNet.instance.m_peers, package);
        }

        internal static IEnumerator OnClientReceiveLevels(long sender, ZPackage package) {
            yield return null;
            if (ZNet.instance == null || ZNet.instance.IsServer()) { yield break; }
            int count = package.ReadInt();
            if (count < 0 || count > MaxRegionCount) { yield break; }
            for (int i = 0; i < count; i++) {
                int id = package.ReadInt();
                int level = package.ReadInt();
                if (grid == null) {
                    pendingLevels[id] = level;
                } else if (id >= 1 && id <= Regions.Count) {
                    Regions[id - 1].Level = level;
                }
            }
            if (count > 0 && grid != null) { RegionScaleSystem.DrawMinimapOverlay(); }
        }
    }
}
