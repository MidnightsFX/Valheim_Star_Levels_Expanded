using HarmonyLib;
using StarLevelSystem.Data;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    public static class Splitter {
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class CharacterOnDeath {
            public static void Prefix(Character __instance) {
                if (__instance == null || __instance.IsPlayer()) {
                    return;
                }
                CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(__instance);
                if (cDetails != null && cDetails.Modifiers.ContainsKey(ModifierNames.Splitter)) {
                    Logger.LogDebug("Checking splitter multiplication");
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.Splitter, cDetails.Modifiers[ModifierNames.Splitter]);
                    float totalsplits = cmcfg.basepower + (__instance.m_level * cmcfg.perlevelpower);
                    // Split based on scaled creature level and the base split power
                    while (totalsplits >= 1) {
                        GameObject.Instantiate(cDetails.CreaturePrefab, __instance.transform.position, __instance.transform.rotation);
                        totalsplits -= 1f;
                    }
                }
            }
        }
    }
}
