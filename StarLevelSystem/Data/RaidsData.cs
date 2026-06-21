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

        internal static Dictionary<string, RaidDefinition> RaidsByName = new Dictionary<string, RaidDefinition>();

        public static readonly RaidConfiguration DefaultConfiguration = new RaidConfiguration()
        {
            GlobalSettings = new GlobalRaidSettings()
            {
                DisableAllRaids = false,
                GlobalRaidIntervalScalar = 1f,
                GlobalRaidChanceScalar = 1f,
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
                        new RaidSpawnEntry() { PrefabName = "Greyling", MaxSpawned = 20, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Neck",    MaxSpawned = 10, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Boar",    MaxSpawned = 10, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Troll", MaxSpawned = 9, SpawnInterval = 30f, SpawnChance = 100f, LevelMin = 1, LevelMax = 3, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2, 
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
                        new RaidSpawnEntry() { PrefabName = "Greydwarf",        MaxSpawned = 20, SpawnInterval = 3f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3},
                        new RaidSpawnEntry() { PrefabName = "Greydwarf_Elite",  MaxSpawned = 6, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Greydwarf_Shaman", MaxSpawned = 4, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 5, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Skeleton",        MaxSpawned = 20, SpawnInterval = 4f, SpawnChance = 100f, UseRaidLevelSystem = false, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Poison", MaxSpawned = 8, SpawnInterval = 6f, SpawnChance = 100f, UseRaidLevelSystem = false, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "blobs",
                    Duration = 90f,
                    StartMessage = "$event_blobs_start",
                    EndMessage = "$event_blobs_over",
                    ForceEnvironment = DataObjects.Environment.SwampRain,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_gdking" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_bonemass" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Blob",      MaxSpawned = 20, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "BlobElite", MaxSpawned = 8, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Ghost", MaxSpawned = 12, SpawnInterval = 30f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Wraith", MaxSpawned = 12, SpawnInterval = 30f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Surtling", MaxSpawned = 40, SpawnInterval = 4f, SpawnChance = 100f, UseRaidLevelSystem = false, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2,
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
                        new RaidSpawnEntry() { PrefabName = "Skeleton",      MaxSpawned = 30, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "Blob",          MaxSpawned = 10, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Draugr",        MaxSpawned = 12, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "Draugr_Elite",  MaxSpawned = 4, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Draugr_Ranged", MaxSpawned = 8, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 12, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Wolf",    MaxSpawned = 20, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Fenring", MaxSpawned = 8, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Ulv",    MaxSpawned = 20, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist", MaxSpawned = 6, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Hatchling",        MaxSpawned = 12, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "Wolf",             MaxSpawned = 12, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist",  MaxSpawned = 6, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 16, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Goblin",        MaxSpawned = 16, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2 },
                        new RaidSpawnEntry() { PrefabName = "GoblinArcher",  MaxSpawned = 12, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "GoblinShaman",  MaxSpawned = 4, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "GoblinBrute",   MaxSpawned = 2, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 20, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Bat", MaxSpawned = 20, SpawnInterval = 3f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
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
                        new RaidSpawnEntry() { PrefabName = "Gjall",  MaxSpawned = 4, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Tick",   MaxSpawned = 16, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 2, ModifiersNotAllowed = new List<string>() { "FireNova" } },
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
                        new RaidSpawnEntry() { PrefabName = "Seeker",       MaxSpawned = 12, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "SeekerBrute",  MaxSpawned = 2, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Tick",         MaxSpawned = 6, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 26, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Charred_Twitcher", MaxSpawned = 16, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Charred_Archer",   MaxSpawned = 6, SpawnInterval = 12f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Charred_Melee",    MaxSpawned = 4, SpawnInterval = 8f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                    },
                }},
                { new RaidDefinition() {
                    Name = "army_charred_spawners",
                    Duration = 90f,
                    StartMessage = "event_charredspawnerarmy_start",
                    EndMessage = "event_charrespawnerarmy_end",
                    ForceEnvironment = DataObjects.Environment.Ashlands_ashrain,
                    ForceMusic = Music.Zcombat,
                    Activation = new RaidActivation() {
                        NearBaseOnly = true,
                        Chance = 50f,
                        RequiredGlobalKeys = new List<string>() { "defeated_queen" },
                        NotRequiredGlobalKeys = new List<string>() { "defeated_fader" },
                    },
                    Spawns = new List<RaidSpawnEntry>() {
                        new RaidSpawnEntry() { PrefabName = "Spawner_CharredStone", MaxSpawned = 4, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Hildir",  MaxSpawned = 1, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Skeleton", MaxSpawned = 16, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Poison",   MaxSpawned = 6, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist_Hildir",  MaxSpawned = 1, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Ulv", MaxSpawned = 16, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist", MaxSpawned = 4, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "GoblinBruteBros",  MaxSpawned = 1, SpawnInterval = 4f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
                        new RaidSpawnEntry() { PrefabName = "Goblin", MaxSpawned = 16, SpawnInterval = 6f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer, SpawnGroupSize = 3 },
                        new RaidSpawnEntry() { PrefabName = "GoblinShaman",   MaxSpawned = 2, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 1, LevelMax = 30, CreatureAI = AI.HuntPlayer },
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
                        new RaidSpawnEntry() { PrefabName = "GoblinBruteBros",  MaxSpawned = 1, SpawnInterval = 180f, SpawnChance = 100f, LevelMin = 15, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
                        new RaidSpawnEntry() { PrefabName = "Fenring_Cultist_Hildir",  MaxSpawned = 1, SpawnInterval = 180f, SpawnChance = 100f, LevelMin = 20, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Hildir",  MaxSpawned = 1, SpawnInterval = 180f, SpawnChance = 100f, LevelMin = 25, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
                        new RaidSpawnEntry() { PrefabName = "Skeleton_Poison",   MaxSpawned = 8, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 20, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
                        new RaidSpawnEntry() { PrefabName = "GoblinShaman",   MaxSpawned = 2, SpawnInterval = 10f, SpawnChance = 100f, LevelMin = 12, LevelMax = 30, CreatureAI = AI.HuntPlayer, Faction = Character.Faction.Demon },
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
                } else {
                    RaidsData.SaveServerRaidData(DataObjects.yamlserializer.Serialize(RaidControl.ServerPlayerRaidData));
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
                Logger.LogDebug("Loaded new Raid settings...");
                SLE_Raid_Settings = DataObjects.yamldeserializer.Deserialize<RaidConfiguration>(yaml);

                RaidsByName.Clear();
                foreach (RaidDefinition raid in SLE_Raid_Settings.Raids) {
                    if (RaidsByName.ContainsKey(raid.Name)) {
                        Logger.LogWarning($"Raid with duplicate name, will be skipped. ({raid.Name})");
                    }
                    RaidsByName.Add(raid.Name, raid);
                }

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
