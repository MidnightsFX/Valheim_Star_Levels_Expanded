using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System.Reflection;

namespace StarLevelSystem
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class StarLevelSystem : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.jotunnmodstub";
        public const string PluginName = "StarLevelSystem";
        public const string PluginVersion = "0.0.1";

        public ValConfig cfg;
        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        public static ManualLogSource Log;

        private void Awake()
        {
            cfg = new ValConfig(Config);
            // Jotunn comes with its own Logger class to provide a consistent Log style for all mods using it
            Jotunn.Logger.LogInfo("ModStub has landed");

            Assembly assembly = Assembly.GetExecutingAssembly();
            Harmony harmony = new(PluginGUID);
            harmony.PatchAll(assembly);
        }
    }
}