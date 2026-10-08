using Jotunn.Managers;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.AnimationAndSpeed {
    internal static class SpeedModifications {

        internal static void ApplySpeedModifications(Character creature, CharacterCacheEntry cDetails) {
            // Every path that rewrites the stat dictionaries re-applies speed through here, so the attack speed
            // multiplier rebuilds from the new values on the next attack.
            InvalidateAttackSpeed(cDetails);
            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel];
            float base_speed = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Speed];
            float perlevelmod = per_level_mod * (creature.m_level - 1);
            // Modify the creature's speed attributes based on the base speed and per level modifier
            float speedmod = (base_speed + perlevelmod);

            string creaturename = cDetails.RefCreatureName;
            creaturename ??= Utils.GetPrefabName(creature.gameObject);
            GameObject creatureRef = PrefabLookup.FindOrWarn(creaturename, "speed changes");
            if (creatureRef == null) { return; }

            Character refChar = creatureRef.GetComponent<Character>();

            if (refChar == null) {
                Logger.LogWarning($"Unable to find reference character for {creature.name}, not applying speed modifications");
                return;
            }

            creature.m_speed = refChar.m_speed * speedmod;
            creature.m_walkSpeed = refChar.m_walkSpeed * speedmod;
            creature.m_runSpeed = refChar.m_runSpeed * speedmod;
            creature.m_turnSpeed = refChar.m_turnSpeed * speedmod;
            creature.m_flyFastSpeed = refChar.m_flyFastSpeed * speedmod;
            creature.m_flySlowSpeed = refChar.m_flySlowSpeed * speedmod;
            creature.m_flyTurnSpeed = refChar.m_flyTurnSpeed * speedmod;
            creature.m_swimSpeed = refChar.m_swimSpeed * speedmod;
            creature.m_crouchSpeed = refChar.m_crouchSpeed * speedmod;
        }

        // The factor attack animations play at, held on the cache entry since the animation patch reads it every fixed
        // tick of every attack. Level - 1, matching health/damage/speed/size: a 0 star creature attacks at exactly AttackSpeed.
        // Never zero or negative, since a stopped or reversed attack never reaches its hit event.
        internal static float GetAttackSpeedMultiplier(Character creature, CharacterCacheEntry cDetails) {
            int level = creature.GetLevel();
            if (cDetails.AttackSpeedMultiplierLevel != level) {
                // Read defensively: a cache entry may carry a partial stat dictionary (e.g. from config-driven
                // spawns), so fall back to the neutral defaults rather than throwing KeyNotFoundException.
                float attackSpeed = cDetails.CreatureBaseValueModifiers.TryGetValue(CreatureBaseAttribute.AttackSpeed, out float aspd) ? aspd : 1f;
                float attackSpeedPerLevel = cDetails.CreaturePerLevelValueModifiers.TryGetValue(CreaturePerLevelAttribute.AttackSpeedPerLevel, out float aspl) ? aspl : 0f;
                cDetails.AttackSpeedMultiplier = Mathf.Max(0.01f, attackSpeed + (attackSpeedPerLevel * Mathf.Max(0, level - 1)));
                cDetails.AttackSpeedMultiplierLevel = level;
            }
            return cDetails.AttackSpeedMultiplier;
        }

        // For a caller that changes AttackSpeed or AttackSpeedPerLevel without going through ApplySpeedModifications.
        internal static void InvalidateAttackSpeed(CharacterCacheEntry cDetails) {
            if (cDetails != null) { cDetails.AttackSpeedMultiplierLevel = -1; }
        }
    }
}
