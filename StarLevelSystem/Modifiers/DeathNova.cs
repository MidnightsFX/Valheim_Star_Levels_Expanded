using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class DeathNova
    {
        internal static void Patch(Harmony harmony) {
          harmony.PatchAll(typeof(DeathNovaOnDeathPatch));
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class DeathNovaOnDeathPatch
        {
            private static void Prefix(Character __instance) {
                if (__instance == null || __instance.IsPlayer()) {
                    return;
                }
                CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance);
                if (cDetails == null) { return; }
                if (cDetails.Modifiers.Keys.Contains(ModifierNames.FireNova.ToString())) {
                    Logger.LogDebug("Activating FireNova");
                    CreatureModifier cmdef = CreatureModifiersData.GetModifierDef(ModifierNames.FireNova.ToString(), cDetails.Modifiers[ModifierNames.FireNova.ToString()]);
                    GameObject go = GameObject.Instantiate(CreatureModifiersData.LoadedSecondaryEffects[cmdef.SecondaryEffect], __instance.transform.position, __instance.transform.rotation);
                    go.SetActive(false);
                    Aoe aoe = go.GetComponent<Aoe>();
                    // Configure damage
                    if (aoe) {
                        float characterdmg = Extensions.EstimateCharacterDamage(__instance);
                        float dmgmod = cmdef.Config.BasePower + (cmdef.Config.PerlevelPower * __instance.m_level);
                        aoe.m_damage.m_blunt = (characterdmg * dmgmod) / 4f;
                        aoe.m_damage.m_fire = (characterdmg * dmgmod);
                    }
                    go.SetActive(true);
                }
                //if (cDetails.Modifiers.Keys.Contains(ModifierNames.FrostNova)) {

                //}
                //if (cDetails.Modifiers.Keys.Contains(ModifierNames.LightningNova))
                //{

                //}
                //if (cDetails.Modifiers.Keys.Contains(ModifierNames.PoisonNova))
                //{

                //}
            }
        }
    }
}
