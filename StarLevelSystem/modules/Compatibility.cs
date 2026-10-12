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
        public static bool IsVentureLocationResetEnabled = false;
        public static bool IsUpgradeWorldEnabled = false;
        public static bool IsValheimPlusEnabled = false;

        // Active only when CustomRaids is installed AND the compat toggle is enabled.
        public static bool CustomRaidsCompatActive => IsCustomRaidsEnabled && ValConfig.EnableCustomRaidsCompat.Value;


        private const string DropThatGUID = "asharppen.valheim.drop_that";
        private const string DropThatSessionManagerTypeName = "DropThat.Drop.CharacterDropSystem.Managers.CharacterDropSessionManager";
        // Drop That 3.1.5 and older lose track of which config each CharacterDrop entry came from on Valheim 1.0.x,
        // so ModifyDrop finds nothing to apply until 3.1.6.
        private static readonly System.Version DropThatCharacterDropTrackingFixed = new System.Version(3, 1, 6);

        private static Type DropThatCharacterDropSessionManager;
        public static bool DropThatCharacterModifyAvailable => DropThatCharacterModifyDrop != null;

        // CharacterDropSessionManager.ModifyDrop(GameObject drop, List<KeyValuePair<GameObject, int>> drops, int index)
        // Applies DropThat's per-drop item modifiers (durability/quality/custom stacks/EpicLoot/etc.) to an
        // already-instantiated creature loot drop. SLS replaces CharacterDrop.DropItems with its own optimized
        // drop pipeline, so DropThat's own transpiler never runs; we re-invoke this to preserve its modifiers.
        private static MethodInfo DropThatCharacterModifyDrop;
        private static MethodInfo JewelcraftingBossHudPostfix;

        private const string ValheimPlusGUID = "org.bepinex.plugins.valheim_plus";
        // ValheimPlus.GameClasses.PickableYieldState.CalculateYield(GameObject item, int originalAmount): V+'s [Pickable]
        // yield for an item, looked up by its name. It hands the amount back unchanged while that section is off.
        private static Func<GameObject, int, int> ValheimPlusPickableYield;
        // ValheimPlus.Configurations.Configuration.Current.Game: the synced [Game] section, which holds V+'s per-player
        // difficulty scales. V+ applies them by writing Game.m_damageScalePerPlayer / m_healthScalePerPlayer ahead of
        // vanilla's difficulty formulas, which SLS's MultiplayerDamageMod replaces, so SLS reads them from here instead.
        private static PropertyInfo ValheimPlusConfigCurrent;
        private static PropertyInfo ValheimPlusGameSection;
        private static PropertyInfo ValheimPlusGameEnabled;
        private static PropertyInfo ValheimPlusDamageScale;
        private static PropertyInfo ValheimPlusHealthScale;
        private static bool ValheimPlusPickableYieldFailed;
        private static bool ValheimPlusDifficultyFailed;

        internal static void CheckModCompat() {
            try {
                Dictionary<string, BepInEx.BaseUnityPlugin> plugins = BepInExUtils.GetPlugins();
                if (plugins == null) { return; }
                //Logger.LogDebug($"Checking for mod compatibility... {string.Join(",", plugins.Keys)}");
                // Drop That's Awake has already run here: the soft BepInDependency on it (StarLevelSystem.cs) makes
                // BepInEx load it first, and GetPlugins only lists plugins that have an instance.
                if (plugins.TryGetValue(DropThatGUID, out BepInEx.BaseUnityPlugin dropThat)) {
                    IsDropThatEnabled = true;
                    // Resolve the type from the loaded plugin's own assembly. Its assembly is named Valheim.DropThat,
                    // not DropThat, so the old assembly-qualified Type.GetType("..., DropThat") never bound and
                    // Drop That's item modifiers were never applied to the creature loot SLS drops.
                    DropThatCharacterDropSessionManager = dropThat?.GetType().Assembly.GetType(DropThatSessionManagerTypeName);
                    if (DropThatCharacterDropSessionManager != null) {
                        DropThatCharacterModifyDrop = DropThatCharacterDropSessionManager.GetMethod("ModifyDrop", BindingFlags.Public | BindingFlags.Static,
                            null, new[] { typeof(GameObject), typeof(List<KeyValuePair<GameObject, int>>), typeof(int) }, null);
                    }
                    if (DropThatCharacterModifyAvailable == false) {
                        Logger.LogWarning("Warning: DropThat compat method (CharacterDropSessionManager.ModifyDrop) not found; DropThat item modifiers will not be applied to creature loot.");
                    } else {
                        System.Version dropThatVersion = dropThat.Info?.Metadata?.Version;
                        if (dropThatVersion != null && dropThatVersion < DropThatCharacterDropTrackingFixed) {
                            Logger.LogWarning($"Drop That {dropThatVersion} detected; its creature drop modifiers need Drop That {DropThatCharacterDropTrackingFixed} or newer on this Valheim version and will not apply until it is updated.");
                        } else {
                            Logger.LogInfo("Drop That detected; its item modifiers will be applied to creature loot.");
                        }
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
                if (plugins.Keys.Contains("com.Venture.LocationReset")) {
                    IsVentureLocationResetEnabled = true;
                    // Both mods reset the same locations on their own timers and fight over the same
                    // ZDOs, which can duplicate or destroy loot. Hard-disable ours; the gate lives on
                    // LocationResetData so a mid-session config edit cannot re-enable it.
                    Data.LocationResetData.BlockedByModConflict = true;
                    if (ValConfig.EnableLocationReset.Value) {
                        Logger.LogWarning("VentureValheim LocationReset detected; SLS Location Reset has been disabled. Remove VentureValheim LocationReset to use SLS Location Reset - running both will fight over the same objects.");
                    } else {
                        Logger.LogInfo("VentureValheim LocationReset detected; SLS Location Reset is unavailable while it is installed.");
                    }
                }
                if (plugins.Keys.Contains("upgrade_world")) {
                    IsUpgradeWorldEnabled = true;
                    // Manual admin console toolkit with no automatic timer, so it only competes if an
                    // admin actually runs a command. Informational only.
                    Logger.LogInfo("Upgrade World detected; its locations_reset / vegetation_reset commands overlap with SLS Location Reset. Avoid running both against the same zones.");
                }
                if (plugins.Keys.Contains("org.bepinex.plugins.jewelcrafting")) {
                    IsJewelcraftingEnabled = true;
                    JewelcraftingBossHudPostfix = AccessTools.Method("Jewelcrafting.WorldBosses.BossHud+DisplayMultipleBossHuds:Postfix");
                    if (JewelcraftingBossHudPostfix == null) {
                        Logger.LogWarning("Jewelcrafting detected but DisplayMultipleBossHuds.Postfix could not be found; boss-HUD compatibility patch will be skipped.");
                    }
                }
                // Loaded ahead of SLS by the same soft BepInDependency as Drop That above.
                if (plugins.TryGetValue(ValheimPlusGUID, out BepInEx.BaseUnityPlugin valheimPlus)) {
                    IsValheimPlusEnabled = true;
                    BindValheimPlus(valheimPlus.GetType().Assembly);
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

        // Binds the two parts of Valheim Plus that SLS folds into its own results. Each is bound on its own, so a member V+
        // renames only loses that one.
        private static void BindValheimPlus(Assembly valheimPlus) {
            MethodInfo yield = valheimPlus.GetType("ValheimPlus.GameClasses.PickableYieldState")?.GetMethod("CalculateYield",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject), typeof(int) }, null);
            if (yield != null && yield.ReturnType == typeof(int)) {
                ValheimPlusPickableYield = (Func<GameObject, int, int>)Delegate.CreateDelegate(typeof(Func<GameObject, int, int>), yield);
                Logger.LogInfo("Valheim Plus detected; its [Pickable] yields apply to pickables with an SLS loot table.");
            } else {
                Logger.LogWarning("Valheim Plus detected but its pickable yield (PickableYieldState.CalculateYield) was not found; its [Pickable] yields will not apply to pickables with an SLS loot table.");
            }

            Type configuration = valheimPlus.GetType("ValheimPlus.Configurations.Configuration");
            ValheimPlusGameSection = configuration?.GetProperty("Game", BindingFlags.Public | BindingFlags.Instance);
            Type game = ValheimPlusGameSection?.PropertyType;
            ValheimPlusGameEnabled = game?.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
            ValheimPlusDamageScale = game?.GetProperty("gameDifficultyDamageScale", BindingFlags.Public | BindingFlags.Instance);
            ValheimPlusHealthScale = game?.GetProperty("gameDifficultyHealthScale", BindingFlags.Public | BindingFlags.Instance);
            // Current is bound last: TryGetValheimPlusDifficulty treats it being set as everything else being bound.
            if (ValheimPlusGameEnabled?.PropertyType == typeof(bool) && ValheimPlusDamageScale?.PropertyType == typeof(float) && ValheimPlusHealthScale?.PropertyType == typeof(float)) {
                ValheimPlusConfigCurrent = configuration.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            }
            if (ValheimPlusConfigCurrent == null) {
                Logger.LogWarning("Valheim Plus detected but its [Game] difficulty settings were not found; they will not stack with SLS multiplayer scaling.");
            } else {
                Logger.LogInfo("Valheim Plus detected; its [Game] difficulty scales stack with SLS multiplayer scaling.");
            }
        }

        // V+'s [Pickable] yield applied to an amount SLS rolled for a pickable it has a loot table for. The amount is
        // returned as-is without V+, or once V+ has thrown (its yield table is only built when its RPC_Pick transpiler runs).
        internal static int ValheimPlus_PickableYield(GameObject prefab, int amount) {
            if (ValheimPlusPickableYield == null || ValheimPlusPickableYieldFailed || prefab == null || amount <= 0) { return amount; }
            try {
                return Mathf.Max(0, ValheimPlusPickableYield(prefab, amount));
            } catch (Exception e) {
                ValheimPlusPickableYieldFailed = true;
                Logger.LogWarning($"Valheim Plus pickable yield failed and will not be applied to SLS pickable loot this session: {e.Message}");
                return amount;
            }
        }

        // V+'s [Game] per-player difficulty scales as fractions (its config is in percent), read live so they follow its
        // server sync. False while V+ is missing or that section is off, which leaves SLS's multiplayer scaling on its own.
        internal static bool TryGetValheimPlusDifficulty(out float damagePerPlayer, out float healthPerPlayer) {
            damagePerPlayer = 0f;
            healthPerPlayer = 0f;
            if (ValheimPlusConfigCurrent == null || ValheimPlusDifficultyFailed) { return false; }
            try {
                object current = ValheimPlusConfigCurrent.GetValue(null);
                object game = current != null ? ValheimPlusGameSection.GetValue(current) : null;
                if (game == null || (bool)ValheimPlusGameEnabled.GetValue(game) == false) { return false; }
                damagePerPlayer = (float)ValheimPlusDamageScale.GetValue(game) / 100f;
                healthPerPlayer = (float)ValheimPlusHealthScale.GetValue(game) / 100f;
                return true;
            } catch (Exception e) {
                ValheimPlusDifficultyFailed = true;
                Logger.LogWarning($"Reading Valheim Plus difficulty settings failed; they will not stack with SLS multiplayer scaling this session: {e.Message}");
                return false;
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
