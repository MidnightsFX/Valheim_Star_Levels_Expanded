using Jotunn.Managers;
using StarLevelSystem.common;
using System.Collections.Generic;

namespace StarLevelSystem.modules
{
    public static class LevelSystemConfiguration
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
                { Heightmap.Biome.Mountain, new DataObjects.BiomeSpecificSetting()
                    {
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
                        CreatureSpawnHealthPerLevelBonus = 0.80f,
                        CreatureSpawnDamagePerLevelBonus = 0.80f,
                        CreatureLootMultiplierPerLevel = 1f,
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
                },
                { Heightmap.Biome.Ocean, new DataObjects.BiomeSpecificSetting()
                    {
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

            CreatureConfiguration = new Dictionary<string, DataObjects.CreatureSpecificSetting>() { }
        };


        internal static void Init() {
            // Load the default configuration
            SLE_Global_Settings = DefaultConfiguration;
        }
        public static string YamlDefaultConfig() {
            var yaml = DataObjects.yamlserializer.Serialize(DefaultConfiguration);
            return yaml;
        }
        public static void UpdateYamlConfig(DataObjects.CreatureLevelSettings newcfg) {
            SLE_Global_Settings = newcfg;
        }
        public static bool UpdateYamlConfig(string yaml)
        {
            try {
                SLE_Global_Settings = DataObjects.yamldeserializer.Deserialize<DataObjects.CreatureLevelSettings>(yaml);
            } catch (System.Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse YAML config: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}
