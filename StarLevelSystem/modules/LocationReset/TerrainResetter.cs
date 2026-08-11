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

            int resets = 0;
            List<Heightmap> heightmaps = new List<Heightmap>();
            // Heightmaps overlap their neighbours, so search wider than the radius to catch a
            // compiler whose centre is outside it but whose vertices are not.
            Heightmap.FindHeightmap(center, radius + 100f, heightmaps);

            resets += ResetModifiers(center, radius, heightmaps);
            resets += ResetHeightAndPaint(center, radius, heightmaps);

            if (ClutterSystem.instance != null) {
                ClutterSystem.instance.ResetGrass(center, radius);
            }

            return resets;
        }

        // TerrainModifier components are the discrete raise/level operations players leave behind.
        // Reverting one means subtracting it from every heightmap it touched, then destroying it.
        private static int ResetModifiers(Vector3 center, float radius, List<Heightmap> heightmaps) {
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
                if (Utils.DistanceXZ(modifier.transform.position, center) > radius) { continue; }

                if (nview.IsOwner() == false) { nview.ClaimOwnership(); }
                for (int h = 0; h < heightmaps.Count; h++) {
                    if (heightmaps[h].TerrainVSModifier(modifier)) { heightmaps[h].Poke(true); }
                }
                nview.Destroy();
                resets++;
            }
            return resets;
        }

        // The TerrainComp holds one entry per heightmap vertex. Clearing the modified flags inside
        // the radius returns those vertices to world-generation height and unpainted ground.
        private static int ResetHeightAndPaint(Vector3 center, float radius, List<Heightmap> heightmaps) {
            int resets = 0;

            for (int i = 0; i < heightmaps.Count; i++) {
                Heightmap heightmap = heightmaps[i];
                TerrainComp comp = TerrainComp.FindTerrainCompiler(heightmap.transform.position);
                if (comp == null || comp.m_initialized == false) { continue; }
                if (comp.m_nview == null || comp.m_nview.IsValid() == false) { continue; }
                // TerrainComp.Save() silently no-ops for a non-owner.
                if (comp.m_nview.IsOwner() == false) { comp.m_nview.ClaimOwnership(); }

                heightmap.WorldToVertex(center, out int vertexX, out int vertexY);
                heightmap.WorldToVertexMask(center, out int maskX, out int maskY);

                // Vertices are m_scale metres apart, so convert the world radius into vertex units
                // rather than assuming the default 1m spacing.
                float scale = heightmap.m_scale > 0f ? heightmap.m_scale : 1f;
                float vertexRadius = radius / scale;
                float vertexRadiusSqr = vertexRadius * vertexRadius;

                bool changed = false;
                int stride = comp.m_width + 1;
                for (int y = 0; y < stride; y++) {
                    for (int x = 0; x < stride; x++) {
                        int idx = y * stride + x;

                        if (comp.m_modifiedHeight[idx] && WithinSqr(vertexX, vertexY, x, y, vertexRadiusSqr)) {
                            comp.m_modifiedHeight[idx] = false;
                            comp.m_levelDelta[idx] = 0f;
                            comp.m_smoothDelta[idx] = 0f;
                            changed = true;
                            resets++;
                        }

                        if (comp.m_modifiedPaint[idx] && WithinSqr(maskX, maskY, x, y, vertexRadiusSqr)) {
                            comp.m_modifiedPaint[idx] = false;
                            comp.m_paintMask[idx] = Color.clear;
                            changed = true;
                            resets++;
                        }
                    }
                }

                if (changed) {
                    // Bumping the operation counter is what makes peers treat the new TCData as a
                    // fresh edit rather than a stale copy of what they already have.
                    comp.m_operations++;
                    comp.m_lastOpPoint = center;
                    comp.m_lastOpRadius = radius;
                    comp.Save();
                    heightmap.Poke(true);
                }
            }

            return resets;
        }

        private static bool WithinSqr(int cx, int cy, int x, int y, float radiusSqr) {
            float dx = cx - x;
            float dy = cy - y;
            return (dx * dx) + (dy * dy) <= radiusSqr;
        }
    }
}
