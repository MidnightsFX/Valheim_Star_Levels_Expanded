using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using StarLevelSystem.modules;
using System.Reflection;

namespace StarLevelSystem
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class StarLevelSystem : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.StarLevelSystem";
        public const string PluginName = "StarLevelSystem";
        public const string PluginVersion = "0.0.1";

        public ValConfig cfg;
        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        public static Harmony HarmonyInstance { get; private set; }
        public static ManualLogSource Log;

        public void Awake()
        {
            Log = this.Logger;
            cfg = new ValConfig(Config);
            cfg.SetupConfigRPCs();
            cfg.LoadYamlConfigs();

            HarmonyInstance = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), harmonyInstanceId: PluginGUID);
            Colorization.SetupLevelEffects();
            Colorization.Init();
            LevelSystemConfiguration.Init();
            //PrefabManager.OnVanillaPrefabsAvailable += Colorization.DumpDefaultColorizations;

            //CommandManager.Instance.AddConsoleCommand(new SpawnerLevelExtension.ExtendedSpawnCommand());

            Jotunn.Logger.LogInfo("Star Levels have been expanded.");
        }
    }
}