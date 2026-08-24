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
                // Pass the original drops list through so DropThat compat can map each instantiated drop back to its config.
                LootPerformanceChanges.DropItemsPreferAsync(centerPos, LootPerformanceChanges.DropItemsDetermineDropStackSize(drops), dropThatCharacterDrop: true, characterDropSource: drops);
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
                // Per-creature custom loot stored on the ZDO (e.g. nemesis spawns) replaces the global table for this instance.
                List<ExtendedCharacterDrop> customLoot = LootSystemData.GetCustomLoot(__instance.m_character);
                bool hasGlobal = LootSystemData.SLS_Drop_Settings?.CharacterSpecificLoot?.ContainsKey(name) == true;
                if (customLoot == null && hasGlobal != true) { return true; }

                __result = LootStyles.ModifyCharacterDrops(__instance, name, customLoot);
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
                ).ThrowIfNotMatch("Unable to patch Character drop generator, level scaling.")
                .Advance(2).RemoveInstructions(15).InsertAndAdvance(
                    Transpilers.EmitDelegate(DetermineLootScale)
                )
                // Chance branch: `chance *= (float)num` -> `chance *= ChanceScaleOverride(num)`.
                // Anchor on the first m_levelMultiplier load, then the unique `ldloc.1; conv.r4; mul`
                // (num -> float -> multiply); Set replaces the conv.r4 in place, preserving labels.
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(CharacterDrop.Drop), nameof(CharacterDrop.Drop.m_levelMultiplier)))
                ).ThrowIfNotMatch("Unable to patch Character drop generator, chance branch anchor.")
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_1),
                    new CodeMatch(OpCodes.Conv_R4),
                    new CodeMatch(OpCodes.Mul)
                ).ThrowIfNotMatch("Unable to patch Character drop generator, chance multiplier.")
                .Advance(1)
                .Set(OpCodes.Call, AccessTools.Method(typeof(CalculateLootPerLevelStyle), nameof(ChanceScaleOverride)))
                // Amount branch: the search resumes past the chance branch's m_levelMultiplier pair.
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(CharacterDrop.Drop), nameof(CharacterDrop.Drop.m_levelMultiplier)))
                ).ThrowIfNotMatch("Unable to patch Character drop generator, amount branch anchor.")
                .Advance(2).InsertAndAdvance(
                    Transpilers.EmitDelegate(OverrideLootScalingEnabler)
                ).MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_S),
                    new CodeMatch(OpCodes.Ble)
                ).ThrowIfNotMatch("Unable to patch Character drop limit removal.")
                .Advance(3).RemoveInstructions(2);

                return codeMatcher.Instructions();
            }

            private static bool OverrideLootScalingEnabler(bool defaultlootLevelMultiplier) {
                if (ValConfig.ScaleAllLootByLevel.Value) { return true; }
                return defaultlootLevelMultiplier;
            }

            // Chance multiplier for the vanilla chance branch, written by DetermineLootScale at the
            // top of each GenerateDropList run and read per-drop by ChanceScaleOverride in that run.
            private static float chancePerLevelChanceMultiplier = 1f;

            // Replaces the (float)num cast in vanilla's `chance *= (float)num`. ChancePerLevel grows
            // the chance by its own configurable ramp instead of the amount multiplier; every other
            // style keeps vanilla semantics (chance scales by the same multiplier as amounts).
            private static float ChanceScaleOverride(int amountMultiplier) {
                if (LootStyles.SelectedLootFactor == LootFactorType.ChancePerLevel) {
                    return chancePerLevelChanceMultiplier;
                }
                return amountMultiplier;
            }

            // This determines the "level" that is used to generate loot multipled by level in vanilla configurations
            private static int DetermineLootScale(Character character) {
                // Reset first so every early return leaves vanilla chance behavior in place.
                chancePerLevelChanceMultiplier = 1f;
                // A missing character, or a base (0-star, level 1) creature, scales to 1x. This matches the
                // custom-loot path's level==1 short-circuit (MultiplyLootPerLevel/ExponentLootPerLevel) and
                // avoids a null deref inside SelectCharacterLootSettings.
                if (character == null) { return 1; }
                int char_level = character.GetLevel();
                if (char_level <= 1) { return 1; }
                LootStyles.SelectCharacterLootSettings(character, out DistanceLootModifier distance_bonus);
                float min;
                float max;
                if (LootStyles.SelectedLootFactor == LootFactorType.PerLevel) {
                    min = char_level * (distance_bonus.MinAmountScaleFactorBonus + ValConfig.PerLevelLootScale.Value);
                    max = char_level * (distance_bonus.MaxAmountScaleFactorBonus + ValConfig.PerLevelLootScale.Value);
                } else if (LootStyles.SelectedLootFactor == LootFactorType.Exponential) {
                    // Implicit 1x base plus the per-level scale, raised to the star count (level-1) so a
                    // 0-star creature stays 1x. Matches ExponentLootPerLevel's effective base of
                    // scale_factor(1) + PerLevelLootScale + bonus; defaults (scale 1.0) reproduce
                    // vanilla's 2^(level-1) doubling.
                    min = Mathf.Pow(1f + ValConfig.PerLevelLootScale.Value + distance_bonus.MinAmountScaleFactorBonus, char_level - 1);
                    max = Mathf.Pow(1f + ValConfig.PerLevelLootScale.Value + distance_bonus.MaxAmountScaleFactorBonus, char_level - 1);
                } else if (LootStyles.SelectedLootFactor == LootFactorType.ChancePerLevel) {
                    // Chance gate + linear amounts: drop amounts scale exactly like PerLevel, while the
                    // chance branch multiplies by a configurable per-level ramp instead of the amount
                    // multiplier (see ChanceScaleOverride).
                    min = char_level * (distance_bonus.MinAmountScaleFactorBonus + ValConfig.PerLevelLootScale.Value);
                    max = char_level * (distance_bonus.MaxAmountScaleFactorBonus + ValConfig.PerLevelLootScale.Value);
                    chancePerLevelChanceMultiplier = ValConfig.ChanceBaseChancePerLevel.Value + ((ValConfig.PerLevelLootChanceScale.Value + distance_bonus.ChanceScaleFactorBonus) * char_level);
                } else {
                    // Fallback just leveled loot scale without distance bonus
                    // With a min of 1 to prevent 0 drops
                    min = char_level * ValConfig.PerLevelLootScale.Value;
                    max = char_level * ValConfig.PerLevelLootScale.Value;
                }
                // Clamp before rounding: Pow can reach float Infinity at very high max levels, and the
                // vanilla body multiplies drop amounts by this value in int math.
                min = Mathf.Min(min, LootStyles.MaxLootScaleResult);
                max = Mathf.Min(max, LootStyles.MaxLootScaleResult);
                int result = Mathf.RoundToInt(UnityEngine.Random.Range(min, max));
                if (result < 1) { result = 1; }
                if (ValConfig.EnableDebugLootDetails.Value) {
                    string chanceDetail = LootStyles.SelectedLootFactor == LootFactorType.ChancePerLevel ? $" Chance multiplier {chancePerLevelChanceMultiplier}." : "";
                    Logger.LogDebug($"Loot Factor {LootStyles.SelectedLootFactor} | lvl {char_level} select {min} <-> {max} selected {result}.{chanceDetail}");
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
                List<LootEntry> optimizeDrops = LootStyles.ModifyRockDropsOrDefault(instance.transform, instance.m_dropItems, Utils.GetPrefabName(instance.gameObject), LevelSelection.DeterministicDetermineRockLevel(instance.gameObject.transform.position));
                Vector3 position = hit.m_point - hit.m_dir * 0.2f + UnityEngine.Random.insideUnitSphere * 0.3f;
                position.y += 0.5f; // Offset the drops upwards slightly, this is primarily to fix ores which are partially in the ground
                LootPerformanceChanges.DropItemsPreferAsync(position, optimizeDrops);
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
                return codeMatcher.Instructions();
            }

            internal static void MineDrop(MineRock5 instance, Vector3 vector) {
                int level = LevelSelection.DeterministicDetermineRockLevel(vector);
                List<LootEntry> optimizeDrops = LootStyles.ModifyRockDropsOrDefault(instance.transform, instance.m_dropItems, Utils.GetPrefabName(instance.gameObject), level);
                LootPerformanceChanges.DropItemsPreferAsync(vector, optimizeDrops);
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
                List<LootEntry> optimizeDrops = LootStyles.ModifyObjectDropsOrDefault(instance.m_dropWhenDestroyed, Utils.GetPrefabName(instance.gameObject), level, distance_bonus, DropType.Destructible);
                LootPerformanceChanges.DropItemsPreferAsync(instance.transform.position, optimizeDrops);
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

                return codeMatcher.Instructions();
            }

            private static void TreebaseDropDestroyedItems(TreeBase instance) {
                List<LootEntry> optimizeDrops = LootStyles.ModifyTreeDropsOrDefault(instance);
                LootPerformanceChanges.DropItemsPreferAsync(instance.transform.position, optimizeDrops);
            }
        }

        [HarmonyPatch(typeof(Pickable))]
        public static class PickableDropPatch {
            [HarmonyTranspiler]
            [HarmonyPatch("RPC_Pick")]
            [HarmonyEmitIL(".dumps")]
            static public IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
                var codeMatcher = new CodeMatcher(instructions);
                // Start of the vanilla main-item drop calculation: `this.m_dontScale ? ...`
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Pickable), nameof(Pickable.m_dontScale)))
                ).ThrowIfNotMatch("Unable to patch Pickable drop: drop calculation start not found.");
                int start = codeMatcher.Pos;

                // End of the section: the `if (!m_extraDrops.IsEmpty())` block begins right after the drop loop.
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Pickable), nameof(Pickable.m_extraDrops)))
                ).ThrowIfNotMatch("Unable to patch Pickable drop: extra-drops section not found.");
                int end = codeMatcher.Pos;

                codeMatcher.Start().Advance(start)
                    .RemoveInstructions(end - start)
                    .InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldarg_0), // The pickable
                        new CodeInstruction(OpCodes.Ldarg_2), // bonus amount
                        Transpilers.EmitDelegate(HandlePickableDrop)
                    );

                return codeMatcher.Instructions();
            }

            internal static void HandlePickableDrop(Pickable instance, int bonus) {
                string name = Utils.GetPrefabName(instance.gameObject);
                //Logger.LogDebug($"Checking for custom loot for {name} available: {string.Join(",", LootSystemData.SLS_Drop_Settings.NonCharacterSpecificLoot.Keys)}");
                if (LootSystemData.SLS_Drop_Settings != null && LootSystemData.SLS_Drop_Settings.NonCharacterSpecificLoot != null && LootSystemData.SLS_Drop_Settings.NonCharacterSpecificLoot.ContainsKey(name) == true) {
                    //Logger.LogDebug($"Custom Pickable loot set for {name}");
                    Vector3 dropPos = instance.transform.position + Vector3.up * instance.m_spawnOffset;
                    int level = LevelSelection.DetermineisticDetermineObjectLevel(instance.transform.position);
                    List<LootEntry> drops = LootStyles.ModifyPickableDropsOrDefault(instance.transform, name, level);
                    LootPerformanceChanges.DropItemsPreferAsync(dropPos, drops);
                    return;
                }

                // Vanilla behavior preserved for non-configured pickables.
                int num = instance.m_dontScale ? instance.m_amount : Mathf.Max(instance.m_minAmountScaled, Game.instance.ScaleDrops(instance.m_itemPrefab, instance.m_amount));
                num += bonus;
                int num2 = 0;
                for (int i = 0; i < num; i++) {
                    instance.Drop(instance.m_itemPrefab, num2++, 1);
                }
            }
        }
    }
}
