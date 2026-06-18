using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.IO;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class NemesisSystemData
    {
        public static NemesisConfiguration SLE_Nemesis_Settings = DefaultConfiguration;

        public static readonly NemesisConfiguration DefaultConfiguration = new NemesisConfiguration() {
            NemesisVersion = 3,
            NemesisActionCooldownSeconds = 10,
            NemesisInfluenceRadius = 300f,
            NemesisMinSpawnDistance = 30f,
            ScoreSystem = new NemesisScore() {
                MaxScore = 10000f,
                NeutralScore = 5000f,
                MinScore = 0f,
                ScoreIntervalSeconds = 30,
                DecayPerUpdate = 250f,

                NearbyAveragingWeight = 0.05f,
                NearbyPlayerRadius = 60f,

                BossKillBonus = 500f,
                BossKillRadius = 100f,
                
                MeleeDamageDealtFactor = 0.75f,
                RangedDamageDealtFactor = 0.25f,
                MagicDamageDealtFactor = 0.5f,
                DamageTakenFactor = -0.5f,
                DeathScoreReduction = 1500f,
            },
            GaurenteedChanges = new NemesisGaurenteedChanges() {
                FirstBossSetLevel = false,
                FirstBossLevel = 1,
            },
            ChanceChanges = new NemesisChanceChanges() { 
                CreatureOps = new Dictionary<string, NemesisChanceEntry>() {
                    { "TreasureTrolls", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.1f,
                        AllowedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mistlands },
                        // Blackforest/meadows level players get hunted when they go to plains
                        RequiredGlobalKeys = new List<string> { "defeated_goblinking" },
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -100,
                        ExtraCooldownSeconds = 60,
                        PlayerReqs = new NemesisPlayerStateRequirements() {
                            PlayerCurrentBiome = Heightmap.Biome.Plains,
                        },
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { 
                                Prefab = "Troll",
                                CreatureAI = AI.Alerted,
                                Faction = Character.Faction.Boss,
                                SpawnGroupSize = 3,
                                CustomName = "Treasure Troll",
                                CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.Size, 0.3f } },
                                CustomLoot = new List<ExtendedCharacterDrop>() {
                                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 250, Max = 750, Prefab = "Coins", Chance = 100f } },
                                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 10, Max = 15, Prefab = "Ruby", Chance = 25f } },
                                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 15, Max = 20, Prefab = "Amber", Chance = 25f } }
                                }
                            },
                    }}},
                    { "MistlandHunters", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.5f,
                        AllowedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mistlands },
                        // Blackforest/meadows level players get hunted when they go to plains
                        NotRequiredGlobalKeys = new List<string> { "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" },
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -100,
                        ExtraCooldownSeconds = 60,
                        PlayerReqs = new NemesisPlayerStateRequirements() {
                            PlayerCurrentBiome = Heightmap.Biome.Mistlands,
                        },
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Seeker", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.MistlandsMonsters, SpawnGroupSize = 3 },
                    }}},
                    { "PlainsDefenders", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.5f,
                        AllowedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Plains },
                        // Blackforest/meadows level players get hunted when they go to plains
                        NotRequiredGlobalKeys = new List<string> { "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" },
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -100,
                        ExtraCooldownSeconds = 60,
                        PlayerReqs = new NemesisPlayerStateRequirements() {
                            PlayerCurrentBiome = Heightmap.Biome.Plains,
                        },
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Goblin", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.PlainsMonsters, SpawnGroupSize = 6 },
                    }}},
                    { "MountainDefenders", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.5f,
                        AllowedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mountain },
                        // Blackforest/meadows level players get hunted when they go to plains
                        NotRequiredGlobalKeys = new List<string> { "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" },
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -100,
                        ExtraCooldownSeconds = 60,
                        PlayerReqs = new NemesisPlayerStateRequirements() {
                            PlayerCurrentBiome = Heightmap.Biome.Mountain,
                        },
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Hatchling", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.MountainMonsters, SpawnGroupSize = 4 },
                    }}},
                    { "CharredAttack", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.3f,
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.DeepNorth },
                        RequiredGlobalKeys = new List<string> { "defeated_fader" },
                        ScoreThreshold = 9000f,
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -2000,
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Charred_Melee", CreatureAI = AI.HuntPlayer, SpawnGroupSize = 1, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Fire", ModifierType.Major } } },
                            new NemesisSpawn() { Prefab = "Charred_Ranged", CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Fire", ModifierType.Major } } }
                    }}},
                    { "SeekerAttack", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.3f,
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth },
                        RequiredGlobalKeys = new List<string> { "defeated_queen" },
                        ScoreThreshold = 9000f,
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -2000,
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Seeker", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.MistlandsMonsters, SpawnGroupSize = 3, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Lightning", ModifierType.Major } } },
                    }}},
                    { "GoblinAttack", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.3f,
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth },
                        RequiredGlobalKeys = new List<string> { "defeated_goblinking" },
                        ScoreThreshold = 9000f,
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -2000,
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Goblin", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.PlainsMonsters, SpawnGroupSize = 3, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Fire", ModifierType.Major } } },
                            new NemesisSpawn() { Prefab = "GoblinShaman", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.PlainsMonsters, SpawnGroupSize = 1, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Lightning", ModifierType.Major } } },
                    }}},
                    { "FenringAttack", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.3f,
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth },
                        RequiredGlobalKeys = new List<string> { "defeated_dragon" },
                        ScoreThreshold = 9000f,
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -2000,
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Fenring", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.MountainMonsters, SpawnGroupSize = 2, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Big", ModifierType.Major } } },
                    }}},
                    { "SwampAttack", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.3f,
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth },
                        RequiredGlobalKeys = new List<string> { "defeated_bonemass" },
                        ScoreThreshold = 9000f,
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -2000,
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Draugr_Elite", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Undead, SpawnGroupSize = 2, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Poison", ModifierType.Major } } },
                    }}},
                    { "BlackForestSwarm", new NemesisChanceEntry() { 
                        Enabled = true, 
                        Chance = 0.3f, 
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth }, 
                        RequiredGlobalKeys = new List<string> { "defeated_gdking" }, 
                        ScoreThreshold = 9000f,
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5, 
                        ScoreChange = -2000, 
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Greydwarf", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.ForestMonsters, SpawnGroupSize = 6, RequiredModifiers = new Dictionary<string, ModifierType>() { { "FireNova", ModifierType.Minor } } },
                    }}},
                    // Reduce Creature levels to make things easier
                    { "ReduceCreatureLevel", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.25f,
                        ScoreThreshold = 4000f,
                        Action = NemesisAction.ChangeLevel,
                        LevelBonus = -1,
                        ScoreChange = 200f
                    }},
                    { "SignificantlyReduceCreatureLevel", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.5f,
                        ScoreThreshold = 2000f,
                        Action = NemesisAction.ChangeLevel,
                        LevelBonus = -3,
                        ScoreChange = 400f,
                    }},
                    // Increase Creature levels to make things harder
                    { "IncreaseCreatureLevel", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.25f,
                        ScoreThreshold = 6000f,
                        Action = NemesisAction.ChangeLevel,
                        LevelBonus = 1,
                        ScoreChange = -250f
                    }},
                    { "SignificantlyIncreaseCreatureLevel", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.5f,
                        ScoreThreshold = 8000f,
                        Action = NemesisAction.ChangeLevel,
                        LevelBonus = 3,
                        ScoreChange = -500f
                    }},

                    { "NemesisMiniboss1", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.05f,
                        ScoreThreshold = 9500f,
                        Action = NemesisAction.SpawnMiniboss,
                        LevelBonus = 7,
                        ScoreChange = -3000f
                    }},
                },
            },
            AvailableMiniBosses = new List<NemesisMiniboss>(),
            NemesisMinionTemplatesByBiome = new Dictionary<Heightmap.Biome, List<NemesisMinion>>() {
                { Heightmap.Biome.Meadows, new List<NemesisMinion>() {
                    new NemesisMinion() { PrefabName = "Neck", MinAmount = 6, MaxAmount = 9, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Boar", MinAmount = 3, MaxAmount = 6, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Greyling", MinAmount = 2, MaxAmount = 5, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                }},
                { Heightmap.Biome.BlackForest, new List<NemesisMinion>() {
                    new NemesisMinion() { PrefabName = "GreyDwarf", MinAmount = 6, MaxAmount = 12, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "GreyDwarfShaman", MinAmount = 2, MaxAmount = 4, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Skeleton", MinAmount = 4, MaxAmount = 10, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "SkeletonArcher", MinAmount = 3, MaxAmount = 6, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                }},
                { Heightmap.Biome.Swamp, new List<NemesisMinion>() {
                    new NemesisMinion() { PrefabName = "Surtling", MinAmount = 6, MaxAmount = 12, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Draugr", MinAmount = 4, MaxAmount = 8, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Draugr_Ranged", MinAmount = 3, MaxAmount = 6, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Blob", MinAmount = 6, MaxAmount = 10, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                }},
                { Heightmap.Biome.Mountain, new List<NemesisMinion>() {
                    new NemesisMinion() { PrefabName = "Wolf", MinAmount = 4, MaxAmount = 8, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Ulv", MinAmount = 4, MaxAmount = 8, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Bat", MinAmount = 8, MaxAmount = 20, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Hatchling", MinAmount = 2, MaxAmount = 4, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                }},
                { Heightmap.Biome.Plains, new List<NemesisMinion>() {
                    new NemesisMinion() { PrefabName = "Deathsquito", MinAmount = 4, MaxAmount = 8, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 3f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Fuling", MinAmount = 6, MaxAmount = 12, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "FulingArcher", MinAmount = 5, MaxAmount = 9, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "FulingShaman", MinAmount = 2, MaxAmount = 4, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                }},
                { Heightmap.Biome.Mistlands, new List<NemesisMinion>() {
                    new NemesisMinion() { PrefabName = "Seeker", MinAmount = 4, MaxAmount = 7, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "DvergerRouge", MinAmount = 3, MaxAmount = 6, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "DvergerMage", MinAmount = 2, MaxAmount = 5, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Gjall", MinAmount = 2, MaxAmount = 4, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                }},
                { Heightmap.Biome.AshLands, new List<NemesisMinion>() {
                    new NemesisMinion() { PrefabName = "Charred_Archer", MinAmount = 3, MaxAmount = 6, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Charred_Twitcher", MinAmount = 6, MaxAmount = 10, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Charred_Mage", MinAmount = 1, MaxAmount = 3, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                    new NemesisMinion() { PrefabName = "Charred_Melee", MinAmount = 3, MaxAmount = 5, CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() { { CreatureBaseAttribute.BaseHealth, 2f } }, CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>() { { CreaturePerLevelAttribute.DamagePerLevel, 0.01f } } },
                }},
            }

        };

        internal static void Init() {
            SLE_Nemesis_Settings = DefaultConfiguration;
            try {
                if (File.Exists(ValConfig.nemesisFilePath)) {
                    UpdateYamlConfig(File.ReadAllText(ValConfig.nemesisFilePath));
                }
            }
            catch (Exception e) { Jotunn.Logger.LogWarning($"There was an error updating the Nemesis values, defaults will be used. Exception: {e}"); }
        }

        public static string YamlDefaultConfig() {
            return DataObjects.yamlserializer.Serialize(DefaultConfiguration);
        }

        internal static void UpdateNemesisLog(string data) {
            Logger.LogNemesis($"Updating Nemesis Action Log: {ValConfig.nemesisLogFilePath}");
            ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
            const long maxLogSizeBytes = 1024L * 1024L * 1024L; // 1 GB
            FileInfo logInfo = new FileInfo(ValConfig.nemesisLogFilePath);
            if (logInfo.Exists && logInfo.Length > maxLogSizeBytes) {
                Logger.LogNemesis($"Nemesis log exceeded {maxLogSizeBytes} bytes, overwriting with most recent event.");
                File.WriteAllText(ValConfig.nemesisLogFilePath, data);
                return;
            }
            File.AppendAllText(ValConfig.nemesisLogFilePath, data);
        }

        public static bool UpdateYamlConfig(string yaml) {
            try {
                Logger.LogNemesis("Loaded new Nemesis settings...");
                SLE_Nemesis_Settings = DataObjects.yamldeserializer.Deserialize<NemesisConfiguration>(yaml);
                if (SLE_Nemesis_Settings.NemesisVersion != DefaultConfiguration.NemesisVersion) {
                    Logger.LogInfo("Nemesis Config version outdated, resetting to default.");
                    SLE_Nemesis_Settings = DefaultConfiguration;
                    File.WriteAllText(ValConfig.nemesisFilePath, YamlDefaultConfig());
                }
            }
            catch (Exception ex) {
                StarLevelSystem.Log.LogWarning($"Failed to parse NemesisSettings.yaml, defaults will be used. Error: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}
