using BepInEx.Configuration;
using HarmonyLib;
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
                if (LootSystemData.SLS_Drop_Settings.characterSpecificLoot == null) { return true; }
                if (LootSystemData.SLS_Drop_Settings.characterSpecificLoot != null && LootSystemData.SLS_Drop_Settings.characterSpecificLoot.ContainsKey(name) != true) { return true; }

                List<KeyValuePair<GameObject, int>> drop_results = new List<KeyValuePair<GameObject, int>>();
                int level = 1;
                if (__instance.m_character != null) {
                    level = __instance.m_character.m_level;
                }
                
                SelectLootSettings(__instance.m_character, out DistanceLootModifier distance_bonus, out Heightmap.Biome biome);
                // Logger.LogDebug($"SLS Custom drop set for {name} - level {level}");
                // Use modified loot drop settings
                foreach(ExtendedDrop loot in LootSystemData.SLS_Drop_Settings.characterSpecificLoot[name]) {
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
                        if (loot.ChanceScaleFactor > 0f) { chance *= (scale_multiplier * level); }
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
                    if (loot.ScalePerNearbyPlayer) {
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
                        drop *= MultiplyLootPerLevel(level, distance_bonus, scale_factor);
                    } else {
                        drop *= ExponentLootPerLevel(level, distance_bonus, scale_factor);
                    }
                    Logger.LogDebug($"Drop {loot.Drop.Prefab} drops amount base {drop_base_amount} x scale_mult {scale_multiplier} x loot factor type {ValConfig.LootDropCalculationType.Value} ({scale_factor}) = {drop}");

                    // Enforce max drop cap
                    if (loot.MaxScaledAmount > 0 && drop > loot.MaxScaledAmount) { 
                        drop = loot.MaxScaledAmount;
                        Logger.LogDebug($"Drop {loot.Drop.Prefab} capped to {drop}");
                    }

                    //Logger.LogDebug($"Drop {loot.Drop.Prefab} capped to {drop}");
                    // Add the drop to the results
                    if (loot.GameDrop == null || loot.GameDrop.m_prefab == null) {
                        // Logger.LogDebug($"Drop Prefab not yet cached, updating and caching.");
                        loot.SetupDrop();
                    }

                    drop_results.Add(new KeyValuePair<GameObject, int>(loot.GameDrop.m_prefab, drop));
                }
                __result = drop_results;
                return false;
            }
        }

        private static int DetermineLootScale(Character character) {
            int char_level = 1;
            if (character != null) { char_level = character.GetLevel(); }
            SelectLootSettings(character, out DistanceLootModifier distance_bonus, out Heightmap.Biome biome);
            if (SelectedLootFactor == LootFactorType.PerLevel) {
                return MultiplyLootPerLevel(char_level, distance_bonus);
            } else {
                return ExponentLootPerLevel(char_level, distance_bonus);
            }
        }

        private static int MultiplyLootPerLevel(int level, DistanceLootModifier dmod, float scale_factor = 1f)
        {
            if (level == 1) { return 1; } // no scaling for level 1, just return the base loot amount
            float dmod_max = dmod.MaxAmountScaleFactorBonus;
            if (dmod_max <= 0) { dmod_max = 1; }
            float dmod_min = dmod.MinAmountScaleFactorBonus;
            if (dmod_min <= 0) { dmod_min = 1; }
            float loot_scale_factor = ValConfig.PerLevelLootScale.Value * level;
            int min_drop = (int)(loot_scale_factor * dmod_min * scale_factor);
            int max_drop = (int)(loot_scale_factor * dmod_max * scale_factor);
            Logger.LogDebug($"MLPL range: {min_drop}-{max_drop} using: loot_factor {loot_scale_factor} x {dmod_min} or {dmod_max} x {scale_factor}");
            if (min_drop == max_drop) {
                return min_drop;
            }
            return UnityEngine.Random.Range(min_drop, max_drop);
        }

        private static int ExponentLootPerLevel(int level, DistanceLootModifier dmod, float scale_factor = 1)
        {
            if (level == 1) { return 1; } // no scaling for level 1, just return the base loot amount
            float dmod_max = dmod.MaxAmountScaleFactorBonus;
            if (dmod_max <= 0) { dmod_max = 1; }
            float dmod_min = dmod.MinAmountScaleFactorBonus;
            if (dmod_min <= 0) { dmod_min = 1; }
            float loot_scale_factor = Mathf.Pow(ValConfig.PerLevelLootScale.Value, level);
            int min_drop = (int)(loot_scale_factor * dmod_min * scale_factor);
            int max_drop = (int)(loot_scale_factor * dmod_max * scale_factor);
            Logger.LogDebug($"ELPL range: {min_drop}-{max_drop} using: loot_factor {loot_scale_factor} x {dmod_min} or {dmod_max} x {scale_factor}");
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
                                if (chara == null)
                                {
                                    chara = droppedItem.GetComponent<Humanoid>();
                                }
                                if (chara != null)
                                {
                                    CompositeLazyCache.GetAndSetLocalCache(chara, spawnMultiplyCheck: false);
                                }
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

        

        public static IEnumerator DropItemsAsync(List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea) {
            int obj_spawns = 0;

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
                            ModificationExtensionSystem.CreatureSetup(chara, force: true);
                        }
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

        public static void SelectLootSettings(Character creature, out DistanceLootModifier distance_bonus, out Heightmap.Biome creature_biome)
        {
            Vector3 p = creature.gameObject.transform.position;
            ZoneSystem.instance.GetGroundData(ref p, out var normal, out var biome, out var biomeArea, out var hmap);
            creature_biome = biome;
            float distance_from_center = Vector2.Distance(p, LevelSystem.center);
            distance_bonus = SelectDistanceFromCenterLootBonus(distance_from_center);
            //Logger.LogDebug($"{creature.gameObject.name} {biome} {p}");
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
