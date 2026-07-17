using HarmonyLib;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.AnimationAndSpeed {
    internal static class AnimationSpeedPatches {

        // TODO: Add caching for animation speed adjustments
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        public static class ModifyCharacterAnimationSpeed {
            public static void Postfix(CharacterAnimEvent __instance) {
                if (__instance.m_character == null || !__instance.m_character.InAttack()) { return; }
                CharacterCacheEntry cdc = CompositeLazyCache.GetCacheEntry(__instance.m_character);
                if (cdc == null) { return; }
                // Read defensively: a cache entry may carry a partial stat dictionary (e.g. from config-driven
                // spawns), so fall back to the neutral defaults rather than throwing KeyNotFoundException.
                float attackSpeed = cdc.CreatureBaseValueModifiers.TryGetValue(CreatureBaseAttribute.AttackSpeed, out float aspd) ? aspd : 1f;
                float attackSpeedPerLevel = cdc.CreaturePerLevelValueModifiers.TryGetValue(CreaturePerLevelAttribute.AttackSpeedPerLevel, out float aspl) ? aspl : 0f;
                if (attackSpeed != 1f || attackSpeedPerLevel != 0f) {
                    __instance.m_animator.speed = attackSpeed + (attackSpeedPerLevel * __instance.m_character.m_level);
                }
            }
        }
    }
}
