using BepInEx.Configuration;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {

    // A tree's level and size, worked out from where it stands and the world seed. Every peer gets the same answer, so
    // nothing is saved on standing trees.
    internal static class TreeLevels {
        internal enum Pattern { Scattered, Groves }

        internal struct TreeSize {
            public int Level;
            public float Scale;
        }

        internal static readonly TreeSize Unscaled = new TreeSize { Level = 1, Scale = 1f };

        internal static readonly AcceptableValueList<string> AllowedPatterns = new AcceptableValueList<string>(Pattern.Scattered.ToString(), Pattern.Groves.ToString());

        private const uint TreeSalt = 0x5EED7EE5u;
        private const uint GroveSalt = 0x6A09E667u;
        // Blending four lattice values bunches the result toward the middle; this spreads it back over the range.
        private const double GroveStretch = 2.0;
        // How much a tree in a grove can differ from its neighbours.
        private const double GroveJitter = 0.25;

        private static Pattern pattern = Pattern.Scattered;
        private static Dictionary<Heightmap.Biome, int> biomeMaxLevels = new Dictionary<Heightmap.Biome, int>();
        private static Dictionary<Heightmap.Biome, float> biomeSizeScales = new Dictionary<Heightmap.Biome, float>();
        private static readonly Dictionary<string, Vector3> prefabScales = new Dictionary<string, Vector3>();

        // Set while this peer instantiates a log from a tree or a log, so the new log's Awake takes that level.
        internal static int PendingLogLevel;

        internal static void ReadSettings() {
            pattern = Enum.TryParse(ValConfig.TreeSizePattern.Value, out Pattern parsed) ? parsed : Pattern.Scattered;
            biomeMaxLevels = ParseBiomeList(ValConfig.TreeBiomeMaxLevels, (string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 1);
            biomeSizeScales = ParseBiomeList(ValConfig.TreeBiomeSizeScale, (string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0f);
        }

        private delegate bool ValueParser<T>(string text, out T value);

        private static Dictionary<Heightmap.Biome, T> ParseBiomeList<T>(ConfigEntry<string> entry, ValueParser<T> parseValue) {
            Dictionary<Heightmap.Biome, T> table = new Dictionary<Heightmap.Biome, T>();
            if (string.IsNullOrWhiteSpace(entry.Value)) { return table; }
            foreach (string part in entry.Value.Split(',')) {
                if (string.IsNullOrWhiteSpace(part)) { continue; }
                string[] pair = part.Split(':');
                Heightmap.Biome biome = pair.Length == 2 && pair[0].Trim().Length > 0 ? BiomeRegistry.FromConfigName(pair[0].Trim()) : Heightmap.Biome.None;
                // BiomeRegistry lookups never consider All, so an entry for it would silently do nothing.
                if (biome == Heightmap.Biome.None || biome == Heightmap.Biome.All || parseValue(pair[1].Trim(), out T value) == false) {
                    Logger.LogWarning($"{entry.Definition.Key}: skipping '{part.Trim()}', which is not a single biome and a value such as Mistlands:5.");
                    continue;
                }
                table[biome] = value;
            }
            return table;
        }

        internal static TreeSize Compute(Vector3 position) {
            if (ValConfig.EnableTreeScaling.Value == false) { return Unscaled; }
            Heightmap.Biome biome = BiomeLookup.World(position);
            float reach = ReachAt(position, biome);
            WorldGenerator generator = WorldGenerator.instance;
            double roll = generator != null ? Roll(position, generator.GetSeed()) : 0.0;
            int level = Math.Max(1, Mathf.RoundToInt((float)(reach * (1.0 - ValConfig.TreeSizeRandomness.Value * roll))));
            float sizeScale = BiomeRegistry.TryGetEntry(biomeSizeScales, biome, out float biomeScale) ? biomeScale : 1f;
            return new TreeSize { Level = level, Scale = 1f + ValConfig.TreeSizeScalePerLevel.Value * sizeScale * level };
        }

        // The level with no roll: the largest a tree at this spot can be.
        internal static int Reach(Vector3 position) {
            if (ValConfig.EnableTreeScaling.Value == false) { return 1; }
            return Math.Max(1, Mathf.RoundToInt(ReachAt(position, BiomeLookup.World(position))));
        }

        // From the world's origin rather than DistanceScaleSystem.center: a client learns the starter temple's position
        // well after trees start loading, so a tree loaded before then would be measured from a different spot than the
        // server measured it from. The temple is near the origin.
        private static float ReachAt(Vector3 position, Heightmap.Biome biome) {
            int maxLevel = ValConfig.TreeMaxLevel.Value;
            int cap = BiomeRegistry.TryGetEntry(biomeMaxLevels, biome, out int biomeCap) ? Math.Min(maxLevel, biomeCap) : maxLevel;
            float distance = new Vector2(position.x, position.z).magnitude;
            return Mathf.Min(maxLevel * distance / WorldSize.Radius, cap);
        }

        // 0 is a tree at the largest size for its spot, towards 1 the smallest.
        private static double Roll(Vector3 position, int seed) {
            double own = Hash01(seed, Mathf.FloorToInt(position.x * 4f), Mathf.FloorToInt(position.z * 4f), TreeSalt);
            if (pattern == Pattern.Scattered) { return own; }
            return Clamp01(GroveNoise(position.x, position.z, seed) + (own - 0.5) * GroveJitter);
        }

        // Value noise: a hashed value at each corner of a TreeGroveSize lattice, blended with smoothstep.
        private static double GroveNoise(float x, float z, int seed) {
            double cell = Math.Max(1.0, ValConfig.TreeGroveSize.Value);
            double gx = x / cell;
            double gz = z / cell;
            double floorX = Math.Floor(gx);
            double floorZ = Math.Floor(gz);
            int ix = (int)floorX;
            int iz = (int)floorZ;
            double u = Smooth(gx - floorX);
            double v = Smooth(gz - floorZ);
            double near = Lerp(Hash01(seed, ix, iz, GroveSalt), Hash01(seed, ix + 1, iz, GroveSalt), u);
            double far = Lerp(Hash01(seed, ix, iz + 1, GroveSalt), Hash01(seed, ix + 1, iz + 1, GroveSalt), u);
            return Clamp01((Lerp(near, far, v) - 0.5) * GroveStretch + 0.5);
        }

        // Integer mixing only, so a Windows client and a Linux server get the same bits; string hashes and
        // UnityEngine.Random are not guaranteed to.
        private static double Hash01(int seed, int x, int z, uint salt) {
            unchecked {
                uint h = (uint)seed ^ salt;
                h = Mix(h ^ ((uint)x * 0x9E3779B1u));
                h = Mix(h ^ ((uint)z * 0x85EBCA77u));
                return (h >> 8) / 16777216.0;
            }
        }

        private static uint Mix(uint h) {
            unchecked {
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double Clamp01(double value) => value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);

        // A log carries its tree's level on its ZDO: where it comes to rest says nothing about the tree it fell from. A log
        // saved before that was stored gets the reach where it lies, which barely moves as it rolls.
        internal static int LogLevel(ZNetView nview) {
            if (ValConfig.EnableTreeScaling.Value == false || nview == null) { return 1; }
            ZDO zdo = nview.GetZDO();
            int stored = zdo != null ? zdo.GetInt(SLS_TREE, 0) : 0;
            return stored > 0 ? stored : Reach(nview.transform.position);
        }

        // Waits because a grown sapling gets its size just after the tree wakes, which would undo the scaling.
        internal static IEnumerator SizeAfterWake(TreeBase tree) {
            yield return new WaitForSeconds(1f);
            if (tree == null) { yield break; }
            ApplyScale(tree);
            TaskRunner.RequestPhysicsSync();
        }

        // Absolute rather than multiplied, so a resize pass and the delayed first sizing can land in either order. Never
        // through ZNetView.SetLocalScale: on the owner that saves the grown size, which would grow again on every load.
        internal static void ApplyScale(TreeBase tree) {
            if (TryBaseScale(tree, out Vector3 baseScale) == false) { return; }
            float scale = ValConfig.EnableTreeScaling.Value ? CompositeLazyCache.GetCachedTreeScale(tree.m_nview) : 1f;
            tree.transform.localScale = baseScale * scale;
        }

        // The size ZNetView.Awake gave the tree: its saved scale, else its saved uniform scale, else its prefab's.
        private static bool TryBaseScale(TreeBase tree, out Vector3 baseScale) {
            ZDO zdo = tree.m_nview != null ? tree.m_nview.GetZDO() : null;
            if (zdo != null) {
                baseScale = zdo.GetVec3(ZDOVars.s_scaleHash, Vector3.zero);
                if (baseScale != Vector3.zero) { return true; }
                float scalar = zdo.GetFloat(ZDOVars.s_scaleScalarHash, 0f);
                if (scalar != 0f) {
                    baseScale = new Vector3(scalar, scalar, scalar);
                    return true;
                }
            }
            string name = Utils.GetPrefabName(tree.gameObject);
            if (prefabScales.TryGetValue(name, out baseScale)) { return true; }
            GameObject prefab = PrefabLookup.FindOrWarn(name, "resizing a tree");
            if (prefab == null) { return false; }
            baseScale = prefab.transform.localScale;
            prefabScales[name] = baseScale;
            return true;
        }
    }
}
