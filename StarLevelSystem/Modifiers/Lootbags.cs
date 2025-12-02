using HarmonyLib;
using JetBrains.Annotations;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class Lootbags
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, CreatureDetailCache ccache) {
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
                CreatureDetailCache cDetails = CompositeLazyCache.GetCacheOrZDOOnly(__instance.m_character);
                if (cDetails != null && cDetails.Modifiers != null && cDetails.Modifiers.ContainsKey(ModifierNames.Lootbags.ToString())) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.Lootbags.ToString(), cDetails.Modifiers[ModifierNames.Lootbags.ToString()]);
                    List <KeyValuePair<GameObject, int>> ExtraLoot = new List <KeyValuePair<GameObject, int>>();
                    float modifier = cmcfg.BasePower + cmcfg.PerlevelPower * cDetails.Level;
                    foreach (var kvp in __result) {
                        ExtraLoot.Add(new KeyValuePair<GameObject, int>(key: kvp.Key, value: Mathf.RoundToInt(kvp.Value * UnityEngine.Random.Range(0.5f, 1) * modifier)));
                    }
                    ZNet.instance.StartCoroutine(LootLevelsExpanded.DropItemsAsync(ExtraLoot, __instance.gameObject.transform.position, 1f));
                }
            }
        }
    }
}
