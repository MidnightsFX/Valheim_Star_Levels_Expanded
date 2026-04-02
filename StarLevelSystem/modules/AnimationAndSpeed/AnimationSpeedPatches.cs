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
                if (__instance.m_character != null && __instance.m_character.InAttack()) {
                    CharacterCacheEntry cdc = CompositeLazyCache.GetCacheEntry(__instance.m_character);
                    if (cdc != null && cdc.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] != 1 || cdc != null && cdc.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel] != 0f) {
                        __instance.m_animator.speed = cdc.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] + (cdc.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel] * __instance.m_character.m_level);
                    }
                }
            }
        }
    }
}
