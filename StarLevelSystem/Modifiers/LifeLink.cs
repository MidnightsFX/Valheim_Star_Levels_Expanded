using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Damage;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    public static class LifeLink
    {
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        public static class LifeLinkDamageDistributionPatch {
            // Per-creature redirect cooldown (owner-local; RPC_Damage runs on the damaged creature's
            // owner). A single static float here was one GLOBAL cooldown shared by every LifeLink
            // creature in the world.
            static readonly Dictionary<ZDOID, float> NextAllowedRedirection = new Dictionary<ZDOID, float>();

            public static void Prefix(Character __instance, HitData hit) {
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance);
                if (mods != null && mods.ContainsKey(ModifierNames.LifeLink.ToString())) {
                    //Logger.LogDebug($"Lifelink triggered for {__instance.name}");
                    CreatureModifierConfiguration cm = CreatureModifiersData.GetModifierDef(ModifierNames.LifeLink.ToString(), mods[ModifierNames.LifeLink.ToString()]);
                    // Fraction of the hit the linked target keeps; the linked neighbour absorbs the rest.
                    float kept_fraction = 1 - (cm.Config.BasePower + (cm.Config.PerlevelPower * __instance.m_level));
                    kept_fraction = Mathf.Clamp(kept_fraction, 0.1f, 1f);

                    HitData transferHit = new HitData() { m_attacker = hit.m_attacker, m_damage = hit.m_damage };

                    // Minimum damage to transfer is 20
                    if (transferHit.GetTotalDamageOptions() < 20f) {
                        return;
                    }

                    // Not allowed to redirect damage more than once every second, to prevent infinite loops and excessive damage transfer
                    ZDOID cid = __instance.GetZDOID();
                    if (NextAllowedRedirection.TryGetValue(cid, out float nextAllowed) && Time.realtimeSinceStartup < nextAllowed) {
                        return;
                    }

                    // Split, not duplicate: the neighbour absorbs the redirected share and the original
                    // hit keeps the rest. Previously BOTH sides took kept_fraction x the full hit, so a
                    // LifeLink creature INCREASED the total damage dealt (up to ~2x the incoming hit).
                    transferHit.m_damage.Modify(1f - kept_fraction);
                    // The source hit already carries the attacker's bonuses; without this the transfer
                    // re-enters Character.Damage and the attacker-side prefixes apply them again.
                    DamageModifications.MarkSynthesized(transferHit);
                    NextAllowedRedirection[cid] = Time.realtimeSinceStartup + 1f;
                    if (NextAllowedRedirection.Count > 256) { PruneExpiredCooldowns(); }

                    List<Character> CharactersNearby = SLSExtensions.GetCharactersInRange(__instance.transform.position, 15f);
                    bool transferred = false;
                    foreach (Character character in CharactersNearby) {
                        // No players, and not self
                        if (character.IsPlayer() || character == __instance) { continue; }
                        if (Logger.IsDebugEnabled) { Logger.LogDebug($"Distributing Damage to {character.m_name}"); }

                        // TODO: Improve VFX for this
                        //if (CreatureModifiersData.LoadedSecondaryEffects.ContainsKey(CreatureModifiersData.ModifierDefinitions[ModifierNames.LifeLink.ToString()].SecondaryEffect)) {
                        //    Vector3 targetTravel = __instance.transform.position - character.transform.position;
                        //    GameObject go = GameObject.Instantiate(CreatureModifiersData.LoadedSecondaryEffects[CreatureModifiersData.ModifierDefinitions[ModifierNames.LifeLink.ToString()].SecondaryEffect], targetTravel, Quaternion.identity);
                        //}
                        
                        character.Damage(transferHit);
                        transferred = true;
                        break;
                    }

                    if (transferred) {
                        hit.m_damage.Modify(kept_fraction);
                    }
                }
            }

            private static void PruneExpiredCooldowns() {
                float now = Time.realtimeSinceStartup;
                List<ZDOID> expired = new List<ZDOID>();
                foreach (KeyValuePair<ZDOID, float> kvp in NextAllowedRedirection) {
                    if (kvp.Value < now) { expired.Add(kvp.Key); }
                }
                foreach (ZDOID id in expired) { NextAllowedRedirection.Remove(id); }
            }
        }
    }
}
