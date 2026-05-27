using BepInEx;
using BepInEx.Configuration;
using Jotunn.Entities;
using Jotunn.Managers;
using Splatform;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.Loot;
using StarLevelSystem.modules.Raids;
using StarLevelSystem.modules.Sizes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem
{
    internal class ValConfig
    {
        public static ConfigFile cfg;
        internal const string LevelSettingsFileName = "LevelSettings.yaml";
        internal const string ColorSettingsFileName = "ColorSettings.yaml";
        internal const string LootSettingsFileName = "LootSettings.yaml";
        internal const string ModifiersFileName = "Modifiers.yaml";
        internal const string RaidSettingsFileName = "RaidSettings.yaml";
        internal const string NemesisSettingsFileName = "NemesisSettings.yaml";
        internal const string StarLevelSystem = "StarLevelSystem";
        internal const string ServerRaidSavedData = "ServerRaidSavedData.yaml";
        internal const string SavedData = "SavedData";
        internal const string NemesisLogFileName = "NemesisLog.log";
        internal static String levelsFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, LevelSettingsFileName);
        internal static String colorsFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, ColorSettingsFileName);
        internal static String creatureLootFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, LootSettingsFileName);
        internal static String creatureModifierFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, ModifiersFileName);
        internal static String raidsFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, RaidSettingsFileName);
        internal static String nemesisFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, NemesisSettingsFileName);
        internal static String nemesisLogFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, SavedData, NemesisLogFileName);
        internal static String raidsServerSavedData = Path.Combine(Paths.ConfigPath, StarLevelSystem, SavedData, ServerRaidSavedData);

        internal static bool RecievedConfigsFromServer = false;

        private static CustomRPC LevelSettingsRPC;
        private static CustomRPC ColorSettingsRPC;
        private static CustomRPC CreatureLootSettingsRPC;
        private static CustomRPC ModifiersRPC;
        private static CustomRPC RaidsRPC;
        private static CustomRPC NemesisRPC;
        internal static CustomRPC ClientSendPlayerPrivateKeysRPC;
        internal static CustomRPC ClientStartRaidRPC;
        internal static CustomRPC ClientForcePlayMusicRPC;
        internal static CustomRPC ClientClearNearbyEventsRPC;
        internal static CustomRPC SendNewNemesisBossRPC;
        internal static CustomRPC RemoveNemeisBossRPC;

        public static ConfigEntry<bool> EnableDebugMode;
        public static ConfigEntry<int> MaxLevel;
        public static ConfigEntry<bool> OverlevedCreaturesGetRerolledOnLoad;
        public static ConfigEntry<bool> EnableMapRingsForDistanceBonus;
        public static ConfigEntry<bool> DistanceBonusIsFromStarterTemple;
        public static ConfigEntry<int> MiniMapRingGeneratorUpdatesPerFrame;
        public static ConfigEntry<string> DistanceRingColorOptions;
        public static ConfigEntry<bool> ControlSpawnerLevels;
        public static ConfigEntry<bool> ForceControlAllSpawns;
        public static ConfigEntry<string> SpawnsAlwaysControlled;
        public static ConfigEntry<bool> ControlBossSpawns;
        public static ConfigEntry<bool> ControlAbilitySpawnedCreatures;
        public static ConfigEntry<bool> EnableCreatureScalingPerLevel;
        public static ConfigEntry<bool> EnableScalingInDungeons;
        public static ConfigEntry<float> PerLevelScaleBonus;
        public static ConfigEntry<float> PerLevelLootScale;
        public static ConfigEntry<bool> ScaleAllLootByLevel;
        public static ConfigEntry<int> LootDropsPerTick;
        public static ConfigEntry<string> LootDropCalculationType;
        public static ConfigEntry<bool> LootEggsDropIncreaseStacks;
        public static ConfigEntry<bool> EggLevelDeterminedByItemQuality;
        public static ConfigEntry<bool> OffspringCanBeStrongerThanParents;
        public static ConfigEntry<float> OffspringGainExtraLevelChance;
        public static ConfigEntry<bool> OffspringCanBeInfertile;
        public static ConfigEntry<float> OffspringChanceToBeInfertile;
        public static ConfigEntry<float> EnemyHealthMultiplier;
        public static ConfigEntry<float> BossEnemyHealthMultiplier;
        public static ConfigEntry<float> EnemyHealthPerWorldLevel;
        public static ConfigEntry<float> EnemyDamageLevelMultiplier;
        public static ConfigEntry<float> BossEnemyDamageMultiplier;
        public static ConfigEntry<bool> EnableScalingBirds;
        public static ConfigEntry<float> BirdSizeScalePerLevel;
        public static ConfigEntry<bool> EnableScalingFish;
        public static ConfigEntry<float> FishSizeScalePerLevel;
        public static ConfigEntry<bool> EnableTreeScaling;
        public static ConfigEntry<float> TreeSizeScalePerLevel;
        public static ConfigEntry<bool> UseDeterministicTreeScaling;
        public static ConfigEntry<bool> RandomizeTameChildrenLevels;
        public static ConfigEntry<bool> RandomizeTameChildrenModifiers;
        public static ConfigEntry<bool> SpawnMultiplicationAppliesToTames;
        public static ConfigEntry<bool> BossCreaturesNeverSpawnMultiply;
        public static ConfigEntry<bool> EnableColorization;
        public static ConfigEntry<bool> EnableRockLevels;
        public static ConfigEntry<bool> EnableRidableCreatureSizeFixes;
        public static ConfigEntry<bool> MultipliedNightSpawnsRemovedDuringDay;

        public static ConfigEntry<float> PerLevelTreeLootScale;
        public static ConfigEntry<float> PerLevelBirdLootScale;
        public static ConfigEntry<float> PerLevelMineRockLootScale;
        public static ConfigEntry<float> PerLevelDestructibleLootScale;

        public static ConfigEntry<int> FishMaxLevel;
        public static ConfigEntry<int> BirdMaxLevel;
        public static ConfigEntry<int> TreeMaxLevel;
        public static ConfigEntry<int> RockMaxLevel;
        public static ConfigEntry<int> DestructibleMaxLevel;

        public static ConfigEntry<int> MaxMajorModifiersPerCreature;
        public static ConfigEntry<int> MaxMinorModifiersPerCreature;
        public static ConfigEntry<bool> LimitCreatureModifiersToCreatureStarLevel;
        public static ConfigEntry<float> ChanceMajorModifier;
        public static ConfigEntry<float> ChanceMinorModifier;
        public static ConfigEntry<bool> EnableBossModifiers;
        public static ConfigEntry<float> ChanceOfBossModifier;
        public static ConfigEntry<int> MaxBossModifiersPerBoss;
        public static ConfigEntry<bool> SplittersInheritLevel;
        public static ConfigEntry<int> LimitCreatureModifierPrefixes;
        public static ConfigEntry<bool> MinorModifiersFirstInName;

        public static ConfigEntry<bool> EnableDistanceLevelScalingBonus;
        public static ConfigEntry<bool> EnableMultiplayerEnemyHealthScaling;
        public static ConfigEntry<bool> EnableMultiplayerEnemyDamageScaling;
        public static ConfigEntry<int> MultiplayerScalingRequiredPlayersNearby;
        public static ConfigEntry<float> MultiplayerEnemyDamageModifier;
        public static ConfigEntry<float> MultiplayerEnemyHealthModifier;

        public static ConfigEntry<int> NumberOfCacheUpdatesPerFrame;
        public static ConfigEntry<bool> OutputColorizationGeneratorsData;
        public static ConfigEntry<int> FallbackDelayBeforeCreatureSetup;
        public static ConfigEntry<float> InitialDelayBeforeSetup;
        public static ConfigEntry<bool> EnableDebugOutputForDamage;
        public static ConfigEntry<bool> EnableDebugOutputLevelRolls;
        public static ConfigEntry<bool> EnableDebugLootDetails;
        public static ConfigEntry<bool> UseStarShapedModifierIcons;

        public static ConfigEntry<float> EnemyHealthbarScalarX;
        public static ConfigEntry<float> EnemyHealthbarScalarY;
        public static ConfigEntry<bool> EnableEnemyHeathbarNumberDisplay;
        public static ConfigEntry<float> HealthDisplayFontSizeAdjustment;

        public static ConfigEntry<bool> OnlyControlVanillaAreaSpawners;
        public static ConfigEntry<bool> OverrideCreatureModifiedHealth;

        public static ConfigEntry<bool> UseVanillaRaidConfiguration;
        public static ConfigEntry<float> RaidEventRate;
        public static ConfigEntry<int> MaxActiveRaids;
        public static ConfigEntry<int> MaxRaidAttemptsPerPlayer;
        public static ConfigEntry<float> RaidPerPlayerUpdateCheck;
        public static ConfigEntry<int> ServerTimeBetweenRaidStartChecks;

        public static ConfigEntry<bool> EnableNemesisSystem;

        public static ConfigEntry<float> ConfigPollIntervalSeconds;

        public ValConfig(ConfigFile cf)
        {
            // ensure all the config values are created
            cfg = cf;
            cfg.SaveOnConfigSet = true;
            CreateConfigValues(cf);
            ConfigFileWatcher.Register(cfg.ConfigFilePath, OnMainConfigFileChanged);
        }

        public void SetupConfigRPCs() {
            LevelSettingsRPC = NetworkManager.Instance.AddRPC("SLS_LevelsRPC", OnServerRecieveConfigs, OnClientReceiveLevelConfigs);
            ColorSettingsRPC = NetworkManager.Instance.AddRPC("SLS_ColorsRPC", OnServerRecieveConfigs, OnClientReceiveColorConfigs);
            CreatureLootSettingsRPC = NetworkManager.Instance.AddRPC("SLS_CreatureLootRPC", OnServerRecieveConfigs, OnClientReceiveCreatureLootConfigs);
            ModifiersRPC = NetworkManager.Instance.AddRPC("SLS_ModifiersRPC", OnServerRecieveConfigs, OnClientReceiveModifiersConfigs);
            RaidsRPC = NetworkManager.Instance.AddRPC("SLS_RaidsRPC", OnServerRecieveConfigs, OnClientReceiveRaidConfigs);
            NemesisRPC = NetworkManager.Instance.AddRPC("SLS_NemesisRPC", OnServerRecieveConfigs, OnClientReceiveNemesisConfigs);
            ClientSendPlayerPrivateKeysRPC = NetworkManager.Instance.AddRPC("SLS_SendPlayerKeysRPC", OnServerRecievePlayerPrivateKeys, OnClientRecieveRequestForPrivatekeys);
            ClientStartRaidRPC = NetworkManager.Instance.AddRPC("SLS_ClientStartRaidRPC", OnServerRecieveConfigs, OnClientRecieveRaidStart);
            ClientForcePlayMusicRPC = NetworkManager.Instance.AddRPC("SLS_ClientForcePlayMusicRPC", OnServerRecieveConfigs, OnClientRecieveForcePlayMusic);
            ClientClearNearbyEventsRPC = NetworkManager.Instance.AddRPC("SLS_ClientForceRemoveNearbyEventsRPC", OnServerRecieveConfigs, OnClientRecieveForceRemoveNearbyEvents);
            SendNewNemesisBossRPC = NetworkManager.Instance.AddRPC("SLS_SendNewNemesisBossRPC", OnServerRecieveNemesisBossAdd, OnClientRecieveMiniBossAdd);
            RemoveNemeisBossRPC = NetworkManager.Instance.AddRPC("SLS_RemoveNemesisBossRPC", OnServerRecieveNemesisBossRemove, OnClientRecieveMiniBossRemove);

            SynchronizationManager.Instance.AddInitialSynchronization(ClientSendPlayerPrivateKeysRPC, SendRequestForPrivateKeys);
            SynchronizationManager.Instance.AddInitialSynchronization(LevelSettingsRPC, SendLevelsConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(ColorSettingsRPC, SendColorsConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(CreatureLootSettingsRPC, SendCreatureLootConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(ModifiersRPC, SendModifierConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(RaidsRPC, SendRaidConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(NemesisRPC, SendNemesisConfigs);
        }

        private void CreateConfigValues(ConfigFile Config) {
            // Debugmode
            EnableDebugMode = Config.Bind("Client config", "EnableDebugMode", false,
                new ConfigDescription("Enables Debug logging.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugMode.SettingChanged += Logger.enableDebugLogging;
            Logger.CheckEnableDebugLogging();
            EnableDebugOutputForDamage = Config.Bind("Client config", "EnableDebugOutputForDamage", false,
                new ConfigDescription("Enables Detailed logging for damage calculations, warning, lots of logging.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugOutputLevelRolls = Config.Bind("Client config", "EnableDebugOutputLevelRolls", false,
                new ConfigDescription("Enables Detailed logging for creature levelup rolls.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugLootDetails = Config.Bind("Client config", "EnableDebugLootDetails", false,
                new ConfigDescription("Enables Detailed logging for loot generation.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));


            MaxLevel = BindServerConfig("LevelSystem", "MaxLevel", 20, "The Maximum number of stars that a creature can have", false, 1, 200);
            MaxLevel.SettingChanged += UpdateLevelsOnChange.ModifyLoadedCreatureLevels;
            OverlevedCreaturesGetRerolledOnLoad = BindServerConfig("LevelSystem", "OverlevedCreaturesGetRerolledOnLoad", true, "Rerolls creature levels which are above maximum defined level, when those creatures are loaded. This will automatically clean up overleveled creatures if you reduce the max level.");
            EnableCreatureScalingPerLevel = BindServerConfig("LevelSystem", "EnableCreatureScalingPerLevel", true, "Enables started creatures to get larger for each star");

            EnableDistanceLevelScalingBonus = BindServerConfig("LevelSystem", "EnableDistanceLevelScalingBonus", true, "Creatures further away from the center of the world have a higher chance to levelup, this is a bonus applied to existing creature/biome configuration.");
            EnableMapRingsForDistanceBonus = BindServerConfig("LevelSystem", "EnableMapRingsForDistanceBonus", true, "Enables map rings to show distance levels, this is a visual aid to help you see how far away from the center of the world you are.");
            EnableMapRingsForDistanceBonus.SettingChanged += DistanceScaleSystem.UpdateMapRingEnableSettingOnChange;
            DistanceBonusIsFromStarterTemple = BindServerConfig("LevelSystem", "DistanceBonusIsFromStarterTemple", false, "When enabled the distance bonus is calculated from the starter temple instead of world center, typically this makes little difference. But can help ensure your starting area is more correctly calculated.");
            DistanceBonusIsFromStarterTemple.SettingChanged += DistanceScaleSystem.OnRingCenterChanged;
            DistanceRingColorOptions = BindServerConfig("LevelSystem", "DistanceRingColorOptions", "White,Blue,Teal,Green,Yellow,Purple,Orange,Pink,Purple,Red,Grey", "The colors that distance rings will use, if there are more rings than colors, the color pattern will be repeated. (Optional, use an HTML hex color starting with # to have a custom color.) Available options: Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink, Gray, Brown, Black");
            DistanceRingColorOptions.SettingChanged += DistanceScaleSystem.UpdateMapColorSettingsOnChange;
            MiniMapRingGeneratorUpdatesPerFrame = BindServerConfig("LevelSystem", "MiniMapRingGeneratorUpdatesPerFrame", 1000, "The number of ring points to calculate per frame when generating the minimap rings. Higher values make this go faster, but can get it killed or cause instability.", true);
            PerLevelScaleBonus = BindServerConfig("LevelSystem", "PerLevelScaleBonus", 0.10f, "The additional size that a creature grows each star level.", true, 0f, 2f);
            PerLevelScaleBonus.SettingChanged += SizeModifications.StarLevelScaleChanged;
            EnableScalingInDungeons = BindServerConfig("LevelSystem", "EnableScalingInDungeons", false, "Enables scaling in dungeons, this can cause creatures to become stuck.");
            EnableColorization = BindServerConfig("LevelSystem", "EnableColorization", true, "Enables this mods colorization of creatures based on their star level.");
            EnemyHealthMultiplier = BindServerConfig("LevelSystem", "EnemyHealthMultiplier", 1f, "The amount of health that each level gives a creature, vanilla is 1x.", false, 0f, 5f);
            EnemyHealthPerWorldLevel = BindServerConfig("LevelSystem", "EnemyHealthPerWorldLevel", 0.2f, "The percent amount of health that each world level gives a creature, vanilla is 2x (eg 200% more health each world level).", false, 0.00f, 2f);
            EnemyDamageLevelMultiplier = BindServerConfig("LevelSystem", "EnemyDamageLevelMultiplier", 0.1f, "The amount of damage that each level gives a creatures, vanilla is 0.5x (eg 50% more damage each level).", false, 0.00f, 2f);
            BossEnemyHealthMultiplier = BindServerConfig("LevelSystem", "BossEnemyHealthMultiplier", 0.3f, "The amount of health that each level gives a boss. 1 is 100% more health per level.", false, 0f, 5f);
            BossEnemyDamageMultiplier = BindServerConfig("LevelSystem", "BossEnemyDamageMultiplier", 0.02f, "The amount of damage that each level gives a boss. 1 is 100% more damage per level.", false, 0f, 5f);
            RandomizeTameChildrenLevels = BindServerConfig("LevelSystem", "RandomizeTameLevels", false, "Randomly rolls bred creature levels, instead of inheriting from parent.");
            RandomizeTameChildrenModifiers = BindServerConfig("LevelSystem", "RandomizeTameChildrenModifiers", true, "Randomly rolls bred creatures modifiers instead of inheriting from a parent");
            SpawnMultiplicationAppliesToTames = BindServerConfig("LevelSystem", "SpawnMultiplicationAppliesToTames", false, "Spawn multipliers set on creature or biome will apply to produced tames when enabled.");
            BossCreaturesNeverSpawnMultiply = BindServerConfig("LevelSystem", "BossCreaturesNeverSpawnMultiply", true, "Boss creatures never have spawn multipliers applied to them.");
            EnableRidableCreatureSizeFixes = BindServerConfig("LevelSystem", "EnableRidableCreatureSizeFixes", true, "Enables collider fixes for ridable creatures (lox and Askavin).");
            MultipliedNightSpawnsRemovedDuringDay = BindServerConfig("LevelSystem", "MultipliedNightSpawnsRemovedDuringDay", true, "When true, night spawns will be flagged to despawn during the day, which will result in them running away and despawning. This can be disabled if desired.");

            EnableScalingBirds = BindServerConfig("ObjectLevels", "EnableScalingBirds", true, "Enables birds to scale with the level system. This will cause them to become larger and give more drops.");
            EnableScalingBirds.SettingChanged += UpdateLevelsOnChange.UpdateBirdSizeOnConfigChange;
            BirdSizeScalePerLevel = BindServerConfig("ObjectLevels", "BirdSizeScalePerLevel", 0.1f, "The amount of size that birds gain per level. 0.1 = 10% larger per level.", true, 0f, 2f);
            BirdSizeScalePerLevel.SettingChanged += UpdateLevelsOnChange.UpdateBirdSizeOnConfigChange;
            EnableScalingFish = BindServerConfig("ObjectLevels", "EnableScalingFish", true, "Enables star scaling for fish. This does potentially allow huge fish.");
            EnableScalingFish.SettingChanged += UpdateLevelsOnChange.UpdateFishSizeOnConfigChange;
            EnableRockLevels = BindServerConfig("ObjectLevels", "EnableRockLevels", false, "Enables level scaling for rocks.");
            FishMaxLevel = BindServerConfig("ObjectLevels", "FishMaxLevel", 20, "Sets the max level that fish can scale up to.", true, 1, 150);
            BirdMaxLevel = BindServerConfig("ObjectLevels", "BirdMaxLevel", 10, "Sets the max level that birds can scale up to.", true, 1, 150);
            TreeMaxLevel = BindServerConfig("ObjectLevels", "TreeMaxLevel", 10, "Sets the max level that trees can scale up to.", true, 1, 150);
            RockMaxLevel = BindServerConfig("ObjectLevels", "RockMaxLevel", 10, "Sets the max level that rocks can scale up to.", true, 1, 150);
            DestructibleMaxLevel = BindServerConfig("ObjectLevels", "DestructibleMaxLevel", 1, "Sets the max level that generic destructibles can be leveled to", true, 1, 150);
            FishSizeScalePerLevel = BindServerConfig("ObjectLevels", "FishSizeScalePerLevel", 0.1f, "The amount of size that fish gain per level 0.1 = 10% larger per level.");
            FishSizeScalePerLevel.SettingChanged += UpdateLevelsOnChange.UpdateFishSizeOnConfigChange;
            EnableTreeScaling = BindServerConfig("ObjectLevels", "EnableTreeScaling", true, "Enables level scaling of trees. Make the trees bigger than reasonable? sure why not.");
            EnableTreeScaling.SettingChanged += UpdateLevelsOnChange.UpdateTreeSizeOnConfigChange;
            UseDeterministicTreeScaling = BindServerConfig("ObjectLevels", "UseDeterministicTreeScaling", true, "Scales the level of trees based on biome and distance from the center/spawn. This does not randomize tree levels, but reduces network usage.");
            TreeSizeScalePerLevel = BindServerConfig("ObjectLevels", "TreeSizeScalePerLevel", 0.1f, "The amount of size that trees gain per level 0.1 = 10% larger per level.");
            TreeSizeScalePerLevel.SettingChanged += UpdateLevelsOnChange.UpdateTreeSizeOnConfigChange;
            PerLevelTreeLootScale = BindServerConfig("ObjectLevels", "PerLevelTreeLootScale", 0.2f, "The amount of additional wood that each level grants for a tree.", true);
            PerLevelBirdLootScale = BindServerConfig("ObjectLevels", "PerLevelBirdLootScale", 0.3f, "Per level additional loot that birds gain.", true);
            PerLevelMineRockLootScale = BindServerConfig("ObjectLevels", "PerLevelMineRockLootScale", 0.2f, "The amount of additional stones and ores that each level grants for a rock", true);
            PerLevelDestructibleLootScale = BindServerConfig("ObjectLevels", "PerLevelDestructibleLootScale", 0.2f, "The amount of additional loot that destructible items grant for each level", true);

            MultiplayerEnemyDamageModifier = BindServerConfig("Multiplayer", "MultiplayerEnemyDamageModifier", 0.05f, "The additional amount of damage enemies will do to players, when there is a group of players together, per player. .2 = 20%. Vanilla gives creatures 4% more damage per player nearby.", true, 0, 2f);
            MultiplayerEnemyHealthModifier = BindServerConfig("Multiplayer", "MultiplayerEnemyHealthModifier", 0.2f, "Enemies take reduced damage when there is a group of players, vanilla gives creatures 30% damage resistance per player nearby.", true, 0, 0.99f);
            MultiplayerScalingRequiredPlayersNearby = BindServerConfig("Multiplayer", "MultiplayerScalingRequiredPlayersNearby", 3, "The number of players in a local area required to cause monsters to gain bonus health and/or damage.", true, 1, 20);
            EnableMultiplayerEnemyHealthScaling = BindServerConfig("Multiplayer", "EnableMultiplayerEnemyHealthScaling", true, "Wether or not creatures gain more health when players are grouped up.");
            EnableMultiplayerEnemyDamageScaling = BindServerConfig("Multiplayer", "EnableMultiplayerEnemyDamageScaling", false, "Wether or not creatures gain more damage when players are grouped up.");
            
            ControlSpawnerLevels = BindServerConfig("LevelSystem", "ControlSpawnerLevels", true, "Overrides spawner levels to be controlled by SLS (this impacts all naturally spawning creatures)");
            ControlAbilitySpawnedCreatures = BindServerConfig("LevelSystem", "ControlAbilitySpawnedCreatures", true, "Forces creatures spawned from abilities to be controlled by SLS. This primarily impacts things such as the roots from Elder.");
            ControlBossSpawns = BindServerConfig("LevelSystem", "ControlBossSpawns", true, "Forces boss creatures to be controlled by SLS. Bosses will not get star levels if this is disabled.");
            ForceControlAllSpawns = BindServerConfig("LevelSystem", "ForceControlAllSpawns", false, "Forces all creatures to be controlled by SLS, this includes creatures spawned from player abilities and items. This will override creature levels, other mods must use the API to ensure their spawned creature levels are set.");
            //DistanceBonusMapsCanIncludeLowerLevels = BindServerConfig("LevelSystem", "DistanceBonusMapsCanIncludeLowerLevels", true, "When enabled makes the distance bonus configuration include the highest previously lower level defined keys, if they are not defined in the current level.");
            SpawnsAlwaysControlled = BindServerConfig("LevelSystem", "SpawnsAlwaysControlled", "piece_TrainingDummy", "A list of creatures which always get their level set");
            SpawnsAlwaysControlled.SettingChanged += LevelSelection.LeveledCreatureListChanged;
            LevelSelection.SetupForceLeveledCreatureList();

            PerLevelLootScale = BindServerConfig("LootSystem", "PerLevelLootScale", 1f, "The amount of additional loot that a creature provides per each star level", false, 0f, 4f);
            LootDropCalculationType = BindServerConfig("LootSystem", "LootDropCaluationType", "PerLevel", "The type of loot calculation to use. Per Level ", LootStyles.AllowedLootFactors, false);
            LootDropCalculationType.SettingChanged += LootStyles.LootFactorChanged;
            LootDropsPerTick = BindServerConfig("LootSystem", "LootDropsPerTick", 20, "The number of loot drops that are generated per tick, reducing this will reduce lag when massive amounts of loot is generated at once.", true, 1, 100);
            ScaleAllLootByLevel = BindServerConfig("LootSystem", "ScaleAllLootByLevel", false, "Enables scaling of all loot which does not normally scale per level. Typically this is just trophies.");
            LootEggsDropIncreaseStacks = BindServerConfig("LootSystem", "LootEggsDropIncreaseStacks", true, "This causes higher level chickens (and other egg producers) to drop MORE eggs instead of higher leveled ones.");
            EggLevelDeterminedByItemQuality = BindServerConfig("LootSystem", "EggLevelDeterminedByItemQuality", false, "When enabled, the level of egg grown creatures is determined by the eggs quality level. Otherwise the grown creature uses its default level configuration.");
            OffspringCanBeStrongerThanParents = BindServerConfig("LevelSystem", "OffspringCanBeStrongerThanParents", false, "When enabled, creatures that are bred can have higher levels than their parents. Otherwise, they will be capped at the highest parent level.");
            OffspringGainExtraLevelChance = BindServerConfig("LevelSystem", "OffspringGainExtraLevelChance", 0.05f, "When enabled, creatures that are bred have a chance to gain an extra level above their parents. Chance is based on this value, 0.1 = 10% chance.", false, 0f, 1f);
            OffspringCanBeInfertile = BindServerConfig("LevelSystem", "OffspringCanBeInfertile", false, "When enabled, creatures produced from breeding have a chance to be infertile.");
            OffspringChanceToBeInfertile = BindServerConfig("LevelSystem", "OffspringChanceToBeInfertile", 0.5f, "When enabled, the chance that a creature produced from breeding will be infertile.", true, 0f,1f);

            UseVanillaRaidConfiguration = BindServerConfig("Raids", "UseVanillaRaidConfiguration", false, "Reverts to use vanilla raid configuration when enabled.");
            RaidEventRate = BindServerConfig("Raids", "RaidEventRate", 1f, "The rate at which raid events occur (Vanilla is 1.0), higher values result in less frequent raids, lower values results in more frequent raids. This modifies the raid timing settings which are set per-raid.", false, 0.001f, 10f);
            MaxRaidAttemptsPerPlayer = BindServerConfig("Raids", "MaxRaidAttemptsPerPlayer", 5, "The Maximum number of times to try to activate a raid for a given player. The available raids will be shuffled each time before rolling their activation chance. With 10 raids defined the randomly selected first X will get a chance to spawn.", true, 0, 50);
            RaidPerPlayerUpdateCheck = BindServerConfig("Raids", "RaidPerPlayerUpdateCheck", 10f, "The Interval in minutes between updating the valid raids for each player. Reduce if you want new raids to become available faster for players, increase to reduce pressure on server.", true, 1f, 120f);
            ServerTimeBetweenRaidStartChecks = BindServerConfig("Raids", "ServerTimeBetweenRaidStartChecks", 25, "Number of minutes between when the server whill check to start raids (raids can still be on cooldown and will not be started).", true, 1, 120);
            MaxActiveRaids = BindServerConfig("Raids", "MaxActiveRaids", 10, "The maximum number of concurrent raids, automatically limited to 1 per player.");

            EnableNemesisSystem = BindServerConfig("Nemesis", "EnableNemesisSystem", true, "Enables the per-player Nemesis system that biases newly-spawning creature star levels based on a tracked player score.");

            MaxMajorModifiersPerCreature = BindServerConfig("Modifiers", "MaxMajorModifiersPerCreature", 1, "The default number of major modifiers that a creature can have.");
            MaxMinorModifiersPerCreature = BindServerConfig("Modifiers", "MaxMinorModifiersPerCreature", 1, "The default number of minor modifiers that a creature can have.");
            LimitCreatureModifiersToCreatureStarLevel = BindServerConfig("Modifiers", "LimitCreatureModifiersToCreatureStarLevel", true, "Limits the number of modifiers that a creature can have based on its level.");
            ChanceMajorModifier = BindServerConfig("Modifiers", "ChanceMajorModifier", 0.15f, "The chance that a creature will have a major modifier (creatures can have BOTH major and minor modifiers).", false, 0, 1f);
            ChanceMajorModifier.SettingChanged += CreatureModifiersData.ClearProbabilityCaches;
            ChanceMinorModifier = BindServerConfig("Modifiers", "ChanceMinorModifier", 0.25f, "The chance that a creature will have a minor modifier (creatures can have BOTH major and minor modifiers).", false, 0, 1f);
            ChanceMinorModifier.SettingChanged += CreatureModifiersData.ClearProbabilityCaches;
            EnableBossModifiers = BindServerConfig("Modifiers", "EnableBossModifiers", true, "Wether or not bosses can spawn with modifiers.");
            ChanceOfBossModifier = BindServerConfig("Modifiers", "ChanceOfBossModifier", 0.75f, "The chance that a boss will have a modifier.", false, 0, 1f);
            ChanceOfBossModifier.SettingChanged += CreatureModifiersData.ClearProbabilityCaches;
            MaxBossModifiersPerBoss = BindServerConfig("Modifiers", "MaxBossModifiersPerBoss", 2, "The maximum number of modifiers that a boss can have.");
            SplittersInheritLevel = BindServerConfig("Modifiers", "SplittersInheritLevel", true, "Wether or not creatures spawned from the Splitter modifier inherit the level of the parent creature.");
            LimitCreatureModifierPrefixes = BindServerConfig("Modifiers", "LimitCreatureModifierPrefixes", 3, "Maximum number of prefix names to use when building a creatures name.");
            MinorModifiersFirstInName = BindServerConfig("Modifiers", "MinorModifiersFirstInName", false, "Enables or disables ordering of modifiers for naming. If enabled, minor modifiers will be sorted first eg: Fast Poisonous");
            UseStarShapedModifierIcons = BindServerConfig("Modifiers", "UseStarShapedModifierIcons", true, "When enabled, uses modifier icons that are star shaped. When disabled, uses non-star shaped modifier icons. Requires a restart.", advanced: true);

            EnemyHealthbarScalarX = BindServerConfig("UI", "EnemyHealthbarScalarX", 1f, "The scale of the health bar for typical enemies. This does not impact bosses or players.", false, 0f, 4f);
            EnemyHealthbarScalarY = BindServerConfig("UI", "EnemyHealthbarScalarY", 1.75f, "The scale of the health bar for typical enemies. This does not impact bosses or players.", false, 0f, 4f);
            HealthDisplayFontSizeAdjustment = BindServerConfig("UI", "HealthDisplayFontSizeAdjustment", 0.8f, "Percentage modification for the font size on creature health.");
            EnableEnemyHeathbarNumberDisplay = BindServerConfig("UI", "EnableEnemyHeathbarNumberDisplay", false, "Enables a numerical display for enemy creatures health");

            NumberOfCacheUpdatesPerFrame = BindServerConfig("Misc", "NumberOfCacheUpdatesPerFrame", 10, "Number of cache updates to process when performing live updates", true, 1, 150);
            OutputColorizationGeneratorsData = BindServerConfig("Misc", "OutputColorizationGeneratorsData", false, "Writes out color generators to a debug file. This can be useful if you want to hand pick color settings from generated values.");
            InitialDelayBeforeSetup = BindServerConfig("Misc", "InitialDelayBeforeSetup", 0.5f, "The delay waited before a creature is setup, this is the delay that the person controlling the creature will wait before setup. Higher values will delay setup.");
            FallbackDelayBeforeCreatureSetup = BindServerConfig("Misc", "FallbackDelayBeforeCreatureSetup", 5, "The number of seconds non-owned creatures we will waited on before loading their modified attributes. This is a fallback setup.");
            ConfigPollIntervalSeconds = BindServerConfig("Misc", "ConfigPollIntervalSeconds", 30f, "The number of seconds between checks for changes in the yaml config files.", true, 1f, 300f);


            OnlyControlVanillaAreaSpawners = BindServerConfig("ModCompat", "OnlyControlVanillaAreaSpawners", true, "When enabled, will only control the spawned level from an AreaSpawner if it is a vanilla one.");
            OverrideCreatureModifiedHealth = BindServerConfig("ModCompat", "OverrideCreatureModifiedHealth", false, "When enabled, will always set creatures health based on the SLS settings for the creature. This overrides other mods changes to creatures.");
        }

        internal static void RecievedServerUpdates() {
            RecievedConfigsFromServer = true;
        }

        internal void LoadYamlConfigs()
        {
            string externalConfigFolder = ValConfig.GetSecondaryConfigDirectoryPath();
            string[] presentFiles = Directory.GetFiles(externalConfigFolder);
            bool foundLevelsFile = false;
            bool foundColorFile = false;
            bool foundLootFile = false;
            bool foundModifierFile = false;
            bool foundRaidFile = false;
            bool foundNemesisFile = false;

            foreach (string configFile in presentFiles) {
                if (configFile.Contains(LevelSettingsFileName))
                {
                    Logger.LogDebug($"Found level configuration: {configFile}");
                    levelsFilePath = configFile;
                    foundLevelsFile = true;
                }

                if (configFile.Contains(ColorSettingsFileName))
                {
                    Logger.LogDebug($"Found color configuration: {configFile}");
                    colorsFilePath = configFile;
                    foundColorFile = true;
                }

                if (configFile.Contains(LootSettingsFileName))
                {
                    Logger.LogDebug($"Found loot configuration: {configFile}");
                    creatureLootFilePath = configFile;
                    foundLootFile = true;
                }
                if (configFile.Contains(ModifiersFileName))
                {
                    Logger.LogDebug($"Found modifier configuration: {configFile}");
                    creatureModifierFilePath = configFile;
                    foundModifierFile = true;
                }
                if (configFile.Contains(RaidSettingsFileName))
                {
                    Logger.LogDebug($"Found raid configuration: {configFile}");
                    raidsFilePath = configFile;
                    foundRaidFile = true;
                }
                if (configFile.Contains(NemesisSettingsFileName))
                {
                    Logger.LogDebug($"Found nemesis configuration: {configFile}");
                    nemesisFilePath = configFile;
                    foundNemesisFile = true;
                }
            }

            if (foundModifierFile == false)
            {
                Logger.LogDebug("Loot config missing, recreating.");
                using (StreamWriter writetext = new StreamWriter(creatureModifierFilePath))
                {
                    String header = @"#################################################
# Star Level System Expanded - Creature Modifier Configuration
#################################################
";
                    writetext.WriteLine(header);
                    writetext.WriteLine(CreatureModifiersData.GetModifierDefaultConfig());
                }
            }

            if (foundLootFile == false)
            {
                Logger.LogDebug("Loot config missing, recreating.");
                using (StreamWriter writetext = new StreamWriter(creatureLootFilePath))
                {
                    String header = @"#################################################
# Star Level System Expanded - Creature loot configuration
#################################################
";
                    writetext.WriteLine(header);
                    writetext.WriteLine(LootSystemData.YamlDefaultConfig());
                }
            }

            if (foundLevelsFile == false) {
                Logger.LogDebug("Level config file missing, recreating.");
                using (StreamWriter writetext = new StreamWriter(levelsFilePath)) {
                    String header = @"#################################################
# Star Level System Expanded - Level Settings
#################################################
";
                    writetext.WriteLine(header);
                    writetext.WriteLine(LevelSystemData.YamlDefaultConfig());
                }
            }

            if (foundColorFile == false)
            {
                Logger.LogDebug("Color config file missing, recreating.");
                using (StreamWriter writetext = new StreamWriter(colorsFilePath)) {
                    String header = @"#################################################
# Star Level System Expanded - Creature Level Color Settings
#################################################
";
                    writetext.WriteLine(header);
                    writetext.WriteLine(Colorization.YamlDefaultConfig());
                }
            }

            if (foundRaidFile == false) {
                Logger.LogDebug("Raid config file missing, recreating.");
                using (StreamWriter writetext = new StreamWriter(raidsFilePath)) {
                    String header = @"#################################################
# Star Level System Expanded - Raid Settings
#################################################
";
                    writetext.WriteLine(header);
                    writetext.WriteLine(RaidsData.YamlDefaultConfig());
                }

            }

            if (foundNemesisFile == false) {
                Logger.LogDebug("Nemesis config file missing, recreating.");
                using (StreamWriter writetext = new StreamWriter(nemesisFilePath)) {
                    String header = @"#################################################
# Star Level System Expanded - Nemesis Settings
#################################################
";
                    writetext.WriteLine(header);
                    writetext.WriteLine(NemesisSystemData.YamlDefaultConfig());
                }
            }

            ConfigFileWatcher.Register(colorsFilePath, UpdateColorSettings);
            ConfigFileWatcher.Register(levelsFilePath, UpdateLevelSettings);
            ConfigFileWatcher.Register(creatureModifierFilePath, UpdateModifierSettings);
            ConfigFileWatcher.Register(creatureLootFilePath, UpdateLootSettings);
            ConfigFileWatcher.Register(raidsFilePath, UpdateRaidSettings);
            ConfigFileWatcher.Register(nemesisFilePath, UpdateNemesisSettings);
        }

        private static void UpdateColorSettings(string fullFileName) {
            Logger.LogDebug("Triggering Color Settings update.");
            string filetext = File.ReadAllText(fullFileName);
            Colorization.UpdateYamlConfig(filetext);
            ColorSettingsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(fullFileName));
        }

        private static void UpdateLevelSettings(string fullFileName) {
            Logger.LogDebug("Triggering Level Settings update.");
            string filetext = File.ReadAllText(fullFileName);
            LevelSystemData.UpdateYamlConfig(filetext);
            LevelSettingsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(fullFileName));
        }

        private static void UpdateLootSettings(string fullFileName) {
            Logger.LogDebug("Triggering Loot Settings update.");
            string filetext = File.ReadAllText(fullFileName);
            LootSystemData.UpdateYamlConfig(filetext);
            CreatureLootSettingsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(fullFileName));
        }

        private static void UpdateModifierSettings(string fullFileName) {
            Logger.LogDebug("Triggering Modifiers Settings update.");
            string filetext = File.ReadAllText(fullFileName);
            CreatureModifiersData.UpdateModifierConfig(filetext);
            ModifiersRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(fullFileName));
        }

        private static void UpdateRaidSettings(string fullFileName) {
            Logger.LogDebug("Triggering Raid Settings update.");
            string filetext = File.ReadAllText(fullFileName);
            RaidsData.UpdateYamlConfig(filetext);
            RaidsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(fullFileName));
        }

        private static void UpdateNemesisSettings(string fullFileName) {
            Logger.LogDebug("Triggering Nemesis Settings update.");
            string filetext = File.ReadAllText(fullFileName);
            NemesisSystemData.UpdateYamlConfig(filetext);
            NemesisRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(fullFileName));
        }

        private static void OnMainConfigFileChanged(string _) {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) {
                return;
            }
            Logger.LogInfo("Configuration file has been changed, reloading settings.");
            cfg.Reload();
        }

        private static ZPackage SendFileAsZPackage(string filepath) {
            string filecontents = File.ReadAllText(filepath);
            ZPackage package = new ZPackage();
            package.Write(filecontents);
            return package;
        }

        private static ZPackage SendLevelsConfigs() {
            return SendFileAsZPackage(levelsFilePath);
        }

        private static ZPackage SendCreatureLootConfigs() {
            return SendFileAsZPackage(creatureLootFilePath);
        }

        private static ZPackage SendColorsConfigs() {
            return SendFileAsZPackage(colorsFilePath);
        }

        private static ZPackage SendModifierConfigs() {
            return SendFileAsZPackage(creatureModifierFilePath);
        }

        private static ZPackage SendRaidConfigs() {
            return SendFileAsZPackage(raidsFilePath);
        }

        private static ZPackage SendNemesisConfigs() {
            return SendFileAsZPackage(nemesisFilePath);
        }

        private static ZPackage SendRequestForPrivateKeys() {
           ZPackage package = new ZPackage();
            return package;
        }

        public static IEnumerator OnServerRecieveConfigs(long sender, ZPackage package) {
            Logger.LogDebug("Server recieved config from client, rejecting due to being the server.");
            yield return null;
        }

        public static IEnumerator OnServerRecieveNemesisBossAdd(long sender, ZPackage package) {
            // Write the update to the local file and memory
            var yaml = package.ReadString();
            NemesisMiniboss nemboss = DataObjects.yamldeserializer.Deserialize<NemesisMiniboss>(yaml);
            NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.Add(nemboss);
            File.WriteAllText(ValConfig.nemesisFilePath, DataObjects.yamlserializer.Serialize(NemesisSystemData.SLE_Nemesis_Settings));
            // Send the update to all of the other clients
            ZNet.instance.GetPeers().ForEach(peer => {
                if (peer.m_uid != sender) {
                    ClientSendPlayerPrivateKeysRPC.SendPackage(peer.m_uid, package);
                }
            });

            yield return null;
        }

        public static IEnumerator OnServerRecieveNemesisBossRemove(long sender, ZPackage package) {
            // Write the update to the local file and memory
            var yaml = package.ReadString();
            NemesisMiniboss nemboss = DataObjects.yamldeserializer.Deserialize<NemesisMiniboss>(yaml);
            NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.Remove(nemboss);
            File.WriteAllText(ValConfig.nemesisFilePath, DataObjects.yamlserializer.Serialize(NemesisSystemData.SLE_Nemesis_Settings));
            // Send the update to all of the other clients
            ZNet.instance.GetPeers().ForEach(peer => {
                if (peer.m_uid != sender) {
                    ClientSendPlayerPrivateKeysRPC.SendPackage(peer.m_uid, package);
                }
            });

            yield return null;
        }

        private static IEnumerator OnClientReceiveLevelConfigs(long sender, ZPackage package) {
            var levelsyaml = package.ReadString();
            LevelSystemData.UpdateYamlConfig(levelsyaml);

            yield return null;
        }

        private static IEnumerator OnClientReceiveColorConfigs(long sender, ZPackage package) {
            var colorsyaml = package.ReadString();
            Colorization.UpdateYamlConfig(colorsyaml);

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientReceiveCreatureLootConfigs(long sender, ZPackage package) {
            var colorsyaml = package.ReadString();
            LootSystemData.UpdateYamlConfig(colorsyaml);

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientReceiveModifiersConfigs(long sender, ZPackage package) {
            var yaml = package.ReadString();
            CreatureModifiersData.UpdateModifierConfig(yaml);

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientRecieveMiniBossAdd(long sender, ZPackage package) {
            var yaml = package.ReadString();
            NemesisMiniboss nemboss = DataObjects.yamldeserializer.Deserialize<NemesisMiniboss>(yaml);
            if (NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.Contains(nemboss) == false) {
                NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.Add(nemboss);
            }
            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientRecieveMiniBossRemove(long sender, ZPackage package) {
            var yaml = package.ReadString();
            NemesisMiniboss nemboss = DataObjects.yamldeserializer.Deserialize<NemesisMiniboss>(yaml);
            NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.Remove(nemboss);
            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientRecieveRaidStart(long sender, ZPackage package) {
            var yaml = package.ReadString();
            NetworkRaidRequest raidNetRequest = DataObjects.yamldeserializer.Deserialize<NetworkRaidRequest>(yaml);
            Vector3 raidPosition = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            if (raidNetRequest.RaidPostion != Vector3.zero) {
                raidPosition = raidNetRequest.RaidPostion;
            }

            RaidControl.StartRaidRunner(raidNetRequest.Raid, raidPosition);

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientRecieveForcePlayMusic(long sender, ZPackage package) {
            string musicName = package.ReadString();
            if (Enum.TryParse<Music>(musicName, out Music music) == false) {
                Logger.LogWarning($"Music {musicName} not found.");
                yield break;
            }

            MusicMan.instance.TriggerMusic(music.ToString());

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientRecieveForceRemoveNearbyEvents(long sender, ZPackage package) {
            RaidControl.RemoveNearbyRunningEvents();
            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        internal static IEnumerator OnClientRecieveRequestForPrivatekeys(long sender, ZPackage _) {
            if (Player.m_localPlayer == null) { yield break; }
            //Logger.LogDebug("Collecting players private keys");
            List<string> playerKeys = Player.m_localPlayer.GetPrivateKeysSanitize();
            string filecontents = DataObjects.yamlserializerJsonCompat.Serialize(playerKeys);
            ZPackage package = new ZPackage();
            package.Write(filecontents);
            if (string.IsNullOrEmpty(filecontents) || playerKeys.Count <= 0) {
                Logger.LogDebug($"No private keys recieved from player: {Player.m_localPlayer.m_name}, skipping update to the server.");
                yield break;
            }
            
            if (ZNet.instance.GetServerPeer() != null && ZNet.instance.IsCurrentServerDedicated()) {
                Logger.LogDebug($"Sending private keys to server: {filecontents}");
                ClientSendPlayerPrivateKeysRPC.SendPackage(ZNet.instance.GetServerPeer().m_uid, package);
            } else {
                // This is to handle integrated servers (singleplayer) where the server is the same as the client
                Logger.LogDebug($"Updating server with private keys: {filecontents}");
                string PlatformAndID = SLSExtensions.GetLocalUserPlatformAndID();
                if (string.IsNullOrEmpty(PlatformAndID)) {
                    Logger.LogWarning("Could not update player private keys. Players platform was not detected.");
                    yield break;
                }
                RaidControl.UpdateOrAddPlayerPrivateKeys(PlatformAndID, playerKeys);
            }
        }

        private static IEnumerator OnServerRecievePlayerPrivateKeys(long sender, ZPackage package) {
            var yaml = package.ReadString();
            List<string> playerKeys = DataObjects.yamldeserializer.Deserialize<List<string>>(yaml);
            RaidControl.UpdateOrAddPlayerPrivateKeys(sender, playerKeys);
            yield break;
        }

        private static IEnumerator OnClientReceiveRaidConfigs(long sender, ZPackage package) {
            var yaml = package.ReadString();
            RaidsData.UpdateYamlConfig(yaml);

            yield return null;
        }

        private static IEnumerator OnClientReceiveNemesisConfigs(long sender, ZPackage package) {
            var yaml = package.ReadString();
            NemesisSystemData.UpdateYamlConfig(yaml);

            yield return null;
        }

        public static string GetSecondaryConfigDirectoryPath() {
            string path = Path.Combine(Paths.ConfigPath, StarLevelSystem);
            DirectoryInfo dirInfo = Directory.CreateDirectory(path);

            return dirInfo.FullName;
        }

        public static string GetSavedDataSecondaryConfigDirectoryPath() {
            string savedDataFolder = Path.Combine(Paths.ConfigPath, StarLevelSystem, SavedData);
            DirectoryInfo dirInfo = Directory.CreateDirectory(savedDataFolder);
            return dirInfo.FullName;
        }

        /// <summary>
        ///  Helper to bind configs for bool types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="acceptableValues"></param>>
        /// <param name="advanced"></param>
        /// <returns></returns>
        public static ConfigEntry<bool> BindServerConfig(string catagory, string key, bool value, string description, AcceptableValueBase acceptableValues = null, bool advanced = false) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for int types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valmin"></param>
        /// <param name="valmax"></param>
        /// <returns></returns>
        public static ConfigEntry<int> BindServerConfig(string catagory, string key, int value, string description, bool advanced = false, int valmin = 0, int valmax = 150) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<int>(valmin, valmax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for float types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valmin"></param>
        /// <param name="valmax"></param>
        /// <returns></returns>
        public static ConfigEntry<float> BindServerConfig(string catagory, string key, float value, string description, bool advanced = false, float valmin = 0, float valmax = 150) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<float>(valmin, valmax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for strings
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <returns></returns>
        public static ConfigEntry<string> BindServerConfig(string catagory, string key, string value, string description, AcceptableValueList<string> acceptableValues = null, bool advanced = false) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(
                    description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }
    }
}
