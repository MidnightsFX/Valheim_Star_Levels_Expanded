using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.modules
{
    internal static class Compatibility
    {
        // Mod flags
        public static bool IsDropThatEnabled = false;
        public static bool IsExpandWorldEnabled = false;
        public static bool IsFGNEnabled = false;
        public static bool IsCustomRaidsEnabled = false;
        public static bool IsJewelcraftingEnabled = false;

        // Active only when CustomRaids is installed AND the compat toggle is enabled.
        public static bool CustomRaidsCompatActive => IsCustomRaidsEnabled && ValConfig.EnableCustomRaidsCompat.Value;


        private static Type DropThatCharacterDropSessionManager;
        public static bool DropThatCharacterModifyAvailable => DropThatCharacterModifyDrop != null;

        // CharacterDropSessionManager.ModifyDrop(GameObject drop, List<KeyValuePair<GameObject, int>> drops, int index)
        // Applies DropThat's per-drop item modifiers (durability/quality/custom stacks/EpicLoot/etc.) to an
        // already-instantiated creature loot drop. SLS replaces CharacterDrop.DropItems with its own optimized
        // drop pipeline, so DropThat's own transpiler never runs; we re-invoke this to preserve its modifiers.
        private static MethodInfo DropThatCharacterModifyDrop;
        private static MethodInfo JewelcraftingBossHudPostfix;

        internal static void CheckModCompat() {
            try {
                Dictionary<string, BepInEx.BaseUnityPlugin> plugins = BepInExUtils.GetPlugins();
                if (plugins == null) { return; }
                //Logger.LogDebug($"Checking for mod compatibility... {string.Join(",", plugins.Keys)}");
                if (plugins.Keys.Contains("asharppen.valheim.drop_that")) {
                    IsDropThatEnabled = true;
                    DropThatCharacterDropSessionManager = Type.GetType("DropThat.Drop.CharacterDropSystem.Managers.CharacterDropSessionManager, DropThat");
                    if (DropThatCharacterDropSessionManager != null) {
                        DropThatCharacterModifyDrop = DropThatCharacterDropSessionManager.GetMethod("ModifyDrop", BindingFlags.Public | BindingFlags.Static,
                            null, new[] { typeof(GameObject), typeof(List<KeyValuePair<GameObject, int>>), typeof(int) }, null);
                    }
                    if (DropThatCharacterModifyAvailable == false) {
                        Logger.LogWarning("Warning: DropThat compat method (CharacterDropSessionManager.ModifyDrop) not found; DropThat item modifiers will not be applied to creature loot.");
                    }
                }
                if (plugins.Keys.Contains("com.Fire.FiresGhettoNetworkMod")) {
                    IsFGNEnabled = true;
                }
                if (plugins.Keys.Contains("expand_world_size")) {
                    IsExpandWorldEnabled = true;
                }
                if (plugins.Keys.Contains("asharppen.valheim.custom_raids")) {
                    IsCustomRaidsEnabled = true;
                    Logger.LogInfo("Valheim.CustomRaids detected; CustomRaids raids will fire alongside SLS raids while EnableCustomRaidsCompat is enabled.");
                }
                if (plugins.Keys.Contains("org.bepinex.plugins.jewelcrafting")) {
                    IsJewelcraftingEnabled = true;
                    JewelcraftingBossHudPostfix = AccessTools.Method("Jewelcrafting.WorldBosses.BossHud+DisplayMultipleBossHuds:Postfix");
                    if (JewelcraftingBossHudPostfix == null) {
                        Logger.LogWarning("Jewelcrafting detected but DisplayMultipleBossHuds.Postfix could not be found; boss-HUD compatibility patch will be skipped.");
                    }
                }
            } catch {
                Logger.LogWarning("Unable to check mod compatibility. Ensure that Bepinex can load.");
            }

        }

        // Re-applies DropThat's per-drop item modifiers to a creature loot drop SLS instantiated itself.
        // 'drops' must be the same list instance CharacterDrop.DropItems received (DropThat keys its config
        // cache by that reference), and 'index' the drop's position within it. No-op for drops DropThat
        // hasn't configured.
        public static void DropThat_ModifyCharacterDrop(GameObject drop, List<KeyValuePair<GameObject, int>> drops, int index) {
            if (DropThatCharacterModifyAvailable == false) { return; }
            try {
                DropThatCharacterModifyDrop.Invoke(null, new object[] { drop, drops, index });
            } catch (Exception e) {
                Logger.LogWarning($"DropThat ModifyDrop failed: {e.Message}");
            }
        }

        // Applies compatibility patches that depend on another mod's methods. Called from Awake after
        // SLS's own Harmony instance exists (detection in CheckModCompat runs earlier).
        internal static void ApplyConditionalPatches(Harmony harmony) {
            if (IsJewelcraftingEnabled && JewelcraftingBossHudPostfix != null) {
                harmony.Patch(JewelcraftingBossHudPostfix,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(Compatibility), nameof(SkipJewelcraftingBossHud))));
                Logger.LogInfo("Jewelcrafting detected; suppressing its multi-boss HUD layout so SLS controls the boss healthbars.");
            }
        }

        // Skip Jewelcraftings forced boss hud Resize
        private static bool SkipJewelcraftingBossHud() {
           return !ValConfig.EnableJewelCraftingBossHudCompat.Value; 
        } 
    }
}
