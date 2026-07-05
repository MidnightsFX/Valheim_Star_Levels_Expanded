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

                // Player.OnDeath is broadcast to every machine (m_nview.InvokeRPC(ZNetView.Everybody)),
                // so this postfix also runs on the dedicated server and on remote clients that merely
                // witnessed the death (the vanilla method logs "OnDeath call but not the owner" there).
                // Only the machine whose local player actually died should create a nemesis — the boss
                // is named after Player.m_localPlayer. On a dedicated server m_localPlayer is null (NRE);
                // on a remote client this is someone else's death (misattributed + duplicated). Bail on both.
                if (Player.m_localPlayer == null || __instance != Player.m_localPlayer) { return; }

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
                // Only a subset of biomes has minion templates seeded. Dying in an unseeded biome
                // (DeepNorth/Ocean/None) must not throw KeyNotFoundException, and an empty list would
                // throw on Random.Range(0,0) indexing. In those cases the boss spawns with no minions.
                if (NemesisSystemData.SLE_Nemesis_Settings.NemesisMinionTemplatesByBiome.TryGetValue(mb.Biome, out List<NemesisMinion> biomeMinions) && biomeMinions.Count > 0) {
                    int numMinions = UnityEngine.Random.Range(1, 4);
                    while (numMinions > 0) {
                        numMinions--;
                        NemesisMinion nm = biomeMinions[UnityEngine.Random.Range(0, biomeMinions.Count)];
                        GameObject minionGo = PrefabManager.Instance.GetPrefab(nm.PrefabName);
                        if (minionGo == null) { continue; }
                        Character mchara = minionGo.GetComponent<Character>();
                        if (mchara != null) {
                            NemesisSpawn nmspawn = new NemesisSpawn() { Faction = Character.Faction.Boss, CustomName = $"$SLS_minion {mchara.m_name}", Prefab = nm.PrefabName, SpawnGroupSize = UnityEngine.Random.Range(nm.MinAmount, nm.MaxAmount) };
                            minions.Add(nmspawn);
                        }
                    }
                }
                mb.Minions = minions;

                string mbYaml = DataObjects.yamlSerializer.Serialize(mb);
                if (ZNet.instance != null && ZNet.instance.IsServer()) {
                    // Host/listen-server: we are the authority, so apply and broadcast directly.
                    // GetServerPeer() is null on a server, so there is no peer to RPC to.
                    ValConfig.ApplyNemesisBossAdd(mbYaml, ZNet.GetUID());
                } else {
                    ZNetPeer server = ZNet.instance?.GetServerPeer();
                    if (server != null) {
                        ZPackage zpack = new ZPackage();
                        zpack.Write(mbYaml);
                        ValConfig.SendNewNemesisBossRPC.SendPackage(server.m_uid, zpack);
                    }
                }

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
