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
                { 20, 0.0015f },
                { 21, 0.0010f },
                { 22, 0.0009f },
                { 23, 0.0008f },
                { 24, 0.0007f },
                { 25, 0.0006f },
                { 26, 0.0005f },
                { 27, 0.0004f },
                { 28, 0.0003f },
                { 29, 0.0002f },
                { 30, 0.0001f },
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
                { "Eikthyr", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 4,
                    }
                },
                { "gd_king", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 6,
                    }
                },
                { "Bonemass", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 8,
                    }
                },
                { "Dragon", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 10,
                    }
                },
                { "GoblinKing", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 12,
                    }
                },
                { "SeekerQueen", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 14,
                    }
                },
                { "Fader", new DataObjects.CreatureSpecificSetting()
                    {
                        CreatureMaxLevelOverride = 16,
                    }
                }
            },

            EnableDistanceLevelBonus = true,
            DistanceLevelBonus = new SortedDictionary<int, SortedDictionary<int, float>>()
            {
                { 750, new SortedDictionary<int, float>() {
                        { 1, 25f },
                    }
                },
                { 1200, new SortedDictionary<int, float>() {
                        { 1, 50f },
                        { 2, 25f },
                    }
                },
                { 2000, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 75f },
                        { 3, 50f },
                        { 4, 25f },
                    }
                },
                { 3000, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 100f },
                        { 3, 75f },
                        { 4, 50f },
                        { 5, 25f },
                        { 6, 15f },
                    }
                },
                { 4000, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 100f },
                        { 3, 100f },
                        { 4, 75f },
                        { 5, 50f },
                        { 6, 25f },
                        { 7, 20f },
                        { 8, 15f },
                    }
                },
                { 5000, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 100f },
                        { 3, 100f },
                        { 4, 100f },
                        { 5, 75f },
                        { 6, 50f },
                        { 7, 25f },
                        { 8, 20f },
                        { 9, 15f },
                    }
                },
                { 6000, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 100f },
                        { 3, 100f },
                        { 4, 100f },
                        { 5, 100f },
                        { 6, 75f },
                        { 7, 50f },
                        { 8, 25f },
                        { 9, 20f },
                        { 10, 15f },
                    }
                },
                { 7000, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 100f },
                        { 3, 100f },
                        { 4, 100f },
                        { 5, 100f },
                        { 6, 100f },
                        { 7, 75f },
                        { 8, 50f },
                        { 9, 25f },
                        { 10, 20f },
                        { 11, 15f },
                        { 12, 10f },
                        { 13, 5f },
                        { 14, 2.5f },
                    }
                },
                { 8000, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 100f },
                        { 3, 100f },
                        { 4, 100f },
                        { 5, 100f },
                        { 6, 100f },
                        { 7, 100f },
                        { 8, 75f },
                        { 9, 50f },
                        { 10, 25f },
                        { 11, 20f },
                        { 12, 15f },
                        { 13, 10f },
                        { 14, 5f },
                        { 15, 2.5f },
                    }
                },
                { 9100, new SortedDictionary<int, float>() {
                        { 1, 100f },
                        { 2, 100f },
                        { 3, 100f },
                        { 4, 100f },
                        { 5, 100f },
                        { 6, 100f },
                        { 7, 100f },
                        { 8, 100f },
                        { 9, 75f },
                        { 10, 50f },
                        { 11, 25f },
                        { 12, 20f },
                        { 13, 15f },
                        { 14, 10f },
                        { 15, 5f },
                        { 16, 2.5f },
                        { 17, 1.2f },
                        { 18, 0.6f },
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
                LevelSystem.CreateLevelBonusRingMapOverlays();
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
