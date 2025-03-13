using BepInEx.Configuration;

namespace StarLevelSystem
{
    internal class ValConfig
    {
        public static ConfigFile cfg;
        public static ConfigEntry<bool> EnableDebugMode;
        public static ConfigEntry<int> MaxLevel;
        public static ConfigEntry<bool> EnableCreatureScalingPerLevel;
        public static ConfigEntry<float> PerLevelScaleBonus;
        public static ConfigEntry<float> PerLevelLootScale;
        public static ConfigEntry<float> LevelUpChance;
        public static ConfigEntry<float> LevelUpHealthBonus;
        public static ConfigEntry<float> LevelUpDamageBonus;
        
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
            PerLevelScaleBonus = BindServerConfig("LevelSystem", "PerLevelScaleBonus", 0.025f, "The additional size that a creature grows each star level.", true, 0f, 1f);
            PerLevelLootScale = BindServerConfig("LevelSystem", "PerLevelLootScale", 0.5f, "The amount of additional loot that a creature provides per each star level", true, 0f, 2f);
            LevelUpChance = BindServerConfig("LevelSystem", "LevelUpChance", 0.10f, "The chance that a creature will go from one level to the next. ");
            LevelUpHealthBonus = BindServerConfig("LevelSystem", "LevelUpHealthBonus", 0.2f, "The amount of health that each level gives a creature, vanilla is 1x (eg 100% more health each level).", false, 0.00f, 2f);
            LevelUpDamageBonus = BindServerConfig("LevelSystem", "LevelUpDamageBonus", 0.1f, "The amount of damage that each level gives a creatures, vanilla is 0.5x (eg 50% more damage each level).", false, 0.00f, 2f);
            MultiplayerEnemyDamageModifier = BindServerConfig("Multiplayer", "MultiplayerEnemyDamageModifier", 0.05f, "The additional amount of damage enemies will do to players, when there is a group of players together, per player. .2 = 20%", true, 0, 2f);
            MultiplayerEnemyHealthModifier = BindServerConfig("Multiplayer", "MultiplayerEnemyHealthModifier", 0.2f, "The additional amount of health enemies gain when players are grouped together, per player. .3 = 30%", true, 0, 2f);
            MultiplayerScalingRequiredPlayersNearby = BindServerConfig("Multiplayer", "MultiplayerScalingRequiredPlayersNearby", 3, "The number of players in a local area required to cause monsters to gain bonus health and/or damage.", true, 0, 10);
            EnableMultiplayerEnemyHealthScaling = BindServerConfig("Multiplayer", "EnableMultiplayerEnemyHealthScaling", true, "Wether or not creatures gain more health when players are grouped up.");
            EnableMultiplayerEnemyDamageScaling = BindServerConfig("Multiplayer", "EnableMultiplayerEnemyDamageScaling", false, "Wether or not creatures gain more damage when players are grouped up.");
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
