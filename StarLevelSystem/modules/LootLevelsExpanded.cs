using BepInEx.Configuration;
using HarmonyLib;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
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
            SelectedLootFactor = (LootFactorType)Enum.Parse(typeof(LootFactorType), ValConfig.LootDropCaluationType.Value);
        }

        //[HarmonyPatch(typeof(CharacterDrop))]
        //public static class ModifyLootPerLevelEffect
        //{
        //    //[HarmonyDebug]
        //    [HarmonyTranspiler]
        //    [HarmonyPatch(nameof(CharacterDrop.GenerateDropList))]
        //    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
        //    {
        //        var codeMatcher = new CodeMatcher(instructions);
        //        codeMatcher.MatchStartForward(
        //            new CodeMatch(OpCodes.Ldloc_S),
        //            new CodeMatch(OpCodes.Ldloc_1),
        //            new CodeMatch(OpCodes.Mul),
        //            new CodeMatch(OpCodes.Stloc_S)
        //            ).Advance(2).RemoveInstruction().InsertAndAdvance(
        //            Transpilers.EmitDelegate(ModifyDropsForExtendedStars)
        //            ).ThrowIfNotMatch("Unable to patch Character drop generator, drops will not be modified.");

        //        return codeMatcher.Instructions();
        //    }

        //}

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        public static class ModifyLootPerLevelEffect
        {
            public static bool Prefix(List<KeyValuePair<GameObject, int>> __result, CharacterDrop __instance) {
                List<KeyValuePair<GameObject, int>> drop_results = new List<KeyValuePair<GameObject, int>>();
                int level = 1;
                if (__instance.m_character != null) { level = __instance.m_character.GetLevel(); }
                SelectLootSettings(__instance.gameObject, out string name, out DistanceLootModifier distance_bonus, out Heightmap.Biome biome);

                if (LootSystemData.SLS_Drop_Settings.characterSpecificLoot.ContainsKey(name)) {
                    // Use modified loot drop settings
                    foreach(ExtendedDrop loot in LootSystemData.SLS_Drop_Settings.characterSpecificLoot[name]) {
                        // Skip this loop drop if it doesn't drop for tamed creatures or only drops for tamed creatures
                        if (__instance.m_character != null) {
                            if (loot.untamedOnlyDrop && __instance.m_character.IsTamed()) { continue; }
                            if (loot.tamedOnlyDrop && __instance.m_character.IsTamed() != true) { continue; }
                        }

                        float scale_multiplier = 1f;
                        
                        // If chance is enabled, calculate all of the chance characteristics
                        if (loot.Drop.chance < 1) {
                            float chance = UnityEngine.Random.value;
                            if (loot.chanceScaleFactor > 0f) { chance *= (scale_multiplier * level); }
                            // check the chance for this to be rolled
                            if (chance < loot.Drop.chance) { continue; }
                        }
                        // Set scale modifier for the loot drop, based on chance, if enabled
                        if (loot.useChanceAsMultiplier) {
                            scale_multiplier = (loot.chanceScaleFactor * level);
                        }

                        // Modify the multiplier for how many this drops based on local players
                        if (loot.scalePerNearbyPlayer) {
                            scale_multiplier += Player.GetPlayersInRangeXZ(__instance.transform.position, 500f);
                        }

                        int drop_min = loot.Drop.min;
                        int drop_max = loot.Drop.max;
                        // Scale the min/max drops based on their per level scale factors
                        if (loot.minAmountScaleFactor > 0f) { drop_min = (int)(drop_min * loot.minAmountScaleFactor * level); }
                        if (loot.maxAmountScaleFactor > 0f) { drop_max = (int)(drop_max * loot.maxAmountScaleFactor * level); }

                        int drop_base_amount = drop_min;
                        if (drop_min != drop_max) {
                            if (loot.scalebyMaxLevel) {
                                drop_base_amount = ((drop_max - drop_min) / ValConfig.MaxLevel.Value) * level;
                            } else {
                                drop_base_amount = UnityEngine.Random.Range(drop_min, drop_max);
                            }
                        }

                        // Determine the actual amount of the drop
                        int drop = (int)(drop_base_amount * scale_multiplier);
                        if (SelectedLootFactor == LootFactorType.PerLevel) {
                            drop = multiplyLootPerLevel(level, drop, distance_bonus);
                        } else {
                            drop = ExponentLootPerLevel(level, drop, distance_bonus);
                        }

                        // Enforce max drop cap
                        if (loot.maxScaledAmount > 0 && drop > loot.maxScaledAmount) { drop = loot.maxScaledAmount; }
                        
                        // Add the drop to the results
                        drop_results.Add(new KeyValuePair<GameObject, int>(loot.gameDrop.m_prefab, drop));
                    }
                }

                // Vanilla loot setup with configuration
                foreach (var drop in __instance.m_drops) {
                    // Roll the chance for this drop, if its enabled
                    if (drop.m_chance < 1 && drop.m_chance < UnityEngine.Random.value) {
                        continue;
                    }

                    // Determine scaling 
                    int drop_base_amount = drop.m_amountMin;
                    if (drop.m_amountMin != drop.m_amountMax) {
                        drop_base_amount = UnityEngine.Random.Range(drop.m_amountMin, drop.m_amountMax);
                    }

                    if (drop.m_dontScale) {
                        drop_results.Add(new KeyValuePair<GameObject, int>(drop.m_prefab, drop_base_amount));
                        continue;
                    }

                    if (SelectedLootFactor == LootFactorType.PerLevel) {
                        drop_results.Add(new KeyValuePair<GameObject, int>(drop.m_prefab, multiplyLootPerLevel(level, drop_base_amount, distance_bonus)));
                        continue;
                    }

                    // Expoential scaling is the vanilla default, but chances are we dont want to use that, hence why this is the fallthrough
                    drop_results.Add(new KeyValuePair<GameObject, int>(drop.m_prefab, ExponentLootPerLevel(level, drop_base_amount, distance_bonus)));
                }

                return false;
            }
        }

        private static int multiplyLootPerLevel(int level, int loot, DistanceLootModifier dmod)
        {
            float dmod_max = dmod.maxAmountScaleFactorBonus;
            if (dmod_max <= 0) { dmod_max = 1; }
            float dmod_min = dmod.minAmountScaleFactorBonus;
            if (dmod_min <= 0) { dmod_min = 1; }
            float loot_scale_factor = ValConfig.PerLevelLootScale.Value * level;
            int min_drop = (int)(loot_scale_factor * dmod_min * loot);
            int max_drop = (int)(loot_scale_factor * dmod_max * loot);
            if (min_drop == max_drop) {
                return min_drop;
            }
            return UnityEngine.Random.Range(min_drop, max_drop);
        }

        private static int ExponentLootPerLevel(int level, int loot, DistanceLootModifier dmod)
        {
            float dmod_max = dmod.maxAmountScaleFactorBonus;
            if (dmod_max <= 0) { dmod_max = 1; }
            float dmod_min = dmod.minAmountScaleFactorBonus;
            if (dmod_min <= 0) { dmod_min = 1; }
            float loot_scale_factor = Mathf.Pow(ValConfig.PerLevelLootScale.Value, level);
            int min_drop = (int)(loot_scale_factor * dmod_min * loot);
            int max_drop = (int)(loot_scale_factor * dmod_max * loot);
            if (min_drop == max_drop) {
                return min_drop;
            }
            return UnityEngine.Random.Range(min_drop, max_drop);
        }

        [HarmonyPatch(typeof(CharacterDrop))]
        public static class  DropItemsPerformancePatch
        {
            // effectively replace the drop items function since we need to drop things in a way that is not insane for large amounts of loot
            [HarmonyPatch(nameof(CharacterDrop.DropItems))]
            public static bool Prefix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea)
            {
                //float loot_multiplier = 1;
                //if (__instance.m_character != null)
                //{
                //    loot_multiplier = __instance.m_character.GetLevel() * ValConfig.PerLevelLootScale.Value;
                //} else {
                //    Humanoid hu = __instance.gameObject.GetComponent<Humanoid>();
                //    if (hu != null) {
                //        loot_multiplier = hu.GetLevel() * ValConfig.PerLevelLootScale.Value;
                //    }

                //}

                if (Player.m_localPlayer != null)
                {
                    Player.m_localPlayer.StartCoroutine(DropItemsAsync(drops, centerPos, dropArea));
                }
                else
                {
                    foreach (var drop in drops)
                    {
                        bool set_stack_size = false;
                        int max_stack_size = 0;
                        var item = drop.Key;
                        int amount = drop.Value;
                        Logger.LogDebug($"Dropping {item.name} {amount}");
                        for (int i = 0; i < amount;)
                        {
                            // Drop the item at the specified position
                            GameObject droppedItem = UnityEngine.Object.Instantiate(item, centerPos, Quaternion.identity);

                            ItemDrop component = droppedItem.GetComponent<ItemDrop>();
                            if (set_stack_size == false)
                            {
                                set_stack_size = true;
                                if (component) { max_stack_size = component.m_itemData.m_shared.m_maxStackSize; }
                            }

                            // Drop in stacks if this is an item
                            if ((object)component != null)
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

                            Rigidbody component2 = droppedItem.GetComponent<Rigidbody>();
                            if ((bool)component2)
                            {
                                Vector3 insideUnitSphere = UnityEngine.Random.insideUnitSphere;
                                if (insideUnitSphere.y < 0f)
                                {
                                    insideUnitSphere.y = 0f - insideUnitSphere.y;
                                }
                                component2.AddForce(insideUnitSphere * 5f, ForceMode.VelocityChange);
                            }
                            i++;
                        }
                    }
                }

                return false;
            }
        }

        

        static IEnumerator DropItemsAsync(List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea) {
            int obj_spawns = 0;

            foreach (var drop in drops)
            {
                bool set_stack_size = false;
                int max_stack_size = 0;
                var item = drop.Key;
                int amount = drop.Value;
                Logger.LogDebug($"Dropping {item.name} {amount}");
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
                    if ((object)component != null) {
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
                    }

                    Rigidbody component2 = droppedItem.GetComponent<Rigidbody>();
                    if ((bool)component2) {
                        Vector3 insideUnitSphere = UnityEngine.Random.insideUnitSphere;
                        if (insideUnitSphere.y < 0f) {
                            insideUnitSphere.y = 0f - insideUnitSphere.y;
                        }
                        component2.AddForce(insideUnitSphere * 5f, ForceMode.VelocityChange);
                    }
                    i++;
                }
            }

            yield break;
        }

        public static void SelectLootSettings(GameObject creature, out string creature_name, out DistanceLootModifier distance_bonus, out Heightmap.Biome creature_biome)
        {
            Vector3 p = creature.transform.position;
            creature_name = creature.name.Replace("(Clone)", "");
            ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
            creature_biome = biome;
            float distance_from_center = Vector2.Distance(p, LevelSystem.center);
            distance_bonus = SelectDistanceFromCenterLootBonus(distance_from_center);
            Logger.LogDebug($"{creature_name} {biome} {p}");
        }

        private static DistanceLootModifier SelectDistanceFromCenterLootBonus(float distance_from_center)
        {
            DistanceLootModifier distance_levelup_bonuses = new DistanceLootModifier() { };
            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LootSystemData.SLS_Drop_Settings.DistanceLootModifier != null)
            {
                // Check if we are in a distance level bonus area
                foreach (KeyValuePair<int, DistanceLootModifier> kvp in LootSystemData.SLS_Drop_Settings.DistanceLootModifier)
                {
                    if (distance_from_center <= kvp.Key)
                    {
                        Logger.LogDebug($"Distance Loot area: {kvp.Key}");
                        distance_levelup_bonuses = kvp.Value;
                        break;
                    }
                }
            }
            return distance_levelup_bonuses;
        }
    }
}
