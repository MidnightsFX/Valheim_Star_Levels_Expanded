using HarmonyLib;
using JetBrains.Annotations;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.Loot;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class Lootbags
    {
        [UsedImplicitly]
        public static void RunOnce(Character _, CreatureModConfig __, CharacterCacheEntry ccache) {
            if (ccache == null) { return; }
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.Speed] += 0.15f;
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth] += 0.3f;
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        public static class CreatureLootMultiplied {
            public static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result) {
                if (__instance == null || __instance.m_character == null || __instance.m_character.IsPlayer()) {
                    return;
                }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance.m_character);
                if (mods != null && mods.ContainsKey(ModifierNames.Lootbags.ToString())) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.Lootbags.ToString(), mods[ModifierNames.Lootbags.ToString()]);
                    List<LootEntry> ExtraLoot = new List<LootEntry>();
                    float modifier = cmcfg.BasePower + cmcfg.PerlevelPower * __instance.m_character.m_level;
                    HashSet<GameObject> fixedDrops = FixedCustomDrops(__instance.m_character);

                    foreach (var kvp in __result) {
                        if (kvp.Key == null) { continue; }
                        if (fixedDrops != null && fixedDrops.Contains(kvp.Key)) { continue; }
                        int maxPerStack = LootPerformanceChanges.CheckItemStackingConfig(kvp.Key.GetComponent<ItemDrop>(), DropType.Item);
                        ExtraLoot.Add(new LootEntry() { Prefab = kvp.Key, Amount = Mathf.RoundToInt(kvp.Value * UnityEngine.Random.Range(0.5f, 1) * modifier), MaxAmountPerDrop = maxPerStack });
                    }
                    // Async: the extra loot is a bonus burst on top of the regular drops, and the
                    // immediate path instantiated every item synchronously inside the death frame.
                    LootPerformanceChanges.DropItemsPreferAsync(__instance.gameObject.transform.position, ExtraLoot);
                }
            }

            // Drops this creature's own loot table (a Nemesis boss's reward) marks as not scaling. They are a fixed
            // payout: multiplying them let a Lootbags boss drop several times its table's coin range.
            private static HashSet<GameObject> FixedCustomDrops(Character character) {
                List<ExtendedCharacterDrop> customLoot = LootSystemData.GetCustomLoot(character);
                if (customLoot == null) { return null; }
                HashSet<GameObject> fixedDrops = new HashSet<GameObject>();
                foreach (ExtendedCharacterDrop loot in customLoot) {
                    if (loot?.GameDrop?.m_prefab == null) { continue; }
                    if (loot.DoesNotScale || loot.Drop.DontScale) { fixedDrops.Add(loot.GameDrop.m_prefab); }
                }
                return fixedDrops;
            }
        }
    }
}
