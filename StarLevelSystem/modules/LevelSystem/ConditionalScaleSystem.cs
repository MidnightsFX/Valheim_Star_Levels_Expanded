using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class ConditionalScaleSystem {

        // The ExtraStars value (any negative one) that moves a biome's cap an equal step toward MaxLevel per boss.
        internal const int EvenStepsToMaxLevel = -1;

        internal static Dictionary<Heightmap.Biome, SortedDictionary<int, float>>  CurrentGlobalKeyConditionalLevelup = new Dictionary<Heightmap.Biome, SortedDictionary<int, float>>();
        private static readonly Dictionary<Heightmap.Biome, List<LevelGenerator>> resolvedByBiome = new Dictionary<Heightmap.Biome, List<LevelGenerator>>();
        // Biomes with no increases yet are left out.
        private static readonly Dictionary<Heightmap.Biome, int> increasesByBiome = new Dictionary<Heightmap.Biome, int>();
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

        // The difficulty increases boss kills have given this biome so far. Fixed levels take their place.
        private static int IncreasesIn(Heightmap.Biome biome) {
            if (TiersEnabled() == false) { return 0; }
            if (GetConditionalLevelupChance(biome) != null) { return 0; }
            return BiomeRegistry.TryGetKey(increasesByBiome, biome, out Heightmap.Biome key) && increasesByBiome.TryGetValue(key, out int increases) ? increases : 0;
        }

        // The increases a creature's roll gets. Follows DetermineLevelupChance: a creature entry's curve or a boss curve outranks
        // the biome's, so those stay as written.
        internal static int GetIncreases(Heightmap.Biome biome, CreatureSpecificSetting creature_settings, bool isBoss) {
            if (creature_settings != null && creature_settings.CustomCreatureLevelUpChance != null) { return 0; }
            SortedDictionary<int, float> bossChances = LevelSystemData.SLE_Level_Settings?.BossCreatureLevelUpChance;
            if (isBoss && bossChances != null && bossChances.Count > 0) { return 0; }
            return IncreasesIn(biome);
        }

        internal static ConditionalIncreaseBonus Bonus => LevelSystemData.SLE_Level_Settings?.ConditionalIncreaseBonus;

        // The level cap (stars + 1) a creature in this biome has after the bosses beaten so far, given the cap it would have
        // without them. Fixed levels are levels someone wrote, so their top can pass MaxLevel.
        internal static int RaiseCap(Heightmap.Biome biome, CreatureSpecificSetting creature_settings, int cap) {
            if (creature_settings != null && creature_settings.CustomCreatureLevelUpChance != null) { return cap; }
            int increases = IncreasesIn(biome);
            if (increases > 0) {
                return RaisedCap(cap, ValConfig.MaxLevel.Value + 1, increases, Bonus, BossKeyOrder(LevelSystemData.SLE_Level_Settings).Count);
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

        // --- the math, shared by the roll, the panel's preview and sls-boss-scaling ---------------------------------------

        // One increase per boss from the biome's own on, less the delay, up to the most it may have. latest is the index in
        // order of the latest defeated boss, -1 with none.
        internal static int IncreasesEarned(List<string> order, string ownBossKey, int latest, int delay, int most) {
            if (latest < 0 || order == null) { return 0; }
            int own = order.FindIndex(key => string.Equals(key, ownBossKey, StringComparison.OrdinalIgnoreCase));
            if (own < 0) { return 0; }
            return Mathf.Clamp(latest - own + 1 - Math.Max(0, delay), 0, Math.Max(1, most));
        }

        // The cap (stars + 1) after this many increases, never past top (MaxLevel + 1). A cap already at or above top stays,
        // so beating a boss never lowers a biome's levels. Even steps are one per boss after the first, so the oldest biome
        // reaches top with the last boss.
        internal static int RaisedCap(int cap, int top, int increases, ConditionalIncreaseBonus bonus, int bossCount) {
            if (increases <= 0 || bonus == null || cap >= top) { return cap; }
            if (bonus.ExtraStars < 0) {
                float progress = Mathf.Clamp01(increases / (float)Math.Max(1, bossCount - 1));
                return Mathf.RoundToInt(cap + (top - cap) * progress);
            }
            return Math.Min(top, cap + increases * bonus.ExtraStars);
        }

        internal static float ChanceMultiplier(ConditionalIncreaseBonus bonus, int increases) {
            if (increases <= 0 || bonus == null) { return 1f; }
            return Mathf.Max(0f, 1f + increases * bonus.ChanceIncrease);
        }

        // The curve with each increase's LevelBonus added. A curve that stops below the cap with chance left to roll past its
        // last level gets a level after it, or the roll would stop on that last level and a raised cap could never be reached.
        internal static SortedDictionary<int, float> ApplyIncreases(SortedDictionary<int, float> curve, ConditionalIncreaseBonus bonus, int increases, int cap) {
            if (increases <= 0 || bonus == null || curve == null) { return curve; }
            SortedDictionary<int, float> raised = LevelSelection.ApplyLevelupBonus(curve, bonus.LevelBonus, increases);
            if (raised.Count > 0) {
                int last = raised.Keys.Last();
                if (last < cap && raised[last] > 0f) { raised[last + 1] = 0f; }
            }
            return raised;
        }

        // Index in order of the latest boss this world has defeated, -1 with none or outside a world.
        internal static int LatestDefeated(List<string> order) {
            if (ZoneSystem.instance == null || order == null) { return -1; }
            return order.FindLastIndex(key => key != null && ZoneSystem.instance.GetGlobalKey(key));
        }

        // Files written before ConditionalIncreaseBonus get the block their settings came to: even steps for the cap, and
        // ConditionalFullChanceMultiplier (4 unless written) spread over one increase per boss after the first.
        internal static void FillIncreaseBonus(CreatureLevelSettings settings) {
            if (settings == null || settings.ConditionalIncreaseBonus != null) { return; }
            float full = Mathf.Max(1f, settings.LegacyFullChanceMultiplier ?? 4f);
            int steps = Math.Max(1, BossKeyOrder(settings).Count - 1);
            settings.ConditionalIncreaseBonus = new ConditionalIncreaseBonus() {
                ExtraStars = EvenStepsToMaxLevel,
                ChanceIncrease = Mathf.Round((full - 1f) / steps * 10000f) / 10000f,
            };
        }

        // --- the cache ------------------------------------------------------------------------------------------------

        // The biome's own entry within the active tier, else that tier's 'All' entry as its fallback.
        private static Heightmap.Biome EntryBiome(Heightmap.Biome biome) {
            return BiomeRegistry.TryGetKey(CurrentGlobalKeyConditionalLevelup, biome, out Heightmap.Biome key) ? key : Heightmap.Biome.All;
        }

        private static void ClearActiveTier() {
            CurrentGlobalKey = null;
            CurrentGlobalKeyConditionalLevelup.Clear();
            resolvedByBiome.Clear();
            increasesByBiome.Clear();
        }

        private static void RebuildCache() {
            ClearActiveTier();
            cacheValid = true;
            CreatureLevelSettings settings = LevelSystemData.SLE_Level_Settings;
            if (settings == null || ZoneSystem.instance == null) { return; }

            List<string> order = BossKeyOrder(settings);
            int latest = LatestDefeated(order);
            foreach (KeyValuePair<Heightmap.Biome, string> biome in LevelSystemData.OwnBossOfBiome) {
                int increases = IncreasesEarned(order, biome.Value, latest, settings.ConditionalBiomeDelay, settings.ConditionalDifficultyIncreases);
                if (increases > 0) { increasesByBiome[biome.Key] = increases; }
            }

            Dictionary<string, Dictionary<Heightmap.Biome, ConditionalLevelupChance>> conditional = settings.ConditionalCreatureLevelupChance;
            if (conditional == null) { return; }
            CurrentGlobalKey = SelectGlobalKey(conditional, order);
            // Nothing resolved still leaves the cache valid, so an empty result is not rebuilt on every call.
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
            Logger.LogDebug($"ConditionalScaleSystem: resolved conditional levelup generators for '{CurrentGlobalKey}' across {resolvedByBiome.Count} biome(s).");
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
