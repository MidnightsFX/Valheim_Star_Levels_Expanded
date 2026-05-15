using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    internal static class NemesisScoreSystem {

        public static float GetScore(Player player) {
            float neutral = NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.MaxScore * 0.5f;
            if (player == null || player.m_nview == null) { return neutral; }
            ZDO zdo = player.m_nview.GetZDO();
            if (zdo == null) { return neutral; }
            return zdo.GetFloat(SLS_NEMESIS_SCORE, neutral);
        }

        public static void SetScore(Player player, float s) {
            if (player == null || player.m_nview == null) { return; }

            float clamped = Mathf.Clamp(s, NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.MinScore, NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.MaxScore);
            ZDO zdo = player.m_nview.GetZDO();
            if (zdo == null) { return; }
            zdo.Set(SLS_NEMESIS_SCORE, clamped);
            NemesisSystem.CachedPlayerScore = clamped;
        }

        public static float UpdateScore(Player player, float change) {
            float neutral = NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.MaxScore * 0.5f;
            if (player == null || player.m_nview == null) { return neutral; }
            
            ZDO zdo = player.m_nview.GetZDO();
            if (zdo == null) { return NemesisSystem.CachedPlayerScore; }
            
            float current_score = zdo.GetFloat(SLS_NEMESIS_SCORE, neutral);
            float clamped = Mathf.Clamp(current_score + change, NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.MinScore, NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.MaxScore);
            zdo.Set(SLS_NEMESIS_SCORE, clamped);
            NemesisSystem.CachedPlayerScore = clamped;
            return clamped;
        }

        internal static Tuple<float,float, float> DamageTrendCalculation(DamageScoreData dsd_recent, int historyLengthUsed = 3) {
            // Maybe we want to use more than just the most recent data points
            float dealtMeleeDamage = 0;
            float dealtRangedDamage = 0;
            float takenDamage = 0;

            int index = 0;
            while(historyLengthUsed > index && index < NemesisSystem.PlayerScore.DamageScoreHistory.Count) {
                DamageScoreData dsd = NemesisSystem.PlayerScore.DamageScoreHistory[index];
                dealtMeleeDamage += dsd.DamageDealtMelee;
                dealtRangedDamage += dsd.DamageDealtRanged;
                takenDamage += dsd.DamageTaken;

                index++;
            }

            // Average them out to get a per-update trend
            if (dealtMeleeDamage > 0 && historyLengthUsed > 0) { dealtMeleeDamage /= historyLengthUsed; }
            if (dealtRangedDamage > 0 && historyLengthUsed > 0) { dealtRangedDamage /= historyLengthUsed; }
            if (takenDamage > 0 && historyLengthUsed > 0) { takenDamage /= historyLengthUsed; }

            return Tuple.Create(dealtMeleeDamage, dealtRangedDamage, takenDamage);
        }

        public static void UpdateScore(Player player) {
            DataObjects.NemesisScoreSystem cfg = NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem;

            DamageScoreData dsd_recent = new DamageScoreData() {
                DamageDealtMelee = NemesisSystem.PlayerScore.DamageDealtMelee,
                DamageDealtRanged = NemesisSystem.PlayerScore.DamageDealtRanged,
                DamageDealtMagic = NemesisSystem.PlayerScore.DamageDealtMagic,
                DamageTaken = NemesisSystem.PlayerScore.DamageTaken
                
            };

            var (dmgDealtMeleeTrend, dmgDealtRangedTrend, dmgTakenTrend) = DamageTrendCalculation(dsd_recent);
            float score = GetScore(player); // Current score before applying changes

            score += (dmgDealtMeleeTrend * cfg.MeleeDamageDealtFactor) + (dmgDealtRangedTrend * cfg.RangedDamageDealtFactor) + (NemesisSystem.PlayerScore.DamageDealtMagic * cfg.MagicDamageDealtFactor);
            score -= dmgTakenTrend * cfg.DamageTakenFactor;
            score += NemesisSystem.PlayerScore.BossKills * cfg.BossKillBonus;

            // Decay the score towards neutral over time, neutral can be a naturally easy, hard or balanced target.
            if (score > cfg.NeutralScore) { 
                score -= cfg.DecayPerUpdate;
            } else {
                score += cfg.DecayPerUpdate;
            }
            
            // When in range of other players loosely sychronize scores
            List<Player> peers = SLSExtensions.GetPlayersInRange(player.transform.position, cfg.NearbyPlayerRadius).Where(p => p != null && p != player && p.m_nview != null && p.m_nview.GetZDO() != null).ToList();
            if (peers.Count > 0) {
                float avg = peers.Average(p => p.m_nview.GetZDO().GetFloat(SLS_NEMESIS_SCORE, cfg.NeutralScore));
                score = Mathf.Lerp(score, avg, cfg.NearbyAveragingWeight);
            }

            // Set the updated score
            SetScore(player, score);
            NemesisSystem.CachedPlayerScore = score;

            NemesisSystem.PlayerScore.DamageScoreHistory.Insert(0, dsd_recent);
            SaveScoreData(player);

            if (ValConfig.EnableDebugMode.Value) {
                Logger.LogDebug($"Nemesis recalc: score={GetScore(player):0.0} dealtΔ(m/r)={dmgDealtMeleeTrend:0.0}/{dmgDealtRangedTrend:0.0} takenΔ={dmgTakenTrend:0.0} bossΔ={NemesisSystem.PlayerScore.BossKills} peers={peers.Count}");
            }

            // Clear the current score buckets
            NemesisSystem.PlayerScore.DamageDealtMelee = 0;
            NemesisSystem.PlayerScore.DamageDealtRanged = 0;
            NemesisSystem.PlayerScore.DamageDealtMagic = 0;
            NemesisSystem.PlayerScore.DamageTaken = 0;
            NemesisSystem.PlayerScore.BossKills = 0;
        }

        public static void RecordBossKill(string prefabName) {
            if (NemesisSystem.PlayerScore == null || string.IsNullOrEmpty(prefabName)) { return; }
            if (NemesisSystem.PlayerScore.BossKillsHistory.ContainsKey(prefabName) == false) {
                NemesisSystem.PlayerScore.BossKillsHistory.Add(prefabName, 1);
            } else {
                NemesisSystem.PlayerScore.BossKillsHistory[prefabName]++;
            }
            SaveScoreData(Player.m_localPlayer);
        }

        internal static void LoadScoreData(Player player) {
            NemesisSystem.PlayerScore = null;
            if (player.m_customData != null && player.m_customData.TryGetValue(SLS_NEMESIS_SCOREDATA, out string raw) && !string.IsNullOrEmpty(raw)) {
                try {
                    NemesisSystem.PlayerScore = DataObjects.yamldeserializer.Deserialize<ScoreData>(raw);
                } catch (System.Exception ex) {
                    Logger.LogWarning($"Nemesis ScoreData failed to deserialize, resetting: {ex.Message}");
                    NemesisSystem.PlayerScore = null;
                }
            }
            if (NemesisSystem.PlayerScore == null) { NemesisSystem.PlayerScore = new ScoreData(); }
        }

        internal static void SaveScoreData(Player player) {
            if (player == null || player.m_customData == null || NemesisSystem.PlayerScore == null) { return; }
            player.m_customData[SLS_NEMESIS_SCOREDATA] = DataObjects.yamlserializer.Serialize(NemesisSystem.PlayerScore);
        }

        public static void PlayerDeathScoreChange(Player player) {
            if (NemesisSystem.NemesisManager == null) { return; }
            NemesisSystem.PlayerScore.LastDeath = ZNet.instance.GetTimeSeconds();
            SaveScoreData(player);
            SetScore(player, GetScore(player) - NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.DeathScoreReduction);
            if (ZNet.instance != null) {
                NemesisSystem.NemesisManager.nextRecalcTime = ZNet.instance.GetTimeSeconds() + NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.ScoreIntervalSeconds;
            }
        }

        internal static void RecordDamageDealtMelee(float amt) {
            if (NemesisSystem.PlayerScore == null || amt <= 0f) { return; }
            NemesisSystem.PlayerScore.DamageDealtMelee += amt;
        }

        internal static void RecordDamageDealtRanged(float amt) {
            if (NemesisSystem.PlayerScore == null || amt <= 0f) { return; }
            NemesisSystem.PlayerScore.DamageDealtRanged += amt;
        }

        internal static void RecordDamageDealtMagic(float amt) {
            if (NemesisSystem.PlayerScore == null || amt <= 0f) { return; }
            NemesisSystem.PlayerScore.DamageDealtMagic += amt;
        }

        internal static void RecordDamageTaken(float amt) {
            if (NemesisSystem.PlayerScore == null || amt <= 0f) { return; }
            NemesisSystem.PlayerScore.DamageTaken += amt;
        }
    }
}
