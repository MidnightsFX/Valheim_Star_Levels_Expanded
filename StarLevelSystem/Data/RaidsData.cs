using StarLevelSystem.common;
using StarLevelSystem.modules.Raids;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class RaidsData
    {
        public static RaidConfiguration SLE_Raid_Settings = DefaultConfiguration;

        public static readonly RaidConfiguration DefaultConfiguration = new RaidConfiguration()
        {
            GlobalSettings = new GlobalRaidSettings()
            {
                DisableAllRaids = false,
                GlobalRaidIntervalScalar = 1f,
                GlobalRaidChanceScalar = 1f,
            },
            Raids = new Dictionary<string, RaidDefinition>()
            {
                { "army_eikthyr", new RaidDefinition() {
                    Duration = 60f,
                    StartMessage = "$event_eikthyr_start",
                    EndMessage = "$event_eikthyr_over",
                    ForceMusic = "RaidMeadows",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Meadows },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_eikthyr" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Greyling", MaxSpawned = 4, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Neck",    MaxSpawned = 2, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.Alerted },
                        new RaidSpawnEntry() { PrefabName = "Boar",    MaxSpawned = 2, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.Alerted },
                    },
                }},
                { "foresttrolls", new RaidDefinition() {
                    Duration = 60f,
                    StartMessage = "$event_forest_start",
                    EndMessage = "$event_forest_over",
                    ForceMusic = "RaidBlackForest",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.BlackForest },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_eikthyr" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Troll", MaxSpawned = 2, SpawnInterval = 30f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.AgitatedByBuild },
                    },
                }},
                { "army_theelder", new RaidDefinition() {
                    Duration = 90f,
                    StartMessage = "$event_theelder_start",
                    EndMessage = "$event_theelder_over",
                    ForceMusic = "RaidBlackForest",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Greydwarf",        MaxSpawned = 6, SpawnInterval = 3f, SpawnChance = 100f, LevelMin = 1, LevelMax = 3, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Greydwarf_Elite",  MaxSpawned = 2, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.AgitatedByBuild },
                        new RaidSpawnEntry() { PrefabName = "Greydwarf_Shaman", MaxSpawned = 2, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.Alerted },
                    },
                }},
                { "skeletons", new RaidDefinition() {
                    Duration = 60f,
                    StartMessage = "$event_skeletons_start",
                    EndMessage = "$event_skeletons_over",
                    ForceMusic = "RaidBlackForest",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Skeleton",        MaxSpawned = 6, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.Alerted },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Poison", MaxSpawned = 2, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.Alerted },
                    },
                }},
                { "blobs", new RaidDefinition() {
                    Duration = 60f,
                    StartMessage = "$event_blobs_start",
                    EndMessage = "$event_blobs_over",
                    ForceMusic = "RaidSwamp",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Swamp },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Blob",      MaxSpawned = 4, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "BlobElite", MaxSpawned = 2, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { "surtlings", new RaidDefinition() {
                    Duration = 60f,
                    StartMessage = "$event_surtlings_start",
                    EndMessage = "$event_surtlings_over",
                    ForceMusic = "RaidSwamp",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Swamp },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Surtling", MaxSpawned = 4, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { "army_bonemass", new RaidDefinition() {
                    Duration = 90f,
                    StartMessage = "$event_bonemass_start",
                    EndMessage = "$event_bonemass_over",
                    ForceMusic = "RaidSwamp",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Swamp },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_dragon" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Skeleton",      MaxSpawned = 4, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.Alerted },
                        new RaidSpawnEntry() { PrefabName = "Blob",          MaxSpawned = 2, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.Alerted },
                        new RaidSpawnEntry() { PrefabName = "Draugr",        MaxSpawned = 3, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.AgitatedByBuild },
                        new RaidSpawnEntry() { PrefabName = "Draugr_Elite",  MaxSpawned = 1, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Draugr_Ranged", MaxSpawned = 2, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { "wolves", new RaidDefinition() {
                    Duration = 60f,
                    StartMessage = "$event_wolves_start",
                    EndMessage = "$event_wolves_over",
                    ForceMusic = "RaidMountain",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mountain },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_dragon" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Wolf",    MaxSpawned = 5, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Fenring", MaxSpawned = 1, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { "army_moder", new RaidDefinition() {
                    Duration = 90f,
                    StartMessage = "$event_moder_start",
                    EndMessage = "$event_moder_over",
                    ForceMusic = "RaidMountain",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mountain },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_dragon" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_goblinking" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Hatchling",        MaxSpawned = 3, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Wolf",             MaxSpawned = 3, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist",  MaxSpawned = 1, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.AgitatedByBuild },
                    },
                }},
                { "army_goblin", new RaidDefinition() {
                    Duration = 90f,
                    StartMessage = "$event_goblins_start",
                    EndMessage = "$event_goblins_over",
                    ForceMusic = "RaidPlains",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Plains },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_goblinking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_queen" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Goblin",        MaxSpawned = 4, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "GoblinArcher",  MaxSpawned = 2, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "GoblinShaman",  MaxSpawned = 1, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.Alerted },
                        new RaidSpawnEntry() { PrefabName = "GoblinBrute",   MaxSpawned = 1, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.AgitatedByBuild },
                    },
                }},
                { "bats", new RaidDefinition() {
                    Duration = 60f,
                    StartMessage = "$event_bats_start",
                    EndMessage = "$event_bats_over",
                    ForceMusic = "RaidMistlands",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mistlands },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_goblinking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_queen" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Bat", MaxSpawned = 6, SpawnInterval = 3f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { "army_seekers", new RaidDefinition() {
                    Duration = 90f,
                    StartMessage = "$event_seekers_start",
                    EndMessage = "$event_seekers_over",
                    ForceMusic = "RaidMistlands",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Mistlands },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_queen" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_fader" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Seeker",       MaxSpawned = 4, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "SeekerBrute",  MaxSpawned = 1, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.AgitatedByBuild },
                        new RaidSpawnEntry() { PrefabName = "Tick",         MaxSpawned = 2, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { "army_charred", new RaidDefinition() {
                    Duration = 90f,
                    StartMessage = "$event_charred_start",
                    EndMessage = "$event_charred_over",
                    ForceMusic = "RaidAshLands",
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.AshLands },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_fader" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Charred_Melee",  MaxSpawned = 3, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Charred_Archer", MaxSpawned = 2, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Charred_Mage",   MaxSpawned = 1, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 2, CreatureAI = AI.HuntPlayer },
                    },
                }},
            },
        };

        internal static void SaveServerRaidData(string data) {
            ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
            File.WriteAllText(ValConfig.raidsServerSavedData, data);
        }

        internal static string LoadServerRaidData() {
            ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
            if (File.Exists(ValConfig.raidsServerSavedData)) {
                return File.ReadAllText(ValConfig.raidsServerSavedData);
            }
            return "";
        }

        internal static void Init() {
            SLE_Raid_Settings = DefaultConfiguration;
            try {
                if (File.Exists(ValConfig.raidsFilePath)) {
                    UpdateYamlConfig(File.ReadAllText(ValConfig.raidsFilePath));
                }
            }
            catch (Exception e) { Jotunn.Logger.LogWarning($"There was an error updating the Raid values, defaults will be used. Exception: {e}"); }
        }

        public static string YamlDefaultConfig() {
            return DataObjects.yamlserializer.Serialize(DefaultConfiguration);
        }

        public static bool UpdateYamlConfig(string yaml) {
            try {
                SLE_Raid_Settings = DataObjects.yamldeserializer.Deserialize<RaidConfiguration>(yaml);
                Logger.LogDebug("Loaded new Raid settings...");
                RaidControl.ApplyRaidConfiguration(RandEventSystem.instance);
            }
            catch (Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse RaidSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}
