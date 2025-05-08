using BepInEx.Configuration;
using System.IO;
using System;
using BepInEx;
using StarLevelSystem.modules;
using StarLevelSystem.common;
using Jotunn.Managers;
using Jotunn.Entities;
using System.Collections;

namespace StarLevelSystem
{
    internal class ValConfig
    {
        public static ConfigFile cfg;
        internal static String levelsFilePath = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "LevelSettings.yaml");
        internal static String colorsFilePath = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "ColorSettings.yaml");

        private static CustomRPC LevelSettingsRPC;
        private static CustomRPC ColorSettingsRPC;

        public static ConfigEntry<bool> EnableDebugMode;
        public static ConfigEntry<int> MaxLevel;
        public static ConfigEntry<bool> EnableCreatureScalingPerLevel;
        public static ConfigEntry<bool> EnableScalingInDungeons;
        public static ConfigEntry<float> PerLevelScaleBonus;
        public static ConfigEntry<float> PerLevelLootScale;
        public static ConfigEntry<int> LootDropsPerTick;
        public static ConfigEntry<float> EnemyHealthMultiplier;
        public static ConfigEntry<float> BossEnemyHealthMultiplier;
        public static ConfigEntry<float> EnemyHealthPerWorldLevel;
        public static ConfigEntry<float> EnemyDamageLevelMultiplier;
        public static ConfigEntry<float> BossEnemyDamageMultiplier;
        
        public static ConfigEntry<bool> EnableDistanceLevelScalingBonus;
        public static ConfigEntry<bool> EnableMultiplayerEnemyHealthScaling;
        public static ConfigEntry<bool> EnableMultiplayerEnemyDamageScaling;
        public static ConfigEntry<int> MultiplayerScalingRequiredPlayersNearby;
        public static ConfigEntry<float> MultiplayerEnemyDamageModifier;
        public static ConfigEntry<float> MultiplayerEnemyHealthModifier;

        public ValConfig(ConfigFile cf)
        {
            // ensure all the config values are created
            cfg = cf;
            cfg.SaveOnConfigSet = true;
            CreateConfigValues(cf);
        }

        public void SetupConfigRPCs() {
            LevelSettingsRPC = NetworkManager.Instance.AddRPC("LSE_LevelsRPC", OnServerRecieveConfigs, OnClientReceiveLevelConfigs);
            ColorSettingsRPC = NetworkManager.Instance.AddRPC("LSE_ColorsRPC", OnServerRecieveConfigs, OnClientReceiveColorConfigs);

            SynchronizationManager.Instance.AddInitialSynchronization(LevelSettingsRPC, SendLevelsConfigs);
            SynchronizationManager.Instance.AddInitialSynchronization(ColorSettingsRPC, SendColorsConfigs);
        }

        private void CreateConfigValues(ConfigFile Config) {
            // Debugmode
            EnableDebugMode = Config.Bind("Client config", "EnableDebugMode", false,
                new ConfigDescription("Enables Debug logging.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugMode.SettingChanged += Logger.enableDebugLogging;
            Logger.CheckEnableDebugLogging();


            MaxLevel = BindServerConfig("LevelSystem", "MaxLevel", 5, "The Maximum number of stars that a creature can have", false, 1, 100);
            EnableCreatureScalingPerLevel = BindServerConfig("LevelSystem", "EnableCreatureScalingPerLevel", true, "Enables started creatures to get larger for each star");
            EnableDistanceLevelScalingBonus = BindServerConfig("LevelSystem", "EnableDistanceLevelScalingBonus", true, "Creatures further away from the center of the world have a higher chance to levelup, this is a bonus applied to existing creature/biome configuration.");
            PerLevelScaleBonus = BindServerConfig("LevelSystem", "PerLevelScaleBonus", 0.10f, "The additional size that a creature grows each star level.", true, 0f, 2f);
            PerLevelScaleBonus.SettingChanged += Colorization.StarLevelScaleChanged;
            EnableScalingInDungeons = BindServerConfig("LevelSystem", "EnableScalingInDungeons", false, "Enables scaling in dungeons, this can cause creatures to become stuck.");
            PerLevelLootScale = BindServerConfig("LevelSystem", "PerLevelLootScale", 0.5f, "The amount of additional loot that a creature provides per each star level", true, 0f, 2f);
            LootDropsPerTick = BindServerConfig("LevelSystem", "LootDropsPerTick", 20, "The number of loot drops that are generated per tick, reducing this will reduce lag when massive amounts of loot is generated at once.", true, 1, 100);
            EnemyHealthMultiplier = BindServerConfig("LevelSystem", "EnemyHealthMultiplier", 1f, "The amount of health that each level gives a creature, vanilla is 1x. At 2x each creature has double the base health and gains twice as much per level.", false, 0.01f, 5f);
            EnemyHealthPerWorldLevel = BindServerConfig("LevelSystem", "EnemyHealthPerWorldLevel", 0.2f, "The percent amount of health that each world level gives a creature, vanilla is 2x (eg 200% more health each world level).", false, 0.00f, 2f);
            EnemyDamageLevelMultiplier = BindServerConfig("LevelSystem", "EnemyDamageLevelMultiplier", 0.1f, "The amount of damage that each level gives a creatures, vanilla is 0.5x (eg 50% more damage each level).", false, 0.00f, 2f);
            BossEnemyHealthMultiplier = BindServerConfig("LevelSystem", "BossEnemyHealthMultiplier", 0.3f, "The amount of health that each level gives a boss. 1 is 100% more health per level.", false, 0f, 5f);
            BossEnemyDamageMultiplier = BindServerConfig("LevelSystem", "BossEnemyDamageMultiplier", 0.02f, "The amount of damage that each level gives a boss. 1 is 100% more damage per level.", false, 0f, 5f);
            MultiplayerEnemyDamageModifier = BindServerConfig("Multiplayer", "MultiplayerEnemyDamageModifier", 0.05f, "The additional amount of damage enemies will do to players, when there is a group of players together, per player. .2 = 20%", true, 0, 2f);
            MultiplayerEnemyHealthModifier = BindServerConfig("Multiplayer", "MultiplayerEnemyHealthModifier", 0.2f, "The additional amount of health enemies gain when players are grouped together, per player. .3 = 30%", true, 0, 2f);
            MultiplayerScalingRequiredPlayersNearby = BindServerConfig("Multiplayer", "MultiplayerScalingRequiredPlayersNearby", 3, "The number of players in a local area required to cause monsters to gain bonus health and/or damage.", true, 0, 10);
            EnableMultiplayerEnemyHealthScaling = BindServerConfig("Multiplayer", "EnableMultiplayerEnemyHealthScaling", true, "Wether or not creatures gain more health when players are grouped up.");
            EnableMultiplayerEnemyDamageScaling = BindServerConfig("Multiplayer", "EnableMultiplayerEnemyDamageScaling", false, "Wether or not creatures gain more damage when players are grouped up.");
        }

        internal void LoadYamlConfigs()
        {
            string externalConfigFolder = ValConfig.GetSecondaryConfigDirectoryPath();
            string[] presentFiles = Directory.GetFiles(externalConfigFolder);
            bool foundLevelsFile = false;
            bool foundColorFile = false;

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
                    Colorization.UpdateYamlConfig(filetext);
                    ColorSettingsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(e.FullPath));
                    break;
                case "LevelSettings.yaml":
                    Logger.LogDebug("Triggering Level Settings update.");
                    LevelSystemData.UpdateYamlConfig(filetext);
                    LevelSettingsRPC.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(e.FullPath));
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

        private static ZPackage SendColorsConfigs() {
            return SendFileAsZPackage(colorsFilePath);
        }

        public static IEnumerator OnServerRecieveConfigs(long sender, ZPackage package)
        {
            Logger.LogDebug("Server recieved config from client, rejecting due to being the server.");
            yield return null;
        }

        private static IEnumerator OnClientReceiveLevelConfigs(long sender, ZPackage package) {
            var levelsyaml = package.ReadString();
            bool level_update_valid = LevelSystemData.UpdateYamlConfig(levelsyaml);

            // Add in a check if we want to write the server config to disk or use it virtually
            if (level_update_valid) {
                using (StreamWriter writetext = new StreamWriter(levelsFilePath)) {
                    writetext.WriteLine(levelsyaml);
                }
            }
            yield return null;
        }

        private static IEnumerator OnClientReceiveColorConfigs(long sender, ZPackage package) {
            var colorsyaml = package.ReadString();
            bool level_update_valid = Colorization.UpdateYamlConfig(colorsyaml);

            // Add in a check if we want to write the server config to disk or use it virtually
            if (level_update_valid) {
                using (StreamWriter writetext = new StreamWriter(levelsFilePath)) {
                    writetext.WriteLine(colorsyaml);
                }
            }
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
