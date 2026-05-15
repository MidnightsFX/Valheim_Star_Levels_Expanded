using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Damage;
using UnityEngine;

namespace StarLevelSystem.modules.NemesisSystem {
    internal static class NemesisPatches {

        // Load the NemesisManager data when the player spawns in
        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        public static class AttachNemesisManagerOnSpawn {
            public static void Postfix(Player __instance) {
                if (NemesisSystemData.SLE_Nemesis_Settings == null || ValConfig.EnableNemesisSystem.Value == false) { return; }
                NemesisSystem.NemesisManager.Setup(__instance);
            }
        }

        // Record damage dealt and taken for the local player
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class TrackPlayerDamage {
            public static void Prefix(HitData hit, Character __instance) {
                if (hit == null || __instance == null) { return; }
                if (ValConfig.EnableNemesisSystem.Value == false || NemesisSystemData.SLE_Nemesis_Settings == null || Player.m_localPlayer == null) { return; }

                
                float amt = hit.GetTotalDamageOptions(include_poison: true, include_spirit: true);
                if (amt <= 0f) { return; }

                Character attacker = hit.GetAttacker();
                if (attacker == null || NemesisSystem.NemesisManager == null) { return; }
                if (attacker == Player.m_localPlayer) {
                    switch (hit.m_skill) {
                        case Skills.SkillType.Bows:
                        case Skills.SkillType.Crossbows:
                            NemesisScoreSystem.RecordDamageDealtRanged(amt);
                            break;
                        case Skills.SkillType.ElementalMagic:
                        case Skills.SkillType.BloodMagic:
                            NemesisScoreSystem.RecordDamageDealtMagic(amt);
                            break;
                        default:
                            NemesisScoreSystem.RecordDamageDealtMelee(amt);
                            break;
                    }
                } else if (__instance == Player.m_localPlayer) {
                    NemesisScoreSystem.RecordDamageTaken(amt);
                }
            }
        }


        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class TrackPlayerDeathAndBossKills {
            public static void Prefix(Character __instance) {
                if (__instance == null || ValConfig.EnableNemesisSystem.Value == false || Player.m_localPlayer == null) { return; }
                if (NemesisSystemData.SLE_Nemesis_Settings == null) { return; }

                if (__instance == Player.m_localPlayer) {
                    NemesisScoreSystem.PlayerDeathScoreChange(Player.m_localPlayer);
                    return;
                }

                // Record bosskill if close enough to the player
                if (__instance.IsBoss()) {
                    float distanceToKill = Vector3.Distance(__instance.transform.position, Player.m_localPlayer.transform.position);
                    if (distanceToKill <= NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.BossKillRadius) {
                        NemesisScoreSystem.RecordBossKill(__instance.gameObject.name.Replace("(Clone)", ""));
                    }
                }
            }
        }

    }
}
