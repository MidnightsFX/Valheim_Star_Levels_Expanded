using Jotunn;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.LevelSystem;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    internal static class NemesisActions {

        private static readonly List<NemesisAction> SpawnActions = new List<NemesisAction>() { NemesisAction.Spawn, NemesisAction.SpawnMiniboss };

        // Nemesis actions with the level system
        public static int LevelSystemDetermineNemesisInfluence(Character character, int min_level, int max_level, int current_level) {
            int level = current_level;

            // Don't evaluate score-based actions until the manager is set up and the player score cache is seeded.
            if (NemesisSystem.NemesisManager == null || NemesisSystem.NemesisManager.IsSetup == false || NemesisSystem.PlayerScore == null) {
                return level;
            }

            bool isBoss = character.IsBoss();
            string characterName = character.gameObject.name.Replace("(Clone)", "");
            if (isBoss) {
                // Set first boss encountered to a specific level if that is enabled
                if (NemesisSystem.PlayerScore.BossKillsHistory.ContainsKey(characterName) == false && NemesisSystemData.SLE_Nemesis_Settings.GaurenteedChanges.FirstBossSetLevel == true) {
                    level = NemesisSystemData.SLE_Nemesis_Settings.GaurenteedChanges.FirstBossLevel;
                    return level;
                }

            } else {

                // If not a boss, check player score and evaluate the random change based changes
                if (NemesisSystem.NemesisManager != null && NemesisSystem.NemesisManager.ReadyForNextNemesisAction() && NemesisSystemData.SLE_Nemesis_Settings.ChanceChanges != null && NemesisSystemData.SLE_Nemesis_Settings.ChanceChanges != null) {

                    // For all of the level changing Nemesis actions
                    foreach (KeyValuePair<string, NemesisChanceEntry> entry in NemesisSystemData.SLE_Nemesis_Settings.ChanceChanges.CreatureOps) {
                        if (entry.Value.Enabled == false || entry.Value.Action != NemesisAction.ChangeLevel) {
                            continue;
                        }

                        if (MeetsScoreThreshold(entry.Value.ScoreThreshold) == false) {
                            Logger.LogNemesis($"{entry.Key} skipped due to player score {NemesisSystem.CachedPlayerScore:0.0} not meeting threshold {entry.Value.ScoreThreshold:0.0}.");
                            continue;
                        }

                        float chance = UnityEngine.Random.Range(0, 1f);
                        if (chance > entry.Value.Chance) {
                            Logger.LogNemesis($"{entry.Key} failed chance roll.");
                            continue;
                        }

                        level = Mathf.Clamp(entry.Value.LevelBonus + level, min_level, max_level);
                        NemesisSystem.NemesisManager.RecordNemesisAction($"Changed {character.m_name} level by {entry.Value.LevelBonus}({level}) due to {entry.Key} spawn with score {NemesisSystem.CachedPlayerScore}");
                        if (entry.Value.ScoreChange != 0) {
                            NemesisScoreSystem.UpdateScore(Player.m_localPlayer, entry.Value.ScoreChange);
                        }
                        break;
                    }
                }
            }
            return level;
        }


        public static void NemesisRandomSpawner(Character chara) {
            if (NemesisSystem.NemesisManager == null || NemesisSystem.NemesisManager.IsSetup == false) { return; }
            if (NemesisSystem.NemesisManager.ReadyForNextNemesisAction() == false) { return; }

            float distance = Vector3.Distance(chara.transform.position, Player.m_localPlayer.transform.position);
            if (distance > NemesisSystemData.SLE_Nemesis_Settings.NemesisInfluenceRadius) { return; }
            if (distance < NemesisSystemData.SLE_Nemesis_Settings.NemesisMinSpawnDistance) { return; }

            List<string> playerPrivateKeys = Player.m_localPlayer.GetPrivateKeysSanitize();
            Heightmap.Biome targetBiome = Heightmap.FindBiome(chara.transform.position);
            Heightmap.Biome playerBiome = Heightmap.FindBiome(Player.m_localPlayer.transform.position);
            // For all of the level changing Nemesis actions
            foreach (KeyValuePair<string, NemesisChanceEntry> entry in NemesisSystemData.SLE_Nemesis_Settings.ChanceChanges.CreatureOps) {
                if (entry.Value.Enabled == false || SpawnActions.Contains(entry.Value.Action) == false) {
                    continue;
                }

                if (entry.Value.RequiredGlobalKeys != null && entry.Value.RequiredGlobalKeys.Count > 0 && entry.Value.RequiredGlobalKeys.Any(key => ZoneSystem.instance.GetGlobalKey(key) == false)) {
                    bool hasAllRequired = true;
                    foreach(string key in entry.Value.RequiredGlobalKeys) {
                        if (ZoneSystem.instance.GetGlobalKey(key) == false) {
                            hasAllRequired = false;
                            Logger.LogNemesis($"{entry.Key} skipped due to server missing a required global key ({key}) in {string.Join(", ", entry.Value.RequiredGlobalKeys)}");
                            break;
                        }
                    }
                    if (hasAllRequired == false) {
                        continue;
                    }
                }

                if (entry.Value.NotRequiredGlobalKeys != null && entry.Value.NotRequiredGlobalKeys.Count > 0 && entry.Value.NotRequiredGlobalKeys.Any(key => ZoneSystem.instance.GetGlobalKey(key) == true)) {
                    bool hasNoAvoidedKeys = true;
                    foreach(string key in entry.Value.NotRequiredGlobalKeys) {
                        if (ZoneSystem.instance.GetGlobalKey(key) == true) {
                            hasNoAvoidedKeys = false;
                            Logger.LogNemesis($"{entry.Key} skipped due to server having an unwanted global key ({key}) in {string.Join(", ", entry.Value.NotRequiredGlobalKeys)}");
                            break;
                        }
                    }
                    if (hasNoAvoidedKeys == false) {
                        continue;
                    }
                }

                if (entry.Value.RequiredPrivateKeys != null && entry.Value.RequiredPrivateKeys.Count > 0 && entry.Value.RequiredPrivateKeys.Any(key => playerPrivateKeys.Contains(key) == false)) {

                    bool hasAllRequired = true;
                    foreach (string key in entry.Value.RequiredPrivateKeys) {
                        if (playerPrivateKeys.Contains(key) == false) {
                            hasAllRequired = false;
                            Logger.LogNemesis($"{entry.Key} skipped due to server missing a required private key ({key}) in {string.Join(", ", entry.Value.RequiredPrivateKeys)}");
                            break;
                        }
                    }
                    if (hasAllRequired == false) {
                        continue;
                    }
                }

                if (entry.Value.PlayerReqs != null) {
                    if (entry.Value.PlayerReqs.PlayerCurrentBiome != Heightmap.Biome.None && entry.Value.PlayerReqs.PlayerCurrentBiome != playerBiome) {
                        Logger.LogNemesis($"{entry.Key} skipped due to player not being in the current required biome.");
                        continue;
                    }
                    float playerHealthP = Player.m_localPlayer.GetHealthPercentage();
                    if (entry.Value.PlayerReqs.PlayerHealthPercentAbove != 0f && playerHealthP <= entry.Value.PlayerReqs.PlayerHealthPercentAbove) {
                        Logger.LogNemesis($"{entry.Key} skipped due to player health percentage not above the requirement ({playerHealthP}) req: {entry.Value.PlayerReqs.PlayerHealthPercentAbove}.");
                        continue;
                    }
                    if (entry.Value.PlayerReqs.PlayerHealthPercentBelow != 0f && playerHealthP >= entry.Value.PlayerReqs.PlayerHealthPercentBelow) {
                        Logger.LogNemesis($"{entry.Key} skipped due to player health percentage not below the requirement ({playerHealthP}) req: {entry.Value.PlayerReqs.PlayerHealthPercentBelow}.");
                        continue;
                    }
                    if (entry.Value.PlayerReqs.MinBiomeHistory > 0 && NemesisScoreSystem.PlayerRecentBiomeCount(targetBiome) < entry.Value.PlayerReqs.MinBiomeHistory) {
                        Logger.LogNemesis($"{entry.Key} skipped due to not meeting the minimum biome history requirement ({NemesisScoreSystem.PlayerRecentBiomeCount(targetBiome)} < {entry.Value.PlayerReqs.MinBiomeHistory}).");
                        continue;
                    }
                }

                if (entry.Value.ScoreThreshold != 0 && MeetsScoreThreshold(entry.Value.ScoreThreshold) == false) {
                    Logger.LogNemesis($"{entry.Key} skipped due to player score {NemesisSystem.CachedPlayerScore:0.0} not meeting threshold {entry.Value.ScoreThreshold:0.0}.");
                    continue;
                }


                if (entry.Value.AllowedBiomes != null && entry.Value.AllowedBiomes.Count > 0 && entry.Value.AllowedBiomes.Contains(targetBiome) == false) {
                    Logger.LogNemesis($"{entry.Key} skipped due to not being an allowed biome Allowed: {entry.Value.AllowedBiomes} Current: {targetBiome}.");
                    continue;
                }

                if (entry.Value.DeniedBiomes != null && entry.Value.DeniedBiomes.Count > 0 && entry.Value.DeniedBiomes.Contains(targetBiome)) {
                    Logger.LogNemesis($"{entry.Key} skipped due to being in a denied biome: {string.Join(", ", entry.Value.DeniedBiomes)} Current: {targetBiome}.");
                    continue;
                }

                float chance = UnityEngine.Random.Range(0, 1f);
                if (chance > entry.Value.Chance) {
                    Logger.LogNemesis($"{entry.Key} failed chance roll.");
                    continue;
                }

                string spawnedDetails = "";
                List<NemesisSpawn> NemesisCreatureSpawns = entry.Value.SpawnConfig;
                // Minibosses do not have a spawn config by default
                if (entry.Value.Action == NemesisAction.SpawnMiniboss) {
                    // Selects a biome appropriate miniboss.
                    NemesisMiniboss nemBoss =  NemesisMiniBossManager.RandomlySelectAppropriateMiniboss(chara.transform.position);

                    if (nemBoss != null) {
                        spawnedDetails += $"Selected NemesisBoss: {nemBoss.BossSpawn.CustomName} to spawn. ";
                        NemesisCreatureSpawns = (List<NemesisSpawn>)(new List<NemesisSpawn>(nemBoss.Minions) ?? Enumerable.Empty<NemesisSpawn>());
                        NemesisCreatureSpawns.Add(nemBoss.BossSpawn);

                        // Propagate the removal so this boss isn't drawn again. On a host we ARE the
                        // authority (GetServerPeer() is null on a server), so broadcast to peers directly;
                        // a remote client tells the server, which rebroadcasts.
                        ZPackage removeBossPack = new ZPackage();
                        removeBossPack.Write(DataObjects.yamlSerializer.Serialize(nemBoss));
                        if (ZNet.instance != null && ZNet.instance.IsServer()) {
                            ZNet.instance.GetPeers().ForEach(peer => ValConfig.RemoveNemesisBossRPC.SendPackage(peer.m_uid, removeBossPack));
                        } else {
                            ZNetPeer server = ZNet.instance?.GetServerPeer();
                            if (server != null) {
                                ValConfig.RemoveNemesisBossRPC.SendPackage(server.m_uid, removeBossPack);
                            }
                        }
                        // Locally remove (reliable reference removal on the machine that selected it)
                        NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses.Remove(nemBoss);
                    } else {
                        continue;
                    }
                }

                foreach(NemesisSpawn spawn in NemesisCreatureSpawns) {
                    if (spawn.IsBoss) { spawnedDetails += "Miniboss Spawn "; }
                    // Reactive spawns keep their existing behavior (no biome loot, no map pin): pass null for both.
                    NemesisRemoteSpawnControl.SpawnNemesisSpawn(spawn, chara.transform.position, chara.transform.rotation, entry.Value.LevelBonus, null, null);
                    spawnedDetails += $" {spawn.Prefab}x{spawn.SpawnGroupSize}";
                }

                NemesisSystem.NemesisManager.RecordNemesisAction($"Spawn event {entry.Key}, {spawnedDetails} | score {NemesisSystem.CachedPlayerScore:0.0}");
                if (entry.Value.ExtraCooldownSeconds > 0) {
                    NemesisSystem.NemesisManager.AddCooldownForNextNemesisAction(entry.Value.ExtraCooldownSeconds);
                }
                if (entry.Value.ScoreChange != 0) {
                    NemesisScoreSystem.UpdateScore(Player.m_localPlayer, entry.Value.ScoreChange);
                }
                break;
            }
        }

        internal static bool MeetsScoreThreshold(float scoreThreshold) {
            if (scoreThreshold > NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.NeutralScore && NemesisSystem.CachedPlayerScore < scoreThreshold) {
                return false;
            }

            if (scoreThreshold < NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.NeutralScore && NemesisSystem.CachedPlayerScore > scoreThreshold) {
                return false;
            }
            return true;
        }
    }
}
