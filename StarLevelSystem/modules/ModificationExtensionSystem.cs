using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    internal class ModificationExtensionSystem
    {

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        public static class CreatureSizeSyncEquipItems
        {
            public static void Postfix(Character __instance)
            {
                if (__instance.IsPlayer()) { return; }
                // Logger.LogDebug($"Character Awake called for {__instance.name} with level {__instance.m_level}");
                CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance);
                LoadApplySizeModifications(__instance.gameObject, __instance.m_nview, cDetails);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GiveDefaultItems))]
        public static class CreatureSizeSyncItems
        {
            public static void Postfix(Character __instance)
            {
                if (__instance.IsPlayer()) { return; }
                // Logger.LogDebug($"Character Awake called for {__instance.name} with level {__instance.m_level}");
                CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance);
                LoadApplySizeModifications(__instance.gameObject, __instance.m_nview, cDetails);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class CreatureCharacterExtension
        {
            public static void Postfix(Character __instance) {
                // Logger.LogDebug($"Character Awake called for {__instance.name} with level {__instance.m_level}");
                CreatureSetup(__instance);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
        public static class ModifyRagdollHumanoid
        {
            public static void Postfix(Character __instance, Ragdoll ragdoll)
            {
                Logger.LogDebug($"Ragdoll Humanoid created for {__instance.name} with level {__instance.m_level}");
                CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance);
                ApplySizeModificationZRefOnly(ragdoll.gameObject, __instance.m_nview);
                CompositeLazyCache.RemoveFromCache(__instance);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        public static class ModifyCharacterVisualsToLevel
        {
            public static bool Prefix(Character __instance) {
                Logger.LogDebug("Setting Charcter level");
                CreatureSetup(__instance);
                __instance.SetupMaxHealth();
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class CharacterDamageModificationApply {
            private static void Prefix(HitData hit, Character __instance) {
                // If the attacker has a damage modification, apply it to damage done
                if (hit.m_attacker == null) { return; }
                ZDO atkr = ZDOMan.instance.GetZDO(hit.m_attacker);
                if (atkr == null) { return; }
                ZNetView zview = ZNetScene.instance.FindInstance(atkr);
                Character atkrChar = hit.GetAttacker();
                if (zview == null || atkrChar == null) { return; }


                // Modify damage types based on bonusees
                DictionaryDmgNetProperty DamageBonuses = new DictionaryDmgNetProperty("SLE_DBon", zview, new Dictionary<DamageType, float>());
                AddDamagesToHit(hit, DamageBonuses.Get());
                // Modify damage totals
                float damage_mod = atkr.GetFloat("SLE_DMod", 1);
                hit.m_damage.Modify(damage_mod);
                Logger.LogDebug($"Applied dmg mod {damage_mod} new damages: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");

                // Apply damage recieved Modifiers for the target
                CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(__instance);
                if (cdc == null) { return; }
                hit.m_damage.m_fire *= cdc.DamageRecievedModifiers[DamageType.Fire];
                hit.m_damage.m_frost *= cdc.DamageRecievedModifiers[DamageType.Frost];
                hit.m_damage.m_lightning *= cdc.DamageRecievedModifiers[DamageType.Lightning];
                hit.m_damage.m_poison *= cdc.DamageRecievedModifiers[DamageType.Poison];
                hit.m_damage.m_spirit *= cdc.DamageRecievedModifiers[DamageType.Spirit];
                hit.m_damage.m_blunt *= cdc.DamageRecievedModifiers[DamageType.Blunt];
                hit.m_damage.m_slash *= cdc.DamageRecievedModifiers[DamageType.Slash];
                hit.m_damage.m_pierce *= cdc.DamageRecievedModifiers[DamageType.Pierce];
                Logger.LogDebug($"Applied dmg recieved mods new damages: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");

            }
        }

        internal static void AddDamagesToHit(HitData hit, Dictionary<DamageType, float> damageBonuses) {
            float hitdamage = hit.GetTotalDamage();
            foreach (var dmg in damageBonuses) {
                switch(dmg.Key) {
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
                        hit.m_damage.m_lightning +=  hitdamage * dmg.Value;
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

        // Delayed destruction of an object so that it can finish being setup- otherwise there are lots of vanilla scripts that explode
        // Since apparently instanciating and destroying something in the same frame breaks vanilla assumptions :sigh:
        static IEnumerator DestroyCoroutine(GameObject go, float delay = 0.2f) {
            yield return new WaitForSeconds(delay);
            ZNetScene.instance.Destroy(go);
        }

        internal static void CreatureSetup(Character __instance, bool refresh_cache = false, int leveloverride = 0) {
            if (__instance.IsPlayer()) { return; }

            // Select the creature data
            CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance, refresh_cache, leveloverride);

            // Determine if this creature should get deleted due to disableSpawn
            // We do not delete tamed creatures, to allow supporting taming of creatures, bringing them to a banned biome and breeding
            if (__instance.m_tamed == false && cDetails.creatureDisabledInBiome) {
                Logger.LogDebug($"Creature {__instance.name} in biome {cDetails.Biome} selected for deletion.");
                ZNetScene.instance.StartCoroutine(DestroyCoroutine(__instance.gameObject));
                return;
            }

            // Modify the creatures stats by custom character/biome modifications
            ApplySpeedModifications(__instance, cDetails);
            ApplyDamageModification(__instance, cDetails);
            LoadApplySizeModifications(__instance.gameObject, __instance.m_nview, cDetails, refresh_cache);

            if (__instance.m_level <= 1) { return; }
            // Colorization and visual adjustments
            Colorization.ApplyColorizationWithoutLevelEffects(__instance, cDetails.Colorization);
            Colorization.ApplyLevelVisual(__instance);
        }

        internal static void LoadApplySizeModifications(GameObject creature, ZNetView zview, CreatureDetailCache cDetails, bool force_update = false, bool include_existing = false, float bonus = 0f) {
            // Don't scale in dungeons
            if (creature.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false) {
                return;
            }

            float current_size = zview.GetZDO().GetFloat("SLE_Size", 0f);
            if (current_size > 0f && force_update == false) {

                if (cDetails.CreaturePrefab) {
                    Vector3 sizeEstimate = cDetails.CreaturePrefab.transform.localScale * current_size;
                    creature.transform.localScale = sizeEstimate;
                    Logger.LogDebug($"Setting character Size from existing {current_size} -> {sizeEstimate}.");
                }
                Physics.SyncTransforms();
                return;
            }
            if (include_existing && current_size > 0) {
                zview.GetZDO().Set("SLE_Size", current_size + bonus);
                if (cDetails.CreaturePrefab) {
                    Vector3 sizeEstimate = cDetails.CreaturePrefab.transform.localScale * current_size;
                    creature.transform.localScale = sizeEstimate;
                    Logger.LogDebug($"Increasing character size from existing {current_size} + {bonus}.");
                }
                Physics.SyncTransforms();
                return;
            }

            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel];
            float base_size_mod = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size];

            float creature_level_size = (per_level_mod * cDetails.Level);
            float scale = base_size_mod + creature_level_size;

            if (cDetails.CreaturePrefab) {
                Vector3 sizeEstimate = cDetails.CreaturePrefab.transform.localScale * scale;
                creature.transform.localScale = sizeEstimate;
                Logger.LogDebug($"Setting character size {scale} = {base_size_mod} + {creature_level_size}.");
            }
            zview.GetZDO().Set("SLE_Size", scale);
            Physics.SyncTransforms();
        }

        private static void ApplySizeModificationZRefOnly(GameObject obj, ZNetView zview) {
            // Don't scale in dungeons
            if (obj.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false)
            {
                return;
            }
            float current_size = zview.GetZDO().GetFloat("SLE_Size", 0f);
            obj.transform.localScale *= current_size;
            Physics.SyncTransforms();
        }

        private static void ApplySpeedModifications(Character creature, CreatureDetailCache cDetails) {
            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel];
            float base_speed = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Speed];
            float perlevelmod = per_level_mod * (creature.m_level - 1);
            // Modify the creature's speed attributes based on the base speed and per level modifier
            float speedmod = (base_speed + perlevelmod);

            if (cDetails.CreaturePrefab)
            {
                Character refChar = cDetails.CreaturePrefab.GetComponent<Character>();
                if (refChar == null) {
                    Logger.LogWarning($"Creature reference {cDetails.CreaturePrefab.name} does not have a Character component, cannot apply speed modifications.");
                    return;
                }
                creature.m_speed = refChar.m_speed * speedmod;
                creature.m_walkSpeed = refChar.m_walkSpeed * speedmod;
                creature.m_runSpeed = refChar.m_runSpeed * speedmod;
                creature.m_turnSpeed = refChar.m_turnSpeed * speedmod;
                creature.m_flyFastSpeed = refChar.m_flyFastSpeed * speedmod;
                creature.m_flySlowSpeed = refChar.m_flySlowSpeed * speedmod;
                creature.m_flyTurnSpeed = refChar.m_flyTurnSpeed * speedmod;
                creature.m_swimSpeed = refChar.m_swimSpeed * speedmod;
                creature.m_crouchSpeed = refChar.m_crouchSpeed * speedmod;
                Logger.LogDebug($"Applying speed modifications for {creature.name}-{creature.m_level} speed modified by: {speedmod}, b{base_speed} + p{perlevelmod}");
            } else {
                Logger.LogWarning("Creature reference not set, can't apply speed modifiers.");
            }
        }

        public static void ForceUpdateDamageMod(Character creature, float increase_dmg_by)
        {
            float current_dmg_bonus = creature.m_nview.GetZDO().GetFloat("SLE_DMod");
            creature.m_nview.GetZDO().Set("SLE_DMod", current_dmg_bonus + increase_dmg_by);
        }

        private static void ApplyDamageModification(Character creature, CreatureDetailCache cDetails) {
            Humanoid chumanoid = creature.GetComponent<Humanoid>();
            if (chumanoid == null) { return; }

            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel];
            float base_dmg_mod = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseDamage];

            // No changes, do nothing
            if (base_dmg_mod == 1 && per_level_mod == 1) { return; }
            float dmgmod = base_dmg_mod + (per_level_mod * (creature.m_level - 1));
            
            DictionaryDmgNetProperty DamageBonuses = new DictionaryDmgNetProperty("SLE_DBon", creature.m_nview, new Dictionary<DamageType, float>());
            Dictionary<DamageType, float> dmgBonuses = DamageBonuses.Get();
            if (dmgBonuses.Count == 0 && cDetails.CreatureDamageBonus.Count > 0) {
                DamageBonuses.Set(cDetails.CreatureDamageBonus);
            }
            creature.m_nview.GetZDO().Set("SLE_DMod", dmgmod);
            Logger.LogDebug($"Applying damage buffs {creature.name} +{string.Join(",", cDetails.CreatureDamageBonus)}  *{dmgmod}");
        }

    }
}
