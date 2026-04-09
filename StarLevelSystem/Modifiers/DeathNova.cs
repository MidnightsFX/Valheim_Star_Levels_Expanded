using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Control;
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
                    CreatureModifierConfiguration cmdef = CreatureModifiersData.GetModifierDef(ModifierNames.FireNova.ToString(), mods[ModifierNames.FireNova.ToString()]);
                    if (cmdef == null) { return; }
                    GameObject go = CreatureModifiers.ApplySecondaryVFX(ModifierDefinitions[ModifierNames.FireNova.ToString()].SecondaryEffect, __instance.transform.position, __instance.transform.rotation);
                    if (go == null) { return; }
                    go.SetActive(false);
                    Aoe aoe = go.GetComponent<Aoe>();
                    // Configure damage
                    float dmgmod = cmdef.Config.BasePower + (cmdef.Config.PerlevelPower * __instance.m_level);
                    if (aoe) {
                        float characterdmg = SLSExtensions.EstimateCharacterDamage(__instance, DamageEstimateType.Average);
                        aoe.m_damage.m_blunt = (characterdmg * dmgmod) / 4f;
                        aoe.m_damage.m_fire = (characterdmg * dmgmod) / 2f; // Fire is applied multiple times so we need to ensure this is a dimished return
                        Logger.LogDebug($"Activating FireNova m:{dmgmod} x c:{characterdmg} = {(characterdmg * dmgmod)} | blunt: {aoe.m_damage.m_blunt} fire: {aoe.m_damage.m_fire}");
                    }
                    
                    go.SetActive(true);
                }
                //if (cDetails.Modifiers.Keys.Contains(ModifierNames.FrostNova)) {

                //}
                //if (cDetails.Modifiers.Keys.Contains(ModifierNames.LightningNova))
                //{

                //}
                if (mods.Keys.Contains(ModifierNames.PoisonNova.ToString())) {
                    CreatureModifierConfiguration cmdef = CreatureModifiersData.GetModifierDef(ModifierNames.PoisonNova.ToString(), mods[ModifierNames.PoisonNova.ToString()]);
                    if (cmdef == null) { return; }
                    GameObject go = CreatureModifiers.ApplySecondaryVFX(ModifierDefinitions[ModifierNames.PoisonNova.ToString()].SecondaryEffect, __instance.transform.position, __instance.transform.rotation);
                    if (go == null) { return; }
                    go.SetActive(false);
                    Aoe aoe = go.GetComponent<Aoe>();
                    // Configure damage
                    float dmgmod = cmdef.Config.BasePower + (cmdef.Config.PerlevelPower * __instance.m_level);
                    if (aoe) {
                        float characterdmg = SLSExtensions.EstimateCharacterDamage(__instance, DamageEstimateType.Average);
                        aoe.m_damage.m_blunt = (characterdmg * dmgmod) / 6f;
                        aoe.m_damage.m_poison = (characterdmg * dmgmod);
                        Logger.LogDebug($"Activating Poison Nova m:{dmgmod} x c:{characterdmg} = {(characterdmg * dmgmod)}");
                    }

                    go.SetActive(true);
                }
            }
        }
    }
}
