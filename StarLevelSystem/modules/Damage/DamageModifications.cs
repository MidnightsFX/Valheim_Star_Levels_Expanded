using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;
using DamageType = StarLevelSystem.common.DataObjects.DamageType;

namespace StarLevelSystem.modules.Damage {
    internal static class DamageModifications {

        public static void ForceUpdateDamageMod(Character creature, float increase_dmg_by) {
            float current_dmg_bonus = creature.m_nview.GetZDO().GetFloat(SLS_DAMAGE_MODIFIER);
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
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Applying damage recieved mods for {chara.m_name}");
            if (hit.m_damage.m_blunt > 0 && damageMods.ContainsKey(DamageType.Blunt)) {
                sb.Append($"  {DamageType.Blunt}: {hit.m_damage.m_blunt} * {damageMods[DamageType.Blunt]}");
                hit.m_damage.m_blunt *= damageMods[DamageType.Blunt];
                sb.Append($" = {hit.m_damage.m_blunt}\n");
            }
            if (hit.m_damage.m_pierce > 0 && damageMods.ContainsKey(DamageType.Pierce)) {
                sb.Append($"  {DamageType.Pierce}: {hit.m_damage.m_pierce} * {damageMods[DamageType.Pierce]}");
                hit.m_damage.m_pierce *= damageMods[DamageType.Pierce];
                sb.Append($" = {hit.m_damage.m_pierce}\n");
            }
            if (hit.m_damage.m_slash > 0 && damageMods.ContainsKey(DamageType.Slash)) {
                sb.Append($"  {DamageType.Slash}: {hit.m_damage.m_slash} * {damageMods[DamageType.Slash]}");
                hit.m_damage.m_slash *= damageMods[DamageType.Slash];
                sb.Append($" = {hit.m_damage.m_slash}\n");
            }
            if (hit.m_damage.m_fire > 0 && damageMods.ContainsKey(DamageType.Fire)) {
                sb.Append($"  {DamageType.Fire}: {hit.m_damage.m_fire} * {damageMods[DamageType.Fire]}");
                hit.m_damage.m_fire *= damageMods[DamageType.Fire];
                sb.Append($" = {hit.m_damage.m_fire}\n");
            }
            if (hit.m_damage.m_frost > 0 && damageMods.ContainsKey(DamageType.Frost)) {
                sb.Append($"  {DamageType.Fire}: {hit.m_damage.m_frost} * {damageMods[DamageType.Frost]}");
                hit.m_damage.m_frost *= damageMods[DamageType.Frost];
                sb.Append($" = {hit.m_damage.m_frost}\n");
            }
            if (hit.m_damage.m_lightning > 0 && damageMods.ContainsKey(DamageType.Lightning)) {
                sb.Append($"  {DamageType.Lightning}: {hit.m_damage.m_lightning} * {damageMods[DamageType.Lightning]}");
                hit.m_damage.m_lightning *= damageMods[DamageType.Lightning];
                sb.Append($" = {hit.m_damage.m_lightning}\n");
            }
            if (hit.m_damage.m_poison > 0 && damageMods.ContainsKey(DamageType.Poison)) {
                sb.Append($"  {DamageType.Poison}: {hit.m_damage.m_poison} * {damageMods[DamageType.Poison]}");
                hit.m_damage.m_poison *= damageMods[DamageType.Poison];
                sb.Append($" = {hit.m_damage.m_poison}\n");
            }
            if (hit.m_damage.m_spirit > 0 && damageMods.ContainsKey(DamageType.Spirit)) {
                sb.Append($"  {DamageType.Spirit}: {hit.m_damage.m_spirit} * {damageMods[DamageType.Spirit]}");
                hit.m_damage.m_spirit *= damageMods[DamageType.Spirit];
                sb.Append($" = {hit.m_damage.m_spirit}\n");
            }
            if (hit.m_damage.m_chop > 0 && damageMods.ContainsKey(DamageType.Chop)) {
                sb.Append($"  {DamageType.Spirit}: {hit.m_damage.m_chop} * {damageMods[DamageType.Chop]}");
                hit.m_damage.m_chop *= damageMods[DamageType.Chop];
                sb.Append($" = {hit.m_damage.m_chop}\n");
            }
            if (hit.m_damage.m_pickaxe > 0 && damageMods.ContainsKey(DamageType.Pickaxe)) {
                sb.Append($"  {DamageType.Pickaxe}: {hit.m_damage.m_pickaxe} * {damageMods[DamageType.Pickaxe]}");
                hit.m_damage.m_pickaxe *= damageMods[DamageType.Pickaxe];
                sb.Append($" = {hit.m_damage.m_pickaxe}\n");
            }
            if (ValConfig.EnableDebugOutputForDamage.Value) {
                Logger.LogInfo(sb.ToString());
            }
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
            return creatureBaseValueModifiers;
        }
    }
}
