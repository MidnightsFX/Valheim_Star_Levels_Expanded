using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static UnityEngine.UI.Image;

namespace StarLevelSystem.modules.NemesisSystem {
    internal static class NemesisActions {

        // Nemesis actions with the level system
        public static int LevelSystemDetermineNemesisInfluence(Character character, int min_level, int max_level, int current_level) {
            int level = current_level;

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
                    float playerScore = NemesisSystem.CachedPlayerScore;

                    // For all of the level changing Nemesis actions
                    foreach (var entry in NemesisSystemData.SLE_Nemesis_Settings.ChanceChanges.CreatureOps) {
                        if (entry.Value.Enabled == false || entry.Value.Action != NemesisAction.ChangeLevel) {
                            continue;
                        }

                        if ((entry.Value.ScoreThreshold >= NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.NeutralScore && NemesisSystem.CachedPlayerScore >= entry.Value.ScoreThreshold) ||
                            (entry.Value.ScoreThreshold < NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.NeutralScore && NemesisSystem.CachedPlayerScore < entry.Value.ScoreThreshold)) {
                            Logger.LogDebug($"{entry.Key} skipped due to player score {NemesisSystem.CachedPlayerScore:0.0} not meeting threshold {entry.Value.ScoreThreshold:0.0}.");
                            continue;
                        }

                        float chance = UnityEngine.Random.Range(0, 1f);
                        if (chance > entry.Value.Chance) {
                            Logger.LogDebug($"{entry.Key} failed chance roll.");
                            continue;
                        }

                        level = Mathf.Clamp(entry.Value.LevelBonus + level, min_level, max_level);
                        NemesisSystem.NemesisManager.RecordNemesisAction($"Changed level by {entry.Value.LevelBonus} due to {entry.Key} spawn with score {NemesisSystem.CachedPlayerScore:0.0}");
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
            if (NemesisSystem.NemesisManager.ReadyForNextNemesisAction() == false) { return; }

            float distance = Vector3.Distance(chara.transform.position, Player.m_localPlayer.transform.position);
            if (distance > NemesisSystemData.SLE_Nemesis_Settings.NemesisInfluenceRadius) { return; }

            // For all of the level changing Nemesis actions
            foreach (var entry in NemesisSystemData.SLE_Nemesis_Settings.ChanceChanges.CreatureOps) {
                if (entry.Value.Enabled == false || entry.Value.Action != NemesisAction.Spawn) {
                    continue;
                }

                if ((entry.Value.ScoreThreshold >= NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.NeutralScore && NemesisSystem.CachedPlayerScore >= entry.Value.ScoreThreshold) || 
                    (entry.Value.ScoreThreshold < NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.NeutralScore && NemesisSystem.CachedPlayerScore < entry.Value.ScoreThreshold)) {
                    Logger.LogDebug($"{entry.Key} skipped due to player score {NemesisSystem.CachedPlayerScore:0.0} not meeting threshold {entry.Value.ScoreThreshold:0.0}.");
                    continue;
                }

                float chance = UnityEngine.Random.Range(0, 1f);
                if (chance > entry.Value.Chance) {
                    Logger.LogDebug($"{entry.Key} failed chance roll.");
                    continue;
                }

                string spawnedDetails = "";
                foreach(NemesisSpawn spawn in entry.Value.SpawnConfig) {
                    var offset = UnityEngine.Random.insideUnitCircle * 0.8f;
                    Vector3 determinedSpawn = chara.transform.position + new Vector3(offset.x, 0, offset.y);
                    int count = 0;
                    while (count < spawn.SpawnGroupSize) {
                        count++;
                        GameObject go = PrefabManager.Instance.GetPrefab(spawn.Prefab);
                        if (go == null) { continue; }
                        GameObject cgo = GameObject.Instantiate(go, determinedSpawn, chara.transform.rotation);
                        Character spawnChara = cgo.GetComponent<Character>();
                        if (spawnChara != null) {
                            if (spawn.Faction != Character.Faction.TrainingDummy) {
                                spawnChara.m_faction = spawn.Faction;
                            }
                        }
                        MonsterAI spawnAI = cgo.GetComponent<MonsterAI>();
                        if (spawnAI != null) {
                            spawnAI.HuntPlayer();
                        }
                        CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(spawnChara, requiredModifiers: spawn.RequiredModifiers);
                        cce.Level += entry.Value.LevelBonus;
                        CreatureSetupControl.CreatureSetupNoDelay(spawnChara);
                    }
                    spawnedDetails += $" {spawn.Prefab}x{spawn.SpawnGroupSize}";
                }

                NemesisSystem.NemesisManager.RecordNemesisAction($"Spawn event {entry.Key}, {spawnedDetails} | score {NemesisSystem.CachedPlayerScore:0.0}");
                if (entry.Value.ScoreChange != 0) {
                    NemesisScoreSystem.UpdateScore(Player.m_localPlayer, entry.Value.ScoreChange);
                }
                break;
            }
        }
    }
}
