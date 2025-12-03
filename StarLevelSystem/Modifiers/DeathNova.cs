using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class DeathNova
    {
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class DeathNovaOnDeathPatch
        {
            private static void Prefix(Character __instance) {
                if (__instance == null || __instance.IsPlayer()) {
                    return;
                }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance);
                if (mods == null) { return; }
                if (mods.Keys.Contains(ModifierNames.FireNova.ToString())) {
                    CreatureModifier cmdef = CreatureModifiersData.GetModifierDef(ModifierNames.FireNova.ToString(), mods[ModifierNames.FireNova.ToString()]);
                    if (cmdef == null) { return; }
                    GameObject go = GameObject.Instantiate(CreatureModifiersData.LoadedSecondaryEffects[cmdef.SecondaryEffect], __instance.transform.position, __instance.transform.rotation);
                    go.SetActive(false);
                    Aoe aoe = go.GetComponent<Aoe>();
                    // Configure damage
                    float dmgmod = cmdef.Config.BasePower + (cmdef.Config.PerlevelPower * __instance.m_level);
                    if (aoe) {
                        float characterdmg = Extensions.EstimateCharacterDamage(__instance);
                        aoe.m_damage.m_blunt = (characterdmg * dmgmod) / 4f;
                        aoe.m_damage.m_fire = (characterdmg * dmgmod);
                        Logger.LogDebug($"Activating FireNova m:{dmgmod} x c:{characterdmg} = {(characterdmg * dmgmod)}");
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
