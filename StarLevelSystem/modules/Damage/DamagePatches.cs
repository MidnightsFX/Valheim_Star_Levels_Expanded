using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static HitData;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Damage {
    internal static class DamagePatches {

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetLevelDamageFactor))]
        public static class ModifyDamagePerLevel {
            public static bool Prefix(Attack __instance, ref float __result) {
                __result = DetermineDamageFactor(__instance.m_character);
                return false;
            }

            private static float DetermineDamageFactor(Character character) {
                if (character.IsPlayer()) { return 1f; } // Players are not leveled, so return 1
                // Read-only lookup: this runs per attack, and GetAndSetLocalCache re-ran the full
                // cache build (FindBiome + config merges) on every call for a creature whose level
                // hadn't resolved yet. The setup queue builds the entry; until then the config
                // multipliers below are the correct fallback.
                CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(character);
                int level = Mathf.Max(0, character.GetLevel() - 1);
                float result = 1f;
                if (character.IsBoss()) {
                    if (cce != null && cce.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel] != 0) {
                        result += (level * cce.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel]);
                    } else {
                        result += (level * ValConfig.BossEnemyDamageMultiplier.Value);
                    }
                } else {
                    if (cce != null && cce.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel] != 0) {
                        result += (level * cce.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel]);
                    } else {
                        result += (level * ValConfig.EnemyDamageLevelMultiplier.Value);
                    }
                }
                float dmgMod = 1f;
                if (character.m_nview != null && character.m_nview.GetZDO() != null) {
                    dmgMod = character.m_nview.GetZDO().GetFloat(SLS_DAMAGE_MODIFIER, 1);
                }

                if (ValConfig.EnableDebugOutputForDamage.Value) {
                    Logger.LogDebug($"Setting {character.name} lvl {level} dmg factor to {result} * {dmgMod} = {result * dmgMod}");
                }
                result *= dmgMod;
                return result;
            }
        }

        //// For debugging full details on damage calculations
        //[HarmonyPatch(typeof(Attack), nameof(Attack.ModifyDamage))]
        //public static class ModifyDamagePrefix {
        //    private static void Prefix(HitData hitData) {
        //        if (ValConfig.EnableDebugOutputForDamage.Value == false) { return; }
        //        Logger.LogDebug($"Before Damage modification: D:{hitData.m_damage.m_damage} fi:{hitData.m_damage.m_fire} fr:{hitData.m_damage.m_frost} s:{hitData.m_damage.m_spirit} po:{hitData.m_damage.m_poison} b:{hitData.m_damage.m_blunt} p:{hitData.m_damage.m_pierce} s:{hitData.m_damage.m_slash}");
        //    }
        //}

        //// For debugging full details on damage calculations
        //[HarmonyPatch(typeof(Attack), nameof(Attack.ModifyDamage))]
        //public static class ModifyDamagePostfix {
        //    private static void Postfix(HitData hitData) {
        //        if (ValConfig.EnableDebugOutputForDamage.Value == false) { return; }
        //        Logger.LogDebug($"After Damage modification: D:{hitData.m_damage.m_damage} fi:{hitData.m_damage.m_fire} fr:{hitData.m_damage.m_frost} s:{hitData.m_damage.m_spirit} po:{hitData.m_damage.m_poison} b:{hitData.m_damage.m_blunt} p:{hitData.m_damage.m_pierce} s:{hitData.m_damage.m_slash}");
        //    }
        //}

        // For debugging full details on damage calculations
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        public static class CharacterApplyDamage {
            private static void Prefix(HitData hit) {
                if (ValConfig.EnableDebugOutputForDamage.Value == false) { return; }
                Logger.LogDebug($"Applying Damage: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");
            }
        }

        // For debugging full details on damage calculations
        [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
        public static class ApplyResistance {
            private static void Postfix(HitData __instance, DamageModifiers modifiers) {
                if (ValConfig.EnableDebugOutputForDamage.Value == false) { return; }
                Logger.LogDebug($"Applying Damage Modifiers {modifiers}, result after modifiers: D:{__instance.m_damage.m_damage} fi:{__instance.m_damage.m_fire} fr:{__instance.m_damage.m_frost} s:{__instance.m_damage.m_spirit} po:{__instance.m_damage.m_poison} b:{__instance.m_damage.m_blunt} p:{__instance.m_damage.m_pierce} s:{__instance.m_damage.m_slash}");
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class CharacterDamageModificationApply {
            private static void Prefix(HitData hit, Character __instance) {
                if (hit == null) { return; }
                CharacterCacheEntry attackerCharacter = CompositeLazyCache.GetCacheEntry(hit.GetAttacker());
                CharacterCacheEntry damagedCharacter = CompositeLazyCache.GetCacheEntry(__instance);

                // Attacker-side bonuses are skipped for hits SLS synthesized (LifeLink transfers):
                // the source hit already carried them when it first went through Character.Damage.
                if (attackerCharacter != null && attackerCharacter.CreatureDamageBonus != null && attackerCharacter.CreatureDamageBonus.Count > 0
                    && DamageModifications.IsSynthesized(hit) == false) {
                    if (ValConfig.EnableDebugOutputForDamage.Value) {
                        Logger.LogDebug($"{__instance.name} Hit:{hit.GetTotalDamageOptions()} Adding {attackerCharacter.GetDamageBonusDescription()}");
                    }
                    DamageModifications.AddDamagesToHit(hit, attackerCharacter.CreatureDamageBonus);
                }

                // Apply damage recieved Modifiers for the target
                if (damagedCharacter != null) {
                    DamageModifications.ApplyDamageModifiers(hit, __instance, damagedCharacter.DamageRecievedModifiers);
                }
            }
        }
    }
}
