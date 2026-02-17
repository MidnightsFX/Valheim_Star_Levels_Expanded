using Jotunn;
using Jotunn.Managers;
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

        public static readonly DataObjects.CreatureLevelSettings DefaultConfiguration = new DataObjects.CreatureLevelSettings()
        {
            DefaultCreatureLevelUpChance = new SortedDictionary<int, float>() {
                { 1, 20f },
                { 2, 10f },
                { 3, 5f },
                { 4, 2f },
                { 5, 1f },
                { 6, 0.5f },
                { 7, 0.25f },
                { 8, 0.125f },
                { 9, 0.0625f },
                { 10, 0.0312f },
                { 11, 0.0156f },
                { 12, 0.0078f },
                { 13, 0.0039f },
                { 14, 0.0019f },
                { 15, 0.0015f },
                { 16, 0.0010f },
                { 17, 0.0009f },
                { 18, 0.0008f },
                { 19, 0.0007f },
                { 20, 0.0006f },
                { 21, 0.0005f },
                { 22, 0.0004f },
                { 23, 0.0003f },
                { 24, 0.0002f },
                { 25, 0.0001f },
            },
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
                { "Lox", new DataObjects.CreatureSpecificSetting()
                    {
                        CreaturePerLevelValueModifiers = new Dictionary<DataObjects.CreaturePerLevelAttribute, float>() {
                            { DataObjects.CreaturePerLevelAttribute.SpeedPerLevel, -0.01f },
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
            }
        };  


        internal static void Init() {
            // Load the default configuration
            SLE_Level_Settings = DefaultConfiguration;
            Colorization.UpdateMapColorSelection();
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
                LevelSystem.DelayedMinimapSetup();
                foreach (var chara in Resources.FindObjectsOfTypeAll<Character>()) {
                    if (chara.m_level <= 1) { continue; }

                    CharacterCacheEntry ccd = CompositeLazyCache.GetCacheEntry(chara);
                    if (ccd == null) { continue; }
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
                CharacterCacheEntry ccd = CompositeLazyCache.GetCacheEntry(character);
                if (ccd == null) { continue; }
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
