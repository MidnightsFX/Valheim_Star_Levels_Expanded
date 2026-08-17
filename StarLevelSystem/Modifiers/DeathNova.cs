using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Modifiers;
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
                if (__instance == null || __instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.IsOwner() == false) {
                    return;
                }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance);
                if (mods == null) { return; }
                // Each nova resolves independently: an unconfigured FireNova must not stop a PoisonNova
                // on the same creature (the old early-returns did exactly that).
                TriggerNova(__instance, mods, ModifierNames.FireNova.ToString(), bluntFraction: 0.25f, fireFraction: 0.5f, poisonFraction: 0f);
                // Fire is applied multiple times so its fraction is a diminished return
                TriggerNova(__instance, mods, ModifierNames.PoisonNova.ToString(), bluntFraction: 0.16f, fireFraction: 0f, poisonFraction: 1f);
                // FrostNova / LightningNova: not yet implemented
            }

            private static void TriggerNova(Character chara, Dictionary<string, ModifierType> mods, string novaName, float bluntFraction, float fireFraction, float poisonFraction) {
                if (mods.TryGetValue(novaName, out ModifierType modType) == false) { return; }
                CreatureModifierConfiguration cmdef = CreatureModifiersData.GetModifierDef(novaName, modType);
                if (cmdef == null || cmdef.Config == null) { return; }
                if (ModifierDefinitions.TryGetValue(novaName, out var novaDef) == false) { return; }
                GameObject go = CreatureModifiers.ApplySecondaryVFX(novaDef.SecondaryEffect, chara.transform.position, chara.transform.rotation);
                if (go == null) { return; }
                go.SetActive(false);
                Aoe aoe = go.GetComponent<Aoe>();
                // Configure damage
                float dmgmod = cmdef.Config.BasePower + (cmdef.Config.PerlevelPower * chara.m_level);
                if (aoe) {
                    float characterdmg = SLSExtensions.EstimateCharacterDamage(chara, DamageEstimateType.Average);
                    if (characterdmg <= 0) {
                        characterdmg = 10 * chara.m_level; // fallback to a base damage if for some reason we can't get an estimate
                    }
                    if (bluntFraction > 0) { aoe.m_damage.m_blunt = Mathf.Clamp((characterdmg * dmgmod) * bluntFraction, 0f, 5000f); }
                    if (fireFraction > 0) { aoe.m_damage.m_fire = Mathf.Clamp((characterdmg * dmgmod) * fireFraction, 0f, 5000f); }
                    if (poisonFraction > 0) { aoe.m_damage.m_poison = Mathf.Clamp((characterdmg * dmgmod) * poisonFraction, 0f, 5000f); }
                    Logger.LogDebug($"Activating {novaName} m:{dmgmod} x c:{characterdmg} = {(characterdmg * dmgmod)}");
                }

                go.SetActive(true);
            }
        }
    }
}
