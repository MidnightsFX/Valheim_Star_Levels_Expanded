using HarmonyLib;
using JetBrains.Annotations;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Sizes;
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
        public static class SoulEaterAndEvolveOnDeath
        {
            private static void Prefix(Character __instance) {
                if (__instance == null || __instance.IsPlayer()) {
                    return;
                }
                
                List<Character> characters = SLSExtensions.GetCharactersInRange(__instance.transform.position, 5);
                string soulEaterKey = ModifierNames.SoulEater.ToString();
                foreach (Character character in characters) {
                    // Logger.LogDebug($"Checking SoulEater on {character.name}");
                    if (character == null || character.IsPlayer() || character == __instance) { continue; }
                    CharacterCacheEntry cDetails = CompositeLazyCache.GetCacheEntry(character);
                    // The SoulEater modifier belongs to the SURVIVOR feeding on this death. Reading it
                    // off __instance (the creature that died) buffed every bystander around a dying
                    // SoulEater while a living SoulEater never grew.
                    Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(character);
                    if (cDetails != null && mods != null && mods.ContainsKey(soulEaterKey)) {
                        CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(soulEaterKey, mods[soulEaterKey]);
                        float powerIncrease = cmcfg.PerlevelPower * character.m_level;
                        if (Logger.IsDebugEnabled) { Logger.LogDebug($"SoulEater Increased on {character.name} by {cmcfg.PerlevelPower} * {character.m_level} = {powerIncrease}"); }
                        DamageModifications.ForceUpdateDamageMod(character, powerIncrease);
                        int nearbyDeaths = character.m_nview.GetZDO().GetInt(SLS_SOULEATER, 0);
                        nearbyDeaths += 1;
                        character.m_nview.GetZDO().Set(SLS_SOULEATER, nearbyDeaths);
                        SizeModifications.SetSizeModification(character.gameObject, character.m_nview, cDetails, true, 0.01f * nearbyDeaths);
                        character.Heal(character.GetMaxHealth() * cmcfg.PerlevelPower);
                    }
                }
            }
        }
    }
}
