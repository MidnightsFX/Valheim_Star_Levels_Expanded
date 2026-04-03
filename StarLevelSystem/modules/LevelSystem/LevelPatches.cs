using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.Loot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class LevelPatches {

        [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Awake))]
        public static class RandomTreeLevelExtension {
            public static void Postfix(TreeBase __instance) {
                if (ValConfig.EnableTreeScaling.Value == false) { return; }

                int storedLevel = 0;
                if (ValConfig.UseDeterministicTreeScaling.Value) {
                    storedLevel = CompositeLazyCache.GetOrAddCachedTreeEntry(__instance.m_nview);
                } else {
                    storedLevel = __instance.m_nview.GetZDO().GetInt(SLS_TREE, 0);
                    if (storedLevel == 0) {
                        LevelSelection.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                        storedLevel = LevelSelection.DetermineLevel(__instance.gameObject, creature_name, creature_settings, biome_settings, ValConfig.TreeMaxLevel.Value);
                        __instance.m_nview.GetZDO().Set(SLS_TREE, storedLevel);
                    }
                }
                if (storedLevel >= 1) {
                    __instance.StartCoroutine(LevelSelection.ModifyTreeWithLevel(__instance, storedLevel));
                }
            }
        }

        [HarmonyPatch(typeof(TreeLog))]
        public static class SetTreeLogPassLevel {
            [HarmonyPatch(nameof(TreeLog.Destroy))]
            [HarmonyPrefix]
            //[HarmonyPriority(Priority.LowerThanNormal)] // We are skipping the original, give everyone else a chance to run first.
            static bool DropAndSpawnTreeLogs(TreeLog __instance) {
                // Modify Tree Drops
                List<KeyValuePair<GameObject, int>> optimizeDrops = LootStyles.ModifyTreeDropsOrDefault(__instance);

                //Logger.LogDebug($"Starting TreeLog Destroy drop sequence tree:{__instance} drops:{dropList}");
                Vector3 position = __instance.transform.position + __instance.transform.up * UnityEngine.Random.Range(-__instance.m_spawnDistance, __instance.m_spawnDistance) + Vector3.up * 0.3f;

                __instance.m_destroyedEffect.Create(position, __instance.transform.rotation);
                LootPerformanceChanges.DropItemsPreferAsync(position, optimizeDrops, dropThatNonCharacterDrop: true);

                // Spawn logs if we should
                if (__instance.m_subLogPrefab != null) {
                    foreach (Transform transform in __instance.m_subLogPoints) {
                        Quaternion logRotation = __instance.m_useSubLogPointRotation ? __instance.transform.rotation : __instance.transform.rotation;
                        Logger.LogDebug($"Spawning Treelog at point {transform.position} rot:{logRotation}");
                        SetupTreeLog(__instance, transform, logRotation);
                    }
                }
                if (ValConfig.EnableTreeScaling.Value == true) {
                    if (__instance.m_nview != null && __instance.m_nview.GetZDO() != null) { CompositeLazyCache.RemoveTreeCacheEntry(__instance.m_nview.GetZDO().m_uid.ID); }
                }
                ZNetScene.instance.Destroy(__instance.gameObject);
                // Skip the original, we entirely rewrite it.
                return false;
            }


            [HarmonyPatch(nameof(TreeLog.Awake))]
            [HarmonyPostfix]
            static void SetupAwakeLog(TreeLog __instance) {
                if (ValConfig.EnableTreeScaling.Value == false) { return; }
                int level = 1;
                if (ValConfig.UseDeterministicTreeScaling.Value) {
                    level = CompositeLazyCache.GetOrAddCachedTreeEntry(__instance.m_nview);
                } else {
                    if (__instance.m_nview.GetZDO() != null) {
                        //Logger.LogDebug("Checking stored Zvalue for tree level");
                        level = __instance.m_nview.GetZDO().GetInt(SLS_TREE, 1);
                    }
                }
                __instance.m_health += (__instance.m_health * 0.1f * level);
                __instance.GetComponent<ImpactEffect>()?.m_damages.Modify(1 + (0.1f * level));
            }

            internal static void SetupTreeLog(TreeLog instance, Transform tform, Quaternion qt) {
                GameObject go = GameObject.Instantiate(instance.m_subLogPrefab, tform.position, qt);
                ZNetView nview = go.GetComponent<ZNetView>();
                TreeLog tchild = go.GetComponent<TreeLog>();
                //Logger.LogDebug($"Setting Treelog scale {nview}");
                nview.SetLocalScale(instance.transform.localScale);
                // pass on the level
                // Logger.LogDebug($"Getting tree level");

                int level = 1;
                if (ValConfig.EnableTreeScaling.Value == true) {
                    if (ValConfig.UseDeterministicTreeScaling.Value) {
                        level = CompositeLazyCache.GetOrAddCachedTreeEntry(instance.m_nview);
                    } else {
                        if (instance.m_nview.GetZDO() != null) {
                            //Logger.LogDebug("Checking stored Zvalue for tree level");
                            level = instance.m_nview.GetZDO().GetInt(SLS_TREE, 1);
                        }
                    }
                }

                //Logger.LogDebug($"Got Tree level {level}");
                tchild.m_health += (tchild.m_health * 0.1f * level);
                go.GetComponent<ImpactEffect>()?.m_damages.Modify(1 + (0.1f * level));
                //Logger.LogDebug($"Setting tree level {level}");
                if (ValConfig.UseDeterministicTreeScaling.Value == false) {
                    nview.GetZDO().Set(SLS_TREE, level);
                }
            }
        }

        [HarmonyPatch(typeof(RandomFlyingBird), nameof(RandomFlyingBird.Awake))]
        public static class RandomFlyingBirdExtension {
            public static void Postfix(RandomFlyingBird __instance) {
                if (ValConfig.EnableScalingBirds.Value == false) { return; }
                int storedLevel = __instance.m_nview.GetZDO().GetInt(SLS_BIRD, 0);
                if (storedLevel == 0) {
                    LevelSelection.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                    storedLevel = LevelSelection.DetermineLevel(__instance.gameObject, creature_name, creature_settings, biome_settings, ValConfig.BirdMaxLevel.Value);
                    __instance.m_nview.GetZDO().Set(SLS_BIRD, storedLevel);
                }
                if (storedLevel > 1) {
                    float scale = 1 + (ValConfig.BirdSizeScalePerLevel.Value * storedLevel);
                    //Logger.LogDebug($"Setting bird size {scale}.");
                    __instance.transform.localScale *= scale;
                    Physics.SyncTransforms();
                    DropOnDestroyed dropondeath = __instance.gameObject.GetComponent<DropOnDestroyed>();
                    List<DropTable.DropData> drops = new List<DropTable.DropData>();
                    foreach (var drop in dropondeath.m_dropWhenDestroyed.m_drops) {
                        DropTable.DropData lvlupdrop = new DropTable.DropData();
                        // Scale the amount of drops based on level
                        lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (1 + ValConfig.PerLevelBirdLootScale.Value * storedLevel));
                        lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (1 + ValConfig.PerLevelBirdLootScale.Value * storedLevel));
                        //Logger.LogDebug($"Scaling drop {drop.m_item.name} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {storedLevel}.");
                        lvlupdrop.m_item = drop.m_item;
                        drops.Add(lvlupdrop);
                    }
                    dropondeath.m_dropWhenDestroyed.m_drops = drops;
                }
            }
        }

        [HarmonyPatch(typeof(Growup))]
        public static class SetGrowUpLevel {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(Growup.GrowUpdate))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchForward(false,
                    //new CodeMatch(OpCodes.Ldloc_1),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.GetLevel))),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                    ).RemoveInstructions(2).InsertAndAdvance(
                    Transpilers.EmitDelegate(SetupGrownUp)
                    ).ThrowIfNotMatch("Unable to patch child grow up level set.");

                return codeMatcher.Instructions();
            }

            internal static void SetupGrownUp(Character grownup, Character childChar) {
                CharacterCacheEntry cdc_child = CompositeLazyCache.GetCacheEntry(childChar);
                int level = 0;
                if (cdc_child == null) {
                    level = childChar.m_level;
                    grownup.SetLevel(childChar.m_level);
                }
                CharacterCacheEntry cdc_grownup = CompositeLazyCache.GetAndSetLocalCache(grownup);
                cdc_grownup.CreatureModifiers = cdc_child.CreatureModifiers;
                cdc_grownup.Level = cdc_child.Level;
                //CompositeLazyCache.UpdateCharacterCacheEntry(grownup, cdc_child);
                CompositeLazyCache.SetCreatureModifiers(grownup, cdc_child.CreatureModifiers);
                CreatureSetupControl.CreatureSpawnerSetup(grownup, level, multiply: false);
            }
        }


        [HarmonyPatch]
        public static class SetChildLevel {
            //[HarmonyEmitIL(".dumps")]
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(typeof(Procreation),nameof(Procreation.Procreate))]
            static IEnumerable<CodeInstruction> SetChildLevelProcreateTranspiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchForward(true,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Tameable), nameof(Tameable.IsTamed))),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetTamed)))
                ).RemoveInstructions(15).InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc, (byte)6),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    Transpilers.EmitDelegate(SetupChildCharacter)
                ).MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Procreation), nameof(Procreation.m_minOffspringLevel))),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Procreation), nameof(Procreation.m_character)))
                ).Advance(2)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SetupEggItem)
                )
                .RemoveInstructions(13)
                .ThrowIfNotMatch("Unable to patch child spawn level set.");

                return codeMatcher.Instructions();
            }

            [HarmonyTranspiler]
            [HarmonyPatch(typeof(EggGrow), nameof(EggGrow.GrowUpdate))]
            static IEnumerable<CodeInstruction> EggSetChildLevelTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_1),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(EggGrow), nameof(EggGrow.m_item))),
                    new CodeMatch(OpCodes.Ldfld),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(ItemDrop.ItemData), nameof(ItemDrop.m_itemData.m_quality)))
                ).Advance(2)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(ControlEggSpawnLevelInheritance)
                )
                .RemoveInstructions(4)
                .ThrowIfNotMatch("Unable to patch Egg.GrowUpdate");

                return codeMatcher.Instructions();
            }
            private static void ControlEggSpawnLevelInheritance(Character spawnedChar, EggGrow egg) {
                if (ValConfig.EggLevelDeterminedByItemQuality.Value) {
                    int qualityLevel = egg.m_item.m_itemData.m_quality;
                    Logger.LogDebug($"Setting egg spawn level based on item quality {qualityLevel}.");
                    CreatureSetupControl.CreatureSetup(spawnedChar, qualityLevel);
                } else {
                    // Don't set the level, let it be determined by the creatures configuration
                    CreatureSetupControl.CreatureSetup(spawnedChar);
                }
                CheckToMakeOffspringInfertile(spawnedChar);
            }

            private static void CheckToMakeOffspringInfertile(Character chara) {
                if (ValConfig.OffspringCanBeInfertile.Value) {
                    if (UnityEngine.Random.value <= ValConfig.OffspringChanceToBeInfertile.Value) {
                        chara.m_nview.GetZDO().Set(SLS_INFERTILE, true);
                        Logger.LogDebug($"Child is infertile.");
                    }
                }
            }

            internal static void SetupEggItem(ItemDrop item, Procreation proclass) {
                if (ValConfig.LootEggsDropIncreaseStacks.Value) {
                    int leveled_loot = Mathf.RoundToInt(1 * (1 + ValConfig.PerLevelLootScale.Value * proclass.m_character.m_level));
                    if (leveled_loot > item.m_itemData.m_shared.m_maxStackSize) { leveled_loot = item.m_itemData.m_shared.m_maxStackSize; }
                    item.m_itemData.m_stack = leveled_loot;
                } else {
                    // This is effectively the current vanilla behavior
                    int level = Mathf.Max(proclass.m_minOffspringLevel, proclass.m_character ? proclass.m_character.GetLevel() : proclass.m_minOffspringLevel);
                    if (ValConfig.OffspringCanBeStrongerThanParents.Value == true) {
                        if (UnityEngine.Random.value <= ValConfig.OffspringGainExtraLevelChance.Value) {
                            level += 1;
                            Logger.LogDebug($"Child egg is stronger than parents and has a higher max level.");
                        }
                    }
                    item.SetQuality(level);
                }
            }

            internal static void SetupChildCharacter(Character chara, Procreation proc) {
                Logger.LogDebug($"Setting child level for {chara.m_name}");
                chara.SetTamed(true);
                CharacterCacheEntry cdc_parent = CompositeLazyCache.GetCacheEntry(proc.m_character);

                if (ValConfig.RandomizeTameChildrenModifiers.Value == false && proc.m_character != null) {
                    //// TODO: Add randomization, limits and variations to children
                    if (cdc_parent != null) {
                        CompositeLazyCache.SetCreatureModifiers(chara, cdc_parent.CreatureModifiers);
                    }
                }

                if (ValConfig.SpawnMultiplicationAppliesToTames.Value == false && chara.m_nview.GetZDO() != null) {
                    Logger.LogDebug("Disabling spawn multiplier for tamed child.");
                    chara.m_nview.GetZDO().Set(SLS_SPAWN_MULT, true);
                }

                int inheritedLevel = proc.m_character.m_level;
                if (cdc_parent != null && cdc_parent.Level != 0) {
                    inheritedLevel = cdc_parent.Level;
                }

                if (ValConfig.RandomizeTameChildrenLevels.Value == true) {
                    int level = UnityEngine.Random.Range(1, inheritedLevel);
                    if (ValConfig.OffspringCanBeStrongerThanParents.Value == true) {
                        if (UnityEngine.Random.value <= ValConfig.OffspringGainExtraLevelChance.Value) {
                            level += 1;
                            Logger.LogDebug($"Child is strong, but still random.");
                        }
                    }
                    Logger.LogDebug($"Character randomized level {level} (1-{inheritedLevel}) being used for child.");
                    CreatureSetupControl.CreatureSetup(chara, level, delay: 0.1f);
                } else {
                    if (ValConfig.OffspringCanBeStrongerThanParents.Value == true) {
                        if (UnityEngine.Random.value <= ValConfig.OffspringGainExtraLevelChance.Value) {
                            inheritedLevel += 1;
                            Logger.LogDebug($"Child is stronger than parents and has a higher max level.");
                        }
                    }
                    Logger.LogDebug($"Parent level {inheritedLevel} being used for child from: proc-{proc.m_character.m_level} cdc-{cdc_parent.Level}.");
                    CreatureSetupControl.CreatureSetup(chara, inheritedLevel, delay: 0.1f);
                }
                CheckToMakeOffspringInfertile(chara);
            }
        }

        [HarmonyPatch(typeof(Procreation), nameof(Procreation.ReadyForProcreation))]
        public static class ProcreationPrevention {
            public static void Postfix(Procreation __instance, ref bool __result) {
                if (__instance.m_character != null || __instance.m_character.m_nview != null || __instance.m_character.m_nview.GetZDO() != null && __result == true) {
                    if (__instance.m_nview.GetZDO().GetBool(SLS_INFERTILE, false)) {
                        __result = false;
                        Logger.LogDebug($"Preventing procreation because child is infertile.");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
        public static class ProcreationPreventionDisplay {
            public static void Postfix(Tameable __instance, ref string __result) {
                if (__instance.m_character != null && __instance.m_character.m_nview != null && __instance.m_character.m_nview.GetZDO() != null) {
                    if (__instance.m_nview.GetZDO().GetBool(SLS_INFERTILE, false)) {
                        __result += Localization.instance.Localize("\n<color=red>$SLS_infertile</color>");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CreatureSpawner))]
        public static class CreatureSpawnerSpawn {
            //[HarmonyEmitIL(".dumps")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CreatureSpawner.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(CreatureSpawnerCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .Insert(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .MatchStartBackwards(
                    new CodeMatch(OpCodes.Stloc_S),
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(2)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .ThrowIfNotMatch("Unable to patch CreatureSpawner.Spawn.");

                return codeMatcher.Instructions();
            }

            private static void CreatureSpawnerCharacterLevelControl(Character chara, int providedLevel) {
                //Logger.LogDebug($"CreatureSpawner.Spawn setting {chara.m_name} {providedLevel}");
                LevelSelection.SetCharacterLevelControl(chara, providedLevel);
            }

            [HarmonyPatch(nameof(CreatureSpawner.Awake))]
            [HarmonyPostfix]
            public static void Postfix(CreatureSpawner __instance) {
                __instance.m_minLevel = 1;
            }
        }

        [HarmonyPatch(typeof(SpawnArea))]
        public static class SpawnAreaSpawnOnePatch {
            private static readonly List<string> VanillaSpawnAreaControllers = new List<string>() {
                "EvilHeart_Forest",
                "Spawner_GreydwarfNest",
                "Spawner_DraugrPile",
                "BonePileSpawner",
                "Spawner_CharredCross",
                "Spawner_CharredStone_Elite",
                "Spawner_Kvastur",
                "Spawner_CharredStone",
                "Spawner_CharredStone_event",
                "EvilHeart_Swamp"
            };

            //[HarmonyEmitIL(".dumps")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnArea.SpawnOne))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(GameObject), "GetComponent", null, new Type[] { typeof(Character) })),
                    new CodeMatch(OpCodes.Stloc_S)
                    )
                .Advance(2)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc_S, 5),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldloc_2),
                    Transpilers.EmitDelegate(SpawnAreaSetCharacterLevelControl)
                    )
                .CreateLabelOffset(out System.Reflection.Emit.Label label, offset: 28)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Br, label))
                .ThrowIfNotMatch("Unable to patch SpawnArea.SpawnOne.");

                return codeMatcher.Instructions();
            }

            private static void SpawnAreaSetCharacterLevelControl(Character chara, SpawnArea spawnArea, SpawnArea.SpawnData spawndata) {
                if (ValConfig.ControlSpawnerLevels.Value) {
                    // Alternatively only control this spawner if it is a vanilla spawner and control spawners is enabled
                    if (ValConfig.OnlyControlVanillaAreaSpawners.Value && VanillaSpawnAreaControllers.Contains(Utils.GetPrefabName(spawnArea.gameObject))) {
                        //Logger.LogDebug($"SpawnArea.SpawnOne (SLS Controlled) leveling {chara.m_name}");
                        CreatureSetupControl.CreatureSpawnerSetup(chara, delay: 0.1f);
                        return;
                    }

                    CreatureSetupControl.CreatureSpawnerSetup(chara, delay: 0.1f);
                } else {
                    LevelGenerator LG = new LevelGenerator() { LevelUpChance = spawnArea.GetLevelUpChance(), MaxLevel = spawndata.m_maxLevel, MinLevel = spawndata.m_minLevel, PrefabName = spawndata.m_prefab.name };
                    int level = LG.RollAndDetermineLevel();
                    Logger.LogDebug($"SpawnArea.SpawnOne using provided {chara.m_name} level {level}");
                    CreatureSetupControl.CreatureSpawnerSetup(chara, level, delay: 0.1f);
                }
            }

            [HarmonyPatch(nameof(SpawnArea.SelectWeightedPrefab))]
            [HarmonyPostfix]
            public static void Prefix(ref SpawnArea.SpawnData __result) {
                if (__result == null) { return; }
                __result.m_minLevel = 1;
            }
        }

        [HarmonyPatch(typeof(SpawnSystem))]
        public static class SpawnSystemSpawnPatch {
            //[HarmonyEmitIL(".dumps")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnSystem.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(SpawnSystem.SpawnData), nameof(SpawnSystem.SpawnData.m_prefab))),
                    new CodeMatch(OpCodes.Ldarg_2),
                    new CodeMatch(OpCodes.Call),
                    new CodeMatch(OpCodes.Call),
                    new CodeMatch(OpCodes.Stloc_0)
                )
                .Advance(5)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc_0),
                    Transpilers.EmitDelegate(SpawnSystemSetCharacterWithoutZoneLimits)
                )
                .MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SpawnSystemSetCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.SetQuality)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SetItemLevelFish)
                )
                .ThrowIfNotMatch("Unable to patch SpawnSystem Spawn set level.");

                return codeMatcher.Instructions();
            }

            private static void SpawnSystemSetCharacterWithoutZoneLimits(GameObject go) {
                Character chara = go.GetComponent<Character>();
                if (chara == null) { return; }
                //Logger.LogDebug($"SpawnSystem.Spawn setting without zone control {chara.m_name}");
                LevelSelection.SetCharacterLevelControl(chara, 1);
            }

            private static void SetItemLevelFish(ItemDrop item, int _providedLevel) {
                LevelSelection.SelectCreatureBiomeSettings(item.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                int determinedLevel = LevelSelection.DetermineLevel(item.gameObject, creature_name, creature_settings, biome_settings, ValConfig.FishMaxLevel.Value);
                // not sure we need max quality set high
                item.m_itemData.m_shared.m_maxQuality = ValConfig.FishMaxLevel.Value + 1;
                item.SetQuality(determinedLevel);
                item.m_itemData.m_shared.m_scaleByQuality = ValConfig.FishSizeScalePerLevel.Value;
                item.Save();
            }

            private static void SpawnSystemSetCharacterLevelControl(Character chara, int providedLevel) {
                //Logger.LogDebug($"SpawnSystem.Spawn setting {chara.m_name} {providedLevel}");
                LevelSelection.SetCharacterLevelControl(chara, providedLevel);
            }

            [HarmonyPatch(nameof(SpawnSystem.Spawn))]
            [HarmonyPrefix]
            public static void Prefix(ref SpawnSystem.SpawnData critter) {
                critter.m_minLevel = 1;
            }
        }

        [HarmonyPatch(typeof(TriggerSpawner))]
        public static class TriggerSpawnerSpawnPatch {
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TriggerSpawner.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(TriggerSpawnerSetCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(TriggerSpawner), nameof(TriggerSpawner.m_maxLevel))),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(2)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .ThrowIfNotMatch("Unable to patch TriggerSpawner.Spawn.");

                return codeMatcher.Instructions();
            }

            private static void TriggerSpawnerSetCharacterLevelControl(Character chara, int providedLevel) {
                //Logger.LogDebug($"TriggerSpawner.Spawn setting {chara.m_name} {providedLevel}");
                LevelSelection.SetCharacterLevelControl(chara, providedLevel);
            }

            [HarmonyPatch(nameof(TriggerSpawner.Awake))]
            [HarmonyPostfix]
            public static void Postfix(TriggerSpawner __instance) {
                __instance.m_minLevel = 1;
            }
        }

        [HarmonyPatch(typeof(SpawnAbility))]
        public static class SpawnAbilitySpawnPatch {
            // Note: This is an IEnumerator so we need to patch the MoveNext method inside the generated class

            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnAbility.Spawn), MethodType.Enumerator)]
            static IEnumerable<CodeInstruction> TranspileMoveNext(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                    ).RemoveInstructions(1).InsertAndAdvance(
                    Transpilers.EmitDelegate(SetSpawnAbilityLevelControl)
                    ).ThrowIfNotMatch("Unable to patch TriggerSpawner Spawn set level.");

                return codeMatcher.Instructions();
            }

            public static void SetSpawnAbilityLevelControl(Character chara, int providedLevel) {
                if (ValConfig.ControlAbilitySpawnedCreatures.Value) {
                    CreatureSetupControl.CreatureSpawnerSetup(chara);
                    return;
                }
                // Fallback
                chara.SetLevel(providedLevel);
            }
        }

        // Add support for Seeker egg hatches
        [HarmonyPatch(typeof(EggHatch))]
        public static class EggHatchSpawnPatch {
            // Skip original prefix that causes levelup to trigger for seeker broods
            [HarmonyPatch(nameof(EggHatch.Hatch))]
            static bool Prefix(EggHatch __instance) {
                __instance.m_hatchEffect.Create(__instance.transform.position, __instance.transform.rotation, null, 1f, -1);
                GameObject go = UnityEngine.Object.Instantiate(__instance.m_spawnPrefab, __instance.transform.TransformPoint(__instance.m_spawnOffset), Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f));
                Character chara = go.GetComponent<Character>();
                if (chara != null) {
                    CreatureSetupControl.CreatureSetup(chara);
                }
                __instance.m_nview.Destroy();
                return false;
            }
        }

        // Ensure creatures that are spawned as loot drops also get leveled
        [HarmonyPatch(typeof(ItemDrop))]
        public static class DropOnDestroyedSpawnPatch {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(ItemDrop.OnCreateNew), new Type[] { typeof(GameObject) })]
            static void Postfix(GameObject go) {
                Character chara = go.GetComponent<Character>();
                if (chara != null) {
                    CreatureSetupControl.CreatureSetup(chara);
                }
            }
        }
    }
}
