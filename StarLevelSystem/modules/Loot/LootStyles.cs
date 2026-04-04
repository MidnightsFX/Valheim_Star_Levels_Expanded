using BepInEx.Configuration;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static Mono.Security.X509.X520;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Loot {
    internal static class LootStyles {

        public static LootFactorType SelectedLootFactor = LootFactorType.PerLevel;

        internal static readonly AcceptableValueList<string> AllowedLootFactors = new AcceptableValueList<string>(new string[] {
            LootFactorType.PerLevel.ToString(),
            LootFactorType.Exponential.ToString()
        });

        internal static void LootFactorChanged(object s, EventArgs e) {
            SelectedLootFactor = (LootFactorType)Enum.Parse(typeof(LootFactorType), ValConfig.LootDropCalculationType.Value);
        }

        internal static List<KeyValuePair<GameObject, int>> ModifyTreeDropsOrDefault(TreeBase treeInstance) {
            int level = CompositeLazyCache.GetOrAddCachedTreeEntry(treeInstance.m_nview);
            SelectObjectDistanceBonus(treeInstance.transform, out DistanceLootModifier distance_bonus);
            List<KeyValuePair<GameObject, int>> drops = ModifyObjectDropsOrDefault(treeInstance.m_dropWhenDestroyed, Utils.GetPrefabName(treeInstance.gameObject), level, distance_bonus, DropType.Tree);
            return drops;
        }

        internal static List<KeyValuePair<GameObject, int>> ModifyTreeDropsOrDefault(TreeLog treeInstance) {
            int level = CompositeLazyCache.GetOrAddCachedTreeEntry(treeInstance.m_nview);
            SelectObjectDistanceBonus(treeInstance.transform, out DistanceLootModifier distance_bonus);
            List<KeyValuePair<GameObject, int>> drops = ModifyObjectDropsOrDefault(treeInstance.m_dropWhenDestroyed, Utils.GetPrefabName(treeInstance.gameObject), level, distance_bonus, DropType.Tree);
            return drops;
        }

        internal static List<KeyValuePair<GameObject, int>> ModifyRockDropsOrDefault(Transform tform, DropTable droptable, string name, int level) {
            SelectObjectDistanceBonus(tform, out DistanceLootModifier distance_bonus);
            List<KeyValuePair<GameObject, int>> drops = ModifyObjectDropsOrDefault(droptable, name, level, distance_bonus, DropType.Rock);
            return drops;
        }


        internal static List<KeyValuePair<GameObject, int>> ModifyObjectDropsOrDefault(DropTable defaultDrops, string lookupkey, int level, DistanceLootModifier distance_bonus, DropType type = DropType.Tree) {
            List<KeyValuePair<GameObject, int>> dropList = new List<KeyValuePair<GameObject, int>>();
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
                        dropList.Add(new KeyValuePair<GameObject, int>(eod.DropGo, UnityEngine.Random.Range(eod.Drop.Min, eod.Drop.Max)));
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
                    switch (SelectedLootFactor) {
                        case LootFactorType.PerLevel:
                            drop = MultiplyLootPerLevel(drop, level, distance_bonus, scale_factor);
                            break;
                        case LootFactorType.Exponential:
                            drop = ExponentLootPerLevel(drop, level, distance_bonus, scale_factor);
                            break;
                        case LootFactorType.ChancePerLevel:
                            float luck_roll = UnityEngine.Random.value;
                            float chance = eod.Drop.Chance;
                            if (eod.ChanceScaleFactor > 0f) { chance *= 1 + (eod.ChanceScaleFactor * level); }
                            // check the chance for this to be rolled
                            if (luck_roll > chance) {
                                Logger.LogDebug($"Drop {eod.Drop.Prefab} failed random drop chance ({luck_roll} < {chance}).");
                                continue;
                            }
                            break;
                    }

                    // Enforce max drop cap
                    if (eod.MaxScaledAmount > 0 && drop > eod.MaxScaledAmount) {
                        drop = eod.MaxScaledAmount;
                        Logger.LogDebug($"Drop {eod.Drop.Prefab} capped to {drop}");
                    }

                    dropList.Add(new KeyValuePair<GameObject, int>(eod.DropGo, drop));
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
                dropList = OptimizeToDropStacks(updatedDropTable.GetDropList());
            }
            return dropList;
        }

        internal static List<KeyValuePair<GameObject, int>> ModifyCharacterDrops(CharacterDrop cdrop, string name) {
            List<KeyValuePair<GameObject, int>> drop_results = new List<KeyValuePair<GameObject, int>>();
            int level = 1;
            if (cdrop.m_character != null) {
                level = cdrop.m_character.m_level;
            }

            LootStyles.SelectCharacterLootSettings(cdrop.m_character, out DistanceLootModifier distance_bonus);
            // Logger.LogDebug($"SLS Custom drop set for {name} - level {level}");
            // Use modified loot drop settings
            StringBuilder sb = new StringBuilder();
            foreach (ExtendedCharacterDrop loot in LootSystemData.SLS_Drop_Settings.characterSpecificLoot[name]) {
                // Skip this loop drop if it doesn't drop for tamed creatures or only drops for tamed creatures
                if (cdrop.m_character != null) {
                    if (ValConfig.EnableDebugLootDetails.Value) {
                        sb.AppendLine($"Skipping {loot.Drop.Prefab} for tamed creature.");
                    }
                    // Logger.LogDebug($"Checking if drop is tame only or non-tame only.");
                    if (loot.UntamedOnlyDrop && cdrop.m_character.IsTamed()) { continue; }
                    if (loot.TamedOnlyDrop && cdrop.m_character.IsTamed() != true) { continue; }
                }

                float scale_multiplier = 1f;

                // If chance is enabled, calculate all of the chance characteristics
                if (loot.Drop.Chance < 1) {
                    float luck_roll = UnityEngine.Random.value;
                    float chance = loot.Drop.Chance;
                    if (loot.ChanceScaleFactor > 0f) { chance *= 1 + (loot.ChanceScaleFactor * level); }
                    // check the chance for this to be rolled
                    if (luck_roll > chance) {
                        if (ValConfig.EnableDebugLootDetails.Value) {
                            sb.AppendLine($"Drop {loot.Drop.Prefab} failed random drop chance ({luck_roll} < {chance}).");
                        }
                        continue;
                    }
                }

                // Set scale modifier for the loot drop, based on chance, if enabled
                if (loot.UseChanceAsMultiplier) {
                    scale_multiplier = (loot.Drop.Chance * level);
                    if (ValConfig.EnableDebugLootDetails.Value) {
                        sb.AppendLine($"Drop {loot.Drop.Prefab} modified by chance and creature level (scale multiplier now: {scale_multiplier}).");
                    }
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
                    if (ValConfig.EnableDebugLootDetails.Value) {
                        sb.AppendLine($"Drop {loot.Drop.Prefab} determined base drop amount: {drop_base_amount}).");
                    }
                }

                if (loot.DoesNotScale == true) {
                    // Apply Random change, and the range of the loot drop
                    if (ValConfig.EnableDebugLootDetails.Value) {
                        sb.AppendLine($"Drop {loot.Drop.Prefab} does not scale and will drop {drop_base_amount}");
                    }
                    drop_results.Add(new KeyValuePair<GameObject, int>(loot.GameDrop.m_prefab, drop_base_amount));
                    continue;
                }

                // Determine the actual amount of the drop
                int drop = drop_base_amount;
                if (loot.DoesNotScale != true && loot.Drop.DontScale != true) {
                    float scale_factor = loot.AmountScaleFactor * scale_multiplier;
                    if (scale_factor <= 0f) { scale_factor = 1f; }
                    if (SelectedLootFactor == LootFactorType.PerLevel) {
                        drop = MultiplyLootPerLevel(drop, level, distance_bonus, scale_factor);
                    } else {
                        drop = ExponentLootPerLevel(drop, level, distance_bonus, scale_factor);
                    }
                    if (ValConfig.EnableDebugLootDetails.Value) {
                        sb.AppendLine($"Drop {loot.Drop.Prefab} Using {SelectedLootFactor} factor {scale_factor} base {drop_base_amount} = {drop}");
                    }
                    // Enforce max drop cap
                    if (loot.MaxScaledAmount > 0 && drop > loot.MaxScaledAmount) {
                        drop = loot.MaxScaledAmount;
                        Logger.LogDebug($"Drop {loot.Drop.Prefab} capped to {drop}");
                    }
                }


                // Modify the multiplier for how many this drops based on local players
                if (loot.Drop.OnePerPlayer) {
                    int playersNearby = Player.GetPlayersInRangeXZ(cdrop.transform.position, 500f);
                    drop *= Mathf.Max(1, playersNearby);
                }

                // Add the drop to the results
                if (loot.GameDrop == null || loot.GameDrop.m_prefab == null) {
                    loot.ToCharacterDrop();
                }

                drop_results.Add(new KeyValuePair<GameObject, int>(loot.GameDrop.m_prefab, drop));
            }
            if (ValConfig.EnableDebugLootDetails.Value) {
                Logger.LogDebug($"Generated drops for {name}:\n{sb.ToString()}");
            }
            return drop_results;
        }

        // This builds the total amount of loot from this style of loot modification
        // Loot amount x level x modifiers 
        private static int MultiplyLootPerLevel(int lootdrop_amount, int level, DistanceLootModifier dmod, float scale_factor = 1f) {
            if (level == 1) { return lootdrop_amount; } // no scaling for level 1, just return the base loot amount
            float min_drop_scale = (level * ValConfig.PerLevelLootScale.Value) * (scale_factor + dmod.MinAmountScaleFactorBonus);
            float max_drop_scale = (level * ValConfig.PerLevelLootScale.Value) * (scale_factor + dmod.MaxAmountScaleFactorBonus);
            // If its the same no need to randomize
            if (min_drop_scale == max_drop_scale) {
                int result = Mathf.RoundToInt(min_drop_scale * lootdrop_amount);
                if (ValConfig.EnableDebugLootDetails.Value) {
                    Logger.LogDebug($"MultiplyLootPerLevel {result} = drop_base: {lootdrop_amount} * {min_drop_scale} scale from (lvl:{level} * PerLevelLootScale:{ValConfig.PerLevelLootScale.Value}) * (factor:{scale_factor} + scaleFactorBonus:{dmod.MinAmountScaleFactorBonus})");
                }
                return result;
            }
            int min_drop = Mathf.RoundToInt(min_drop_scale * lootdrop_amount);
            int max_drop = Mathf.RoundToInt(max_drop_scale * lootdrop_amount);
            int randomized_result = UnityEngine.Random.Range(min_drop, max_drop);
            if (ValConfig.EnableDebugLootDetails.Value) {
                Logger.LogDebug($"MultiplyLootPerLevel {randomized_result} from range: ({min_drop} <-> {max_drop}) = drop_base: {lootdrop_amount} * min:{min_drop_scale}/max:{max_drop_scale} scale from (lvl:{level} * PerLevelLootScale:{ValConfig.PerLevelLootScale.Value}) * (factor:{scale_factor} + scaleFactorBonus: min{dmod.MinAmountScaleFactorBonus}/max:{dmod.MaxAmountScaleFactorBonus})");
            }
            return randomized_result;
        }

        private static int ExponentLootPerLevel(int lootdrop_amount, int level, DistanceLootModifier dmod, float scale_factor = 1) {
            if (level == 1) { return lootdrop_amount; } // no scaling for level 1, just return the base loot amount

            float min_drop_scale = ValConfig.PerLevelLootScale.Value + dmod.MinAmountScaleFactorBonus + scale_factor;
            float max_drop_scale = ValConfig.PerLevelLootScale.Value + dmod.MaxAmountScaleFactorBonus + scale_factor;
            float selectedMod = UnityEngine.Random.Range(min_drop_scale, max_drop_scale);
            float loot_scale_factor = Mathf.Pow(selectedMod, level);
            int result = Mathf.RoundToInt(loot_scale_factor * lootdrop_amount);
            Logger.LogDebug($"ExponentLootPerLevel {result} from range ({min_drop_scale} <-> {max_drop_scale}) = drop_base: {lootdrop_amount} * min:{min_drop_scale}/max:{max_drop_scale} scale from (factor:{scale_factor} + distance:(min){dmod.MinAmountScaleFactorBonus}/(max){dmod.MaxAmountScaleFactorBonus} perlevelscale:{dmod.MinAmountScaleFactorBonus})");
            return result;
        }

        private static int ChancePerLevel(int lootdrop_amount, int level, DistanceLootModifier dmod, float scale_factor = 1) {
            float min_drop_scale = ValConfig.PerLevelLootScale.Value + dmod.MinAmountScaleFactorBonus + scale_factor;
            float max_drop_scale = ValConfig.PerLevelLootScale.Value + dmod.MaxAmountScaleFactorBonus + scale_factor;
            float selectedMod = UnityEngine.Random.Range(min_drop_scale, max_drop_scale);
            float chance = Mathf.Pow(selectedMod, level);
            float luck_roll = UnityEngine.Random.value;
            Logger.LogDebug($"ChancePerLevel roll {luck_roll} vs chance {chance} from range ({min_drop_scale} <-> {max_drop_scale}) = drop_base: {lootdrop_amount} * min:{min_drop_scale}/max:{max_drop_scale} scale from (factor:{scale_factor} + distance:(min){dmod.MinAmountScaleFactorBonus}/(max){dmod.MaxAmountScaleFactorBonus} perlevelscale:{dmod.MinAmountScaleFactorBonus})");
            if (luck_roll < chance) {
                return lootdrop_amount;
            }
            return 0;
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

        public static void SelectCharacterLootSettings(Character creature, out DistanceLootModifier distance_bonus) {
            Vector3 p = creature.gameObject.transform.position;
            //ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
            //creature_biome = biome;
            float distance_from_center = Vector2.Distance(p, DistanceScaleSystem.center);
            distance_bonus = SelectDistanceFromCenterLootBonus(distance_from_center);
            //Logger.LogDebug($"{creature.gameObject.name} {biome} {p}");
        }

        public static void SelectObjectDistanceBonus(Transform tform, out DistanceLootModifier distance_bonus) {
            Vector3 p = tform.position;
            //ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
            float distance_from_center = Vector2.Distance(p, DistanceScaleSystem.center);
            distance_bonus = SelectDistanceFromCenterLootBonus(distance_from_center);
            //Logger.LogDebug($"{creature.gameObject.name} {biome} {p}");
        }

        private static DistanceLootModifier SelectDistanceFromCenterLootBonus(float distance_from_center) {
            DistanceLootModifier distance_levelup_bonuses = new DistanceLootModifier();
            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LootSystemData.SLS_Drop_Settings != null && LootSystemData.SLS_Drop_Settings.DistanceLootModifier != null) {
                // Check if we are in a distance level bonus area
                foreach (KeyValuePair<int, DistanceLootModifier> kvp in LootSystemData.SLS_Drop_Settings.DistanceLootModifier) {
                    if (distance_from_center <= kvp.Key) {
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
