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
                { "BlobElite", new List<ExtendedDrop>() {
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "Ooze",
                                Min = 2,
                                Max = 3,
                            },
                            AmountScaleFactor = 0.5f,
                        },
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "IronScrap",
                                Min = 1,
                                Max = 1,
                                Chance = 0.33f
                            },
                            AmountScaleFactor = 0.2f,
                        },
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "TrophyBlob",
                                Min = 1,
                                Max = 1,
                                Chance = 0.1f
                            },
                            ChanceScaleFactor = 1.01f,
                            MaxScaledAmount = 1,
                        },
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "Blob",
                                Min = 2,
                                Max = 2,
                            },
                            AmountScaleFactor = 0.5f,
                            MaxScaledAmount = 6,
                        }
                    }
                },
                { "Tick", new List<ExtendedDrop>() {
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "GiantBloodSack",
                                Min = 1,
                                Max = 1,
                                Chance = 1f
                            },
                            AmountScaleFactor = 0.5f,
                        },
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "TrophyTick",
                                Min = 1,
                                Max = 1,
                                Chance = 0.05f
                            },
                            ChanceScaleFactor = 1.01f,
                            MaxScaledAmount = 1,
                        }
                    }
                },
                { "Greyling", new List<ExtendedDrop>() {
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "Resin",
                                Min = 1,
                                Max = 1,
                                Chance = 0.75f
                            },
                            AmountScaleFactor = 0.5f,
                        }
                    }
                },
                { "Greydwarf", new List<ExtendedDrop>() {
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "Stone",
                                Min = 1,
                                Max = 1,
                                Chance = 1f
                            },
                            AmountScaleFactor = 0.5f,
                        },
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "Wood",
                                Min = 1,
                                Max = 1,
                                Chance = 1f
                            },
                            AmountScaleFactor = 0.5f,
                        },
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "Resin",
                                Min = 1,
                                Max = 1,
                                Chance = 0.50f
                            },
                            AmountScaleFactor = 0.5f,
                        },
                    new ExtendedDrop{
                            Drop = new Drop
                            {
                                Prefab = "GreydwarfEye",
                                Min = 1,
                                Max = 1,
                                Chance = 0.50f
                            },
                            AmountScaleFactor = 0.5f,
                        }
                    }

                }
            },
            EnableDistanceLootModifier = true,
            DistanceLootModifier = new SortedDictionary<int, DistanceLootModifier>()
            {
                { 1250, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 1.02f,
                        MaxAmountScaleFactorBonus = 1.02f
                    }
                },
                { 2500, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 1.05f,
                        MaxAmountScaleFactorBonus = 1.05f
                    }
                },
                { 3750, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 1.08f,
                        MaxAmountScaleFactorBonus = 1.08f
                    }
                },
                { 5000, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 1.12f,
                        MaxAmountScaleFactorBonus = 1.12f
                    }
                },
                { 6250, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 1.15f,
                        MaxAmountScaleFactorBonus = 1.15f
                    }
                },
                { 7500, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 1.2f,
                        MaxAmountScaleFactorBonus = 1.2f
                    }
                },
                { 8750, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 1.35f,
                        MaxAmountScaleFactorBonus = 1.35f
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
