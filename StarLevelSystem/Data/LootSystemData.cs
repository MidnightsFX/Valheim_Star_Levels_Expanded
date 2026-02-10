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
            characterSpecificLoot = new Dictionary<string, List<ExtendedCharacterDrop>>() {
                { "BlobElite", new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop{
                            Drop = new Drop
                            {
                                Prefab = "Ooze",
                                Min = 2,
                                Max = 3,
                            },
                            AmountScaleFactor = 0.5f,
                        },
                    new ExtendedCharacterDrop{
                            Drop = new Drop
                            {
                                Prefab = "IronScrap",
                                Min = 1,
                                Max = 1,
                                Chance = 0.33f
                            },
                            AmountScaleFactor = 0.2f,
                        },
                    new ExtendedCharacterDrop{
                            Drop = new Drop
                            {
                                Prefab = "TrophyBlob",
                                Min = 1,
                                Max = 1,
                                Chance = 0.1f
                            },
                            ChanceScaleFactor = 0.01f,
                            MaxScaledAmount = 1,
                        },
                    new ExtendedCharacterDrop{
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
                { "Tick", new List<ExtendedCharacterDrop>() {
                    new ExtendedCharacterDrop{
                            Drop = new Drop
                            {
                                Prefab = "GiantBloodSack",
                                Min = 1,
                                Max = 1,
                                Chance = 1f
                            },
                            AmountScaleFactor = 0.5f,
                        },
                    new ExtendedCharacterDrop{
                            Drop = new Drop
                            {
                                Prefab = "TrophyTick",
                                Min = 1,
                                Max = 1,
                                Chance = 0.05f
                            },
                            ChanceScaleFactor = 0.01f,
                            MaxScaledAmount = 1,
                        }
                    }
                }
            },
            nonCharacterSpecificLoot = new Dictionary<string, List<ExtendedObjectDrop>>() {
                {"caverock_ice_stalagtite_falling", new List<ExtendedObjectDrop>() {
                    new ExtendedObjectDrop() {
                        Drop = new Drop {
                            Prefab = "Crystal",
                            Min = 1,
                            Max = 3,
                            Chance = 0.3f,
                            DontScale = true
                        },
                        }
                    }
                },
                { "EvilHeart_Forest", new List<ExtendedObjectDrop>() {
                    new ExtendedObjectDrop() {
                        Drop = new Drop {
                            Prefab = "AncientSeed",
                            Min = 1,
                            Max = 1,
                            OnePerPlayer = true,
                        },
                        MaxScaledAmount = 3
                        }
                    }
                }
            },
            EnableDistanceLootModifier = true,
            DistanceLootModifier = new SortedDictionary<int, DistanceLootModifier>()
            {
                { 1250, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 0.02f,
                        MaxAmountScaleFactorBonus = 0.02f
                    }
                },
                { 2500, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 0.05f,
                        MaxAmountScaleFactorBonus = 0.05f
                    }
                },
                { 3750, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 0.08f,
                        MaxAmountScaleFactorBonus = 0.08f
                    }
                },
                { 5000, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 0.12f,
                        MaxAmountScaleFactorBonus = 0.12f
                    }
                },
                { 6250, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 0.15f,
                        MaxAmountScaleFactorBonus = 0.15f
                    }
                },
                { 7500, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 0.2f,
                        MaxAmountScaleFactorBonus = 0.2f
                    }
                },
                { 8750, new DistanceLootModifier() {
                        ChanceScaleFactorBonus = 0f,
                        MinAmountScaleFactorBonus = 0.35f,
                        MaxAmountScaleFactorBonus = 0.35f
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
            if (lootconfig == null) { return; }
            if (lootconfig.characterSpecificLoot != null) {
                foreach (KeyValuePair<string, List<ExtendedCharacterDrop>> dropset in lootconfig.characterSpecificLoot) {
                    foreach (ExtendedCharacterDrop itemdrop in dropset.Value) {
                        itemdrop.ToCharacterDrop();
                    }
                }
            }
            if (lootconfig.nonCharacterSpecificLoot != null) {
                foreach (KeyValuePair<string, List<ExtendedObjectDrop>> dropset in lootconfig.nonCharacterSpecificLoot) {
                    foreach (ExtendedObjectDrop itemdrop in dropset.Value) {
                        itemdrop.ResolveDropPrefab();
                    }
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
