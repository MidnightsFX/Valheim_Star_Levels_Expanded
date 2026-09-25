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
        // Sent to each soul-eater's owner for every death it feeds on. See SoulEaterAndEvolveOnDeath.
        private const string RPC_SoulEaterFeed = "SLS_SoulEaterFeed";

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class RegisterSoulEaterFeedRPC
        {
            private static void Postfix(Character __instance) {
                // Same condition vanilla registers its own Character RPCs under.
                if (__instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.GetZDO() == null) { return; }
                __instance.m_nview.Register(RPC_SoulEaterFeed, (long sender) => Feed(__instance));
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class SoulEaterAndEvolveOnDeath
        {
            private static void Prefix(Character __instance) {
                // Owner only, like vanilla's own death work: a creature with a death animation runs OnDeath from
                // the animation event on every client that plays it, which healed the soul-eaters around it once
                // per client.
                if (__instance == null || __instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.IsOwner() == false) {
                    return;
                }
                
                List<Character> characters = SLSExtensions.GetCharactersInRange(__instance.transform.position, 5);
                string soulEaterKey = ModifierNames.SoulEater.ToString();
                foreach (Character character in characters) {
                    // Logger.LogDebug($"Checking SoulEater on {character.name}");
                    if (character == null || character.IsPlayer() || character == __instance) { continue; }
                    if (character.m_nview == null || character.m_nview.IsValid() == false || character.m_nview.HasOwner() == false) { continue; }
                    // The SoulEater modifier belongs to the SURVIVOR feeding on this death. Reading it
                    // off __instance (the creature that died) buffed every bystander around a dying
                    // SoulEater while a living SoulEater never grew.
                    Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(character);
                    if (mods != null && mods.ContainsKey(soulEaterKey)) {
                        // This peer owns the creature that died, not necessarily the survivor, and its growth is a
                        // set of writes to the survivor's ZDO. Its owner makes them; InvokeRPC routes there, and runs
                        // in place when that is this peer.
                        character.m_nview.InvokeRPC(RPC_SoulEaterFeed);
                    }
                }
            }
        }

        // Runs on the soul-eater's owner.
        private static void Feed(Character character) {
            if (character == null || character.m_nview == null || character.m_nview.IsValid() == false || character.m_nview.IsOwner() == false) { return; }
            string soulEaterKey = ModifierNames.SoulEater.ToString();
            CharacterCacheEntry cDetails = CompositeLazyCache.GetCacheEntry(character);
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(character);
            if (cDetails == null || mods == null || mods.ContainsKey(soulEaterKey) == false) { return; }
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
