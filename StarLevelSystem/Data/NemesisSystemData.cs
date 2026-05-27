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
            NemesisActionCooldownSeconds = 10,
            NemesisInfluenceRadius = 300f,
            ScoreSystem = new NemesisScoreSystem() {
                MaxScore = 1000f,
                NeutralScore = 500f,
                MinScore = 0f,
                ScoreIntervalSeconds = 30,
                DecayPerUpdate = 35f,

                NearbyAveragingWeight = 0.05f,
                NearbyPlayerRadius = 60f,

                BossKillBonus = 100f,
                BossKillRadius = 100f,
                
                MeleeDamageDealtFactor = 0.75f,
                RangedDamageDealtFactor = 0.25f,
                MagicDamageDealtFactor = 0.5f,
                DamageTakenFactor = -0.5f,
                DeathScoreReduction = 300f,
            },
            GaurenteedChanges = new NemesisGaurenteedChanges() {
                FirstBossSetLevel = true,
                FirstBossLevel = 1,
            },
            ChanceChanges = new NemesisChanceChanges() { 
                CreatureOps = new Dictionary<string, NemesisChanceEntry>() {
                    { "CharredAttack", new NemesisChanceEntry() { Enabled = true, Chance = 0.3f, RequiredGlobalKey = "defeated_fader", ScoreThreshold = 900f, Action = NemesisAction.Spawn, LevelBonus = 5, ScoreChange = -200,SpawnConfig = new List<NemesisSpawn>(){
                        new NemesisSpawn() { Prefab = "Charred_Melee", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Boss, SpawnGroupSize = 1, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Fire", ModifierType.Major } } },
                        new NemesisSpawn() { Prefab = "Charred_Ranged", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Boss, SpawnGroupSize = 2, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Fire", ModifierType.Major } } }
                    }}},
                    { "SeekerAttack", new NemesisChanceEntry() { Enabled = true, Chance = 0.3f, RequiredGlobalKey = "defeated_queen", ScoreThreshold = 900f, Action = NemesisAction.Spawn, LevelBonus = 5, ScoreChange = -200,SpawnConfig = new List<NemesisSpawn>(){
                        new NemesisSpawn() { Prefab = "Seeker", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Boss, SpawnGroupSize = 3, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Lightning", ModifierType.Major } } },
                    }}},
                    { "GoblinAttack", new NemesisChanceEntry() { Enabled = true, Chance = 0.3f, RequiredGlobalKey = "defeated_goblinking", ScoreThreshold = 900f, Action = NemesisAction.Spawn, LevelBonus = 5, ScoreChange = -200,SpawnConfig = new List<NemesisSpawn>(){
                        new NemesisSpawn() { Prefab = "Goblin", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Boss, SpawnGroupSize = 3, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Fire", ModifierType.Major } } },
                        new NemesisSpawn() { Prefab = "GoblinShaman", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Boss, SpawnGroupSize = 1, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Lightning", ModifierType.Major } } },
                    }}},
                    { "FenringAttack", new NemesisChanceEntry() { Enabled = true, Chance = 0.3f, RequiredGlobalKey = "defeated_dragon", ScoreThreshold = 900f, Action = NemesisAction.Spawn, LevelBonus = 5, ScoreChange = -200,SpawnConfig = new List<NemesisSpawn>(){
                        new NemesisSpawn() { Prefab = "Fenring", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Boss, SpawnGroupSize = 2, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Big", ModifierType.Major } } },
                    }}},
                    { "SwampAttack", new NemesisChanceEntry() { Enabled = true, Chance = 0.3f, RequiredGlobalKey = "defeated_bonemass", ScoreThreshold = 900f, Action = NemesisAction.Spawn, LevelBonus = 5, ScoreChange = -200,SpawnConfig = new List<NemesisSpawn>(){
                        new NemesisSpawn() { Prefab = "Draugr_Elite", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Boss, SpawnGroupSize = 2, RequiredModifiers = new Dictionary<string, ModifierType>() { { "Poison", ModifierType.Major } } },
                    }}},
                    { "BlackForestSwarm", new NemesisChanceEntry() { Enabled = true, Chance = 0.3f, RequiredGlobalKey = "defeated_gdking", ScoreThreshold = 900f, Action = NemesisAction.Spawn, LevelBonus = 5, ScoreChange = -200,SpawnConfig = new List<NemesisSpawn>(){
                        new NemesisSpawn() { Prefab = "Greydwarf", CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Boss, SpawnGroupSize = 6, RequiredModifiers = new Dictionary<string, ModifierType>() { { "FireNova", ModifierType.Minor } } },
                    }}},
                    // Reduce Creature levels to make things easier
                    { "ReduceCreatureLevel", new NemesisChanceEntry() { Enabled = true, Chance = 0.25f, ScoreThreshold = 400f, Action = NemesisAction.ChangeLevel, LevelBonus = -1, ScoreChange = 20f, } },
                    { "SignificantlyReduceCreatureLevel", new NemesisChanceEntry() { Enabled = true, Chance = 0.5f, ScoreThreshold = 200f, Action = NemesisAction.ChangeLevel, LevelBonus = -3, ScoreChange = 40f, } },
                    // Increase Creature levels to make things harder
                    { "IncreaseCreatureLevel", new NemesisChanceEntry() { Enabled = true, Chance = 0.25f, ScoreThreshold = 600f, Action = NemesisAction.ChangeLevel, LevelBonus = 1, ScoreChange = -25f, } },
                    { "SignificantlyIncreaseCreatureLevel", new NemesisChanceEntry() { Enabled = true, Chance = 0.5f, ScoreThreshold = 800f, Action = NemesisAction.ChangeLevel, LevelBonus = 3, ScoreChange = -50f, } },

                    { "NemesisMiniboss1", new NemesisChanceEntry() { Enabled = true, Chance = 0.05f, ScoreThreshold = 950f, Action = NemesisAction.SpawnMiniboss, LevelBonus = 7, ScoreChange = -300f } },
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
            Logger.LogDebug($"Updating Nemesis Action Log: {ValConfig.nemesisLogFilePath}");
            ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
            File.AppendAllText(ValConfig.nemesisLogFilePath, data);
        }

        public static bool UpdateYamlConfig(string yaml) {
            try {
                Logger.LogDebug("Loaded new Nemesis settings...");
                SLE_Nemesis_Settings = DataObjects.yamldeserializer.Deserialize<NemesisConfiguration>(yaml);
            }
            catch (Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse NemesisSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}
