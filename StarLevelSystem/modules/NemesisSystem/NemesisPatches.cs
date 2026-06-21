using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Damage;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    internal static class NemesisPatches {

        // Load the NemesisManager data when the player spawns in
        [HarmonyPatch(typeof(Player))]
        public static class NemesisCreateNemesisFromPlayerKillers {
            [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
            [HarmonyPostfix]
            public static void PlayerDiedCreateNemesis(Player __instance) {
                if (NemesisSystemData.SLE_Nemesis_Settings == null || ValConfig.EnableNemesisSystem.Value == false) { return; }
                
                if (NemesisSystemData.SLE_Nemesis_Settings.CreateMinibossFromPlayerKiller == false || __instance.m_lastHit == null) { return; }
                Character killer = __instance.m_lastHit.GetAttacker();
                if (killer == null) { return; }

                // Can't make minibosses out of bosses
                if (killer.IsBoss()) { return; }

                float roll = UnityEngine.Random.Range(0f, 1f);
                Logger.LogNemesis($"Rolling for potential Nemesis boss creation {roll} <= {NemesisSystemData.SLE_Nemesis_Settings.NemesisBossChance}");
                if (roll > NemesisSystemData.SLE_Nemesis_Settings.NemesisBossChance) { return; }

                int level = Mathf.Min(ValConfig.MaxLevel.Value, Mathf.RoundToInt(killer.m_level * UnityEngine.Random.Range(NemesisSystemData.SLE_Nemesis_Settings.NemesisBossMinLevelBonus, NemesisSystemData.SLE_Nemesis_Settings.NemesisBossMaxLevelBonus)));

                NemesisMiniboss mb = new NemesisMiniboss();
                mb.BossSpawn = new NemesisSpawn() {
                    CreatureAI = AI.HuntPlayer,
                    ForcedLevel = level,
                    Faction = Character.Faction.Boss,
                    Prefab = Utils.GetPrefabName(killer.gameObject),
                    IsBoss = true,
                    CustomName = NemesisMiniBossManager.RandomlySelectBossName(Player.m_localPlayer.m_name),
                    // RequiredModifiers // Modifiers will require shaped themes
                    SpawnGroupSize = 1
                };
                mb.KilledPlayerName = Player.m_localPlayer.GetPlayerName();
                mb.BossCreatedFromKillingPlayer = true;
                mb.Biome = Heightmap.FindBiome(killer.transform.position);

                List<NemesisSpawn> minions = new List<NemesisSpawn>();
                int numMinions = UnityEngine.Random.Range(1, 4);
                int availableMinions = NemesisSystemData.SLE_Nemesis_Settings.NemesisMinionTemplatesByBiome[mb.Biome].Count;

                while (numMinions > 0) {
                    NemesisMinion nm = NemesisSystemData.SLE_Nemesis_Settings.NemesisMinionTemplatesByBiome[mb.Biome][UnityEngine.Random.Range(0, availableMinions)];
                    GameObject minionGo = PrefabManager.Instance.GetPrefab(nm.PrefabName);
                    Character mchara = minionGo.GetComponent<Character>();
                    if (mchara != null) {
                        NemesisSpawn nmspawn = new NemesisSpawn() { Faction = Character.Faction.Boss, CustomName = $"$SLS_minion {mchara.m_name}", Prefab = nm.PrefabName, SpawnGroupSize = UnityEngine.Random.Range(nm.MinAmount, nm.MaxAmount) };
                        minions.Add(nmspawn);
                    }
                    numMinions--;
                }
                mb.Minions = minions;

                ZPackage zpack = new ZPackage();
                zpack.Write(DataObjects.yamlSerializer.Serialize(mb));
                ValConfig.SendNewNemesisBossRPC.SendPackage(ZNet.instance.GetServerPeer().m_uid, zpack);

                if (NemesisSystemData.SLE_Nemesis_Settings.CreationRemovesSourceCreature) {
                    killer.m_nview.ClaimOwnership();
                    ZNetScene.instance.Destroy(killer.gameObject);
                }

                Player.MessageAllInRange(__instance.transform.position, 25f, MessageHud.MessageType.Center, "$SLS_Secret_warning");
            }
        }

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
