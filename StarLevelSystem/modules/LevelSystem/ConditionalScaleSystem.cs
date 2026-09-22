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

            if (CurrentGlobalKeyConditionalLevelup.TryGetValue(EntryBiome(biome), out SortedDictionary<int, float> gen)) { return gen; }
            return null;
        }

        // NightMultiplier of the generators behind GetConditionalLevelupChance for this biome, 1 when none apply.
        internal static float GetConditionalNightMultiplier(Heightmap.Biome biome) {
            if (GetConditionalLevelupChance(biome) == null) { return 1f; }
            return resolvedByBiome.TryGetValue(EntryBiome(biome), out List<LevelGenerator> generators) ? LevelGeneratorResolver.NightMultiplierOf(generators) : 1f;
        }

        // The level range (stars + 1) the active tier gives a creature in this biome: the tier replaces the biome's
        // Min/Max as well as its curve. Only where the tier is the curve the creature rolls from - a creature entry with
        // its own curve outranks it. Bosses are the caller's concern: MaxBossLevel caps them wherever they stand.
        internal static bool TryGetConditionalLevelRange(Heightmap.Biome biome, CreatureSpecificSetting creature_settings, out int minLevel, out int maxLevel) {
            minLevel = 0;
            maxLevel = 0;
            if (creature_settings != null && creature_settings.CustomCreatureLevelUpChance != null) { return false; }
            SortedDictionary<int, float> table = GetConditionalLevelupChance(biome);
            if (table == null || table.Count == 0) { return false; }
            minLevel = Math.Max(1, table.Keys.First());
            maxLevel = Math.Max(minLevel, table.Keys.Last());
            return true;
        }

        // The biome's own entry within the active tier, else that tier's 'All' entry as its fallback.
        private static Heightmap.Biome EntryBiome(Heightmap.Biome biome) {
            return CurrentGlobalKeyConditionalLevelup.ContainsKey(biome) ? biome : Heightmap.Biome.All;
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
