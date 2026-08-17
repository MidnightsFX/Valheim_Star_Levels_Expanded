using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.Modifiers;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    public static class Splitter {
        // Hard cap: totalsplits scales with level x PerlevelPower, so a misconfigured or very
        // high-level creature could otherwise instantiate an unbounded number of full creatures.
        private const int MaxSplits = 10;
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
                        level = UnityEngine.Random.Range(1, level + 1);
                    }
                    Logger.LogDebug($"Splitter on {__instance.name} total split potential:{totalsplits} split creature level: {level}");
                    int splits = Mathf.Min(MaxSplits, Mathf.FloorToInt(totalsplits));
                    if (splits < 1) { return; }
                    GameObject creatureToCreate = PrefabManager.Instance.GetPrefab(Utils.GetPrefabName(__instance.gameObject));
                    if (creatureToCreate == null) { return; }
                    // Spread the spawns across frames: each split is a full creature instantiate + setup,
                    // and doing them all synchronously inside the death prefix produced a frame hitch that
                    // scaled with the creature's level.
                    TaskRunner.Run().StartCoroutine(SpawnSplitsAsync(creatureToCreate, __instance.transform.position, __instance.transform.rotation, splits, level, shouldTame));
                }
            }

            private static IEnumerator SpawnSplitsAsync(GameObject prefab, Vector3 position, Quaternion rotation, int splits, int level, bool shouldTame) {
                List<string> notAllowed = new List<string>() { ModifierNames.Splitter.ToString() };
                for (int i = 0; i < splits; i++) {
                    GameObject sgo = GameObject.Instantiate(prefab, position, rotation);
                    Character sChar = sgo.GetComponent<Character>();
                    if (sChar != null) {
                        if (shouldTame) { sChar.SetTamed(true); }
                        CompositeLazyCache.GetAndSetLocalCache(sChar, level, updateCache: true, notAllowedModifiers: notAllowed);
                        CreatureSetupControl.CreatureSetup(sChar, level, multiply: false, delay: 0.1f);
                        CreatureModifiers.RemoveCreatureModifier(sChar, ModifierNames.Splitter.ToString());
                    }
                    yield return null;
                }
            }
        }
    }
}
