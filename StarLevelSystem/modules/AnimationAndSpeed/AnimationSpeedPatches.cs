using HarmonyLib;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.AnimationAndSpeed {
    internal static class AnimationSpeedPatches {

        // Attack speed scales whatever speed the attack is already playing at, rather than replacing it. Vanilla changes
        // the animator speed mid-attack: hit-stop (CharacterAnimEvent.FreezeFrame) drops it to near zero and restores it
        // after, and animation events (CharacterAnimEvent.Speed) set their own. Writing an absolute speed every fixed
        // tick stomped both, so big hits never paused and event-driven slow-downs played at the flat SLS speed.
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        public static class ModifyCharacterAnimationSpeed {

            private class AttackSpeedState {
                // The speed vanilla (or an animation event) set, before SLS's multiplier.
                public float BaseSpeed;
                // The speed SLS last wrote. A different current speed means something else set a new one since.
                public float LastWritten = float.NaN;
            }

            // Entries die with the CharacterAnimEvent, so no eviction pass is needed.
            private static readonly ConditionalWeakTable<CharacterAnimEvent, AttackSpeedState> attackSpeedStates = new ConditionalWeakTable<CharacterAnimEvent, AttackSpeedState>();

            public static void Postfix(CharacterAnimEvent __instance) {
                Character character = __instance.m_character;
                if (character == null || character.IsPlayer() || !character.InAttack()) { return; }
                // Only the owner: ZSyncAnimation copies the owner's animator speed to every other peer each fixed tick,
                // multiplier included, so applying it there as well would scale it twice.
                if (__instance.m_nview == null || __instance.m_nview.IsOwner() == false) { return; }
                // Hit-stop owns the speed until UpdateFreezeFrame restores the one it saved, which already carries the multiplier.
                if (__instance.m_pauseTimer > 0f) { return; }
                float current = __instance.m_animator.speed;
                if (current < 0.01f) { return; }

                CharacterCacheEntry cdc = CompositeLazyCache.GetCacheEntry(character);
                if (cdc == null) { return; }
                float multiplier = SpeedModifications.GetAttackSpeedMultiplier(character, cdc);

                AttackSpeedState state;
                if (multiplier == 1f) {
                    // Nothing to scale, unless an earlier multiplier is still on the animator and has to come back off.
                    if (attackSpeedStates.TryGetValue(__instance, out state) == false || float.IsNaN(state.LastWritten)) { return; }
                } else {
                    state = attackSpeedStates.GetOrCreateValue(__instance);
                }

                if (current != state.LastWritten) { state.BaseSpeed = current; }
                float target = state.BaseSpeed * multiplier;
                if (target != current) { __instance.m_animator.speed = target; }
                // At 1x the animator is back on its own speed, so there is nothing left to track.
                state.LastWritten = multiplier == 1f ? float.NaN : target;
            }
        }
    }
}
