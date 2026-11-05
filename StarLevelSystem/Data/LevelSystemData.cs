using Jotunn;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.modules;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class LevelSystemData
    {

        // Vanilla boss defeat keys, earliest progression first. Declared above DefaultConfiguration because static
        // initializers run in textual order and the default config copies this list.
        internal static readonly List<string> VanillaBossKeyOrder = new List<string>() {
            "defeated_eikthyr",
            "defeated_gdking",
            "defeated_bonemass",
            "defeated_dragon",
            "defeated_goblinking",
            "defeated_queen",
            "defeated_fader",
            // The Deep North's final boss. Its first phase sets defeated_frozenking partway through the fight; this
            // key is set when the last phase dies.
            "defeated_frozenking_p3",
        };

        // The boss each biome belongs to. Conditional scaling counts a biome's difficulty increases from it.
        internal static readonly Dictionary<Heightmap.Biome, string> OwnBossOfBiome = new Dictionary<Heightmap.Biome, string>() {
            { Heightmap.Biome.Meadows, "defeated_eikthyr" },
            { Heightmap.Biome.BlackForest, "defeated_gdking" },
            { Heightmap.Biome.Swamp, "defeated_bonemass" },
            { Heightmap.Biome.Mountain, "defeated_dragon" },
            { Heightmap.Biome.Plains, "defeated_goblinking" },
            { Heightmap.Biome.Mistlands, "defeated_queen" },
            { Heightmap.Biome.AshLands, "defeated_fader" },
            { Heightmap.Biome.DeepNorth, "defeated_frozenking_p3" },
        };

        // Bumped when what the shipped files hold for conditional scaling changes. Files below it go through RemoveShippedTiers.
        private const int CurrentConditionalTierVersion = 2;

        // The MinLevel and MaxLevel of every generator a shipped tier has held, in any release.
        private static readonly HashSet<(int, int)> ShippedTierLevels = new HashSet<(int, int)>() {
            (1, 8), (2, 8), (2, 12), (3, 12), (3, 16), (4, 16), (4, 20), (5, 20), (5, 24), (6, 24), (6, 30), (7, 30), (8, 36), (9, 42),
        };

        // Assigned in the static constructor, which runs after every field initializer. An initializer here ran before
        // DefaultConfiguration's (initializers run in textual order), so this was null until the first config load.
        public static DataObjects.CreatureLevelSettings SLE_Level_Settings;

        static LevelSystemData() {
            SLE_Level_Settings = DefaultConfiguration;
        }

        public static readonly DataObjects.CreatureLevelSettings DefaultConfiguration = new DataObjects.CreatureLevelSettings()
        {
            // Named, reusable level generators. These are examples and are NOT referenced anywhere by default.
            // Reference them by name from any DefaultLevelupGeneratorRefs / LevelupGeneratorRefs list (biome,
            // creature, conditional, raid, or nemesis spawn); the generated curve overwrites that section's chances.
            CustomLevelupGenerators = new Dictionary<string, List<LevelGenerator>>() {
                { "early_game", new List<LevelGenerator>() {
                        new LevelGenerator() { MinLevel = 0, MaxLevel = 6, LevelUpChance = 0.2f, LevelupCalculationStyle = DataObjects.LevelupCalculationStyle.Exponential }
                    }
                },
                { "late_game", new List<LevelGenerator>() {
                        new LevelGenerator() { MinLevel = 1, MaxLevel = 25, LevelUpChance = 0.35f, LevelupCalculationStyle = DataObjects.LevelupCalculationStyle.Gaussian, GaussianOffset = 0.25f }
                    }
                },
            },
            // Normal at the default max level, so a new install reads as that difficulty on the panel's first page.
            DefaultCreatureLevelUpChance = DifficultyPreset.Normal.CreatureCurve(DifficultyPreset.DefaultMaxLevel),
            BiomeConfiguration = new Dictionary<Heightmap.Biome, DataObjects.BiomeSpecificSetting>()
            {
                { Heightmap.Biome.All, new DataObjects.BiomeSpecificSetting()
                    {
                        SpawnRateModifier = 1.1f,
                        DistanceScaleModifier = 1.5f,
                        DamageRecievedModifiers = new Dictionary<DataObjects.DamageType, float>() {
                            {DataObjects.DamageType.Poison, 1.5f } 
                        },
                        NightSettings = new BiomeNightSettings() {
                            NightLevelUpChanceScaler = 1.5f,
                        }
                    }
                },
                { Heightmap.Biome.Meadows, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = DifficultyPreset.BiomeCap(Heightmap.Biome.Meadows, DifficultyPreset.DefaultMaxLevel),
                    }
                },
                { Heightmap.Biome.BlackForest, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = DifficultyPreset.BiomeCap(Heightmap.Biome.BlackForest, DifficultyPreset.DefaultMaxLevel),
                    }
                },
                { Heightmap.Biome.Swamp, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = DifficultyPreset.BiomeCap(Heightmap.Biome.Swamp, DifficultyPreset.DefaultMaxLevel),
                    }
                },
                { Heightmap.Biome.Mountain, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = DifficultyPreset.BiomeCap(Heightmap.Biome.Mountain, DifficultyPreset.DefaultMaxLevel),
                    }
                },
                { Heightmap.Biome.Plains, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = DifficultyPreset.BiomeCap(Heightmap.Biome.Plains, DifficultyPreset.DefaultMaxLevel),
                    }
                },
                { Heightmap.Biome.Mistlands, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = DifficultyPreset.BiomeCap(Heightmap.Biome.Mistlands, DifficultyPreset.DefaultMaxLevel),
                    }
                },
                { Heightmap.Biome.AshLands, new DataObjects.BiomeSpecificSetting()
                    {
                        DistanceScaleModifier = 0.5f,
                        BiomeMaxLevelOverride = DifficultyPreset.BiomeCap(Heightmap.Biome.AshLands, DifficultyPreset.DefaultMaxLevel),
                    }
                },
                { Heightmap.Biome.DeepNorth, new DataObjects.BiomeSpecificSetting()
                    {
                        DistanceScaleModifier = 0.5f,
                        BiomeMaxLevelOverride = DifficultyPreset.BiomeCap(Heightmap.Biome.DeepNorth, DifficultyPreset.DefaultMaxLevel),
                    }
                }
            },

            CreatureConfiguration = new Dictionary<string, DataObjects.CreatureSpecificSetting>() {
                { "piece_TrainingDummy", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 11,
                        SpawnRateModifier = 1f,
                        CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                            { DataObjects.CreatureBaseAttribute.BaseHealth, 1.05f },
                            { DataObjects.CreatureBaseAttribute.BaseDamage, 0.95f },
                            { DataObjects.CreatureBaseAttribute.Speed, 1.05f },
                            { DataObjects.CreatureBaseAttribute.Size, 1.05f },
                            { DataObjects.CreatureBaseAttribute.AttackSpeed, 1.05f },
                        },
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.005f }
                        },
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>()
                        {
                            {1, 100 },
                            {2, 100 },
                            {3, 0 },
                        }
                    }
                },
                { "Troll", new DataObjects.CreatureSpecificSetting()
                    {
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.05f },
                        }
                    }
                },
                { "Bjorn", new DataObjects.CreatureSpecificSetting()
                    {
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.05f },
                        }
                    }
                },
                { "GoblinBruteBros", new DataObjects.CreatureSpecificSetting()
                    {
                        SpawnRateModifier = 1f,
                    }
                },
                { "GoblinShaman_Hildir", new DataObjects.CreatureSpecificSetting()
                    {
                        SpawnRateModifier = 1f,
                    }
                },
                { "GoblinBrute_Hildir", new DataObjects.CreatureSpecificSetting()
                    {
                        SpawnRateModifier = 1f,
                    }
                },
                { "Skeleton_Hildir", new DataObjects.CreatureSpecificSetting()
                    {
                        SpawnRateModifier = 1f,
                    }
                },
                { "Fenring_Cultist_Hildir", new DataObjects.CreatureSpecificSetting()
                    {
                        SpawnRateModifier = 1f,
                    }
                },
                { "Charred_Melee_Dyrnwyn", new DataObjects.CreatureSpecificSetting()
                    {
                        SpawnRateModifier = 1f,
                    }
                },
            },

            EnableDistanceLevelBonus = true,
            DistanceLevelBonus = new SortedDictionary<int, SortedDictionary<int, float>>()
            {
                { 750, new SortedDictionary<int, float>() {
                        { 1, 15f },
                        { 2, 5f },
                        { 3, 1f },
                    }
                },
                { 1200, new SortedDictionary<int, float>() {
                        { 1, 18f },
                        { 2, 7f },
                        { 3, 1.5f },
                        { 4, 0.5f },
                    }
                },
                { 2000, new SortedDictionary<int, float>() {
                        { 1, 24f },
                        { 2, 10f },
                        { 3, 3f },
                        { 4, 1f },
                        { 5, 0.5f },
                    }
                },
                { 3000, new SortedDictionary<int, float>() {
                        { 1, 32f },
                        { 2, 16f },
                        { 3, 8f },
                        { 4, 4f },
                        { 5, 2f },
                        { 6, 1f },
                    }
                },
                { 4000, new SortedDictionary<int, float>() {
                        { 1, 40f },
                        { 2, 25f },
                        { 3, 12f },
                        { 4, 6f },
                        { 5, 3f },
                        { 6, 2f },
                        { 7, 1f },
                        { 8, 0.5f },
                    }
                },
                { 5000, new SortedDictionary<int, float>() {
                        { 1, 50f },
                        { 2, 35f },
                        { 3, 18f },
                        { 4, 9f },
                        { 5, 6f },
                        { 6, 3f },
                        { 7, 2f },
                        { 8, 1f },
                        { 9, 0.5f },
                        { 10, 0.25f },
                    }
                },
                { 6000, new SortedDictionary<int, float>() {
                        { 1, 60f },
                        { 2, 40f },
                        { 3, 20f },
                        { 4, 10f },
                        { 5, 8f },
                        { 6, 6f },
                        { 7, 4f },
                        { 8, 2f },
                        { 9, 1f },
                        { 10, 0.5f },
                        { 11, 0.25f },
                        { 12, 0.12f },
                    }
                },
                { 7000, new SortedDictionary<int, float>() {
                        { 1, 70f },
                        { 2, 50f },
                        { 3, 25f },
                        { 4, 12f },
                        { 5, 10f },
                        { 6, 8f },
                        { 7, 6f },
                        { 8, 4f },
                        { 9, 2f },
                        { 10, 1f },
                        { 11, 0.5f },
                        { 12, 0.25f },
                        { 13, 0.12f },
                        { 14, 0.06f },
                    }
                },
                { 8000, new SortedDictionary<int, float>() {
                        { 1, 80f },
                        { 2, 60f },
                        { 3, 40f },
                        { 4, 20f },
                        { 5, 15f },
                        { 6, 12f },
                        { 7, 10f },
                        { 8, 8f },
                        { 9, 6f },
                        { 10, 4f },
                        { 11, 2f },
                        { 12, 1f },
                        { 13, 0.5f },
                        { 14, 0.25f },
                        { 15, 0.12f },
                        { 16, 0.06f },
                    }
                },
                { 9100, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 80f },
                        { 3, 60f },
                        { 4, 40f },
                        { 5, 30f },
                        { 6, 20f },
                        { 7, 16f },
                        { 8, 14f },
                        { 9, 12f },
                        { 10, 10f },
                        { 11, 8f },
                        { 12, 4f },
                        { 13, 2f },
                        { 14, 1f },
                        { 15, 0.5f },
                        { 16, 0.25f },
                        { 17, 0.12f },
                        { 18, 0.06f },
                    }
                }
            },
            // Example tables for the Table curve style. No default generator uses them; a Table generator whose
            // MinLevel..MaxLevel covers 4, 5 or 6 levels picks up the matching entry.
            LevelupChanceTablesBySpan = new Dictionary<int, SortedDictionary<int, float>>() {
                { 4, new SortedDictionary<int, float>() { { 1, 30f }, { 2, 15f },   { 3, 5f },      { 4, 0.01f } } },
                { 5, new SortedDictionary<int, float>() { { 1, 30f }, { 2, 16f },   { 3, 6.8333f }, { 4, 2.5f }, { 5, 0.01f } } },
                { 6, new SortedDictionary<int, float>() { { 1, 30f }, { 2, 17.5f }, { 3, 8.5f },    { 4, 3.0f }, { 5, 1.0f }, { 6, 0.01f } } },
            },
            EnableConditionalCreatureLevelupChance = false,
            ConditionalBossKeyOrder = new List<string>(VanillaBossKeyOrder),
            // Empty: biomes work out their increases from the world's own settings, so they follow any max level and curve. This
            // holds fixed levels someone writes for a boss and biome.
            ConditionalCreatureLevelupChance = new Dictionary<string, Dictionary<Heightmap.Biome, ConditionalLevelupChance>>(),
            ConditionalTierVersion = CurrentConditionalTierVersion,
        };

        // ConditionalBossKeyOrder as every LevelSettings.yaml written before the Deep North update holds it.
        private static readonly List<string> PreDeepNorthBossKeyOrder = new List<string>() {
            "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader",
        };

        // One-shot in-place migration for LevelSettings.yaml. A file still holding the old vanilla boss order gains the
        // Deep North's final boss key. Appending the key is also what stops this firing again, and a file whose order was
        // changed by hand is left alone.
        internal static bool AddDeepNorthBossKey(CreatureLevelSettings settings) {
            List<string> order = settings?.ConditionalBossKeyOrder;
            if (order == null || order.Count != PreDeepNorthBossKeyOrder.Count) { return false; }
            for (int i = 0; i < order.Count; i++) {
                if (string.Equals(order[i], PreDeepNorthBossKeyOrder[i], StringComparison.OrdinalIgnoreCase) == false) { return false; }
            }
            order.Add("defeated_frozenking_p3");
            return true;
        }

        // One-shot in-place migration for LevelSettings.yaml. Shipped tiers used to be written as fixed levels, which every
        // file then held, and those rolled far past a low MaxLevel and replaced the world's own curve. An entry still holding
        // what shipped goes, so the biome uses its difficulty increases, and so does an entry with nothing in it. Anything else
        // was written by hand, so it stays. The version stamp is what stops this firing again, so writing those numbers back
        // afterwards sticks.
        internal static bool RemoveShippedTiers(CreatureLevelSettings settings) {
            if (settings == null || settings.ConditionalTierVersion >= CurrentConditionalTierVersion) { return false; }
            settings.ConditionalTierVersion = CurrentConditionalTierVersion;
            Dictionary<string, Dictionary<Heightmap.Biome, ConditionalLevelupChance>> tiers = settings.ConditionalCreatureLevelupChance;
            if (tiers == null) { return true; }
            foreach (string key in tiers.Keys.ToList()) {
                Dictionary<Heightmap.Biome, ConditionalLevelupChance> tier = tiers[key];
                if (tier == null) { continue; }
                foreach (Heightmap.Biome biome in tier.Keys.ToList()) {
                    ConditionalLevelupChance entry = tier[biome];
                    bool empty = entry == null || ((entry.LevelupGenerators == null || entry.LevelupGenerators.Count == 0) && (entry.LevelupGeneratorRefs == null || entry.LevelupGeneratorRefs.Count == 0));
                    if (empty || (ShippedInTier(key, biome) && HoldsShippedLevels(entry))) { tier.Remove(biome); }
                }
                if (tier.Count == 0) { tiers.Remove(key); }
            }
            return true;
        }

        // Shipped tiers named a biome from its own boss on, and the Deep North from the Fader on, alongside the Ashlands.
        private static bool ShippedInTier(string key, Heightmap.Biome biome) {
            if (OwnBossOfBiome.TryGetValue(biome, out string own) == false) { return false; }
            if (biome == Heightmap.Biome.DeepNorth) { own = "defeated_fader"; }
            int tier = VanillaBossKeyOrder.IndexOf(key);
            return tier >= 0 && tier >= VanillaBossKeyOrder.IndexOf(own);
        }

        private static bool HoldsShippedLevels(ConditionalLevelupChance entry) {
            if (entry.LevelupGeneratorRefs != null && entry.LevelupGeneratorRefs.Count > 0) { return false; }
            if (entry.LevelupGenerators == null || entry.LevelupGenerators.Count != 1) { return false; }
            LevelGenerator generator = entry.LevelupGenerators[0];
            return generator != null && ShippedTierLevels.Contains((generator.MinLevel, generator.MaxLevel))
                && Mathf.Approximately(generator.LevelUpChance, 0.25f) && generator.LevelupCalculationStyle == LevelupCalculationStyle.Exponential
                && Mathf.Approximately(generator.NightMultiplier, 1f) && Mathf.Approximately(generator.GaussianOffset, 0f);
        }

        // Everything LevelSettings.yaml needs brought up to date when it loads. Both run, so neither can hide the other.
        internal static bool MigrateLevelSettings(CreatureLevelSettings settings) {
            bool addedBoss = AddDeepNorthBossKey(settings);
            bool removedTiers = RemoveShippedTiers(settings);
            return addedBoss || removedTiers;
        }


        // Everything that has to happen once new level settings exist, whatever produced them -- a hand
        // edit, a server broadcast, or the in-game editor. Registered as the Apply hook for
        // LevelSettings.yaml, so all three routes run identically.
        internal static void ApplyLoaded(DataObjects.CreatureLevelSettings parsed) {
            SLE_Level_Settings = parsed;
            AuthoredLevelSettings = CopyOf(parsed);
            // Before ApplyLevelupGenerators, so its Table fallback warnings are reported for this load.
            LevelGenerator.ClearWarnings();
            ValidateLevelSettings(parsed);
            ApplyLevelupGenerators();
            Logger.LogDebug("Loaded new Star Level Creature settings, updating loaded creatures...");
            DistanceScaleSystem.DelayedMinimapSetup();
            CompositeLazyCache.FlushCache();
            ConditionalScaleSystem.ResetCache();
            LevelSelection.ClearMergedBiomeSettings();
            BiomeRegistry.OnLevelSettingsApplied();
            RestartCreatureAttributeUpdate();
        }

        private static Coroutine runningAttributeUpdate;

        // Re-runs creature setup over every loaded creature, replacing any pass already running.
        // Coroutine rather than a straight loop: this walks every Character in the scene and is
        // budgeted per frame. Harmless at startup, where the scene is empty.
        // Character.GetAllCharacters() is the live registry - Resources.FindObjectsOfTypeAll also
        // walked every loaded asset (including prefabs) synchronously on each reload.
        // Only one pass runs at a time: a new one covers every creature the old one had left.
        internal static void RestartCreatureAttributeUpdate() {
            var runner = TaskRunner.Run();
            if (runningAttributeUpdate != null) {
                runner.StopCoroutine(runningAttributeUpdate);
                runningAttributeUpdate = null;
            }
            runningAttributeUpdate = runner.StartCoroutine(UpdateCreatureAttributes(new List<Character>(Character.GetAllCharacters())));
        }

        // The level settings as the file wrote them, taken before ApplyLevelupGenerators expands generators into the
        // chance tables of the live copy. Editors save from this: saving the live copy wrote every expansion back over the
        // hand-written table it had replaced, so removing a generator later could no longer bring that table back. Null
        // until the first load, when the live settings are still the untouched defaults.
        internal static DataObjects.CreatureLevelSettings AuthoredLevelSettings { get; private set; }

        // A deep copy through the file's own yaml format, so it holds exactly what a save would write.
        private static DataObjects.CreatureLevelSettings CopyOf(DataObjects.CreatureLevelSettings settings) {
            if (settings == null) { return null; }
            try {
                // LevelSettings is still unassigned during the first load, which runs from inside its registration.
                YamlFormat format = YamlConfigManager.LevelSettings?.EffectiveFormat ?? YamlFormat.Default;
                return format.Deserializer.Deserialize<DataObjects.CreatureLevelSettings>(format.Serializer.Serialize(settings));
            } catch (Exception e) {
                Logger.LogWarning($"Could not keep a copy of the level settings as written; the config panel will save the expanded curves: {e.Message}");
                return null;
            }
        }

        // Reports settings that load fine but cannot do what was meant. Runs once per apply, so these show up
        // when the file is loaded rather than on every spawn that reads them.
        private static void ValidateLevelSettings(CreatureLevelSettings settings) {
            if (settings == null) { return; }

            ValidateChanceTables("LevelupChanceTablesBySpan", settings.LevelupChanceTablesBySpan, "use the Exponential curve");
            ValidateChanceTables("BossLevelupChanceTablesBySpan", settings.BossLevelupChanceTablesBySpan, "read LevelupChanceTablesBySpan instead");

            if (settings.ConditionalCreatureLevelupChance != null) {
                HashSet<string> ordered = new HashSet<string>(ConditionalScaleSystem.BossKeyOrder(settings).Where(k => k != null), StringComparer.OrdinalIgnoreCase);
                foreach (string key in settings.ConditionalCreatureLevelupChance.Keys) {
                    if (key != null && ordered.Contains(key) == false) {
                        Logger.LogWarning($"ConditionalCreatureLevelupChance key '{key}' is not in ConditionalBossKeyOrder, so it only applies while no listed boss key is defeated. Add it to ConditionalBossKeyOrder to give it a tier.");
                    }
                }
            }
        }

        private static void ValidateChanceTables(string name, Dictionary<int, SortedDictionary<int, float>> tables, string whenEmpty) {
            if (tables != null) {
                foreach (KeyValuePair<int, SortedDictionary<int, float>> entry in tables) {
                    SortedDictionary<int, float> table = entry.Value;
                    if (table == null || table.Count == 0) {
                        Logger.LogWarning($"{name} '{entry.Key}' is empty; Table generators covering {entry.Key} levels will {whenEmpty}.");
                        continue;
                    }
                    // Keys are sorted and unique, so this is exactly "one entry for each of 1..span".
                    if (table.Count != entry.Key || table.Keys.First() != 1 || table.Keys.Last() != entry.Key) {
                        Logger.LogWarning($"{name} '{entry.Key}' should have keys 1-{entry.Key}, but has {string.Join(", ", table.Keys)}. Keys outside 1-{entry.Key} are ignored and missing keys leave those levels out of the roll.");
                    }
                    float previous = float.MaxValue;
                    foreach (KeyValuePair<int, float> kvp in table) {
                        if (kvp.Value >= previous) {
                            Logger.LogWarning($"{name} '{entry.Key}': key {kvp.Key} ({kvp.Value}) is not lower than the key before it ({previous}). Values are the % chance to roll past each level and must decrease, otherwise that level can never be rolled.");
                            break;
                        }
                        previous = kvp.Value;
                    }
                }
            }
        }

        // Expands any configured level generators (inline or referenced via CustomLevelupGenerators) into the
        // levelup-chance tables of the default/biome/creature sections, overwriting the existing chances when
        // generators are present. Runs against the freshly deserialized settings so the shared DefaultConfiguration
        // static is never mutated.
        private static void ApplyLevelupGenerators() {
            CreatureLevelSettings settings = SLE_Level_Settings;
            if (settings == null) { return; }

            SortedDictionary<int, float> defaultChances = LevelGeneratorResolver.BuildLevelupChance(settings.DefaultLevelupGenerators, settings.DefaultLevelupGeneratorRefs, out float defaultNight);
            if (defaultChances != null) { settings.DefaultCreatureLevelUpChance = defaultChances; }
            settings.DefaultGeneratorNightMultiplier = defaultNight;

            SortedDictionary<int, float> bossChances = LevelGeneratorResolver.BuildLevelupChance(settings.BossLevelupGenerators, settings.BossLevelupGeneratorRefs, out float bossNight, settings.BossLevelupChanceTablesBySpan);
            if (bossChances != null) { settings.BossCreatureLevelUpChance = bossChances; }
            settings.BossGeneratorNightMultiplier = bossNight;

            if (settings.BiomeConfiguration != null) {
                foreach (BiomeSpecificSetting biome in settings.BiomeConfiguration.Values) {
                    if (biome == null) { continue; }
                    SortedDictionary<int, float> biomeChances = LevelGeneratorResolver.BuildLevelupChance(biome.LevelupGenerators, biome.LevelupGeneratorRefs, out float biomeNight);
                    if (biomeChances != null) { biome.CustomCreatureLevelUpChance = biomeChances; }
                    biome.GeneratorNightMultiplier = biomeNight;
                }
            }

            if (settings.CreatureConfiguration != null) {
                foreach (CreatureSpecificSetting creature in settings.CreatureConfiguration.Values) {
                    if (creature == null) { continue; }
                    SortedDictionary<int, float> creatureChances = LevelGeneratorResolver.BuildLevelupChance(creature.LevelupGenerators, creature.LevelupGeneratorRefs, out float creatureNight);
                    if (creatureChances != null) { creature.CustomCreatureLevelUpChance = creatureChances; }
                    creature.GeneratorNightMultiplier = creatureNight;
                }
            }
        }

        internal static IEnumerator UpdateCreatureAttributes(List<Character> characters) {
            int i = 0;
            WaitForSeconds sleep = new WaitForSeconds(0.1f);
            try {
                foreach (var character in characters) {
                    if (i >= ValConfig.NumberOfCacheUpdatesPerFrame.Value) {
                        yield return sleep;
                        i = 0;
                    }
                    if (character == null || character.m_nview == null || character.m_nview.IsValid() == false) { continue; }
                    // Without this, SetSizeModification short-circuits on the persisted SLS_SIZE and a changed
                    // Size/SizePerLevel would not apply to already-spawned creatures.
                    CreatureSetupQueue.MarkForRefresh(character);
                    CreatureSetupControl.CreatureSetup(character, delay: 0);
                    i++;
                }
            } finally {
                runningAttributeUpdate = null;
            }
        }
    }
}
