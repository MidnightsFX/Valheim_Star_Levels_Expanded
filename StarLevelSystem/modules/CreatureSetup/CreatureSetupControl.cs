using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Control;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {
    internal static class CreatureSetupControl {

        // Runs the actual creature setup pipeline. Returns true once the creature has been fully configured.
        // Made internal so the queue worker can drive it.
        internal static bool RunCharacterSetup(Character __instance, CharacterCacheEntry cDetails) {
            if (__instance == null || cDetails == null || cDetails.Level == 0) { return false; }

            if (ValConfig.ForceControlAllSpawns.Value == true) {
                CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, cDetails);
                cDetails = CompositeLazyCache.GetCacheEntry(__instance);
            }

            cDetails.CreatureNameLocalizable = CreatureModifiers.BuildCreatureLocalizableName(__instance, cDetails.CreatureModifiers);

            CreatureModifiers.RunOnceModifierSetup(__instance, cDetails);

            CreatureModifiers.SetupModifiers(__instance, cDetails, CompositeLazyCache.GetCreatureModifiers(__instance));
            SpeedModifications.ApplySpeedModifications(__instance, cDetails);
            DamageModifications.ApplyDamageModification(__instance, cDetails);
            SizeModifications.SetSizeModification(__instance.gameObject, __instance.m_nview, cDetails);
            HealthModifications.ApplyHealthModifications(__instance, cDetails);

            UIHudControl.InvalidateCacheEntry(__instance);

            if (__instance.m_level <= 1) { return true; }
            Colorization.ApplyColorizationWithoutLevelEffects(__instance.gameObject, cDetails.Colorization);
            Colorization.ApplyLevelVisual(__instance);

            return true;
        }

        internal static void CreatureSpawnerSetup(Character chara, int leveloverride = 0, bool multiply = true, float delay = 0.1f, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null) {
            if (chara == null) { return; }
            CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(chara, leveloverride, requiredModifiers, notAllowedModifiers);
            CompositeLazyCache.StartZOwnerCreatureRoutines(chara, cce, multiply);
            CreatureSetup(chara, leveloverride, multiply, delay, requiredModifiers, notAllowedModifiers);
        }

        // Schedules the creature for setup with no artificial delay (next queue tick).
        internal static void CreatureSetupNoDelay(Character __instance) {
            if (__instance == null) { return; }
            CreatureSetupQueue.Enqueue(__instance, levelOverride: 0, spawnMultiply: true, delay: 0f, requiredModifiers: null, notAllowedModifiers: null);
        }

        // Primary entry point - enqueues the creature for the timestamped setup queue.
        internal static void CreatureSetup(Character __instance, int leveloverride = 0, bool multiply = true, float delay = 1f, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null) {
            if (__instance == null) { return; }
            if (__instance.IsPlayer()) { return; }
            if (delay < 0f) { delay = 0f; }

            CreatureSetupQueue.Enqueue(__instance, leveloverride, multiply, delay, requiredModifiers, notAllowedModifiers);
        }
    }
}
