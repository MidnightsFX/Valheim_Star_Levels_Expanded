using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    public static class LevelSystem
    {
        public static Vector2 center = new Vector2(0, 0);


        public static int DetermineLevelSetAndRetrieve(Character character, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings)
        {
            int clevel = character.m_nview.GetZDO().GetInt(ZDOVars.s_level, 0);
            if (clevel <= 0) {
                // Determine max level
                int max_level = ValConfig.MaxLevel.Value + 1;
                if (biome_settings != null && biome_settings.BiomeMaxLevelOverride != 0) {
                    max_level = biome_settings.BiomeMaxLevelOverride;
                }
                if (creature_settings != null && creature_settings.CreatureMaxLevelOverride != 0) {
                    max_level = creature_settings.CreatureMaxLevelOverride;
                }

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
                int level = LevelSystem.DetermineLevelRollResult(levelup_roll, max_level, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
                character.m_level = level;
                character.m_nview.GetZDO().Set(ZDOVars.s_level, level);
                character.SetupMaxHealth();
                return level;
            }
            return clevel;
        }

        //public static int DetermineLevelInline(GameObject creature)
        //{
        //    SelectCreatureBiomeSettings(creature, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
        //    return DetermineLevel(creature, creature_name, creature_settings, biome_settings);
        //}

        //public static void DetermineApplyLevelInline(GameObject creature)
        //{
        //    SelectCreatureBiomeSettings(creature, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
        //    DetermineApplyLevelGeneric(creature, creature_name, creature_settings, biome_settings);
        //}

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
                Logger.LogDebug($"Creature specific config found for {creature_name}");
                if (creature_settings.CustomCreatureLevelUpChance != null) {
                    if (creature_settings.EnableCreatureLevelOverride) { maxLevel = creature_settings.CreatureMaxLevelOverride; }
                    if (creature_settings.CustomCreatureLevelUpChance != null) { levelup_chances = creature_settings.CustomCreatureLevelUpChance; }
                    return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
                }
            } else {
                if (ValConfig.EnableDebugMode.Value) {
                    Logger.LogDebug($"Creature specific setting not found for {creature_name}.");
                    //foreach (var kvp in LevelSystemData.SLE_Global_Settings.CreatureConfiguration) {
                    //    Logger.LogDebug($"Creature: {kvp.Key}");
                    //}
                }
            }

            // biome override 
            if (biome_settings != null) {
                if (biome_settings.EnableBiomeLevelOverride == true) { maxLevel = biome_settings.BiomeMaxLevelOverride; }
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
                        Logger.LogDebug($"Distance Level area: {kvp.Key}");
                        distance_levelup_bonuses = kvp.Value;
                        break;
                    }
                }
            }
            return distance_levelup_bonuses;
        }

        // Consider decision tree for levelups to reduce iterations
        public static int DetermineLevelRollResult(float roll, int maxLevel, SortedDictionary<int, float> creature_levelup_chance, SortedDictionary<int, float> levelup_bonus, float distance_influence) {
            // Do we want to do a re-roll after certain points?
            int selected_level = 0;
            //Logger.LogDebug($"levelup distance bonus entries: {levelup_bonus.Count}");
            //foreach (var lb in levelup_bonus) {
            //    Logger.LogDebug($"levelup bonus: {lb.Key} {lb.Value}");
            //}
            foreach (KeyValuePair<int, float> kvp in creature_levelup_chance) {
                Logger.LogDebug($"levelup k: {kvp.Key} v: {kvp.Value}");
                if (levelup_bonus.ContainsKey(kvp.Key)) {
                    float distance_bonus = ((1f + levelup_bonus[kvp.Key]) * distance_influence);
                    float levelup_req = kvp.Value * distance_bonus;
                    if (roll >= levelup_req || kvp.Key >= maxLevel) {
                        selected_level = kvp.Key;
                        Logger.LogDebug($"Level Roll: {roll} >= {levelup_req} = {kvp.Value} * {distance_bonus} | Selected Level: {selected_level}");
                        break;
                    }
                } else {
                    if (roll >= kvp.Value || kvp.Key >= maxLevel) {
                        selected_level = kvp.Key;
                        Logger.LogDebug($"Level Roll: {roll} | Selected Level: {selected_level}");
                        break;
                    }
                }
            }
            return selected_level;
        }

        //public static void DetermineApplyCreatureLevelOrUpgrade(SpawnSystem.SpawnData critter_def, GameObject creature)
        //{
        //    SelectCreatureBiomeSettings(creature, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
        //    int level = DetermineLevel(creature, creature_name, creature_settings, biome_settings);
        //    if (level > 1) {
        //        Character component2 = creature.GetComponent<Character>();
        //        component2?.SetLevel(level);
        //        if (creature.GetComponent<Fish>() != null) {
        //            ItemDrop component3 = creature.GetComponent<ItemDrop>();
        //            component3?.SetQuality(level);
        //        }
        //    }
        //}

        //public static void DetermineApplyLevelGeneric(GameObject creature, string creature_name, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings)
        //{
        //    int level = DetermineLevel(creature, creature_name, creature_settings, biome_settings);
        //    if (level > 1)
        //    {
        //        // Creature apply level
        //        Character component2 = creature.GetComponent<Character>();
        //        component2?.SetLevel(level);
        //        // Boss apply level
        //        Humanoid component3 = creature.GetComponent<Humanoid>();
        //        if (component3 != null)
        //        {
        //            component3.SetLevel(level);
        //        }
        //        if (creature.GetComponent<Fish>() != null)
        //        {
        //            ItemDrop component4 = creature.GetComponent<ItemDrop>();
        //            component4?.SetQuality(level);
        //        }
        //    }
        //}

        ////Do we need to modify the spawner max level if we don't use it?
        //public static class ExpandSpawnerTriggerLevel
        //{
        //    [HarmonyPatch(nameof(TriggerSpawner.Awake))]
        //    public static void Postfix(TriggerSpawner __instance)
        //    {
        //        Vector3 p = __instance.transform.position;
        //        ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
        //        LevelSystemData.SLE_Level_Settings.BiomeConfiguration.TryGetValue(biome, out var biomeConfig);

        //        if (biomeConfig != null)
        //        {
        //            if (biomeConfig.EnableBiomeLevelOverride == true) { __instance.m_maxLevel = biomeConfig.BiomeMaxLevelOverride; }
        //        }
        //        else
        //        {
        //            __instance.m_maxLevel = ValConfig.MaxLevel.Value + 1;
        //        }
        //    }
        //}

        //public static class ExpandLevelupRoll
        //{
        //    [HarmonyPatch(typeof(TriggerSpawner))]
        //    public static class SetupCreatureSpawnerLevelExtendedRoll
        //    {
        //        //[HarmonyDebug]
        //        [HarmonyTranspiler]
        //        [HarmonyPatch(nameof(TriggerSpawner.Spawn))]
        //        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
        //        {
        //            var codeMatcher = new CodeMatcher(instructions);
        //            codeMatcher.MatchStartForward(
        //                new CodeMatch(OpCodes.Ldarg_0),
        //                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(TriggerSpawner), nameof(TriggerSpawner.m_minLevel))),
        //                new CodeMatch(OpCodes.Stloc_S)
        //                ).RemoveInstructions(21).InsertAndAdvance(
        //                new CodeInstruction(OpCodes.Ldloc_S, (byte)5), // Load the spawned creature
        //                Transpilers.EmitDelegate(DetermineLevelInline),
        //                new CodeInstruction(OpCodes.Stloc_S, (byte)8)
        //                ).ThrowIfNotMatch("Unable to patch Creature Spawner set level.");

        //            return codeMatcher.Instructions();
        //        }
        //    }
        //}

        public static class ExpandLevelupRollBosses
        {
            // private static readonly string QueenName = "SeekerQueen(Clone)";

            //[HarmonyPatch(typeof(OfferingBowl))]
            //public static class SetupCreatureSpawnerLevelExtendedRoll
            //{
            //    //[HarmonyDebug]
            //    [HarmonyTranspiler]
            //    [HarmonyPatch(nameof(OfferingBowl.DelayedSpawnBoss))]
            //    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            //    {
            //        var codeMatcher = new CodeMatcher(instructions);
            //        codeMatcher.MatchStartForward(
            //            new CodeMatch(OpCodes.Ldarg_0),
            //            new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(OfferingBowl), nameof(OfferingBowl.m_bossPrefab)))
            //            ).Advance(7).InsertAndAdvance(
            //            new CodeInstruction(OpCodes.Ldloc_0),
            //            Transpilers.EmitDelegate(DetermineApplyLevelInline)
            //            ).ThrowIfNotMatch("Unable to patch boss Spawner set level.");
            //        return codeMatcher.Instructions();
            //    }
            //}

            

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Awake))]
            public static class PostfixSetupBosses {
                public static void Postfix(Humanoid __instance) {
                    ModificationExtensionSystem.CreatureSetup(__instance);
                }
            }
        }

        //public static class ExpandLevelupRollAreaSpawner
        //{
        //    // Does not need a min/max level patch as DetermineLevel will handle it

        //    //[HarmonyEmitIL("./dumps")]
        //    [HarmonyPatch(typeof(SpawnArea))]
        //    public static class SetupAreaSpawnerLevelExtendedRoll
        //    {
        //        //[HarmonyDebug]
        //        [HarmonyTranspiler]
        //        [HarmonyPatch(nameof(SpawnArea.SpawnOne))]
        //        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
        //        {
        //            var codeMatcher = new CodeMatcher(instructions);
        //            codeMatcher.MatchStartForward(
        //                new CodeMatch(OpCodes.Ldloc_2),
        //                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(SpawnArea.SpawnData), nameof(SpawnArea.SpawnData.m_maxLevel))),
        //                new CodeMatch(OpCodes.Ldc_I4_1)
        //                ).RemoveInstructions(27).InsertAndAdvance(
        //                new CodeInstruction(OpCodes.Ldloc_S, (byte)4), // Load the spawned creature
        //                Transpilers.EmitDelegate(DetermineLevelInline),
        //                new CodeInstruction(OpCodes.Stloc_S, (byte)8),
        //                new CodeInstruction(OpCodes.Ldloc_S, (byte)5),
        //                new CodeInstruction(OpCodes.Ldloc_S, (byte)8)
        //                ).ThrowIfNotMatch("Unable to patch Area Spawner set level.");

        //            return codeMatcher.Instructions();
        //        }
        //    }
        //}

        //public static class ExpandLevelupRollSpawnSystem {
        //    // Does not need a patch for max level since its overriden in the spawner

        //    //[HarmonyEmitIL("./dumps")]
        //    [HarmonyPatch(typeof(SpawnSystem))]
        //    public static class SetupAreaSpawnerLevelExtendedRoll
        //    {
        //        //[HarmonyDebug]
        //        [HarmonyTranspiler]
        //        [HarmonyPatch(nameof(SpawnSystem.Spawn))]
        //        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
        //        {
        //            var codeMatcher = new CodeMatcher(instructions);
        //            codeMatcher.MatchStartForward(
        //                new CodeMatch(OpCodes.Ldarg_1),
        //                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(SpawnSystem.SpawnData), nameof(SpawnSystem.SpawnData.m_levelUpMinCenterDistance))),
        //                new CodeMatch(OpCodes.Ldc_R4)
        //            ).Advance(1).RemoveInstructions(62).InsertAndAdvance(
        //                new CodeInstruction(OpCodes.Ldloc_0), // Load the spawned creature
        //                Transpilers.EmitDelegate(DetermineApplyCreatureLevelOrUpgrade)
        //            ).ThrowIfNotMatch("Unable to patch Spawn System set level.");

        //            return codeMatcher.Instructions();
        //        }
        //    }
        //}

        //public static class ExpandLevelupRollCreatureSpawner
        //{
        //    [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Awake))]
        //    public static class PostfixLevelSetupCreatureSpawner
        //    {
        //        public static void Postfix(CreatureSpawner __instance) {
        //            Vector3 p = __instance.transform.position;
        //            ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
        //            try {
        //                if (LevelSystemData.SLE_Level_Settings.BiomeConfiguration.ContainsKey(biome))
        //                {
        //                    DataObjects.BiomeSpecificSetting biomeConfig = LevelSystemData.SLE_Level_Settings.BiomeConfiguration[biome];
        //                    if (biomeConfig.BiomeMaxLevelOverride != 0)
        //                    {
        //                        __instance.m_maxLevel = biomeConfig.BiomeMaxLevelOverride;
        //                    }
        //                    else
        //                    {
        //                        __instance.m_maxLevel = ValConfig.MaxLevel.Value + 1;
        //                    }
        //                }
        //                else
        //                {
        //                    __instance.m_maxLevel = ValConfig.MaxLevel.Value + 1;
        //                }
        //            } catch (Exception ex) {
        //                Logger.LogWarning($"Exception trying to set CreatureSpawner max level {ex}");
        //            }
                    
        //        }
        //    }

        //    //[HarmonyEmitIL("./dumps")]
        //    [HarmonyPatch(typeof(CreatureSpawner))]
        //    public static class SetupCreatureSpawnerLevelExtendedRoll
        //    {
        //        // [HarmonyDebug]
        //        [HarmonyTranspiler]
        //        [HarmonyPatch(nameof(CreatureSpawner.Spawn))]
        //        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
        //        {
        //            var codeMatcher = new CodeMatcher(instructions);
        //            codeMatcher.MatchStartForward(
        //                new CodeMatch(OpCodes.Ldloc_3),
        //                new CodeMatch(OpCodes.Callvirt),
        //                new CodeMatch(OpCodes.Stloc_S),
        //                new CodeMatch(OpCodes.Ldloc_S),
        //                new CodeMatch(OpCodes.Call)
        //            ).Advance(6).RemoveInstructions(16).InsertAndAdvance(
        //                new CodeInstruction(OpCodes.Ldloc_3), // Load the spawned creature
        //                Transpilers.EmitDelegate(DetermineLevelInline),
        //                new CodeInstruction(OpCodes.Stloc_S, (byte)11)
        //            ).ThrowIfNotMatch("Unable to patch Creature Spawner set level.");

        //            return codeMatcher.Instructions();
        //        }
        //    }
        //}


        [HarmonyPatch(typeof(Character), nameof(Character.GetMaxHealthBase))]
        public static class SetupMaxLevelHealthPatch
        {
            // Modify the max health that a creature can have based on the levelup health bonus value
            // [HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(Character.GetMaxHealthBase))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Character), nameof(Character.m_health))), // AccessTools.Field(typeof(Character), nameof(Character.m_health)
                    new CodeMatch(OpCodes.Stloc_0)
                    ).Advance(1).RemoveInstructions(1).InsertAndAdvance(
                    Transpilers.EmitDelegate(CharacterHealthMultiplier)
                    ).ThrowIfNotMatch("Unable to patch enemy base health and world level modifiers.");
                return codeMatcher.Instructions();
            }

            //public static float CharacterMaxHealthModifiersApplied(Character character) {
            //    // Filter out if we want the world level and game instance multipliers to be applied
            //    return character.m_health * (Game.m_worldLevel * ValConfig.EnemyHealthPerWorldLevel.Value);
            //}

            public static float CharacterHealthMultiplier(Character character) {
                if (character.IsPlayer()) { return 1f; }
                CreatureDetailCache ccd = CompositeLazyCache.GetAndSetDetailCache(character);
                float base_health_mod = ccd.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth];
                float base_health_per_level = ccd.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.HealthPerLevel];

                float per_level_health_factor = Mathf.Pow(base_health_per_level, character.m_level - 1);
                float base_health = (character.m_health * base_health_mod) * per_level_health_factor;
                //Logger.LogDebug($"Setting {character.m_name} health {character.m_health} to {base_health} using basehealth mod {base_health_mod} and perlevel mod {base_health_per_level} ({per_level_health_factor})");
                if (character.IsBoss()) {
                    // Use character specific customization OR the default configuration
                    if (base_health_per_level != 1f) {
                        return base_health * base_health_per_level;
                    } else {
                        return base_health * ValConfig.BossEnemyHealthMultiplier.Value;
                    }
                } else {
                    if (base_health_per_level != 1f) {
                        return base_health * base_health_per_level;
                    } else {
                        return base_health * ValConfig.EnemyHealthMultiplier.Value;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetLevelDamageFactor))]
        public static class SetupMaxLevelDamagePatch
        {
            public static void Postfix(Attack __instance, float __result) {
                if (__instance.m_character != null && __instance.m_character.IsBoss()) {
                    __result = 1f + (float)Mathf.Max(0, __instance.m_character.GetLevel() - 1) * ValConfig.BossEnemyDamageMultiplier.Value;
                } else {
                    __result = 1f + (float)Mathf.Max(0, __instance.m_character.GetLevel() - 1) * ValConfig.EnemyDamageLevelMultiplier.Value;
                }
            }
        }

        public static void SelectCreatureBiomeSettings(GameObject creature, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome creature_biome) {
            // Determine creature max level from biome
            Vector3 p = creature.transform.position;
            creature_name = Utils.GetPrefabName(creature.gameObject);
            ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
            creature_biome = biome;
            Logger.LogDebug($"{creature_name} {biome} {p}");
            bool biome_setting_check = LevelSystemData.SLE_Level_Settings.BiomeConfiguration.TryGetValue(biome, out var biomeConfig);
            if (biome_setting_check) { biome_settings = biomeConfig; } else { biome_settings = null; }
            bool creature_setting_check = LevelSystemData.SLE_Level_Settings.CreatureConfiguration.TryGetValue(creature_name, out var creatureConfig);
            if (creature_setting_check) { creature_settings = creatureConfig; } else { creature_settings = null; }
        }

    }
}
