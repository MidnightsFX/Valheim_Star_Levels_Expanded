using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Loot {
    internal static class LootPatches {

        [HarmonyPatch(typeof(CharacterDrop))]
        public static class DropItemsPerformancePatch {
            // effectively replace the drop items function since we need to drop things in a way that is not insane for large amounts of loot
            [HarmonyPatch(nameof(CharacterDrop.DropItems))]
            public static bool Prefix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea) {
                LootPerformanceChanges.DropItemsPreferAsync(centerPos, drops, dropThatCharacterDrop: true);
                return false;
            }
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        [HarmonyPriority(Priority.Last)]
        public static class ModifyLootPerLevelEffect {
            public static bool Prefix(ref List<KeyValuePair<GameObject, int>> __result, CharacterDrop __instance) {
                // Passthrough for things that are not managed by SLS or that do not have characters attached to their drops
                if (__instance.m_character == null) { return true; }
                string name = Utils.GetPrefabName(__instance.m_character.gameObject);
                // Logger.LogDebug($"Checking if character drop is managed by SLS {name}");
                if (LootSystemData.SLS_Drop_Settings == null || LootSystemData.SLS_Drop_Settings.characterSpecificLoot == null) { return true; }
                if (LootSystemData.SLS_Drop_Settings.characterSpecificLoot != null && LootSystemData.SLS_Drop_Settings.characterSpecificLoot.ContainsKey(name) != true) { return true; }

                __result = LootStyles.ModifyCharacterDrops(__instance, name);
                return false;
            }
        }

        [HarmonyPatch(typeof(CharacterDrop))]
        public static class CalculateLootPerLevelStyle {
            //[HarmonyEmitIL(".dumps")]
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CharacterDrop.GenerateDropList))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(CharacterDrop), nameof(CharacterDrop.m_character))),
                    new CodeMatch(OpCodes.Call)
                ).Advance(2).RemoveInstructions(15).InsertAndAdvance(
                    Transpilers.EmitDelegate(DetermineLootScale)
                ).ThrowIfNotMatch("Unable to patch Character drop generator, level scaling.")
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(CharacterDrop.Drop), nameof(CharacterDrop.Drop.m_levelMultiplier)))
                ).Advance(1).MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(CharacterDrop.Drop), nameof(CharacterDrop.Drop.m_levelMultiplier)))
                ).Advance(2).InsertAndAdvance(
                    Transpilers.EmitDelegate(OverrideLootScalingEnabler)
                ).MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_S),
                    new CodeMatch(OpCodes.Ble)
                ).Advance(3).RemoveInstructions(2)
                .ThrowIfNotMatch("Unable to patch Character drop limit removal.");

                return codeMatcher.Instructions();
            }

            private static bool OverrideLootScalingEnabler(bool defaultlootLevelMultiplier) {
                if (ValConfig.ScaleAllLootByLevel.Value) { return true; }
                return defaultlootLevelMultiplier;
            }

            // This determines the "level" that is used to generate loot multipled by level in vanilla configurations
            private static int DetermineLootScale(Character character) {
                int char_level = 1;
                if (character != null) { char_level = character.GetLevel(); }
                LootStyles.SelectCharacterLootSettings(character, out DistanceLootModifier distance_bonus);
                float min;
                float max;
                if (LootStyles.SelectedLootFactor == LootFactorType.PerLevel) {
                    min = char_level * (distance_bonus.MinAmountScaleFactorBonus + ValConfig.PerLevelLootScale.Value);
                    max = char_level * (distance_bonus.MaxAmountScaleFactorBonus + ValConfig.PerLevelLootScale.Value);
                } else if (LootStyles.SelectedLootFactor == LootFactorType.Exponential) {
                    min = Mathf.Pow((ValConfig.PerLevelLootScale.Value + distance_bonus.MinAmountScaleFactorBonus), char_level);
                    max = Mathf.Pow((ValConfig.PerLevelLootScale.Value + distance_bonus.MaxAmountScaleFactorBonus), char_level);
                } else {
                    // Fallback just leveled loot scale without distance bonus
                    // With a min of 1 to prevent 0 drops
                    min = char_level * ValConfig.PerLevelLootScale.Value;
                    max = char_level * ValConfig.PerLevelLootScale.Value;
                }
                int result = Mathf.RoundToInt(UnityEngine.Random.Range(min, max));
                if (result < 1) { result = 1; }
                if (ValConfig.EnableDebugLootDetails.Value) {
                    Logger.LogDebug($"Loot Scale {LootStyles.SelectedLootFactor} select {min} <-> {max} selected {result}.");
                }
                return result;
            }
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
                List<KeyValuePair<GameObject, int>> optimizeDrops = LootStyles.ModifyRockDropsOrDefault(instance.transform, instance.m_dropItems, Utils.GetPrefabName(instance.gameObject), LevelSelection.DeterministicDetermineRockLevel(instance.gameObject.transform.position));
                Vector3 position = hit.m_point - hit.m_dir * 0.2f + UnityEngine.Random.insideUnitSphere * 0.3f;
                LootPerformanceChanges.DropItemsPreferAsync(position, optimizeDrops, dropThatNonCharacterDrop: true);
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
                int level = LevelSelection.DeterministicDetermineRockLevel(vector);
                List<KeyValuePair<GameObject, int>> optimizeDrops = LootStyles.ModifyRockDropsOrDefault(instance.transform, instance.m_dropItems, Utils.GetPrefabName(instance.gameObject), level);
                LootPerformanceChanges.DropItemsPreferAsync(instance.transform.position, optimizeDrops, dropThatNonCharacterDrop: true);
            }

            // This is specifically used for compatibility with DropThat, as removing the loop will break DropThats functionality for Minerock5
            internal static List<GameObject> NonPerformanceBasedMineDrop(MineRock5 instance) {
                // Modify Loot Drop for minerock5
                List<GameObject> drops = new List<GameObject>();
                List<KeyValuePair<GameObject, int>> optimizeDrops = LootStyles.ModifyRockDropsOrDefault(instance.transform, instance.m_dropItems, Utils.GetPrefabName(instance.gameObject), LevelSelection.DeterministicDetermineRockLevel(instance.transform.position));
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
                int level = LevelSelection.DetermineisticDetermineObjectLevel(instance.transform.position);
                LootStyles.SelectObjectDistanceBonus(instance.transform, out DistanceLootModifier distance_bonus);
                List<KeyValuePair<GameObject, int>> optimizeDrops = LootStyles.ModifyObjectDropsOrDefault(instance.m_dropWhenDestroyed, Utils.GetPrefabName(instance.gameObject), level, distance_bonus, DropType.Destructible);
                LootPerformanceChanges.DropItemsPreferAsync(instance.transform.position, optimizeDrops, dropThatNonCharacterDrop: true);
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
                List<KeyValuePair<GameObject, int>> optimizeDrops = LootStyles.ModifyTreeDropsOrDefault(instance);
                LootPerformanceChanges.DropItemsPreferAsync(instance.transform.position, optimizeDrops, dropThatNonCharacterDrop: true);
            }

            internal static List<GameObject> NonPerformanceBasedTreeBaseDrop(TreeBase instance) {
                // Modify Loot Drop for TreeBase
                List<GameObject> drops = new List<GameObject>();
                List<KeyValuePair<GameObject, int>> optimizeDrops = LootStyles.ModifyTreeDropsOrDefault(instance);
                foreach (KeyValuePair<GameObject, int> drop in optimizeDrops) {
                    drop.Value.Times(() => drops.Add(drop.Key));
                }
                return drops;
            }
        }
    }
}
