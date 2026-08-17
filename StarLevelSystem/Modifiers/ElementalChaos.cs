using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.Damage;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers {

    internal static class ElementalChaos {

        [HarmonyPriority(Priority.VeryHigh)]
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class ElementalChaosRandomDamageSelectionBonus {
            private static readonly List<DamageType> ElementalDamages = new List<DamageType>() { DamageType.Fire, DamageType.Frost, DamageType.Lightning, DamageType.Spirit, DamageType.Poison };

            private static void Prefix(HitData hit, Character __instance) {
                if (hit == null || hit.m_attacker == null || __instance == null) { return; }
                // Skip hits SLS synthesized (LifeLink transfers) - the source hit already rolled its
                // chaos bonus, and re-entering here would stack a second one onto the same attack.
                if (DamageModifications.IsSynthesized(hit)) { return; }
                Character attacker = hit.GetAttacker();
                if (attacker == null || attacker.IsPlayer()) { return; }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(attacker);
                if (mods == null) { return; }
                if (mods.Keys.Contains(ModifierNames.ElementalChaos.ToString())) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.ElementalChaos.ToString(), mods[ModifierNames.ElementalChaos.ToString()]);
                    // The value is a FRACTION of the hit's damage added as the rolled element (see
                    // AddDamagesToHit). The old ceiling of 500 meant a misconfigured power turned into
                    // a 500x damage hit instead of being rejected; 5 (+500% as one element) is already
                    // far beyond any sane configuration.
                    float value = Mathf.Clamp(cmcfg.BasePower + (cmcfg.PerlevelPower * attacker.m_level), 0, 5f);
                    DamageType dmgT = RandomSelectDamageType();
                    if (Logger.IsDebugEnabled) { Logger.LogDebug($"Elemental Chaos adding {dmgT} modifier {value}"); }
                    DamageModifications.AddDamagesToHit(hit, new Dictionary<DamageType, float>() { { dmgT, value } });
                }
            }

            private static DamageType RandomSelectDamageType() {
                // int Range is max-exclusive, so Count includes the last entry (Poison)
                int index = UnityEngine.Random.Range(0, ElementalDamages.Count);
                return ElementalDamages[index];
            }
        }
    }
}
