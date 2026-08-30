using BepInEx;
using BepInEx.Configuration;
using Jotunn.Entities;
using Jotunn.Managers;
using Splatform;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.LocationReset;
using StarLevelSystem.modules.Loot;
using StarLevelSystem.modules.Raids;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common {
    internal class ValConfig
    {
        public static ConfigFile cfg;
        internal const string LevelSettingsFileName = "LevelSettings.yaml";
        internal const string ColorSettingsFileName = "ColorSettings.yaml";
        internal const string LootSettingsFileName = "LootSettings.yaml";
        internal const string ModifiersFileName = "Modifiers.yaml";
        internal const string RaidSettingsFileName = "RaidSettings.yaml";
        internal const string NemesisSettingsFileName = "NemesisSettings.yaml";
        internal const string LocationResetSettingsFileName = "LocationResetSettings.yaml";
        internal const string StarLevelSystem = "StarLevelSystem";
        // Read by Common/Config to resolve every config path. Same value as the const above; the framework
        // just expects this name.
        internal static readonly string cfgFolder = StarLevelSystem;
        internal const string ServerRaidSavedData = "ServerRaidSavedData.yaml";
        internal const string SavedData = "SavedData";
        internal const string NemesisLogFileName = "NemesisLog.log";
        internal static String nemesisLogFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, SavedData, NemesisLogFileName);
        // World-state files resolve per-world (basename.<world>.ext) - see PerWorldStatePath. With a
        // single shared path, switching worlds silently overwrote world A's state with world B's.
        internal static String raidsServerSavedData => PerWorldStatePath(ServerRaidSavedData);
        internal const string ZoneDataFileName = "ZoneData.yaml";
        internal static String zoneDataSavedDataPath => PerWorldStatePath(ZoneDataFileName);
        internal const string NemesisRemoteStateFileName = "NemesisRemoteState.yaml";
        internal static String nemesisRemoteStateFilePath => PerWorldStatePath(NemesisRemoteStateFileName);
        // Per-zone reset stamps + prefab census. World state rather than config: binary, never watched,
        // never RPC'd, and flushed with the world save.
        internal const string LocationResetStateFileName = "LocationResetState.dat";
        internal static String locationResetStatePath => PerWorldStatePath(LocationResetStateFileName);

        private static string perWorldCacheWorld;
        private static readonly Dictionary<string, string> perWorldPathCache = new Dictionary<string, string>();

        // Resolves a world-state file to a per-world path, seeding it once from the legacy shared
        // file so pre-existing data carries over. Seeding every world with the legacy data is safe:
        // the loaders validate the world name recorded inside the file and start fresh on a
        // mismatch, so only the world the data belongs to keeps it.
        internal static string PerWorldStatePath(string fileName) {
            string dir = Path.Combine(Paths.ConfigPath, StarLevelSystem, SavedData);
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : null;
            if (string.IsNullOrEmpty(world)) {
                // No world loaded yet (menu-time access): fall back to the legacy shared path.
                return Path.Combine(dir, fileName);
            }
            if (perWorldCacheWorld != world) {
                perWorldPathCache.Clear();
                perWorldCacheWorld = world;
            }
            if (perWorldPathCache.TryGetValue(fileName, out string cached)) { return cached; }

            string sanitized = world;
            foreach (char c in Path.GetInvalidFileNameChars()) {
                sanitized = sanitized.Replace(c, '_');
            }
            string suffixedPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(fileName) + "." + sanitized + Path.GetExtension(fileName));
            try {
                string legacyPath = Path.Combine(dir, fileName);
                if (File.Exists(suffixedPath) == false && File.Exists(legacyPath)) {
                    Directory.CreateDirectory(dir);
                    File.Copy(legacyPath, suffixedPath);
                }
            } catch (Exception e) {
                Logger.LogWarning($"Could not migrate {fileName} to a per-world file: {e.Message}");
            }
            perWorldPathCache[fileName] = suffixedPath;
            return suffixedPath;
        }
        internal const string LocationResetCatalogFileName = "LocationResetCatalog.yaml";
        internal static String locationResetCatalogPath = Path.Combine(Paths.ConfigPath, StarLevelSystem, SavedData, LocationResetCatalogFileName);
        // A human-readable record of every chunk the reset sweep touched, in the same shape as the
        // Nemesis action log: buffered in memory and appended in batches rather than written per line.
        internal const string LocationResetLogFileName = "LocationResetLog.log";
        internal static String locationResetLogFilePath = Path.Combine(Paths.ConfigPath, StarLevelSystem, SavedData, LocationResetLogFileName);

        // The seven config-file RPCs, their initial-sync providers and the sync-state flag now belong
        // to Common/Config -- registrations in StarLevelConfigFiles.cs, wiring in ConfigNetwork.
        // Everything below is RPC traffic that is NOT a config file.
        internal static CustomRPC ClientSendPlayerPrivateKeysRPC;
        internal static CustomRPC ClientStartRaidRPC;
        internal static CustomRPC RaidCommittedRPC;
        internal static CustomRPC ClientForcePlayMusicRPC;
        internal static CustomRPC ClientClearNearbyEventsRPC;
        internal static CustomRPC SendNewNemesisBossRPC;
        internal static CustomRPC RemoveNemesisBossRPC;
        internal static CustomRPC AddNemesisBossPinRPC;
        internal static CustomRPC RemoveNemesisBossPinRPC;
        internal static CustomRPC ReportNemesisBossDeathRPC;
        internal static CustomRPC ClientPlaceNemesisSpawnerRPC;
        internal static CustomRPC ClientCommandRequestRPC;
        internal static CustomRPC CommandOutputRPC;
        internal static CustomRPC LocationApiRequestRPC;
        internal static CustomRPC LocationApiResultRPC;
        internal static CustomRPC ZoneKillReportRPC;
        internal static CustomRPC ZoneLevelSyncRPC;

        public static ConfigEntry<bool> EnableDebugMode;
        public static ConfigEntry<bool> EnableTerminalColors;
        public static ConfigEntry<int> MaxLevel;
        public static ConfigEntry<int> MaxBossLevel;
        public static ConfigEntry<bool> OverLevelCreaturesGetRerolledOnLoad;
        public static ConfigEntry<bool> OverLevelTamesGetRerolledOnLoad;
        public static ConfigEntry<bool> EnableMapRingsForDistanceBonus;
        public static ConfigEntry<bool> MapRingsAboveFog;
        public static ConfigEntry<bool> DistanceBonusIsFromStarterTemple;
        public static ConfigEntry<int> MiniMapRingGeneratorUpdatesPerFrame;
        public static ConfigEntry<string> DistanceRingColorOptions;
        public static ConfigEntry<bool> ControlSpawnerLevels;
        public static ConfigEntry<bool> ForceControlAllSpawns;
        public static ConfigEntry<bool> ControlBossSpawns;
        public static ConfigEntry<bool> ControlAbilitySpawnedCreatures;
        public static ConfigEntry<bool> EnableCreatureScalingPerLevel;
        public static ConfigEntry<bool> EnableScalingInDungeons;
        public static ConfigEntry<float> PerLevelScaleBonus;
        public static ConfigEntry<float> MinimumCreatureScale;
        public static ConfigEntry<float> PerLevelLootScale;
        public static ConfigEntry<float> PerLevelLootChanceScale;
        public static ConfigEntry<float> ChanceBaseChancePerLevel;
        public static ConfigEntry<bool> ScaleAllLootByLevel;
        public static ConfigEntry<int> LootDropsPerTick;
        public static ConfigEntry<string> LootDropCalculationType;
        public static ConfigEntry<bool> LootEggsDropIncreaseStacks;
        public static ConfigEntry<bool> CreatureLootDropStacked;
        public static ConfigEntry<bool> TreeLootDropsStacked;
        public static ConfigEntry<bool> RockLootDropsStacked;
        public static ConfigEntry<bool> MiscLootDropsStacked;

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
        public static ConfigEntry<bool> EvolvingCanRollNewModifiers;
        public static ConfigEntry<float> EvolvingChanceToRollNewModifier;

        public static ConfigEntry<bool> EnableDistanceLevelScalingBonus;
        public static ConfigEntry<bool> EnableMultiplayerEnemyHealthScaling;
        public static ConfigEntry<bool> EnableMultiplayerEnemyDamageScaling;
        public static ConfigEntry<int> MultiplayerScalingRequiredPlayersNearby;
        public static ConfigEntry<float> MultiplayerEnemyDamageModifier;
        public static ConfigEntry<float> MultiplayerEnemyHealthModifier;
        public static ConfigEntry<float> MultiplayerEnemyMinDamageTaken;

        public static ConfigEntry<int> NumberOfCacheUpdatesPerFrame;
        public static ConfigEntry<bool> OutputColorizationGeneratorsData;
        public static ConfigEntry<int> FallbackDelayBeforeCreatureSetup;
        public static ConfigEntry<float> InitialDelayBeforeSetup;
        public static ConfigEntry<bool> EnableDebugOutputForDamage;
        public static ConfigEntry<bool> EnableDebugOutputLevelRolls;
        public static ConfigEntry<bool> EnableDebugLootDetails;
        public static ConfigEntry<string> ModifierIconDisplayStyle;

        public static ConfigEntry<float> EnemyHealthbarScalarX;
        public static ConfigEntry<float> EnemyHealthbarScalarY;
        public static ConfigEntry<bool> EnableEnemyHealthbarNumberDisplay;
        public static ConfigEntry<float> HealthDisplayFontSizeAdjustment;
        public static ConfigEntry<bool> StackMultipleBossHealthbars;
        public static ConfigEntry<float> BossHealthbarSpacing;
        public static ConfigEntry<int> BossHudTopBuffer;
        public static ConfigEntry<float> BossHealthbarWidthPercent;
        public static ConfigEntry<bool> EnableJewelCraftingBossHudCompat;
        public static ConfigEntry<bool> UseCustomHealthFont;

        public static ConfigEntry<bool> OnlyControlVanillaAreaSpawners;
        public static ConfigEntry<bool> OverrideCreatureModifiedHealth;

        public static ConfigEntry<bool> UseVanillaRaidConfiguration;
        public static ConfigEntry<float> RaidEventRate;
        public static ConfigEntry<int> MaxActiveRaids;
        public static ConfigEntry<int> MaxRaidAttemptsPerPlayer;
        public static ConfigEntry<int> ServerTimeBetweenRaidStartChecks;
        public static ConfigEntry<int> RaidWindDownSeconds;
        public static ConfigEntry<bool> RaidForceDeleteStragglers;
        public static ConfigEntry<bool> EnableDebugRaidDetails;
        public static ConfigEntry<bool> EnableCustomRaidsCompat;

        public static ConfigEntry<bool> EnableNemesisSystem;
        public static ConfigEntry<bool> EnableNemesisRemoteSpawning;
        public static ConfigEntry<bool> EnableDebugNemesisDetails;

        public static ConfigEntry<bool> EnableLocationReset;
        public static ConfigEntry<float> LocationResetSweepBudgetMs;
        // The envelope around client-issued Location Reset API requests. There is no permission gate
        // on those by design (a mod that resets locations from gameplay has to work for the players
        // using it), so these three are what bound them -- alongside the protection scan, which is
        // never bypassable by any route.
        public static ConfigEntry<float> ClientLocationResetMaxRadius;
        public static ConfigEntry<float> ClientLocationResetMaxDistance;
        public static ConfigEntry<float> ClientLocationResetCooldownSeconds;
        public static ConfigEntry<bool> EnableDebugLocationResetDetails;
        public static ConfigEntry<bool> EnableLocationResetLog;

        public static ConfigEntry<bool> EnableZoneScalingBonus;
        public static ConfigEntry<float> ZoneLevelBonusPerLevel;
        public static ConfigEntry<int> ZoneKillsPerLevelUp;
        public static ConfigEntry<float> ZoneDecayLevelsPerHour;
        public static ConfigEntry<string> ZoneDecayClock;
        public static ConfigEntry<bool> EnableZoneMapOverlay;
        public static ConfigEntry<bool> ZoneOverlayAboveFog;
        public static ConfigEntry<float> MinZoneSize;
        public static ConfigEntry<float> MaxZoneSize;
        public static ConfigEntry<float> KillReportFlushIntervalSeconds;
        public static ConfigEntry<string> ZoneOverlayColorOptions;
        public static ConfigEntry<bool> ShowMinimapLevelIndicator;
        public static ConfigEntry<float> ZoneOverlayColorTransparency;
        public static ConfigEntry<bool> ShowQuickConfigureButton;

        public static ConfigEntry<float> ConfigPollIntervalSeconds;
        public static ConfigEntry<float> ConfigApplyDelay;

        public ValConfig(ConfigFile cf)
        {
            // ensure all the config values are created
            cfg = cf;
            // Configs are not written to disk until they are all bound - with SaveOnConfigSet
            // enabled, every individual Bind rewrites the entire cfg file. Binding everything in
            // memory and flushing once is a significant speedup in mod load time.
            cfg.SaveOnConfigSet = false;
            CreateConfigValues(cf);
            cfg.Save();
            cfg.SaveOnConfigSet = true;
            ConfigFileWatcher.Register(cfg.ConfigFilePath, OnMainConfigFileChanged);
        }

        public void SetupConfigRPCs() {
            ClientSendPlayerPrivateKeysRPC = NetworkManager.Instance.AddRPC("SLS_SendPlayerKeysRPC", OnServerReceivePlayerPrivateKeys, OnClientReceiveRequestForPrivateKeys);
            ClientStartRaidRPC = NetworkManager.Instance.AddRPC("SLS_ClientStartRaidRPC", OnServerReceiveConfigs, OnClientReceiveRaidStart);
            // Owning (raided) client confirms its raid actually started; server then sets the cooldown and broadcasts music.
            RaidCommittedRPC = NetworkManager.Instance.AddRPC("SLS_RaidCommittedRPC", OnServerReceiveRaidCommitted, NOOPReceive);
            ClientForcePlayMusicRPC = NetworkManager.Instance.AddRPC("SLS_ClientForcePlayMusicRPC", OnServerReceiveConfigs, OnClientReceiveForcePlayMusic);
            ClientClearNearbyEventsRPC = NetworkManager.Instance.AddRPC("SLS_ClientForceRemoveNearbyEventsRPC", OnServerReceiveConfigs, OnClientReceiveForceRemoveNearbyEvents);
            SendNewNemesisBossRPC = NetworkManager.Instance.AddRPC("SLS_SendNewNemesisBossRPC", OnServerReceivedNemesisBossAdd, OnClientReceiveMiniBossAdd);
            RemoveNemesisBossRPC = NetworkManager.Instance.AddRPC("SLS_RemoveNemesisBossRPC", OnServerReceiveNemesisBossRemove, OnClientReceiveMiniBossRemove);
            // Server -> client shared world map pins for remote Nemesis bosses. Add carries a list of pins
            // (used for both incremental updates and the initial full-set sync); remove carries a pin id.
            AddNemesisBossPinRPC = NetworkManager.Instance.AddRPC("SLS_AddNemesisBossPinRPC", OnServerReceiveConfigs, OnClientReceiveNemesisBossPinAdd);
            RemoveNemesisBossPinRPC = NetworkManager.Instance.AddRPC("SLS_RemoveNemesisBossPinRPC", OnServerReceiveConfigs, OnClientReceiveNemesisBossPinRemove);
            // Owner of a dying remote boss reports it to the server, which removes the registry entry + pin.
            ReportNemesisBossDeathRPC = NetworkManager.Instance.AddRPC("SLS_ReportNemesisBossDeathRPC", OnServerReceiveNemesisBossDeath, NOOPReceive);
            // Admin client asks the server to run a server-authoritative SLS console command, and the
            // server streams that command's output back to whoever asked. Both directions are needed
            // because a dedicated server has no Terminal of its own: Console.Awake and
            // Terminal.InitTerminal only ever run on a client, so these commands are otherwise
            // untypeable anywhere. Vanilla's own relay (remoteCommand -> ZNet.RPC_RemoteCommand) ends in
            // Console.instance.TryRunCommand and null-references headless, so it cannot be used here.
            ClientCommandRequestRPC = NetworkManager.Instance.AddRPC("SLS_ClientCommandRequestRPC", OnServerReceiveCommandRequest, NOOPReceive);
            CommandOutputRPC = NetworkManager.Instance.AddRPC("SLS_CommandOutputRPC", OnServerReceiveConfigs, OnClientReceiveCommandOutput);
            // Client -> server: a Location Reset API call, and the typed answer back. Deliberately not
            // carried on the command relay above: that speaks command names and lines of terminal text,
            // which cannot express a result a mod's callback can read.
            LocationApiRequestRPC = NetworkManager.Instance.AddRPC("SLS_LocationApiRequestRPC", modules.LocationReset.LocationResetNetwork.OnServerReceiveRequest, NOOPReceive);
            LocationApiResultRPC = NetworkManager.Instance.AddRPC("SLS_LocationApiResultRPC", OnServerReceiveConfigs, modules.LocationReset.LocationResetNetwork.OnClientReceiveResult);
            // Server -> a chosen client: instantiate + own the dormant remote-boss placeholder. A dedicated
            // server can't own/drive it itself, so it delegates instantiation to the nearest ready peer.
            ClientPlaceNemesisSpawnerRPC = NetworkManager.Instance.AddRPC("SLS_ClientPlaceNemesisSpawnerRPC", OnServerReceiveConfigs, OnClientReceivePlaceNemesisSpawner);
            // Owner peers report batched creature deaths to the server; server pushes zone level changes back.
            ZoneKillReportRPC = NetworkManager.Instance.AddRPC("SLS_ZoneKillReportRPC", OnServerReceiveZoneKills, NOOPReceive);
            ZoneLevelSyncRPC = NetworkManager.Instance.AddRPC("SLS_ZoneLevelSyncRPC", OnServerReceiveConfigs, ZoneScaleSystemData.OnClientReceiveZoneLevels);

            SynchronizationManager.Instance.AddInitialSynchronization(ClientSendPlayerPrivateKeysRPC, SendRequestForPrivateKeys);
            // Joining clients receive the current set of active remote-boss map pins.
            SynchronizationManager.Instance.AddInitialSynchronization(AddNemesisBossPinRPC, SendNemesisBossPins);
            // Give joining clients the current (non-default) zone levels for their overlay / level bonuses.
            SynchronizationManager.Instance.AddInitialSynchronization(ZoneLevelSyncRPC, ZoneScaleSystemData.SerializeLeveledZonesForSync);
        }

        private void CreateConfigValues(ConfigFile Config) {
            // Debugmode
            EnableDebugMode = Config.Bind("Client config", "EnableDebugMode", false,
                new ConfigDescription("Enables Debug logging.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugMode.SettingChanged += Logger.EnableDebugLogging;
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
            EnableDebugNemesisDetails = Config.Bind("Client config", "EnableDebugNemesisDetails", false,
                new ConfigDescription("Enables Detailed logging for the Nemesis system.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugRaidDetails = Config.Bind("Client config", "EnableDebugRaidDetails", false,
                new ConfigDescription("Enables Detailed logging for the Raid system.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugLocationResetDetails = Config.Bind("Client config", "EnableDebugLocationResetDetails", false,
                new ConfigDescription("Enables Detailed logging for the Location Reset system. Adds the background sweep's per-chunk lines to the BepInEx log, and expands every chunk record with a per-entry breakdown of what was skipped and why.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableLocationResetLog = Config.Bind("Client config", "EnableLocationResetLog", true,
                new ConfigDescription("Writes a record of every chunk the Location Reset system touches - its zone coordinates, world position, and what was and was not reset inside it - to SavedData/LocationResetLog.log.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableTerminalColors = Config.Bind("Client config", "EnableTerminalColors", true,
                new ConfigDescription("Colours StarLevelSystem console output by severity. Only affects what the console shows - the BepInEx log and the Location Reset chunk log are always written as plain text.",
                null,
                new ConfigurationManagerAttributes { }));
            ShowMinimapLevelIndicator = Config.Bind("Client config", "ShowMinimapLevelIndicator", true,
                new ConfigDescription("Show a small ring/zone level readout next to the minimap. Each section is hidden when its scaling system is disabled.",
                null,
                new ConfigurationManagerAttributes { }));
            ShowMinimapLevelIndicator.SettingChanged += MinimapLevelIndicator.OnShowIndicatorChanged;
            ShowQuickConfigureButton = Config.Bind("Client config", "ShowQuickConfigureButton", true,
                new ConfigDescription("Show the StarLevelSystem quick configuration button on the main menu and (for hosts/admins) the pause menu.",
                null,
                new ConfigurationManagerAttributes { }));
            ShowQuickConfigureButton.SettingChanged += QuickConfigureTool.OnShowButtonChanged;


            MaxLevel = BindServerConfig("LevelSystem", "MaxLevel", 20, "The Maximum number of stars that a creature can have.", false, 1, 200);
            MaxLevel.SettingChanged += UpdateLevelsOnChange.ModifyLoadedCreatureLevels;
            MaxBossLevel = BindServerConfig("LevelSystem", "MaxBossLevel", 10, "The Maximum number of stars that a boss creature can have.", false, 1, 200);
            OverLevelCreaturesGetRerolledOnLoad = BindServerConfig("LevelSystem", "OverlevedCreaturesGetRerolledOnLoad", true, "Rerolls creature levels which are above maximum defined level, when those creatures are loaded. This will automatically clean up over leveled creatures if you reduce the max level.");
            OverLevelTamesGetRerolledOnLoad = BindServerConfig("LevelSystem", "OverLevelTamesGetRerolledOnLoad", false, "Rerolls tamed creatures that have a level is above the maximum defined level. This includes biome specific level settings.");
            EnableCreatureScalingPerLevel = BindServerConfig("LevelSystem", "EnableCreatureScalingPerLevel", true, "Enables started creatures to get larger for each star");

            EnableDistanceLevelScalingBonus = BindServerConfig("LevelSystem", "EnableDistanceLevelScalingBonus", true, "Creatures further away from the center of the world have a higher chance to levelup, this is a bonus applied to existing creature/biome configuration.");
            EnableMapRingsForDistanceBonus = BindServerConfig("LevelSystem", "EnableMapRingsForDistanceBonus", true, "Enables map rings to show distance levels, this is a visual aid to help you see how far away from the center of the world you are.");
            EnableMapRingsForDistanceBonus.SettingChanged += DistanceScaleSystem.UpdateMapRingEnableSettingOnChange;
            MapRingsAboveFog = BindServerConfig("LevelSystem", "MapRingsAboveFog", false, "When enabled, distance map rings draw above the map fog so they are visible even in unexplored areas. When disabled, rings sit below the fog and only appear once an area has been explored.");
            MapRingsAboveFog.SettingChanged += DistanceScaleSystem.UpdateMapRingFogSettingOnChange;
            DistanceBonusIsFromStarterTemple = BindServerConfig("LevelSystem", "DistanceBonusIsFromStarterTemple", false, "When enabled the distance bonus is calculated from the starter temple instead of world center, typically this makes little difference. But can help ensure your starting area is more correctly calculated.");
            DistanceBonusIsFromStarterTemple.SettingChanged += DistanceScaleSystem.OnRingCenterChanged;
            // Location Reset's distance bands measure from the same centre, but run server-side where
            // the map-ring handler above bails out, so they need their own re-resolve + invalidation.
            DistanceBonusIsFromStarterTemple.SettingChanged += modules.LocationReset.ZoneRates.OnCenterChanged;
            DistanceRingColorOptions = BindServerConfig("LevelSystem", "DistanceRingColorOptions", "White,Blue,Teal,Green,Yellow,Purple,Orange,Pink,Purple,Red,Grey", "The colors that distance rings will use, if there are more rings than colors, the color pattern will be repeated. (Optional, use an HTML hex color starting with # to have a custom color.) Available options: Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink, Gray, Brown, Black");
            DistanceRingColorOptions.SettingChanged += DistanceScaleSystem.UpdateMapColorSettingsOnChange;
            MiniMapRingGeneratorUpdatesPerFrame = BindServerConfig("LevelSystem", "MiniMapRingGeneratorUpdatesPerFrame", 1000, "The number of ring points to calculate per frame when generating the minimap rings. Higher values make this go faster, but can get it killed or cause instability.", true);
            PerLevelScaleBonus = BindServerConfig("LevelSystem", "PerLevelScaleBonus", 0.10f, "The size a creature gains each star level. Negative values shrink creatures each star, down to MinimumCreatureScale.", true, -0.5f, 2f);
            PerLevelScaleBonus.SettingChanged += SizeModifications.StarLevelScaleChanged;
            MinimumCreatureScale = BindServerConfig("LevelSystem", "MinimumCreatureScale", 0.1f, "The smallest scale multiplier a creature can shrink to. Stops negative size-per-level values from producing zero-sized or inside-out creatures.", true, 0.01f, 1f);
            MinimumCreatureScale.SettingChanged += SizeModifications.StarLevelScaleChanged;
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
            MultiplayerEnemyMinDamageTaken = BindServerConfig("Multiplayer", "MultiplayerEnemyMinDamageTaken", 0.2f, "Minimum amount of damage that enemies can take from multiplayer scaling. 0.2 = 20%", advanced: true);
            MultiplayerScalingRequiredPlayersNearby = BindServerConfig("Multiplayer", "MultiplayerScalingRequiredPlayersNearby", 3, "The number of players in a local area required to cause monsters to gain bonus health and/or damage.", true, 1, 20);
            EnableMultiplayerEnemyHealthScaling = BindServerConfig("Multiplayer", "EnableMultiplayerEnemyHealthScaling", true, "Creatures gain more health when players are grouped up.");
            EnableMultiplayerEnemyDamageScaling = BindServerConfig("Multiplayer", "EnableMultiplayerEnemyDamageScaling", false, "Creatures gain more damage when players are grouped up.");
            
            ControlSpawnerLevels = BindServerConfig("LevelSystem", "ControlSpawnerLevels", true, "Overrides spawner levels to be controlled by SLS (this impacts all naturally spawning creatures)");
            ControlAbilitySpawnedCreatures = BindServerConfig("LevelSystem", "ControlAbilitySpawnedCreatures", true, "Forces creatures spawned from abilities to be controlled by SLS. This primarily impacts things such as the roots from Elder.");
            ControlBossSpawns = BindServerConfig("LevelSystem", "ControlBossSpawns", true, "Forces boss creatures to be controlled by SLS. Bosses will not get star levels if this is disabled.");
            ForceControlAllSpawns = BindServerConfig("LevelSystem", "ForceControlAllSpawns", false, "Forces all creatures to be controlled by SLS, this includes creatures spawned from player abilities and items. This will override creature levels, other mods must use the API to ensure their spawned creature levels are set.");
            //DistanceBonusMapsCanIncludeLowerLevels = BindServerConfig("LevelSystem", "DistanceBonusMapsCanIncludeLowerLevels", true, "When enabled makes the distance bonus configuration include the highest previously lower level defined keys, if they are not defined in the current level.");
            OffspringCanBeStrongerThanParents = BindServerConfig("LevelSystem", "OffspringCanBeStrongerThanParents", false, "When enabled, creatures that are bred can have higher levels than their parents. Otherwise, they will be capped at the highest parent level.");
            OffspringGainExtraLevelChance = BindServerConfig("LevelSystem", "OffspringGainExtraLevelChance", 0.05f, "When enabled, creatures that are bred have a chance to gain an extra level above their parents. Chance is based on this value, 0.1 = 10% chance.", false, 0f, 1f);
            OffspringCanBeInfertile = BindServerConfig("LevelSystem", "OffspringCanBeInfertile", false, "When enabled, creatures produced from breeding have a chance to be infertile.");
            OffspringChanceToBeInfertile = BindServerConfig("LevelSystem", "OffspringChanceToBeInfertile", 0.5f, "When enabled, the chance that a creature produced from breeding will be infertile.", true, 0f, 1f);

            PerLevelLootScale = BindServerConfig("LootSystem", "PerLevelLootScale", 1f, "The amount of additional loot that a creature provides per each star level", false, 0f, 4f);
            ChanceBaseChancePerLevel = BindServerConfig("LootSystem", "ChanceBaseChancePerLevel", 0.25f, "When using ChancePerLevel loot scaling, this is the base chance that any item will drop. Increased by the creatures level.", false, 0f, 1f);
            PerLevelLootChanceScale = BindServerConfig("LootSystem", "PerLevelLootChanceScale", 0.05f, "Under the PerLevel and ChancePerLevel loot styles: additional chance per level that a sub-100% loot drop will occur, added on top of any per-drop ChanceScaleFactor. Under ChancePerLevel it also grows vanilla drop-table chances. E.g. 0.05 = +5% drop chance per level. Has no effect on drops with a base Chance of 1.", false, 0f, 1f);
            LootDropCalculationType = BindServerConfig("LootSystem", "LootDropCalculationType", "PerLevel", "How loot amounts scale with level. PerLevel: amount scales linearly with level. Exponential: amount scales exponentially ((1 + PerLevelLootScale)^(level-1) for vanilla drop tables). ChancePerLevel: amounts scale linearly like PerLevel, and each sub-100% drop's chance to occur also grows with level (see PerLevelLootChanceScale).", LootStyles.AllowedLootFactors, false);
            LootStyles.ParseLootFactor();
            LootDropCalculationType.SettingChanged += LootStyles.LootFactorChanged;
            LootDropsPerTick = BindServerConfig("LootSystem", "LootDropsPerTick", 20, "The number of loot drops that are generated per tick, reducing this will reduce lag when massive amounts of loot is generated at once.", true, 1, 100);
            ScaleAllLootByLevel = BindServerConfig("LootSystem", "ScaleAllLootByLevel", false, "Enables scaling of all loot which does not normally scale per level. Typically this is just trophies.");
            LootEggsDropIncreaseStacks = BindServerConfig("LootSystem", "LootEggsDropIncreaseStacks", true, "This causes higher level chickens (and other egg producers) to drop MORE eggs instead of higher leveled ones.");
            EggLevelDeterminedByItemQuality = BindServerConfig("LootSystem", "EggLevelDeterminedByItemQuality", false, "When enabled, the level of egg grown creatures is determined by the eggs quality level. Otherwise the grown creature uses its default level configuration.");
            CreatureLootDropStacked = BindServerConfig("LootSystem", "CreatureLootDropStacked", true, "When enabled, character drops will be automatically stacked before dropping (significantly more performant).");
            TreeLootDropsStacked = BindServerConfig("LootSystem", "TreeLootDropsStacked", true, "When enabled, tree drops will be automatically stacked before dropping (significantly more performant).");
            RockLootDropsStacked = BindServerConfig("LootSystem", "RockLootDropsStacked", true, "When enabled, rock drops will be automatically stacked before dropping (significantly more performant).");
            MiscLootDropsStacked = BindServerConfig("LootSystem", "MiscLootDropsStacked", true, "When enabled, misc (such as small destructible skeletons etc) drops will be automatically stacked before dropping (significantly more performant).");

            UseVanillaRaidConfiguration = BindServerConfig("Raids", "UseVanillaRaidConfiguration", false, "Reverts to use vanilla raid configuration when enabled. Server authoritative: on a dedicated or player hosted server the server's value is synced to every client, so editing this in a client's own config file has no effect.");
            UseVanillaRaidConfiguration.SettingChanged += RaidControl.OnVanillaRaidModeChanged;
            RaidEventRate = BindServerConfig("Raids", "RaidEventRate", 1f, "The rate at which raid events occur (Vanilla is 1.0), higher values result in less frequent raids, lower values results in more frequent raids. This modifies the raid timing settings which are set per-raid.", false, 0.001f, 10f);
            MaxRaidAttemptsPerPlayer = BindServerConfig("Raids", "MaxRaidAttemptsPerPlayer", 5, "The Maximum number of times to try to activate a raid for a given player. The available raids will be shuffled each time before rolling their activation chance. With 10 raids defined the randomly selected first X will get a chance to spawn.", true, 0, 50);
            ServerTimeBetweenRaidStartChecks = BindServerConfig("Raids", "ServerTimeBetweenRaidStartChecks", 25, "Number of minutes between when the server will check to start raids (raids can still be on cooldown and will not be started).", true, 1, 120);
            MaxActiveRaids = BindServerConfig("Raids", "MaxActiveRaids", 10, "The maximum number of concurrent raids, automatically limited to 1 per player.");
            RaidWindDownSeconds = BindServerConfig("Raids", "RaidWindDownSeconds", 60, "Seconds after a raid ends during which its creatures move away and despawn naturally. 0 = no linger.", true, 0, 600);
            RaidForceDeleteStragglers = BindServerConfig("Raids", "RaidForceDeleteStragglers", true, "When enabled, any raid creatures still present at the end of RaidWindDownSeconds are force-deleted. When disabled, leftover creatures are left to wander off and despawn on their own.", advanced: true);
            EnableCustomRaidsCompat = BindServerConfig("Raids", "EnableCustomRaidsCompat", true, "When CustomRaids is installed and SLS raids are enabled, allow CustomRaids raids to fire alongside SLS raids. Has no effect if CustomRaids is not installed.", advanced: true);

            EnableNemesisSystem = BindServerConfig("Nemesis", "EnableNemesisSystem", true, "Enables the per-player Nemesis system that biases newly-spawning creature star levels based on a tracked player score.");
            EnableNemesisRemoteSpawning = BindServerConfig("Nemesis", "EnableNemesisRemoteSpawning", false, "Enables ambient, server-driven remote spawning of Nemesis minibosses across the world (a second, finer gate lives in NemesisSettings.yaml under RemoteSpawning.Enabled).");

            EnableLocationReset = BindServerConfig("LocationReset", "EnableLocationReset", false, "Master switch for the background Location Reset sweep, which restores looted locations, dungeons, ores, pickables and vegetation so they can be gathered again. Per-target opt-in lives in LocationResetSettings.yaml. Back up your world before enabling.");
            EnableLocationReset.SettingChanged += LocationResetControl.OnMasterSwitchChanged;
            ClientLocationResetMaxRadius = BindServerConfig("LocationReset", "ClientLocationResetMaxRadius", 256f, "Largest reset radius a connected client may ask for through the mod API. Requests above this are clamped, not refused. Client requests are not admin-gated, so this is one of the limits that bounds them.", true, 0f, 2048f);
            ClientLocationResetMaxDistance = BindServerConfig("LocationReset", "ClientLocationResetMaxDistance", 256f, "How far from their own position a client may ask for a reset, in metres. Stops a client resetting content on the far side of the world. 0 disables the check.", true, 0f, 8192f);
            ClientLocationResetCooldownSeconds = BindServerConfig("LocationReset", "ClientLocationResetCooldownSeconds", 30f, "Minimum seconds between reset or registration requests from any one client. Read-only queries are not affected. 0 disables the cooldown.", true, 0f, 3600f);
            LocationResetSweepBudgetMs = BindServerConfig("LocationReset", "LocationResetSweepBudgetMs", 4f, "Milliseconds of server frame time the reset sweep may consume per frame. This is the main throughput throttle: raise it to restore the world faster, lower it if the server is under strain. 0 uses the value from LocationResetSettings.yaml.", false, 0f, 33f);

            EnableZoneScalingBonus = BindServerConfig("ZoneScaling", "EnableZoneScalingBonus", true, "Divides the world into island-based zones. Zones gain levels from creature kills and apply bonus level-up chances to creatures that spawn inside them.");
            ZoneLevelBonusPerLevel = BindServerConfig("ZoneScaling", "ZoneLevelBonusPerLevel", 2.0f, "Bonus added to each level-up chance tier for each zone level above 1. E.g. 2.0 at zone level 3 adds +4 to every tier.", false, 0.1f, 50f);
            ZoneKillsPerLevelUp = BindServerConfig("ZoneScaling", "ZoneKillsPerLevelUp", 100, "Number of creature deaths in a zone required to raise that zone's level by 1.", false, 1, 10000);
            ZoneDecayLevelsPerHour = BindServerConfig("ZoneScaling", "ZoneDecayLevelsPerHour", 0.25f, "How many zone levels decay per hour. 0 disables decay entirely; lower values decay slower, higher values faster. Default 0.25 = one level lost every four hours. Whether that hour is wall-clock time or time actually spent in the world is set by ZoneDecayClock.", false, 0f, 50f);
            ZoneDecayClock = BindServerConfig("ZoneScaling", "ZoneDecayClock", ZoneDecayClockSource.RealTime.ToString(), "Which clock zone level decay is measured against. RealTime is the wall clock, so zones keep decaying while nobody is playing and a world left alone overnight comes back several levels lower. GameTime is the world's own time, which only advances while the world is actually being played, so nothing decays while you are logged out or while a dedicated server sits empty. Switching between them re-bases every zone's decay timer within 15 minutes; zone levels themselves are never lost by the switch.", new AcceptableValueList<string>(ZoneDecayClockSource.RealTime.ToString(), ZoneDecayClockSource.GameTime.ToString()));
            ZoneDecayClock.SettingChanged += ZoneScaleSystemData.OnDecayClockChanged;
            EnableZoneMapOverlay = BindServerConfig("ZoneScaling", "EnableZoneMapOverlay", true, "Draws zone boundaries on the minimap, colored by zone level.");
            ZoneOverlayAboveFog = BindServerConfig("ZoneScaling", "ZoneOverlayAboveFog", false, "When enabled, zone boundaries draw above the map fog so they are visible even in unexplored areas. When disabled, boundaries sit below the fog and only appear once an area has been explored.");
            ZoneOverlayAboveFog.SettingChanged += ZoneScaleSystem.UpdateZoneOverlayFogOnChange;
            MinZoneSize = BindServerConfig("ZoneScaling", "MinZoneSize", 1000f, "Minimum landmass size (meters, on both axes) for an island to be split into zones. Islands smaller than this get no zones. Changes apply when zones are rebuilt (sls-zone-rebuild).", false, 500f, 10000f);
            MaxZoneSize = BindServerConfig("ZoneScaling", "MaxZoneSize", 3000f, "Side length (meters) of each square zone cell. Land is tiled onto a global grid of this size so zones never overlap. Changes apply when zones are rebuilt (sls-zone-rebuild).", false, 1000f, 10000f);
            KillReportFlushIntervalSeconds = BindServerConfig("ZoneScaling", "KillReportFlushIntervalSeconds", 10f, "The number of seconds between update checks for zone kill counters.", true);
            ZoneOverlayColorOptions = BindServerConfig("ZoneScaling", "ZoneOverlayColorOptions", "Grey,White,LightYellow,Yellow,LightOrange,Orange,DarkOrange,LightRed,Red,DarkRed,LightPurple,Purple,DarkPurple", "The colors used for zone boundaries on the minimap, walked by zone level (higher levels step further along the list, wrapping if there are more levels than colors). (Optional, use an HTML hex color starting with # to have a custom color.) Available options: LightYellow, Yellow, LightOrange, Orange, DarkOrange, LightRed, Red, DarkRed, LightPurple, Purple, DarkPurple, Green, Teal, Blue, Pink, Gray, Brown, Black, White");
            ZoneOverlayColorOptions.SettingChanged += ZoneScaleSystem.UpdateZoneOverlayColorsOnChange;
            ZoneOverlayColorTransparency = BindServerConfig("ZoneScaling", "ZoneOverlayColorTransparency", 0.5f, "Transparency value of the color used for zone boundaries.", true, 0f, 1f);
            ZoneOverlayColorTransparency.SettingChanged += ZoneScaleSystem.UpdateZoneOverlayColorsOnChange;

            MaxMajorModifiersPerCreature = BindServerConfig("Modifiers", "MaxMajorModifiersPerCreature", 1, "The default number of major modifiers that a creature can have.");
            MaxMinorModifiersPerCreature = BindServerConfig("Modifiers", "MaxMinorModifiersPerCreature", 1, "The default number of minor modifiers that a creature can have.");
            LimitCreatureModifiersToCreatureStarLevel = BindServerConfig("Modifiers", "LimitCreatureModifiersToCreatureStarLevel", true, "Limits the number of modifiers that a creature can have based on its level.");
            LimitCreatureModifiersToCreatureStarLevel.SettingChanged += CreatureModifiersData.ModifierNamingChanged;
            ChanceMajorModifier = BindServerConfig("Modifiers", "ChanceMajorModifier", 0.15f, "The chance that a creature will have a major modifier (creatures can have BOTH major and minor modifiers).", false, 0, 1f);
            ChanceMajorModifier.SettingChanged += CreatureModifiersData.ClearProbabilityCaches;
            ChanceMinorModifier = BindServerConfig("Modifiers", "ChanceMinorModifier", 0.25f, "The chance that a creature will have a minor modifier (creatures can have BOTH major and minor modifiers).", false, 0, 1f);
            ChanceMinorModifier.SettingChanged += CreatureModifiersData.ClearProbabilityCaches;
            EnableBossModifiers = BindServerConfig("Modifiers", "EnableBossModifiers", true, "Bosses can spawn with modifiers.");
            ChanceOfBossModifier = BindServerConfig("Modifiers", "ChanceOfBossModifier", 0.75f, "The chance that a boss will have a modifier.", false, 0, 1f);
            ChanceOfBossModifier.SettingChanged += CreatureModifiersData.ClearProbabilityCaches;
            MaxBossModifiersPerBoss = BindServerConfig("Modifiers", "MaxBossModifiersPerBoss", 2, "The maximum number of modifiers that a boss can have.");
            SplittersInheritLevel = BindServerConfig("Modifiers", "SplittersInheritLevel", true, "Creatures spawned from the Splitter modifier inherit the level of the parent creature.");
            LimitCreatureModifierPrefixes = BindServerConfig("Modifiers", "LimitCreatureModifierPrefixes", 3, "Maximum number of prefix names to use when building a creatures name.");
            LimitCreatureModifierPrefixes.SettingChanged += CreatureModifiersData.ModifierNamingChanged;
            MinorModifiersFirstInName = BindServerConfig("Modifiers", "MinorModifiersFirstInName", false, "Enables or disables ordering of modifiers for naming. If enabled, minor modifiers will be sorted first eg: Fast Poisonous");
            MinorModifiersFirstInName.SettingChanged += CreatureModifiersData.ModifierNamingChanged;
            ModifierIconDisplayStyle = BindServerConfig("Modifiers", "ModifierIconDisplayStyle", ModifierDisplayStyle.Stars.ToString(), "Style to display modifiers as on the creature HUD. Icons = detailed modifier icons, Stars = star-shaped modifier icons, None = plain default stars.", new AcceptableValueList<string>(ModifierDisplayStyle.Icons.ToString(), ModifierDisplayStyle.Stars.ToString(), ModifierDisplayStyle.None.ToString()));
            CreatureModifiersData.ParseModifierDisplayStyle();
            ModifierIconDisplayStyle.SettingChanged += CreatureModifiersData.ModifierDisplayStyleChanged;
            EvolvingCanRollNewModifiers = BindServerConfig("Modifiers", "EvolvingCanRollNewModifiers", false, "When enabled, evolving creatures have a chance to gain new modifiers when they evolve.");
            EvolvingChanceToRollNewModifier = BindServerConfig("Modifiers", "EvolvingChanceToRollNewModifier", 0.15f, "Chance that an evolving creature will gain a new major, minor, or boss modifier (based on creature type), up to the configured modifier limit.", false, 0f, 1f);

            EnemyHealthbarScalarX = BindServerConfig("UI", "EnemyHealthbarScalarX", 1f, "The scale of the health bar for typical enemies. This does not impact bosses or players.", false, 0f, 4f);
            EnemyHealthbarScalarY = BindServerConfig("UI", "EnemyHealthbarScalarY", 1.75f, "The scale of the health bar for typical enemies. This does not impact bosses or players.", false, 0f, 4f);
            UseCustomHealthFont = Config.Bind("UI", "UseCustomHealthFont", false, "[Client side Config] Enable to use a custom version of the Norse font.");
            HealthDisplayFontSizeAdjustment = BindServerConfig("UI", "HealthDisplayFontSizeAdjustment", 0.8f, "Percentage modification for the font size on creature health.");
            EnableEnemyHealthbarNumberDisplay = BindServerConfig("UI", "EnableEnemyHealthbarNumberDisplay", false, "Enables a numerical display for enemy creatures health");
            StackMultipleBossHealthbars = BindServerConfig("UI", "StackMultipleBossHealthbars", true, "When more than one boss healthbar is shown, stack them vertically (one full bar per row). When disabled, the boss bars are squished horizontally so they sit side-by-side.");
            BossHealthbarSpacing = BindServerConfig("UI", "BossHealthbarSpacing", 30f, "Gap, in pixels, between boss healthbars (vertical gap when stacked, horizontal gap when squished).", true, 0f, 120f);
            BossHudTopBuffer = Config.Bind("UI", "BossHudTopBuffer", 120,
                new ConfigDescription("[Client side Config] Space, in 1080p-equivalent pixels, between the top of the screen and the boss health HUD.",
                new AcceptableValueRange<int>(0, 600)));
            BossHealthbarWidthPercent = Config.Bind("UI", "BossHealthbarWidthPercent", 0.4f,
                new ConfigDescription("[Client side Config] Boss health bar width as a fraction of the screen width.",
                new AcceptableValueRange<float>(0.1f, 1f)));
            StackMultipleBossHealthbars.SettingChanged += UIHudControl.OnBossHudConfigChanged;
            BossHealthbarSpacing.SettingChanged += UIHudControl.OnBossHudConfigChanged;
            BossHudTopBuffer.SettingChanged += UIHudControl.OnBossHudConfigChanged;
            BossHealthbarWidthPercent.SettingChanged += UIHudControl.OnBossHudConfigChanged;
            EnableJewelCraftingBossHudCompat = BindServerConfig("UI", "EnableJewelcraftingBossHudCompat", true, "When Jewelcrafting is installed, suppress its multi-boss HUD layout (which rescales the boss health bar every frame) so SLS controls the boss healthbars. Has no effect if Jewelcrafting is not installed.", advanced: true);


            NumberOfCacheUpdatesPerFrame = BindServerConfig("Misc", "NumberOfCacheUpdatesPerFrame", 10, "Number of cache updates to process when performing live updates", true, 1, 150);
            OutputColorizationGeneratorsData = BindServerConfig("Misc", "OutputColorizationGeneratorsData", false, "Writes out color generators to a debug file. This can be useful if you want to hand pick color settings from generated values.");
            InitialDelayBeforeSetup = BindServerConfig("Misc", "InitialDelayBeforeSetup", 0.5f, "The delay waited before a creature is setup, this is the delay that the person controlling the creature will wait before setup. Higher values will delay setup.");
            FallbackDelayBeforeCreatureSetup = BindServerConfig("Misc", "FallbackDelayBeforeCreatureSetup", 5, "The number of seconds non-owned creatures we will waited on before loading their modified attributes. This is a fallback setup.");
            ConfigPollIntervalSeconds = BindServerConfig("Misc", "ConfigPollIntervalSeconds", 30f, "The number of seconds between checks for changes in the yaml config files.", true, 1f, 300f);
            // Read by ConfigChangeDebouncer. Most editors save by truncating and then writing, which the
            // watcher sees as two separate changes; this collapses them into one reload.
            ConfigApplyDelay = BindServerConfig("Misc", "ConfigApplyDelay", 1f, "Delay in seconds before a changed yaml config file is applied. Coalesces a burst of rapid edits into a single apply. Set to 0 to apply instantly.", true, 0f, 10f);


            OnlyControlVanillaAreaSpawners = BindServerConfig("ModCompat", "OnlyControlVanillaAreaSpawners", true, "When enabled, will only control the spawned level from an AreaSpawner if it is a vanilla one.");
            OverrideCreatureModifiedHealth = BindServerConfig("ModCompat", "OverrideCreatureModifiedHealth", false, "When enabled, will always set creatures health based on the SLS settings for the creature. This overrides other mods changes to creatures.");
        }

        // --- Empty-config fallback helpers ---

        private static void OnMainConfigFileChanged(string _) {
            // Apply in the main menu too (ZNet not up yet): the watcher deliberately keeps polling
            // there, but requiring a live server connection meant menu-time hand-edits were never
            // picked up. A connected pure client still defers to the server-synced values.
            if (ZNet.instance != null && ZNet.instance.IsServer() == false) {
                return;
            }
            Logger.LogInfo("Configuration file has been changed, reloading settings.");
            cfg.Reload();
        }

        private static ZPackage SendRequestForPrivateKeys() {
           ZPackage package = new ZPackage();
            return package;
        }

        public static IEnumerator OnServerReceiveConfigs(long sender, ZPackage package) {
            Logger.LogDebug("Server received config from client, rejecting due to being the server.");
            yield return null;
        }

        public static IEnumerator OnServerReceivedNemesisBossAdd(long sender, ZPackage package) {
            ApplyNemesisBossAdd(package.ReadString(), sender);
            yield return null;
        }

        public static IEnumerator OnServerReceiveNemesisBossRemove(long sender, ZPackage package) {
            ApplyNemesisBossRemove(package.ReadString(), sender);
            yield return null;
        }

        // Authoritatively add a nemesis miniboss to the shared pool, persist it, and propagate it
        // to every peer except senderToExclude. Called both from the server RPC handler (excluding
        // the originating client) and directly on a host/listen-server, where GetServerPeer() is
        // null so there is no server peer to send an RPC to. Pass ZNet.GetUID() to exclude nobody.
        internal static void ApplyNemesisBossAdd(string yaml, long senderToExclude) {
            NemesisMiniboss nemesisBoss = DataObjects.yamlDeserializer.Deserialize<NemesisMiniboss>(yaml);
            NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.Add(nemesisBoss);
            // Through the config manager: a bare File.WriteAllText dropped the documented header and left
            // the watcher stamp stale, so the change reached peers only by accident on the next poll.
            YamlConfigManager.WriteCurrentToDisk(YamlConfigManager.NemesisSettings);
            if (ZNet.instance == null) { return; }
            // Send the update to all of the other clients via the correct nemesis-add channel
            // (OnClientReceiveMiniBossAdd), not the private-keys RPC.
            ZPackage package = new ZPackage();
            package.Write(yaml);
            ZNet.instance.GetPeers().ForEach(peer => {
                if (peer.m_uid != senderToExclude) {
                    SendNewNemesisBossRPC.SendPackage(peer.m_uid, package);
                }
            });
        }

        // Authoritatively remove a nemesis miniboss from the shared pool, persist it, and propagate
        // the removal to every peer except senderToExclude. See ApplyNemesisBossAdd for the host case.
        internal static void ApplyNemesisBossRemove(string yaml, long senderToExclude) {
            int idx = FindMinibossIndex(yaml);
            if (idx >= 0) {
                NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.RemoveAt(idx);
                // Through the config manager: a bare File.WriteAllText dropped the documented header and left
            // the watcher stamp stale, so the change reached peers only by accident on the next poll.
            YamlConfigManager.WriteCurrentToDisk(YamlConfigManager.NemesisSettings);
            }
            if (ZNet.instance == null) { return; }
            // Forward the removal to peers even if we had already removed it locally, so their pools converge.
            ZPackage package = new ZPackage();
            package.Write(yaml);
            ZNet.instance.GetPeers().ForEach(peer => {
                if (peer.m_uid != senderToExclude) {
                    RemoveNemesisBossRPC.SendPackage(peer.m_uid, package);
                }
            });
        }

        // Locate a miniboss in the shared pool matching the given serialized boss. NemesisMiniboss has
        // no value-equality override, so a freshly deserialized copy never matches a stored instance by
        // reference — matching by the serialized form is required. Both sides are normalized through the
        // same deserialize→serialize round-trip so equal bosses compare equal regardless of incidental
        // formatting. Returns the index of the first match, or -1 if none / the yaml is unparseable.
        // Serialized-form memo for pool entries: without it every FindMinibossIndex call re-serialized
        // the WHOLE pool (N YAML serializations per miniboss add/remove, fanned out to every peer).
        // Pool entries are never mutated once added, so the form can be computed once per instance;
        // the weak table lets dropped entries be collected normally.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NemesisMiniboss, string> minibossYamlCache = new System.Runtime.CompilerServices.ConditionalWeakTable<NemesisMiniboss, string>();

        private static string SerializedMiniboss(NemesisMiniboss boss) {
            if (minibossYamlCache.TryGetValue(boss, out string cached)) { return cached; }
            string serialized = DataObjects.yamlSerializer.Serialize(boss);
            minibossYamlCache.Add(boss, serialized);
            return serialized;
        }

        private static int FindMinibossIndex(string yaml) {
            string target;
            try { target = DataObjects.yamlSerializer.Serialize(DataObjects.yamlDeserializer.Deserialize<NemesisMiniboss>(yaml)); }
            catch { return -1; }
            return NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses
                .FindIndex(b => SerializedMiniboss(b) == target);
        }

        // Initial-sync payload: the current set of active remote-boss map pins, derived by the server
        // from live boss/spawner ZDOs. Returns an empty list off-server.
        private static ZPackage SendNemesisBossPins() {
            ZPackage package = new ZPackage();
            List<NemesisBossPin> pins = null;
            if (ZNet.instance != null && ZNet.instance.IsServer()) {
                pins = global::StarLevelSystem.modules.NemesisSystem.NemesisRemoteSpawnControl.GetActiveBossPins();
            }
            package.Write(DataObjects.yamlSerializer.Serialize(pins ?? new List<NemesisBossPin>()));
            return package;
        }

        private static IEnumerator OnClientReceiveNemesisBossPinAdd(long sender, ZPackage package) {
            string yaml = package.ReadString();
            List<NemesisBossPin> pins = null;
            try { pins = DataObjects.yamlDeserializer.Deserialize<List<NemesisBossPin>>(yaml); }
            catch (Exception ex) { Logger.LogWarning($"Failed to parse Nemesis boss pin add: {ex.Message}"); }
            if (pins != null) {
                foreach (NemesisBossPin pin in pins) {
                    global::StarLevelSystem.modules.NemesisSystem.NemesisMinimap.AddOrUpdatePin(pin);
                }
            }
            yield return null;
        }

        private static IEnumerator OnClientReceiveNemesisBossPinRemove(long sender, ZPackage package) {
            string id = package.ReadString();
            global::StarLevelSystem.modules.NemesisSystem.NemesisMinimap.RemovePin(id);
            yield return null;
        }

        // Server handler: an admin client asked to run a server-authoritative SLS console command. These
        // commands read or mutate world state only the server owns, and on a dedicated server there is
        // no console to type them into, so the request is routed here. Gate on admin because any peer
        // could craft this RPC; the client-side check is only there for a clearer message.
        public static IEnumerator OnServerReceiveCommandRequest(long sender, ZPackage package) {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { yield break; }

            string command = package.ReadString();
            if (SenderIsAdmin(sender) == false) {
                Logger.LogWarning($"Rejecting '{command}' from non-admin peer {sender}.");
                // Answer rather than going quiet, so the sender sees a refusal instead of nothing.
                TerminalOutput refusal = TerminalOutput.Remote(sender);
                refusal.Error($"Only server admins can run {command}.", log: false);
                refusal.Flush();
                yield break;
            }

            int argCount = package.ReadInt();
            string[] args = new string[argCount];
            for (int i = 0; i < argCount; i++) { args[i] = package.ReadString(); }
            bool hasCenter = package.ReadBool();
            Vector3 center = package.ReadVector3();

            TerminalManager.ExecuteFromNetwork(command, args, center, hasCenter, TerminalOutput.Remote(sender));
            yield return null;
        }

        // Client handler: a batch of output lines from a command this client asked the server to run.
        // Severity travels as a byte and the colour is applied here, so the server's log and chunk log
        // never contain markup and each client honours its own EnableTerminalColors setting.
        private static IEnumerator OnClientReceiveCommandOutput(long sender, ZPackage package) {
            int count = package.ReadInt();
            for (int i = 0; i < count; i++) {
                OutputLevel level = (OutputLevel)package.ReadByte();
                TerminalManager.PrintResponse(level, package.ReadString());
            }
            yield return null;
        }

        // True when the given peer uid belongs to a connected admin. Used to authorize client-issued
        // server-side actions; the integrated host itself never routes through an RPC so is not considered here.
        private static bool SenderIsAdmin(long sender) {
            ZNetPeer peer = ZNet.instance?.GetPeer(sender);
            if (peer == null || peer.m_socket == null) { return false; }
            return ZNet.instance.IsAdmin(peer.m_socket.GetHostName());
        }

        // Server handler: the owner of a dying remote boss reported its pin id; drop it and broadcast removal.
        public static IEnumerator OnServerReceiveNemesisBossDeath(long sender, ZPackage package) {
            if (ZNet.instance != null && ZNet.instance.IsServer()) {
                string pinId = package.ReadString();
                global::StarLevelSystem.modules.NemesisSystem.NemesisRemoteSpawnControl.RemoveActiveBoss(pinId);
            }
            yield return null;
        }

        // Client handler: the server asked this client to instantiate + own the dormant remote-boss
        // placeholder (dedicated servers can't own/drive it themselves). The client owns the resulting ZDO,
        // so its NemesisRemoteSpawner.Update drives the spawn once a player reaches the pinned location.
        private static IEnumerator OnClientReceivePlaceNemesisSpawner(long sender, ZPackage package) {
            string yaml = package.ReadString();
            global::StarLevelSystem.modules.NemesisSystem.NemesisRemoteSpawnControl.InstantiateSpawnerFromRequest(yaml);
            yield return null;
        }

        // Server-side: broadcast a pin add to every peer and apply it locally (integrated host).
        internal static void BroadcastNemesisBossPinAdd(NemesisBossPin pin) {
            if (pin == null || ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            ZPackage package = new ZPackage();
            package.Write(DataObjects.yamlSerializer.Serialize(new List<NemesisBossPin>() { pin }));
            ZNet.instance.GetPeers().ForEach(peer => AddNemesisBossPinRPC.SendPackage(peer.m_uid, package));
            global::StarLevelSystem.modules.NemesisSystem.NemesisMinimap.AddOrUpdatePin(pin);
        }

        // Server-side: broadcast a pin removal to every peer and apply it locally (integrated host).
        internal static void BroadcastNemesisBossPinRemove(string id) {
            if (string.IsNullOrEmpty(id) || ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            ZPackage package = new ZPackage();
            package.Write(id);
            ZNet.instance.GetPeers().ForEach(peer => RemoveNemesisBossPinRPC.SendPackage(peer.m_uid, package));
            global::StarLevelSystem.modules.NemesisSystem.NemesisMinimap.RemovePin(id);
        }

        internal static IEnumerator NOOPReceive(long sender, ZPackage package) {
            yield break;
        }

        // Server handler for ZoneKillReportRPC: a remote client reported a batch of death positions.
        internal static IEnumerator OnServerReceiveZoneKills(long sender, ZPackage package) {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { yield break; }
            List<SerializableVector3> deaths = DataObjects.yamlDeserializer.Deserialize<List<SerializableVector3>>(package.ReadString());
            if (deaths == null) { yield break; }
            ZoneScaleSystemData.ApplyDeaths(deaths);
            yield return null;
        }

        private static IEnumerator OnClientReceiveMiniBossAdd(long sender, ZPackage package) {
            var yaml = package.ReadString();
            // Dedupe by serialized form (reference-equality Contains never matches a deserialized copy).
            if (FindMinibossIndex(yaml) < 0) {
                NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.Add(DataObjects.yamlDeserializer.Deserialize<NemesisMiniboss>(yaml));
            }
            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientReceiveMiniBossRemove(long sender, ZPackage package) {
            // Remove by serialized form (reference-equality Remove never matches a deserialized copy).
            int idx = FindMinibossIndex(package.ReadString());
            if (idx >= 0) {
                NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.RemoveAt(idx);
            }
            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientReceiveRaidStart(long sender, ZPackage package) {
            var yaml = package.ReadString();
            NetworkRaidRequest raidNetRequest = DataObjects.yamlDeserializer.Deserialize<NetworkRaidRequest>(yaml);
            Vector3 raidPosition = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            if (raidNetRequest.RaidPostion != Vector3.zero) {
                raidPosition = raidNetRequest.RaidPostion;
            }

            RaidControl.StartRaidRunner(raidNetRequest.Raid, raidPosition);

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        // The owning client tells the server its raid actually started (passed spawn-point validation and showed
        // its start message). Only now do we commit the full cooldown and broadcast combat music to the area —
        // a raid that aborts before this never reaches here, so it leaves no trace.
        private static IEnumerator OnServerReceiveRaidCommitted(long sender, ZPackage package) {
            string raidName = package.ReadString();
            Vector3 pos = new Vector3(package.ReadSingle(), package.ReadSingle(), package.ReadSingle());
            string playerPlatformID = SLSExtensions.GetPlatformUserID(sender).ToString();
            RaidControl.FinalizeRaidCommit(playerPlatformID, raidName, pos);
            yield break;
        }

        private static IEnumerator OnClientReceiveForcePlayMusic(long sender, ZPackage package) {
            string musicName = package.ReadString();
            if (Enum.TryParse<Music>(musicName, out Music music) == false) {
                Logger.LogWarning($"Music {musicName} not found.");
                yield break;
            }

            MusicMan.instance.TriggerMusic(music.ToString());

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientReceiveForceRemoveNearbyEvents(long sender, ZPackage package) {
            RaidControl.RemoveNearbyRunningEvents();
            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        internal static IEnumerator OnClientReceiveRequestForPrivateKeys(long sender, ZPackage _) {
            if (Player.m_localPlayer == null) { yield break; }
            //Logger.LogDebug("Collecting players private keys");
            List<string> playerKeys = Player.m_localPlayer.GetPrivateKeysSanitize() ?? new List<string>();
            // A player with no keys still has to answer: the server registers players by this response, and
            // staying silent used to leave it waiting on that peer forever, holding up raid checks.
            if (playerKeys.Count <= 0) {
                Logger.LogDebug($"No private keys held by player: {Player.m_localPlayer.m_name}, registering them with an empty key set.");
            }
            string fileContents = DataObjects.yamlSerializerJsonCompat.Serialize(playerKeys);
            ZPackage package = new ZPackage();
            package.Write(fileContents);

            if (ZNet.instance.GetServerPeer() != null && ZNet.instance.IsCurrentServerDedicated()) {
                Logger.LogDebug($"Sending private keys to server: {fileContents}");
                ClientSendPlayerPrivateKeysRPC.SendPackage(ZNet.instance.GetServerPeer().m_uid, package);
            } else {
                // This is to handle integrated servers (single player) where the server is the same as the client
                Logger.LogDebug($"Updating server with private keys: {fileContents}");
                string PlatformAndID = SLSExtensions.GetLocalUserPlatformAndID();
                if (string.IsNullOrEmpty(PlatformAndID)) {
                    Logger.LogWarning("Could not update player private keys. Players platform was not detected.");
                    yield break;
                }
                RaidControl.UpdateOrAddPlayerPrivateKeys(PlatformAndID, playerKeys);
            }
        }

        private static IEnumerator OnServerReceivePlayerPrivateKeys(long sender, ZPackage package) {
            var yaml = package.ReadString();
            List<string> playerKeys = DataObjects.yamlDeserializer.Deserialize<List<string>>(yaml);
            RaidControl.UpdateOrAddPlayerPrivateKeys(sender, playerKeys);
            yield break;
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
        /// <param name="category"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="acceptableValues"></param>>
        /// <param name="advanced"></param>
        /// <returns></returns>
        public static ConfigEntry<bool> BindServerConfig(string category, string key, bool value, string description, AcceptableValueBase acceptableValues = null, bool advanced = false) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for int types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="category"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valMin"></param>
        /// <param name="valMax"></param>
        /// <returns></returns>
        public static ConfigEntry<int> BindServerConfig(string category, string key, int value, string description, bool advanced = false, int valMin = 0, int valMax = 150) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<int>(valMin, valMax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for float types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="category"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valMin"></param>
        /// <param name="valMax"></param>
        /// <returns></returns>
        public static ConfigEntry<float> BindServerConfig(string category, string key, float value, string description, bool advanced = false, float valMin = 0, float valMax = 150) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<float>(valMin, valMax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for strings
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="category"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <returns></returns>
        public static ConfigEntry<string> BindServerConfig(string category, string key, string value, string description, AcceptableValueList<string> acceptableValues = null, bool advanced = false) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(
                    description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }
    }
}
