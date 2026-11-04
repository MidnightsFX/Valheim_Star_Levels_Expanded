using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.modules.LevelSystem {
    // The region system's life in a world: the server loads or builds the map, clients wait to be sent it, and every peer with
    // a map to look at draws it.
    internal static class RegionScaleSystem {
        private const float MinCellSize = 48f;
        private const int MaxGridDimension = 1024;
        // Sampling runs on the main thread, because the world generator's biome tables are not all safe to read off it.
        private const long SampleBudgetMilliseconds = 4;

        private static Coroutine buildCoroutine;
        // A map whose fill is wanted drawn, and where it is drawn to.
        private static readonly MinimapRegionFill fill = new MinimapRegionFill();

        internal static bool OverlayWanted => ValConfig.EnableRegionScalingBonus.Value && ValConfig.EnableRegionMapOverlay.Value;

        internal static void Initialize() {
            if (ValConfig.EnableRegionScalingBonus.Value == false || ZNet.instance == null) { return; }
            if (ZNet.instance.IsServer() == false) {
                DrawMinimapOverlay();
                return;
            }
            if (RegionScaleSystemData.MapReady) {
                RegionScaleSystemData.Ledger.Start();
                RegionScaleSystemData.BroadcastMap();
                DrawMinimapOverlay();
                return;
            }
            if (RegionScaleSystemData.Building) { return; }
            if (RegionScaleSystemData.LoadRegionData()) {
                RegionScaleSystemData.Ledger.Start();
                RegionScaleSystemData.BroadcastMap();
                DrawMinimapOverlay();
                return;
            }
            RegionScaleSystemData.Building = true;
            buildCoroutine = TaskRunner.Run().StartCoroutine(BuildMap());
        }

        private static IEnumerator BuildMap() {
            Logger.LogInfo("Region map generation started.");
            // As for zones: a mod supplying biomes may still be filling its tables as a dedicated server starts.
            yield return new WaitForSeconds(5f);

            WorldGenerator generator = WorldGenerator.instance;
            if (generator == null) {
                Logger.LogWarning("WorldGenerator not available, cannot build the region map.");
                RegionScaleSystemData.Building = false;
                yield break;
            }

            float radius = WorldSize.TotalRadius;
            float radiusSquared = radius * radius;
            float cellSize = Mathf.Max(MinCellSize, radius * 2f / MaxGridDimension);
            int dim = Mathf.CeilToInt(radius * 2f / cellSize);
            float half = dim * cellSize * 0.5f;
            float waterLevel = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            bool[] land = new bool[dim * dim];
            bool[] mountain = new bool[dim * dim];

            Stopwatch budget = Stopwatch.StartNew();
            for (int z = 0; z < dim; z++) {
                float wz = -half + (z + 0.5f) * cellSize;
                for (int x = 0; x < dim; x++) {
                    float wx = -half + (x + 0.5f) * cellSize;
                    if (wx * wx + wz * wz > radiusSquared) { continue; }
                    Heightmap.Biome biome = BiomeLookup.World(new Vector3(wx, 0f, wz));
                    if (biome == Heightmap.Biome.None) { continue; }
                    // Height rather than the biome: Ocean only means land about 4 m tall, so plenty of underwater ground
                    // reads as Meadows, and the sea past the Ashlands reads as Ashlands.
                    bool above = generator.GetBiomeHeight(biome, wx, wz, out Color _) >= waterLevel;
                    land[z * dim + x] = above;
                    mountain[z * dim + x] = above && BiomeRegistry.TerrainOf(biome) == Heightmap.Biome.Mountain;
                    if (budget.ElapsedMilliseconds >= SampleBudgetMilliseconds) {
                        yield return null;
                        budget.Restart();
                    }
                }
            }

            RegionBuildInput input = new RegionBuildInput {
                Dim = dim,
                CellSize = cellSize,
                Land = land,
                Mountain = mountain,
                TargetSize = ValConfig.RegionTargetSize.Value,
                MinSize = ValConfig.RegionMinSize.Value,
                MountainMinSize = ValConfig.RegionMountainMinSize.Value,
            };
            Task<RegionBuildResult> work = Task.Run(() => RegionBuilder.Build(input));
            while (work.IsCompleted == false) { yield return null; }
            if (work.IsFaulted) {
                Logger.LogError($"The region map could not be built: {work.Exception?.GetBaseException()}");
                RegionScaleSystemData.Building = false;
                yield break;
            }

            RegionScaleSystemData.AdoptBuilt(work.Result, dim, cellSize);
            RegionScaleSystemData.Building = false;
            buildCoroutine = null;
            RegionScaleSystemData.Ledger.Start();
            RegionScaleSystemData.BroadcastMap();
            DrawMinimapOverlay();
        }

        // Sizes only reach the map through here, because cutting it again resets every region's level.
        internal static bool RebuildRegions(out string outcome) {
            if (ValConfig.EnableRegionScalingBonus.Value == false) {
                outcome = "Region scaling is disabled (EnableRegionScalingBonus=false); nothing to rebuild.";
                return false;
            }
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) {
                outcome = "Only the server can rebuild the region map.";
                return false;
            }
            if (RegionScaleSystemData.Building) {
                outcome = "The region map is already building; please wait.";
                return false;
            }
            RegionScaleSystemData.Ledger.Reset();
            RegionScaleSystemData.ClearMap();
            fill.Clear();
            RegionScaleSystemData.Building = true;
            buildCoroutine = TaskRunner.Run().StartCoroutine(BuildMap());
            outcome = "Rebuilding the region map from the world. Region kill counts and levels are reset.";
            return true;
        }

        // Invoked on leaving a world (ZNet.Shutdown). TaskRunner persists across worlds, so the coroutines must be stopped
        // explicitly. A build's worker thread finishes on its own, with nothing left waiting for its result.
        internal static void ResetForWorldChange() {
            if (buildCoroutine != null) {
                TaskRunner.Run().StopCoroutine(buildCoroutine);
                buildCoroutine = null;
            }
            RegionScaleSystemData.ResetState();
            fill.Reset();
        }

        internal static void DrawMinimapOverlay() {
            if (OverlayWanted == false || RegionScaleSystemData.MapReady == false) { return; }
            if (MinimapOverlayFog.CanDrawOverlays() == false || ZNet.instance.IsDedicated()) { return; }
            fill.Draw();
        }

        // Called from the Minimap.Update patch. Not on MinimapOverlayFog's ten second refresh, as nearly every step into the fog
        // uncovers some of the fill, and each upload makes Jotunn compose every overlay again.
        internal static void MaybeReveal() {
            if (OverlayWanted && ValConfig.RegionOverlayAboveFog.Value == false) { fill.MaybeReveal(); }
        }

        internal static void NoteExplored(int mapX, int mapY) {
            fill.NoteExplored(mapX, mapY);
        }

        // Shared map data lands everywhere at once, so it is not worth tracking where.
        internal static void OnSharedExplorationChanged() {
            if (ValConfig.RegionOverlayAboveFog.Value == false) { DrawMinimapOverlay(); }
        }

        public static void OnOverlaySettingChanged(object s, EventArgs e) {
            if (MinimapOverlayFog.CanReactToSettings() == false) { return; }
            if (OverlayWanted) {
                DrawMinimapOverlay();
            } else if (MinimapOverlayFog.CanDrawOverlays()) {
                fill.Hide();
            }
        }

        public static void OnEnabledChanged(object s, EventArgs e) {
            if (MinimapOverlayFog.CanReactToSettings() == false) { return; }
            if (ValConfig.EnableRegionScalingBonus.Value) { Initialize(); }
            OnOverlaySettingChanged(s, e);
        }

        public static void OnColorSettingChanged(object s, EventArgs e) {
            Colorization.UpdateZoneOverlayColorSelection();
            if (MinimapOverlayFog.CanReactToSettings()) { DrawMinimapOverlay(); }
        }
    }
}
