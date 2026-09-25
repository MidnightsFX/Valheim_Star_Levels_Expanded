using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.Modifiers;
using StarLevelSystem.modules.Sizes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers {
    internal static class Evolve {

        // Sent to the killer's owner for each kill it makes. See SoulEaterAndEvolveOnDeath.
        private const string RPC_EvolveKill = "SLS_EvolveKill";

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class RegisterEvolveKillRPC {
            private static void Postfix(Character __instance) {
                // Same condition vanilla registers its own Character RPCs under.
                if (__instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.GetZDO() == null) { return; }
                __instance.m_nview.Register(RPC_EvolveKill, (long sender) => CountKill(__instance));
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class SoulEaterAndEvolveOnDeath {
            private static void Prefix(Character __instance) {
                // Owner only, like vanilla's own death work: a creature with a death animation runs OnDeath from
                // the animation event on every client that plays it, which counted the kill (and could level the
                // killer) once per client.
                if (__instance == null || __instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.IsOwner() == false || __instance.m_lastHit == null) {
                    return;
                }
                Character chara = __instance.m_lastHit.GetAttacker();
                if (chara == null || chara.m_nview == null || chara.m_nview.IsValid() == false || chara.m_nview.HasOwner() == false) { return; }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(chara);
                if (mods != null && mods.Keys.Contains(ModifierNames.Evolving.ToString())) {
                    // This peer owns the creature that died, not necessarily its killer, and the kill count and any
                    // level-up are writes to the killer's ZDO. Its owner makes them; InvokeRPC routes there, and runs
                    // in place when that is this peer.
                    chara.m_nview.InvokeRPC(RPC_EvolveKill);
                }
            }
        }

        // Runs on the killer's owner.
        private static void CountKill(Character chara) {
            if (chara == null || chara.m_nview == null || chara.m_nview.IsValid() == false || chara.m_nview.IsOwner() == false) { return; }
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(chara);
            if (mods != null && mods.Keys.Contains(ModifierNames.Evolving.ToString())) {
                CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.Evolving.ToString(), mods[ModifierNames.Evolving.ToString()]);
                int kills = chara.m_nview.GetZDO().GetInt(SLS_EVOLVE, 0);
                kills += 1;
                int level = chara.m_level;
                int levelup_req = Mathf.RoundToInt(cmcfg.BasePower + (cmcfg.PerlevelPower * level));
                Logger.LogDebug($"Evolve check: {kills} >= {levelup_req}");
                if (kills >= levelup_req) {
                    int newLevel = level + 1;
                    chara.m_nview.GetZDO().Set(ZDOVars.s_level, newLevel);
                    // Vanilla only copies s_level into m_level in Character.Awake, and GetAndSetLocalCache only
                    // reconciles m_level for non-owners - so on the owning client the live level stayed behind
                    // the ZDO, and everything reading it (speed/health scaling, the hud stars, the modifier
                    // name/icon budget) lagged a level. Same pairing StartZOwnerCreatureRoutines uses.
                    chara.m_level = newLevel;
                    kills = 1;
                    CharacterCacheEntry scd = CompositeLazyCache.GetAndSetLocalCache(chara, updateCache: true);
                    // Evolution modifier roll. Runs after the cache rebuild (the new modifier is set up against
                    // the fresh entry) and before the stat re-apply below, which then picks up its changes.
                    if (ValConfig.EvolvingCanRollNewModifiers.Value) {
                        CreatureModifiers.TryRollEvolutionModifier(chara, scd, newLevel);
                    }
                    SpeedModifications.ApplySpeedModifications(chara, scd);
                    DamageModifications.ApplyDamageModification(chara, scd);
                    SizeModifications.SetSizeModification(chara.gameObject, chara.m_nview, scd, true);
                    HealthModifications.ForceApplyHealthModifications(chara, scd);
                    chara.Heal(chara.GetMaxHealth() * 5f);
                    Logger.LogDebug($"Evolve: {chara} level: {level} -> {newLevel}");
                }
                chara.m_nview.GetZDO().Set(SLS_EVOLVE, kills);
            }
        }
    }
}
