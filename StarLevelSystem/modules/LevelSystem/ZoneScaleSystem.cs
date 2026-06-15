using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class ZoneScaleSystem {
        internal static List<ZoneData> Zones = new List<ZoneData>();
        internal static bool zonesBuilt = false;
        private static bool buildingZones = false;
        private static bool decayRunning = false;
        private static bool overlayAvailable = false;
        private static int overlayUpdates = 0;

        // Grid resolution for island detection (world units per sample)
        private const float GridResolution = 100f;

        // Size constraints (world units)
        private const float MinZoneSize = 750f;
        private const float MaxZoneSize = 1500f;

        public static void Initialize() {
            if (!ValConfig.EnableZoneScalingBonus.Value) { return; }
            if (zonesBuilt || buildingZones) { return; }
            if (ZNet.instance.IsDedicated()) {
                Logger.LogDebug("Server is headless, skipping zone minimap generation.");
            }
            if (LoadZoneData()) {
                Logger.LogInfo($"Zone data loaded from file. {Zones.Count} zones available.");
                zonesBuilt = true;
                StartDecayCoroutine();
                DrawMinimapOverlay();
                return;
            }
            Logger.LogInfo("No zone data found, building zone map from world...");
            buildingZones = true;
            TaskRunner.Run().StartCoroutine(BuildZoneMap());
        }

        private static IEnumerator BuildZoneMap() {
            Logger.LogInfo("Zone map generation started.");
            yield return new WaitForSeconds(5f);

            if (WorldGenerator.instance == null) {
                Logger.LogWarning("WorldGenerator not available, cannot build zone map.");
                buildingZones = false;
                yield break;
            }

            float worldRadius = WorldGenerator.worldSize;
            int gridDim = Mathf.CeilToInt((worldRadius * 2f) / GridResolution);
            bool[,] landGrid = new bool[gridDim, gridDim];

            // PHASE 1: Sample biomes to build land grid
            int updates = 0;
            for (int gx = 0; gx < gridDim; gx++) {
                float wx = -worldRadius + gx * GridResolution;
                for (int gz = 0; gz < gridDim; gz++) {
                    float wz = -worldRadius + gz * GridResolution;
                    Heightmap.Biome biome = WorldGenerator.instance.GetBiome(new Vector3(wx, 0, wz));
                    landGrid[gx, gz] = (biome != Heightmap.Biome.Ocean && biome != Heightmap.Biome.None);
                    updates++;
                    if (updates % 5000 == 0) {
                        yield return new WaitForEndOfFrame();
                    }
                }
            }
            Logger.LogDebug($"Zone map sampling complete. Grid: {gridDim}x{gridDim}");

            // PHASE 2: BFS flood-fill to find connected land regions
            bool[,] visited = new bool[gridDim, gridDim];
            List<List<(int gx, int gz)>> regions = new List<List<(int, int)>>();

            for (int gx = 0; gx < gridDim; gx++) {
                for (int gz = 0; gz < gridDim; gz++) {
                    if (!landGrid[gx, gz] || visited[gx, gz]) { continue; }
                    var region = new List<(int, int)>();
                    var queue = new Queue<(int, int)>();
                    queue.Enqueue((gx, gz));
                    visited[gx, gz] = true;
                    while (queue.Count > 0) {
                        var (cx, cz) = queue.Dequeue();
                        region.Add((cx, cz));
                        foreach (var (nx, nz) in Neighbors(cx, cz)) {
                            if (nx < 0 || nx >= gridDim || nz < 0 || nz >= gridDim) { continue; }
                            if (visited[nx, nz] || !landGrid[nx, nz]) { continue; }
                            visited[nx, nz] = true;
                            queue.Enqueue((nx, nz));
                        }
                    }
                    regions.Add(region);
                    if (regions.Count % 10 == 0) { yield return new WaitForEndOfFrame(); }
                }
            }
            Logger.LogDebug($"Found {regions.Count} connected land regions.");

            // PHASE 3: Convert regions to candidate zones (compute bounding boxes)
            var candidateZones = new List<ZoneData>();
            foreach (var region in regions) {
                int minGX = region.Min(r => r.gx);
                int maxGX = region.Max(r => r.gx);
                int minGZ = region.Min(r => r.gz);
                int maxGZ = region.Max(r => r.gz);
                float minX = -worldRadius + minGX * GridResolution;
                float maxX = -worldRadius + (maxGX + 1) * GridResolution;
                float minZ = -worldRadius + minGZ * GridResolution;
                float maxZ = -worldRadius + (maxGZ + 1) * GridResolution;
                candidateZones.Add(new ZoneData {
                    MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ
                });
            }

            // PHASE 4: Merge small zones into nearest neighbor
            var mergedZones = new List<ZoneData>();
            var smallZones = candidateZones.Where(z => (z.MaxX - z.MinX) < MinZoneSize || (z.MaxZ - z.MinZ) < MinZoneSize).ToList();
            var validZones = candidateZones.Where(z => (z.MaxX - z.MinX) >= MinZoneSize && (z.MaxZ - z.MinZ) >= MinZoneSize).ToList();

            foreach (var small in smallZones) {
                float sCenterX = small.CenterX;
                float sCenterZ = small.CenterZ;
                ZoneData nearest = validZones.Count > 0
                    ? validZones.OrderBy(z => Mathf.Pow(z.CenterX - sCenterX, 2) + Mathf.Pow(z.CenterZ - sCenterZ, 2)).First()
                    : (mergedZones.Count > 0 ? mergedZones.OrderBy(z => Mathf.Pow(z.CenterX - sCenterX, 2) + Mathf.Pow(z.CenterZ - sCenterZ, 2)).First() : null);
                if (nearest != null) {
                    nearest.MinX = Mathf.Min(nearest.MinX, small.MinX);
                    nearest.MaxX = Mathf.Max(nearest.MaxX, small.MaxX);
                    nearest.MinZ = Mathf.Min(nearest.MinZ, small.MinZ);
                    nearest.MaxZ = Mathf.Max(nearest.MaxZ, small.MaxZ);
                } else {
                    mergedZones.Add(small);
                }
            }
            mergedZones.AddRange(validZones);

            // PHASE 5: Subdivide large zones into MaxZoneSize cells
            var finalZones = new List<ZoneData>();
            foreach (var zone in mergedZones) {
                float width = zone.MaxX - zone.MinX;
                float depth = zone.MaxZ - zone.MinZ;
                if (width <= MaxZoneSize && depth <= MaxZoneSize) {
                    finalZones.Add(zone);
                    continue;
                }
                int colCount = Mathf.CeilToInt(width / MaxZoneSize);
                int rowCount = Mathf.CeilToInt(depth / MaxZoneSize);
                float cellW = width / colCount;
                float cellD = depth / rowCount;
                for (int col = 0; col < colCount; col++) {
                    for (int row = 0; row < rowCount; row++) {
                        float cellMinX = zone.MinX + col * cellW;
                        float cellMaxX = cellMinX + cellW;
                        float cellMinZ = zone.MinZ + row * cellD;
                        float cellMaxZ = cellMinZ + cellD;
                        // Only keep cells that contain at least one land sample
                        bool hasLand = false;
                        for (int gx = 0; gx < gridDim && !hasLand; gx++) {
                            float wx = -worldRadius + gx * GridResolution;
                            if (wx < cellMinX || wx > cellMaxX) { continue; }
                            for (int gz = 0; gz < gridDim && !hasLand; gz++) {
                                float wz = -worldRadius + gz * GridResolution;
                                if (wz >= cellMinZ && wz <= cellMaxZ && landGrid[gx, gz]) {
                                    hasLand = true;
                                }
                            }
                        }
                        if (hasLand) {
                            finalZones.Add(new ZoneData {
                                MinX = cellMinX, MaxX = cellMaxX,
                                MinZ = cellMinZ, MaxZ = cellMaxZ
                            });
                        }
                    }
                }
            }

            // Assign IDs and finalize
            int idCounter = 0;
            foreach (var zone in finalZones) {
                zone.ZoneId = idCounter++;
                zone.LastDecayTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            Zones = finalZones;
            zonesBuilt = true;
            buildingZones = false;
            Logger.LogInfo($"Zone map built: {Zones.Count} zones created.");
            SaveZoneData();
            StartDecayCoroutine();
            DrawMinimapOverlay();
        }

        private static IEnumerable<(int, int)> Neighbors(int x, int z) {
            yield return (x - 1, z);
            yield return (x + 1, z);
            yield return (x, z - 1);
            yield return (x, z + 1);
        }

        internal static ZoneData GetZoneForPosition(Vector3 pos) {
            foreach (var zone in Zones) {
                if (zone.ContainsPosition(pos)) { return zone; }
            }
            return null;
        }

        internal static SortedDictionary<int, float> SelectZoneLevelBonus(ZoneData zone) {
            var result = new SortedDictionary<int, float>();
            if (zone == null || zone.ZoneLevel <= 1) { return result; }
            float bonus = (zone.ZoneLevel - 1) * ValConfig.ZoneLevelBonusPerLevel.Value;
            if (LevelSystemData.SLE_Level_Settings?.DefaultCreatureLevelUpChance == null) { return result; }
            foreach (var key in LevelSystemData.SLE_Level_Settings.DefaultCreatureLevelUpChance.Keys) {
                result[key] = bonus;
            }
            return result;
        }

        internal static void OnCreatureKilled(Vector3 pos) {
            if (!ValConfig.EnableZoneScalingBonus.Value) { return; }
            if (!ZNet.instance.IsServer()) { return; }
            if (!zonesBuilt) { return; }
            ZoneData zone = GetZoneForPosition(pos);
            if (zone == null) { return; }
            zone.TotalKills++;
            if (zone.TotalKills % ValConfig.ZoneKillsPerLevelUp.Value == 0) {
                zone.ZoneLevel++;
                Logger.LogDebug($"Zone {zone.ZoneId} leveled up to {zone.ZoneLevel} after {zone.TotalKills} kills.");
                SaveZoneData();
                TaskRunner.Run().StartCoroutine(UpdateMinimapOverlay());
            } else if (zone.TotalKills % 10 == 0) {
                SaveZoneData();
            }
        }

        private static void StartDecayCoroutine() {
            if (decayRunning) { return; }
            decayRunning = true;
            TaskRunner.Run().StartCoroutine(DecayZoneLevels());
        }

        private static IEnumerator DecayZoneLevels() {
            while (true) {
                yield return new WaitForSeconds(60f);
                if (!zonesBuilt || Zones.Count == 0) { continue; }
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                bool changed = false;
                foreach (var zone in Zones) {
                    if (zone.ZoneLevel <= 1) { continue; }
                    double elapsedHours = (now - zone.LastDecayTimestamp) / 3600.0;
                    if (elapsedHours >= 1.0) {
                        int hoursDecayed = (int)elapsedHours;
                        zone.ZoneLevel = Mathf.Max(1, zone.ZoneLevel - hoursDecayed);
                        zone.LastDecayTimestamp = now;
                        changed = true;
                    }
                }
                if (changed) {
                    Logger.LogDebug("Zone levels decayed.");
                    SaveZoneData();
                    TaskRunner.Run().StartCoroutine(UpdateMinimapOverlay());
                }
            }
        }

        private static void SaveZoneData() {
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                var saveData = new ZoneSystemSaveData {
                    Zones = Zones,
                    WorldName = ZNet.instance?.GetWorldName() ?? "unknown"
                };
                File.WriteAllText(ValConfig.zoneDataSavedDataPath, DataObjects.yamlserializer.Serialize(saveData));
            } catch (Exception e) {
                Logger.LogWarning($"Failed to save zone data: {e.Message}");
            }
        }

        private static bool LoadZoneData() {
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                if (!File.Exists(ValConfig.zoneDataSavedDataPath)) { return false; }
                string yaml = File.ReadAllText(ValConfig.zoneDataSavedDataPath);
                var loaded = DataObjects.yamldeserializer.Deserialize<ZoneSystemSaveData>(yaml);
                if (loaded?.Zones == null || loaded.Zones.Count == 0) { return false; }
                string currentWorld = ZNet.instance?.GetWorldName() ?? "";
                if (!string.IsNullOrEmpty(loaded.WorldName) && loaded.WorldName != currentWorld) {
                    Logger.LogInfo($"Zone data is for a different world ({loaded.WorldName} vs {currentWorld}), rebuilding.");
                    return false;
                }
                Zones = loaded.Zones;
                return true;
            } catch (Exception e) {
                Logger.LogWarning($"Failed to load zone data: {e.Message}");
                return false;
            }
        }

        private static void DrawMinimapOverlay() {
            if (!ValConfig.EnableZoneMapOverlay.Value) { return; }
            if (ZNet.instance.IsDedicated()) { return; }
            TaskRunner.Run().StartCoroutine(BuildZoneMapOverlay());
        }

        private static IEnumerator UpdateMinimapOverlay() {
            if (!ValConfig.EnableZoneMapOverlay.Value || ZNet.instance.IsDedicated()) { yield break; }
            if (overlayAvailable) {
                MinimapManager.MapOverlay existing = MinimapManager.Instance.GetMapOverlay("SLS-ZoneLevels");
                if (existing != null) {
                    int mapSize = existing.TextureSize * existing.TextureSize;
                    existing.OverlayTex.SetPixels(new Color[mapSize]);
                    existing.OverlayTex.Apply();
                    overlayAvailable = false;
                }
            }
            yield return BuildZoneMapOverlay();
        }

        private static IEnumerator BuildZoneMapOverlay() {
            if (!ValConfig.EnableZoneMapOverlay.Value || ZNet.instance.IsDedicated()) { yield break; }
            if (!zonesBuilt || Zones.Count == 0) { yield break; }
            if (Minimap.instance == null) { yield break; }

            MinimapManager.MapOverlay zoneOverlay = MinimapManager.Instance.GetMapOverlay("SLS-ZoneLevels");
            zoneOverlay.Enabled = true;
            int texSize = zoneOverlay.TextureSize;
            int mapSize = texSize * texSize;
            Color[] pixels = new Color[mapSize];
            zoneOverlay.OverlayTex.SetPixels(pixels);

            List<Color> colors = Colorization.mapRingColors;
            Color defaultColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

            overlayUpdates = 0;
            foreach (var zone in Zones) {
                Color zoneColor = zone.ZoneLevel <= 1
                    ? defaultColor
                    : colors != null && colors.Count > 0
                        ? colors[(zone.ZoneLevel - 1) % colors.Count]
                        : defaultColor;

                // Draw the 4 edges of the zone rectangle
                yield return DrawZoneEdge(pixels, texSize, zone.MinX, zone.MinZ, zone.MaxX, zone.MinZ, zoneColor);
                yield return DrawZoneEdge(pixels, texSize, zone.MinX, zone.MaxZ, zone.MaxX, zone.MaxZ, zoneColor);
                yield return DrawZoneEdge(pixels, texSize, zone.MinX, zone.MinZ, zone.MinX, zone.MaxZ, zoneColor);
                yield return DrawZoneEdge(pixels, texSize, zone.MaxX, zone.MinZ, zone.MaxX, zone.MaxZ, zoneColor);
            }

            if (zoneOverlay == null) { yield break; }
            zoneOverlay.OverlayTex.SetPixels(pixels);
            zoneOverlay.OverlayTex.Apply();
            overlayAvailable = true;
            Logger.LogDebug($"Zone map overlay drawn for {Zones.Count} zones.");
        }

        private static IEnumerator DrawZoneEdge(Color[] pixels, int texSize, float x0, float z0, float x1, float z1, Color color) {
            float dx = x1 - x0;
            float dz = z1 - z0;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 10f));
            for (int i = 0; i <= steps; i++) {
                float t = (float)i / steps;
                float wx = x0 + dx * t;
                float wz = z0 + dz * t;
                Minimap.instance.WorldToPixel(new Vector3(wx, 0, wz), out int px, out int pz);
                int idx = pz * texSize + px;
                if (idx >= 0 && idx < pixels.Length) {
                    pixels[idx] = color;
                }
                overlayUpdates++;
                if (overlayUpdates % 3000 == 0) {
                    yield return new WaitForEndOfFrame();
                }
            }
        }
    }
}
