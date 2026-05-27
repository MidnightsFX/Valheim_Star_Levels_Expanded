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
            // Pre-populate the cache eagerly so the spawn-time params (level / required mods / not-allowed mods)
            // are stored before Enqueue. Character.Awake's postfix already enqueued a setup with no overrides
            // during Instantiate; if we don't write our params into the cache first, dedupe will drop this call
            // and the queue worker will run with the empty Awake-time params instead. The expensive ZOwner work
            // is left to the queue worker so it only runs once per creature.
            CompositeLazyCache.GetAndSetLocalCache(chara, leveloverride, requiredModifiers, notAllowedModifiers, updateCache: true);
            CreatureSetup(chara, leveloverride, multiply, delay, requiredModifiers, notAllowedModifiers);
        }


        // Primary entry point
        internal static void CreatureSetup(Character __instance, int leveloverride = 0, bool multiply = true, float delay = 1f, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null) {
            if (delay < 0f) { delay = 0f; }

            CreatureSetupQueue.Enqueue(__instance, leveloverride, multiply, delay, requiredModifiers, notAllowedModifiers);
        }
    }
}
