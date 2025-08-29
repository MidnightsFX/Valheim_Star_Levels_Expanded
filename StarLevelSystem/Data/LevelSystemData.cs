using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.IO;

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
                { 15, 0.03125f },
                { 16, 0.015625f },
                { 17, 0.0078125f },
                { 18, 0.00390625f },
                { 19, 0.001953125f },
                { 20, 0.0009765625f }
            },
            BiomeConfiguration = new Dictionary<Heightmap.Biome, DataObjects.BiomeSpecificSetting>()
            {
                { Heightmap.Biome.All, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        BiomeMaxLevelOverride = 20,
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
                { Heightmap.Biome.AshLands, new DataObjects.BiomeSpecificSetting()
                    {
                        DistanceScaleModifier = 0.5f,
                    }
                },
                { Heightmap.Biome.DeepNorth, new DataObjects.BiomeSpecificSetting()
                    {
                        DistanceScaleModifier = 0.5f,
                    }
                }
            },

            CreatureConfiguration = new Dictionary<string, DataObjects.CreatureSpecificSetting>() {
                { "Lox", new DataObjects.CreatureSpecificSetting()
                    {
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.SpeedPerLevel, 0.9f },
                        }
                    }
                },
                { "Troll", new DataObjects.CreatureSpecificSetting()
                    {
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.SizePerLevel, 0.05f },
                        }
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
        public static bool UpdateYamlConfig(string yaml)
        {
            try
            {
                SLE_Level_Settings = DataObjects.yamldeserializer.Deserialize<DataObjects.CreatureLevelSettings>(yaml);
                Logger.LogDebug("Loaded new Star Level Creature settings.");
            }
            catch (Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse CreatureLevelSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}
