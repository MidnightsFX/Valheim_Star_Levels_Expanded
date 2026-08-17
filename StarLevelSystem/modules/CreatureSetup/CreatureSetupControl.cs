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


        // Cave dwellers (Ulv, Fenring_Cultist, Draugr in crypts...) ship with MonsterAI.m_sleeping set on the
        // prefab, so a bare Instantiate reproduces that and MonsterAI.UpdateAI short-circuits on IsSleeping().
        // Neither SetAlerted nor SetHuntPlayer wakes a sleeper, so an SLS-spawned one would lie there inert until
        // a player walked within m_wakeupRange (5m default) -- and our spawn points sit up to EventRange * 0.8
        // (~77m) out. Wakeup() is also what clears both sleep records: ZDOVars.s_sleeping and the salted animator
        // key ZSyncAnimation writes, so don't hand-roll it or the creature moves while stuck in the sleep pose.
        // m_fallAsleepDistance is zeroed because UpdateSleep's other branch re-Sleep()s an awake creature whenever
        // no player is within m_wakeupRange -- which would undo the wake within a frame or two.
        internal static void ForceAwake(MonsterAI ai) {
            if (ai == null) { return; }
            // Instance field on the clone, not the shared prefab -- same as the m_faction write in the raid spawner.
            ai.m_fallAsleepDistance = 0f;
            ZNetView nview = ai.m_nview;
            if (nview == null || nview.IsValid() == false) { return; }
            if (nview.IsOwner()) { nview.GetZDO().Set(SLS_NO_SLEEP, true); }
            ai.Wakeup();
        }

        // Applies the configured spawn-time AI behaviour to a freshly spawned creature.
        // Shared by the raid and nemesis spawners so they stay in sync.
        internal static void ApplySpawnAI(MonsterAI ai, AI creatureAI) {
            if (ai == null) { return; }
            // Being awake is a precondition for any of the modes below meaning anything, not an alternative to them.
            ForceAwake(ai);
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
