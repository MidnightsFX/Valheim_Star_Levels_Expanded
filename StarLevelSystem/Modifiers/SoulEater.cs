using HarmonyLib;
using JetBrains.Annotations;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class SoulEater
    {

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class SoulEaterOnDeath
        {
            private static void Postfix(Character __instance) {
                if (__instance == null || __instance.IsPlayer()) {
                    return;
                }
                

                List<Character> characters = Extensions.GetCharactersInRange(__instance.transform.position, 5);
                foreach (Character character in characters) {
                    // Logger.LogDebug($"Checking SoulEater on {character.name}");
                    if (character == null || character.IsPlayer()) { continue; }
                    CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(character, onlycache: true);
                    if (cDetails != null && cDetails.Modifiers.Keys.Contains(ModifierNames.SoulEater.ToString())) {
                        CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.SoulEater.ToString(), cDetails.Modifiers[ModifierNames.SoulEater.ToString()]);
                        int powerIncrease = Mathf.RoundToInt(cmcfg.PerlevelPower * character.m_level);
                        Logger.LogDebug($"SoulEater Increased on {character.name} by {cmcfg.PerlevelPower} * {character.m_level} = {powerIncrease}");
                        ModificationExtensionSystem.ForceUpdateDamageMod(character, powerIncrease);
                        ModificationExtensionSystem.LoadApplySizeModifications(character.gameObject, character.m_nview, cDetails, true, true, 0.01f);
                        character.Heal(character.GetMaxHealth() * cmcfg.PerlevelPower);
                    }
                }
            }
        }
    }
}
