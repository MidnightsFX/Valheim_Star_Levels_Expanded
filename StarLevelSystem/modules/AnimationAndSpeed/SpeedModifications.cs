using Jotunn.Managers;
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
            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel];
            float base_speed = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Speed];
            float perlevelmod = per_level_mod * (creature.m_level - 1);
            // Modify the creature's speed attributes based on the base speed and per level modifier
            float speedmod = (base_speed + perlevelmod);

            string creaturename = cDetails.RefCreatureName;
            creaturename ??= Utils.GetPrefabName(creature.gameObject);
            GameObject creatureRef = PrefabManager.Instance.GetPrefab(creaturename);
            if (creatureRef == null) {
                Logger.LogWarning($"Unable to find reference object for {creature.name}, not applying speed modifications");
                return;
            }

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
    }
}
