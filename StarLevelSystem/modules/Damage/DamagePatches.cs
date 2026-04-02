using HarmonyLib;
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
                CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(__instance.m_character);
                if (__instance.m_character.IsBoss()) {
                    if (cce != null && cce.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel] != 0) {
                        __result = Mathf.Max(0, __instance.m_character.GetLevel() - 1) * cce.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel];
                    } else {
                        __result = 1f + (float)Mathf.Max(0, __instance.m_character.GetLevel() - 1) * ValConfig.BossEnemyDamageMultiplier.Value;
                    }
                } else {
                    if (cce != null && cce.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel] != 0) {
                        __result = Mathf.Max(0, __instance.m_character.GetLevel() - 1) * cce.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel];
                    } else {
                        __result = 1f + (float)Mathf.Max(0, __instance.m_character.GetLevel() - 1) * ValConfig.EnemyDamageLevelMultiplier.Value;
                    }
                }
                if (ValConfig.EnableDebugOutputForDamage.Value) {
                    Logger.LogDebug($"Setting {__instance.m_character.name} lvl {__instance.m_character.GetLevel() - 1} dmg factor to {__result}");
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetLevelDamageFactor))]
        public static class SetupMaxLevelDamagePatch {
            public static bool Prefix(Attack __instance, ref float __result) {
                // We do not want to skip to return 1 if the character is not leveled because this might need to apply base damage changes too
                if (__instance.m_character == null || __instance.m_character.m_nview == null || __instance.m_character.m_nview.GetZDO() == null) {
                    __result = 1f;
                    return false;
                }
                __result = __instance.m_character.m_nview.GetZDO().GetFloat(SLS_DAMAGE_MODIFIER, 1);
                //Logger.LogDebug($"Damage Level Factor: {__result}");
                return false;
            }
        }

        // For debugging full details on damage calculations
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        public static class CharacterApplyDamage {
            private static void Prefix(HitData hit, Character __instance) {
                if (ValConfig.EnableDebugOutputForDamage.Value == false) { return; }
                Logger.LogDebug($"Applying Damage: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");
            }
        }
        // For debugging full details on damage calculations
        [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
        public static class ApplyResistance {
            private static void Postfix(HitData __instance, DamageModifiers modifiers) {
                if (ValConfig.EnableDebugOutputForDamage.Value == false) { return; }
                Logger.LogDebug($"Applying {modifiers} Modifiers: D:{__instance.m_damage.m_damage} fi:{__instance.m_damage.m_fire} fr:{__instance.m_damage.m_frost} s:{__instance.m_damage.m_spirit} po:{__instance.m_damage.m_poison} b:{__instance.m_damage.m_blunt} p:{__instance.m_damage.m_pierce} s:{__instance.m_damage.m_slash}");
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class CharacterDamageModificationApply {
            private static void Prefix(HitData hit, Character __instance) {
                CharacterCacheEntry attackerCharacter = CompositeLazyCache.GetCacheEntry(hit.GetAttacker());
                CharacterCacheEntry damagedCharacter = CompositeLazyCache.GetCacheEntry(__instance);

                if (attackerCharacter != null && attackerCharacter.CreatureDamageBonus != null && attackerCharacter.CreatureDamageBonus.Count > 0) {
                    if (ValConfig.EnableDebugOutputForDamage.Value) {
                        Logger.LogDebug($"{__instance.name} Hit:{hit.GetTotalDamageOptions()} Adding {attackerCharacter.GetDamageBonusDescription()}");
                    }
                    AddDamagesToHit(hit, attackerCharacter.CreatureDamageBonus);
                }

                // Apply damage recieved Modifiers for the target
                if (damagedCharacter != null) {
                    ApplyDamageModifiers(hit, __instance, damagedCharacter.DamageRecievedModifiers);
                }
            }
        }
    }
}
