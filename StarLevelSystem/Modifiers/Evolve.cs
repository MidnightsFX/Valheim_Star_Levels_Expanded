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

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class SoulEaterAndEvolveOnDeath {
            private static void Prefix(Character __instance) {
                if (__instance == null || __instance.IsPlayer() || __instance.m_lastHit == null) {
                    return;
                }
                Character chara = __instance.m_lastHit.GetAttacker();
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
}
