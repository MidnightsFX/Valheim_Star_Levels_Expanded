using HarmonyLib;
using Jotunn.Managers;
using PlayFab.EconomyModels;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using static HitData;
using static StarLevelSystem.common.DataObjects;
using DamageType = StarLevelSystem.common.DataObjects.DamageType;

namespace StarLevelSystem.modules
{
    internal static class ModificationExtensionSystem
    {

        public static List<string> ForceLeveledCreatures = new List<string>();

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
                CharacterCacheEntry cDetails = CompositeLazyCache.GetCacheEntry(__instance);
                LoadApplySizeModifications(__instance.gameObject, __instance.m_nview, cDetails);
            }
        }

        // TODO: Address scaling issues
        // This isn't needed with delayed setup due to the weapons already being assigned and present for the creature when the size is applied
        //[HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachItem))]
        //public static class VisualEquipmentScaleToFit
        //{
        //    public static void Postfix(VisEquipment __instance, GameObject __result) {
        //        if (__instance.m_isPlayer == true) { return; }
        //        ApplySizeModificationToObjWhenZReady(__result, __instance.m_nview);
        //    }
        //}

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class CreatureCharacterExtension
        {
            public static void Postfix(Character __instance) {
                CreatureSetup(__instance, delay: ValConfig.InitialDelayBeforeSetup.Value);
            }
        }

        //[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Awake))]
        //public static class PostfixSetupBosses
        //{
        //    public static void Postfix(Humanoid __instance)
        //    {
        //        if (__instance.IsBoss() && ValConfig.ControlBossSpawns.Value || ForceLeveledCreatures.Contains(__instance.name))
        //        {
        //            CreatureSetup(__instance, delay: 1f);
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
        public static class ModifyRagdollHumanoid
        {
            public static void Postfix(Character __instance, Ragdoll ragdoll)
            {
                if (__instance == null || __instance.IsPlayer()) { return; }
                //Logger.LogDebug($"Ragdoll Humanoid created for {__instance.name} with level {__instance.m_level}");
                CharacterCacheEntry cDetails = CompositeLazyCache.GetCacheEntry(__instance);
                if (__instance.m_nview != null) {
                    ApplySizeModificationZRefOnly(ragdoll.gameObject, __instance.m_nview);
                }
                
                if (__instance.m_level > 1 && cDetails != null && cDetails.Colorization != null) {
                    Colorization.ApplyColorizationWithoutLevelEffects(ragdoll.gameObject, cDetails.Colorization);
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        public static class ModifyCharacterVisualsToLevel
        {
            public static void Prefix(Character __instance, ref int level) {
                if (level <= 1) { level = 1; }
            }
        }

        // TODO: Add caching for animation speed adjustments
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        public static class ModifyCharacterAnimationSpeed {
            public static void Postfix(CharacterAnimEvent __instance) {
                if (__instance.m_character != null && __instance.m_character.InAttack()) {
                    CharacterCacheEntry cdc = CompositeLazyCache.GetCacheEntry(__instance.m_character);
                    if (cdc != null && cdc.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] != 1 || cdc != null && cdc.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel] != 0f) {
                        __instance.m_animator.speed = cdc.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] + (cdc.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel] * __instance.m_character.m_level);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetLevelDamageFactor))]
        public static class ModifyDamagePerLevel
        {
            public static bool Prefix(Attack __instance, ref float __result)
            {
                if (__instance.m_character.IsBoss())
                {
                    __result = 1f + (float)Mathf.Max(0, __instance.m_character.GetLevel() - 1) * ValConfig.BossEnemyDamageMultiplier.Value;
                } else {
                    __result = 1f + (float)Mathf.Max(0, __instance.m_character.GetLevel() - 1) * ValConfig.EnemyDamageLevelMultiplier.Value;
                }
                if (ValConfig.EnableDebugOutputForDamage.Value) {
                    Logger.LogDebug($"Setting {__instance.m_character.name} lvl {__instance.m_character.GetLevel() - 1} dmg factor to {__result}");
                }
                return false;
            }
        }

        

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class CharacterDamageModificationApply {
            private static void Prefix(HitData hit, Character __instance) {
                CharacterCacheEntry attackerCharacter = CompositeLazyCache.GetCacheEntry(hit.GetAttacker());
                CharacterCacheEntry damagedCharacter = CompositeLazyCache.GetCacheEntry(__instance);

                if (attackerCharacter != null && attackerCharacter.CreatureDamageBonus != null && attackerCharacter.CreatureDamageBonus.Count > 0) {
                    if (ValConfig.EnableDebugOutputForDamage.Value) {
                        Logger.LogDebug($"{__instance.name} Hit:{hit.GetTotalDamage()} Adding {attackerCharacter.GetDamageBonusDescription()}");
                    }
                    AddDamagesToHit(hit, attackerCharacter.CreatureDamageBonus);
                }

                // Apply damage recieved Modifiers for the target
                if (damagedCharacter != null) {
                    ApplyDamageModifiers(hit, __instance, damagedCharacter.DamageRecievedModifiers);
                }
            }
        }

        public static void UpdateRidingCreaturesForSizeScaling(GameObject creature, CharacterCacheEntry cDetails) {
            if (ValConfig.EnableRidableCreatureSizeFixes.Value == false) { return; }
            // Handle tame specific collider scaling
            Tameable tame = creature.GetComponent<Tameable>();
            if (tame != null && tame.IsTamed()) {
                string name = Utils.GetPrefabName(creature.gameObject);
                //Logger.LogDebug($"Checking Tame collider adjustment for {name} with for level {cDetails.Level}");
                if (name == "Lox") {
                    UpdateLoxCollider(creature.gameObject, cDetails);
                }
                if (name == "Askvin") {
                    UpdateAskavinCollider(creature.gameObject);
                }
            }

        }

        internal static void UpdateLoxCollider(GameObject go, CharacterCacheEntry cDetails) {
            CapsuleCollider loxcc = go.GetComponent<CapsuleCollider>();
            float size_set = (cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel] * cDetails.Level) + cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size];
            float levelchange = (size_set - 1) * 0.1555f;
            //float levelchange = cDetails.Level * 0.016f;  // 3.31 -lvl 20 (size 3), 3.15 -lvl 10 (size 2) or 0.016f per level at default sizing
            loxcc.height = 3f + levelchange;
            loxcc.radius = 0.5f; //1.22?
        }

        internal static void UpdateAskavinCollider(GameObject go) {
            CapsuleCollider askcc = go.GetComponent<CapsuleCollider>();
            askcc.radius = 0.842f;
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

        internal static void ApplyDamageModifiers(HitData hit, Character chara, Dictionary<DamageType, float> damageMods)
        {
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
            if (go != null) {
                // recheck tame status
                Character chara = go.GetComponent<Character>();
                if (chara != null && chara.m_tamed) { yield break; }
                // Remove drops before destroying the creature to ensure that we don't litter drops everywhere
                CharacterDrop cd = go.GetComponent<CharacterDrop>();
                if (cd != null) { GameObject.Destroy(cd); }
                //Logger.LogDebug($"Destroying object {go.name}.");
                ZNetScene.instance.Destroy(go);
            }
        }

        static public IEnumerator DelayedSetupValidateZnet(Character __instance, int level_override = 0, float delay = 1f, bool spawnMultiply = true, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null)
        {
            int times = 0;
            bool status = false;
            while (status == false) {
                // have to wait while we check the ZValid state- otherwise this results in an almost instant loop which will kill the client
                yield return new WaitForSeconds(delay);
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) { continue; }
                
                // Try to ensure that the Zowner gets the creature setup
                // Logger.LogDebug($"{__instance.name} DSVZ owner:{__instance.m_nview.IsOwner()} - {__instance.m_nview.m_zdo.Owned} - {__instance.m_nview.m_zdo.GetOwner()} force:{force}");
                if (__instance.m_nview.m_zdo.Owned == false) { __instance.m_nview.ClaimOwnership(); }
                // Only the owner should setup a creature, OR if someone is controlling it and it was just spawned it is setup immediately
                CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(__instance);
                if (__instance.m_nview.IsOwner() || delay == 0) {
                    //SetupCreatureZOwner(__instance, level_override, spawnMultiply, requiredModifiers);
                    if (__instance.m_nview == null || __instance.m_nview.IsValid() == false) { continue; }
                    //Logger.LogDebug("Setting up creature cache as Z-owner");
                    cce = CompositeLazyCache.GetAndSetLocalCache(__instance, level_override, requiredModifiers);
                    CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, cce, spawnMultiply);
                }
                
                status = CharacterSetup(__instance, cce);
                //Logger.LogDebug($"Setup status: {status}");
                times += 1;
                // We've failed to get the creature setup and we don't have data for it, its not getting setup
                if (times >= ValConfig.FallbackDelayBeforeCreatureSetup.Value - 1) {
                    CharacterCacheEntry scd = CompositeLazyCache.GetAndSetLocalCache(__instance, level_override, requiredModifiers);
                    CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, scd, spawnMultiply);
                    CharacterSetup(__instance, scd);
                    Logger.LogDebug($"{scd.RefCreatureName} running delayed setup.");
                }
                if (times >= ValConfig.FallbackDelayBeforeCreatureSetup.Value) { break; }
            }

            yield break;
        }

        // This is the main entry point for setting up a character
        private static bool CharacterSetup(Character __instance, CharacterCacheEntry cDetails)
        {
            if (__instance == null || cDetails == null || cDetails.Level == 0) { return false; }

            if (ValConfig.ForceControlAllSpawns.Value == true) {
                CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, cDetails);
                cDetails = CompositeLazyCache.GetCacheEntry(__instance); // refresh after running zsetup
            }

            // Determine creature name
            //Logger.LogDebug("Setting creature name.");
            cDetails.CreatureNameLocalizable = CreatureModifiers.BuildCreatureLocalizableName(__instance, cDetails.CreatureModifiers);

            // Run once modifier setup to modify stats on creatures
            CreatureModifiers.RunOnceModifierSetup(__instance, cDetails);

            // Modify the creatures stats by custom character/biome modifications
            CreatureModifiers.SetupModifiers(__instance, cDetails, CompositeLazyCache.GetCreatureModifiers(__instance));
            ApplySpeedModifications(__instance, cDetails);
            ApplyDamageModification(__instance, cDetails);
            LoadApplySizeModifications(__instance.gameObject, __instance.m_nview, cDetails);
            ApplyHealthModifications(__instance, cDetails);

            // Rebuild UI since it may have been created before these changes were applied
            LevelUI.InvalidateCacheEntry(__instance.GetZDOID().ID);

            if (__instance.m_level <= 1) { return true; }
            // Colorization and visual adjustments
            Colorization.ApplyColorizationWithoutLevelEffects(__instance.gameObject, cDetails.Colorization);
            Colorization.ApplyLevelVisual(__instance);

            return true;
        }

        internal static void CreatureSpawnerSetup(Character chara, int leveloverride = 0, bool multiply = true)
        {
            CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(chara, leveloverride);
            CompositeLazyCache.StartZOwnerCreatureRoutines(chara, cce, multiply);
            CreatureSetup(chara, delay: 0.1f);
        }


        // This is the primary flow setup for setting up a character
        internal static void CreatureSetup(Character __instance, int leveloverride = 0, bool multiply = true, float delay = 1f, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null) {
            if (__instance.IsPlayer()) { return; }
            // Setting a zero delay can prevent all other scripts from running by hogging the CPU
            if (delay == 0) { delay = 0.1f; }

            //// Select the creature data
            //CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance, leveloverride);
            //if (cDetails == null) { return; } // For invalid things, skip. This happens when placing TWIG etc (not a valid or awake character)


            // Generally a bad idea to run setup immediately if this is a networked player and the owner hasn't setup the creature
            if (ZNetScene.instance != null) {
                ZNetScene.instance.StartCoroutine(DelayedSetupValidateZnet(__instance, leveloverride, delay: delay, spawnMultiply: multiply, requiredModifiers: requiredModifiers));
            } else {
                Logger.LogDebug("FALLBACK setup of creature, does this client have network issues?");
                CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(__instance, leveloverride, requiredModifiers);
                CompositeLazyCache.StartZOwnerCreatureRoutines(__instance, cce);
                CharacterSetup(__instance, cce);
            }
        }

        internal static void ApplyHealthModifications(Character chara, CharacterCacheEntry cDetails) {
            float chealth = chara.m_health; // base creature health not current total health
            if (!chara.IsPlayer() && Game.m_worldLevel > 0) {
                chealth *= (float)Game.m_worldLevel * Game.instance.m_worldLevelEnemyHPMultiplier;
            }


            if (cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth] != 1 || cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.HealthPerLevel] > 0) {
                float basehp = chealth * cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth];
                float perlvlhp = (chealth * cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.HealthPerLevel] * (chara.GetLevel() - 1));
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

        internal static void SetCreatureSizeFromCCE(Character creature, CharacterCacheEntry cDetails) {
            GameObject creatureref = PrefabManager.Instance.GetPrefab(cDetails.RefCreatureName);
            UpdateSizeZNet(creature.gameObject, creature.m_nview, cDetails, creatureref);
        }

        internal static void UpdateSizeZNet(GameObject creature, ZNetView zview, CharacterCacheEntry cDetails, GameObject creatureref) {
            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel];
            float base_size_mod = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size];

            float creature_level_size = (per_level_mod * cDetails.Level);
            float scale = base_size_mod + creature_level_size;

            if (creatureref) {
                Vector3 sizeEstimate = creatureref.transform.localScale * scale;
                creature.transform.localScale = sizeEstimate;
                //Logger.LogDebug($"Setting character size {scale} = {base_size_mod} + {creature_level_size}.");
                UpdateRidingCreaturesForSizeScaling(creature, cDetails);
            }
            
            zview.GetZDO().Set(SLS_SIZE, scale);
            Physics.SyncTransforms();
        }

        internal static void LoadApplySizeModifications(GameObject creature, ZNetView zview, CharacterCacheEntry cDetails, bool force_update = false, bool include_existing = false, float bonus = 0f) {
            // Don't scale in dungeons
            if (creature.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false || cDetails == null) {
                return;
            }
            float current_size = zview.GetZDO().GetFloat(SLS_SIZE, 0f);

            GameObject creatureref = PrefabManager.Instance.GetPrefab(cDetails.RefCreatureName);

            if (current_size > 0f && force_update == false) {
                if (creatureref) {
                    Vector3 sizeEstimate = creatureref.transform.localScale * current_size;
                    //Logger.LogDebug($"Setting character Size from existing {current_size} -> {sizeEstimate}.");
                    creature.transform.localScale = sizeEstimate;
                    Physics.SyncTransforms();
                    // Maybe check if the vector values are equal
                    UpdateRidingCreaturesForSizeScaling(creature, cDetails);
                }
                return;
            }
            if (include_existing && current_size > 0) {
                zview.GetZDO().Set(SLS_SIZE, current_size + bonus);
                if (creatureref) {
                    Vector3 sizeEstimate = creatureref.transform.localScale * current_size;
                    creature.transform.localScale = sizeEstimate;
                    //Logger.LogDebug($"Increasing character size from existing {current_size} + {bonus}.");
                }
                Physics.SyncTransforms();
                UpdateRidingCreaturesForSizeScaling(creature, cDetails);
                return;
            }

            UpdateSizeZNet(creature, zview, cDetails, creatureref);
        }

        public static void ApplySizeModificationToObjWhenZReady(GameObject obj, ZNetView zview)
        {
            ZNet.instance.StartCoroutine(WaitForZReadyAndApplySize(obj, zview));
        }

        public static IEnumerator WaitForZReadyAndApplySize(GameObject obj, ZNetView zview, int max = 10)
        {
            int attemps = 0;
            while(attemps < max)
            {
                yield return new WaitForSeconds(0.5f);
                attemps++;
                // no new sizes for dead things
                if (obj == null || zview == null || zview.GetZDO() == null) { yield break; }
                float current_size = zview.GetZDO().GetFloat(SLS_SIZE, 0f);
                // Don't scale in dungeons
                if (obj.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false) { yield break; }
                if (current_size == 0f) { continue; }
                obj.transform.localScale *= current_size;
                Physics.SyncTransforms();
                break;
            }
            yield break;
        }

        private static void ApplySizeModificationZRefOnly(GameObject obj, ZNetView zview) {
            if (obj == null || zview == null || zview.GetZDO() == null) { return; }
            // Don't scale in dungeons
            if (obj.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false)
            {
                return;
            }
            float current_size = zview.GetZDO().GetFloat(SLS_SIZE, 0f);
            if (current_size == 0f) { return; }
            obj.transform.localScale *= current_size;
            Physics.SyncTransforms();
        }

        internal static void ApplySpeedModifications(Character creature, CharacterCacheEntry cDetails) {
            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SpeedPerLevel];
            float base_speed = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Speed];
            float perlevelmod = per_level_mod * (creature.m_level - 1);
            // Modify the creature's speed attributes based on the base speed and per level modifier
            float speedmod = (base_speed + perlevelmod);

            string creaturename = cDetails.RefCreatureName;
            creaturename ??= Utils.GetPrefabName(creature.gameObject);
            GameObject creatureRef = PrefabManager.Instance.GetPrefab(creaturename);
            if (creatureRef == null)
            {
                Logger.LogWarning($"Unable to find reference object for {creature.name}, not applying speed modifications");
                return;
            }

            Character refChar = creatureRef.GetComponent<Character>();

            if (refChar == null) {
                Logger.LogWarning($"Unable to find reference character for {creature.name}, not applying speed modifications");
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
        }

        public static void ForceUpdateDamageMod(Character creature, float increase_dmg_by)
        {
            float current_dmg_bonus = creature.m_nview.GetZDO().GetFloat(SLS_DAMAGE_MODIFIER);
            creature.m_nview.GetZDO().Set(SLS_DAMAGE_MODIFIER, current_dmg_bonus + increase_dmg_by);
        }

        internal static void ApplyDamageModification(Character creature, CharacterCacheEntry cDetails, bool updateCache = false) {
            if (creature.m_nview == null || cDetails == null) { return; }
            float per_level_mod = cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.DamagePerLevel];
            float base_dmg_mod = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseDamage];

            // No changes, do nothing
            if (base_dmg_mod == 1 && per_level_mod == 0) { return; }
            float dmgmod = base_dmg_mod + (per_level_mod * (creature.GetLevel() - 1));

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
