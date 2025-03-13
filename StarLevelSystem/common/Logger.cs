using BepInEx.Logging;
using System;


namespace StarLevelSystem
{
    internal class Logger
    {
        public static LogLevel Level = LogLevel.Info;

        public static void enableDebugLogging(object sender, EventArgs e)
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
            }
            else
            {
                Level = LogLevel.Info;
            }
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
