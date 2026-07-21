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

        // Grid resolution for island detection (world units per sample)
        private const float GridResolution = 100f;
        private const string ZoneLayer = "SLS-ZoneLevels";
        private static Color defaultColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

        private static readonly List<SerializableVector3> pendingDeaths = new List<SerializableVector3>();
        private static bool flushRunning = false;
        // Tracks Overlay rebuilds to prevent flickering
        private static Coroutine overlayRebuildCoroutine;
        // Handles for the long-running zone coroutines so they can be stopped on world unload
        // (TaskRunner is DontDestroyOnLoad, so they would otherwise survive across worlds).
        private static Coroutine flushCoroutine;
        private static Coroutine buildCoroutine;

        public static void Initialize() {
            if (!ValConfig.EnableZoneScalingBonus.Value) { return; }
            if (ZoneScaleSystemData.zonesBuilt || ZoneScaleSystemData.buildingZones) { return; }
            if (ZNet.instance.IsDedicated()) {
                Logger.LogDebug("Server is headless, skipping zone minimap generation.");
            }
            if (ZoneScaleSystemData.LoadZoneData()) {
                Logger.LogInfo($"Zone data loaded from file. {ZoneScaleSystemData.Zones.Count} zones available.");
                ZoneScaleSystemData.zonesBuilt = true;
                ZoneScaleSystemData.StartDecayCoroutine();
                DrawMinimapOverlay();
                return;
            }
            Logger.LogInfo("No zone data found, building zone map from world...");
            ZoneScaleSystemData.buildingZones = true;
            buildCoroutine = TaskRunner.Run().StartCoroutine(BuildZoneMap());
        }

        private static IEnumerator BuildZoneMap() {
            Logger.LogInfo("Zone map generation started.");
            yield return new WaitForSeconds(5f);

            if (WorldGenerator.instance == null) {
                Logger.LogWarning("WorldGenerator not available, cannot build zone map.");
                ZoneScaleSystemData.buildingZones = false;
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

            // PHASE 3: Tile qualifying land onto a single global grid of MaxZoneSize cells.
            // A region qualifies only if its bounding box is at least MinZoneSize on both axes;
            // trivial islets are dropped (no zone). Every emitted zone is a cell of one global
            // grid anchored at the world min corner, so any two zones are either identical
            // (deduped below) or disjoint -> guaranteed non-overlapping.
            float cellSize = ValConfig.MaxZoneSize.Value;
            var cellZones = new Dictionary<long, ZoneData>();
            foreach (var region in regions) {
                int minGX = region.Min(r => r.gx);
                int maxGX = region.Max(r => r.gx);
                int minGZ = region.Min(r => r.gz);
                int maxGZ = region.Max(r => r.gz);
                float regionWidth = (maxGX - minGX + 1) * GridResolution;
                float regionDepth = (maxGZ - minGZ + 1) * GridResolution;
                if (regionWidth < ValConfig.MinZoneSize.Value || regionDepth < ValConfig.MinZoneSize.Value) {
                    continue; // island too small to scale; skip it entirely
                }
                foreach (var (gx, gz) in region) {
                    // Sample center -> global cell index (floored from the world min corner).
                    float wx = -worldRadius + (gx + 0.5f) * GridResolution;
                    float wz = -worldRadius + (gz + 0.5f) * GridResolution;
                    int gcx = Mathf.FloorToInt((wx + worldRadius) / cellSize);
                    int gcz = Mathf.FloorToInt((wz + worldRadius) / cellSize);
                    long key = ((long)(uint)gcx << 32) | (uint)gcz;
                    if (cellZones.ContainsKey(key)) { continue; }
                    float cellMinX = -worldRadius + gcx * cellSize;
                    float cellMinZ = -worldRadius + gcz * cellSize;
                    cellZones[key] = new ZoneData {
                        // Deterministic id from the cell coords so every peer agrees (see ZoneIdForCell).
                        ZoneId = ZoneScaleSystemData.ZoneIdForCell(gcx, gcz),
                        MinX = cellMinX, MaxX = cellMinX + cellSize,
                        MinZ = cellMinZ, MaxZ = cellMinZ + cellSize
                    };
                }
            }
            var finalZones = cellZones.Values.ToList();

            // Finalize (ids are already assigned deterministically from cell coords above).
            foreach (var zone in finalZones) {
                zone.LastDecayTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            ZoneScaleSystemData.Zones = finalZones;
            ZoneScaleSystemData.BuildZoneIndex();
            ZoneScaleSystemData.zonesBuilt = true;
            ZoneScaleSystemData.buildingZones = false;
            Logger.LogInfo($"Zone map built: {ZoneScaleSystemData.Zones.Count} zones created.");
            ZoneScaleSystemData.SaveZoneData();
            ZoneScaleSystemData.StartDecayCoroutine();
            DrawMinimapOverlay();
        }

        internal static void RebuildZones() {
            if (!ValConfig.EnableZoneScalingBonus.Value) {
                Logger.LogInfo("Zone scaling is disabled (EnableZoneScalingBonus=false); nothing to rebuild.");
                return;
            }
            if (ZNet.instance == null) {
                Logger.LogInfo("Not in a world; cannot rebuild zones.");
                return;
            }
            if (ZoneScaleSystemData.buildingZones) {
                Logger.LogInfo("Zone map is already building; please wait.");
                return;
            }

            // Clear the existing minimap overlay so stale boundaries don't linger during the rebuild.
            if (ZoneScaleSystemData.overlayAvailable && !ZNet.instance.IsDedicated() && MinimapManager.Instance != null) {
                var existing = MinimapManager.Instance.GetMapOverlay(ZoneLayer, ignoreFog: ValConfig.ZoneOverlayAboveFog.Value);
                if (existing != null) {
                    int mapSize = existing.TextureSize * existing.TextureSize;
                    existing.OverlayTex.SetPixels(new Color[mapSize]);
                    existing.OverlayTex.Apply();
                }
            }

            // Reset state and regenerate from the world.
            ZoneScaleSystemData.Zones = new List<ZoneData>();
            ZoneScaleSystemData.BuildZoneIndex();
            ZoneScaleSystemData.zonesBuilt = false;
            ZoneScaleSystemData.overlayAvailable = false;
            ZoneScaleSystemData.buildingZones = true;
            Logger.LogInfo("Rebuilding zone map from world...");
            buildCoroutine = TaskRunner.Run().StartCoroutine(BuildZoneMap());
        }

        // Invoked on leaving a world (ZNet.Shutdown). Stops the zone coroutines and clears all zone
        // state so joining another world/server rebuilds and re-syncs from scratch instead of reusing
        // stale geometry. TaskRunner persists across worlds, so these must be stopped explicitly.
        internal static void ResetForWorldChange() {
            Orchestrator runner = TaskRunner.Run();
            if (buildCoroutine != null) { runner.StopCoroutine(buildCoroutine); buildCoroutine = null; }
            if (flushCoroutine != null) { runner.StopCoroutine(flushCoroutine); flushCoroutine = null; }
            if (overlayRebuildCoroutine != null) { runner.StopCoroutine(overlayRebuildCoroutine); overlayRebuildCoroutine = null; }
            pendingDeaths.Clear();
            flushRunning = false;
            ZoneScaleSystemData.ResetState();
        }

        private static IEnumerable<(int, int)> Neighbors(int x, int z) {
            yield return (x - 1, z);
            yield return (x + 1, z);
            yield return (x, z - 1);
            yield return (x, z + 1);
        }

        // Runs on the creature's owner peer (which may be a client, not the server). We only record
        // the death position and let the batched flush coroutine report it to the server.
        internal static void OnCreatureKilled(Vector3 pos) {
            if (!ValConfig.EnableZoneScalingBonus.Value) { return; }
            pendingDeaths.Add(pos);
            StartKillReportFlush();
        }

        private static void StartKillReportFlush() {
            if (flushRunning) { return; }
            flushRunning = true;
            flushCoroutine = TaskRunner.Run().StartCoroutine(FlushKillReports());
        }

        // Periodically drains pendingDeaths. The authority (dedicated server or host) applies them
        // directly; a remote client batches them into a single RPC to the server.
        private static IEnumerator FlushKillReports() {
            while (true) {
                yield return new WaitForSeconds(ValConfig.KillReportFlushIntervalSeconds.Value);
                if (pendingDeaths.Count == 0) { continue; }
                if (ZNet.instance == null) { pendingDeaths.Clear(); continue; }
                List<SerializableVector3> batch = new List<SerializableVector3>(pendingDeaths);
                pendingDeaths.Clear();
                if (ZNet.instance.IsServer()) {
                    ZoneScaleSystemData.ApplyDeaths(batch);
                } else {
                    ZNetPeer server = ZNet.instance.GetServerPeer();
                    if (server == null) { continue; }
                    ValConfig.ZoneKillReportRPC.SendPackage(server.m_uid, ZoneScaleSystemData.SerializeDeaths(batch));
                }
            }
        }

        // Single entrypoint for map overlay building
        internal static void DrawMinimapOverlay() {
            if (!ValConfig.EnableZoneMapOverlay.Value) { return; }
            // Skip while in the main menu or on a loading screen (no live minimap yet); the overlay is
            // (re)drawn from OnVanillaMapDataLoaded once the map is ready. Also skip on a headless server.
            if (!MinimapOverlayFog.CanDrawOverlays() || ZNet.instance.IsDedicated()) { return; }
            Orchestrator runner = TaskRunner.Run();
            if (overlayRebuildCoroutine != null) { runner.StopCoroutine(overlayRebuildCoroutine); }
            overlayRebuildCoroutine = runner.StartCoroutine(BuildZoneMapOverlay());
        }

        public static void UpdateZoneOverlayColorsOnChange(object s, EventArgs e) {
            Colorization.UpdateZoneOverlayColorSelection();
            if (ZNet.instance == null || ZNet.instance.IsDedicated()) { return; }
            DrawMinimapOverlay();
        }

        // SettingChanged handler for ZoneOverlayAboveFog: redraw so the overlay's fog flag is
        // re-synced (see BuildZoneMapOverlay) and recomposed above/below the fog as configured.
        public static void UpdateZoneOverlayFogOnChange(object s, EventArgs e) {
            if (ZNet.instance == null || ZNet.instance.IsDedicated()) { return; }
            DrawMinimapOverlay();
        }

        private static IEnumerator BuildZoneMapOverlay() {
            if (!ValConfig.EnableZoneMapOverlay.Value || ZNet.instance.IsDedicated()) { yield break; }
            if (!ZoneScaleSystemData.zonesBuilt || ZoneScaleSystemData.Zones.Count == 0) { yield break; }
            if (Minimap.instance == null) { yield break; }

            // ZoneOverlayAboveFog controls whether boundaries render across the whole map (above the
            // fog) or only in explored areas (below the fog). Sync the flag in case the config was
            // toggled after the overlay was first created; the SetPixels/Apply below recomposes it.
            MinimapManager.MapOverlay zoneOverlay = MinimapManager.Instance.GetMapOverlay(ZoneLayer, ignoreFog: ValConfig.ZoneOverlayAboveFog.Value);
            zoneOverlay.Enabled = true;
            MinimapOverlayFog.SetIgnoreFog(zoneOverlay, ValConfig.ZoneOverlayAboveFog.Value);
            int texSize = zoneOverlay.TextureSize;
            int mapSize = texSize * texSize;
            // Build the whole frame into this local buffer; the live OverlayTex is left untouched
            // until the single atomic SetPixels/Apply at the end, so the old overlay stays visible.
            Color[] pixels = new Color[mapSize];

            List<Color> colors = Colorization.zoneOverlayColors;


            // Configured pixel inset -> world units via the live minimap scale, so each outline is
            // drawn slightly inside its cell and adjacent zones don't collapse onto one shared line.
            Minimap.instance.WorldToPixel(Vector3.zero, out int sp0x, out _);
            Minimap.instance.WorldToPixel(new Vector3(5000f, 0f, 0f), out int sp1x, out _);
            float pixelsPerWorld = Mathf.Abs(sp1x - sp0x) / 5000f;
            float outlineInset = pixelsPerWorld > 0f ? 1 / pixelsPerWorld : 0f;

            ZoneScaleSystemData.overlayUpdates = 0;
            foreach (var zone in ZoneScaleSystemData.Zones) {
                Color zoneColor = defaultColor;
                if (zone.ZoneLevel >= 1 && colors != null && colors.Count > 0) {
                    zoneColor = colors[(zone.ZoneLevel - 1) % colors.Count];
                }
                zoneColor.a = ValConfig.ZoneOverlayColorTransparency.Value;

                // Draw the 4 edges of the zone rectangle, inset slightly so neighbouring cells
                // render as two parallel lines rather than one merged boundary. Fall back to the
                // raw bounds if a cell is too small for the inset.
                float ix0 = zone.MinX + outlineInset, ix1 = zone.MaxX - outlineInset;
                float iz0 = zone.MinZ + outlineInset, iz1 = zone.MaxZ - outlineInset;
                if (ix1 <= ix0 || iz1 <= iz0) { ix0 = zone.MinX; ix1 = zone.MaxX; iz0 = zone.MinZ; iz1 = zone.MaxZ; }
                yield return DrawZoneEdge(pixels, texSize, ix0, iz0, ix1, iz0, zoneColor); // bottom
                yield return DrawZoneEdge(pixels, texSize, ix0, iz1, ix1, iz1, zoneColor); // top
                yield return DrawZoneEdge(pixels, texSize, ix0, iz0, ix0, iz1, zoneColor); // left
                yield return DrawZoneEdge(pixels, texSize, ix1, iz0, ix1, iz1, zoneColor); // right
            }

            if (zoneOverlay == null) { yield break; }
            zoneOverlay.OverlayTex.SetPixels(pixels);
            zoneOverlay.OverlayTex.Apply();
            ZoneScaleSystemData.overlayAvailable = true;
            Logger.LogDebug($"Zone map overlay drawn for {ZoneScaleSystemData.Zones.Count} zones.");
        }

        private static IEnumerator DrawZoneEdge(Color[] pixels, int texSize, float x0, float z0, float x1, float z1, Color color) {
            float dx = x1 - x0;
            float dz = z1 - z0;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 10f));
            int border = 1; // Configure border size
            int lo = -(border - 1) / 2;
            int hi = border / 2;
            for (int i = 0; i <= steps; i++) {
                float t = (float)i / steps;
                float wx = x0 + dx * t;
                float wz = z0 + dz * t;
                Minimap.instance.WorldToPixel(new Vector3(wx, 0, wz), out int px, out int pz);
                for (int oz = lo; oz <= hi; oz++) {
                    int bz = pz + oz;
                    if (bz < 0 || bz >= texSize) { continue; }
                    for (int ox = lo; ox <= hi; ox++) {
                        int bx = px + ox;
                        if (bx < 0 || bx >= texSize) { continue; }
                        pixels[bz * texSize + bx] = color;
                    }
                }
                ZoneScaleSystemData.overlayUpdates++;
                if (ZoneScaleSystemData.overlayUpdates % 3000 == 0) {
                    yield return new WaitForEndOfFrame();
                }
            }
        }
    }
}
