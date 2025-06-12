using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.IO;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class LootSystemData
    {
        public static LootSettings SLS_Drop_Settings { get; set; }
        public static LootSettings DefaultDropConfiguration = new LootSettings()
        {
            characterSpecificLoot = new Dictionary<string, List<ExtendedDrop>>() {
                { "Tick", new List<ExtendedDrop>() {
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                prefab = "GiantBloodSack",
                                min = 1,
                                max = 1,
                                chance = 1f
                            },
                            amountScaleFactor = 0.5f,
                        },
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                prefab = "TrophyTick",
                                min = 1,
                                max = 1,
                                chance = 0.05f
                            },
                            chanceScaleFactor = 1.01f,
                            maxScaledAmount = 1,
                        }
                    }
                }
            },
            EnableDistanceLootModifier = true,
            DistanceLootModifier = new SortedDictionary<int, DistanceLootModifier>()
            {
                { 1250, new DistanceLootModifier() {
                        chanceScaleFactorBonus = 0f,
                        minAmountScaleFactorBonus = 1.02f,
                        maxAmountScaleFactorBonus = 1.02f
                    }
                },
                { 2500, new DistanceLootModifier() {
                        chanceScaleFactorBonus = 0f,
                        minAmountScaleFactorBonus = 1.05f,
                        maxAmountScaleFactorBonus = 1.05f
                    }
                },
                { 3750, new DistanceLootModifier() {
                        chanceScaleFactorBonus = 0f,
                        minAmountScaleFactorBonus = 1.08f,
                        maxAmountScaleFactorBonus = 1.08f
                    }
                },
                { 5000, new DistanceLootModifier() {
                        chanceScaleFactorBonus = 0f,
                        minAmountScaleFactorBonus = 1.12f,
                        maxAmountScaleFactorBonus = 1.12f
                    }
                },
                { 6250, new DistanceLootModifier() {
                        chanceScaleFactorBonus = 0f,
                        minAmountScaleFactorBonus = 1.15f,
                        maxAmountScaleFactorBonus = 1.15f
                    }
                },
                { 7500, new DistanceLootModifier() {
                        chanceScaleFactorBonus = 0f,
                        minAmountScaleFactorBonus = 1.2f,
                        maxAmountScaleFactorBonus = 1.2f
                    }
                },
                { 8750, new DistanceLootModifier() {
                        chanceScaleFactorBonus = 0f,
                        minAmountScaleFactorBonus = 1.35f,
                        maxAmountScaleFactorBonus = 1.35f
                    }
                }
            }
        };

        internal static void Init()
        {
            // Load the default configuration
            SLS_Drop_Settings = DefaultDropConfiguration;
            try
            {
                UpdateYamlConfig(File.ReadAllText(ValConfig.creatureLootFilePath));
            }
            catch (Exception e) {
                SLS_Drop_Settings = DefaultDropConfiguration;
                Jotunn.Logger.LogWarning($"There was an error updating the Loot Level values, defaults will be used. Exception: {e}"); 
            }
        }
        public static string YamlDefaultConfig()
        {
            var yaml = DataObjects.yamlserializer.Serialize(DefaultDropConfiguration);
            return yaml;
        }

        internal static void AttachPrefabsWhenReady() {
            AttachLootPrefabs(SLS_Drop_Settings);
        }

        public static void AttachLootPrefabs(LootSettings lootconfig) {
            if (lootconfig.characterSpecificLoot == null || lootconfig.characterSpecificLoot.Count == 0) { return; }
            foreach (var dropset in lootconfig.characterSpecificLoot) {
                foreach (var itemdrop in dropset.Value) {
                    itemdrop.SetupDrop();
                }
            }
        }

        public static bool UpdateYamlConfig(string yaml)
        {
            try {
                SLS_Drop_Settings = DataObjects.yamldeserializer.Deserialize<LootSettings>(yaml);
                // Resolve all of the prefab references
                AttachLootPrefabs(SLS_Drop_Settings);
                Logger.LogDebug("Loaded new Creature loot configuration.");
            }
            catch (Exception ex)
            {
                StarLevelSystem.Log.LogError($"Failed to parse LootLevelSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}
