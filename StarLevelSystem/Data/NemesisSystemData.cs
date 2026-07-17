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
            NemesisVersion = 5,
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
                    { "Ocean Serpent Attack", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.3f,
                        AllowedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Ocean },
                        // Blackforest/meadows level players get hunted when they go to plains
                        RequiredGlobalKeys = new List<string> { "defeated_gdking" },
                        Action = NemesisAction.Spawn,
                        ScoreChange = -1000,
                        ScoreThreshold = 7000f,
                        ExtraCooldownSeconds = 300,
                        PlayerReqs = new NemesisPlayerStateRequirements() {
                            PlayerCurrentBiome = Heightmap.Biome.Ocean,
                            MinBiomeHistory = 2,
                        },
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Serpent", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.SeaMonsters, SpawnGroupSize = 1 },
                    }}},
                    { "Ocean Serpent Ambush", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.3f,
                        AllowedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Ocean },
                        // Blackforest/meadows level players get hunted when they go to plains
                        RequiredGlobalKeys = new List<string> { "defeated_bonemass" },
                        Action = NemesisAction.Spawn,
                        ScoreChange = -1000,
                        ScoreThreshold = 7000f,
                        ExtraCooldownSeconds = 300,
                        PlayerReqs = new NemesisPlayerStateRequirements() {
                            PlayerCurrentBiome = Heightmap.Biome.Ocean,
                            MinBiomeHistory = 5,
                        },
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Serpent", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.SeaMonsters, SpawnGroupSize = 2 },
                    }}},
                    { "TreasureTrolls", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.1f,
                        AllowedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mistlands },
                        RequiredGlobalKeys = new List<string> { "defeated_goblinking" },
                        Action = NemesisAction.Spawn,
                        LevelBonus = 5,
                        ScoreChange = -100,
                        ExtraCooldownSeconds = 60,
                        PlayerReqs = new NemesisPlayerStateRequirements() {
                            PlayerCurrentBiome = Heightmap.Biome.Plains,
                            MinBiomeHistory = 3,
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
                            MinBiomeHistory = 3,
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
                            MinBiomeHistory = 2,
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
                            MinBiomeHistory = 2,
                        },
                        SpawnConfig = new List<NemesisSpawn>(){
                            new NemesisSpawn() { Prefab = "Hatchling", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.MountainMonsters, SpawnGroupSize = 4 },
                    }}},
                    { "CharredAttack", new NemesisChanceEntry() {
                        Enabled = true,
                        Chance = 0.3f,
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean },
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
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean },
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
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean },
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
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean },
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
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean },
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
                        DeniedBiomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean }, 
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
            },
            RemoteSpawning = new RemoteNemesisSpawnSettings() {
                Enabled = true,
                CheckIntervalMinutes = 30f,
                MaxSpawnsPerInterval = 3,
                MaxConcurrentTotal = 10,
                ShowMapPin = true,
                MapPinAreaRadius = 60f,
                MapPinSpriteAsset = "",
                FallbackPinType = Minimap.PinType.Boss,
                PinShowsBossName = true,
                TargetPerBiome = new Dictionary<Heightmap.Biome, int>() {
                    { Heightmap.Biome.Meadows, 1 },
                    { Heightmap.Biome.BlackForest, 1 },
                    { Heightmap.Biome.Swamp, 1 },
                    { Heightmap.Biome.Mountain, 1 },
                    { Heightmap.Biome.Plains, 1 },
                    { Heightmap.Biome.Mistlands, 1 },
                    { Heightmap.Biome.AshLands, 1 },
                },
                MaxConcurrentPerBiome = new Dictionary<Heightmap.Biome, int>() {
                    { Heightmap.Biome.Meadows, 2 },
                    { Heightmap.Biome.BlackForest, 2 },
                    { Heightmap.Biome.Swamp, 2 },
                    { Heightmap.Biome.Mountain, 2 },
                    { Heightmap.Biome.Plains, 2 },
                    { Heightmap.Biome.Mistlands, 2 },
                    { Heightmap.Biome.AshLands, 2 },
                },
                BiomeRadiusRanges = new Dictionary<Heightmap.Biome, BiomeSpawnRadius>() {
                    { Heightmap.Biome.Meadows, new BiomeSpawnRadius() { Min = 500f, Max = 3000f } },
                    { Heightmap.Biome.BlackForest, new BiomeSpawnRadius() { Min = 600f, Max = 4000f } },
                    { Heightmap.Biome.Swamp, new BiomeSpawnRadius() { Min = 1000f, Max = 5000f } },
                    { Heightmap.Biome.Mountain, new BiomeSpawnRadius() { Min = 1000f, Max = 6000f } },
                    { Heightmap.Biome.Plains, new BiomeSpawnRadius() { Min = 2000f, Max = 7000f } },
                    { Heightmap.Biome.Mistlands, new BiomeSpawnRadius() { Min = 3000f, Max = 8000f } },
                    { Heightmap.Biome.AshLands, new BiomeSpawnRadius() { Min = 8000f, Max = 10000f } },
                },
                BossCandidatesByBiome = new Dictionary<Heightmap.Biome, List<NemesisSpawn>>() {
                    { Heightmap.Biome.Meadows, new List<NemesisSpawn>() {
                        new NemesisSpawn() {
                            Prefab = "Boar",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 2, MaxLevel = 8, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Neck",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 3, MaxLevel = 9, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Greyling",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 2, MaxLevel = 7, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                    }},
                    { Heightmap.Biome.BlackForest, new List<NemesisSpawn>() {
                        new NemesisSpawn() {
                            Prefab = "Greydwarf",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 3, MaxLevel = 10, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Skeleton",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 4, MaxLevel = 11, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Troll",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 3, MaxLevel = 8, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 2f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                    }},
                    { Heightmap.Biome.Swamp, new List<NemesisSpawn>() {
                        new NemesisSpawn() {
                            Prefab = "DraugrElite",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 4, MaxLevel = 11, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Wraith",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 4, MaxLevel = 11, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Abomination",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 4, MaxLevel = 8, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                    }},
                    { Heightmap.Biome.Mountain, new List<NemesisSpawn>() {
                        new NemesisSpawn() {
                            Prefab = "Fenring",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 5, MaxLevel = 10, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Fenring_Cultist",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 5, MaxLevel = 10, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 2f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "StoneGolem",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 5, MaxLevel = 10, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 2f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                    }},
                    { Heightmap.Biome.Plains, new List<NemesisSpawn>() {
                        new NemesisSpawn() {
                            Prefab = "GoblinBrute",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 6, MaxLevel = 11, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 2f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Unbjorn",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 6, MaxLevel = 11, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 2f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Deathsquito",
                            SelectionWeight = 0.1f,
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 6, MaxLevel = 11, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 10f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                    }},
                    { Heightmap.Biome.Mistlands, new List<NemesisSpawn>() {
                        new NemesisSpawn() {
                            Prefab = "SeekerBrute",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 7, MaxLevel = 12, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 2f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Gjall",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 7, MaxLevel = 14, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 2f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Dverger",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 7, MaxLevel = 14, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                    }},
                    { Heightmap.Biome.AshLands, new List<NemesisSpawn>() {
                        new NemesisSpawn() {
                            Prefab = "Charred_Melee",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 8, MaxLevel = 16, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "Charred_Mage",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 8, MaxLevel = 16, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "FallenValkyrie",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 8, MaxLevel = 16, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 2f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                    }},
                    { Heightmap.Biome.Ocean, new List<NemesisSpawn>() {
                        new NemesisSpawn() {
                            Prefab = "Serpent",
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 2, MaxLevel = 16, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                        new NemesisSpawn() {
                            Prefab = "BonemawSerpent",
                            SelectionWeight = 0.1f,
                            IsBoss = true,
                            Faction = Character.Faction.Boss,
                            CreatureAI = AI.Alerted,
                            LevelupGenerators = new List<LevelGenerator>() {
                                new LevelGenerator() { MinLevel = 2, MaxLevel = 16, LevelUpChance = 0.3f }
                            },
                            CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                                { CreatureBaseAttribute.BaseHealth, 4f },
                                { CreatureBaseAttribute.BaseDamage, 1.5f },
                            }
                        },
                    }},
                },
            },
            NemesisBossLootTables = new Dictionary<Heightmap.Biome, List<ExtendedCharacterDrop>>() {
                { Heightmap.Biome.Meadows, new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 50, Max = 150, Prefab = "Coins", Chance = 100f } },
                }},
                { Heightmap.Biome.BlackForest, new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 100, Max = 250, Prefab = "Coins", Chance = 100f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Amber", Chance = 25f } },
                }},
                { Heightmap.Biome.Swamp, new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 150, Max = 300, Prefab = "Coins", Chance = 100f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Ruby", Chance = 15f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Amber", Chance = 25f } },
                }},
                { Heightmap.Biome.Mountain, new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 200, Max = 350, Prefab = "Coins", Chance = 100f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Amber", Chance = 25f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Ruby", Chance = 15f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "AmberPearl", Chance = 10f } },
                }},
                { Heightmap.Biome.Plains, new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 250, Max = 400, Prefab = "Coins", Chance = 100f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 5, Max = 10, Prefab = "Amber", Chance = 50f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Ruby", Chance = 15f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "AmberPearl", Chance = 25f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 1, Prefab = "SilverNecklace", Chance = 5f } },
                }},
                { Heightmap.Biome.Mistlands, new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 300, Max = 500, Prefab = "Coins", Chance = 100f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 5, Max = 10, Prefab = "Amber", Chance = 50f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Ruby", Chance = 15f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "AmberPearl", Chance = 25f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 1, Prefab = "SilverNecklace", Chance = 10f } },
                }},
                { Heightmap.Biome.AshLands, new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 400, Max = 600, Prefab = "Coins", Chance = 100f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 5, Max = 10, Prefab = "Amber", Chance = 60f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Ruby", Chance = 25f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "AmberPearl", Chance = 35f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 1, Prefab = "SilverNecklace", Chance = 15f } },
                }},
                { Heightmap.Biome.Ocean, new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 500, Max = 800, Prefab = "Coins", Chance = 100f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 5, Max = 10, Prefab = "Amber", Chance = 75f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "Ruby", Chance = 50f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 3, Prefab = "AmberPearl", Chance = 30f } },
                    new ExtendedCharacterDrop() { Drop = new Drop() { DontScale = true, Min = 1, Max = 1, Prefab = "SilverNecklace", Chance = 25f } },
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
            return DataObjects.yamlSerializer.Serialize(DefaultConfiguration);
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
                SLE_Nemesis_Settings = DataObjects.yamlDeserializer.Deserialize<NemesisConfiguration>(yaml);
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
