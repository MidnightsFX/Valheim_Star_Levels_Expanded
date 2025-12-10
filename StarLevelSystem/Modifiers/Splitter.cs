using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System;
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
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance);
                if (mods != null && mods.ContainsKey(ModifierNames.Splitter.ToString())) {
                    
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.Splitter.ToString(), mods[ModifierNames.Splitter.ToString()]);
                    float totalsplits = cmcfg.BasePower + (__instance.m_level * cmcfg.PerlevelPower);
                    // Split based on scaled creature level and the base split power
                    bool shouldTame = __instance.IsTamed();
                    int level = Mathf.RoundToInt(__instance.m_level / totalsplits);
                    if (level <= 0) { level = 1; }
                    if (ValConfig.SplittersInheritLevel.Value == false) {
                        level = UnityEngine.Random.Range(1, level);
                    }
                    Logger.LogDebug($"Splitter on {__instance.name} total split potential:{totalsplits} split creature level: {level}");
                    while (totalsplits >= 1) {
                        GameObject creatureToCreate = PrefabManager.Instance.GetPrefab(Utils.GetPrefabName(__instance.gameObject));
                        if (creatureToCreate == null) { break; }
                        GameObject sgo = GameObject.Instantiate(creatureToCreate, __instance.transform.position, __instance.transform.rotation);
                        totalsplits -= 1f;
                        if (shouldTame) { sgo.GetComponent<Character>().SetTamed(true); }
                        Character sChar = sgo.GetComponent<Character>();
                        if (sChar != null) {
                            CompositeLazyCache.GetAndSetLocalCache(sChar, level, notAllowedModifiers: new List<string>() { ModifierNames.Splitter.ToString() });
                            ModificationExtensionSystem.CreatureSetup(sChar, multiply: false);
                            CreatureModifiers.RemoveCreatureModifier(sChar, ModifierNames.Splitter.ToString());
                        }
                        
                    }
                }
            }
        }
    }
}
