using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Damage;
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
            static float NextAllowedRedirection = 0;

            public static void Prefix(Character __instance, HitData hit) {
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance);
                if (mods != null && mods.ContainsKey(ModifierNames.LifeLink.ToString())) {
                    //Logger.LogDebug($"Lifelink triggered for {__instance.name}");
                    CreatureModifierConfiguration cm = CreatureModifiersData.GetModifierDef(ModifierNames.LifeLink.ToString(), mods[ModifierNames.LifeLink.ToString()]);
                    float damage_reduction = 1 - (cm.Config.BasePower + (cm.Config.PerlevelPower * __instance.m_level));
                    damage_reduction = Mathf.Clamp(damage_reduction, 0.1f, 1f);

                    HitData transferHit = new HitData() { m_attacker = hit.m_attacker, m_damage = hit.m_damage };
                    
                    // Minimum damage to transfer is 20
                    if (transferHit.GetTotalDamageOptions() < 20f) {
                        return;
                    }

                    // Not allowed to redirect damage more than once every second, to prevent infinite loops and excessive damage transfer
                    if (Time.realtimeSinceStartup < NextAllowedRedirection) {
                        return;
                    }

                    transferHit.m_damage.Modify(damage_reduction);
                    NextAllowedRedirection = Time.realtimeSinceStartup + 1f;

                    List<Character> CharactersNearby = SLSExtensions.GetCharactersInRange(__instance.transform.position, 15f);
                    bool transferred = false;
                    foreach (Character character in CharactersNearby) {
                        // No players, and not self
                        if (character.IsPlayer() || character == __instance) { continue; }
                        Logger.LogDebug($"Distributing Damage to {character.m_name}");

                        // TODO: Improve VFX for this
                        //if (CreatureModifiersData.LoadedSecondaryEffects.ContainsKey(CreatureModifiersData.ModifierDefinitions[ModifierNames.LifeLink.ToString()].SecondaryEffect)) {
                        //    Vector3 targetTravel = __instance.transform.position - character.transform.position;
                        //    GameObject go = GameObject.Instantiate(CreatureModifiersData.LoadedSecondaryEffects[CreatureModifiersData.ModifierDefinitions[ModifierNames.LifeLink.ToString()].SecondaryEffect], targetTravel, Quaternion.identity);
                        //}
                        
                        character.Damage(transferHit);
                        transferred = true;
                        break;
                    }

                    if (transferred) {
                        hit.m_damage.Modify(damage_reduction);
                    }   
                }
            }
        }
    }
}
