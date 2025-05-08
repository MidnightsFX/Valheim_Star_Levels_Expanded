using Jotunn.Managers;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.IO;

namespace StarLevelSystem.modules
{
    public static class LevelSystemData
    {

        public static DataObjects.CreatureLevelSettings SLE_Global_Settings = DefaultConfiguration;

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
                { Heightmap.Biome.None, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 1,
                        CreatureSpawnDamagePerLevelBonus = 1,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 20,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.Meadows, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 1,
                        CreatureSpawnDamagePerLevelBonus = 1,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 2,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.BlackForest, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 1,
                        CreatureSpawnDamagePerLevelBonus = 1,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 3,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.Swamp, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 0.85f,
                        CreatureSpawnDamagePerLevelBonus = 0.85f,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 4,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.Mountain, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 0.85f,
                        CreatureSpawnDamagePerLevelBonus = 0.85f,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 4,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.Plains, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 0.85f,
                        CreatureSpawnDamagePerLevelBonus = 0.85f,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 5,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.Mistlands, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 0.80f,
                        CreatureSpawnDamagePerLevelBonus = 0.80f,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 6,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.AshLands, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 0.80f,
                        CreatureSpawnDamagePerLevelBonus = 0.80f,
                        CreatureLootMultiplierPerLevel = 1f,
                        DistanceScaleModifier = 0.5f,
                        BiomeMaxLevelOverride = 7,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.DeepNorth, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 0.85f,
                        CreatureSpawnDamagePerLevelBonus = 0.85f,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 8,
                        DistanceScaleModifier = 0.5f,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                },
                { Heightmap.Biome.Ocean, new DataObjects.BiomeSpecificSetting()
                    {
                        EnableBiomeLevelOverride = true,
                        CreatureSpawnHealthPerLevelBonus = 0.85f,
                        CreatureSpawnDamagePerLevelBonus = 0.85f,
                        CreatureLootMultiplierPerLevel = 1f,
                        BiomeMaxLevelOverride = 8,
                        CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
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
                    }
                }
            },

            CreatureConfiguration = new Dictionary<string, DataObjects.CreatureSpecificSetting>() { },

            EnableDistanceLevelBonus = true,
            DistanceLevelBonus = new SortedDictionary<int, SortedDictionary<int, float>>()
            {
                { 1250, new SortedDictionary<int, float>() {
                        { 1, 0.15f },
                    }
                },
                { 2500, new SortedDictionary<int, float>() {
                        { 1, 0.15f },
                        { 2, 0.10f },
                    }
                },
                { 3750, new SortedDictionary<int, float>() {
                        { 1, 0.15f },
                        { 2, 0.10f },
                        { 3, 0.05f },
                    }
                },
                { 5000, new SortedDictionary<int, float>() {
                        { 1, 0.20f },
                        { 2, 0.15f },
                        { 3, 0.10f },
                        { 4, 0.05f },
                    }
                },
                { 6250, new SortedDictionary<int, float>() {
                        { 1, 0.25f },
                        { 2, 0.20f },
                        { 3, 0.15f },
                        { 4, 0.10f },
                        { 5, 0.05f },
                    }
                },
                { 7500, new SortedDictionary<int, float>() {
                        { 1, 0.35f },
                        { 2, 0.30f },
                        { 3, 0.25f },
                        { 4, 0.20f },
                        { 5, 0.15f },
                        { 6, 0.10f },
                        { 7, 0.05f },
                    }
                },
                { 8750, new SortedDictionary<int, float>() {
                        { 1, 0.45f },
                        { 2, 0.40f },
                        { 3, 0.35f },
                        { 4, 0.30f },
                        { 5, 0.25f },
                        { 6, 0.20f },
                        { 7, 0.15f },
                        { 8, 0.10f },
                        { 9, 0.05f },
                    }
                }
            }
        };  


        internal static void Init() {
            // Load the default configuration
            SLE_Global_Settings = DefaultConfiguration;
            try {
                UpdateYamlConfig(File.ReadAllText(ValConfig.levelsFilePath));
            }
            catch (Exception e) { Jotunn.Logger.LogWarning($"There was an error updating the Color Level values, defaults will be used. Exception: {e}"); }
        }
        public static string YamlDefaultConfig() {
            var yaml = DataObjects.yamlserializer.Serialize(DefaultConfiguration);
            return yaml;
        }
        public static bool UpdateYamlConfig(string yaml)
        {
            try
            {
                SLE_Global_Settings = DataObjects.yamldeserializer.Deserialize<DataObjects.CreatureLevelSettings>(yaml);
                Logger.LogDebug("Loaded new Star Level Creature settings.");
            }
            catch (System.Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse CreatureLevelSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}
