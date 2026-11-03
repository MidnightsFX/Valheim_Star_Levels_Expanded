using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Shared;
using StarLevelSystem.modules;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.NemesisSystem;
using StarLevelSystem.modules.Raids;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace StarLevelSystem
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    [BepInIncompatibility("org.bepinex.plugins.creaturelevelcontrol")]
    [BepInDependency("asharppen.valheim.drop_that", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.bepinex.plugins.valheim_plus", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(ExpandWorldDataBiomes.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    internal class StarLevelSystem : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.StarLevelSystem";
        public const string PluginName = "StarLevelSystem";
        public const string PluginVersion = "1.32.0";

        public ValConfig cfg;
        // No Jotunn instances should be created before Awake, or RPC names will not match
        public static CustomLocalization Localization;
        public static AssetBundle EmbeddedResourceBundle;
        public static Harmony HarmonyInstance { get; private set; }
        public static ManualLogSource Log;

        public void Awake()
        {
            Localization = LocalizationManager.Instance.GetLocalization();
            Log = this.Logger;
            cfg = new ValConfig(Config);
            cfg.SetupConfigRPCs();
            TaskRunner.Setup();
            Compatibility.CheckModCompat();
            BiomeRegistry.Init();

            EmbeddedResourceBundle = LoadEmbeddedBundle("StarLevelSystem.assets.starlevelsystems");
            HarmonyInstance = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), harmonyInstanceId: PluginGUID);
            Compatibility.ApplyConditionalPatches(HarmonyInstance);
            Colorization.UpdateMapColorSelection();
            Colorization.UpdateZoneOverlayColorSelection();
            YamlConfigManager.Init();
            QuickConfigureTool.Init();
            LocalizationLoader.AddLocalizations();
            PrefabManager.OnVanillaPrefabsAvailable += CreatureModifiersData.LoadPrefabs;
            PrefabManager.OnVanillaPrefabsAvailable += UpdateLevelsOnChange.UpdateFishMaxLevel;
            PrefabManager.OnVanillaPrefabsAvailable += UpdateLevelsOnChange.UpdateFishScaleByQuality;
            PrefabManager.OnVanillaPrefabsAvailable += UIHudControl.SetDefaultStar;
            PrefabManager.OnVanillaPrefabsAvailable += NemesisRemoteSpawnControl.LoadAssets;

            PrefabManager.OnPrefabsRegistered += PrefabLookup.ResetWarnings;
            PrefabManager.OnPrefabsRegistered += SwimAnimationModifications.ApplyToPrefabs;
            PrefabManager.OnPrefabsRegistered += LootSystemData.AttachPrefabsWhenReady;
            MinimapManager.OnVanillaMapDataLoaded += DistanceScaleSystem.DelayedMinimapSetup;
            MinimapManager.OnVanillaMapDataLoaded += ZoneScaleSystem.Initialize;
            MinimapManager.OnVanillaMapDataLoaded += NemesisMinimap.OnMapReady;
            PrefabManager.OnPrefabsRegistered += SizeModifications.PrepareSizeRefCache;
            PrefabManager.OnPrefabsRegistered += NovaBurstFx.Register;
            PrefabManager.OnPrefabsRegistered += ModifierSounds.Apply;

            UIHudControl.LoadAssets();
            ModifierStatusEffects.Register();
            RaidControl.LoadAssets();
            TerminalManager.Init();
            NemesisSystem.Initialize();
            //Jotunn.Logger.LogInfo("Star Levels have been expanded.");
            // TODO: consider finishing documentation generation
            //DocumentationUpdater.UpdateDocumentation();
        }

        private static AssetBundle LoadEmbeddedBundle(string resourceName) {
            Assembly assembly = typeof(StarLevelSystem).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resourceName));
            if (name == null) {
                Log.LogError($"The asset bundle {resourceName} is not available.");
                return null;
            }
            byte[] bytes;
            using (Stream stream = assembly.GetManifestResourceStream(name)) {
                bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length) {
                    int got = stream.Read(bytes, read, bytes.Length - read);
                    if (got <= 0) { break; }
                    read += got;
                }
            }
            AssetBundle bundle = AssetBundle.LoadFromMemory(bytes);
            return bundle;
        }
    }
}