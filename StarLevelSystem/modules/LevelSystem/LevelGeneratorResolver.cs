using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    // Resolves level generators (inline and/or referenced from CreatureLevelSettings.CustomLevelupGenerators)
    // into the levelup-chance tables consumed by the level/raid/nemesis systems. References are looked up
    // against the currently loaded settings so they always reflect the latest config.
    internal static class LevelGeneratorResolver {

        // Combines inline generators with those referenced by name from the CustomLevelupGenerators registry.
        internal static List<LevelGenerator> Resolve(List<LevelGenerator> inline, List<string> refs) {
            List<LevelGenerator> result = new List<LevelGenerator>();
            if (inline != null) { result.AddRange(inline); }
            if (refs != null && refs.Count > 0) {
                Dictionary<string, List<LevelGenerator>> registry = LevelSystemData.SLE_Level_Settings?.CustomLevelupGenerators;
                foreach (string name in refs) {
                    if (name == null) { continue; }
                    if (registry != null && registry.TryGetValue(name, out List<LevelGenerator> gens) && gens != null) {
                        result.AddRange(gens);
                    } else {
                        Logger.LogWarning($"Levelup generator reference '{name}' was not found in CreatureLevelSettings.CustomLevelupGenerators.");
                    }
                }
            }
            return result;
        }

        // True when either an inline list or a list of references is configured.
        internal static bool HasGenerators(List<LevelGenerator> inline, List<string> refs) {
            return (inline != null && inline.Count > 0) || (refs != null && refs.Count > 0);
        }

        // Builds a merged levelup-chance table from the configured generators, or null when none are configured.
        internal static SortedDictionary<int, float> BuildLevelupChance(List<LevelGenerator> inline, List<string> refs) {
            if (!HasGenerators(inline, refs)) { return null; }
            List<LevelGenerator> gens = Resolve(inline, refs);
            if (gens.Count == 0) { return null; }
            SortedDictionary<int, float> chances = new SortedDictionary<int, float>();
            foreach (LevelGenerator gen in gens) {
                chances.MergeSortedDictionary(gen.GetLevelUpDefinition());
            }
            return chances;
        }

        // Rolls a concrete level from the configured generators, or 0 when none are configured.
        internal static int RollLevel(List<LevelGenerator> inline, List<string> refs) {
            if (!HasGenerators(inline, refs)) { return 0; }
            List<LevelGenerator> gens = Resolve(inline, refs);
            if (gens.Count == 0) { return 0; }
            SortedDictionary<int, float> chances = new SortedDictionary<int, float>();
            int maxLevel = 1;
            foreach (LevelGenerator gen in gens) {
                chances.MergeSortedDictionary(gen.GetLevelUpDefinition());
                if (gen.MaxLevel > maxLevel) { maxLevel = gen.MaxLevel; }
            }
            float roll = UnityEngine.Random.Range(0f, 100f);
            return LevelSelection.DetermineLevelRollResult(roll, maxLevel, chances, new SortedDictionary<int, float>(), 1f);
        }
    }
}
