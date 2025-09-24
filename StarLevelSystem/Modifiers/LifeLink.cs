using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    public static class LifeLink
    {
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        public static class LifeLinkDamageDistributionPatch {
            public static void Prefix(Character __instance, HitData hit) {
                CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(__instance);
                if (cdc != null && cdc.Modifiers.ContainsKey(ModifierNames.LifeLink.ToString())) {
                    CreatureModifier cm = CreatureModifiersData.GetModifierDef(ModifierNames.LifeLink.ToString(), cdc.Modifiers[ModifierNames.LifeLink.ToString()]);
                    float damage_reduction = 1 - (cm.Config.BasePower + (cm.Config.PerlevelPower* __instance.m_level));
                    if (damage_reduction < 0) { damage_reduction = 0f; }

                    hit.m_damage.Modify(damage_reduction);

                    HitData transferHit = new HitData() { m_attacker = hit.m_attacker, m_damage = hit.m_damage };
                    transferHit.m_damage.Modify(1 - damage_reduction);

                    List<Character> CharactersNearby = Extensions.GetCharactersInRange(__instance.transform.position, 15f);
                    foreach (Character character in CharactersNearby) {
                        // No players, and not self
                        if (character.IsPlayer() || character == __instance) { continue; }
                        Logger.LogDebug($"Distributing Damage to {character.m_name}");
                        if (CreatureModifiersData.LoadedSecondaryEffects.ContainsKey(cm.SecondaryEffect)) {
                            Vector3 targetTravel = __instance.transform.position - character.transform.position;
                            GameObject go = GameObject.Instantiate(CreatureModifiersData.LoadedSecondaryEffects[cm.SecondaryEffect], targetTravel, Quaternion.identity);
                        }
                        
                        character.Damage(transferHit);
                        break;
                    }
                }
            }
        }
    }
}
