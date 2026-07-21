using BepInEx.Logging;
using StarLevelSystem.common;
using System;


namespace StarLevelSystem {
    internal static class Logger
    {
        public static LogLevel Level = LogLevel.Info;

        public static void EnableDebugLogging(object sender, EventArgs e)
        {
            if (ValConfig.EnableDebugMode.Value) {
                Level = LogLevel.Debug;
            } else {
                Level = LogLevel.Info;
            }
            // set log level
        }

        public static void CheckEnableDebugLogging()
        {
            if (ValConfig.EnableDebugMode.Value)
            {
                Level = LogLevel.Debug;
            } else {
                Level = LogLevel.Info;
            }
        }

        public static void LogNemesis(string message) {
            if (ValConfig.EnableDebugNemesisDetails.Value == false) { return; }
            StarLevelSystem.Log.LogInfo("[Nemesis] " + message);
        }

        public static void LogRaid(string message) {
            if (ValConfig.EnableDebugRaidDetails.Value == false) { return; }
            StarLevelSystem.Log.LogInfo("[Raid] " + message);
        }

        public static void LogLoot(string message) {
            if (ValConfig.EnableDebugLootDetails.Value == false) { return; }
            StarLevelSystem.Log.LogInfo("[Loot] " + message);
        }

        public static void LogDebug(string message)
        {
            if (Level >= LogLevel.Debug)
            {
                StarLevelSystem.Log.LogInfo(message);
            }
        }
        public static void LogInfo(string message)
        {
            if (Level >= LogLevel.Info)
            {
                StarLevelSystem.Log.LogInfo(message);
            }
        }

        public static void LogWarning(string message)
        {
            if (Level >= LogLevel.Warning)
            {
                StarLevelSystem.Log.LogWarning(message);
            }
        }

        public static void LogError(string message)
        {
            if (Level >= LogLevel.Error)
            {
                StarLevelSystem.Log.LogError(message);
            }
        }
    }
}
