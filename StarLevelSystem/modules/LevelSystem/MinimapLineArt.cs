using Jotunn.Managers;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace StarLevelSystem.modules.LevelSystem {
    // One SLS minimap overlay (the distance rings or the zone outlines), kept cheap to update.
    //
    // Both overlays are sparse line art: tens of thousands of line pixels on a 2048x2048 texture.
    // They used to be redrawn from scratch on every refresh -- a new full-map Color[] (~67 MB), a
    // SetPixels of all four million pixels and an Apply -- and in below-fog mode that refresh ran
    // every ten seconds while the player explored, which made them the largest allocator on a client.
    //
    // Instead the line pixels are kept here as a list of (pixel, colour) in draw order, rebuilt only
    // when the drawing itself changes (geometry, colours, ring centre, zone levels or fog mode):
    //  - The builder coroutine stages the new lines (BeginStage/Stage), spread over frames, and
    //    Commit swaps them in within a single frame: it erases the previous lines pixel by pixel,
    //    writes each new line pixel that may show yet and applies once. A build that is stopped
    //    part-way never reaches Commit, so the overlay keeps showing the previous lines.
    //  - Reveal (new exploration, below-fog mode) writes only the hidden line pixels that are now
    //    explored, and applies only if there were any.
    // Pixels go straight into the texture's own CPU copy, so after a texture's first draw nothing
    // map-sized is allocated or converted. Every Apply also makes Jotunn recompose all map overlays,
    // so applying only when something actually changed matters as much as the upload itself.
    internal sealed class MinimapLineArt {
        private struct LinePixel {
            internal ushort X;
            internal ushort Y;
            internal Color32 Color;
        }

        // Zeroed source for clearing a fresh texture's pixel data; all-zero bytes are transparent.
        private static readonly byte[] ClearBlock = new byte[64 * 1024];

        private readonly string layer;
        // Cached so redraws and reveals don't go back to MinimapManager.GetMapOverlay, which writes a
        // debug log line per call. Jotunn destroys every overlay on Minimap.OnDestroy and a new world
        // has a new Minimap, so the cache is tied to the Minimap it was fetched for (see GetOverlay).
        private MinimapManager.MapOverlay overlay;
        private Minimap overlayMinimap;
        // The texture whose pixel data holds `lines`. Any other texture is a fresh one (new world, or
        // Jotunn recreated the overlay) and is cleared once at its first Commit.
        private Texture2D drawnTexture;
        private int stageSize;

        private List<LinePixel> lines = new List<LinePixel>();
        private List<LinePixel> staged = new List<LinePixel>();
        // Indices into `lines` not written yet because they sit over unexplored map (below-fog mode
        // only). Kept in draw order, so where two lines share a pixel the later one's colour still wins.
        private readonly List<int> hidden = new List<int>();

        internal MinimapLineArt(string layer) {
            this.layer = layer;
        }

        // The Jotunn overlay for this layer, fetched once per Minimap. It is always created
        // ignoreFog: true; see MinimapOverlayFog for why fog masking is done by the line writes
        // instead. Callers gate on MinimapOverlayFog.CanDrawOverlays.
        internal MinimapManager.MapOverlay GetOverlay() {
            Minimap minimap = Minimap.instance;
            if (overlay == null || overlayMinimap != minimap || overlay.OverlayTex == null) {
                overlay = MinimapManager.Instance.GetMapOverlay(layer, ignoreFog: true);
                overlayMinimap = minimap;
            }
            return overlay;
        }

        // Starts a new drawing; the lines staged from here on replace the current ones at Commit.
        internal bool BeginStage() {
            staged.Clear();
            MinimapManager.MapOverlay target = GetOverlay();
            if (target == null) { return false; }
            stageSize = target.TextureSize;
            return true;
        }

        // Adds one line pixel in overlay coordinates; pixels off the texture are dropped. Ring and edge
        // sampling hits the same pixel several times in a row, so a repeat of the previous pixel takes
        // the new colour instead of adding an entry.
        internal void Stage(int x, int y, Color32 color) {
            if (x < 0 || y < 0 || x >= stageSize || y >= stageSize) { return; }
            LinePixel pixel = new LinePixel { X = (ushort)x, Y = (ushort)y, Color = color };
            int last = staged.Count - 1;
            if (last >= 0 && staged[last].X == pixel.X && staged[last].Y == pixel.Y) {
                staged[last] = pixel;
                return;
            }
            staged.Add(pixel);
        }

        // Swaps the staged lines in and applies once. Below fog (aboveFog false) only line pixels over
        // explored map are written; the rest are left for Reveal. Returns false when the overlay
        // texture is gone (the world was torn down while the build was yielded).
        internal bool Commit(bool aboveFog) {
            MinimapManager.MapOverlay target = GetOverlay();
            Texture2D tex = target?.OverlayTex;
            if (tex == null) { return false; }

            bool sameTexture = tex == drawnTexture;
            if (!sameTexture) { ClearTexture(tex); }
            PixelWriter writer = new PixelWriter(tex);
            if (sameTexture) {
                // Only the previous lines were ever written to this texture, so erasing them clears it.
                for (int i = 0; i < lines.Count; i++) { writer.Write(lines[i], default); }
            }

            List<LinePixel> previous = lines;
            lines = staged;
            staged = previous;
            staged.Clear();
            hidden.Clear();

            MinimapOverlayFog.ExploredMask mask = aboveFog ? default : MinimapOverlayFog.ExploredMask.Capture();
            for (int i = 0; i < lines.Count; i++) {
                LinePixel pixel = lines[i];
                if (aboveFog || mask.IsExplored(pixel.X, pixel.Y)) {
                    writer.Write(pixel, pixel.Color);
                } else {
                    hidden.Add(i);
                }
            }
            target.Enabled = true;
            tex.Apply();
            drawnTexture = tex;
            return true;
        }

        // Writes the hidden line pixels that are now explored and applies once if there were any.
        // Exploring away from every line therefore costs one scan of the hidden list and no upload.
        internal void Reveal() {
            if (hidden.Count == 0) { return; }
            if (!TryGetDrawnTexture(out Texture2D tex)) { return; }
            MinimapOverlayFog.ExploredMask mask = MinimapOverlayFog.ExploredMask.Capture();
            if (!mask.IsLive) { return; }

            PixelWriter writer = default;
            bool changed = false;
            int kept = 0;
            for (int i = 0; i < hidden.Count; i++) {
                LinePixel pixel = lines[hidden[i]];
                if (!mask.IsExplored(pixel.X, pixel.Y)) {
                    hidden[kept++] = hidden[i];
                    continue;
                }
                if (!changed) {
                    writer = new PixelWriter(tex);
                    changed = true;
                }
                writer.Write(pixel, pixel.Color);
            }
            hidden.RemoveRange(kept, hidden.Count - kept);
            if (changed) { tex.Apply(); }
        }

        // Erases the drawn lines (zone rebuild), so stale outlines don't linger until the next Commit.
        internal void Clear() {
            if (lines.Count > 0 && TryGetDrawnTexture(out Texture2D tex)) {
                PixelWriter writer = new PixelWriter(tex);
                for (int i = 0; i < lines.Count; i++) { writer.Write(lines[i], default); }
                tex.Apply();
            }
            lines.Clear();
            hidden.Clear();
        }

        internal void Hide() {
            MinimapManager.MapOverlay target = GetOverlay();
            if (target != null) { target.Enabled = false; }
        }

        // Leaving a world: Jotunn destroys the overlay with the Minimap, so forget it and the lines.
        // The lists keep their capacity for the next world.
        internal void Reset() {
            overlay = null;
            overlayMinimap = null;
            drawnTexture = null;
            lines.Clear();
            staged.Clear();
            hidden.Clear();
        }

        // The texture holding the current lines, if it is still this Minimap's live overlay texture.
        private bool TryGetDrawnTexture(out Texture2D tex) {
            tex = drawnTexture;
            return tex != null && overlay != null && overlayMinimap == Minimap.instance && overlay.OverlayTex == tex;
        }

        // Zeroes a texture's whole pixel data, the same transparent result the old full-map clear
        // gave. Done once per texture, before its first draw; after that only line pixels are erased.
        private static void ClearTexture(Texture2D tex) {
            NativeArray<byte> bytes = tex.GetRawTextureData<byte>();
            for (int offset = 0; offset < bytes.Length; offset += ClearBlock.Length) {
                NativeArray<byte>.Copy(ClearBlock, 0, bytes, offset, Math.Min(ClearBlock.Length, bytes.Length - offset));
            }
        }

        // Writes single pixels into a texture's CPU-side data. Jotunn creates overlay textures as
        // RGBA32, whose raw data is one Color32 per pixel, so those are written in place; any other
        // format goes through SetPixel. Get a new writer after anything else modifies the texture.
        private struct PixelWriter {
            private readonly Texture2D tex;
            private readonly int width;
            private readonly bool direct;
            private NativeArray<Color32> raw;

            internal PixelWriter(Texture2D tex) {
                this.tex = tex;
                width = tex.width;
                direct = tex.format == TextureFormat.RGBA32;
                raw = direct ? tex.GetRawTextureData<Color32>() : default;
            }

            internal void Write(LinePixel pixel, Color32 color) {
                if (direct) {
                    raw[pixel.Y * width + pixel.X] = color;
                } else {
                    tex.SetPixel(pixel.X, pixel.Y, color);
                }
            }
        }
    }
}
