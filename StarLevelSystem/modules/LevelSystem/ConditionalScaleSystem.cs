using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
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
            foreach (KeyValuePair<string, Dictionary<Heightmap.Biome, ConditionalLevelupChance>> entry in conditional) {
                if (entry.Key != null && ZoneSystem.instance.GetGlobalKey(entry.Key)) {
                    CurrentGlobalKey = entry.Key;
                    break;
                }
            }

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

        internal static void ResetCache() {
            cacheValid = false;
            CurrentGlobalKey = null;
            resolvedByBiome.Clear();
        }
    }
}
