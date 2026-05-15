using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static StarLevelSystem.common.ConfigFileWatcher;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {

    internal static class NemesisSystem {
        internal static ScoreData PlayerScore = null;
        internal static NemesisManager NemesisManager = null;
        internal static float CachedPlayerScore = 0;

        internal static void Initialize() {
            if (NemesisManager != null) return;
            GameObject go = new GameObject("SLS_NemesisManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            NemesisManager = go.AddComponent<NemesisManager>();
            Logger.LogInfo("Nemesis System initialized.");
        }
    }

    internal class NemesisManager : MonoBehaviour {
        private Player player;
        private bool setup;
        private List<string> NemesisActionLog = new List<string>();

        public double nextRecalcTime;
        public double nextNemeisActionTime;
        

        public void FixedUpdate() {
            if (ValConfig.EnableNemesisSystem.Value == false) { return; }
            if (setup == false) { return; }

            // Score updater
            if (ZNet.instance.GetTimeSeconds() >= nextRecalcTime) {
                NemesisScoreSystem.UpdateScore(player);
                nextRecalcTime = ZNet.instance.GetTimeSeconds() + NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.ScoreIntervalSeconds;
                NemesisScoreSystem.SaveScoreData(player);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{ZNet.instance.GetTimeSeconds()} | Score: {NemesisScoreSystem.GetScore(player)} | Actions: {NemesisActionLog.Count}");
                foreach (string action in NemesisActionLog) {
                    sb.AppendLine(action);
                }
                NemesisSystemData.UpdateNemesisLog(sb.ToString());
            }
            // Nemesis action
            //if (ReadyForNextNemesisAction()) {
            //    nextNemeisActionTime = ZNet.instance.GetTimeSeconds() + NemesisSystemData.SLE_Nemesis_Settings.NemesisActionCooldownSeconds;
            //}
        }

        public void RecordNemesisAction(string nemSummary) {
            nextNemeisActionTime = ZNet.instance.GetTimeSeconds() + NemesisSystemData.SLE_Nemesis_Settings.NemesisActionCooldownSeconds;
            NemesisActionLog.Add($"  {nemSummary}");
        }

        public bool ReadyForNextNemesisAction() {
            return ZNet.instance.GetTimeSeconds() >= nextNemeisActionTime;
        }

        internal void Setup(Player p) {
            player = p;
            if (player == null) { return; }
            NemesisScoreSystem.LoadScoreData(player);
            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;
            nextRecalcTime = now + NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.ScoreIntervalSeconds;
            NemesisSystem.NemesisManager = this;
            setup = true;
        }
    }
}
