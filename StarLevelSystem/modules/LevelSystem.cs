using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    public static class LevelSystem
    {
        public static Vector2 center = new Vector2(0, 0);

        public static void SetAndUpdateCharacterLevel(Character character, int level) {
            if (character == null) { return; }
            character.m_level = level;
            character.SetupMaxHealth();
            if (character.m_nview != null && character.m_nview.GetZDO() != null) {
                character.m_nview.GetZDO().Set(ZDOVars.s_level, level);
            }
        }

        public static void SetCharacterLevelControl(Character chara, int providedLevel) {
            if (ValConfig.ControlSpawnerLevels.Value) {
                CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
                // Logger.LogDebug($"Setting creature level from cache {cdc.Level}");
                chara.SetLevel(cdc.Level);
                //chara.m_level = cdc.Level;
                //chara.m_nview.GetZDO().Set(ZDOVars.s_level, cdc.Level);
                return;
            }
            // Fallback
            // Logger.LogDebug($"Setting creature level from provided {providedLevel}");
            chara.SetLevel(providedLevel);
        }

        public static int DetermineLevel(Character character, ZDO cZDO, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings, int leveloverride = 0)
        {
            if (character == null || cZDO == null) {
                Logger.LogWarning($"Creature null or nview null, cannot set level.");
                return 0;
            }
            if (leveloverride > 0) {
                cZDO.Set(ZDOVars.s_level, leveloverride);
                character.m_level = leveloverride;
                return leveloverride;
            }

            int clevel = cZDO.GetInt(ZDOVars.s_level, 0);
            bool setup = cZDO.GetBool(SLS_SETUP, false);
            //Logger.LogDebug($"Current level from ZDO: {clevel} is-setup? {setup}");
            if (clevel <= 0 && setup == false) {
                // Determine max level
                int max_level = ValConfig.MaxLevel.Value + 1;
                int min_level = -1;
                if (biome_settings != null && biome_settings.BiomeMaxLevelOverride != 0) { max_level = biome_settings.BiomeMaxLevelOverride; }
                if (creature_settings != null && creature_settings.CreatureMaxLevelOverride > -1) { max_level = creature_settings.CreatureMaxLevelOverride; }
                
                if (biome_settings != null && biome_settings.BiomeMinLevelOverride > 0) { min_level = biome_settings.BiomeMinLevelOverride; }
                if (creature_settings != null && creature_settings.CreatureMinLevelOverride > -1) { min_level = creature_settings.CreatureMinLevelOverride; }
                min_level += 1;

                float levelup_roll = UnityEngine.Random.Range(0f, 100f);
                Vector3 p = character.transform.position;
                float distance_from_center = Vector2.Distance(p, center);
                float distance_level_modifier = 1;
                SortedDictionary<int, float> distance_levelup_bonuses = new SortedDictionary<int, float>() { };
                SortedDictionary<int, float> levelup_chances = LevelSystemData.SLE_Level_Settings.DefaultCreatureLevelUpChance;
                if (biome_settings != null) { distance_level_modifier = biome_settings.DistanceScaleModifier; }
                // If we are using distance level bonuses | Check if we are in a distance level bonus area
                if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemData.SLE_Level_Settings.DistanceLevelBonus != null)
                {
                    distance_levelup_bonuses = LevelSystem.SelectDistanceFromCenterLevelBonus(distance_from_center);
                }
                // Apply Night level scalers
                float nightScaleBonus = 1f;
                if (biome_settings != null && biome_settings.NightSettings != null && biome_settings.NightSettings.NightLevelUpChanceScaler != 1) {
                    nightScaleBonus = biome_settings.NightSettings.NightLevelUpChanceScaler;
                }
                if (creature_settings != null && creature_settings.NightSettings != null && creature_settings.NightSettings.NightLevelUpChanceScaler != 1) {
                    nightScaleBonus = creature_settings.NightSettings.NightLevelUpChanceScaler;
                }
                int level = LevelSystem.DetermineLevelRollResult(levelup_roll, max_level, levelup_chances, distance_levelup_bonuses, distance_level_modifier, nightScaleBonus);
                if (min_level > 0 && level < min_level) { level = min_level; }
                //Logger.LogDebug($"Determined level {level} min: {min_level} max {max_level}");
                //character.m_level = level;
                return level;
            }
            return clevel;
        }

        public static int DetermineLevel(GameObject creature, string creature_name, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings) {
            if (creature == null) {
                Logger.LogWarning($"Creature is null, cannot determine level, set 1.");
                return 1;
            }

            float levelup_roll = UnityEngine.Random.Range(0f, 100f);
            // Logger.LogDebug($"levelroll: {levelup_roll}");
            // Check if the creature has an override level
            // Use the default non-biom based levelup chances
            int maxLevel = ValConfig.MaxLevel.Value + 1;
            // Logger.LogDebug($"maxlevel default: {maxLevel}");
            // Determine creature location to check its biome
            // Determine creature max level from biome
            Vector3 p = creature.transform.position;
            float distance_from_center = Vector2.Distance(p, center);
            SortedDictionary<int, float> distance_levelup_bonuses = new SortedDictionary<int, float>() {};
            SortedDictionary<int, float> levelup_chances = LevelSystemData.SLE_Level_Settings.DefaultCreatureLevelUpChance;

            // If we are using distance level bonuses | Check if we are in a distance level bonus area
            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemData.SLE_Level_Settings.DistanceLevelBonus != null) {
                distance_levelup_bonuses = SelectDistanceFromCenterLevelBonus(distance_from_center);
            }

            float distance_level_modifier = 1;
            if (biome_settings != null) { distance_level_modifier = biome_settings.DistanceScaleModifier; }
            // creature specific override
            if (LevelSystemData.SLE_Level_Settings.CreatureConfiguration.ContainsKey(creature_name)) {
                //Logger.LogDebug($"Creature specific config found for {creature_name}");
                if (creature_settings.CustomCreatureLevelUpChance != null) {
                    if (creature_settings.CreatureMaxLevelOverride > -1) { maxLevel = creature_settings.CreatureMaxLevelOverride; }
                    if (creature_settings.CustomCreatureLevelUpChance != null) { levelup_chances = creature_settings.CustomCreatureLevelUpChance; }
                    return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
                }
            }

            // biome override 
            if (biome_settings != null) {
                if (biome_settings.BiomeMaxLevelOverride > 0) { maxLevel = biome_settings.BiomeMaxLevelOverride; }
                if (biome_settings.CustomCreatureLevelUpChance != null) { levelup_chances = biome_settings.CustomCreatureLevelUpChance; }
                return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
            }
            return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
        }

        internal static SortedDictionary<int, float> SelectDistanceFromCenterLevelBonus(float distance_from_center)
        {
            SortedDictionary<int, float> distance_levelup_bonuses = new SortedDictionary<int, float>() { };
            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemData.SLE_Level_Settings.DistanceLevelBonus != null)
            {
                // Check if we are in a distance level bonus area
                foreach (KeyValuePair<int, SortedDictionary<int, float>> kvp in LevelSystemData.SLE_Level_Settings.DistanceLevelBonus)
                {
                    if (distance_from_center <= kvp.Key)
                    {
                        //Logger.LogDebug($"Distance Level area: {kvp.Key}");
                        distance_levelup_bonuses = kvp.Value;
                        break;
                    }
                }
            }
            return distance_levelup_bonuses;
        }

        // Consider decision tree for levelups to reduce iterations
        public static int DetermineLevelRollResult(float roll, int maxLevel, SortedDictionary<int, float> creature_levelup_chance, SortedDictionary<int, float> levelup_bonus, float distance_influence, float nightBonus = 1f) {
            // Do we want to do a re-roll after certain points?
            int selected_level = 0;
            //Logger.LogDebug($"levelup distance bonus entries: {levelup_bonus.Count}");
            //foreach (var lb in levelup_bonus) {
            //    Logger.LogDebug($"levelup bonus: {lb.Key} {lb.Value}");
            //}
            foreach (KeyValuePair<int, float> kvp in creature_levelup_chance) {
                // Logger.LogDebug($"levelup k: {kvp.Key} v: {kvp.Value}");
                if (levelup_bonus.ContainsKey(kvp.Key)) {
                    float distance_bonus = ((1f + levelup_bonus[kvp.Key]) * distance_influence);
                    float levelup_req = kvp.Value * nightBonus * distance_bonus;
                    if (roll >= levelup_req || kvp.Key >= maxLevel) {
                        selected_level = kvp.Key;
                        Logger.LogDebug($"Level Roll: {roll} >= {levelup_req} = {kvp.Value} * {nightBonus} * {distance_bonus} | Selected Level: {selected_level}");
                        break;
                    }
                } else {
                    if (roll >= (kvp.Value * nightBonus) || kvp.Key >= maxLevel) {
                        selected_level = kvp.Key;
                        Logger.LogDebug($"Level Roll: {roll} >= {(kvp.Value * nightBonus)} | Selected Level: {selected_level}");
                        break;
                    }
                }
            }
            return selected_level;
        }

        [HarmonyPatch(typeof(Fish), nameof(Fish.Awake))]
        public static class RandomFishLevelExtension
        {
            public static void Postfix(Fish __instance) {
                if (ValConfig.EnableScalingFish.Value == false) { return; }
                if (__instance.m_nview == null || __instance.m_nview.GetZDO() == null) {  return; }
                int storedLevel = __instance.m_nview.GetZDO().GetInt("SLE_Fish", 0);
                if (storedLevel == 0) {
                    LevelSystem.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                    storedLevel = LevelSystem.DetermineLevel(__instance.gameObject, creature_name, creature_settings, biome_settings);
                    __instance.m_nview.GetZDO().Set("SLE_Fish", storedLevel);
                    __instance.GetComponent<ItemDrop>().SetQuality(storedLevel);
                    __instance.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality = storedLevel + 1;
                }
                if (storedLevel > 1) {
                    float scale = 1 + (ValConfig.FishSizeScalePerLevel.Value * storedLevel);
                    __instance.GetComponent<ItemDrop>().SetQuality(storedLevel);
                    __instance.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality = storedLevel + 1;
                    //Logger.LogDebug($"Setting Fish level {storedLevel} size {scale}.");
                    __instance.transform.localScale *= scale;
                    Physics.SyncTransforms();

                }
            }
        }

        public static void UpdateMaxLevel() {
            IEnumerable<GameObject> fishes = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.name.StartsWith("Fish"));
            foreach (GameObject fish in fishes) {
                if (fish.GetComponent<Fish>() != null) {
                    //Logger.LogDebug($"Updating max quality {fish.gameObject.name}");
                    fish.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality = 999;
                }
            }
        }

        [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Awake))]
        public static class RandomTreeLevelExtension
        {
            public static void Postfix(TreeBase __instance)
            {
                if (ValConfig.EnableTreeScaling.Value == false) { return; }

                int storedLevel = __instance.m_nview.GetZDO().GetInt("SLE_Tree", 0);
                if (storedLevel == 0)
                {
                    LevelSystem.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                    storedLevel = LevelSystem.DetermineLevel(__instance.gameObject, creature_name, creature_settings, biome_settings);
                    __instance.m_nview.GetZDO().Set("SLE_Tree", storedLevel);
                }
                if (storedLevel > 1)
                {
                    float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * storedLevel);
                    __instance.m_health += (__instance.m_health * 0.1f * storedLevel);
                    // Logger.LogDebug($"Setting Tree size {scale}.");
                    __instance.transform.localScale *= scale;
                    List<DropTable.DropData> drops = new List<DropTable.DropData>();
                    foreach (var drop in __instance.m_dropWhenDestroyed.m_drops)
                    {
                        DropTable.DropData lvlupdrop = new DropTable.DropData();
                        // Scale the amount of drops based on level
                        lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (ValConfig.PerLevelLootScale.Value * storedLevel));
                        lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (ValConfig.PerLevelLootScale.Value * storedLevel));
                        // Logger.LogDebug($"Scaling drop {drop.m_item.name} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {storedLevel}.");
                        lvlupdrop.m_item = drop.m_item;
                        drops.Add(lvlupdrop);
                    }
                    Physics.SyncTransforms();
                }
            }
        }

        [HarmonyPatch(typeof(TreeLog))]
        public static class SetTreeLogPassLevel
        {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TreeLog.Destroy))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Call),
                        new CodeMatch(OpCodes.Ldarg_0),
                        new CodeMatch(OpCodes.Call),
                        new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(ZNetScene), nameof(ZNetScene.Destroy)))
                    ).RemoveInstructions(4)
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(TreeLog), nameof(TreeLog.m_subLogPrefab))),
                        new CodeMatch(OpCodes.Ldloc_S),
                        new CodeMatch(OpCodes.Callvirt)
                    ).Advance(1)
                    .RemoveInstructions(10)
                    .InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldloc_S, (byte)10),
                        new CodeInstruction(OpCodes.Ldloc_S, (byte)11),
                        Transpilers.EmitDelegate(SetupTreeLog)
                    //).MatchStartForward(
                    //    new CodeMatch(OpCodes.Blt_S),
                    //    new CodeMatch(OpCodes.Ret)
                    //).Advance(1)
                    //.Insert(
                    //    new CodeInstruction(OpCodes.Ldarg_0),
                    //    Transpilers.EmitDelegate(RemoveTreeLogInst)
                    ).ThrowIfNotMatch("Unable to patch Tree Log Child Spawn Set Level.");

                return codeMatcher.Instructions();
            }

            [HarmonyPatch(nameof(TreeLog.Awake))]
            [HarmonyPostfix]
            static void SetupAwakeLog(TreeLog __instance) {
                int level = __instance.m_nview.GetZDO().GetInt("SLE_Tree", 1);
                UpdateDrops(__instance, level);
                __instance.m_health += (__instance.m_health * 0.1f * level);
                __instance.GetComponent<ImpactEffect>()?.m_damages.Modify(1 + (0.1f * level));
            }

            [HarmonyPatch(nameof(TreeLog.Destroy))]
            [HarmonyPostfix]
            internal static void RemoveTreeLogInst(TreeLog __instance) {
                Logger.LogDebug("Destroying Treelog");
                ZNetScene.instance.Destroy(__instance.gameObject);
            }

            internal static void SetupTreeLog(TreeLog instance, Transform tform, Quaternion qt)
            {
                GameObject go = GameObject.Instantiate(instance.m_subLogPrefab, tform.position, qt);
                ZNetView nview = go.GetComponent<ZNetView>();
                TreeLog tchild = go.GetComponent<TreeLog>();
                //Logger.LogDebug($"Setting Treelog scale {nview}");
                nview.SetLocalScale(instance.transform.localScale);
                // pass on the level
                // Logger.LogDebug($"Getting tree level");
                int level = 1;
                if (instance.m_nview.GetZDO() != null) {
                    Logger.LogDebug("Checking stored Zvalue for tree level");
                    level = instance.m_nview.GetZDO().GetInt("SLE_Tree", 1);
                }
                Logger.LogDebug($"Got Tree level {level}");
                UpdateDrops(tchild, level);
                tchild.m_health += (tchild.m_health * 0.1f * level);
                go.GetComponent<ImpactEffect>()?.m_damages.Modify(1 + (0.1f * level));
                Logger.LogDebug($"Setting tree level {level}");
                nview.GetZDO().Set("SLE_Tree", level);
                // This is the last log point, destroy the parent
                ZNetScene.instance.Destroy(instance.gameObject);
            }

            internal static void UpdateDrops(TreeLog log, int level) {
                if (log.m_dropWhenDestroyed == null || log.m_dropWhenDestroyed.m_drops == null || level == 1) { return; }
                List<DropTable.DropData> drops = new List<DropTable.DropData>();
                // Update Drops
                Logger.LogDebug($"Updating Drops for tree to: {level}");
                foreach (var drop in log.m_dropWhenDestroyed.m_drops) {
                    DropTable.DropData lvlupdrop = new DropTable.DropData();
                    // Scale the amount of drops based on level
                    lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (ValConfig.PerLevelLootScale.Value * level));
                    lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (ValConfig.PerLevelLootScale.Value * level));
                    //Logger.LogDebug($"Scaling drop {drop.m_item.name} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {storedLevel}.");
                    lvlupdrop.m_item = drop.m_item;
                    drops.Add(lvlupdrop);
                }
                log.m_dropWhenDestroyed.m_drops = drops;
            }
        }


        [HarmonyPatch(typeof(RandomFlyingBird), nameof(RandomFlyingBird.Awake))]
        public static class RandomFlyingBirdExtension
        {
            public static void Postfix(RandomFlyingBird __instance)
            {
                if (ValConfig.EnableScalingBirds.Value == false) { return; }
                int storedLevel = __instance.m_nview.GetZDO().GetInt("SLE_Bird", 0);
                if (storedLevel == 0)
                {
                    LevelSystem.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                    storedLevel = LevelSystem.DetermineLevel(__instance.gameObject, creature_name, creature_settings, biome_settings);
                    __instance.m_nview.GetZDO().Set("SLE_Bird", storedLevel);
                }
                if (storedLevel > 1)
                {
                    float scale = 1 + (ValConfig.BirdSizeScalePerLevel.Value * storedLevel);
                    //Logger.LogDebug($"Setting bird size {scale}.");
                    __instance.transform.localScale *= scale;
                    Physics.SyncTransforms();
                    DropOnDestroyed dropondeath = __instance.gameObject.GetComponent<DropOnDestroyed>();
                    List<DropTable.DropData> drops = new List<DropTable.DropData>();
                    foreach (var drop in dropondeath.m_dropWhenDestroyed.m_drops)
                    {
                        DropTable.DropData lvlupdrop = new DropTable.DropData();
                        // Scale the amount of drops based on level
                        lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (ValConfig.PerLevelLootScale.Value * storedLevel));
                        lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (ValConfig.PerLevelLootScale.Value * storedLevel));
                        //Logger.LogDebug($"Scaling drop {drop.m_item.name} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {storedLevel}.");
                        lvlupdrop.m_item = drop.m_item;
                        drops.Add(lvlupdrop);
                    }
                    dropondeath.m_dropWhenDestroyed.m_drops = drops;
                }
            }
        }

        [HarmonyPatch(typeof(Growup))]
        public static class SetGrowUpLevel
        {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(Growup.GrowUpdate))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchForward(true,
                    //new CodeMatch(OpCodes.Ldloc_1),
                    //new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.GetLevel))),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                    ).RemoveInstructions(1).InsertAndAdvance(
                    Transpilers.EmitDelegate(SetupGrownUp)
                    ).ThrowIfNotMatch("Unable to patch child grow up level set.");

                return codeMatcher.Instructions();
            }

            internal static void SetupGrownUp(Character grownup, int level)
            {
                ModificationExtensionSystem.CreatureSetup(grownup, true, level);
            }
        }


        [HarmonyPatch(typeof(Procreation))]
        public static class SetChildLevel
        {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(Procreation.Procreate))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchForward(true,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Tameable), nameof(Tameable.IsTamed))),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetTamed)))
                    ).RemoveInstructions(15).InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc, (byte)6),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    Transpilers.EmitDelegate(SetupChildCharacter)
                    ).ThrowIfNotMatch("Unable to patch child spawn level set.");

                return codeMatcher.Instructions();
            }

            internal static void SetupChildCharacter(Character chara, Procreation proc)
            {
                Logger.LogDebug($"Setting child level for {chara.m_name}");
                if (!ValConfig.RandomizeTameChildrenLevels.Value)
                {
                    int level = Mathf.Max(proc.m_character.GetLevel(), proc.m_minOffspringLevel);
                    Logger.LogDebug($"character specific level {level} being used for child.");
                    ModificationExtensionSystem.CreatureSetup(chara, true, level);
                    chara.SetTamed(true);
                }
                if (ValConfig.SpawnMultiplicationAppliesToTames.Value == false && chara.m_nview.GetZDO() != null) {
                    Logger.LogDebug("Disabling spawn multiplier for tamed child.");
                    chara.m_nview.GetZDO().Set("SLS_DSpwnMlt", true);
                }
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
                __result = __instance.m_character.m_nview.GetZDO().GetFloat("SLE_DMod", 1);
                Logger.LogDebug($"Damage Level Factor: {__result}");
                return false;
            }
        }

        public static void SelectCreatureBiomeSettings(GameObject creature, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome creature_biome) {
            // Determine creature max level from biome
            Vector3 p = creature.transform.position;
            creature_name = Utils.GetPrefabName(creature.gameObject);
            Heightmap.Biome biome = Heightmap.FindBiome(p);
            creature_biome = biome;
            biome_settings = null;
            // Logger.LogDebug($"{creature_name} {biome} {p}");

            if (LevelSystemData.SLE_Level_Settings.BiomeConfiguration != null) {
                bool biome_all_setting_check = LevelSystemData.SLE_Level_Settings.BiomeConfiguration.TryGetValue(Heightmap.Biome.All, out var allBiomeConfig);
                if (biome_all_setting_check)
                {
                    biome_settings = allBiomeConfig;
                }
                //Logger.LogDebug($"Biome all config checked");
                bool biome_setting_check = LevelSystemData.SLE_Level_Settings.BiomeConfiguration.TryGetValue(biome, out var biomeConfig);
                if (biome_setting_check && biome_all_setting_check)
                {
                    biome_settings = Extensions.MergeBiomeConfigs(biomeConfig, allBiomeConfig);
                }
                else if (biome_setting_check)
                {
                    biome_settings = biomeConfig;
                }
                //Logger.LogDebug($"Merged biome configs");
            }

            creature_settings = null;
            if (LevelSystemData.SLE_Level_Settings.CreatureConfiguration != null) {
                bool creature_setting_check = LevelSystemData.SLE_Level_Settings.CreatureConfiguration.TryGetValue(creature_name, out var creatureConfig);
                if (creature_setting_check) { creature_settings = creatureConfig; }
                //Logger.LogDebug($"Set character specific configs");
            }
        }

        [HarmonyPatch(typeof(CreatureSpawner))]
        public static class CreatureSpawnerSpawn
        {
            //[HarmonyEmitIL(".dumps")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CreatureSpawner.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SetCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .Insert(new CodeInstruction(OpCodes.Ldc_I4, -2))
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Stloc_S),
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(2)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, -2))
                .ThrowIfNotMatch("Unable to patch CreatureSpawner.Spawn.");

                return codeMatcher.Instructions();
            }
        }

        [HarmonyPatch(typeof(SpawnArea))]
        public static class SpawnAreaSpawnOnePatch
        {
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnArea.SpawnOne))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SetCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, -2))
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_2),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(SpawnArea.SpawnData), nameof(SpawnArea.SpawnData.m_maxLevel))),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(2)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, -2))
                .ThrowIfNotMatch("Unable to patch SpawnArea.SpawnOne.");

                return codeMatcher.Instructions();
            }
        }

        [HarmonyPatch(typeof(SpawnSystem))]
        public static class SpawnSystemSpawnPatch
        {
            //[HarmonyEmitIL(".dumps")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnSystem.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SetCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, -2))
                .ThrowIfNotMatch("Unable to patch SpawnSystem Spawn set level.");

                return codeMatcher.Instructions();
            }
        }

        [HarmonyPatch(typeof(TriggerSpawner))]
        public static class TriggerSpawnerSpawnPatch
        {
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TriggerSpawner.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SetCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, -2))
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(TriggerSpawner), nameof(TriggerSpawner.m_maxLevel))),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(2)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, -2))
                .ThrowIfNotMatch("Unable to patch TriggerSpawner.Spawn.");

                return codeMatcher.Instructions();
            }
        }

        [HarmonyPatch(typeof(SpawnAbility))]
        public static class SpawnAbilitySpawnPatch
        {
            // Note: This is an IEnumerator so we need to patch the MoveNext method inside the generated class

            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnAbility.Spawn), MethodType.Enumerator)]
            static IEnumerable<CodeInstruction> TranspileMoveNext(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                    ).RemoveInstructions(1).InsertAndAdvance(
                    Transpilers.EmitDelegate(SetSpawnAbilityLevelControl)
                    ).ThrowIfNotMatch("Unable to patch TriggerSpawner Spawn set level.");

                return codeMatcher.Instructions();
            }

            public static void SetSpawnAbilityLevelControl(Character chara, int providedLevel)
            {
                if (ValConfig.ControlAbilitySpawnedCreatures.Value)
                {
                    CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
                    chara.SetLevel(cdc.Level);
                    return;
                }
                // Fallback
                chara.SetLevel(providedLevel);
            }
        }

        // Add support for Seeker egg hatches
        [HarmonyPatch(typeof(EggHatch))]
        public static class EggHatchSpawnPatch
        {
            // Skip original prefix that causes levelup to trigger for seeker broods
            [HarmonyPatch(nameof(EggHatch.Hatch))]
            static bool Prefix(EggHatch __instance) {
                __instance.m_hatchEffect.Create(__instance.transform.position, __instance.transform.rotation, null, 1f, -1);
                GameObject go = UnityEngine.Object.Instantiate(__instance.m_spawnPrefab, __instance.transform.TransformPoint(__instance.m_spawnOffset), Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f));
                Character chara = go.GetComponent<Character>();
                if (chara != null) {
                    CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
                    chara.SetLevel(cdc.Level);
                    ModificationExtensionSystem.CreatureSetup(chara, delayedSetupTimer: 0);
                }
                __instance.m_nview.Destroy();
                return false;
            }
        }

        // Ensure creatures that are spawned as loot drops also get leveled
        [HarmonyPatch(typeof(ItemDrop))]
        public static class DropOnDestroyedSpawnPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(ItemDrop.OnCreateNew), new Type[] { typeof(GameObject) } )]
            static void Postfix(GameObject go) {
                Character chara = go.GetComponent<Character>();
                if (chara != null) {
                    CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
                    chara.SetLevel(cdc.Level);
                    ModificationExtensionSystem.CreatureSetup(chara, delayedSetupTimer: 0);
                }
            }
        }

    }
}
