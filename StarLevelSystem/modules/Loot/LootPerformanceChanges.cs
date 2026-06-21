using StarLevelSystem.common;
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

        public static void DropItemsPreferAsync(Vector3 position, List<LootEntry> optimizeDrops, bool immediate = false, bool dropThatCharacterDrop = false, List<KeyValuePair<GameObject, int>> characterDropSource = null) {
            if (immediate == false) {
                TaskRunner.Run().StartCoroutine(DropItemsAsync(optimizeDrops, position, 0.5f, dropThatCharacterDrop, characterDropSource));
            } else {
                DropItemsImmediate(optimizeDrops, position, 0.5f, dropThatCharacterDrop, characterDropSource);
            }
        }

        public static List<LootEntry> DropItemsDetermineDropStackSize(List<KeyValuePair<GameObject, int>> drops, bool dropIndividuals = false) {
            List<LootEntry> LootDrops = new List<LootEntry>();
            int MaxDropSize = 1;

            // ReferenceIndex mirrors each entry's position in 'drops' so DropThat compat can look up the
            // matching drop config (DropThat keys its config cache by the source list + index).
            for (int sourceIndex = 0; sourceIndex < drops.Count; sourceIndex++) {
                KeyValuePair<GameObject, int> drop = drops[sourceIndex];
                if (dropIndividuals == false) {
                    ItemDrop id = drop.Key.GetComponent<ItemDrop>();
                    if (id != null) {
                        MaxDropSize = id.m_itemData.m_shared.m_maxStackSize;
                    }
                }
                LootDrops.Add(new LootEntry() { Amount = drop.Value, Prefab = drop.Key, MaxAmountPerDrop = MaxDropSize, ReferenceIndex = sourceIndex });
            }

            return LootDrops;
        }

        public static int CheckItemStackingConfig(ItemDrop id, DropType dropType) {
            if (id == null) { return 1; }
            // Determine the max drop size per entry
            int maxPerStack = 2;
            switch (dropType) {
                case DropType.Rock:
                    if (!ValConfig.RockLootDropsStacked.Value) {
                        maxPerStack = 1;
                    }
                    break;
                case DropType.Tree:
                    if (!ValConfig.TreeLootDropsStacked.Value) {
                        maxPerStack = 1;
                    }
                    break;
                case DropType.Destructible:
                    if (!ValConfig.MiscLootDropsStacked.Value) {
                        maxPerStack = 1;
                    }
                    break;
                case DropType.Item:
                    if (!ValConfig.CreatureLootDropStacked.Value) {
                        maxPerStack = 1;
                    }
                    break;
                case DropType.None:
                    break;
            }

            // Check the items specific max drop size
            if (maxPerStack == 2) {
                return id.m_itemData.m_shared.m_maxStackSize;
            }

            return maxPerStack;
        }


        private static void DropItemsImmediate(List<LootEntry> drops, Vector3 centerPos, float dropArea, bool dropThatCharacterDrop = false, List<KeyValuePair<GameObject, int>> characterDropSource = null) {
            foreach (LootEntry drop in drops) {
                int max_stack_size;
                if (ValConfig.EnableDebugLootDetails.Value) {
                    Logger.LogDebug($"Dropping {drop.Prefab.name} {drop.Amount}");
                }
                for (int i = 0; i < drop.Amount;) {
                    max_stack_size = drop.MaxAmountPerDrop;
                    // Drop the item at the specified position
                    GameObject droppedItem = UnityEngine.Object.Instantiate(drop.Prefab, centerPos, Quaternion.identity);
                    // Number of units this dropped object represents (defaults to 1 for non-stacked items and creatures)
                    int dropped = 1;

                    // Modify the dropped item to be dropped in stacks up to the specified amount
                    if (max_stack_size > 1) {
                        ItemDrop component = droppedItem.GetComponent<ItemDrop>();
                        // Drop in stacks if this is an item
                        if (component is not null) {
                            int stack = Mathf.Min(drop.Amount - i, max_stack_size);
                            component.m_itemData.m_stack = stack;
                            component.m_itemData.m_worldLevel = (byte)Game.m_worldLevel;
                            dropped = stack;
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

                    // Compat for DropThat: re-apply DropThat's item modifiers to creature loot SLS instantiated itself.
                    if (dropThatCharacterDrop && characterDropSource != null && Compatibility.IsDropThatEnabled && Compatibility.DropThatCharacterModifyAvailable) {
                        Compatibility.DropThat_ModifyCharacterDrop(droppedItem, characterDropSource, drop.ReferenceIndex);
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
                    i += dropped;
                }
            }
        }



        private static IEnumerator DropItemsAsync(List<LootEntry> drops, Vector3 centerPos, float dropArea, bool dropThatCharacterDrop = false, List<KeyValuePair<GameObject, int>> characterDropSource = null) {
            int obj_spawns = 0;
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
                    // Number of units this dropped object represents (defaults to 1 for non-stacked items and creatures)
                    int dropped = 1;

                    if (max_stack_size > 1) {
                        ItemDrop component = droppedItem.GetComponent<ItemDrop>();
                        // Drop in stacks if this is an item
                        if (component is not null) {
                            int stack = Mathf.Min(drop.Amount - i, max_stack_size);
                            component.m_itemData.m_stack = stack;
                            component.m_itemData.m_worldLevel = (byte)Game.m_worldLevel;
                            dropped = stack;
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

                    // Compat for DropThat: re-apply DropThat's item modifiers to creature loot SLS instantiated itself.
                    if (dropThatCharacterDrop && characterDropSource != null && Compatibility.IsDropThatEnabled && Compatibility.DropThatCharacterModifyAvailable) {
                        Compatibility.DropThat_ModifyCharacterDrop(droppedItem, characterDropSource, drop.ReferenceIndex);
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
                    i += dropped;
                }
            }

            yield break;
        }
    }
}
