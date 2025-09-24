using StarLevelSystem.common;
using StarLevelSystem.modules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class LevelSystemData
    {

        public static DataObjects.CreatureLevelSettings SLE_Level_Settings = DefaultConfiguration;

        public static DataObjects.CreatureLevelSettings DefaultConfiguration = new DataObjects.CreatureLevelSettings()
        {
            DefaultCreatureLevelUpChance = new SortedDictionary<int, float>() {
                { 1, 20f },
                { 2, 15f },
                { 3, 12f },
                { 4, 10f },
                { 5, 8.0f },
                { 6, 6.5f },
                { 7, 5.0f },
                { 8, 3.5f },
                { 9, 1.5f },
                { 10, 1.0f },
                { 11, 0.5f },
                { 12, 0.25f },
                { 13, 0.125f },
                { 14, 0.0625f },
                { 15, 0.0312f },
                { 16, 0.0156f },
                { 17, 0.0078f },
                { 18, 0.0039f },
                { 19, 0.0019f },
                { 20, 0.0019f },
                { 21, 0.0019f },
                { 22, 0.0019f },
                { 23, 0.0019f },
                { 24, 0.0019f },
                { 25, 0.0019f },
                { 26, 0.0019f },
                { 27, 0.0019f },
                { 28, 0.0019f },
                { 29, 0.0019f },
                { 30, 0.0019f },
            },
            BiomeConfiguration = new Dictionary<Heightmap.Biome, DataObjects.BiomeSpecificSetting>()
            {
                { Heightmap.Biome.All, new DataObjects.BiomeSpecificSetting()
                    {
                        SpawnRateModifier = 1.5f,
                        DistanceScaleModifier = 1.5f,
                        DamageRecievedModifiers = new Dictionary<DataObjects.DamageType, float>() {
                            {DataObjects.DamageType.Poison, 1.5f } 
                        },
                        CreatureBaseValueModifiers = new Dictionary<DataObjects.CreatureBaseAttribute, float>() {
                            { DataObjects.CreatureBaseAttribute.BaseHealth, 1f },
                            { DataObjects.CreatureBaseAttribute.BaseDamage, 1f },
                            { DataObjects.CreatureBaseAttribute.Speed, 1f },
                            { DataObjects.CreatureBaseAttribute.Size, 1f }
                        },
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.4f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.1f },
                            { DataObjects.CreaturePerLevelAttribute.SpeedPerLevel, 0f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.10f }
                        },
                        
                    }
                },
                { Heightmap.Biome.Meadows, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = 4,
                    }
                },
                { Heightmap.Biome.BlackForest, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = 6,
                    }
                },
                { Heightmap.Biome.Swamp, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = 10,
                    }
                },
                { Heightmap.Biome.Mountain, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = 14,
                    }
                },
                { Heightmap.Biome.Plains, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = 18,
                    }
                },
                { Heightmap.Biome.Mistlands, new DataObjects.BiomeSpecificSetting()
                    {
                        BiomeMaxLevelOverride = 22,
                    }
                },
                { Heightmap.Biome.AshLands, new DataObjects.BiomeSpecificSetting()
                    {
                        DistanceScaleModifier = 0.5f,
                        BiomeMaxLevelOverride = 26,
                    }
                },
                { Heightmap.Biome.DeepNorth, new DataObjects.BiomeSpecificSetting()
                    {
                        DistanceScaleModifier = 0.5f,
                        BiomeMaxLevelOverride = 26,
                    }
                }
            },

            CreatureConfiguration = new Dictionary<string, DataObjects.CreatureSpecificSetting>() {
                { "piece_TrainingDummy", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 11,
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.005f }
                        },
                    }
                },
                { "Lox", new DataObjects.CreatureSpecificSetting()
                    {
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.SpeedPerLevel, 0.9f },
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
                { "Eikthyr", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 4,
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.07f }
                        },
                    }
                },
                { "gd_king", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 6,
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.07f }
                        },
                    }
                },
                { "Bonemass", new DataObjects.CreatureSpecificSetting()
                    {
                    CreatureMaxLevelOverride = 8,
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.07f }
                        },
                    }
                },
                { "Dragon", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 10,
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.07f }
                        },
                    }
                },
                { "GoblinKing", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 12,
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.07f }
                        },
                    }
                },
                { "SeekerQueen", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 14,
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.07f }
                        },
                    }
                },
                { "Fader", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 16,
                        SpawnRateModifier = 1f,
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.HealthPerLevel, 0.3f },
                            { DataObjects.CreaturePerLevelAttribute.DamagePerLevel, 0.05f },
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.05f }
                        },
                    }
                }
            },

            EnableDistanceLevelBonus = true,
            DistanceLevelBonus = new SortedDictionary<int, SortedDictionary<int, float>>()
            {
                { 1250, new SortedDictionary<int, float>() {
                        { 1, 0.25f },
                    }
                },
                { 2500, new SortedDictionary<int, float>() {
                        { 1, 0.5f },
                        { 2, 0.25f },
                    }
                },
                { 3750, new SortedDictionary<int, float>() {
                        { 1, 1f },
                        { 2, 0.75f },
                        { 3, 0.5f },
                        { 4, 0.25f },
                    }
                },
                { 5000, new SortedDictionary<int, float>() {
                        { 1, 1f },
                        { 2, 1f },
                        { 3, 0.75f },
                        { 4, 0.5f },
                        { 5, 0.25f },
                        { 6, 0.15f },
                    }
                },
                { 6250, new SortedDictionary<int, float>() {
                        { 1, 1f },
                        { 2, 1f },
                        { 3, 1f },
                        { 4, 0.75f },
                        { 5, 0.5f },
                        { 6, 0.25f },
                        { 7, 0.20f },
                        { 8, 0.15f },
                    }
                },
                { 7500, new SortedDictionary<int, float>() {
                        { 1, 1f },
                        { 2, 1f },
                        { 3, 1f },
                        { 4, 1f },
                        { 5, 0.75f },
                        { 6, 0.5f },
                        { 7, 0.25f },
                        { 8, 0.20f },
                        { 9, 0.15f },
                    }
                },
                { 8750, new SortedDictionary<int, float>() {
                        { 1, 1f },
                        { 2, 1f },
                        { 3, 1f },
                        { 4, 1f },
                        { 5, 1f },
                        { 6, 0.75f },
                        { 7, 0.5f },
                        { 8, 0.25f },
                        { 9, 0.20f },
                        { 10, 0.15f },
                    }
                }
            }
        };  


        internal static void Init() {
            // Load the default configuration
            SLE_Level_Settings = DefaultConfiguration;
            try {
                UpdateYamlConfig(File.ReadAllText(ValConfig.levelsFilePath));
            }
            catch (Exception e) { Jotunn.Logger.LogWarning($"There was an error updating the Creature Level values, defaults will be used. Exception: {e}"); }
        }
        public static string YamlDefaultConfig() {
            var yaml = DataObjects.yamlserializer.Serialize(DefaultConfiguration);
            return yaml;
        }
        public static bool UpdateYamlConfig(string yaml) {
            try {
                SLE_Level_Settings = DataObjects.yamldeserializer.Deserialize<DataObjects.CreatureLevelSettings>(yaml);
                Logger.LogDebug("Loaded new Star Level Creature settings, updating loaded creatures...");
                foreach (var chara in Resources.FindObjectsOfTypeAll<Character>()) {
                    if (chara.m_level <= 1) { continue; }
                    CreatureDetailCache ccd = CompositeLazyCache.GetAndSetDetailCache(chara, true);
                    // Modify the creatures stats by custom character/biome modifications
                    ModificationExtensionSystem.ApplySpeedModifications(chara, ccd);
                    ModificationExtensionSystem.ApplyDamageModification(chara, ccd, true);
                    ModificationExtensionSystem.LoadApplySizeModifications(chara.gameObject, chara.m_nview, ccd, true);
                    ModificationExtensionSystem.ApplyHealthModifications(chara, ccd);
                    //Colorization.ApplyColorizationWithoutLevelEffects(chara.gameObject, ccd.Colorization);
                    //Colorization.ApplyLevelVisual(chara);
                }
            }
            catch (Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse CreatureLevelSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }

        internal static IEnumerator UpdateCreatureAttributes(List<Character> characters) {
            int i = 0;
            WaitForSeconds sleep = new WaitForSeconds(0.1f);
            foreach (var character in characters) {
                if (i >= ValConfig.NumberOfCacheUpdatesPerFrame.Value) {
                    yield return sleep;
                    i = 0;
                }
                if (character.m_level <= 1) { continue; }
                CreatureDetailCache ccd = CompositeLazyCache.GetAndSetDetailCache(character, true);
                // Modify the creatures stats by custom character/biome modifications
                ModificationExtensionSystem.ApplySpeedModifications(character, ccd);
                ModificationExtensionSystem.ApplyDamageModification(character, ccd, true);
                ModificationExtensionSystem.LoadApplySizeModifications(character.gameObject, character.m_nview, ccd, true);
                ModificationExtensionSystem.ApplyHealthModifications(character, ccd);

                i++;
            }
        }
    }
}
