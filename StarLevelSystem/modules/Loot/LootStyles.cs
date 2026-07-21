using BepInEx.Configuration;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static CharacterDrop;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Loot {
    internal static class LootStyles {

        public static LootFactorType SelectedLootFactor = LootFactorType.PerLevel;

        internal static readonly AcceptableValueList<string> AllowedLootFactors = new AcceptableValueList<string>(new string[] {
            LootFactorType.PerLevel.ToString(),
            LootFactorType.Exponential.ToString(),
            LootFactorType.ChancePerLevel.ToString()
        });

        // Initial parse only - called once after the config binds.
        internal static void ParseLootFactor() {
            SelectedLootFactor = (LootFactorType)Enum.Parse(typeof(LootFactorType), ValConfig.LootDropCalculationType.Value);
        }

        // Live re-parse (SettingChanged / RPC / file-watch).
        internal static void LootFactorChanged(object s, EventArgs e) {
            ParseLootFactor();
        }

        internal static List<LootEntry> ModifyTreeDropsOrDefault(TreeBase treeInstance) {
            int level = CompositeLazyCache.GetOrAddCachedTreeEntry(treeInstance.m_nview);
            SelectObjectDistanceBonus(treeInstance.transform, out DistanceLootModifier distance_bonus);
            List<LootEntry> drops = ModifyObjectDropsOrDefault(treeInstance.m_dropWhenDestroyed, Utils.GetPrefabName(treeInstance.gameObject), level, distance_bonus, DropType.Tree);
            return drops;
        }

        internal static List<LootEntry> ModifyTreeDropsOrDefault(TreeLog treeInstance) {
            int level = CompositeLazyCache.GetOrAddCachedTreeEntry(treeInstance.m_nview);
            SelectObjectDistanceBonus(treeInstance.transform, out DistanceLootModifier distance_bonus);
            List<LootEntry> drops = ModifyObjectDropsOrDefault(treeInstance.m_dropWhenDestroyed, Utils.GetPrefabName(treeInstance.gameObject), level, distance_bonus, DropType.Tree);
            return drops;
        }

        internal static List<LootEntry> ModifyRockDropsOrDefault(Transform tform, DropTable droptable, string name, int level) {
            SelectObjectDistanceBonus(tform, out DistanceLootModifier distance_bonus);
            List<LootEntry> drops = ModifyObjectDropsOrDefault(droptable, name, level, distance_bonus, DropType.Rock);
            return drops;
        }

        internal static List<LootEntry> ModifyPickableDropsOrDefault(Transform tform, string name, int level) {
            SelectObjectDistanceBonus(tform, out DistanceLootModifier distance_bonus);
            List<LootEntry> drops = ModifyObjectDropsOrDefault(null, name, level, distance_bonus, DropType.Destructible, tform, honorOnePerPlayer: true);
            return drops;
        }


        internal static List<LootEntry> ModifyObjectDropsOrDefault(DropTable defaultDrops, string lookupkey, int level, DistanceLootModifier distance_bonus, DropType type = DropType.Tree, Transform tform = null, bool honorOnePerPlayer = false) {
            List<LootEntry> dropList = new List<LootEntry>();
            // Accumulate per-drop detail and emit one consolidated summary at the end (mirrors ModifyCharacterDrops).
            // Gate the string building on the flag directly so it costs nothing when loot debugging is disabled.
            bool logloot = ValConfig.EnableDebugLootDetails.Value;
            StringBuilder sb = new StringBuilder();
            if (LootSystemData.SLS_Drop_Settings != null && LootSystemData.SLS_Drop_Settings.NonCharacterSpecificLoot != null && LootSystemData.SLS_Drop_Settings.NonCharacterSpecificLoot.ContainsKey(lookupkey)) {
                if (logloot) { sb.AppendLine($"Custom loot table for {lookupkey} (level {level}):"); }
                foreach (ExtendedObjectDrop eod in LootSystemData.SLS_Drop_Settings.NonCharacterSpecificLoot[lookupkey]) {

                    // If chance is enabled, calculate all of the chance characteristics
                    if (eod.Drop.Chance < 1) {
                        float luck_roll = UnityEngine.Random.value;
                        float chance = eod.Drop.Chance;
                        // Per-drop chance ramp, plus (PerLevel style only) the global per-level chance increase.
                        float chanceScale = eod.ChanceScaleFactor;
                        if (SelectedLootFactor == LootFactorType.PerLevel) { chanceScale += ValConfig.PerLevelLootChanceScale.Value; }
                        if (chanceScale > 0f) { chance *= 1 + (chanceScale * level); }
                        // check the chance for this to be rolled
                        if (luck_roll > chance) {
                            if (logloot) { sb.AppendLine($"Drop {eod.Drop.Prefab} failed random drop chance ({luck_roll} > {chance})."); }
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

                    // Determine how large the dropped stacks can be (honors the per-type stacking config)
                    int maxPerStack = LootPerformanceChanges.CheckItemStackingConfig(eod.DropGo.GetComponent<ItemDrop>(), type);

                    if (eod.Drop.DontScale == true) {
                        // Does not scale with level: just roll the configured range, optionally multiplied per player.
                        int flat_amount = UnityEngine.Random.Range(eod.Drop.Min, eod.Drop.Max);
                        if (logloot) { sb.AppendLine($"Drop {eod.Drop.Prefab}: does not scale, base {flat_amount} from range [{eod.Drop.Min}..{eod.Drop.Max})."); }
                        if (honorOnePerPlayer && eod.Drop.OnePerPlayer && tform != null) {
                            int playersNearby = Mathf.Max(1, Player.GetPlayersInRangeXZ(tform.position, 500f));
                            if (logloot) { sb.AppendLine($"  onePerPlayer: {flat_amount} * {playersNearby} players = {flat_amount * playersNearby}."); }
                            flat_amount *= playersNearby;
                        }
                        if (logloot) { sb.AppendLine($"=> {eod.Drop.Prefab} final amount {flat_amount}."); }
                        dropList.Add(new LootEntry() { Prefab = eod.DropGo, Amount = flat_amount, MaxAmountPerDrop = maxPerStack });
                        continue;
                    }

                    float scale_multiplier = 1f;
                    int drop_min = eod.Drop.Min;
                    int drop_max = eod.Drop.Max;
                    int drop_base_amount = drop_min;
                    if (drop_min != drop_max) {
                        drop_base_amount = UnityEngine.Random.Range(drop_min, drop_max);
                        if (logloot) { sb.AppendLine($"Rolling base drop {eod.Drop.Prefab} {drop_min}<->{drop_max} result:{drop_base_amount}."); }
                    }

                    // Set scale modifier for the loot drop, based on chance, if enabled
                    if (eod.UseChanceAsMultiplier) {
                        scale_multiplier = (eod.Drop.Chance * level);
                        if (logloot) { sb.AppendLine($"  chance-as-multiplier: chance {eod.Drop.Chance} * level {level} = {scale_multiplier}."); }
                    }

                    // Determine the actual amount of the drop
                    int drop = drop_base_amount;
                    float scale_factor = eod.AmountScaleFactor * scale_multiplier;
                    if (scale_factor <= 0f) { scale_factor = 1f; }
                    if (logloot) { sb.AppendLine($"  scaleFactor {scale_factor} (amountScaleFactor {eod.AmountScaleFactor} * chanceMult {scale_multiplier}); factor {SelectedLootFactor}, level {level}, distanceBonus min {distance_bonus.MinAmountScaleFactorBonus}/max {distance_bonus.MaxAmountScaleFactorBonus}."); }
                    switch (SelectedLootFactor) {
                        case LootFactorType.PerLevel:
                            drop = MultiplyLootPerLevel(drop, level, distance_bonus, scale_factor, logloot ? sb : null);
                            break;
                        case LootFactorType.Exponential:
                            drop = ExponentLootPerLevel(drop, level, distance_bonus, scale_factor, logloot ? sb : null);
                            break;
                        case LootFactorType.ChancePerLevel:
                            // All-or-nothing lottery: base amount or nothing, chance scaled by level. The base
                            // Drop.Chance gate (top of loop) still applies; this does not re-roll it.
                            drop = ChancePerLevel(drop, level, distance_bonus, scale_factor, logloot ? sb : null);
                            break;
                    }

                    // Enforce max drop cap
                    if (eod.MaxScaledAmount > 0 && drop > eod.MaxScaledAmount) {
                        if (logloot) { sb.AppendLine($"  capped {drop} -> {eod.MaxScaledAmount} (MaxScaledAmount)."); }
                        drop = eod.MaxScaledAmount;
                    }

                    if (honorOnePerPlayer && eod.Drop.OnePerPlayer && tform != null) {
                        int playersNearby = Mathf.Max(1, Player.GetPlayersInRangeXZ(tform.position, 500f));
                        if (logloot) { sb.AppendLine($"  onePerPlayer: {drop} * {playersNearby} players = {drop * playersNearby}."); }
                        drop *= playersNearby;
                    }

                    if (logloot) { sb.AppendLine($"=> {eod.Drop.Prefab} final amount {drop}."); }
                    dropList.Add(new LootEntry() { Prefab = eod.DropGo, Amount = drop, MaxAmountPerDrop = maxPerStack });
                }

            } else {
                // generate the default loot list for this object
                if (defaultDrops == null) { return dropList; }
                DropTable updatedDropTable = new DropTable();
                switch (type) {
                    case DropType.Tree:
                        if (logloot) { sb.AppendLine($"Default Tree drop table for {lookupkey} (level {level}):"); }
                        updatedDropTable = UpdateDropTableByLevel(defaultDrops, level, ValConfig.PerLevelTreeLootScale.Value);
                        break;
                    case DropType.Rock:
                        if (logloot) { sb.AppendLine($"Default Rock drop table for {lookupkey} (level {level}):"); }
                        updatedDropTable = UpdateDropTableByLevel(defaultDrops, level, ValConfig.PerLevelMineRockLootScale.Value);
                        break;
                    case DropType.Destructible:
                        if (logloot) { sb.AppendLine($"Default Destructible drop table for {lookupkey} (level {level}):"); }
                        updatedDropTable = UpdateDropTableByLevel(defaultDrops, level, ValConfig.PerLevelDestructibleLootScale.Value);
                        break;
                }
                dropList = OptimizeToDropStacks(updatedDropTable.GetDropList(), type);
            }

            if (logloot) {
                foreach (LootEntry le in dropList) {
                    sb.AppendLine($"  -> {Utils.GetPrefabName(le.Prefab)} x{le.Amount} (max {le.MaxAmountPerDrop}/stack)");
                }
                Logger.LogLoot($"Generated drops for {lookupkey}:\n{sb}");
            }
            return dropList;
        }

        internal static List<KeyValuePair<GameObject, int>> ModifyCharacterDrops(CharacterDrop cdrop, string name, List<ExtendedCharacterDrop> customLoot = null) {
            List<KeyValuePair<GameObject, int>> drop_results = new List<KeyValuePair<GameObject, int>>();
            int level = 1;
            if (cdrop.m_character != null) {
                level = cdrop.m_character.m_level;
            }

            LootStyles.SelectCharacterLootSettings(cdrop.m_character, out DistanceLootModifier distance_bonus);
            // Logger.LogDebug($"SLS Custom drop set for {name} - level {level}");
            // Use modified loot drop settings; per-creature custom loot replaces the global table when present.
            List<ExtendedCharacterDrop> lootSet = customLoot ?? LootSystemData.SLS_Drop_Settings.CharacterSpecificLoot[name];
            StringBuilder sb = new StringBuilder();
            foreach (ExtendedCharacterDrop loot in lootSet) {
                // Skip this loop drop if it doesn't drop for tamed creatures or only drops for tamed creatures
                if (cdrop.m_character != null) {
                    // Log only when the drop is actually skipped for tame state.
                    if (loot.UntamedOnlyDrop && cdrop.m_character.IsTamed()) {
                        if (ValConfig.EnableDebugLootDetails.Value) { sb.AppendLine($"Skipping {loot.Drop.Prefab}: untamed-only drop on a tamed creature."); }
                        continue;
                    }
                    if (loot.TamedOnlyDrop && cdrop.m_character.IsTamed() != true) {
                        if (ValConfig.EnableDebugLootDetails.Value) { sb.AppendLine($"Skipping {loot.Drop.Prefab}: tamed-only drop on an untamed creature."); }
                        continue;
                    }
                }

                // Ensure the prefab is resolved; skip drops whose prefab cannot be found so we never
                // hand a null GameObject to vanilla Ragdoll.SaveLootList / ZNetScene.GetPrefabHash.
                if (loot.GameDrop == null || loot.GameDrop.m_prefab == null) {
                    loot.ToCharacterDrop();
                    if (loot.GameDrop == null || loot.GameDrop.m_prefab == null) {
                        Logger.LogWarning($"Loot prefab '{loot.Drop?.Prefab}' for '{name}' was not found. Ensure it is spelled correctly and available in the game. This drop will be skipped.");
                        continue;
                    }
                }

                float scale_multiplier = 1f;

                // If chance is enabled, calculate all of the chance characteristics
                if (loot.Drop.Chance < 1) {
                    float luck_roll = UnityEngine.Random.value;
                    float chance = loot.Drop.Chance;
                    // Per-drop chance ramp, plus (PerLevel style only) the global per-level chance increase.
                    float chanceScale = loot.ChanceScaleFactor;
                    if (SelectedLootFactor == LootFactorType.PerLevel) { chanceScale += ValConfig.PerLevelLootChanceScale.Value; }
                    if (chanceScale > 0f) { chance *= 1 + (chanceScale * level); }
                    // check the chance for this to be rolled
                    if (luck_roll > chance) {
                        if (ValConfig.EnableDebugLootDetails.Value) {
                            sb.AppendLine($"Drop {loot.Drop.Prefab} failed random drop chance ({luck_roll} > {chance}).");
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
                    switch (SelectedLootFactor) {
                        case LootFactorType.PerLevel:
                            drop = MultiplyLootPerLevel(drop, level, distance_bonus, scale_factor);
                            break;
                        case LootFactorType.Exponential:
                            drop = ExponentLootPerLevel(drop, level, distance_bonus, scale_factor);
                            break;
                        case LootFactorType.ChancePerLevel:
                            // All-or-nothing lottery: full base amount or nothing (0 is harmless downstream).
                            drop = ChancePerLevel(drop, level, distance_bonus, scale_factor);
                            break;
                    }
                    if (ValConfig.EnableDebugLootDetails.Value) {
                        sb.AppendLine($"Drop {loot.Drop.Prefab} Using {SelectedLootFactor} factor {scale_factor} base {drop_base_amount} = {drop}");
                    }
                    // Enforce max drop cap
                    if (loot.MaxScaledAmount > 0 && drop > loot.MaxScaledAmount) {
                        drop = loot.MaxScaledAmount;
                        Logger.LogLoot($"Drop {loot.Drop.Prefab} capped to {drop}");
                    }
                }


                // Modify the multiplier for how many this drops based on local players
                if (loot.Drop.OnePerPlayer) {
                    int playersNearby = Player.GetPlayersInRangeXZ(cdrop.transform.position, 500f);
                    drop *= Mathf.Max(1, playersNearby);
                }

                drop_results.Add(new KeyValuePair<GameObject, int>(loot.GameDrop.m_prefab, drop));
            }
            if (ValConfig.EnableDebugLootDetails.Value) {
                Logger.LogLoot($"Generated drops for {name}:\n{sb}");
            }
            return drop_results;
        }

        // Routes a derivation line either into a consolidated summary (when detail is provided) or straight to the
        // loot log (self-gated on EnableDebugLootDetails). Lets the scaling helpers contribute their full breakdown
        // to ModifyObjectDropsOrDefault's summary without double-logging.
        private static void AppendOrLog(StringBuilder detail, string msg) {
            if (detail != null) { detail.AppendLine(msg); return; }
            Logger.LogLoot(msg);
        }

        // This builds the total amount of loot from this style of loot modification
        // Loot amount x level x modifiers
        private static int MultiplyLootPerLevel(int lootDrop_amount, int level, DistanceLootModifier damageMod, float scale_factor = 1f, StringBuilder detail = null) {
            if (level == 1) {
                // no scaling for level 1, just return the base loot amount
                if (detail != null) { detail.AppendLine($"  level 1: no per-level scaling, amount stays {lootDrop_amount}."); }
                return lootDrop_amount;
            }
            float min_drop_scale = (level * ValConfig.PerLevelLootScale.Value) * (scale_factor + damageMod.MinAmountScaleFactorBonus);
            float max_drop_scale = (level * ValConfig.PerLevelLootScale.Value) * (scale_factor + damageMod.MaxAmountScaleFactorBonus);
            // If its the same no need to randomize
            if (min_drop_scale == max_drop_scale) {
                int result = Mathf.RoundToInt(min_drop_scale * lootDrop_amount);
                AppendOrLog(detail, $"  MultiplyLootPerLevel {result} = base {lootDrop_amount} * {min_drop_scale} scale from (lvl:{level} * PerLevelLootScale:{ValConfig.PerLevelLootScale.Value}) * (factor:{scale_factor} + scaleFactorBonus:{damageMod.MinAmountScaleFactorBonus})");
                return result;
            }
            int min_drop = Mathf.RoundToInt(min_drop_scale * lootDrop_amount);
            int max_drop = Mathf.RoundToInt(max_drop_scale * lootDrop_amount);
            int randomized_result = UnityEngine.Random.Range(min_drop, max_drop);
            AppendOrLog(detail, $"  MultiplyLootPerLevel {randomized_result} picked from [{min_drop}..{max_drop}) = base {lootDrop_amount} * scale min:{min_drop_scale}/max:{max_drop_scale} from (lvl:{level} * PerLevelLootScale:{ValConfig.PerLevelLootScale.Value}) * (factor:{scale_factor} + scaleFactorBonus min:{damageMod.MinAmountScaleFactorBonus}/max:{damageMod.MaxAmountScaleFactorBonus})");
            return randomized_result;
        }

        private static int ExponentLootPerLevel(int lootDrop_amount, int level, DistanceLootModifier damageMod, float scale_factor = 1, StringBuilder detail = null) {
            if (level == 1) {
                // no scaling for level 1, just return the base loot amount
                if (detail != null) { detail.AppendLine($"  level 1: no per-level scaling, amount stays {lootDrop_amount}."); }
                return lootDrop_amount;
            }

            float min_drop_scale = ValConfig.PerLevelLootScale.Value + damageMod.MinAmountScaleFactorBonus + scale_factor;
            float max_drop_scale = ValConfig.PerLevelLootScale.Value + damageMod.MaxAmountScaleFactorBonus + scale_factor;
            float selectedMod = UnityEngine.Random.Range(min_drop_scale, max_drop_scale);
            float loot_scale_factor = Mathf.Pow(selectedMod, level);
            int result = Mathf.RoundToInt(loot_scale_factor * lootDrop_amount);
            AppendOrLog(detail, $"  ExponentLootPerLevel {result} = base {lootDrop_amount} * ({selectedMod}^lvl{level} = {loot_scale_factor}); mod picked from [{min_drop_scale}..{max_drop_scale}) = (factor:{scale_factor} + distance min:{damageMod.MinAmountScaleFactorBonus}/max:{damageMod.MaxAmountScaleFactorBonus} + PerLevelLootScale:{ValConfig.PerLevelLootScale.Value})");
            return result;
        }

        // All-or-nothing lottery: the drop's chance to appear scales with level (scale^level); on success the
        // full base amount drops, on failure nothing drops. Level 1 (base creatures/objects) always drops the
        // base amount, mirroring the level==1 short-circuit in MultiplyLootPerLevel/ExponentLootPerLevel.
        private static int ChancePerLevel(int lootDrop_amount, int level, DistanceLootModifier damageMod, float scale_factor = 1, StringBuilder detail = null) {
            if (level <= 1) {
                if (detail != null) { detail.AppendLine($"  level 1: no per-level chance scaling, amount stays {lootDrop_amount}."); }
                return lootDrop_amount;
            }
            float min_drop_scale = ValConfig.PerLevelLootScale.Value + damageMod.MinAmountScaleFactorBonus + scale_factor;
            float max_drop_scale = ValConfig.PerLevelLootScale.Value + damageMod.MaxAmountScaleFactorBonus + scale_factor;
            float selectedMod = UnityEngine.Random.Range(min_drop_scale, max_drop_scale);
            float chance = Mathf.Pow(selectedMod, level);
            float luck_roll = UnityEngine.Random.value;
            bool won = luck_roll < chance;
            AppendOrLog(detail, $"  ChancePerLevel roll {luck_roll} vs chance {chance} ({selectedMod}^lvl{level}) -> {(won ? $"win, amount {lootDrop_amount}" : "loss, amount 0")}; mod picked from [{min_drop_scale}..{max_drop_scale}) = (factor:{scale_factor} + distance min:{damageMod.MinAmountScaleFactorBonus}/max:{damageMod.MaxAmountScaleFactorBonus} + PerLevelLootScale:{ValConfig.PerLevelLootScale.Value})");
            return won ? lootDrop_amount : 0;
        }

        internal static DropTable UpdateDropTableByLevel(DropTable dropTable, int level, float mod) {
            // Update For pure level based distance scaling on rocks
            if (level > 1) {
                DropTable newDropTable = dropTable.Clone();
                List<DropTable.DropData> dropReplacement = new List<DropTable.DropData>();
                foreach (DropTable.DropData drop in dropTable.m_drops) {
                    DropTable.DropData newDrop = drop;
                    newDrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (1 + (mod * level)));
                    newDrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (1 + (mod * level)));
                    Logger.LogDebug($"Scaling drop {drop.m_item} from {drop.m_stackMin}-{drop.m_stackMax} to {newDrop.m_stackMin}-{newDrop.m_stackMax} for level {level}.");
                    dropReplacement.Add(newDrop);
                }
                newDropTable.m_drops = dropReplacement;
                return newDropTable;
            }
            return dropTable;
        }

        internal static List<LootEntry> OptimizeToDropStacks(List<GameObject> drops, DropType dropType = DropType.None, float lootMultiplier = 1) {
            List<LootEntry> optimizeDrops = new List<LootEntry>();
            Dictionary<GameObject, int> dropCollect = new Dictionary<GameObject, int>();
            foreach (GameObject drop in drops) {
                if (dropCollect.ContainsKey(drop)) {
                    dropCollect[drop] += 1;
                    continue;
                }
                dropCollect.Add(drop, 1);
            }

            // Apply loot increases or decreases if we have those set, else just add to the drop list
            if (lootMultiplier != 1) {
                foreach (KeyValuePair<GameObject, int> kvp in dropCollect) {
                    int maxPerStack = LootPerformanceChanges.CheckItemStackingConfig(kvp.Key.GetComponent<ItemDrop>(), dropType);
                    int amount = Mathf.RoundToInt(kvp.Value * lootMultiplier);
                    Logger.LogDebug($"{kvp.Key} loot modified: {kvp.Value} * {lootMultiplier} = {amount}");
                    optimizeDrops.Add(new LootEntry() { Prefab = kvp.Key, Amount = amount, MaxAmountPerDrop = maxPerStack });
                }
            } else {
                foreach (KeyValuePair<GameObject, int> detailDrop in dropCollect) {
                    int maxPerStack = LootPerformanceChanges.CheckItemStackingConfig(detailDrop.Key.GetComponent<ItemDrop>(), dropType);
                    optimizeDrops.Add(new LootEntry() { Prefab = detailDrop.Key, Amount = detailDrop.Value, MaxAmountPerDrop = maxPerStack });
                }
            }
            return optimizeDrops;
        }

        public static void SelectCharacterLootSettings(Character creature, out DistanceLootModifier distance_bonus) {
            Vector3 p = creature.gameObject.transform.position;
            //creature_biome = biome;
            // Distance must be radial in the X-Z ground plane; passing Vector3s to Vector2.Distance drops z and
            // uses altitude (y) instead. Mirror LevelSelection.DetermineDistanceBonus.
            float distance_from_center = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(DistanceScaleSystem.center.x, DistanceScaleSystem.center.z));
            distance_bonus = SelectDistanceFromCenterLootBonus(distance_from_center);
            //Logger.LogDebug($"{creature.gameObject.name} {biome} {p}");
        }

        public static void SelectObjectDistanceBonus(Transform transform, out DistanceLootModifier distance_bonus) {
            Vector3 p = transform.position;
            // Radial X-Z distance (see SelectCharacterLootSettings note).
            float distance_from_center = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(DistanceScaleSystem.center.x, DistanceScaleSystem.center.z));
            distance_bonus = SelectDistanceFromCenterLootBonus(distance_from_center);
            //Logger.LogDebug($"{creature.gameObject.name} {biome} {p}");
        }

        private static DistanceLootModifier SelectDistanceFromCenterLootBonus(float distance_from_center) {
            DistanceLootModifier distance_levelup_bonuses = new DistanceLootModifier();
            // Gate on the loot-specific YAML flag (EnableDistanceLootModifier) so it is authoritative, rather than
            // borrowing the level system's EnableDistanceLevelScalingBonus toggle.
            if (LootSystemData.SLS_Drop_Settings != null && LootSystemData.SLS_Drop_Settings.EnableDistanceLootModifier && LootSystemData.SLS_Drop_Settings.DistanceLootModifier != null) {
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
