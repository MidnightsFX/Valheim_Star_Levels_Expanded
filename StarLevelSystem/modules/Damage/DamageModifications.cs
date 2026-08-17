using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using DamageType = StarLevelSystem.common.DataObjects.DamageType;

namespace StarLevelSystem.modules.Damage {
    internal static class DamageModifications {

        // Hits SLS itself synthesized (LifeLink damage transfers). The source hit already carried the
        // attacker's damage bonuses when it first went through Character.Damage, so the attacker-side
        // prefixes must skip these when the synthetic hit re-enters Character.Damage - otherwise the
        // bonuses (and ElementalChaos) get applied a second time.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HitData, object> SynthesizedHits = new System.Runtime.CompilerServices.ConditionalWeakTable<HitData, object>();
        private static readonly object SynthesizedMarker = new object();

        internal static void MarkSynthesized(HitData hit) {
            if (hit == null) { return; }
            SynthesizedHits.Remove(hit);
            SynthesizedHits.Add(hit, SynthesizedMarker);
        }

        internal static bool IsSynthesized(HitData hit) {
            return hit != null && SynthesizedHits.TryGetValue(hit, out _);
        }

        public static void ForceUpdateDamageMod(Character creature, float increase_dmg_by) {
            // Default must match the reader in DamagePatches (GetFloat(SLS_DAMAGE_MODIFIER, 1)):
            // the value is a multiplier, so an unset key means 1, not 0.
            float current_dmg_bonus = creature.m_nview.GetZDO().GetFloat(SLS_DAMAGE_MODIFIER, 1f);
            creature.m_nview.GetZDO().Set(SLS_DAMAGE_MODIFIER, current_dmg_bonus + increase_dmg_by);
        }

        internal static void ApplyDamageModification(Character creature, CharacterCacheEntry cDetails, bool updateCache = false) {
            if (creature.m_nview == null || cDetails == null) { return; }
            //float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel];
            float dmgmod = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseDamage];

            // No changes, do nothing
            if (dmgmod == 1) { return; }

            DictionaryDmgNetProperty DamageBonuses = new DictionaryDmgNetProperty(SLS_DAMAGE_BONUSES, creature.m_nview, new Dictionary<DamageType, float>());
            Dictionary<DamageType, float> dmgBonuses = DamageBonuses.Get();
            if (dmgBonuses.Count == 0 && cDetails.CreatureDamageBonus.Count > 0 || updateCache == true) {
                DamageBonuses.Set(cDetails.CreatureDamageBonus);
            }
            creature.m_nview.GetZDO().Set(SLS_DAMAGE_MODIFIER, dmgmod);
            Logger.LogDebug($"Built damage buffs for {creature.name} +{string.Join(",", cDetails.CreatureDamageBonus)} *{dmgmod}");
        }

        internal static void ApplyDamageModifiers(HitData hit, Character chara, Dictionary<DamageType, float> damageMods) {
            // Only build the debug report when it will actually be logged - this runs on every hit
            // against every creature, and the StringBuilder + interpolations were previously built
            // unconditionally and thrown away.
            StringBuilder sb = null;
            if (ValConfig.EnableDebugOutputForDamage.Value) {
                sb = new StringBuilder();
                sb.AppendLine($"Applying damage recieved mods for {chara.m_name}");
            }
            ApplyDamageMod(ref hit.m_damage.m_blunt, DamageType.Blunt, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_pierce, DamageType.Pierce, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_slash, DamageType.Slash, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_fire, DamageType.Fire, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_frost, DamageType.Frost, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_lightning, DamageType.Lightning, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_poison, DamageType.Poison, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_spirit, DamageType.Spirit, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_chop, DamageType.Chop, damageMods, sb);
            ApplyDamageMod(ref hit.m_damage.m_pickaxe, DamageType.Pickaxe, damageMods, sb);
            if (sb != null) {
                Logger.LogInfo(sb.ToString());
            }
        }

        private static void ApplyDamageMod(ref float damageValue, DamageType type, Dictionary<DamageType, float> damageMods, StringBuilder sb) {
            if (damageValue <= 0) { return; }
            if (!damageMods.TryGetValue(type, out float multiplier)) { return; }
            if (sb != null) { sb.Append($"  {type}: {damageValue} * {multiplier}"); }
            damageValue *= multiplier;
            if (sb != null) { sb.Append($" = {damageValue}\n"); }
        }

        internal static float GetTotalDamageOptions(this HitData hit, bool include_poison = false, bool include_spirit = false, bool include_pickaxe_and_chop = false) {
            float dmg = hit.m_damage.m_damage + hit.m_damage.m_blunt + hit.m_damage.m_slash + hit.m_damage.m_pierce + hit.m_damage.m_fire + hit.m_damage.m_frost + hit.m_damage.m_lightning;
            if (include_poison) { dmg += hit.m_damage.m_poison; }
            if (include_spirit) { dmg += hit.m_damage.m_spirit; }
            if (include_pickaxe_and_chop) { dmg += hit.m_damage.m_pickaxe + hit.m_damage.m_chop; }
            return dmg;
        }

        internal static void AddDamagesToHit(HitData hit, Dictionary<DamageType, float> damageBonuses) {
            float hitdamage = hit.GetTotalDamageOptions();
            foreach (var dmg in damageBonuses) {
                switch (dmg.Key) {
                    // Physical
                    case DamageType.Blunt:
                        hit.m_damage.m_blunt += hitdamage * dmg.Value;
                        break;
                    case DamageType.Slash:
                        hit.m_damage.m_slash += hitdamage * dmg.Value;
                        break;
                    case DamageType.Pierce:
                        hit.m_damage.m_pierce += hitdamage * dmg.Value;
                        break;
                    // Elemental
                    case DamageType.Fire:
                        hit.m_damage.m_fire += hitdamage * dmg.Value;
                        break;
                    case DamageType.Frost:
                        hit.m_damage.m_frost += hitdamage * dmg.Value;
                        break;
                    case DamageType.Lightning:
                        hit.m_damage.m_lightning += hitdamage * dmg.Value;
                        break;
                    case DamageType.Poison:
                        hit.m_damage.m_poison += hitdamage * dmg.Value;
                        break;
                    case DamageType.Spirit:
                        hit.m_damage.m_spirit += hitdamage * dmg.Value;
                        break;
                    // Utility
                    case DamageType.Chop:
                        hit.m_damage.m_chop += hitdamage * dmg.Value;
                        break;
                    case DamageType.Pickaxe:
                        hit.m_damage.m_pickaxe += hitdamage * dmg.Value;
                        break;
                }
            }
        }

        internal static Dictionary<DamageType, float> DetermineCreatureDamageRecievedModifiers(BiomeSpecificSetting biome_settings, CreatureSpecificSetting creature_settings) {
            Dictionary<DamageType, float> damageRecievedModifiers = new Dictionary<DamageType, float>() {
                { DamageType.Blunt, 1f },
                { DamageType.Pierce, 1f },
                { DamageType.Slash, 1f },
                { DamageType.Fire, 1f },
                { DamageType.Frost, 1f },
                { DamageType.Lightning, 1f },
                { DamageType.Poison, 1f },
                { DamageType.Spirit, 1f },
            };

            if (biome_settings != null && biome_settings.DamageRecievedModifiers != null) {
                foreach (var entry in biome_settings.DamageRecievedModifiers) {
                    damageRecievedModifiers[entry.Key] = entry.Value;
                }
            }
            if (creature_settings != null && creature_settings.DamageRecievedModifiers != null) {
                foreach (var entry in creature_settings.DamageRecievedModifiers) {
                    damageRecievedModifiers[entry.Key] = entry.Value;
                }
            }
            return damageRecievedModifiers;
        }

        internal static Dictionary<CreaturePerLevelAttribute, float> DetermineCharacterPerLevelStats(BiomeSpecificSetting biome_settings, CreatureSpecificSetting creature_settings) {
            Dictionary<CreaturePerLevelAttribute, float> creaturePerLevelSettings = new Dictionary<CreaturePerLevelAttribute, float>()
            {
                { CreaturePerLevelAttribute.DamagePerLevel, 0f },
                { CreaturePerLevelAttribute.HealthPerLevel, ValConfig.EnemyHealthMultiplier.Value },
                { CreaturePerLevelAttribute.SizePerLevel, ValConfig.PerLevelScaleBonus.Value },
                { CreaturePerLevelAttribute.SpeedPerLevel, 0f },
                { CreaturePerLevelAttribute.AttackSpeedPerLevel, 0f },
            };
            // Set creature per level settings
            //Logger.LogDebug("Computing perlevel creature modifiers");
            if (biome_settings != null && biome_settings.CreaturePerLevelValueModifiers != null) {
                foreach (var entry in biome_settings.CreaturePerLevelValueModifiers) {
                    creaturePerLevelSettings[entry.Key] = entry.Value;
                }
            }
            if (creature_settings != null && creature_settings.CreaturePerLevelValueModifiers != null) {
                foreach (var entry in creature_settings.CreaturePerLevelValueModifiers) {
                    creaturePerLevelSettings[entry.Key] = entry.Value;
                }
            }
            return creaturePerLevelSettings;
        }

        internal static Dictionary<CreatureBaseAttribute, float> DetermineCreatureBaseStats(BiomeSpecificSetting biome_settings, CreatureSpecificSetting creature_settings) {
            Dictionary<CreatureBaseAttribute, float> creatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                { CreatureBaseAttribute.BaseDamage, 1f },
                { CreatureBaseAttribute.BaseHealth, 1f },
                { CreatureBaseAttribute.Size, 1f },
                { CreatureBaseAttribute.Speed, 1f },
                { CreatureBaseAttribute.AttackSpeed, 1f },
            };

            if (biome_settings != null && biome_settings.CreatureBaseValueModifiers != null) {
                foreach (var entry in biome_settings.CreatureBaseValueModifiers) {
                    creatureBaseValueModifiers[entry.Key] = entry.Value;
                }
            }
            if (creature_settings != null && creature_settings.CreatureBaseValueModifiers != null) {
                foreach (var entry in creature_settings.CreatureBaseValueModifiers) {
                    creatureBaseValueModifiers[entry.Key] = entry.Value;
                }
            }
            // Base Size is the whole multiplier, not a bonus, so a zero or negative value would mean an
            // invisible or inside-out creature no matter what the per-level value does. SizePerLevel is
            // deliberately left unclamped here - negative values are a supported way to shrink per star,
            // and the combined result is floored in SizeModifications.DetermineScaleMultiplier.
            creatureBaseValueModifiers[CreatureBaseAttribute.Size] =
                Mathf.Max(ValConfig.MinimumCreatureScale.Value, creatureBaseValueModifiers[CreatureBaseAttribute.Size]);
            return creatureBaseValueModifiers;
        }
    }
}
