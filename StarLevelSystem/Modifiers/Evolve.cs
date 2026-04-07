using HarmonyLib;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Health;
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
                        chara.m_nview.GetZDO().Set(ZDOVars.s_level, level + 1);
                        kills = 1;
                        CharacterCacheEntry scd = CompositeLazyCache.GetAndSetLocalCache(chara, updateCache: true);
                        SpeedModifications.ApplySpeedModifications(chara, scd);
                        DamageModifications.ApplyDamageModification(chara, scd);
                        SizeModifications.ApplySizeModifications(chara.gameObject, scd, true);
                        HealthModifications.ApplyHealthModifications(chara, scd);
                        chara.Heal(chara.GetMaxHealth() * 5f);
                        Logger.LogDebug($"Evolve: {chara} level: {level} -> {level + 1}");
                    }
                    chara.m_nview.GetZDO().Set(SLS_EVOLVE, kills);
                }
            }
        }
    }
}
