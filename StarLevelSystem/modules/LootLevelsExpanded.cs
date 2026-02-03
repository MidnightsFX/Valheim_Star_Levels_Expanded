using BepInEx.Configuration;
using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    public static class LootLevelsExpanded
    {
        public enum LootFactorType
        {
            PerLevel,
            Exponential
        }

        public static LootFactorType SelectedLootFactor = LootFactorType.PerLevel;

        internal static readonly AcceptableValueList<string> AllowedLootFactors = new AcceptableValueList<string>(new string[] {
            LootFactorType.PerLevel.ToString(),
            LootFactorType.Exponential.ToString()
        });

        internal static void LootFactorChanged(object s, EventArgs e)
        {
            SelectedLootFactor = (LootFactorType)Enum.Parse(typeof(LootFactorType), ValConfig.LootDropCalculationType.Value);
        }

        [HarmonyPatch(typeof(CharacterDrop))]
        public static class CalculateLootPerLevelStyle
        {
            //[HarmonyEmitIL(".dumps")]
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CharacterDrop.GenerateDropList))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(CharacterDrop), nameof(CharacterDrop.m_character))),
                    new CodeMatch(OpCodes.Call)
                    ).Advance(2).RemoveInstructions(15).InsertAndAdvance(
                    Transpilers.EmitDelegate(DetermineLootScale)
                    ).ThrowIfNotMatch("Unable to patch Character drop generator, level scaling.");

                return codeMatcher.Instructions();
            }
        }

        [HarmonyPatch(typeof(CharacterDrop))]
        public static class RemoveDropLimit
        {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CharacterDrop.GenerateDropList))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_S),
                    new CodeMatch(OpCodes.Ble)
                    ).Advance(3).RemoveInstructions(2)
                    .ThrowIfNotMatch("Unable to patch Character drop limit removal.");
                return codeMatcher.Instructions();
            }
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        [HarmonyPriority(Priority.Last)]
        public static class ModifyLootPerLevelEffect
        {
            public static bool Prefix(ref List<KeyValuePair<GameObject, int>> __result, CharacterDrop __instance) {
                // Passthrough for things that are not managed by SLS or that do not have characters attached to their drops
                if (__instance.m_character == null) { return true; }
                string name = Utils.GetPrefabName(__instance.m_character.gameObject);
                // Logger.LogDebug($"Checking if character drop is managed by SLS {name}");
                if (LootSystemData.SLS_Drop_Settings == null || LootSystemData.SLS_Drop_Settings.characterSpecificLoot == null) { return true; }
                if (LootSystemData.SLS_Drop_Settings.characterSpecificLoot != null && LootSystemData.SLS_Drop_Settings.characterSpecificLoot.ContainsKey(name) != true) { return true; }

                List<KeyValuePair<GameObject, int>> drop_results = new List<KeyValuePair<GameObject, int>>();
                int level = 1;
                if (__instance.m_character != null) {
                    level = __instance.m_character.m_level;
                }
               
                SelectCharacterLootSettings(__instance.m_character, out DistanceLootModifier distance_bonus);
                // Logger.LogDebug($"SLS Custom drop set for {name} - level {level}");
                // Use modified loot drop settings
                foreach(ExtendedCharacterDrop loot in LootSystemData.SLS_Drop_Settings.characterSpecificLoot[name]) {
                    // Skip this loop drop if it doesn't drop for tamed creatures or only drops for tamed creatures
                    if (__instance.m_character != null) {
                        // Logger.LogDebug($"Checking if drop is tame only or non-tame only.");
                        if (loot.UntamedOnlyDrop && __instance.m_character.IsTamed()) { continue; }
                        if (loot.TamedOnlyDrop && __instance.m_character.IsTamed() != true) { continue; }
                    }

                    float scale_multiplier = 1f;
                        
                    // If chance is enabled, calculate all of the chance characteristics
                    if (loot.Drop.Chance < 1) {
                        float luck_roll = UnityEngine.Random.value;
                        float chance = loot.Drop.Chance;
                        if (loot.ChanceScaleFactor > 0f) { chance *= 1 + (loot.ChanceScaleFactor * level); }
                        // check the chance for this to be rolled
                        if (luck_roll > chance) {
                            // Logger.LogDebug($"Drop {loot.Drop.Prefab} failed random drop chance ({luck_roll} < {chance}).");
                            continue;
                        }
                    }

                    // Set scale modifier for the loot drop, based on chance, if enabled
                    if (loot.UseChanceAsMultiplier) {
                        scale_multiplier = (loot.Drop.Chance * level);
                        // Logger.LogDebug($"Drop {loot.Drop.Prefab} modified by chance and creature level.");
                    }

                    // Modify the multiplier for how many this drops based on local players
                    if (loot.Drop.OnePerPlayer) {
                        scale_multiplier += Player.GetPlayersInRangeXZ(__instance.transform.position, 500f);
                        // Logger.LogDebug($"Drop {loot.Drop.Prefab} modified players in local area.");
                    }

                    int drop_min = loot.Drop.Min;
                    int drop_max = loot.Drop.Max;
                    int drop_base_amount = drop_min;
                    if (drop_min != drop_max) {
                        if (loot.ScalebyMaxLevel) {
                            drop_base_amount = ((drop_max - drop_min) / ValConfig.MaxLevel.Value) * level;
                        } else {
                            drop_base_amount = UnityEngine.Random.Range(drop_min, drop_max);
                        }
                    }

                    if (loot.DoesNotScale == true) {
                        // Apply Random change, and the range of the loot drop
                        // Logger.LogDebug($"Drop {loot.Drop.Prefab} does not scale and will drop {drop_base_amount}");
                        drop_results.Add(new KeyValuePair<GameObject, int>(loot.GameDrop.m_prefab, drop_base_amount));
                        continue;
                    }

                    // Determine the actual amount of the drop
                    int drop = drop_base_amount;
                    float scale_factor = loot.AmountScaleFactor * scale_multiplier;
                    if (scale_factor <= 0f) { scale_factor = 1f; }
                    if (SelectedLootFactor == LootFactorType.PerLevel) {
                        drop = MultiplyLootPerLevel(drop, level, distance_bonus, scale_factor);
                    } else {
                        drop = ExponentLootPerLevel(drop, level, distance_bonus, scale_factor);
                    }

                    // Enforce max drop cap
                    if (loot.MaxScaledAmount > 0 && drop > loot.MaxScaledAmount) { 
                        drop = loot.MaxScaledAmount;
                        Logger.LogDebug($"Drop {loot.Drop.Prefab} capped to {drop}");
                    }

                    //Logger.LogDebug($"Drop {loot.Drop.Prefab} capped to {drop}");
                    // Add the drop to the results
                    if (loot.GameDrop == null || loot.GameDrop.m_prefab == null) {
                        // Logger.LogDebug($"Drop Prefab not yet cached, updating and caching.");
                        loot.ToCharacterDrop();
                    }

                    // Modify the multiplier for how many this drops based on local players
                    if (loot.Drop.OnePerPlayer) {
                        scale_multiplier += Player.GetPlayersInRangeXZ(__instance.transform.position, 500f);
                        // Logger.LogDebug($"Drop {loot.Drop.Prefab} modified players in local area.");
                    }

                    drop_results.Add(new KeyValuePair<GameObject, int>(loot.GameDrop.m_prefab, drop));
                }
                __result = drop_results;
                return false;
            }
        }

        internal static List<KeyValuePair<GameObject, int>> ModifyTreeDropsOrDefault(TreeBase treeInstance, out DropTable modifiedDrops) {
            int level = CompositeLazyCache.GetOrAddCachedTreeEntry(treeInstance.m_nview);
            SelectObjectDistanceBonus(treeInstance.transform, out DistanceLootModifier distance_bonus);
            List<KeyValuePair<GameObject, int>> drops = ModifyObjectDropsOrDefault(treeInstance.m_dropWhenDestroyed, Utils.GetPrefabName(treeInstance.gameObject), level, distance_bonus, out DropTable buildDropTable, DropType.Tree);
            modifiedDrops = buildDropTable;
            return drops;
        }

        internal static List<KeyValuePair<GameObject, int>> ModifyTreeDropsOrDefault(TreeLog treeInstance, out DropTable modifiedDrops) {
            int level = CompositeLazyCache.GetOrAddCachedTreeEntry(treeInstance.m_nview);
            SelectObjectDistanceBonus(treeInstance.transform, out DistanceLootModifier distance_bonus);
            List<KeyValuePair<GameObject, int>> drops = ModifyObjectDropsOrDefault(treeInstance.m_dropWhenDestroyed, Utils.GetPrefabName(treeInstance.gameObject), level, distance_bonus, out DropTable buildDropTable, DropType.Tree);
            modifiedDrops = buildDropTable;
            return drops;
        }

        internal static List<KeyValuePair<GameObject, int>> ModifyRockDropsOrDefault(Transform tform, DropTable droptable, string name, int level, out DropTable modifiedDrops) {
            SelectObjectDistanceBonus(tform, out DistanceLootModifier distance_bonus);
            List<KeyValuePair<GameObject, int>> drops = ModifyObjectDropsOrDefault(droptable, name, level, distance_bonus, out DropTable buildDropTable, DropType.Rock);
            modifiedDrops = buildDropTable;
            return drops;
        }


        internal static List<KeyValuePair<GameObject, int>> ModifyObjectDropsOrDefault(DropTable defaultDrops, string lookupkey, int level, DistanceLootModifier distance_bonus, out DropTable modifiedDrops, DropType type = DropType.Tree) {
            List<KeyValuePair<GameObject, int>> dropList = new List<KeyValuePair<GameObject, int>>();
            modifiedDrops = new DropTable();
            Logger.LogDebug($"Checking for custom drop configuration for {lookupkey}");
            if (LootSystemData.SLS_Drop_Settings != null && LootSystemData.SLS_Drop_Settings.nonCharacterSpecificLoot != null && LootSystemData.SLS_Drop_Settings.nonCharacterSpecificLoot.ContainsKey(lookupkey)) {
                Logger.LogDebug($"Custom loot drops configured for:{lookupkey}");
                foreach (ExtendedObjectDrop eod in LootSystemData.SLS_Drop_Settings.nonCharacterSpecificLoot[lookupkey]) {

                    // If chance is enabled, calculate all of the chance characteristics
                    if (eod.Drop.Chance < 1) {
                        float luck_roll = UnityEngine.Random.value;
                        float chance = eod.Drop.Chance;
                        if (eod.ChanceScaleFactor > 0f) { chance *= 1 + (eod.ChanceScaleFactor * level); }
                        // check the chance for this to be rolled
                        if (luck_roll > chance) {
                            Logger.LogDebug($"Drop {eod.Drop.Prefab} failed random drop chance ({luck_roll} < {chance}).");
                            continue;
                        }
                    }

                    if (eod.DropGo == null) {
                        // Logger.LogDebug($"Drop Prefab not yet cached, updating and caching.");
                        eod.ResolveDropPrefab();
                        if (eod.DropGo == null) {
                            Logger.LogWarning($"Prefab {eod.Drop.Prefab} was not found, ensure it is spelled correctly and available in the game. This lootdrop will be skipped.");
                            continue;
                        }
                    }

                    if (eod.Drop.DontScale == true) {
                        // Apply Random change, and the range of the loot drop
                        // Logger.LogDebug($"Drop {loot.Drop.Prefab} does not scale and will drop {drop_base_amount}");
                        dropList.Add(new KeyValuePair<GameObject, int>(eod.DropGo, UnityEngine.Random.Range(eod.Drop.Min,eod.Drop.Max)));
                        continue;
                    }

                    float scale_multiplier = 1f;
                    int drop_min = eod.Drop.Min;
                    int drop_max = eod.Drop.Max;
                    int drop_base_amount = drop_min;
                    if (drop_min != drop_max) {
                        drop_base_amount = UnityEngine.Random.Range(drop_min, drop_max);
                    }

                    // Set scale modifier for the loot drop, based on chance, if enabled
                    if (eod.UseChanceAsMultiplier) {
                        scale_multiplier = (eod.Drop.Chance * level);
                        // Logger.LogDebug($"Drop {loot.Drop.Prefab} modified by chance and creature level.");
                    }

                    // Determine the actual amount of the drop
                    int drop = drop_base_amount;
                    float scale_factor = eod.AmountScaleFactor * scale_multiplier;
                    if (scale_factor <= 0f) { scale_factor = 1f; }
                    if (SelectedLootFactor == LootFactorType.PerLevel) {
                        drop = MultiplyLootPerLevel(drop, level, distance_bonus, scale_factor);
                    } else {
                        drop = ExponentLootPerLevel(drop, level, distance_bonus, scale_factor);
                    }

                    // Enforce max drop cap
                    if (eod.MaxScaledAmount > 0 && drop > eod.MaxScaledAmount) {
                        drop = eod.MaxScaledAmount;
                        Logger.LogDebug($"Drop {eod.Drop.Prefab} capped to {drop}");
                    }

                    dropList.Add(new KeyValuePair<GameObject, int>(eod.DropGo, drop));

                    modifiedDrops.m_drops.Add(new DropTable.DropData() { m_item = eod.DropGo, m_stackMax = drop, m_stackMin = drop, m_weight = 1, m_dontScale = true });
                }
                
            } else {
                // generate the default loot list for this tree
                if (defaultDrops == null) { return dropList; }
                DropTable updatedDropTable = new DropTable();
                switch (type) {
                    case DropType.Tree:
                        Logger.LogDebug($"Updating Tree drops with: level-{level}");
                        updatedDropTable = UpdateDroptableByLevel(defaultDrops, level, ValConfig.PerLevelTreeLootScale.Value);
                        break;
                    case DropType.Rock:
                        Logger.LogDebug($"Updating Rock drops with: level-{level}");
                        updatedDropTable = UpdateDroptableByLevel(defaultDrops, level, ValConfig.PerLevelMineRockLootScale.Value);
                        break;
                    case DropType.Destructible:
                        Logger.LogDebug($"Updating Destructible drops with: level-{level}");
                        updatedDropTable = UpdateDroptableByLevel(defaultDrops, level, ValConfig.PerLevelDestructibleLootScale.Value);
                        break;
                }
                modifiedDrops = updatedDropTable;
                dropList = OptimizeToDropStacks(updatedDropTable.GetDropList());
            }
            return dropList;
        }

        internal static DropTable UpdateDroptableByLevel(DropTable droptable, int level, float mod) {
            // Update For pure level based distance scaling on rocks
            if (level > 1) {
                DropTable newDropTable = droptable.Clone();
                List<DropTable.DropData> dropReplacement = new List<DropTable.DropData>();
                foreach (DropTable.DropData drop in droptable.m_drops) {
                    DropTable.DropData newDrop = drop;
                    newDrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (1 + (mod * level)));
                    newDrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (1 + (mod * level)));
                    Logger.LogDebug($"Scaling drop {drop.m_item} from {drop.m_stackMin}-{drop.m_stackMax} to {newDrop.m_stackMin}-{newDrop.m_stackMax} for level {level}.");
                    dropReplacement.Add(newDrop);
                }
                newDropTable.m_drops = dropReplacement;
                return newDropTable;
            }
            return droptable;
        }

        // This determines the "level" that is used to generate loot multipled by level in vanilla configurations
        private static int DetermineLootScale(Character character) {
            int char_level = 1;
            if (character != null) { char_level = character.GetLevel(); }
            SelectCharacterLootSettings(character, out DistanceLootModifier distance_bonus);
            if (SelectedLootFactor == LootFactorType.PerLevel) {
                float min_drop_scale = char_level * (distance_bonus.MinAmountScaleFactorBonus + ValConfig.PerLevelLootScale.Value);
                float max_drop_scale = char_level * (distance_bonus.MaxAmountScaleFactorBonus + ValConfig.PerLevelLootScale.Value);
                return Mathf.RoundToInt(UnityEngine.Random.Range(min_drop_scale, max_drop_scale));
            } else {
                float min = Mathf.Pow((ValConfig.PerLevelLootScale.Value + distance_bonus.MinAmountScaleFactorBonus), char_level);
                float max = Mathf.Pow((ValConfig.PerLevelLootScale.Value + distance_bonus.MaxAmountScaleFactorBonus), char_level);
                return Mathf.RoundToInt(UnityEngine.Random.Range(min, max));
            }
        }


        // This builds the total amount of loot from this style of loot modification
        // Loot amount x level x modifiers 
        private static int MultiplyLootPerLevel(int lootdrop_amount, int level, DistanceLootModifier dmod, float scale_factor = 1f)
        {
            if (level == 1) { return lootdrop_amount; } // no scaling for level 1, just return the base loot amount
            float min_drop_scale = (level * ValConfig.PerLevelLootScale.Value) * (scale_factor + dmod.MinAmountScaleFactorBonus);
            float max_drop_scale = (level * ValConfig.PerLevelLootScale.Value) * (scale_factor + dmod.MaxAmountScaleFactorBonus);
            // If its the same no need to randomize
            if (min_drop_scale == max_drop_scale) {
                int result = Mathf.RoundToInt(min_drop_scale * lootdrop_amount);
                Logger.LogDebug($"MultiplyLootPerLevel {result} = drop_base: {lootdrop_amount} * {min_drop_scale} scale from (lvl:{level} * PerLevelLootScale:{ValConfig.PerLevelLootScale.Value}) * (factor:{scale_factor} + scaleFactorBonus:{dmod.MinAmountScaleFactorBonus})");
                return result;
            }
            int min_drop = Mathf.RoundToInt(min_drop_scale * lootdrop_amount);
            int max_drop = Mathf.RoundToInt(max_drop_scale * lootdrop_amount);
            int randomized_result = UnityEngine.Random.Range(min_drop, max_drop);
            Logger.LogDebug($"MultiplyLootPerLevel {randomized_result} from range: ({min_drop} <-> {max_drop}) = drop_base: {lootdrop_amount} * min:{min_drop_scale}/max:{max_drop_scale} scale from (lvl:{level} * PerLevelLootScale:{ValConfig.PerLevelLootScale.Value}) * (factor:{scale_factor} + scaleFactorBonus: min{dmod.MinAmountScaleFactorBonus}/max:{dmod.MaxAmountScaleFactorBonus})");

            return randomized_result;
        }

        private static int ExponentLootPerLevel(int lootdrop_amount, int level, DistanceLootModifier dmod, float scale_factor = 1)
        {
            if (level == 1) { return lootdrop_amount; } // no scaling for level 1, just return the base loot amount
            
            float min_drop_scale = ValConfig.PerLevelLootScale.Value + dmod.MinAmountScaleFactorBonus + scale_factor;
            float max_drop_scale = ValConfig.PerLevelLootScale.Value + dmod.MaxAmountScaleFactorBonus + scale_factor;
            float selectedMod = UnityEngine.Random.Range(min_drop_scale, max_drop_scale);
            float loot_scale_factor = Mathf.Pow(selectedMod, level);
            int result = Mathf.RoundToInt(loot_scale_factor * lootdrop_amount);
            Logger.LogDebug($"ExponentLootPerLevel {result} from range ({min_drop_scale} <-> {max_drop_scale}) = drop_base: {lootdrop_amount} * min:{min_drop_scale}/max:{max_drop_scale} scale from (factor:{scale_factor} + distance:(min){dmod.MinAmountScaleFactorBonus}/(max){dmod.MaxAmountScaleFactorBonus} perlevelscale:{dmod.MinAmountScaleFactorBonus})");
            return result;
        }

        [HarmonyPatch]
        public static class MineRockPerformancePatch {
            //[HarmonyEmitIL(".dumps")]
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(typeof(MineRock), nameof(MineRock.RPC_Hit))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(MineRock), nameof(MineRock.m_dropItems))),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(DropTable), nameof(DropTable.GetDropList)))
                )
                .RemoveInstructions(6)
                .InsertAndAdvance(
                    // new CodeInstruction(OpCodes.Ldarg_0), // load __instance
                    new CodeInstruction(OpCodes.Ldarg_2), // load hitdata
                    Transpilers.EmitDelegate(ModifyMinerockDrops)
                )
                //.CreateLabelOffset(out System.Reflection.Emit.Label label, offset: 31)
                //.InsertAndAdvance(new CodeInstruction(OpCodes.Br, label))
                .RemoveInstructions(30)
                .ThrowIfNotMatch("Unable to patch Minerock performance increase.");

                return codeMatcher.Instructions();
            }
            internal static void ModifyMinerockDrops(MineRock instance, HitData hit) {
                // Modify Loot Drop for minerock5
                List<KeyValuePair<GameObject, int>> optimizeDrops = ModifyRockDropsOrDefault(instance.transform, instance.m_dropItems, Utils.GetPrefabName(instance.gameObject), LevelSystem.DeterministicDetermineRockLevel(instance.gameObject.transform.position), out DropTable droptable);
                Vector3 position = hit.m_point - hit.m_dir * 0.2f + UnityEngine.Random.insideUnitSphere * 0.3f;
                LootLevelsExpanded.DropItemsPreferAsync(position, optimizeDrops, dropThatNonCharacterDrop: true, dropThatTable: droptable);
            }
        }



        [HarmonyPatch(typeof(MineRock5))]
        public static class MineRock5performancePatch {
            //[HarmonyEmitIL(".dumps")]
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(MineRock5.DamageArea))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
                var codeMatcher = new CodeMatcher(instructions, generator);
                if (Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable == false) {
                    Logger.LogDebug("DropThat detected, using non-performance based Minerock5 patch for compat.");
                    codeMatcher
                        .MatchStartForward(
                            new CodeMatch(OpCodes.Ldarg_0),
                            new CodeMatch(OpCodes.Ldfld),
                            new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(DropTable), nameof(DropTable.GetDropList)))
                            )
                        .Advance(1) // includes the Ldarg_0
                        .RemoveInstructions(2)
                        .InsertAndAdvance(
                            Transpilers.EmitDelegate(NonPerformanceBasedMineDrop)
                        ).ThrowIfNotMatch("Unable to patch MineRock5 to provide loot modifications.");

                } else {
                    codeMatcher.MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        new CodeMatch(OpCodes.Ldfld),
                        new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(DropTable), nameof(DropTable.GetDropList)))
                        )
                    .Advance(1)
                    .RemoveInstructions(27) //25? + 2
                    .InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldloc_3),
                        Transpilers.EmitDelegate(MineDrop)
                        )
                    .ThrowIfNotMatch("Unable to patch MineRock5 to handle large drops.");
                }
                return codeMatcher.Instructions();
            }

            internal static void MineDrop(MineRock5 instance, Vector3 vector) {
                int level = LevelSystem.DeterministicDetermineRockLevel(vector);
                List<KeyValuePair<GameObject, int>> optimizeDrops = ModifyRockDropsOrDefault(instance.transform, instance.m_dropItems, Utils.GetPrefabName(instance.gameObject), level, out DropTable droptable);
                LootLevelsExpanded.DropItemsPreferAsync(instance.transform.position, optimizeDrops, dropThatNonCharacterDrop: true, dropThatTable: droptable);
            }
            
            // This is specifically used for compatibility with DropThat, as removing the loop will break DropThats functionality for Minerock5
            internal static List<GameObject> NonPerformanceBasedMineDrop(MineRock5 instance) {
                // Modify Loot Drop for minerock5
                List<GameObject> drops = new List<GameObject>();
                List<KeyValuePair<GameObject, int>> optimizeDrops = ModifyRockDropsOrDefault(instance.transform, instance.m_dropItems, Utils.GetPrefabName(instance.gameObject), LevelSystem.DeterministicDetermineRockLevel(instance.transform.position), out DropTable _);
                foreach (KeyValuePair<GameObject, int> drop in optimizeDrops) {
                    drop.Value.Times(() => drops.Add(drop.Key));
                }
                //Vector3 position = vector + UnityEngine.Random.insideUnitSphere * 0.3f;
                //LootLevelsExpanded.DropItemsPreferAsync(position, optimizeDrops);
                return drops;
            }
        }

        [HarmonyPatch(typeof(DropOnDestroyed))]
        public static class DropItemsNonCharacterPerformancePatch {
            
            //[HarmonyEmitIL(".dumps")]
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(DropOnDestroyed.OnDestroyed))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(DropTable), nameof(DropTable.GetDropList))),
                    new CodeMatch(OpCodes.Stloc_2)
                    ).Advance(1)
                    .InsertAndAdvance(
                        Transpilers.EmitDelegate(DropItemsOnDestroy),
                        new CodeInstruction(OpCodes.Ret)
                        )
                    .ThrowIfNotMatch("Unable to patch DropOnDestroy to handle large drops.");
                return codeMatcher.Instructions();
            }

            private static void DropItemsOnDestroy(DropOnDestroyed instance) {
                int level = LevelSystem.DetermineisticDetermineObjectLevel(instance.transform.position);
                SelectObjectDistanceBonus(instance.transform, out DistanceLootModifier distance_bonus);
                List<KeyValuePair<GameObject, int>> optimizeDrops = ModifyObjectDropsOrDefault(instance.m_dropWhenDestroyed, Utils.GetPrefabName(instance.gameObject), level, distance_bonus, out DropTable droptable, DropType.Destructible);
                LootLevelsExpanded.DropItemsPreferAsync(instance.transform.position, optimizeDrops, dropThatNonCharacterDrop: true, dropThatTable: droptable);
            }
        }

        [HarmonyPatch(typeof(TreeBase))]
        public static class DropItemsTreeBasePerformancePatch {

            //[HarmonyEmitIL(".dumps")]
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TreeBase.RPC_Damage))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);

                if (Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable == false) {
                    Logger.LogDebug("DropThat detected, using non-performance based TreeBase patch for compat.");
                    codeMatcher
                        .MatchStartForward(
                            new CodeMatch(OpCodes.Ldarg_0),
                            new CodeMatch(OpCodes.Ldfld),
                            new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(DropTable), nameof(DropTable.GetDropList)))
                            )
                        .Advance(1) // includes the Ldarg_0
                        .RemoveInstructions(2)
                        .InsertAndAdvance(
                            Transpilers.EmitDelegate(NonPerformanceBasedTreeBaseDrop)
                        ).ThrowIfNotMatch("Unable to patch TreeBase to provide loot modifications.");
                } else {
                    codeMatcher.MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        new CodeMatch(OpCodes.Ldfld),
                        new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(DropTable), nameof(DropTable.GetDropList)))
                        ).Advance(1)
                        .InsertAndAdvance(
                            Transpilers.EmitDelegate(TreebaseDropDestroyedItems)
                        )
                        //.MatchStartForward(
                        //new CodeMatch(OpCodes.)
                        .RemoveInstructions(54)
                        .Insert(
                            new CodeInstruction(OpCodes.Ldarg_0)
                        )
                        .ThrowIfNotMatch("Unable to patch Treebase to handle large drops.");
                }

                return codeMatcher.Instructions();
            }

            private static void TreebaseDropDestroyedItems(TreeBase instance) {
                List<KeyValuePair<GameObject, int>> optimizeDrops = ModifyTreeDropsOrDefault(instance, out DropTable modifiedDrops);
                LootLevelsExpanded.DropItemsPreferAsync(instance.transform.position, optimizeDrops, dropThatNonCharacterDrop: true, dropThatTable: modifiedDrops);
            }

            internal static List<GameObject> NonPerformanceBasedTreeBaseDrop(TreeBase instance) {
                // Modify Loot Drop for TreeBase
                List<GameObject> drops = new List<GameObject>();
                List<KeyValuePair<GameObject, int>> optimizeDrops = ModifyTreeDropsOrDefault(instance, out DropTable modifiedDrops);
                foreach (KeyValuePair<GameObject, int> drop in optimizeDrops) {
                    drop.Value.Times(() => drops.Add(drop.Key));
                }
                return drops;
            }
        }

        [HarmonyPatch(typeof(CharacterDrop))]
        public static class  DropItemsPerformancePatch {
            // effectively replace the drop items function since we need to drop things in a way that is not insane for large amounts of loot
            [HarmonyPatch(nameof(CharacterDrop.DropItems))]
            public static bool Prefix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea) {
                DropItemsPreferAsync(centerPos, drops, dropThatCharacterDrop: true);
                return false;
            }
        }

        internal static List<KeyValuePair<GameObject, int>> OptimizeToDropStacks(List<GameObject> drops, float lootmult = 1) {
            List<KeyValuePair<GameObject, int>> optimizeDrops = new List<KeyValuePair<GameObject, int>>();
            Dictionary<GameObject, int> dropCollect = new Dictionary<GameObject, int>();
            foreach (GameObject drop in drops) {
                if (dropCollect.ContainsKey(drop)) {
                    dropCollect[drop] += 1;
                    continue;
                }
                dropCollect.Add(drop, 1);
            }
            // Apply loot increases or decreases if we have those set, else just add to the drop list
            if (lootmult != 1) {
                foreach (KeyValuePair<GameObject, int> kvp in dropCollect) {
                    int amount = Mathf.RoundToInt(kvp.Value * lootmult);
                    Logger.LogDebug($"{kvp.Key} loot modified: {kvp.Value} * {lootmult} = {amount}");
                    optimizeDrops.Add(new KeyValuePair<GameObject, int>(kvp.Key, amount));
                }
            } else {
                foreach (KeyValuePair<GameObject, int> ddrop in dropCollect) {
                    optimizeDrops.Add(new KeyValuePair<GameObject, int>(ddrop.Key, ddrop.Value));
                }
            }
            return optimizeDrops;
        }

        public static void DropItemsPreferAsync(Vector3 position, List<KeyValuePair<GameObject, int>> optimizeDrops, bool immediate = false, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false,  DropTable dropThatTable = null) {
            if (Player.m_localPlayer != null && immediate == false) {
                Player.m_localPlayer.StartCoroutine(DropItemsAsync(optimizeDrops, position, 0.5f, dropThatCharacterDrop));
            } else {
                DropItemsImmediate(optimizeDrops, position, 0.5f, dropThatCharacterDrop, dropThatNonCharacterDrop, dropThatTable);
            }
        }

        internal static void DropItemsImmediate(List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false, DropTable dropThatTable = null) {
            int dropindex = 0;
            foreach (var drop in drops) {
                bool set_stack_size = false;
                int max_stack_size = 0;
                var item = drop.Key;
                int amount = drop.Value;
                Logger.LogDebug($"Dropping {item.name} {amount}");
                for (int i = 0; i < amount;) {
                    // Drop the item at the specified position
                    GameObject droppedItem = UnityEngine.Object.Instantiate(item, centerPos, Quaternion.identity);

                    ItemDrop component = droppedItem.GetComponent<ItemDrop>();
                    if (set_stack_size == false) {
                        set_stack_size = true;
                        if (component) { max_stack_size = component.m_itemData.m_shared.m_maxStackSize; }
                    }

                    // Drop in stacks if this is an item
                    if (component is not null) {
                        int remaining = (amount - i);
                        if (remaining > 0) {
                            if (amount > max_stack_size) {
                                component.m_itemData.m_stack = max_stack_size;
                                i += max_stack_size;
                            } else {
                                component.m_itemData.m_stack = remaining;
                                i += remaining;
                            }
                        }
                        component.m_itemData.m_worldLevel = (byte)Game.m_worldLevel;
                    } else {
                        Character chara = droppedItem.GetComponent<Character>();
                        if (chara == null) {
                            chara = droppedItem.GetComponent<Humanoid>();
                        }
                        if (chara != null) {
                            CompositeLazyCache.GetAndSetLocalCache(chara);
                            ModificationExtensionSystem.CreatureSetup(chara, multiply: false);
                        }
                    }

                    // Compat for DropThat
                    // Send the dropped items to drop that to allow modifications
                    if (dropThatCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyDrop(droppedItem, drops, dropindex);
                    }
                    if (dropThatNonCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyInstantiatedObjectDrop(droppedItem, dropThatTable, dropindex);
                    }

                    Rigidbody component2 = droppedItem.GetComponent<Rigidbody>();
                    if ((bool)component2) {
                        Vector3 insideUnitSphere = UnityEngine.Random.insideUnitSphere * dropArea;
                        if (insideUnitSphere.y < 0f) {
                            insideUnitSphere.y = 0f - insideUnitSphere.y;
                        }
                        component2.AddForce(insideUnitSphere * 5f, ForceMode.VelocityChange);
                    }
                    i++;
                }
                dropindex++;
            }
        }

        

        public static IEnumerator DropItemsAsync(List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false, DropTable dropThatTable = null) {
            int obj_spawns = 0;
            int dropindex = 0;
            foreach (var drop in drops)
            {
                bool set_stack_size = false;
                int max_stack_size = 0;
                var item = drop.Key;
                int amount = drop.Value;
                Logger.LogDebug($"Dropping async {item.name} {amount}");
                for (int i = 0; i < amount;) {

                    // Wait for a short duration to avoid dropping too many items at once
                    if (obj_spawns > 0 && obj_spawns % ValConfig.LootDropsPerTick.Value == 0) {
                        yield return new WaitForSeconds(0.1f);
                    }

                    // Drop the item at the specified position
                    GameObject droppedItem = UnityEngine.Object.Instantiate(item, centerPos, Quaternion.identity);
                    obj_spawns++;

                    ItemDrop component = droppedItem.GetComponent<ItemDrop>();
                    if (set_stack_size == false) {
                        set_stack_size = true;
                        if (component) { max_stack_size = component.m_itemData.m_shared.m_maxStackSize; }
                    }

                    // Drop in stacks if this is an item
                    if (component is not null)
                    {
                        int remaining = (amount - i);
                        if (remaining > 0)
                        {
                            if (amount > max_stack_size)
                            {
                                component.m_itemData.m_stack = max_stack_size;
                                i += max_stack_size;
                            }
                            else
                            {
                                component.m_itemData.m_stack = remaining;
                                i += remaining;
                            }
                        }
                        component.m_itemData.m_worldLevel = (byte)Game.m_worldLevel;
                    }
                    else
                    {
                        Character chara = droppedItem.GetComponent<Character>();
                        if (chara == null) {
                            chara = droppedItem.GetComponent<Humanoid>();
                        }
                        
                        if (chara != null) {
                            ModificationExtensionSystem.CreatureSetup(chara, delay: 0.5f);
                        }
                    }

                    // Compat for DropThat
                    // Send the dropped items to drop that to allow modifications
                    if (dropThatCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyDrop(droppedItem, drops, dropindex);
                    }
                    if (dropThatNonCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyInstantiatedObjectDrop(droppedItem, dropThatTable, dropindex);
                    }

                    Rigidbody component2 = droppedItem.GetComponent<Rigidbody>();
                    if ((bool)component2) {
                        Vector3 insideUnitSphere = UnityEngine.Random.insideUnitSphere * dropArea;
                        if (insideUnitSphere.y < 0f) {
                            insideUnitSphere.y = 0f - insideUnitSphere.y;
                        }
                        component2.AddForce(insideUnitSphere * 5f, ForceMode.VelocityChange);
                    }
                    i++;
                }
                dropindex++;
            }

            yield break;
        }

        public static void SelectCharacterLootSettings(Character creature, out DistanceLootModifier distance_bonus)
        {
            Vector3 p = creature.gameObject.transform.position;
            //ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
            //creature_biome = biome;
            float distance_from_center = Vector2.Distance(p, LevelSystem.center);
            distance_bonus = SelectDistanceFromCenterLootBonus(distance_from_center);
            //Logger.LogDebug($"{creature.gameObject.name} {biome} {p}");
        }

        public static void SelectObjectDistanceBonus(Transform tform, out DistanceLootModifier distance_bonus) {
            Vector3 p = tform.position;
            //ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
            float distance_from_center = Vector2.Distance(p, LevelSystem.center);
            distance_bonus = SelectDistanceFromCenterLootBonus(distance_from_center);
            //Logger.LogDebug($"{creature.gameObject.name} {biome} {p}");
        }

        private static DistanceLootModifier SelectDistanceFromCenterLootBonus(float distance_from_center)
        {
            DistanceLootModifier distance_levelup_bonuses = new DistanceLootModifier();
            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LootSystemData.SLS_Drop_Settings != null && LootSystemData.SLS_Drop_Settings.DistanceLootModifier != null)
            {
                // Check if we are in a distance level bonus area
                foreach (KeyValuePair<int, DistanceLootModifier> kvp in LootSystemData.SLS_Drop_Settings.DistanceLootModifier)
                {
                    if (distance_from_center <= kvp.Key)
                    {
                        // Logger.LogDebug($"Distance Loot area: {kvp.Key}");
                        distance_levelup_bonuses = kvp.Value;
                        break;
                    }
                }
            }
            return distance_levelup_bonuses;
        }
    }
}
