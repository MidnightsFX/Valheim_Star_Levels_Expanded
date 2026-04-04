using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.modules.Loot {
    internal class LootPerformanceChanges {

        public static void DropItemsPreferAsync(Vector3 position, List<KeyValuePair<GameObject, int>> optimizeDrops, bool immediate = false, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false) {
            if (immediate == false) {
                TaskRunner.Run().StartCoroutine(DropItemsAsync(optimizeDrops, position, 0.5f, dropThatCharacterDrop));
            } else {
                DropItemsImmediate(optimizeDrops, position, 0.5f, dropThatCharacterDrop, dropThatNonCharacterDrop);
            }
        }

        private static void DropItemsImmediate(List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false) {
            int dropindex = 0;
            foreach (var drop in drops) {
                bool set_stack_size = false;
                int max_stack_size = 0;
                var item = drop.Key;
                int amount = drop.Value;
                if (ValConfig.EnableDebugLootDetails.Value) {
                    Logger.LogDebug($"Dropping {item.name} {amount}");
                }
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
                            CreatureSetupControl.CreatureSetup(chara, multiply: false);
                        }
                    }

                    // Compat for DropThat
                    // Send the dropped items to drop that to allow modifications
                    if (dropThatCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyDrop(droppedItem, drops, dropindex);
                    }
                    if (dropThatNonCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyInstantiatedObjectDrop(droppedItem, dropindex);
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



        private static IEnumerator DropItemsAsync(List<KeyValuePair<GameObject, int>> drops, Vector3 centerPos, float dropArea, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false) {
            int obj_spawns = 0;
            int dropindex = 0;
            foreach (var drop in drops) {
                bool set_stack_size = false;
                int max_stack_size = 0;
                var item = drop.Key;
                int amount = drop.Value;
                if (ValConfig.EnableDebugLootDetails.Value) {
                    Logger.LogDebug($"Dropping async {item.name} {amount}");
                }
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
                            CreatureSetupControl.CreatureSetup(chara, delay: 0.5f);
                        }
                    }

                    // Compat for DropThat
                    // Send the dropped items to drop that to allow modifications
                    if (dropThatCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyDrop(droppedItem, drops, dropindex);
                    }
                    if (dropThatNonCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyInstantiatedObjectDrop(droppedItem, dropindex);
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
    }
}
