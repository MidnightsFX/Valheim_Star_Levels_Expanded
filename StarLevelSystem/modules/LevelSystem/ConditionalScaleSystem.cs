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
        // How far each biome's difficulty increases have taken it, from above 0 to 1. Biomes with none yet are left out.
        private static readonly Dictionary<Heightmap.Biome, float> progressByBiome = new Dictionary<Heightmap.Biome, float>();
        // The current global key used to select Level Generators and levelup chances
        private static string CurrentGlobalKey = null;
        private static bool cacheValid = false;

        private static bool TiersEnabled() {
            var settings = LevelSystemData.SLE_Level_Settings;
            if (settings == null || settings.EnableConditionalCreatureLevelupChance == false) { return false; }
            if (cacheValid == false) { RebuildCache(); }
            return true;
        }

        // The curve fixed levels written for this boss and biome replace the biome's with. Null without any, which is what ships.
        internal static SortedDictionary<int, float> GetConditionalLevelupChance(Heightmap.Biome biome) {
            if (TiersEnabled() == false) { return null; }
            if (CurrentGlobalKeyConditionalLevelup.TryGetValue(EntryBiome(biome), out SortedDictionary<int, float> gen)) { return gen; }
            return null;
        }

        private static bool TryGetProgress(Heightmap.Biome biome, out float progress) {
            progress = 0f;
            if (TiersEnabled() == false) { return false; }
            // Fixed levels take the place of the biome's increases.
            if (GetConditionalLevelupChance(biome) != null) { return false; }
            return BiomeRegistry.TryGetKey(progressByBiome, biome, out Heightmap.Biome key) && progressByBiome.TryGetValue(key, out progress);
        }

        // What the active tier multiplies level-up chances by in this biome. Whose curve it multiplies follows
        // DetermineLevelupChance: a creature entry's curve or a boss curve outranks the tier, so those stay as written.
        internal static float GetChanceMultiplier(Heightmap.Biome biome, CreatureSpecificSetting creature_settings, bool isBoss) {
            if (creature_settings != null && creature_settings.CustomCreatureLevelUpChance != null) { return 1f; }
            SortedDictionary<int, float> bossChances = LevelSystemData.SLE_Level_Settings?.BossCreatureLevelUpChance;
            if (isBoss && bossChances != null && bossChances.Count > 0) { return 1f; }
            if (TryGetProgress(biome, out float progress) == false) { return 1f; }
            float full = UnityEngine.Mathf.Max(1f, LevelSystemData.SLE_Level_Settings.ConditionalFullChanceMultiplier);
            return 1f + progress * (full - 1f);
        }

        // The level cap (stars + 1) a creature in this biome has after the bosses beaten so far, given the cap it would have
        // without them. Difficulty increases move the cap toward MaxLevel and stop there. A biome already at or above MaxLevel
        // keeps its cap, so beating a boss never lowers a biome's levels. Generators are fixed levels someone wrote, so their
        // top can pass it.
        internal static int RaiseCap(Heightmap.Biome biome, CreatureSpecificSetting creature_settings, int cap) {
            if (creature_settings != null && creature_settings.CustomCreatureLevelUpChance != null) { return cap; }
            if (TryGetProgress(biome, out float progress)) {
                int top = ValConfig.MaxLevel.Value + 1;
                return cap >= top ? cap : UnityEngine.Mathf.RoundToInt(cap + (top - cap) * progress);
            }
            return TryGetConditionalLevelRange(biome, creature_settings, out _, out int tierMax) ? Math.Max(cap, tierMax) : cap;
        }

        // NightMultiplier of the generators behind GetConditionalLevelupChance for this biome, 1 when none apply.
        internal static float GetConditionalNightMultiplier(Heightmap.Biome biome) {
            if (GetConditionalLevelupChance(biome) == null) { return 1f; }
            return resolvedByBiome.TryGetValue(EntryBiome(biome), out List<LevelGenerator> generators) ? LevelGeneratorResolver.NightMultiplierOf(generators) : 1f;
        }

        // The level range (stars + 1) an entry's generators give a creature in this biome: they replace the biome's
        // minimum as well as its curve, while their top only raises the cap (see RaiseCap). Without any the biome's minimum
        // stands.
        // Only where the tier is the curve the creature rolls from - a creature entry with its own curve outranks it.
        // Bosses are the caller's concern: MaxBossLevel caps them wherever they stand.
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
            return BiomeRegistry.TryGetKey(CurrentGlobalKeyConditionalLevelup, biome, out Heightmap.Biome key) ? key : Heightmap.Biome.All;
        }

        private static void ClearActiveTier() {
            CurrentGlobalKey = null;
            CurrentGlobalKeyConditionalLevelup.Clear();
            resolvedByBiome.Clear();
            progressByBiome.Clear();
        }

        private static void RebuildCache() {
            ClearActiveTier();
            cacheValid = true;
            CreatureLevelSettings settings = LevelSystemData.SLE_Level_Settings;
            if (settings == null || ZoneSystem.instance == null) { return; }

            List<string> order = BossKeyOrder(settings);
            WorkOutDifficultyIncreases(settings, order);

            Dictionary<string, Dictionary<Heightmap.Biome, ConditionalLevelupChance>> conditional = settings.ConditionalCreatureLevelupChance;
            if (conditional == null) { return; }
            // Rebuild the cache of which global key is currently targeted for generators
            CurrentGlobalKey = SelectGlobalKey(conditional, order);

            // Rebuild the list of generators; nothing resolved means cache the empty result so we don't rebuild every call
            if (CurrentGlobalKey == null || !conditional.TryGetValue(CurrentGlobalKey, out Dictionary<Heightmap.Biome, ConditionalLevelupChance> biomeMap) || biomeMap == null) {
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
        }

        // A biome earns one increase per boss from its own on, less the delay, up to the most it may have. All are the same size,
        // and one per boss after the first adds up to full strength, so the oldest biome gets there with the last boss when
        // nothing is holding it back. A lower limit stops a biome partway rather than making each increase larger.
        private static void WorkOutDifficultyIncreases(CreatureLevelSettings settings, List<string> order) {
            int latest = order.FindLastIndex(key => key != null && ZoneSystem.instance.GetGlobalKey(key));
            if (latest < 0) { return; }
            int delay = Math.Max(0, settings.ConditionalBiomeDelay);
            int most = Math.Max(1, settings.ConditionalDifficultyIncreases);
            float eachIncrease = 1f / Math.Max(1, order.Count - 1);
            foreach (KeyValuePair<Heightmap.Biome, string> biome in LevelSystemData.OwnBossOfBiome) {
                int own = order.FindIndex(key => string.Equals(key, biome.Value, StringComparison.OrdinalIgnoreCase));
                if (own < 0) { continue; }
                int earned = latest - own + 1 - delay;
                if (earned > 0) { progressByBiome[biome.Key] = UnityEngine.Mathf.Clamp01(Math.Min(earned, most) * eachIncrease); }
            }
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
            ClearActiveTier();
        }
    }
}
