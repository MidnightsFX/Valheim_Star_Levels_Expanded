using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    // The region map on the minimap: a low-opacity fill over each region, in the colour of its level, with a stronger line
    // around it so neighbours at the same level stay apart.
    //
    // MinimapLineArt keeps a list of its pixels, which suits lines. A fill is a million pixels, and every one of them can be
    // worked out from the region grid and the map's own height texture, so nothing is kept but the grid. Pixels go straight into
    // the texture's CPU copy and show at the Apply, so a redraw can be spread over frames and still appears all at once.
    internal sealed class MinimapRegionFill {
        private const string Layer = "SLS-RegionLevels";
        private const long PaintBudgetMilliseconds = 4;
        // Each upload makes Jotunn compose every overlay again, so uncovered fill waits for a while. It should not wait on a map
        // someone is looking at.
        private const float RevealInterval = 30f;
        private const float RevealIntervalOnLargeMap = 2f;
        private const float BorderOpacityScale = 2.5f;

        private MinimapManager.MapOverlay overlay;
        private Minimap overlayMinimap;
        private Texture2D clearedTexture;
        private bool hidden = true;
        private Coroutine drawCoroutine;
        private bool redrawQueued;

        // Which of the map's pixels have been uncovered since the last reveal, as one rectangle. Empty when maxX < minX.
        private int dirtyMinX = int.MaxValue, dirtyMinY = int.MaxValue, dirtyMaxX = int.MinValue, dirtyMaxY = int.MinValue;
        private float nextReveal;

        // Everything a pass over the texture needs, gathered once for that pass.
        private sealed class Painter {
            internal int Size;
            internal int MapSize;
            internal bool AboveFog;
            internal MinimapOverlayFog.ExploredMask Explored;
            internal ushort[] Grid;
            internal int GridDim;
            // The grid cell under each overlay column and row, -1 past the grid's edge.
            internal int[] CellOfPixel;
            internal NativeArray<ushort> Heights;
            internal bool HasHeights;
            internal ushort WaterLevelHalf;
            internal Color32[] FillOf;
            internal Color32[] BorderOf;
        }

        internal void Draw() {
            if (drawCoroutine != null) {
                // Whatever asked for this may have changed what the running pass has already painted.
                redrawQueued = true;
                return;
            }
            drawCoroutine = TaskRunner.Run().StartCoroutine(DrawPass());
        }

        private IEnumerator DrawPass() {
            do {
                redrawQueued = false;
                MinimapManager.MapOverlay target = GetOverlay();
                Texture2D tex = target?.OverlayTex;
                if (tex == null) { break; }
                if (tex != clearedTexture) {
                    // Jotunn's new overlay only copied its blank texture on the GPU, so the CPU copy holds nothing useful yet.
                    ClearTexture(tex);
                    clearedTexture = tex;
                }
                Painter painter = MakePainter(target);
                ClearDirty();
                Stopwatch took = Stopwatch.StartNew();
                int frames = 0;
                int row = 0;
                while (row < painter.Size) {
                    row = PaintRowsWithinBudget(tex, painter, row);
                    frames++;
                    yield return null;
                    if (MinimapOverlayFog.CanDrawOverlays() == false || RegionScaleSystem.OverlayWanted == false || RegionScaleSystemData.MapReady == false) {
                        drawCoroutine = null;
                        yield break;
                    }
                }
                tex.Apply();
                Logger.LogDebug($"Region overlay drawn over {frames} frames, {took.ElapsedMilliseconds} ms from start to upload.");
                // Only a first draw or one after Hide turns it on: the player may have unticked it in Jotunn's overlay panel.
                if (hidden) {
                    target.Enabled = true;
                    hidden = false;
                }
            } while (redrawQueued);
            drawCoroutine = null;
        }

        private Painter MakePainter(MinimapManager.MapOverlay target) {
            Minimap map = Minimap.instance;
            Painter painter = new Painter {
                Size = target.TextureSize,
                MapSize = map.m_textureSize,
                AboveFog = ValConfig.RegionOverlayAboveFog.Value,
                Explored = MinimapOverlayFog.ExploredMask.Capture(),
                Grid = RegionScaleSystemData.Grid,
                GridDim = RegionScaleSystemData.GridDim,
            };

            // The overlay lies over the whole map image whatever either one's resolution, so its pixels divide up the map's width.
            float worldPerPixel = map.m_textureSize * map.m_pixelSize / painter.Size;
            float mapHalf = map.m_textureSize * map.m_pixelSize * 0.5f;
            float gridHalf = RegionScaleSystemData.GridHalfExtent;
            float cellSize = RegionScaleSystemData.GridCellSize;
            painter.CellOfPixel = new int[painter.Size];
            for (int i = 0; i < painter.Size; i++) {
                float world = -mapHalf + (i + 0.5f) * worldPerPixel;
                int cell = Mathf.FloorToInt((world + gridHalf) / cellSize);
                painter.CellOfPixel[i] = cell >= 0 && cell < painter.GridDim ? cell : -1;
            }

            // The grid knows sections, not shores: cells spill a little past the coast so a creature in the shallows has a
            // region. Painting only over ground gives the fill the map's own coastline and leaves rivers showing through it.
            Texture2D heights = map.m_heightTexture;
            if (heights != null && heights.format == TextureFormat.RHalf && heights.width == painter.MapSize) {
                painter.Heights = heights.GetRawTextureData<ushort>();
                painter.HasHeights = painter.Heights.Length == painter.MapSize * painter.MapSize;
                painter.WaterLevelHalf = Mathf.FloatToHalf(ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f);
            }

            List<RegionData> regions = RegionScaleSystemData.Regions;
            painter.FillOf = new Color32[regions.Count + 1];
            painter.BorderOf = new Color32[regions.Count + 1];
            List<Color> colors = Colorization.zoneOverlayColors;
            float opacity = ValConfig.RegionOverlayOpacity.Value;
            foreach (RegionData region in regions) {
                Color color = colors != null && colors.Count > 0 ? colors[(Mathf.Max(1, region.Level) - 1) % colors.Count] : Color.gray;
                painter.FillOf[region.Id] = ComposedColor(color, opacity);
                painter.BorderOf[region.Id] = ComposedColor(color, Mathf.Min(1f, opacity * BorderOpacityScale));
            }
            return painter;
        }

        // Jotunn composes overlays with lerp(below.rgba, overlay.rgba, overlay.a), alpha included, starting from nothing, so what
        // ends up on the map image has an alpha of a*a and a colour of rgb*a. This writes what comes out as the colour and
        // opacity asked for, as near as it can: brightness has to give at low opacity, since rgb*a can't exceed a.
        private static Color32 ComposedColor(Color color, float opacity) {
            if (opacity <= 0f) { return default; }
            float a = Mathf.Sqrt(opacity);
            return new Color(Mathf.Min(1f, color.r / a), Mathf.Min(1f, color.g / a), Mathf.Min(1f, color.b / a), a);
        }

        // Returns the row to carry on from. At least one row a frame, so a very slow frame still gets somewhere.
        private static int PaintRowsWithinBudget(Texture2D tex, Painter painter, int fromRow) {
            NativeArray<Color32> raw = tex.GetRawTextureData<Color32>();
            int size = painter.Size;
            Stopwatch budget = Stopwatch.StartNew();
            int y = fromRow;
            do {
                for (int x = 0; x < size; x++) {
                    raw[y * size + x] = PixelFor(painter, x, y);
                }
                y++;
            } while (y < size && budget.ElapsedMilliseconds < PaintBudgetMilliseconds);
            return y;
        }

        private static ushort RegionAt(Painter painter, int x, int y) {
            if (x < 0 || y < 0 || x >= painter.Size || y >= painter.Size) { return 0; }
            int cellX = painter.CellOfPixel[x], cellZ = painter.CellOfPixel[y];
            return cellX < 0 || cellZ < 0 ? (ushort)0 : painter.Grid[cellZ * painter.GridDim + cellX];
        }

        private static Color32 PixelFor(Painter painter, int x, int y) {
            ushort region = RegionAt(painter, x, y);
            if (region == 0 || region >= painter.FillOf.Length) { return default; }

            int mapX = painter.MapSize == painter.Size ? x : x * painter.MapSize / painter.Size;
            int mapY = painter.MapSize == painter.Size ? y : y * painter.MapSize / painter.Size;
            if (painter.AboveFog == false && painter.Explored.IsExplored(mapX, mapY) == false) { return default; }
            if (painter.HasHeights && IsGround(painter.Heights[mapY * painter.MapSize + mapX], painter.WaterLevelHalf) == false) { return default; }

            ushort right = RegionAt(painter, x + 1, y), up = RegionAt(painter, x, y + 1);
            bool border = (right != 0 && right != region) || (up != 0 && up != region);
            return border ? painter.BorderOf[region] : painter.FillOf[region];
        }

        // Half floats compare as plain numbers while they are positive, and the top bit is the sign. This runs per pixel, and
        // Mathf.HalfToFloat is a call into the engine.
        private static bool IsGround(ushort heightHalf, ushort waterLevelHalf) {
            return heightHalf < 0x8000 && heightHalf >= waterLevelHalf;
        }

        internal void NoteExplored(int mapX, int mapY) {
            if (mapX < dirtyMinX) { dirtyMinX = mapX; }
            if (mapX > dirtyMaxX) { dirtyMaxX = mapX; }
            if (mapY < dirtyMinY) { dirtyMinY = mapY; }
            if (mapY > dirtyMaxY) { dirtyMaxY = mapY; }
        }

        private void ClearDirty() {
            dirtyMinX = dirtyMinY = int.MaxValue;
            dirtyMaxX = dirtyMaxY = int.MinValue;
        }

        // Writes only what was uncovered since the last time, and uploads only if some of it was fill.
        internal void MaybeReveal() {
            if (dirtyMaxX < dirtyMinX || drawCoroutine != null) { return; }
            Minimap map = Minimap.instance;
            if (map == null) { return; }
            float now = Time.unscaledTime;
            if (now < nextReveal) { return; }
            nextReveal = now + (map.m_mode == Minimap.MapMode.Large ? RevealIntervalOnLargeMap : RevealInterval);
            if (MinimapOverlayFog.CanDrawOverlays() == false || RegionScaleSystemData.MapReady == false) { return; }

            MinimapManager.MapOverlay target = GetOverlay();
            Texture2D tex = target?.OverlayTex;
            if (tex == null || tex != clearedTexture) { return; }

            Painter painter = MakePainter(target);
            NativeArray<Color32> raw = tex.GetRawTextureData<Color32>();
            int size = painter.Size;
            int fromX = Mathf.Clamp(dirtyMinX * size / painter.MapSize - 1, 0, size - 1);
            int toX = Mathf.Clamp(dirtyMaxX * size / painter.MapSize + 1, 0, size - 1);
            int fromY = Mathf.Clamp(dirtyMinY * size / painter.MapSize - 1, 0, size - 1);
            int toY = Mathf.Clamp(dirtyMaxY * size / painter.MapSize + 1, 0, size - 1);
            ClearDirty();

            bool changed = false;
            for (int y = fromY; y <= toY; y++) {
                for (int x = fromX; x <= toX; x++) {
                    Color32 pixel = PixelFor(painter, x, y);
                    Color32 was = raw[y * size + x];
                    if (was.r == pixel.r && was.g == pixel.g && was.b == pixel.b && was.a == pixel.a) { continue; }
                    raw[y * size + x] = pixel;
                    changed = true;
                }
            }
            if (changed) { tex.Apply(); }
        }

        internal void Hide() {
            if (overlay != null && overlayMinimap == Minimap.instance) { overlay.Enabled = false; }
            hidden = true;
        }

        // A rebuild is starting: the old sections should not linger until the new ones arrive.
        internal void Clear() {
            Texture2D tex = overlay != null && overlayMinimap == Minimap.instance ? overlay.OverlayTex : null;
            if (tex == null || tex != clearedTexture) { return; }
            ClearTexture(tex);
            tex.Apply();
        }

        // Leaving a world: Jotunn destroys the overlay with the Minimap.
        internal void Reset() {
            if (drawCoroutine != null) {
                TaskRunner.Run().StopCoroutine(drawCoroutine);
                drawCoroutine = null;
            }
            redrawQueued = false;
            overlay = null;
            overlayMinimap = null;
            clearedTexture = null;
            hidden = true;
            ClearDirty();
        }

        private MinimapManager.MapOverlay GetOverlay() {
            Minimap minimap = Minimap.instance;
            if (overlay == null || overlayMinimap != minimap || overlay.OverlayTex == null) {
                overlay = MinimapManager.Instance.GetMapOverlay(Layer, ignoreFog: true);
                overlayMinimap = minimap;
                hidden = true;
            }
            return overlay;
        }

        private static void ClearTexture(Texture2D tex) {
            NativeArray<Color32> raw = tex.GetRawTextureData<Color32>();
            for (int i = 0; i < raw.Length; i++) { raw[i] = default; }
        }
    }
}
