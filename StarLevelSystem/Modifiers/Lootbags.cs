using HarmonyLib;
using JetBrains.Annotations;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;
using static StarLevelSystem.Modifiers.Summoner;

namespace StarLevelSystem.Modifiers
{
    internal static class Lootbags
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CreatureDetailCache ccache)
        {
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.Speed] += 0.25f;
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth] += 1f;
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        public static class CreatureLootMultiplied {
            public static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result) {
                if (__instance.m_character == null || __instance.m_character.IsPlayer()) {
                    return;
                }
                CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance.m_character);
                if (cDetails != null && cDetails.Modifiers.ContainsKey(ModifierNames.Lootbags)) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.Lootbags, cDetails.Modifiers[ModifierNames.Lootbags]);
                    List <KeyValuePair<GameObject, int>> ExtraLoot = new List <KeyValuePair<GameObject, int>>();
                    float modifier = cmcfg.basepower + cmcfg.perlevelpower * cDetails.Level;
                    foreach (var kvp in __result) {
                        ExtraLoot.Add(new KeyValuePair<GameObject, int>(key: kvp.Key, value: Mathf.RoundToInt(kvp.Value * UnityEngine.Random.Range(0.5f, 1) * modifier)));
                    }
                    Player.m_localPlayer.StartCoroutine(LootLevelsExpanded.DropItemsAsync(ExtraLoot, __instance.gameObject.transform.position, 1f));
                }
            }
        }
    }
}
