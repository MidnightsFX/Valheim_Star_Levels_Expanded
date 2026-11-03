using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace StarLevelSystem.modules {

    // Unified biome lookup to allow support for external mod defined biomes
    internal static class BiomeLookup {

        // How long heightmaps are left alone after one throws. A heightmap that is broken stays broken, and every creature
        // around it would otherwise pay for an exception, several times each.
        private const float RetryAfterFailureSeconds = 5f;

        private static float retryHeightmapsAt = float.MinValue;
        private static bool failureReported;

        // What the game reports at this point: the heightmap's biome, which includes a local biome from a mod such as
        // Biome Generator. None when no heightmap covers the point, which config relies on (DeniedBiomes defaults to None).
        internal static Heightmap.Biome AtPosition(Vector3 position) {
            if (Time.unscaledTime < retryHeightmapsAt) { return World(position); }
            try {
                return Heightmap.FindBiome(position);
            } catch (Exception e) {
                OnHeightmapFailure(e);
                return World(position);
            }
        }

        // The biome world generation put here. This overload rather than GetBiome(Vector3): Biome Generator's dome overrides
        // that one, so it can answer with the dome's biome. Also the only answer for an area with no loaded heightmap.
        internal static Heightmap.Biome World(Vector3 position) {
            WorldGenerator generator = WorldGenerator.instance;
            return generator != null ? generator.GetBiome(position.x, position.z) : Heightmap.Biome.None;
        }

        // ZoneSystem.GetGroundData, which reads the biome from the heightmap under the point too. A failure reports no biome
        // and no heightmap, which callers already treat as a spot they cannot use.
        internal static void GroundData(ref Vector3 position, out Vector3 normal, out Heightmap.Biome biome, out Heightmap.BiomeArea area, out Heightmap hmap) {
            if (Time.unscaledTime >= retryHeightmapsAt) {
                try {
                    ZoneSystem.instance.GetGroundData(ref position, out normal, out biome, out area, out hmap);
                    return;
                } catch (Exception e) {
                    OnHeightmapFailure(e);
                }
            }
            normal = Vector3.up;
            biome = Heightmap.Biome.None;
            area = Heightmap.BiomeArea.Everything;
            hmap = null;
        }

        private static void OnHeightmapFailure(Exception e) {
            retryHeightmapsAt = Time.unscaledTime + RetryAfterFailureSeconds;
            if (failureReported) {
                Logger.LogDebug($"Heightmap.GetBiome threw again: {e.GetType().Name}");
                return;
            }
            failureReported = true;
            Logger.LogWarning($"Heightmap.GetBiome threw, so Star Level System is using the world's biome where it could not read one. " +
                $"Mods patching that method, which may or may not be involved: {DescribePatchOwners()}. {e}");
        }

        private static string DescribePatchOwners() {
            try {
                IEnumerable<string> owners = new MethodBase[] {
                    AccessTools.Method(typeof(Heightmap), nameof(Heightmap.GetBiome), new[] { typeof(Vector3), typeof(float), typeof(bool) }),
                    AccessTools.Method(typeof(Heightmap), nameof(Heightmap.FindBiome), new[] { typeof(Vector3) }),
                }.Where(method => method != null)
                    .Select(Harmony.GetPatchInfo)
                    .Where(patches => patches != null)
                    .SelectMany(patches => patches.Owners)
                    .Distinct();
                string described = string.Join(", ", owners);
                return described.Length > 0 ? described : "none";
            } catch (Exception e) {
                return $"could not be read ({e.Message})";
            }
        }
    }
}
