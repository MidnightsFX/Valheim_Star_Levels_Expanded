using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.Damage;
using System.Collections.Generic;
using System.Linq;
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
                Character attacker = hit.GetAttacker();
                if (attacker == null || attacker.IsPlayer()) { return; }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(attacker);
                if (mods == null) { return; }
                if (mods.Keys.Contains(ModifierNames.ElementalChaos.ToString())) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.ElementalChaos.ToString(), mods[ModifierNames.ElementalChaos.ToString()]);
                    float value = cmcfg.BasePower + (cmcfg.PerlevelPower * attacker.m_level);
                    DamageType dmgT = RandomSelectDamageType();
                    Logger.LogDebug($"Elemental Chaos adding {dmgT} modifier {value}");
                    DamageModifications.AddDamagesToHit(hit, new Dictionary<DamageType, float>() { { dmgT, value } });
                }
            }

            private static DamageType RandomSelectDamageType() {
                int index = UnityEngine.Random.Range(0, 4);
                return ElementalDamages[index];
            }
        }
    }
}
