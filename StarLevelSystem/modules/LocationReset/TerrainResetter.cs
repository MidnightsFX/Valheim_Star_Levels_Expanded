using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.modules.LocationReset {
    // Undoes terraforming in a radius: player-placed TerrainModifiers, raise/level/smooth deltas and
    // paint stored in the zone's TerrainComp, plus the grass clutter that follows the heightmap.
    //
    // This operates on live components rather than rewriting the compressed TCData blob, which is
    // simpler and uses vanilla's own Save/Poke path. That is only valid because the sweep has
    // already poke-loaded the zone -- never call this against an unloaded zone.
    internal static class TerrainResetter {

        // Returns the number of individual modifications undone, for logging and the ZDO accounting.
        internal static int Reset(Vector3 center, float radius) {
            if (radius <= 0f) { return 0; }
            return ResetBatch(new List<Vector3>() { center }, new List<float>() { radius });
        }

        // Batched form: one heightmap collection, one vertex sweep and at most one TerrainComp.Save()
        // per compiler for ALL points together. The per-node form ran the full 65x65 vertex loop and
        // re-saved/re-poked every touched compiler once per crater - a chunk regenerating dozens of
        // ore nodes paid dozens of full sweeps and saves in a single frame.
        internal static int ResetBatch(List<Vector3> centers, List<float> radii) {
            if (centers == null || radii == null || centers.Count == 0 || centers.Count != radii.Count) { return 0; }

            int resets = 0;

            // Union of the heightmaps any point can touch. Heightmaps overlap their neighbours, so
            // search wider than the radius to catch a compiler whose centre is outside it but whose
            // vertices are not.
            List<Heightmap> heightmaps = new List<Heightmap>();
            HashSet<Heightmap> seen = new HashSet<Heightmap>();
            List<Heightmap> found = new List<Heightmap>();
            for (int p = 0; p < centers.Count; p++) {
                if (radii[p] <= 0f) { continue; }
                found.Clear();
                Heightmap.FindHeightmap(centers[p], radii[p] + 100f, found);
                for (int h = 0; h < found.Count; h++) {
                    if (seen.Add(found[h])) { heightmaps.Add(found[h]); }
                }
            }

            resets += ResetModifiers(centers, radii, heightmaps);
            resets += ResetHeightAndPaint(centers, radii, heightmaps);

            if (ClutterSystem.instance != null) {
                for (int p = 0; p < centers.Count; p++) {
                    if (radii[p] > 0f) { ClutterSystem.instance.ResetGrass(centers[p], radii[p]); }
                }
            }

            return resets;
        }

        // TerrainModifier components are the discrete raise/level operations players leave behind.
        // Reverting one means subtracting it from every heightmap it touched, then destroying it.
        private static int ResetModifiers(List<Vector3> centers, List<float> radii, List<Heightmap> heightmaps) {
            int resets = 0;
            List<TerrainModifier> modifiers = TerrainModifier.GetAllInstances();
            if (modifiers == null) { return 0; }

            // GetAllInstances returns the live list; destroying entries mutates it, so iterate a copy.
            TerrainModifier[] snapshot = modifiers.ToArray();
            for (int i = 0; i < snapshot.Length; i++) {
                TerrainModifier modifier = snapshot[i];
                if (modifier == null) { continue; }
                ZNetView nview = modifier.GetComponent<ZNetView>();
                if (nview == null || nview.IsValid() == false) { continue; }
                bool inRange = false;
                for (int p = 0; p < centers.Count; p++) {
                    if (radii[p] > 0f && Utils.DistanceXZ(modifier.transform.position, centers[p]) <= radii[p]) {
                        inRange = true;
                        break;
                    }
                }
                if (inRange == false) { continue; }

                if (nview.IsOwner() == false) { nview.ClaimOwnership(); }
                for (int h = 0; h < heightmaps.Count; h++) {
                    // Poke(2), not Poke(true): 1.0.7 replaced the bool with an int naming which pass
                    // regenerates - 1 is Unity's LateUpdate, 2 is CustomLateUpdate, which is where the
                    // old bool was consumed. 2 is therefore the faithful translation, and it is also what
                    // vanilla's own TerrainModifier.PokeHeightmaps passes for this same delayed case.
                    if (heightmaps[h].TerrainVSModifier(modifier)) { heightmaps[h].Poke(2); }
                }
                // Disable first, as vanilla's TerrainComp.UpgradeTerrain does. Destroy only lands at the
                // end of the frame, and until then the modifier is still in s_instances, so
                // Heightmap.ApplyModifiers would bake it straight back into this frame's rebuild.
                modifier.enabled = false;
                nview.Destroy();
                resets++;
            }
            return resets;
        }

        // The TerrainComp holds one entry per heightmap vertex. Clearing the modified flags inside
        // any point's radius returns those vertices to world-generation height and unpainted ground.
        private static int ResetHeightAndPaint(List<Vector3> centers, List<float> radii, List<Heightmap> heightmaps) {
            int resets = 0;
            int pointCount = centers.Count;
            int[] vertexXs = new int[pointCount];
            int[] vertexYs = new int[pointCount];
            int[] maskXs = new int[pointCount];
            int[] maskYs = new int[pointCount];
            float[] vertexRadiiSqr = new float[pointCount];
            List<int> clearedPaint = new List<int>();

            for (int i = 0; i < heightmaps.Count; i++) {
                Heightmap heightmap = heightmaps[i];
                TerrainComp comp = TerrainComp.FindTerrainCompiler(heightmap.transform.position);
                if (comp == null || comp.m_initialized == false) { continue; }
                if (comp.m_nview == null || comp.m_nview.IsValid() == false) { continue; }
                // TerrainComp.Save() silently no-ops for a non-owner.
                if (comp.m_nview.IsOwner() == false) { comp.m_nview.ClaimOwnership(); }

                // Vertices are m_scale metres apart, so convert the world radius into vertex units
                // rather than assuming the default 1m spacing.
                float scale = heightmap.m_scale > 0f ? heightmap.m_scale : 1f;
                for (int p = 0; p < pointCount; p++) {
                    heightmap.WorldToVertex(centers[p], out vertexXs[p], out vertexYs[p]);
                    heightmap.WorldToVertexMask(centers[p], out maskXs[p], out maskYs[p]);
                    float vertexRadius = radii[p] / scale;
                    // -1 marks a disabled point: a zero radius would otherwise still match its own vertex.
                    vertexRadiiSqr[p] = radii[p] > 0f ? vertexRadius * vertexRadius : -1f;
                }

                bool changed = false;
                clearedPaint.Clear();
                int stride = comp.m_width + 1;
                for (int y = 0; y < stride; y++) {
                    for (int x = 0; x < stride; x++) {
                        int idx = y * stride + x;

                        if (comp.m_modifiedHeight[idx] && WithinAnySqr(vertexXs, vertexYs, vertexRadiiSqr, x, y)) {
                            comp.m_modifiedHeight[idx] = false;
                            comp.m_levelDelta[idx] = 0f;
                            comp.m_smoothDelta[idx] = 0f;
                            changed = true;
                            resets++;
                        }

                        if (comp.m_modifiedPaint[idx] && WithinAnySqr(maskXs, maskYs, vertexRadiiSqr, x, y)) {
                            comp.m_modifiedPaint[idx] = false;
                            clearedPaint.Add(idx);
                            changed = true;
                            resets++;
                        }
                    }
                }

                if (changed) {
                    // Peers reload on the ZDO's data revision, not on this counter. The counter only
                    // tells their CheckLoad whether to refresh grass around m_lastOp* (exactly one new
                    // op) or across the whole heightmap. A batch has no single point, so it names the
                    // whole heightmap; otherwise peers would only regrow grass around the first crater.
                    comp.m_operations++;
                    if (pointCount == 1) {
                        comp.m_lastOpPoint = centers[0];
                        comp.m_lastOpRadius = radii[0];
                    } else {
                        comp.m_lastOpPoint = heightmap.transform.position;
                        comp.m_lastOpRadius = heightmap.m_width * scale / 2f;
                    }
                    comp.Save();

                    // Immediate rebuild, not Poke(2), because the paint we just cleared has to be
                    // re-seeded from the rebuilt mask. Since 1.0, TerrainComp.m_paintMask is not
                    // scratch for unmodified vertices: Initialize seeds it from the heightmap's mask,
                    // and PaintCleared reads it back (getMask while a Poke(1) is pending, and the
                    // Deep North neighbour median unconditionally). Leaving the old paint, or a blank
                    // colour with zero alpha, there would let the next brush stroke bake it back in.
                    // The rebuild also consumes any Poke(2) that ResetModifiers queued on this map.
                    heightmap.Poke();
                    for (int c = 0; c < clearedPaint.Count; c++) {
                        int idx = clearedPaint[c];
                        comp.m_paintMask[idx] = heightmap.GetPaintMask(idx % stride, idx / stride);
                    }
                }
            }

            return resets;
        }

        private static bool WithinAnySqr(int[] centerXs, int[] centerYs, float[] radiiSqr, int x, int y) {
            for (int p = 0; p < centerXs.Length; p++) {
                if (radiiSqr[p] < 0f) { continue; }
                float dx = centerXs[p] - x;
                float dy = centerYs[p] - y;
                if ((dx * dx) + (dy * dy) <= radiiSqr[p]) { return true; }
            }
            return false;
        }
    }
}
