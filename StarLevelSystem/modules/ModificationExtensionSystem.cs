using HarmonyLib;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using static HitData;
using static StarLevelSystem.common.DataObjects;
using DamageType = StarLevelSystem.common.DataObjects.DamageType;

namespace StarLevelSystem.modules
{
    internal static class ModificationExtensionSystem
    {

        static List<string> ForceLeveledCreatures = new List<string>();

        internal static void LeveledCreatureListChanged(object s, EventArgs e)
        {
            SetupForceLeveledCreatureList();
        }

        internal static void SetupForceLeveledCreatureList()
        {
            ForceLeveledCreatures.Clear();
            foreach (var item in ValConfig.SpawnsAlwaysControlled.Value.Split(','))
            {
                ForceLeveledCreatures.Add(item);
            }
        }

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

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachItem))]
        public static class VisualEquipmentScaleToFit
        {
            public static void Postfix(VisEquipment __instance, GameObject __result) {
                if (__instance.m_isPlayer == true) { return; }
                ApplySizeModificationZRefOnly(__result, __instance.m_nview);
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

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Awake))]
        public static class PostfixSetupBosses {
            public static void Postfix(Humanoid __instance) {
                CreatureSetup(__instance);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
        public static class ModifyRagdollHumanoid
        {
            public static void Postfix(Character __instance, Ragdoll ragdoll)
            {
                if (__instance == null || __instance.IsPlayer()) { return; }
                //Logger.LogDebug($"Ragdoll Humanoid created for {__instance.name} with level {__instance.m_level}");
                CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance);
                if (__instance.m_nview != null) {
                    ApplySizeModificationZRefOnly(ragdoll.gameObject, __instance.m_nview);
                }
                
                if (__instance.m_level > 1 && cDetails.Colorization != null) {
                    Colorization.ApplyColorizationWithoutLevelEffects(ragdoll.gameObject, cDetails.Colorization);
                }
                CompositeLazyCache.RemoveFromCache(__instance);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        public static class ModifyCharacterVisualsToLevel
        {
            public static void Prefix(Character __instance, ref int level) {
                if (level <= 1) { level = 1; }
            }
        }

            //    public static void CheckSetLevel(Character __instance, int level, MethodBase method) {
            //        Logger.LogDebug($"Character SetLevel called for {__instance.name} with level {level} - {method.Name} {method.DeclaringType} {method.MemberType}");
            //    }
            //}


            [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        public static class ModifyCharacterAnimationSpeed {
            public static void Postfix(CharacterAnimEvent __instance) {
                if (__instance.m_character != null && __instance.m_character.InAttack()) {
                    CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(__instance.m_character);
                    if (cdc != null && cdc.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] != 1 || cdc != null && cdc.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel] != 0f) {
                        __instance.m_animator.speed = cdc.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] + (cdc.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel] * cdc.Level);
                    }
                }
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
                if (zview == null) { return; }

                //Logger.LogDebug($"Original damage: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");

                // Modify damage types based on bonusees
                DictionaryDmgNetProperty DamageBonuses = new DictionaryDmgNetProperty("SLE_DBon", zview, new Dictionary<DamageType, float>());
                //Dictionary<DamageType, float> dbonus = DamageBonuses.Get();
                //foreach (KeyValuePair<DamageType, float> kvp in dbonus) {
                //    Logger.LogDebug($"Damage Bonus: {kvp.Key} {kvp.Value}");
                //}

                AddDamagesToHit(hit, DamageBonuses.Get());
                //Logger.LogDebug($"Adding bonus damage, new totals: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");
                // Damage Modifiers per level etc are all applied in the SetupMaxLevelDamagePatch patch

                // Apply damage recieved Modifiers for the target
                CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(__instance);
                if (cdc == null) { return; }
                Logger.LogDebug($"Damage recieved mods: Fire:{cdc.DamageRecievedModifiers[DamageType.Fire]} Frost:{cdc.DamageRecievedModifiers[DamageType.Frost]} Lightning:{cdc.DamageRecievedModifiers[DamageType.Lightning]} Poison:{cdc.DamageRecievedModifiers[DamageType.Poison]} Spirit:{cdc.DamageRecievedModifiers[DamageType.Spirit]} Blunt:{cdc.DamageRecievedModifiers[DamageType.Blunt]} Slash:{cdc.DamageRecievedModifiers[DamageType.Slash]} Pierce:{cdc.DamageRecievedModifiers[DamageType.Pierce]}");
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

        // For debugging full details on damage calculations

        //[HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        //public static class CharacterApplyDamage
        //{
        //    private static void Prefix(HitData hit, Character __instance)
        //    {
        //        Logger.LogDebug($"Applying Damage: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");
        //    }
        //}

        //[HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
        //public static class ApplyResistance
        //{
        //    private static void Postfix(HitData __instance, DamageModifiers modifiers)
        //    {
        //        Logger.LogDebug($"Applying {modifiers} Modifiers: D:{__instance.m_damage.m_damage} fi:{__instance.m_damage.m_fire} fr:{__instance.m_damage.m_frost} s:{__instance.m_damage.m_spirit} po:{__instance.m_damage.m_poison} b:{__instance.m_damage.m_blunt} p:{__instance.m_damage.m_pierce} s:{__instance.m_damage.m_slash}");
        //    }
        //}

        

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

        // Delayed destruction of an object so that it can finish being setup- otherwise there are lots of vanilla scripts that explode
        // Since apparently instanciating and destroying something in the same frame breaks vanilla assumptions :sigh:
        static IEnumerator DestroyCoroutine(GameObject go, float delay = 0.2f) {
            yield return new WaitForSeconds(delay);
            ZNetScene.instance.Destroy(go);
        }

        static IEnumerator DelayedSetup(Character __instance, bool refresh_cache, int level_override = 0, float delay = 1f) {
            yield return new WaitForSeconds(delay);

            CharacterSetupLevelChecked(__instance, refresh_cache, level_override);
            yield break;
        }

        internal static void ImmediateSetup(Character __instance, bool refresh_cache, int level_override = 0)
        {
            CharacterSetupLevelChecked(__instance,refresh_cache, level_override);
        }

        internal static bool CharacterSetupLevelChecked(Character __instance, bool refresh_cache, int level_override = 0)
        {
            // Character is gone :shrug:
            if (__instance == null) { return false; }

            // TODO: Consider a marker for "already setup" to avoid doing this multiple times
            CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance);

            // Set boss levels
            if (__instance.IsBoss() && ValConfig.ControlBossSpawns.Value)
            {
                __instance.SetLevel(cDetails.Level);
            }
            if (ValConfig.ForceControlAllSpawns.Value == true)
            {
                __instance.SetLevel(cDetails.Level);
            }
            // These creatures are always leveled up, only applies level if its not set
            if (__instance.m_level == 1 && ForceLeveledCreatures.Contains(cDetails.CreatureName))
            {
                __instance.SetLevel(cDetails.Level);
            }

            if (level_override != 0)
            {
                Logger.LogDebug($"Character level override enabled. Setting level {level_override}");
                __instance.SetLevel(level_override);
            }

            // Logic about whether we should use the current level of the creature if it has been modified by something else?
            if (__instance.m_level != cDetails.Level)
            {
                Logger.LogDebug($"Creature {__instance.name} has level {__instance.m_level} but cache says {cDetails.Level}.");
                //cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance, true);
            }

            // Modify the creatures stats by custom character/biome modifications
            CreatureModifiers.SetupModifiers(__instance, cDetails);
            ApplySpeedModifications(__instance, cDetails);
            ApplyDamageModification(__instance, cDetails);
            LoadApplySizeModifications(__instance.gameObject, __instance.m_nview, cDetails, refresh_cache);
            ApplyHealthModifications(__instance, cDetails);

            // Rebuild UI since it may have been created before these changes were applied
            CreatureModifiers.CheckOrBuildCreatureName(__instance, cDetails, false);
            LevelUI.InvalidateCacheEntry(__instance.GetZDOID());

            if (__instance.m_level <= 1) { return true; }
            // Colorization and visual adjustments
            Colorization.ApplyColorizationWithoutLevelEffects(__instance.gameObject, cDetails.Colorization);
            Colorization.ApplyLevelVisual(__instance);

            return true;
        }

        internal static void CreatureSetup(Character __instance, bool refresh_cache = false, int leveloverride = 0, float delayedSetupTimer = 1f, bool rebuildMods = false) {
            if (__instance.IsPlayer()) { return; }

            // Select the creature data
            CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance, refresh_cache, leveloverride, rebuildModifiers: rebuildMods);
            if (cDetails == null) { return; } // For invalid things, skip. This happens when placing TWIG etc (not a valid or awake character)

            // Determine if this creature should get deleted due to disableSpawn
            // We do not delete tamed creatures, to allow supporting taming of creatures, bringing them to a banned biome and breeding
            if (__instance.m_tamed == false && cDetails.CreatureDisabledInBiome) {
                Logger.LogDebug($"Creature {__instance.name} in biome {cDetails.Biome} selected for deletion.");
                ZNetScene.instance.StartCoroutine(DestroyCoroutine(__instance.gameObject));
                return;
            }

            // Bosses get setup right away
            if (__instance.IsBoss()) {
                delayedSetupTimer = 0f;
            }

            if (ZNetScene.instance != null) {
                ZNetScene.instance.StartCoroutine(DelayedSetup(__instance, refresh_cache, leveloverride, delayedSetupTimer));
            } else {
                ImmediateSetup(__instance, refresh_cache, leveloverride);
            }
        }

        internal static void ApplyHealthModifications(Character chara, CreatureDetailCache cDetails) {
            float chealth = chara.m_health;
            if (!chara.IsPlayer() && Game.m_worldLevel > 0) {
                chealth *= (float)Game.m_worldLevel * Game.instance.m_worldLevelEnemyHPMultiplier;
            }

            if (cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth] != 1 || cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.HealthPerLevel] > 0) {
                float basehp = chealth * cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth];
                float perlvlhp = (chealth * cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.HealthPerLevel] * (cDetails.Level - 1));
                float hp = (basehp + perlvlhp);
                chara.SetMaxHealth(hp);
                //Logger.LogDebug($"Setting max HP to: {hp} = {basehp} + {perlvlhp} | base: {chara.m_health} * difficulty = {chealth}");
            } else {
                if (chara.IsBoss()) {
                    chealth *= ValConfig.BossEnemyHealthMultiplier.Value;
                    //Logger.LogDebug($"Setting max HP to: {chara.m_health} * {ValConfig.BossEnemyHealthMultiplier.Value} = {chealth}");
                } else {
                    chealth *= ValConfig.EnemyHealthMultiplier.Value;
                    //Logger.LogDebug($"Setting max HP to: {chara.m_health} * {ValConfig.EnemyHealthMultiplier.Value} = {chealth}");
                }
                chara.SetMaxHealth(chealth);
            }
        }

        internal static void LoadApplySizeModifications(GameObject creature, ZNetView zview, CreatureDetailCache cDetails, bool force_update = false, bool include_existing = false, float bonus = 0f) {
            // Don't scale in dungeons
            if (creature.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false || cDetails == null) {
                return;
            }

            float current_size = zview.GetZDO().GetFloat("SLE_Size", 0f);
            if (current_size > 0f && force_update == false) {
                if (cDetails.CreaturePrefab) {
                    Vector3 sizeEstimate = cDetails.CreaturePrefab.transform.localScale * current_size;
                    // Logger.LogDebug($"Setting character Size from existing {current_size} -> {sizeEstimate}.");
                    creature.transform.localScale = sizeEstimate;
                    Physics.SyncTransforms();
                }
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
                // Logger.LogDebug($"Setting character size {scale} = {base_size_mod} + {creature_level_size}.");
            }
            zview.GetZDO().Set("SLE_Size", scale);
            Physics.SyncTransforms();
        }

        private static void ApplySizeModificationZRefOnly(GameObject obj, ZNetView zview) {
            if (obj == null || zview == null || zview.GetZDO() == null) { return; }
            // Don't scale in dungeons
            if (obj.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false)
            {
                return;
            }
            float current_size = zview.GetZDO().GetFloat("SLE_Size", 0f);
            obj.transform.localScale *= current_size;
            Physics.SyncTransforms();
        }

        internal static void ApplySpeedModifications(Character creature, CreatureDetailCache cDetails) {
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
                // Logger.LogDebug($"Applying speed modifications for {creature.name}-{creature.m_level} speed modified by: {speedmod}, b{base_speed} + p{perlevelmod}");
            } else {
                Logger.LogWarning("Creature reference not set, can't apply speed modifiers.");
            }
        }

        public static void ForceUpdateDamageMod(Character creature, float increase_dmg_by)
        {
            float current_dmg_bonus = creature.m_nview.GetZDO().GetFloat("SLE_DMod");
            creature.m_nview.GetZDO().Set("SLE_DMod", current_dmg_bonus + increase_dmg_by);
        }

        internal static void ApplyDamageModification(Character creature, CreatureDetailCache cDetails, bool updateCache = false) {
            if (creature.m_nview == null || cDetails == null) { return; }
            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel];
            float base_dmg_mod = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseDamage];

            // No changes, do nothing
            if (base_dmg_mod == 1 && per_level_mod == 0) { return; }
            float dmgmod = base_dmg_mod + (per_level_mod * (cDetails.Level - 1));

            //// Debugging
            //foreach (var entry in cDetails.CreatureBaseValueModifiers)
            //{
            //    Logger.LogDebug($"Base Modifier {entry.Key} : {entry.Value}");
            //}
            //foreach (var entry in cDetails.CreaturePerLevelValueModifiers)
            //{
            //    Logger.LogDebug($"Per Level Modifier {entry.Key} : {entry.Value}");
            //}
            //foreach(var entry in cDetails.CreatureDamageBonus) {
            //    Logger.LogDebug($"Damage Bonus {entry.Key} : {entry.Value}");
            //}

            DictionaryDmgNetProperty DamageBonuses = new DictionaryDmgNetProperty("SLE_DBon", creature.m_nview, new Dictionary<DamageType, float>());
            Dictionary<DamageType, float> dmgBonuses = DamageBonuses.Get();
            if (dmgBonuses.Count == 0 && cDetails.CreatureDamageBonus.Count > 0 || updateCache == true) {
                DamageBonuses.Set(cDetails.CreatureDamageBonus);
            }
            creature.m_nview.GetZDO().Set("SLE_DMod", dmgmod);
            Logger.LogDebug($"Applying damage buffs {creature.name} +{string.Join(",", cDetails.CreatureDamageBonus)}  *{dmgmod}");
        }

    }
}
