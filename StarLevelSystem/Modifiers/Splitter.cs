using HarmonyLib;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
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
                if (cDetails != null && cDetails.Modifiers.ContainsKey(ModifierNames.Splitter.ToString())) {
                    Logger.LogDebug("Checking splitter multiplication");
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.Splitter.ToString(), cDetails.Modifiers[ModifierNames.Splitter.ToString()]);
                    float totalsplits = cmcfg.BasePower + (__instance.m_level * cmcfg.PerlevelPower);
                    // Split based on scaled creature level and the base split power
                    bool shouldTame = __instance.IsTamed();
                    while (totalsplits >= 1) {
                        GameObject sgo = GameObject.Instantiate(cDetails.CreaturePrefab, __instance.transform.position, __instance.transform.rotation);
                        totalsplits -= 1f;
                        if (shouldTame) { sgo.GetComponent<Character>()?.SetTamed(true); }
                        Character sChar = sgo.GetComponent<Character>();
                        if (sChar != null && ValConfig.SplittersInheritLevel.Value) {
                            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(sChar);
                            sChar.SetLevel(cDetails.Level);
                            cdc.Level = cDetails.Level;
                            ModificationExtensionSystem.CreatureSetup(sChar, delayedSetupTimer: 0);
                        } else {
                            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(sChar);
                            sChar.SetLevel(cdc.Level);
                            ModificationExtensionSystem.CreatureSetup(sChar, delayedSetupTimer: 0);
                        }
                    }
                }
            }
        }
    }
}
