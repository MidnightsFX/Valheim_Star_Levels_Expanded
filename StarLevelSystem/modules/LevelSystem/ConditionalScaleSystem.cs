using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static Heightmap;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class ConditionalScaleSystem {

        internal static Dictionary<Heightmap.Biome, SortedDictionary<int, float>>  CurrentGlobalKeyConditionalLevelup = new Dictionary<Heightmap.Biome, SortedDictionary<int, float>>();
        private static readonly Dictionary<Heightmap.Biome, List<LevelGenerator>> resolvedByBiome = new Dictionary<Heightmap.Biome, List<LevelGenerator>>();
        // The current global key used to select Level Generators and levelup chances
        private static string CurrentGlobalKey = null;
        private static bool cacheValid = false;

        internal static SortedDictionary<int, float> GetConditionalLevelupChance(Heightmap.Biome biome) {
            var settings = LevelSystemData.SLE_Level_Settings;
            if (settings == null || settings.EnableConditionalCreatureLevelupChance == false || settings.ConditionalCreatureLevelupChance == null) {
                return null;
            }
            if (cacheValid == false) { RebuildCache(); }

            if (CurrentGlobalKeyConditionalLevelup.TryGetValue(biome, out SortedDictionary<int, float> gen)) { return gen; }
            return null;
        }

        private static void RebuildCache() {
            CurrentGlobalKey = null;
            CurrentGlobalKeyConditionalLevelup.Clear();
            resolvedByBiome.Clear();

            Dictionary<string, Dictionary<Heightmap.Biome, ConditionalLevelupChance>> conditional = LevelSystemData.SLE_Level_Settings?.ConditionalCreatureLevelupChance;
            if (conditional == null || ZoneSystem.instance == null) { cacheValid = true; return; }

            // Rebuild the cache of which global key is currently targeted for generators
            CurrentGlobalKey = SelectGlobalKey(conditional, BossKeyOrder(LevelSystemData.SLE_Level_Settings));

            // Rebuild the list of generators; nothing resolved means cache the empty result so we don't rebuild every call
            if (CurrentGlobalKey == null || !conditional.TryGetValue(CurrentGlobalKey, out Dictionary<Heightmap.Biome, ConditionalLevelupChance> biomeMap) || biomeMap == null) {
                cacheValid = true;
                return;
            }
            foreach (KeyValuePair<Heightmap.Biome, ConditionalLevelupChance> kvp in biomeMap) {
                if (kvp.Value == null) { continue; }
                List<LevelGenerator> generators = LevelGeneratorResolver.Resolve(kvp.Value.LevelupGenerators, kvp.Value.LevelupGeneratorRefs);
                if (generators.Count == 0) { continue; }
                resolvedByBiome[kvp.Key] = generators;
                SortedDictionary<int, float> levelupChance = new SortedDictionary<int, float>();
                foreach (var levelgen in generators) {
                    levelupChance.MergeSortedDictionary(levelgen.GetLevelUpDefinition());
                }
                CurrentGlobalKeyConditionalLevelup[kvp.Key] = levelupChance;
            }
            Logger.LogDebug($"BossScaleSystem: resolved conditional levelup generators for '{CurrentGlobalKey}' across {resolvedByBiome.Count} biome(s).");
            cacheValid = true;
        }

        // The boss keys conditional entries are ranked by, earliest progression first. Files written before
        // ConditionalBossKeyOrder existed have no list and get the vanilla order.
        internal static List<string> BossKeyOrder(CreatureLevelSettings settings) {
            List<string> order = settings?.ConditionalBossKeyOrder;
            return order != null && order.Count > 0 ? order : LevelSystemData.VanillaBossKeyOrder;
        }

        // Picks the conditional entry for the furthest boss progression. The order list decides, never the file
        // order: the latest defeated listed key wins, and keys that are not listed rank below every listed key
        // (later in the file above earlier), so custom or modded keys still apply on their own.
        private static string SelectGlobalKey(Dictionary<string, Dictionary<Heightmap.Biome, ConditionalLevelupChance>> conditional, List<string> order) {
            // Global keys are stored lowercased, so config keys are matched without case, as GetGlobalKey does.
            Dictionary<string, string> configKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in conditional.Keys) {
                if (key != null) { configKeys[key] = key; }
            }

            for (int i = order.Count - 1; i >= 0; i--) {
                string listed = order[i];
                if (listed != null && configKeys.TryGetValue(listed, out string configKey) && ZoneSystem.instance.GetGlobalKey(listed)) {
                    return configKey;
                }
            }

            HashSet<string> listedKeys = new HashSet<string>(order.Where(k => k != null), StringComparer.OrdinalIgnoreCase);
            List<string> unlisted = conditional.Keys.Where(k => k != null && listedKeys.Contains(k) == false).ToList();
            for (int i = unlisted.Count - 1; i >= 0; i--) {
                if (ZoneSystem.instance.GetGlobalKey(unlisted[i])) { return unlisted[i]; }
            }
            return null;
        }

        internal static void ResetCache() {
            cacheValid = false;
            CurrentGlobalKey = null;
            CurrentGlobalKeyConditionalLevelup.Clear();
            resolvedByBiome.Clear();
        }
    }
}
