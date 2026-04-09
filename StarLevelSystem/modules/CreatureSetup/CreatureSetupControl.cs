using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Control;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {
    internal static class CreatureSetupControl {

        static public IEnumerator DelayedSetupValidateZnet(Character __instance, int level_override = 0, float delay = 1f, bool spawnMultiply = true, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null) {
            int times = 0;
            bool status = false;
            while (status == false) {
                // have to wait while we check the ZValid state- otherwise this results in an almost instant loop which will kill the client
                yield return new WaitForSeconds(delay);
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) { continue; }

                // Try to ensure that the Zowner gets the creature setup
                // Logger.LogDebug($"{__instance.name} DSVZ owner:{__instance.m_nview.IsOwner()} - {__instance.m_nview.m_zdo.Owned} - {__instance.m_nview.m_zdo.GetOwner()} force:{force}");
                if (__instance.m_nview.m_zdo.Owned == false) { __instance.m_nview.ClaimOwnership(); }
                // Only the owner should setup a creature, OR if someone is controlling it and it was just spawned it is setup immediately
                CharacterCacheEntry cce;
                if (__instance.m_nview.IsOwner() || delay == 0) {
                    //SetupCreatureZOwner(__instance, level_override, spawnMultiply, requiredModifiers);
                    if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) { continue; }
                    //Logger.LogDebug("Setting up creature cache as Z-owner");
                    cce = CompositeLazyCache.GetAndSetLocalCache(__instance, level_override, requiredModifiers);
                    CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, cce, spawnMultiply);
                }
                // If already setup by the owner it should have the correct references
                cce = CompositeLazyCache.GetCacheEntry(__instance);

                status = CharacterSetup(__instance, cce);
                //Logger.LogDebug($"Setup status: {status}");
                times += 1;
                // We've failed to get the creature setup and we don't have data for it, its not getting setup
                if (times >= ValConfig.FallbackDelayBeforeCreatureSetup.Value - 1) {
                    CharacterCacheEntry scd = CompositeLazyCache.GetAndSetLocalCache(__instance, level_override, requiredModifiers);
                    CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, scd, spawnMultiply);
                    CharacterSetup(__instance, scd);
                    Logger.LogDebug($"{scd.RefCreatureName} running delayed setup.");
                }
                if (times >= ValConfig.FallbackDelayBeforeCreatureSetup.Value) { break; }
            }

            yield break;
        }

        // This is the main entry point for setting up a character
        private static bool CharacterSetup(Character __instance, CharacterCacheEntry cDetails) {
            if (__instance == null || cDetails == null || cDetails.Level == 0) { return false; }

            if (ValConfig.ForceControlAllSpawns.Value == true) {
                CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, cDetails);
                cDetails = CompositeLazyCache.GetCacheEntry(__instance); // refresh after running zsetup
            }

            // Determine creature name
            //Logger.LogDebug("Setting creature name.");
            cDetails.CreatureNameLocalizable = CreatureModifiers.BuildCreatureLocalizableName(__instance, cDetails.CreatureModifiers);

            // Run once modifier setup to modify stats on creatures
            CreatureModifiers.RunOnceModifierSetup(__instance, cDetails);

            // Modify the creatures stats by custom character/biome modifications
            CreatureModifiers.SetupModifiers(__instance, cDetails, CompositeLazyCache.GetCreatureModifiers(__instance));
            SpeedModifications.ApplySpeedModifications(__instance, cDetails);
            DamageModifications.ApplyDamageModification(__instance, cDetails);
            SizeModifications.ApplySizeModifications(__instance.gameObject, cDetails);
            HealthModifications.ApplyHealthModifications(__instance, cDetails);

            // Rebuild UI since it may have been created before these changes were applied
            UIHudControl.InvalidateCacheEntry(__instance);

            if (__instance.m_level <= 1) { return true; }
            // Colorization and visual adjustments
            Colorization.ApplyColorizationWithoutLevelEffects(__instance.gameObject, cDetails.Colorization);
            Colorization.ApplyLevelVisual(__instance);

            return true;
        }

        internal static void CreatureSpawnerSetup(Character chara, int leveloverride = 0, bool multiply = true, float delay = 0.1f) {
            CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(chara, leveloverride);
            CompositeLazyCache.StartZOwnerCreatureRoutines(chara, cce, multiply);
            CreatureSetup(chara, delay: delay);
        }

        internal static void CreatureSetupNoDelay(Character __instance) {
            CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(__instance);
            CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, cce);
            cce = CompositeLazyCache.GetCacheEntry(__instance); // refresh after running zsetup
            CharacterSetup(__instance, cce);
        }

        // This is the primary flow setup for setting up a character
        internal static void CreatureSetup(Character __instance, int leveloverride = 0, bool multiply = true, float delay = 1f, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null) {
            if (__instance.IsPlayer()) { return; }
            // Setting a zero delay can prevent all other scripts from running by hogging the CPU
            if (delay == 0) { delay = 0.1f; }

            //// Select the creature data
            //CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance, leveloverride);
            //if (cDetails == null) { return; } // For invalid things, skip. This happens when placing TWIG etc (not a valid or awake character)

            // Logger.LogDebug($"Setting up creature {__instance.gameObject.name} with delay {delay} and level override {leveloverride}");
            // Generally a bad idea to run setup immediately if this is a networked player and the owner hasn't setup the creature
            // we want to delay slightly to allow the ZOwner to setup the creature, send the values and then we can use those, no need to multiply work
            TaskRunner.Run().StartCoroutine(DelayedSetupValidateZnet(__instance, leveloverride, delay: delay, spawnMultiply: multiply, requiredModifiers: requiredModifiers));
        }
    }
}
