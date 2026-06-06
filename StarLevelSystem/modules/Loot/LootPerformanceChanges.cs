using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Loot {
    internal class LootPerformanceChanges {

        public static void DropItemsPreferAsync(Vector3 position, List<LootEntry> optimizeDrops, bool immediate = false, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false) {
            if (immediate == false) {
                TaskRunner.Run().StartCoroutine(DropItemsAsync(optimizeDrops, position, 0.5f, dropThatCharacterDrop));
            } else {
                DropItemsImmediate(optimizeDrops, position, 0.5f, dropThatCharacterDrop, dropThatNonCharacterDrop);
            }
        }

        public static List<LootEntry> DropItemsDetermineDropStackSize(List<KeyValuePair<GameObject, int>> drops, bool dropIndividuals = false) {
            List<LootEntry> LootDrops = new List<LootEntry>();
            int MaxDropSize = 1;

            foreach(KeyValuePair<GameObject, int> drop in drops) {
                if (dropIndividuals == false) {
                    ItemDrop id = drop.Key.GetComponent<ItemDrop>();
                    if (id != null) {
                        MaxDropSize = id.m_itemData.m_shared.m_maxStackSize;
                    }
                }
                LootDrops.Add(new LootEntry() { Amount = drop.Value, Prefab = drop.Key, MaxAmountPerDrop = MaxDropSize });
            }

            return LootDrops;
        }

        public static int CheckItemStackingConfig(DropType dropType) {
            // Determine the max drop size per entry
            int maxPerStack = 2;
            switch (dropType) {
                case DropType.Rock:
                    if (ValConfig.RockLootDropsStacked.Value) {
                        maxPerStack = 1;
                    }
                    break;
                case DropType.Tree:
                    if (ValConfig.TreeLootDropsStacked.Value) {
                        maxPerStack = 1;
                    }
                    break;
                case DropType.Destructible:
                    if (ValConfig.MiscLootDropsStacked.Value) {
                        maxPerStack = 1;
                    }
                    break;
                case DropType.Item:
                    if (ValConfig.CreatureLootDropStacked.Value) {
                        maxPerStack = 1;
                    }
                    break;
                case DropType.None:
                    break;
            }
            return maxPerStack;
        }


        private static void DropItemsImmediate(List<LootEntry> drops, Vector3 centerPos, float dropArea, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false) {
            int dropindex = 0;

            foreach (LootEntry drop in drops) {
                int max_stack_size = 0;
                if (ValConfig.EnableDebugLootDetails.Value) {
                    Logger.LogDebug($"Dropping {drop.Prefab.name} {drop.Amount}");
                }
                for (int i = 0; i < drop.Amount;) {
                    max_stack_size = drop.MaxAmountPerDrop;
                    // Drop the item at the specified position
                    GameObject droppedItem = UnityEngine.Object.Instantiate(drop.Prefab, centerPos, Quaternion.identity);

                    // Modify the dropped item to be dropped in stacks up to the specified amount
                    if (max_stack_size > 1) {
                        ItemDrop component = droppedItem.GetComponent<ItemDrop>();
                        // Drop in stacks if this is an item
                        if (component is not null) {
                            int remaining = (drop.Amount - i);
                            if (remaining > 0) {
                                if (drop.Amount > max_stack_size) {
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
                    }


                    // Compat for DropThat
                    // Send the dropped items to drop that to allow modifications
                    if (dropThatNonCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyInstantiatedObjectDrop(droppedItem, dropindex);
                    }

                    // Poke the item to make it feel like a loot drop
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



        private static IEnumerator DropItemsAsync(List<LootEntry> drops, Vector3 centerPos, float dropArea, bool dropThatCharacterDrop = false, bool dropThatNonCharacterDrop = false) {
            int obj_spawns = 0;
            int dropindex = 0;
            foreach (LootEntry drop in drops) {
                int max_stack_size = drop.MaxAmountPerDrop;
                if (ValConfig.EnableDebugLootDetails.Value) {
                    Logger.LogDebug($"Dropping async {drop.Prefab.name} {drop.Amount}");
                }
                for (int i = 0; i < drop.Amount;) {

                    // Wait for a short duration to avoid dropping too many items at once
                    if (obj_spawns > 0 && obj_spawns % ValConfig.LootDropsPerTick.Value == 0) {
                        yield return new WaitForSeconds(0.1f);
                    }

                    // Drop the item at the specified position
                    GameObject droppedItem = UnityEngine.Object.Instantiate(drop.Prefab, centerPos, Quaternion.identity);
                    obj_spawns++;

                    if (max_stack_size > 1) {
                        ItemDrop component = droppedItem.GetComponent<ItemDrop>();
                        // Drop in stacks if this is an item
                        if (component is not null) {
                            int remaining = (drop.Amount - i);
                            if (remaining > 0) {
                                if (drop.Amount > max_stack_size) {
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
                    }

                    // Compat for DropThat
                    // Send the dropped items to drop that to allow modifications
                    if (dropThatNonCharacterDrop && Compatibility.IsDropThatEnabled && Compatibility.DropThatMethodsAvailable) {
                        Compatibility.DropThat_ModifyInstantiatedObjectDrop(droppedItem, dropindex);
                    }

                    // Make the loot wiggle
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
