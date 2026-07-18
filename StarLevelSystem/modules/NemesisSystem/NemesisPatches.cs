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

                // Only a subset of biomes has minion templates seeded. Dying in an unseeded biome
                // (DeepNorth/Ocean/None) yields no minions rather than throwing.
                mb.Minions = NemesisMiniBossManager.GenerateMinionsForBiome(mb.Biome);

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

        // When a remotely-spawned boss dies, remove its shared map pin. Runs on the creature's owner (which
        // may be a dedicated server or a client), reporting to the server so the registry + pins converge.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class RemoveNemesisBossPinOnDeath {
            public static void Prefix(Character __instance) {
                if (__instance == null || __instance.m_nview == null || __instance.m_nview.IsValid() == false) { return; }
                if (__instance.m_nview.IsOwner() == false) { return; }   // report exactly once, from the owner
                if (__instance.m_nview.GetZDO() == null) { return; }
                string pinId = __instance.m_nview.GetZDO().GetString(SLS_NEMESIS_PIN, "");
                if (string.IsNullOrEmpty(pinId)) { return; }

                if (ZNet.instance != null && ZNet.instance.IsServer()) {
                    NemesisRemoteSpawnControl.RemoveActiveBoss(pinId);
                    return;
                }
                ZNetPeer server = ZNet.instance?.GetServerPeer();
                if (server != null) {
                    ZPackage pkg = new ZPackage();
                    pkg.Write(pinId);
                    ValConfig.ReportNemesisBossDeathRPC.SendPackage(server.m_uid, pkg);
                }
            }
        }

        // Guarantee the dormant remote-boss placeholder prefab is registered into ZNetScene on every machine
        // (clients AND the dedicated server), so its persistent ZDO can be reconstructed instead of being
        // destroyed as an "invalid prefab". Jotunn's own ZNetScene registration relies on
        // OnVanillaPrefabsAvailable (a FejdStartup/main-menu event) having run first, which isn't guaranteed
        // on a dedicated server. Idempotent; safe to run alongside Jotunn's registration.
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        public static class RegisterNemesisRemoteSpawnerPrefab {
            public static void Postfix(ZNetScene __instance) {
                NemesisRemoteSpawnControl.EnsureRegisteredToZNetScene();
            }
        }

        // Attach the server-side remote spawn manager to the RandEventSystem, mirroring the Raid manager.
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.Awake))]
        public static class AttachNemesisRemoteSpawnManager {
            public static void Postfix(RandEventSystem __instance) {
                if (__instance.GetComponent<NemesisRemoteSpawnManager>() != null) { return; }
                NemesisRemoteSpawnControl.Manager = __instance.gameObject.AddComponent<NemesisRemoteSpawnManager>();
                NemesisRemoteSpawnControl.Manager.Setup();
            }
        }

    }
}
