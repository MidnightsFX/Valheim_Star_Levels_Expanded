using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.NemesisSystem;
using StarLevelSystem.modules.Raids;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System.Reflection;
using UnityEngine;

namespace StarLevelSystem
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    [BepInIncompatibility("org.bepinex.plugins.creaturelevelcontrol")]
    [BepInDependency("asharppen.valheim.drop_that", BepInDependency.DependencyFlags.SoftDependency)]
    internal class StarLevelSystem : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.StarLevelSystem";
        public const string PluginName = "StarLevelSystem";
        public const string PluginVersion = "1.7.3";

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
            TaskRunner.Setup();
            Compatibility.CheckModCompat();

            EmbeddedResourceBundle = AssetUtils.LoadAssetBundleFromResources("StarLevelSystem.assets.starlevelsystems", typeof(StarLevelSystem).Assembly);
            HarmonyInstance = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), harmonyInstanceId: PluginGUID);
            Compatibility.ApplyConditionalPatches(HarmonyInstance);
            // The seven per-file Init() calls are gone: Common/Config registers every config file, writes
            // any that are missing, loads them and wires their sync in one pass. Must run after the
            // ValConfig constructor (it reads cfgFolder and the poll/apply intervals) and before anything
            // that reads a settings static.
            //
            // These two were startup-only work inside LevelSystemData.Init that had no equivalent in its
            // reload path, so they would have been silently dropped by the move.
            Colorization.UpdateMapColorSelection();
            Colorization.UpdateZoneOverlayColorSelection();
            YamlConfigManager.Init();
            QuickConfigureTool.Init();
            LocalizationLoader.AddLocalizations();
            PrefabManager.OnVanillaPrefabsAvailable += CreatureModifiersData.LoadPrefabs;
            PrefabManager.OnVanillaPrefabsAvailable += UpdateLevelsOnChange.UpdateFishMaxLevel;
            PrefabManager.OnVanillaPrefabsAvailable += UIHudControl.SetDefaultStar;
            PrefabManager.OnVanillaPrefabsAvailable += NemesisRemoteSpawnControl.LoadAssets;
            PrefabManager.OnPrefabsRegistered += LootSystemData.AttachPrefabsWhenReady;
            MinimapManager.OnVanillaMapDataLoaded += DistanceScaleSystem.DelayedMinimapSetup;
            MinimapManager.OnVanillaMapDataLoaded += ZoneScaleSystem.Initialize;
            MinimapManager.OnVanillaMapDataLoaded += NemesisMinimap.OnMapReady;
            PrefabManager.OnPrefabsRegistered += SizeModifications.PrepareSizeRefCache;
            // ConfigNetwork tracks the sync flag itself now, and patches ZNet.Shutdown to reset it -- the
            // reset used to be a manual call in LevelScalingPatches that was easy to forget.
            UIHudControl.LoadAssets();
            RaidControl.LoadAssets();
            TerminalManager.Init();
            NemesisSystem.Initialize();
            //Jotunn.Logger.LogInfo("Star Levels have been expanded.");
            //DocumentationUpdater.UpdateDocumentation();
        }
    }
}