using HarmonyLib;
using StarLevelSystem.common;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    internal class ModificationExtensionSystem
    {
        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class CreatureCharacterExtension
        {
            public static void Postfix(Character __instance) {
                Logger.LogDebug($"Character Awake called for {__instance.name} with level {__instance.m_level}");
                SetupLevelColorSizeAndStats(__instance);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class CharacterDamageModificationApply {
            private static void Prefix(HitData hit, Character __instance) {
                // If the attacker has a damage modification, apply it to damage done
                if (hit.m_attacker == null) { return; }
                ZDO atkr = ZDOMan.instance.GetZDO(hit.m_attacker);
                if (atkr == null) { return; }
                float damage_mod = atkr.GetFloat("SLE_DMod", 1);
                //Logger.LogDebug($"Damages D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");
                hit.m_damage.Modify(damage_mod);
                //Logger.LogDebug($"Applying dmg mod {damage_mod} new damages: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");
            }
        }


        [HarmonyPatch(typeof(RandomFlyingBird), nameof(RandomFlyingBird.Awake))]
        public static class RandomFlyingBirdExtension
        {
            public static void Postfix(RandomFlyingBird __instance) {
                if (ValConfig.EnableScalingBirds.Value == false) { return; }
                LevelSystem.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                int level = LevelSystem.DetermineLevel(__instance.gameObject, creature_name, creature_settings, biome_settings);
                if (level > 1) {
                    float scale = 1 + (ValConfig.BirdSizeScalePerLevel.Value * level);
                    Logger.LogDebug($"Setting bird size {scale}.");
                    __instance.transform.localScale *= scale;
                    Physics.SyncTransforms();
                    DropOnDestroyed dropondeath = __instance.gameObject.GetComponent<DropOnDestroyed>();
                    List<DropTable.DropData> drops = new List<DropTable.DropData>();
                    foreach (var drop in dropondeath.m_dropWhenDestroyed.m_drops)
                    {
                        DropTable.DropData lvlupdrop = new DropTable.DropData();
                        // Scale the amount of drops based on level
                        lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (ValConfig.PerLevelLootScale.Value * level));
                        lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (ValConfig.PerLevelLootScale.Value * level));
                        Logger.LogDebug($"Scaling drop {drop.m_item.name} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {level}.");
                        lvlupdrop.m_item = drop.m_item;
                        drops.Add(lvlupdrop);
                    }
                    dropondeath.m_dropWhenDestroyed.m_drops = drops;
                }
            }
        }

        private static void SetupLevelColorSizeAndStats(Character __instance)
        {
            if (__instance.IsPlayer()) { return; }
            ZNetView zview = __instance.gameObject.GetComponent<ZNetView>();

            // Select the creature data
            LevelSystem.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);

            // Determine if this creature should get deleted due to disableSpawn
            if (biome_settings != null && biome_settings.creatureSpawnsDisabled != null && biome_settings.creatureSpawnsDisabled.Contains(biome)) {
                Logger.LogDebug($"Creature {__instance.name} in biome {biome} is a  disabled spawn, deleting.");
                ZNetScene.instance.Destroy(__instance.gameObject);
                return;
            }

            bool setupdone = zview.GetZDO().GetBool("SLE_SDone", false);
            if (setupdone != true) {
                // Setup the level and colorization if it wasn't already
                if (__instance.m_level <= 1) {
                    LevelSystem.DetermineApplyLevelGeneric(__instance.gameObject, creature_name, creature_settings, biome_settings);
                }
                zview.GetZDO().Set("SLE_SDone", true);
            }

            // Modify the creatures stats by custom character/biome modifications
            ApplySpeedModifications(__instance, creature_settings, biome_settings);
            ApplyDamageModification(__instance, creature_name, creature_settings, biome_settings);

            // No need to do colorization or size scaling if the creature is level 1 or lower
            if (__instance.m_level <= 1) { return; }
            // Colorization and visual adjustments
            Colorization.ApplyColorizationWithoutLevelEffects(__instance);
            Colorization.ApplyHighLevelVisual(__instance);

            // Scaling
            if (__instance.transform.position.y < 3000f && ValConfig.EnableScalingInDungeons.Value == false) {
                // Don't scale in dungeons
                float scale = 1 + (ValConfig.PerLevelScaleBonus.Value * __instance.m_level);
                Logger.LogDebug($"Setting character size {scale} and color.");
                __instance.transform.localScale *= scale;
                Physics.SyncTransforms();
            }
        }

        private static void ApplySpeedModifications(Character creature, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings)
        {
            float base_speed = 1f;
            float per_level_mod = 1f;
            if (creature_settings != null && creature_settings.CreatureBaseValueModifiers != null && creature_settings.CreatureBaseValueModifiers.TryGetValue(CreatureBaseAttribute.Speed, out float basespeedModifier)) {
                base_speed = basespeedModifier;
            } else if (biome_settings != null &&  biome_settings.CreatureBaseValueModifiers != null && biome_settings.CreatureBaseValueModifiers.TryGetValue(CreatureBaseAttribute.Speed, out float biomespeedModifier)){
                base_speed = biomespeedModifier;
            }

            if (creature_settings != null && creature_settings.CreaturePerLevelValueModifiers != null && creature_settings.CreaturePerLevelValueModifiers.TryGetValue(CreaturePerLevelAttribute.SpeedPerLevel, out float perlevelspeed_creature)){
                per_level_mod = perlevelspeed_creature;
            } else if (biome_settings != null && biome_settings.CreaturePerLevelValueModifiers != null && biome_settings.CreaturePerLevelValueModifiers.TryGetValue(CreaturePerLevelAttribute.SpeedPerLevel, out float perlevelspeed_biome)){
                per_level_mod = perlevelspeed_biome;
            }

            float perlevelmod = Mathf.Pow(per_level_mod, (creature.m_level - 1));
            // Modify the creature's speed attributes based on the base speed and per level modifier
            creature.m_speed = (creature.m_speed * base_speed) * perlevelmod;
            creature.m_walkSpeed = (creature.m_walkSpeed * base_speed) * perlevelmod;
            creature.m_runSpeed = (creature.m_runSpeed * base_speed) * perlevelmod;
            creature.m_turnSpeed = (creature.m_turnSpeed * base_speed) * perlevelmod;
            creature.m_flyFastSpeed = (creature.m_flyFastSpeed * base_speed) * perlevelmod;
            creature.m_flySlowSpeed = (creature.m_flySlowSpeed * base_speed) * perlevelmod;
            creature.m_flyTurnSpeed = (creature.m_flyTurnSpeed * base_speed) * perlevelmod;
            creature.m_swimSpeed = (creature.m_swimSpeed * base_speed) * perlevelmod;
            creature.m_crouchSpeed = (creature.m_crouchSpeed * base_speed) * perlevelmod;

            Logger.LogDebug($"Applying speed modifications for {creature.name} base speed mod: {base_speed}, per level mod: {per_level_mod} x {(creature.m_level - 1)} ({perlevelmod})");
        }

        private static void ApplyDamageModification(Character creature, string creature_name, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings) {
            Humanoid chumanoid = creature.GetComponent<Humanoid>();
            if (chumanoid == null) { return; }

            float basedmgmod = 1f;
            float perlvldmgmod = 1f;

            // Determine what the modifier value will be
            if (creature_settings != null && creature_settings.CreatureBaseValueModifiers != null && creature_settings.CreatureBaseValueModifiers.TryGetValue(CreatureBaseAttribute.BaseDamage, out float charbasedmg)) {
                basedmgmod = charbasedmg;
            }
            else if (biome_settings != null && biome_settings.CreatureBaseValueModifiers != null && biome_settings.CreatureBaseValueModifiers.TryGetValue(CreatureBaseAttribute.BaseDamage, out float biomebasedmg)) {
                basedmgmod = biomebasedmg;
            }
            if (creature_settings != null && creature_settings.CreaturePerLevelValueModifiers != null && creature_settings.CreaturePerLevelValueModifiers.TryGetValue(CreaturePerLevelAttribute.DamagePerLevel, out float charperlvldmg)) {
                perlvldmgmod = charperlvldmg;
            }
            else if (biome_settings != null && biome_settings.CreaturePerLevelValueModifiers != null && biome_settings.CreaturePerLevelValueModifiers.TryGetValue(CreaturePerLevelAttribute.DamagePerLevel, out float biomeperlvldmg)) {
                perlvldmgmod = biomeperlvldmg;
            }

            // No changes, do nothing
            if (basedmgmod == 1 && perlvldmgmod == 1) { return; }
            float dmgmod = basedmgmod * Mathf.Pow(perlvldmgmod, (creature.m_level - 1));
            Logger.LogDebug($"Applying damage modifications for {creature_name} to {dmgmod}");
            creature.m_nview.GetZDO().Set("SLE_DMod", dmgmod);
        }

    }
}
