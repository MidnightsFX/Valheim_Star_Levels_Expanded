using StarLevelSystem.Data;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.Modifiers;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {
    internal static class CreatureSetupControl {

        // Runs the actual creature setup pipeline. Returns true once the creature has been fully configured.
        // Made internal so the queue worker can drive it.
        internal static bool RunCharacterSetup(Character __instance, CharacterCacheEntry cDetails) {
            if (__instance == null || __instance.m_nview == null || __instance.m_nview.IsValid() == false || cDetails == null || cDetails.Level == 0) { return false; }

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


        // Applies the configured spawn-time AI behaviour to a freshly spawned creature.
        // Shared by the raid and nemesis spawners so they stay in sync.
        internal static void ApplySpawnAI(MonsterAI ai, AI creatureAI) {
            if (ai == null) { return; }
            switch (creatureAI) {
                case AI.HuntPlayer:
                    ai.SetHuntPlayer(true);
                    break;
                case AI.Alerted:
                    ai.SetAlerted(true);
                    break;
                case AI.AgitatedByBuild:
                    // SetAggravated is a no-op unless the creature is m_aggravatable (Dvergr/Seekers etc).
                    // For everything else, fall back to alert+hunt so the creature actually engages.
                    if (ai.IsAggravatable()) {
                        ai.SetAggravated(true, BaseAI.AggravatedReason.Building);
                        ai.SetAlerted(true);
                    } else {
                        ai.SetAlerted(true);
                        ai.SetHuntPlayer(true);
                    }
                    break;
                default:
                    ai.SetAlerted(true);
                    break;
            }
        }


        // Primary entry point
        internal static void CreatureSetup(Character __instance, int leveloverride = 0, bool multiply = true, float delay = 1f, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null) {
            if (delay < 0f) { delay = 0f; }

            CreatureSetupQueue.Enqueue(__instance, leveloverride, multiply, delay, requiredModifiers, notAllowedModifiers);
        }
    }
}
