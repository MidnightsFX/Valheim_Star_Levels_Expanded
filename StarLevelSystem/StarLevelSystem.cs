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
using System.Diagnostics;
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
    internal class StarLevelSystem : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.StarLevelSystem";
        public const string PluginName = "StarLevelSystem";
        public const string PluginVersion = "1.30.0";

        public ValConfig cfg;
        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        // Set in Awake, not here: a static initialiser can run before BepInEx has registered this plugin, and Jotunn
        // then attributes the whole mod - its RPC names included - to Jotunn itself for the rest of the session.
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

            EmbeddedResourceBundle = LoadEmbeddedBundle("StarLevelSystem.assets.starlevelsystems");
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
            PrefabManager.OnVanillaPrefabsAvailable += UpdateLevelsOnChange.UpdateFishScaleByQuality;
            PrefabManager.OnVanillaPrefabsAvailable += UIHudControl.SetDefaultStar;
            PrefabManager.OnVanillaPrefabsAvailable += NemesisRemoteSpawnControl.LoadAssets;
            // Ahead of the handlers below, so they report this world's missing prefabs afresh.
            PrefabManager.OnPrefabsRegistered += PrefabLookup.ResetWarnings;
            PrefabManager.OnPrefabsRegistered += SwimAnimationModifications.ApplyToPrefabs;
            PrefabManager.OnPrefabsRegistered += LootSystemData.AttachPrefabsWhenReady;
            MinimapManager.OnVanillaMapDataLoaded += DistanceScaleSystem.DelayedMinimapSetup;
            MinimapManager.OnVanillaMapDataLoaded += ZoneScaleSystem.Initialize;
            MinimapManager.OnVanillaMapDataLoaded += NemesisMinimap.OnMapReady;
            PrefabManager.OnPrefabsRegistered += SizeModifications.PrepareSizeRefCache;
            PrefabManager.OnPrefabsRegistered += NovaBurstFx.Register;
            // ConfigNetwork tracks the sync flag itself now, and patches ZNet.Shutdown to reset it -- the
            // reset used to be a manual call in LevelScalingPatches that was easy to forget.
            UIHudControl.LoadAssets();
            ModifierStatusEffects.Register();
            RaidControl.LoadAssets();
            TerminalManager.Init();
            NemesisSystem.Initialize();
            //Jotunn.Logger.LogInfo("Star Levels have been expanded.");
            //DocumentationUpdater.UpdateDocumentation();
        }

        // Jotunn's AssetUtils.LoadAssetBundleFromResources hands Unity the resource stream and disposes it straight after
        // AssetBundle.LoadFromStream. The bundle is chunk-compressed (LZ4), which Unity reads lazily, a chunk at a time, so
        // the first asset in a chunk it had not already cached read from a closed stream: "ManagedStream object must be
        // readable", then a native "Overflow in memory allocator" that took the game down. Which assets that hit depended
        // on the bundle's layout, so a rebuild could start or stop it. A copy in memory has no stream to lose.
        private static AssetBundle LoadEmbeddedBundle(string resourceName) {
            Stopwatch timer = Stopwatch.StartNew();
            Assembly assembly = typeof(StarLevelSystem).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resourceName));
            if (name == null) {
                Log.LogError($"The asset bundle {resourceName} is not embedded in this build.");
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
            // global:: because inside the plugin class Logger is BepInEx's instance logger.
            global::StarLevelSystem.Logger.LogDebug($"Opened the embedded asset bundle ({bytes.Length / 1024} KB) from memory in {timer.Elapsed.TotalMilliseconds:0.0} ms.");
            return bundle;
        }
    }
}