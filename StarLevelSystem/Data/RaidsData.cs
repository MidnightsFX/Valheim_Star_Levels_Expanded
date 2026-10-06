using StarLevelSystem.common;
using StarLevelSystem.modules.Raids;
using StarLevelSystem.modules.UI;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class RaidsData
    {
        // Assigned in the static constructor, which runs after every field initializer. An initializer here ran before
        // DefaultConfiguration's (initializers run in textual order), so this was null until the first config load.
        public static RaidConfiguration SLE_Raid_Settings;

        static RaidsData() {
            SLE_Raid_Settings = DefaultConfiguration;
        }

        internal static Dictionary<string, RaidDefinition> RaidsByName = new Dictionary<string, RaidDefinition>();

        public static readonly RaidConfiguration DefaultConfiguration = new RaidConfiguration()
        {
            // Bump whenever the shipped raids change in a way every install should pick up (1: the 1.14.0
            // SpawnInterval retune, 2: the 1.19.2 MaxSpawned cut to 0.66x, rounded up, 3: the Deep North raids and
            // the blob and charred spawner raids' own message keys).
            // A file at another version is backed up and replaced with these defaults, unless every version since
            // its own only added raids (RaidsAddedInVersion): then it keeps everything and just gains those.
            RaidVersion = 3,
            GlobalSettings = new GlobalRaidSettings()
            {
                DisableAllRaids = false,
                GlobalRaidIntervalScalar = 1f,
                GlobalRaidChanceScalar = 1f,
                // The counts every raid below is written with. Moving this on the Raids page rescales them.
                RaidCreatureDensity = 3,
            },
            Raids = new List<RaidDefinition>()
            {
                { new RaidDefinition() {
                    Name = "army_eikthyr",
                    Duration = 180f,
                    StartMessage = "$event_eikthyrarmy_start",
                    EndMessage = "$event_eikthyrarmy_end",
                    ForceMusic = Music.Zcombat,
                    ForceEnvironment = DataObjects.Environment.Misty,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_eikthyr" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Greyling", MaxSpawned = 14, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Neck",    MaxSpawned = 7, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Boar",    MaxSpawned = 7, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "foresttrolls",
                    Duration = 180f,
                    StartMessage = "$event_foresttrolls_start",
                    EndMessage = "$event_foresttrolls_end",
                    ForceMusic = Music.Zcombat,
                    ForceEnvironment = DataObjects.Environment.DeepForest_Mist,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_eikthyr" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Troll", MaxSpawned = 6, SpawnInterval = 40f, SpawnChance = 100f, LevelMin = 1, LevelMax = 3, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2, 
                            CustomCreatureLevelUpChance = new SortedDictionary<int, float>() {
                                { 1, 50f },
                                { 2, 25f },
                                { 3, 20f },
                                { 4, 15f },
                                { 5, 7f },
                                { 6, 3f },
                                { 7, 1f },
                            } },
                    },
                }},
                { new RaidDefinition() {
                    Name = "army_theelder",
                    Duration = 180f,
                    StartMessage = "$event_gdkingarmy_start",
                    EndMessage = "$event_gdkingarmy_end",
                    ForceEnvironment = DataObjects.Environment.DeepForest_Mist,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Greydwarf",        MaxSpawned = 14, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3},
                        new RaidSpawnEntry() { PrefabName = "Greydwarf_Elite",  MaxSpawned = 4, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Greydwarf_Shaman", MaxSpawned = 3, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "skeletons",
                    Duration = 90f,
                    StartMessage = "$event_skeletons_start",
                    EndMessage = "$event_skeletons_end",
                    ForceEnvironment = DataObjects.Environment.Crypt,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Skeleton",        MaxSpawned = 14, SpawnInterval = 5f, SpawnChance = 100f, UseRaidLevelSystem = false, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Poison", MaxSpawned = 6, SpawnInterval = 8f, SpawnChance = 100f, UseRaidLevelSystem = false, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "blobs",
                    Duration = 90f,
                    // Vanilla has no blob raid text (its own blob raid borrows the bonemass army's), so these are ours.
                    StartMessage = "$SLS_event_blobs_start",
                    EndMessage = "$SLS_event_blobs_end",
                    ForceEnvironment = DataObjects.Environment.SwampRain,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Blob",      MaxSpawned = 14, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "BlobElite", MaxSpawned = 6, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "ghosts",
                    Duration = 180f,
                    StartMessage = "$event_ghosts_start",
                    EndMessage = "$event_ghosts_end",
                    ForceEnvironment = DataObjects.Environment.Ghosts,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Ghost", MaxSpawned = 8, SpawnInterval = 40f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Wraith", MaxSpawned = 8, SpawnInterval = 40f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "surtlings",
                    Duration = 60f,
                    StartMessage = "$event_surtlings_start",
                    EndMessage = "$event_surtlings_end",
                    ForceEnvironment = DataObjects.Environment.Ashlands_CinderRain,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Swamp, Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest },
                        NearBaseOnly = true,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Surtling", MaxSpawned = 27, SpawnInterval = 5f, SpawnChance = 100f, UseRaidLevelSystem = false, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2,
                            RequiredModifiers = new Dictionary<string, ModifierType>() { { "Fire", ModifierType.Major } }
                        },
                    },
                }},
                { new RaidDefinition() {
                    Name =  "army_bonemass",
                    Duration = 180f,
                    StartMessage = "$event_bonemassarmy_start",
                    EndMessage = "$event_bonemassarmy_end",
                    ForceMusic = Music.Zcombat,
                    ForceEnvironment = DataObjects.Environment.SwampRain,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_dragon" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Skeleton",      MaxSpawned = 20, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "Blob",          MaxSpawned = 7, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Draugr",        MaxSpawned = 8, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "Draugr_Elite",  MaxSpawned = 3, SpawnInterval = 13f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Draugr_Ranged", MaxSpawned = 6, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "wolves",
                    Duration = 60f,
                    StartMessage = "$event_wolves_start",
                    EndMessage = "$event_wolves_end",
                    ForceMusic = Music.Zcombat,
                    ForceEnvironment = DataObjects.Environment.SnowStorm,
                    Activation = new RaidActivation() {
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_dragon" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Wolf",    MaxSpawned = 14, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Fenring", MaxSpawned = 6, SpawnInterval = 16f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "cultists",
                    Duration = 180f,
                    StartMessage = "$event_caves_start",
                    EndMessage = "$event_caves_end",
                    ForceMusic = Music.Zcombat,
                    ForceEnvironment = DataObjects.Environment.SnowStorm,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_dragon" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_queen" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Ulv",    MaxSpawned = 14, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist", MaxSpawned = 4, SpawnInterval = 16f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "army_moder",
                    Duration = 180f,
                    StartMessage = "$event_moderarmy_start",
                    EndMessage = "$event_moderarmy_end",
                    ForceMusic = Music.Zcombat,
                    ForceEnvironment = DataObjects.Environment.Twilight_Snow,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_dragon" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_goblinking" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Hatchling",        MaxSpawned = 8, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "Wolf",             MaxSpawned = 8, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist",  MaxSpawned = 4, SpawnInterval = 16f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "army_goblin",
                    Duration = 180f,
                    StartMessage = "$event_goblinarmy_start",
                    EndMessage = "$event_goblinarmy_end",
                    ForceEnvironment = DataObjects.Environment.GoblinKing,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_goblinking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_queen" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Goblin",        MaxSpawned = 11, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "GoblinArcher",  MaxSpawned = 8, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "GoblinShaman",  MaxSpawned = 3, SpawnInterval = 13f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "GoblinBrute",   MaxSpawned = 2, SpawnInterval = 16f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "bats",
                    Duration = 60f,
                    StartMessage = "$event_bats_start",
                    EndMessage = "$event_bats_end",
                    ForceEnvironment = DataObjects.Environment.GoblinKing,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_goblinking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_queen" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Bat", MaxSpawned = 14, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                    },
                }},
                { new RaidDefinition() {
                    Name = "gjall_ambush",
                    Duration = 90f,
                    StartMessage = "$event_gjallarmy_start",
                    EndMessage = "$event_gjallarmy_end",
                    ForceMusic = Music.Zcombat,
                    ForceEnvironment = DataObjects.Environment.Mistlands_thunder,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_queen" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_fader" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Gjall",  MaxSpawned = 3, SpawnInterval = 16f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Tick",   MaxSpawned = 11, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2, ModifiersNotAllowed = new List<string>() { "FireNova" } },
                    },
                }},
                { new RaidDefinition() {
                    Name = "army_seekers",
                    Duration = 180f,
                    StartMessage = "$event_seekerarmy_start",
                    EndMessage = "$event_seekerarmy_end",
                    ForceEnvironment = DataObjects.Environment.Mistlands_thunder,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_queen" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_fader" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Seeker",       MaxSpawned = 8, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "SeekerBrute",  MaxSpawned = 2, SpawnInterval = 16f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Tick",         MaxSpawned = 4, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "army_charred",
                    Duration = 180f,
                    StartMessage = "$event_charredarmy_start",
                    EndMessage = "$event_charredarmy_end",
                    ForceEnvironment = DataObjects.Environment.Ashlands_storm,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_queen" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_fader" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Charred_Twitcher", MaxSpawned = 11, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Charred_Archer",   MaxSpawned = 4, SpawnInterval = 16f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Charred_Melee",    MaxSpawned = 3, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "army_charred_spawners",
                    Duration = 90f,
                    StartMessage = "$SLS_event_charredspawners_start",
                    EndMessage = "$SLS_event_charredspawners_end",
                    ForceEnvironment = DataObjects.Environment.Ashlands_ashrain,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_queen" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_fader" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Spawner_CharredStone", MaxSpawned = 3, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                    },
                }},
                // The Deep North's two vanilla raids, on vanilla's keys: each starts once its key creature has been killed
                // and stops for good with the final boss.
                { new RaidDefinition() {
                    Name = "army_elakingar",
                    Duration = 180f,
                    StartMessage = "$event_elakingarmy_start",
                    EndMessage = "$event_elakingarmy_end",
                    ForceEnvironment = DataObjects.Environment.Twilight_SnowStorm,
                    ForceMusic = Music.ZCombatEventL1,
                    Activation = new RaidActivation() {
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.DeepNorth },
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "elakingmole_defeated" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_frozenking_p3" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Elaking",        MaxSpawned = 14, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "ElakingLantern", MaxSpawned = 4, SpawnInterval = 10f, SpawnChance = 60f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "ElakingMole",    MaxSpawned = 3, SpawnInterval = 20f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "army_jotuns",
                    Duration = 180f,
                    StartMessage = "$event_jotunarmy_start",
                    EndMessage = "$event_jotunarmy_end",
                    ForceEnvironment = DataObjects.Environment.Twilight_Snow,
                    ForceMusic = Music.ZCombatEventL1,
                    Activation = new RaidActivation() {
                        // Vanilla's biomes for this raid: everywhere but the Ashlands and the ocean.
                        Biomes = new List<Heightmap.Biome>() { Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.DeepNorth },
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "jotun_killed" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_frozenking_p3" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "JotunWarrior", MaxSpawned = 4, SpawnInterval = 20f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Elaking",      MaxSpawned = 8, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                    },
                }},
                { new RaidDefinition() {
                    Name = "hildir_boss_revenge1",
                    Duration = 180f,
                    StartMessage = "$event_hildirboss1_start",
                    EndMessage = "$event_hildirboss1_end",
                    ForceMusic = Music.ZCombatEventL2,
                    ForceEnvironment = DataObjects.Environment.Ashlands_ashrain,
                    Activation = new RaidActivation() {
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "hildir1" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Hildir",  MaxSpawned = 1, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Skeleton", MaxSpawned = 11, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Poison",   MaxSpawned = 4, SpawnInterval = 13f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "hildir_boss_revenge2",
                    Duration = 180f,
                    StartMessage = "$event_hildirboss2_start",
                    EndMessage = "$event_hildirboss2_end",
                    ForceMusic = Music.ZCombatEventL3,
                    ForceEnvironment = DataObjects.Environment.Twilight_SnowStorm,
                    Activation = new RaidActivation() {
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "hildir2" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist_Hildir",  MaxSpawned = 1, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Ulv", MaxSpawned = 11, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist", MaxSpawned = 3, SpawnInterval = 13f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "hildir_boss_revenge3",
                    Duration = 180f,
                    StartMessage = "$event_hildirboss3_start",
                    EndMessage = "$event_hildirboss3_end",
                    ForceMusic = Music.ZCombatEventL4,
                    ForceEnvironment = DataObjects.Environment.GoblinKing,
                    Activation = new RaidActivation() {
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "hildir3" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "GoblinBruteBros",  MaxSpawned = 1, SpawnInterval = 5f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Goblin", MaxSpawned = 11, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "GoblinShaman",   MaxSpawned = 2, SpawnInterval = 13f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "deathlink_surprise",
                    Duration = 180f,
                    StartMessage = "$SLS_Secret_event1_start",
                    EndMessage = "$SLS_Secret_event1_end",
                    ForceEnvironment = DataObjects.Environment.Mistlands_thunder,
                    ForceMusic = Music.ZCombatEventL4,
                    Activation = new RaidActivation() {
                        Chance = 25f,
                        RequiredGlobalKeys = new List<string>() { "defeated_fader" },
                        RequiredPlayerKeys = new List<string>() { "Deathlink" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "GoblinBruteBros",  MaxSpawned = 1, SpawnInterval = 230f, SpawnChance = 100f, LevelMin = 15, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist_Hildir",  MaxSpawned = 1, SpawnInterval = 230f, SpawnChance = 100f, LevelMin = 20, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Hildir",  MaxSpawned = 1, SpawnInterval = 230f, SpawnChance = 100f, LevelMin = 25, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Poison",   MaxSpawned = 6, SpawnInterval = 13f, SpawnChance = 100f, LevelMin = 20, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
                        new RaidSpawnEntry() { PrefabName = "GoblinShaman",   MaxSpawned = 2, SpawnInterval = 13f, SpawnChance = 100f, LevelMin = 12, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
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

        // Apply hook for RaidSettings.yaml.
        internal static void ApplyLoaded(RaidConfiguration parsed) {
            {
                Logger.LogDebug("Loaded new Raid settings...");

                // A file with no Raids section would leave every SLE_Raid_Settings reader
                // (RaidManager.CheckForRaidUpdate first) dereferencing null forever. The config framework
                // already rejects an empty or comment-only document before this hook runs, and names the
                // file when it does, so this only has to cover a structurally valid file with nothing in it.
                if (parsed == null || parsed.Raids == null) {
                    Logger.LogWarning("The raid configuration defines no raids, the built-in default raid configuration will be used instead.");
                    parsed = DefaultConfiguration;
                }
                if (parsed.GlobalSettings == null) {
                    Logger.LogWarning("The raid configuration had no GlobalSettings, the built-in global raid defaults will be used instead.");
                    parsed.GlobalSettings = DefaultConfiguration.GlobalSettings;
                }
                SLE_Raid_Settings = parsed;

                RaidsByName.Clear();
                foreach (RaidDefinition raid in SLE_Raid_Settings.Raids) {
                    if (raid == null || string.IsNullOrEmpty(raid.Name)) {
                        Logger.LogWarning("A raid definition has no Name and will be skipped.");
                        continue;
                    }
                    // Both of these used to fall through to Add and throw, dropping the whole configuration
                    // into the catch below despite only one bad entry.
                    if (RaidsByName.ContainsKey(raid.Name)) {
                        Logger.LogWarning($"Raid with duplicate name, will be skipped. ({raid.Name})");
                        continue;
                    }
                    RaidsByName.Add(raid.Name, raid);
                }

                RaidControl.ApplyRaidConfiguration(RandEventSystem.instance);
            }
        }

        internal static int GetSchemaVersion(RaidConfiguration config) {
            return config != null ? config.RaidVersion : 0;
        }

        internal static void SetSchemaVersion(RaidConfiguration config, int version) {
            if (config != null) { config.RaidVersion = version; }
        }

        // The shipped raids each RaidVersion added and changed nothing else about. A file is only ever brought up through
        // these, never reset, when every version after its own is listed here.
        private static readonly Dictionary<int, string[]> RaidsAddedInVersion = new Dictionary<int, string[]>() {
            { 3, new string[] { "army_elakingar", "army_jotuns" } },
        };

        // Shipped message keys a RaidVersion replaced. A file brought up through RaidsAddedInVersion has each swapped wherever
        // it still holds the old key; a message an admin wrote themselves is left alone. 3: the charred spawner raid's keys
        // had lost their $ and the blob raid's never existed in vanilla, so both raids showed a raw key.
        private static readonly Dictionary<int, Dictionary<string, string>> MessagesReplacedInVersion = new Dictionary<int, Dictionary<string, string>>() {
            { 3, new Dictionary<string, string>() {
                { "event_charredspawnerarmy_start", "$SLS_event_charredspawners_start" },
                { "event_charrespawnerarmy_end", "$SLS_event_charredspawners_end" },
                { "$event_blobs_start", "$SLS_event_blobs_start" },
                { "$event_blobs_over", "$SLS_event_blobs_end" },
            } },
        };

        // Any version other than the current one -- including none at all, which every file written before
        // 1.14.0 has -- is replaced wholesale, unless the versions since its own only added raids. The framework
        // backs the previous file up next to it first, so an admin's own raids can be carried across by hand.
        // Same shape as NemesisSystemData.MigrateToCurrent.
        internal static RaidConfiguration MigrateToCurrent(RaidConfiguration parsed) {
            if (TryAddShippedRaids(parsed)) { return parsed; }
            Logger.LogInfo("Raid config version does not match this build, resetting it to the defaults.");
            // Fresh copy: returning the shared DefaultConfiguration would make it the live, runtime-mutated
            // settings object, and the migration path also writes the returned object over the user's file.
            return YamlFormat.Default.Deserializer.Deserialize<RaidConfiguration>(
                YamlFormat.Default.Serializer.Serialize(DefaultConfiguration));
        }

        // Adds the shipped raids from every version after the file's own, at the density the file's counts sit at, and
        // keeps everything else it holds. False when any of those versions changed more than its raid list.
        private static bool TryAddShippedRaids(RaidConfiguration parsed) {
            int current = DefaultConfiguration.RaidVersion;
            if (parsed?.Raids == null || parsed.RaidVersion <= 0 || parsed.RaidVersion >= current) { return false; }
            List<string> added = new List<string>();
            for (int version = parsed.RaidVersion + 1; version <= current; version++) {
                if (RaidsAddedInVersion.TryGetValue(version, out string[] names) == false) { return false; }
                added.AddRange(names);
            }

            // A fresh copy, as in MigrateToCurrent: the shipped raids must not become the live, runtime-mutated objects.
            RaidConfiguration shipped = YamlFormat.Default.Deserializer.Deserialize<RaidConfiguration>(
                YamlFormat.Default.Serializer.Serialize(DefaultConfiguration));
            int shippedDensity = shipped.GlobalSettings?.RaidCreatureDensity ?? QuickConfigureTool.DefaultRaidDensity;
            int fileDensity = parsed.GlobalSettings?.RaidCreatureDensity ?? shippedDensity;
            foreach (string name in added) {
                if (parsed.Raids.Exists(r => r != null && r.Name == name)) { continue; }
                RaidDefinition raid = shipped.Raids.Find(r => r != null && r.Name == name);
                if (raid == null) { continue; }
                QuickConfigureTool.ScaleRaidToDensity(raid, shippedDensity, fileDensity);
                parsed.Raids.Add(raid);
            }
            int messages = ReplaceShippedMessages(parsed.Raids, parsed.RaidVersion, current);
            string messageNote = messages > 0 ? $", updated {messages} raid messages" : "";
            Logger.LogInfo($"Raid config is from an older version; added the new raids ({string.Join(", ", added.ToArray())}){messageNote} and kept everything else.");
            return true;
        }

        // Swaps every message key the versions after `from` replaced, wherever a raid still holds the old one.
        private static int ReplaceShippedMessages(List<RaidDefinition> raids, int from, int to) {
            int replaced = 0;
            for (int version = from + 1; version <= to; version++) {
                if (MessagesReplacedInVersion.TryGetValue(version, out Dictionary<string, string> keys) == false) { continue; }
                foreach (RaidDefinition raid in raids) {
                    if (raid == null) { continue; }
                    if (raid.StartMessage != null && keys.TryGetValue(raid.StartMessage, out string start)) { raid.StartMessage = start; replaced++; }
                    if (raid.EndMessage != null && keys.TryGetValue(raid.EndMessage, out string end)) { raid.EndMessage = end; replaced++; }
                }
            }
            return replaced;
        }
    }
}
