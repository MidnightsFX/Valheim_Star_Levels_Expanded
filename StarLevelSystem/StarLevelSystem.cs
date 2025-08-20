using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System.Reflection;
using UnityEngine;
using static StarLevelSystem.modules.Commands;

namespace StarLevelSystem
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class StarLevelSystem : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.StarLevelSystem";
        public const string PluginName = "StarLevelSystem";
        public const string PluginVersion = "0.0.9";

        public ValConfig cfg;
        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        public static AssetBundle EmbeddedResourceBundle;
        public static Harmony HarmonyInstance { get; private set; }
        public static ManualLogSource Log;

        public void Awake()
        {
            Log = this.Logger;
            cfg = new ValConfig(Config);
            cfg.SetupConfigRPCs();
            cfg.LoadYamlConfigs();

            EmbeddedResourceBundle = AssetUtils.LoadAssetBundleFromResources("StarLevelSystem.assets.starlevelsystems", typeof(StarLevelSystem).Assembly);
            HarmonyInstance = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), harmonyInstanceId: PluginGUID);
            Colorization.Init();
            LevelSystemData.Init();
            LootSystemData.Init();
            CreatureModifiersData.Init();
            LocalizationLoader.AddLocalizations();
            PrefabManager.OnVanillaPrefabsAvailable += CreatureModifiersData.LoadPrefabs;
            PrefabManager.OnPrefabsRegistered += LootSystemData.AttachPrefabsWhenReady;

            TerminalCommands.AddCommands();
            CommandManager.Instance.AddConsoleCommand(new DumpLootTablesCommand());
            //Jotunn.Logger.LogInfo("Star Levels have been expanded.");
        }
    }
}