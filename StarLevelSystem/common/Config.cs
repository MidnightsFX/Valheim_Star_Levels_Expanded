using BepInEx.Configuration;
using System.IO;
using System;
using BepInEx;
using StarLevelSystem.modules;
using Jotunn.Managers;
using Jotunn.Entities;
using System.Collections;
using StarLevelSystem.Data;

namespace StarLevelSystem
{
    internal class ValConfig
    {
        public static ConfigFile cfg;
        internal static String levelsFilePath = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "LevelSettings.yaml");
        internal static String colorsFilePath = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "ColorSettings.yaml");
        internal static String creatureLootFilePath = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "CreatureLootSettings.yaml");
        internal static String creatureModifierFilePath = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "Modifiers.yaml");

        private static CustomRPC LevelSettingsRPC;
        private static CustomRPC ColorSettingsRPC;
        private static CustomRPC CreatureLootSettingsRPC;
        private static CustomRPC ModifiersRPC;

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
        public static ConfigEntry<int> LootDropsPerTick;
        public static ConfigEntry<string> LootDropCalculationType;
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

        public static ConfigEntry<float> PerLevelTreeLootScale;
        public static ConfigEntry<float> PerLevelBirdLootScale;
        public static ConfigEntry<float> PerLevelMineRockLootScale;

        public static ConfigEntry<int> FishMaxLevel;
        public static ConfigEntry<int> BirdMaxLevel;
        public static ConfigEntry<int> TreeMaxLevel;
        public static ConfigEntry<int> RockMaxLevel;

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

        public ValConfig(ConfigFile cf)
        {
            // ensure all the config values are created
            cfg = cf;
            cfg.SaveOnConfigSet = true;
            CreateConfigValues(cf);
        }

        public void SetupConfigRPCs() {
            LevelSettingsRPC = NetworkManager.Instance.AddRPC("SLS_LevelsRPC", OnServerRecieveConfigs, OnClientReceiveLevelConfigs);
            ColorSettingsRPC = NetworkManager.Instance.AddRPC("SLS_ColorsRPC", OnServerRecieveConfigs, OnClientReceiveColorConfigs);
            CreatureLootSettingsRPC = NetworkManager.Instance.AddRPC("SLS_CreatureLootRPC", OnServerRecieveConfigs, OnClientReceiveCreatureLootConfigs);
            ModifiersRPC = NetworkManager.Instance.AddRPC("SLS_ModifiersRPC", OnServerRecieveConfigs, OnClientReceiveModifiersConfigs);

            SynchronizationManager.Instance.AddInitialSynchronization(LevelSettingsRPC, SendLevelsConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(ColorSettingsRPC, SendColorsConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(CreatureLootSettingsRPC, SendCreatureLootConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(ModifiersRPC, SendModifierConfigs);
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


            MaxLevel = BindServerConfig("LevelSystem", "MaxLevel", 20, "The Maximum number of stars that a creature can have", false, 1, 200);
            MaxLevel.SettingChanged += LevelSystem.ModifyLoadedCreatureLevels;
            OverlevedCreaturesGetRerolledOnLoad = BindServerConfig("LevelSystem", "OverlevedCreaturesGetRerolledOnLoad", true, "Rerolls creature levels which are above maximum defined level, when those creatures are loaded. This will automatically clean up overleveled creatures if you reduce the max level.");
            EnableCreatureScalingPerLevel = BindServerConfig("LevelSystem", "EnableCreatureScalingPerLevel", true, "Enables started creatures to get larger for each star");

            EnableDistanceLevelScalingBonus = BindServerConfig("LevelSystem", "EnableDistanceLevelScalingBonus", true, "Creatures further away from the center of the world have a higher chance to levelup, this is a bonus applied to existing creature/biome configuration.");
            EnableMapRingsForDistanceBonus = BindServerConfig("LevelSystem", "EnableMapRingsForDistanceBonus", true, "Enables map rings to show distance levels, this is a visual aid to help you see how far away from the center of the world you are.");
            EnableMapRingsForDistanceBonus.SettingChanged += LevelSystem.UpdateMapRingEnableSettingOnChange;
            DistanceBonusIsFromStarterTemple = BindServerConfig("LevelSystem", "DistanceBonusIsFromStarterTemple", false, "When enabled the distance bonus is calculated from the starter temple instead of world center, typically this makes little difference. But can help ensure your starting area is more correctly calculated.");
            DistanceBonusIsFromStarterTemple.SettingChanged += LevelSystem.OnRingCenterChanged;
            DistanceRingColorOptions = BindServerConfig("LevelSystem", "DistanceRingColorOptions", "White,Blue,Teal,Green,Yellow,Purple,Orange,Pink,Purple,Red,Grey", "The colors that distance rings will use, if there are more rings than colors, the color pattern will be repeated. (Optional, use an HTML hex color starting with # to have a custom color.) Available options: Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink, Gray, Brown, Black");
            DistanceRingColorOptions.SettingChanged += LevelSystem.UpdateMapColorSettingsOnChange;
            MiniMapRingGeneratorUpdatesPerFrame = BindServerConfig("LevelSystem", "MiniMapRingGeneratorUpdatesPerFrame", 1000, "The number of ring points to calculate per frame when generating the minimap rings. Higher values make this go faster, but can get it killed or cause instability.", true);
            PerLevelScaleBonus = BindServerConfig("LevelSystem", "PerLevelScaleBonus", 0.10f, "The additional size that a creature grows each star level.", true, 0f, 2f);
            PerLevelScaleBonus.SettingChanged += Colorization.StarLevelScaleChanged;
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
            
            EnableScalingBirds = BindServerConfig("ObjectLevels", "EnableScalingBirds", true, "Enables birds to scale with the level system. This will cause them to become larger and give more drops.");
            EnableScalingBirds.SettingChanged += LevelSystem.UpdateBirdSizeOnConfigChange;
            BirdSizeScalePerLevel = BindServerConfig("ObjectLevels", "BirdSizeScalePerLevel", 0.1f, "The amount of size that birds gain per level. 0.1 = 10% larger per level.", true, 0f, 2f);
            BirdSizeScalePerLevel.SettingChanged += LevelSystem.UpdateBirdSizeOnConfigChange;
            EnableScalingFish = BindServerConfig("ObjectLevels", "EnableScalingFish", true, "Enables star scaling for fish. This does potentially allow huge fish.");
            EnableScalingFish.SettingChanged += LevelSystem.UpdateFishSizeOnConfigChange;
            EnableRockLevels = BindServerConfig("ObjectLevels", "EnableRockLevels", false, "Enables level scaling for rocks.");
            FishMaxLevel = BindServerConfig("ObjectLevels", "FishMaxLevel", 20, "Sets the max level that fish can scale up to.", true, 1, 150);
            BirdMaxLevel = BindServerConfig("ObjectLevels", "BirdMaxLevel", 10, "Sets the max level that birds can scale up to.", true, 1, 150);
            TreeMaxLevel = BindServerConfig("ObjectLevels", "TreeMaxLevel", 10, "Sets the max level that trees can scale up to.", true, 1, 150);
            RockMaxLevel = BindServerConfig("ObjectLevels", "RockMaxLevel", 10, "Sets the max level that rocks can scale up to.", true, 1, 150);
            FishSizeScalePerLevel = BindServerConfig("ObjectLevels", "FishSizeScalePerLevel", 0.1f, "The amount of size that fish gain per level 0.1 = 10% larger per level.");
            FishSizeScalePerLevel.SettingChanged += LevelSystem.UpdateFishSizeOnConfigChange;
            EnableTreeScaling = BindServerConfig("ObjectLevels", "EnableTreeScaling", true, "Enables level scaling of trees. Make the trees bigger than reasonable? sure why not.");
            EnableTreeScaling.SettingChanged += LevelSystem.UpdateTreeSizeOnConfigChange;
            UseDeterministicTreeScaling = BindServerConfig("ObjectLevels", "UseDeterministicTreeScaling", true, "Scales the level of trees based on biome and distance from the center/spawn. This does not randomize tree levels, but reduces network usage.");
            TreeSizeScalePerLevel = BindServerConfig("ObjectLevels", "TreeSizeScalePerLevel", 0.1f, "The amount of size that trees gain per level 0.1 = 10% larger per level.");
            TreeSizeScalePerLevel.SettingChanged += LevelSystem.UpdateTreeSizeOnConfigChange;
            PerLevelTreeLootScale = BindServerConfig("ObjectLevels", "PerLevelTreeLootScale", 0.2f, "The amount of additional wood that each level grants for a tree.", true);
            PerLevelBirdLootScale = BindServerConfig("ObjectLevels", "PerLevelBirdLootScale", 0.3f, "Per level additional loot that birds gain.", true);
            PerLevelMineRockLootScale = BindServerConfig("ObjectLevels", "PerLevelMineRockLootScale", 0.2f, "The amount of additional stones and ores that each level grants for a rock", true);

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
            SpawnsAlwaysControlled.SettingChanged += ModificationExtensionSystem.LeveledCreatureListChanged;
            ModificationExtensionSystem.SetupForceLeveledCreatureList();

            PerLevelLootScale = BindServerConfig("LootSystem", "PerLevelLootScale", 1f, "The amount of additional loot that a creature provides per each star level", false, 0f, 4f);
            LootDropCalculationType = BindServerConfig("LootSystem", "LootDropCaluationType", "PerLevel", "The type of loot calculation to use. Per Level ", LootLevelsExpanded.AllowedLootFactors, false);
            LootDropCalculationType.SettingChanged += LootLevelsExpanded.LootFactorChanged;
            LootDropsPerTick = BindServerConfig("LootSystem", "LootDropsPerTick", 20, "The number of loot drops that are generated per tick, reducing this will reduce lag when massive amounts of loot is generated at once.", true, 1, 100);

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

            NumberOfCacheUpdatesPerFrame = BindServerConfig("Misc", "NumberOfCacheUpdatesPerFrame", 10, "Number of cache updates to process when performing live updates", true, 1, 150);
            OutputColorizationGeneratorsData = BindServerConfig("Misc", "OutputColorizationGeneratorsData", false, "Writes out color generators to a debug file. This can be useful if you want to hand pick color settings from generated values.");
            InitialDelayBeforeSetup = BindServerConfig("Misc", "InitialDelayBeforeSetup", 0.5f, "The delay waited before a creature is setup, this is the delay that the person controlling the creature will wait before setup. Higher values will delay setup.");
            FallbackDelayBeforeCreatureSetup = BindServerConfig("Misc", "FallbackDelayBeforeCreatureSetup", 5, "The number of seconds non-owned creatures we will waited on before loading their modified attributes. This is a fallback setup.");
            
        }

        internal void LoadYamlConfigs()
        {
            string externalConfigFolder = ValConfig.GetSecondaryConfigDirectoryPath();
            string[] presentFiles = Directory.GetFiles(externalConfigFolder);
            bool foundLevelsFile = false;
            bool foundColorFile = false;
            bool foundLootFile = false;
            bool foundModifierFile = false;

            foreach (string configFile in presentFiles) {
                if (configFile.Contains("LevelSettings.yaml"))
                {
                    Logger.LogDebug($"Found level configuration: {configFile}");
                    levelsFilePath = configFile;
                    foundLevelsFile = true;
                }

                if (configFile.Contains("ColorSettings.yaml"))
                {
                    Logger.LogDebug($"Found color configuration: {configFile}");
                    colorsFilePath = configFile;
                    foundColorFile = true;
                }

                if (configFile.Contains("LootSettings.yaml"))
                {
                    Logger.LogDebug($"Found loot configuration: {configFile}");
                    creatureLootFilePath = configFile;
                    foundLootFile = true;
                }
                if (configFile.Contains("Modifiers.yaml"))
                {
                    Logger.LogDebug($"Found modifier configuration: {configFile}");
                    creatureModifierFilePath = configFile;
                    foundModifierFile = true;
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

            SetupFileWatcher("ColorSettings.yaml");
            SetupFileWatcher("LevelSettings.yaml");
            SetupFileWatcher("Modifiers.yaml");
            SetupFileWatcher("CreatureLootSettings.yaml");
        }

        private void SetupFileWatcher(string filtername)
        {
            FileSystemWatcher fw = new FileSystemWatcher();
            fw.Path = ValConfig.GetSecondaryConfigDirectoryPath();
            fw.NotifyFilter = NotifyFilters.LastWrite;
            fw.Filter = filtername;
            fw.Changed += new FileSystemEventHandler(UpdateConfigFileOnChange);
            fw.Created += new FileSystemEventHandler(UpdateConfigFileOnChange);
            fw.Renamed += new RenamedEventHandler(UpdateConfigFileOnChange);
            fw.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fw.EnableRaisingEvents = true;
        }

        private static void UpdateConfigFileOnChange(object sender, FileSystemEventArgs e) {
            if (SynchronizationManager.Instance.PlayerIsAdmin == false) {
                Logger.LogInfo("Player is not an admin, and not allowed to change local configuration. Ignoring.");
                return;
            }
            if (!File.Exists(e.FullPath)) { return; }

            string filetext = File.ReadAllText(e.FullPath);
            var fileInfo = new FileInfo(e.FullPath);
            Logger.LogDebug($"Filewatch changes from: ({fileInfo.Name}) {fileInfo.FullName}");
            switch (fileInfo.Name) {
                case "ColorSettings.yaml":
                    Logger.LogDebug("Triggering Color Settings update.");
                    Colorization.UpdateYamlConfig(filetext);
                    ColorSettingsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(e.FullPath));
                    break;
                case "LevelSettings.yaml":
                    Logger.LogDebug("Triggering Level Settings update.");
                    LevelSystemData.UpdateYamlConfig(filetext);
                    LevelSettingsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(e.FullPath));
                    break;
                case "CreatureLootSettings.yaml":
                    Logger.LogDebug("Triggering Loot Settings update.");
                    LootSystemData.UpdateYamlConfig(filetext);
                    CreatureLootSettingsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(e.FullPath));
                    break;
                case "Modifiers.yaml":
                    Logger.LogDebug("Triggering Modifiers Settings update.");
                    CreatureModifiersData.UpdateModifierConfig(filetext);
                    ModifiersRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(e.FullPath));
                    break;
            }
        }

        private static ZPackage SendFileAsZPackage(string filepath)
        {
            string filecontents = File.ReadAllText(filepath);
            ZPackage package = new ZPackage();
            package.Write(filecontents);
            return package;
        }

        private static ZPackage SendLevelsConfigs() {
            return SendFileAsZPackage(levelsFilePath);
        }

        private static ZPackage SendCreatureLootConfigs()
        {
            return SendFileAsZPackage(creatureLootFilePath);
        }

        private static ZPackage SendColorsConfigs() {
            return SendFileAsZPackage(colorsFilePath);
        }

        private static ZPackage SendModifierConfigs()
        {
            return SendFileAsZPackage(creatureModifierFilePath);
        }

        public static IEnumerator OnServerRecieveConfigs(long sender, ZPackage package)
        {
            Logger.LogDebug("Server recieved config from client, rejecting due to being the server.");
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

        private static IEnumerator OnClientReceiveCreatureLootConfigs(long sender, ZPackage package)
        {
            var colorsyaml = package.ReadString();
            LootSystemData.UpdateYamlConfig(colorsyaml);

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        private static IEnumerator OnClientReceiveModifiersConfigs(long sender, ZPackage package)
        {
            var yaml = package.ReadString();
            CreatureModifiersData.UpdateModifierConfig(yaml);

            // Add in a check if we want to write the server config to disk or use it virtually
            yield return null;
        }

        public static string GetSecondaryConfigDirectoryPath()
        {
            var patchesFolderPath = Path.Combine(Paths.ConfigPath, "StarLevelSystem");
            var dirInfo = Directory.CreateDirectory(patchesFolderPath);

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
        public static ConfigEntry<bool> BindServerConfig(string catagory, string key, bool value, string description, AcceptableValueBase acceptableValues = null, bool advanced = false)
        {
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
        public static ConfigEntry<int> BindServerConfig(string catagory, string key, int value, string description, bool advanced = false, int valmin = 0, int valmax = 150)
        {
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
        public static ConfigEntry<float> BindServerConfig(string catagory, string key, float value, string description, bool advanced = false, float valmin = 0, float valmax = 150)
        {
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
        public static ConfigEntry<string> BindServerConfig(string catagory, string key, string value, string description, AcceptableValueList<string> acceptableValues = null, bool advanced = false)
        {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(
                    description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }
    }
}
