using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System.Collections.Generic;
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
                CreatureDetailCache cDetails = CompositeLazyCache.GetCacheOrZDOOnly(__instance);
                if (cDetails != null && cDetails.Modifiers.ContainsKey(ModifierNames.Splitter.ToString())) {
                    Logger.LogDebug("Checking splitter multiplication");
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.Splitter.ToString(), cDetails.Modifiers[ModifierNames.Splitter.ToString()]);
                    float totalsplits = cmcfg.BasePower + (__instance.m_level * cmcfg.PerlevelPower);
                    // Split based on scaled creature level and the base split power
                    bool shouldTame = __instance.IsTamed();
                    while (totalsplits >= 1) {
                        GameObject creatureToCreate = cDetails.CreaturePrefab;
                        if (cDetails.CreaturePrefab == null) {
                            creatureToCreate = PrefabManager.Instance.GetPrefab(Utils.GetPrefabName(__instance.gameObject));
                        }
                        if (creatureToCreate != null) { break; }
                        GameObject sgo = GameObject.Instantiate(creatureToCreate, __instance.transform.position, __instance.transform.rotation);
                        totalsplits -= 1f;
                        if (shouldTame) { sgo.GetComponent<Character>()?.SetTamed(true); }
                        Character sChar = sgo.GetComponent<Character>();
                        if (sChar != null) {
                            if (ValConfig.SplittersInheritLevel.Value) {
                                ModificationExtensionSystem.CreatureSetup(sChar, cDetails.Level, false, notAllowedModifiers: new List<string>() { ModifierNames.Splitter.ToString() });
                            } else {
                                ModificationExtensionSystem.CreatureSetup(sChar, cDetails.Level - 1, false, notAllowedModifiers: new List<string>() { ModifierNames.Splitter.ToString() });
                            }
                        }
                        
                    }
                }
            }
        }
    }
}
