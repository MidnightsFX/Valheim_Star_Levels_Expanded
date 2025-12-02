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
                CreatureDetailCache cDetails = CompositeLazyCache.GetCacheOrZDOOnly(__instance);
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
                CreatureSetup(__instance, delay: 2f);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Awake))]
        public static class PostfixSetupBosses {
            public static void Postfix(Humanoid __instance) {
                CreatureSetup(__instance, delay: 2f);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
        public static class ModifyRagdollHumanoid
        {
            public static void Postfix(Character __instance, Ragdoll ragdoll)
            {
                if (__instance == null || __instance.IsPlayer()) { return; }
                //Logger.LogDebug($"Ragdoll Humanoid created for {__instance.name} with level {__instance.m_level}");
                CreatureDetailCache cDetails = CompositeLazyCache.GetCacheOrZDOOnly(__instance);
                if (__instance.m_nview != null) {
                    ApplySizeModificationZRefOnly(ragdoll.gameObject, __instance.m_nview);
                }
                
                if (__instance.m_level > 1 && cDetails != null && cDetails.Colorization != null) {
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


        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        public static class ModifyCharacterAnimationSpeed {
            public static void Postfix(CharacterAnimEvent __instance) {
                if (__instance.m_character != null && __instance.m_character.InAttack()) {
                    CreatureDetailCache cdc = CompositeLazyCache.GetCacheOrZDOOnly(__instance.m_character);
                    if (cdc != null && cdc.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] != 1 || cdc != null && cdc.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel] != 0f) {
                        __instance.m_animator.speed = cdc.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] + (cdc.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel] * cdc.Level);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class CharacterDamageModificationApply {
            private static void Prefix(HitData hit, Character __instance) {
                CreatureDetailCache attackerCharacter = CompositeLazyCache.GetCacheOrZDOOnly(hit.GetAttacker());
                CreatureDetailCache damagedCharacter = CompositeLazyCache.GetCacheOrZDOOnly(__instance);

                //Logger.LogDebug($"Original damage: D:{hit.m_damage.m_damage} fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");
                if (attackerCharacter != null && attackerCharacter.CreatureDamageBonus != null && attackerCharacter.CreatureDamageBonus.Count > 0) {
                    if (ValConfig.EnableDebugOutputForDamage.Value) {
                        Logger.LogDebug($"{attackerCharacter.CreatureName} Hit:{hit.GetTotalDamage()} Adding {attackerCharacter.GetDamageBonusDescription()}");
                    }
                    
                    AddDamagesToHit(hit, attackerCharacter.CreatureDamageBonus);
                }

                // Apply damage recieved Modifiers for the target
                if (damagedCharacter != null) {
                    if (ValConfig.EnableDebugOutputForDamage.Value) {
                        Logger.LogDebug($"Applying Recieved Dmg Mod: Fi:{damagedCharacter.DamageRecievedModifiers[DamageType.Fire]} Fr:{damagedCharacter.DamageRecievedModifiers[DamageType.Frost]} Li:{damagedCharacter.DamageRecievedModifiers[DamageType.Lightning]} Po:{damagedCharacter.DamageRecievedModifiers[DamageType.Poison]} Sp:{damagedCharacter.DamageRecievedModifiers[DamageType.Spirit]} Bl:{damagedCharacter.DamageRecievedModifiers[DamageType.Blunt]} Sl:{damagedCharacter.DamageRecievedModifiers[DamageType.Slash]} Pi:{damagedCharacter.DamageRecievedModifiers[DamageType.Pierce]}");
                    }
                    hit.m_damage.m_fire *= damagedCharacter.DamageRecievedModifiers[DamageType.Fire];
                    hit.m_damage.m_frost *= damagedCharacter.DamageRecievedModifiers[DamageType.Frost];
                    hit.m_damage.m_lightning *= damagedCharacter.DamageRecievedModifiers[DamageType.Lightning];
                    hit.m_damage.m_poison *= damagedCharacter.DamageRecievedModifiers[DamageType.Poison];
                    hit.m_damage.m_spirit *= damagedCharacter.DamageRecievedModifiers[DamageType.Spirit];
                    hit.m_damage.m_blunt *= damagedCharacter.DamageRecievedModifiers[DamageType.Blunt];
                    hit.m_damage.m_slash *= damagedCharacter.DamageRecievedModifiers[DamageType.Slash];
                    hit.m_damage.m_pierce *= damagedCharacter.DamageRecievedModifiers[DamageType.Pierce];
                    if (ValConfig.EnableDebugOutputForDamage.Value)
                    {
                        Logger.LogDebug($"New damages totals will hit {damagedCharacter.CreatureName}: fi:{hit.m_damage.m_fire} fr:{hit.m_damage.m_frost} s:{hit.m_damage.m_spirit} po:{hit.m_damage.m_poison} b:{hit.m_damage.m_blunt} p:{hit.m_damage.m_pierce} s:{hit.m_damage.m_slash}");
                    }
                }
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
        internal static IEnumerator DestroyCoroutine(GameObject go, float delay = 0.2f) {
            yield return new WaitForSeconds(delay);
            ZNetScene.instance.Destroy(go);
        }

        static public void SetupCreatureZOwner(Character __instance, int level_override = 0, bool spawnMultiply = true, Dictionary<string, ModifierType> requiredModifiers = null) {
            if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) { return; }
            //Logger.LogDebug("Setting up creature cache as Z-owner");
            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, __instance.m_nview, new StoredCreatureDetails());
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(__instance, level_override, requiredModifiers, spawnMultiplyCheck: spawnMultiply);
            cZDO.Set(CompositeLazyCache.ZStoredCreatureValuesFromCreatureDetailCache(cdc));
            __instance.SetLevel(cdc.Level);
        }

        static public IEnumerator DelayedSetupValidateZnet(Character __instance, int level_override = 0, bool force = false, float delay = 1f, bool spawnMultiply = true, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null)
        {
            int times = 0;
            bool status = false;
            while (status == false)
            {
                yield return new WaitForSeconds(delay);
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) { continue; }
                // Try to ensure that the Zowner gets the creature setup
                //Logger.LogDebug($"{__instance.name} DSVZ owner:{__instance.m_nview.IsOwner()} force:{force}");
                if (__instance.m_nview.IsOwner() || force == true) {
                    SetupCreatureZOwner(__instance, level_override, spawnMultiply, requiredModifiers);
                }

                status = CharacterSetupLevelChecked(__instance, level_override);
                times += 1;
                // We've failed to get the creature setup and we don't have data for it, its not getting setup
                if (times == ValConfig.DelayBeforeCreatureSetup.Value - 1) {
                    Logger.LogDebug("Creature Details not set, settings creature as non-zowner is the owners network slow?");
                    CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, __instance.m_nview, new StoredCreatureDetails());
                    CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(__instance, level_override, requiredModifiers, spawnMultiplyCheck: spawnMultiply);
                    cZDO.Set(CompositeLazyCache.ZStoredCreatureValuesFromCreatureDetailCache(cdc));
                    __instance.SetLevel(cdc.Level);
                }
                if (times >= ValConfig.DelayBeforeCreatureSetup.Value) { break; }
            }

            yield break;
        }

        internal static bool CharacterSetupLevelChecked(Character __instance, int level_override = 0)
        {
            // Character is gone :shrug:
            if (__instance == null) { return false; }

            // Do not run setup, only use saved ZDO data/cached
            CreatureDetailCache cDetails = CompositeLazyCache.GetCacheOrZDOOnly(__instance);
            //Logger.LogDebug($"Checking Cache {cDetails}");

            if (ValConfig.ForceControlAllSpawns.Value == true)
            {
                cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance, level_override);
                __instance.SetLevel(cDetails.Level);
            }
            if (cDetails == null) { return false; }
            if (cDetails.Modifiers != null) {
                //Logger.LogDebug($"{cDetails.CreatureName}-{cDetails.Level} b{cDetails.Biome} m:{cDetails.Modifiers.Count}");
            }

            // Set boss levels
            if (__instance.IsBoss() && ValConfig.ControlBossSpawns.Value)
            {
                __instance.SetLevel(cDetails.Level);
            }

            // These creatures are always leveled up, only applies level if its not set
            if (ForceLeveledCreatures.Contains(__instance.name))
            {
                __instance.SetLevel(cDetails.Level);
            }

            if (level_override != 0)
            {
                //Logger.LogDebug($"Character level override enabled. Setting level {level_override}");
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
            LoadApplySizeModifications(__instance.gameObject, __instance.m_nview, cDetails);
            ApplyHealthModifications(__instance, cDetails);

            // Rebuild UI since it may have been created before these changes were applied
            LevelUI.InvalidateCacheEntry(__instance.GetZDOID());

            if (__instance.m_level <= 1) { return true; }
            // Colorization and visual adjustments
            Colorization.ApplyColorizationWithoutLevelEffects(__instance.gameObject, cDetails.Colorization);
            Colorization.ApplyLevelVisual(__instance);

            return true;
        }

        internal static void CreatureSetup(Character __instance, int leveloverride = 0, bool multiply = true, float delay = 1f, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null, bool force = false) {
            if (__instance.IsPlayer()) { return; }

            //// Select the creature data
            //CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance, leveloverride);
            //if (cDetails == null) { return; } // For invalid things, skip. This happens when placing TWIG etc (not a valid or awake character)
            

            // Generally a bad idea to run setup immediately if this is a networked player and the owner hasn't setup the creature
            if (ZNetScene.instance != null) {
                ZNetScene.instance.StartCoroutine(DelayedSetupValidateZnet(__instance, leveloverride, delay: delay, force: force, spawnMultiply: multiply, requiredModifiers: requiredModifiers));
            } else {
                Logger.LogDebug("FALLBACK setup of creature, does this client have network issues?");
                CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, __instance.m_nview, new StoredCreatureDetails());
                CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(__instance, leveloverride, requiredModifiers, spawnMultiplyCheck: multiply);
                cZDO.Set(CompositeLazyCache.ZStoredCreatureValuesFromCreatureDetailCache(cdc));
                __instance.SetLevel(cdc.Level);
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

            float current_size = zview.GetZDO().GetFloat(SLS_SIZE, 0f);
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
                zview.GetZDO().Set(SLS_SIZE, current_size + bonus);
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
            zview.GetZDO().Set(SLS_SIZE, scale);
            Physics.SyncTransforms();
        }

        private static void ApplySizeModificationZRefOnly(GameObject obj, ZNetView zview) {
            if (obj == null || zview == null || zview.GetZDO() == null) { return; }
            // Don't scale in dungeons
            if (obj.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false)
            {
                return;
            }
            float current_size = zview.GetZDO().GetFloat(SLS_SIZE, 0f);
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
            float current_dmg_bonus = creature.m_nview.GetZDO().GetFloat(SLS_DAMAGE_MODIFIER);
            creature.m_nview.GetZDO().Set(SLS_DAMAGE_MODIFIER, current_dmg_bonus + increase_dmg_by);
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

            DictionaryDmgNetProperty DamageBonuses = new DictionaryDmgNetProperty(SLS_DAMAGE_BONUSES, creature.m_nview, new Dictionary<DamageType, float>());
            Dictionary<DamageType, float> dmgBonuses = DamageBonuses.Get();
            if (dmgBonuses.Count == 0 && cDetails.CreatureDamageBonus.Count > 0 || updateCache == true) {
                DamageBonuses.Set(cDetails.CreatureDamageBonus);
            }
            creature.m_nview.GetZDO().Set(SLS_DAMAGE_MODIFIER, dmgmod);
            Logger.LogDebug($"Built damage buffs for {creature.name} +{string.Join(",", cDetails.CreatureDamageBonus)}  *{dmgmod}");
        }

        internal static Dictionary<CreaturePerLevelAttribute, float> DetermineCharacterPerLevelStats(BiomeSpecificSetting biome_settings, CreatureSpecificSetting creature_settings)
        {
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
            if (biome_settings != null && biome_settings.CreaturePerLevelValueModifiers != null)
            {
                foreach (var entry in biome_settings.CreaturePerLevelValueModifiers)
                {
                    creaturePerLevelSettings[entry.Key] = entry.Value;
                }
            }
            if (creature_settings != null && creature_settings.CreaturePerLevelValueModifiers != null)
            {
                foreach (var entry in creature_settings.CreaturePerLevelValueModifiers)
                {
                    creaturePerLevelSettings[entry.Key] = entry.Value;
                }
            }
            return creaturePerLevelSettings;
        }

        internal static Dictionary<CreatureBaseAttribute, float> DetermineCreatureBaseStats(BiomeSpecificSetting biome_settings, CreatureSpecificSetting creature_settings)
        {
            Dictionary<CreatureBaseAttribute, float> creatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>() {
                { CreatureBaseAttribute.BaseDamage, 1f },
                { CreatureBaseAttribute.BaseHealth, 1f },
                { CreatureBaseAttribute.Size, 1f },
                { CreatureBaseAttribute.Speed, 1f },
                { CreatureBaseAttribute.AttackSpeed, 1f },
            };

            if (biome_settings != null && biome_settings.CreatureBaseValueModifiers != null)
            {
                foreach (var entry in biome_settings.CreatureBaseValueModifiers)
                {
                    creatureBaseValueModifiers[entry.Key] = entry.Value;
                }
            }
            if (creature_settings != null && creature_settings.CreatureBaseValueModifiers != null)
            {
                foreach (var entry in creature_settings.CreatureBaseValueModifiers)
                {
                    creatureBaseValueModifiers[entry.Key] = entry.Value;
                }
            }
            return creatureBaseValueModifiers;
        }

        internal static Dictionary<DamageType, float> DetermineCreatureDamageRecievedModifiers(BiomeSpecificSetting biome_settings, CreatureSpecificSetting creature_settings)
        {
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

            if (biome_settings != null && biome_settings.DamageRecievedModifiers != null)
            {
                foreach (var entry in biome_settings.DamageRecievedModifiers)
                {
                    damageRecievedModifiers[entry.Key] = entry.Value;
                }
            }
            if (creature_settings != null && creature_settings.DamageRecievedModifiers != null)
            {
                foreach (var entry in creature_settings.DamageRecievedModifiers)
                {
                    damageRecievedModifiers[entry.Key] = entry.Value;
                }
            }
            return damageRecievedModifiers;
        }

    }
}
