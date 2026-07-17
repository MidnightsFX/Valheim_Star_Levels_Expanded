using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    // Server-side interval loop that scouts valid biome locations and places dormant remote Nemesis boss
    // spawners. Mirrors RaidManager: a frequent InvokeRepeating tick gated by a config-driven backoff.
    public class NemesisRemoteSpawnManager : MonoBehaviour {
        private bool setup = false;
        private bool forceRun = false;
        private double nextCheckTime = 0;

        // Slots reserved by scouts currently in flight this cycle, so we don't over-commit past MaxConcurrentTotal
        // before those scouts finish and register their bosses.
        private int pendingScouts = 0;

        public void Awake() {
            InvokeRepeating("CheckRemoteSpawns", 60, 60);
        }

        public void Setup() {
            NemesisRemoteSpawnControl.LoadState();
            NemesisRemoteSpawnControl.ReconcileFromSpawnerZDOs();
            // Give players a couple of minutes after load before the first placement wave.
            if (ZNet.instance != null) {
                nextCheckTime = ZNet.instance.GetTimeSeconds() + 120;
            }
            setup = true;
        }

        public void CheckRemoteSpawns() {
            if (setup == false) { return; }
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            if (ValConfig.EnableNemesisRemoteSpawning.Value == false) { return; }

            RemoteNemesisSpawnSettings settings = NemesisSystemData.SLE_Nemesis_Settings?.RemoteSpawning;
            if (settings == null || settings.Enabled == false) { return; }

            double now = ZNet.instance.GetTimeSeconds();
            float intervalSeconds = Mathf.Max(60f, settings.CheckIntervalMinutes * 60f);
            if (forceRun == false && now < nextCheckTime) { return; }
            nextCheckTime = now + intervalSeconds;
            forceRun = false;

            // No one online to encounter the bosses; try again next interval.
            if (ZNet.instance.GetNrOfPlayers() <= 0) { return; }

            RunSpawnCycle(settings);
        }

        private void RunSpawnCycle(RemoteNemesisSpawnSettings settings) {
            // Re-add any dormant spawners missing from the in-memory registry so caps stay accurate.
            NemesisRemoteSpawnControl.ReconcileFromSpawnerZDOs();

            if (NemesisRemoteSpawnControl.CountActiveTotal() + pendingScouts >= settings.MaxConcurrentTotal) { return; }
            if (settings.TargetPerBiome == null || settings.TargetPerBiome.Count == 0) { return; }

            // Collect biomes that are below their target and cap.
            List<Heightmap.Biome> needy = new List<Heightmap.Biome>();
            foreach (KeyValuePair<Heightmap.Biome, int> kv in settings.TargetPerBiome) {
                int active = NemesisRemoteSpawnControl.CountActivePerBiome(kv.Key);
                int cap = int.MaxValue;
                if (settings.MaxConcurrentPerBiome != null && settings.MaxConcurrentPerBiome.TryGetValue(kv.Key, out int c)) { cap = c; }
                if (active < kv.Value && active < cap) { needy.Add(kv.Key); }
            }
            if (needy.Count == 0) { return; }
            needy = needy.ShuffleList();

            int budget = settings.MaxSpawnsPerInterval;
            foreach (Heightmap.Biome biome in needy) {
                if (budget <= 0) { break; }
                if (NemesisRemoteSpawnControl.CountActiveTotal() + pendingScouts >= settings.MaxConcurrentTotal) { break; }

                NemesisMiniboss boss = SelectOrGenerateBoss(biome);
                if (boss == null) {
                    Logger.LogNemesis($"No pool or generated boss available for biome {biome}, skipping.");
                    continue;
                }
                budget--;
                pendingScouts++;
                StartCoroutine(ScoutAndPlace(biome, boss));
            }
        }

        // Draw from the shared player-generated pool first (and consume it authoritatively), otherwise fabricate one.
        private NemesisMiniboss SelectOrGenerateBoss(Heightmap.Biome biome) {
            NemesisMiniboss pooled = NemesisMiniBossManager.RandomlySelectAppropriateMiniboss(biome);
            if (pooled != null) {
                string yaml = DataObjects.yamlSerializer.Serialize(pooled);
                // Server is authoritative: remove from the shared pool and broadcast to peers (exclude nobody).
                ValConfig.ApplyNemesisBossRemove(yaml, ZNet.GetUID());
                return pooled;
            }
            return NemesisMiniBossManager.GenerateMinibossForBiome(biome);
        }

        private IEnumerator ScoutAndPlace(Heightmap.Biome biome, NemesisMiniboss boss) {
            bool success = false;
            Vector3 point = Vector3.zero;
            yield return NemesisRemoteSpawnControl.ScoutBiomeLocation(biome, (ok, p) => { success = ok; point = p; });
            pendingScouts = Mathf.Max(0, pendingScouts - 1);

            if (success) {
                NemesisRemoteSpawnControl.PlaceSpawner(boss, point, biome);
            } else {
                Logger.LogNemesis($"Remote scout failed for biome {biome}; boss not placed this cycle.");
            }
        }

        // Console-triggered single placement for a biome, bypassing the interval and caps (testing).
        // Uses the same world-wide scout as the interval system, so the boss is placed at a scouted remote
        // location and its map pin guides you there.
        public void ForceSpawnForBiome(Heightmap.Biome biome) {
            NemesisMiniboss boss = SelectOrGenerateBoss(biome);
            if (boss == null) {
                Logger.LogInfo($"[NemesisRemote] Could not select or generate a Nemesis boss for biome {biome}.");
                return;
            }
            Logger.LogInfo($"[NemesisRemote] DebugSpawnForBiome {biome}: selected boss '{boss.BossSpawn?.Prefab}' (level {boss.BossSpawn?.ForcedLevel}); scouting a location...");
            StartCoroutine(ScoutAndPlace(biome, boss));
        }

        public void ForceRun() { forceRun = true; }
    }
}
