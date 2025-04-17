using HarmonyLib;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;

namespace StarLevelSystem.modules
{
    public static class LevelSystem
    {
        private static Vector2 center = new Vector2(0, 0);
        public static int DetermineLevel(GameObject creature) {
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
            ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
            Logger.LogDebug($"{creature.gameObject.name} {biome} {p}");
            SortedDictionary<int, float> distance_levelup_bonuses = new SortedDictionary<int, float>() {};

            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemConfiguration.SLE_Global_Settings.DistanceLevelBonus != null) {
                // Check if we are in a distance level bonus area
                foreach (KeyValuePair<int, SortedDictionary<int, float>> kvp in LevelSystemConfiguration.SLE_Global_Settings.DistanceLevelBonus)
                {
                    if (distance_from_center <= kvp.Key)
                    {
                        Logger.LogDebug($"Distance Level area: {kvp.Key}");
                        distance_levelup_bonuses = kvp.Value;
                        break;
                    }
                }
            }
            bool biome_based_config = LevelSystemConfiguration.SLE_Global_Settings.BiomeConfiguration.TryGetValue(biome, out var biomeConfig);
            float distance_level_modifier = 1;
            if (biome_based_config) { biomeConfig.DistanceScaleModifier = distance_level_modifier; }
            // creature specific override
            if (LevelSystemConfiguration.SLE_Global_Settings.CreatureConfiguration.ContainsKey(creature.gameObject.name))
            {
                Logger.LogDebug($"Creature specific config found for {creature.gameObject.name}");
                DataObjects.CreatureSpecificSetting creature_specific_config = LevelSystemConfiguration.SLE_Global_Settings.CreatureConfiguration[creature.gameObject.name];
                if (creature_specific_config.CustomCreatureLevelUpChance != null)
                {
                    if (creature_specific_config.EnableCreatureLevelOverride) { maxLevel = creature_specific_config.CreatureMaxLevelOverride; }
                    return DetermineLevelRollResult(levelup_roll, maxLevel, creature_specific_config.CustomCreatureLevelUpChance, distance_levelup_bonuses, distance_level_modifier);
                }
            }

            // biome override 
            if (biome_based_config)
            {
                if (biomeConfig.CustomCreatureLevelUpChance != null)
                {
                    if (biomeConfig.EnableBiomeLevelOverride == true) { maxLevel = biomeConfig.BiomeMaxLevelOverride; }
                    return DetermineLevelRollResult(levelup_roll, maxLevel, biomeConfig.CustomCreatureLevelUpChance, distance_levelup_bonuses, distance_level_modifier);
                }
            }
            return DetermineLevelRollResult(levelup_roll, maxLevel, LevelSystemConfiguration.SLE_Global_Settings.DefaultCreatureLevelUpChance, distance_levelup_bonuses, distance_level_modifier);
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

        public static void DetermineApplyCreatureLevelOrUpgrade(SpawnSystem.SpawnData critter_def, GameObject creature)
        {
           int level = DetermineLevel(creature);
           if (level > 1) {
                Character component2 = creature.GetComponent<Character>();
                if (component2 != null) {
                    component2.SetLevel(level);
                }
                if (creature.GetComponent<Fish>() != null) {
                    ItemDrop component3 = creature.GetComponent<ItemDrop>();
                    if (component3 != null) {
                        component3.SetQuality(level);
                    }
                }
            } 
        }

        // Do we need to modify the spawner max level if we don't use it?
        public static class ExpandSpawnerTriggerLevel
        {
            [HarmonyPatch(nameof(TriggerSpawner.Awake))]
            public static void Postfix(TriggerSpawner __instance)
            {
                Vector3 p = __instance.transform.position;
                ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
                LevelSystemConfiguration.SLE_Global_Settings.BiomeConfiguration.TryGetValue(biome, out var biomeConfig);

                if (biomeConfig != null) {
                    if (biomeConfig.EnableBiomeLevelOverride == true) { __instance.m_maxLevel = biomeConfig.BiomeMaxLevelOverride; }
                } else {
                    __instance.m_maxLevel = ValConfig.MaxLevel.Value + 1;
                }
            }
        }

        public static class ExpandLevelupRoll
        {
            [HarmonyPatch(typeof(TriggerSpawner))]
            public static class SetupCreatureSpawnerLevelExtendedRoll
            {
                //[HarmonyDebug]
                [HarmonyTranspiler]
                [HarmonyPatch(nameof(TriggerSpawner.Spawn))]
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
                {
                    var codeMatcher = new CodeMatcher(instructions);
                    codeMatcher.MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(TriggerSpawner), nameof(TriggerSpawner.m_minLevel))),
                        new CodeMatch(OpCodes.Stloc_S)
                        ).RemoveInstructions(21).InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldloc_S, (byte)5), // Load the spawned creature
                        Transpilers.EmitDelegate(DetermineLevel),
                        new CodeInstruction(OpCodes.Stloc_S, (byte)8)
                        ).ThrowIfNotMatch("Unable to patch Creature Spawner set level.");

                    return codeMatcher.Instructions();
                }
            }
        }

        public static class ExpandLevelupRollAreaSpawner
        {
            // Does not need a min/max level patch as DetermineLevel will handle it

            //[HarmonyEmitIL("./dumps")]
            [HarmonyPatch(typeof(SpawnArea))]
            public static class SetupAreaSpawnerLevelExtendedRoll
            {
                //[HarmonyDebug]
                [HarmonyTranspiler]
                [HarmonyPatch(nameof(SpawnArea.SpawnOne))]
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
                {
                    var codeMatcher = new CodeMatcher(instructions);
                    codeMatcher.MatchStartForward(
                        new CodeMatch(OpCodes.Ldloc_2),
                        new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(SpawnArea.SpawnData), nameof(SpawnArea.SpawnData.m_maxLevel))),
                        new CodeMatch(OpCodes.Ldc_I4_1)
                        ).RemoveInstructions(27).InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldloc_S, (byte)4), // Load the spawned creature
                        Transpilers.EmitDelegate(DetermineLevel),
                        new CodeInstruction(OpCodes.Stloc_S, (byte)8),
                        new CodeInstruction(OpCodes.Ldloc_S, (byte)5),
                        new CodeInstruction(OpCodes.Ldloc_S, (byte)8)
                        ).ThrowIfNotMatch("Unable to patch Area Spawner set level.");

                    return codeMatcher.Instructions();
                }
            }
        }

        public static class ExpandLevelupRollSpawnSystem {
            // Does not need a patch for max level since its overriden in the spawner

            //[HarmonyEmitIL("./dumps")]
            [HarmonyPatch(typeof(SpawnSystem))]
            public static class SetupAreaSpawnerLevelExtendedRoll
            {
                //[HarmonyDebug]
                [HarmonyTranspiler]
                [HarmonyPatch(nameof(SpawnSystem.Spawn))]
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
                {
                    var codeMatcher = new CodeMatcher(instructions);
                    codeMatcher.MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_1),
                        new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(SpawnSystem.SpawnData), nameof(SpawnSystem.SpawnData.m_levelUpMinCenterDistance))),
                        new CodeMatch(OpCodes.Ldc_R4)
                    ).Advance(1).RemoveInstructions(62).InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldloc_0), // Load the spawned creature
                        Transpilers.EmitDelegate(DetermineApplyCreatureLevelOrUpgrade)
                    ).ThrowIfNotMatch("Unable to patch Spawn System set level.");

                    return codeMatcher.Instructions();
                }
            }
        }

        public static class ExpandLevelupRollCreatureSpawner
        {
            [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Awake))]
            public static class PostfixLevelSetupCreatureSpawner
            {
                public static void Postfix(CreatureSpawner __instance) {
                    Vector3 p = __instance.transform.position;
                    ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
                    try {
                        if (LevelSystemConfiguration.SLE_Global_Settings.BiomeConfiguration.ContainsKey(biome))
                        {
                            DataObjects.BiomeSpecificSetting biomeConfig = LevelSystemConfiguration.SLE_Global_Settings.BiomeConfiguration[biome];
                            if (biomeConfig.BiomeMaxLevelOverride != 0)
                            {
                                __instance.m_maxLevel = biomeConfig.BiomeMaxLevelOverride;
                            }
                            else
                            {
                                __instance.m_maxLevel = ValConfig.MaxLevel.Value + 1;
                            }
                        }
                        else
                        {
                            __instance.m_maxLevel = ValConfig.MaxLevel.Value + 1;
                        }
                    } catch (Exception ex) {
                        Logger.LogWarning($"Exception trying to set CreatureSpawner max level {ex}");
                    }
                    
                }
            }

            //[HarmonyEmitIL("./dumps")]
            [HarmonyPatch(typeof(CreatureSpawner))]
            public static class SetupCreatureSpawnerLevelExtendedRoll
            {
                // [HarmonyDebug]
                [HarmonyTranspiler]
                [HarmonyPatch(nameof(CreatureSpawner.Spawn))]
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
                {
                    var codeMatcher = new CodeMatcher(instructions);
                    codeMatcher.MatchStartForward(
                        new CodeMatch(OpCodes.Ldloc_3),
                        new CodeMatch(OpCodes.Callvirt),
                        new CodeMatch(OpCodes.Stloc_S),
                        new CodeMatch(OpCodes.Ldloc_S),
                        new CodeMatch(OpCodes.Call)
                    ).Advance(6).RemoveInstructions(16).InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldloc_3), // Load the spawned creature
                        Transpilers.EmitDelegate(DetermineLevel),
                        new CodeInstruction(OpCodes.Stloc_S, (byte)11)
                    ).ThrowIfNotMatch("Unable to patch Creature Spawner set level.");

                    return codeMatcher.Instructions();
                }
            }
        }


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

                // 

                //.MatchForward(true,
                //    new CodeMatch(OpCodes.Ldloc_0),
                //    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Game), nameof(Game.m_worldLevel))),
                //    new CodeMatch(OpCodes.Conv_R4),
                //    new CodeMatch(OpCodes.Call),
                //    new CodeMatch(OpCodes.Ldfld),
                //    new CodeMatch(OpCodes.Mul),
                //    new CodeMatch(OpCodes.Mul)
                //    ).Advance(1).RemoveInstructions(6).InsertAndAdvance(
                //    Transpilers.EmitDelegate(CharacterMaxHealthModifiersApplied)
                //    )

                return codeMatcher.Instructions();
            }

            //public static float CharacterMaxHealthModifiersApplied(Character character) {
            //    // Filter out if we want the world level and game instance multipliers to be applied
            //    return character.m_health * (Game.m_worldLevel * ValConfig.EnemyHealthPerWorldLevel.Value);
            //}

            public static float CharacterHealthMultiplier(Character character)
            {
                if (character.IsBoss())
                {
                    return character.m_health * ValConfig.BossEnemyHealthMultiplier.Value;
                } else {
                    return character.m_health * ValConfig.EnemyHealthMultiplier.Value;
                }
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetLevelDamageFactor))]
        public static class SetupMaxLevelDamagehPatch
        {
            public static void Postfix(Attack __instance, float __result) {
                if (__instance.m_character != null && __instance.m_character.IsBoss()) {
                    __result = 1f + (float)Mathf.Max(0, __instance.m_character.GetLevel() - 1) * ValConfig.BossEnemyDamageMultiplier.Value;
                } else {
                    __result = 1f + (float)Mathf.Max(0, __instance.m_character.GetLevel() - 1) * ValConfig.EnemyDamageLevelMultiplier.Value;
                }
            }
        }

    }
}
