using HarmonyLib;
using Jotunn.Managers;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection.Emit;
using UnityEngine;
using static ItemDrop;

namespace StarLevelSystem.modules
{
    public static class LootLevelsExpanded
    {
        [HarmonyPatch(typeof(CharacterDrop))]
        public static class ModifyLootPerLevelEffect
        {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CharacterDrop.GenerateDropList))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldloc_1),
                    new CodeMatch(OpCodes.Mul),
                    new CodeMatch(OpCodes.Stloc_S)
                    ).Advance(2).RemoveInstruction().InsertAndAdvance(
                    Transpilers.EmitDelegate(ModifyDropsForExtendedStars)
                    ).ThrowIfNotMatch("Unable to patch Character drop generator, drops will not be modified.");

                return codeMatcher.Instructions();
            }

            [HarmonyPatch(nameof(CharacterDrop.GenerateDropList))]
            public static void Postfix() {

            }

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
                            GameObject droppedItem = Object.Instantiate(item, centerPos, Quaternion.identity);

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
                    GameObject droppedItem = Object.Instantiate(item, centerPos, Quaternion.identity);
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



        public static int ModifyDropsForExtendedStars(int base_drop_amount, int level) {
            return (int)(ValConfig.PerLevelLootScale.Value * level * base_drop_amount);
        }
    }
}
