using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    // Server-side interval loop that scouts valid biome locations and places dormant remote Nemesis boss
    // spawners. Mirrors RaidManager: a frequent InvokeRepeating tick gated by a config-driven backoff.
    public class NemesisRemoteSpawnManager : MonoBehaviour {
        private bool setup = false;
        private bool forceRun = false;
        private double nextCheckTime = 0;

        // Scouts still in flight, by biome. Their bosses count against the caps before they are registered, so a
        // scout that outlasts a short check interval cannot let the next cycle put its biome over the cap.
        private readonly Dictionary<Heightmap.Biome, int> pendingScouts = new Dictionary<Heightmap.Biome, int>();

        public void Awake() {
            InvokeRepeating("CheckRemoteSpawns", 60, 60);
        }

        public void Setup() {
            StartCoroutine(SetupOnceWorldIsKnown());
        }

        // Setup runs from RandEventSystem.Awake, before ZNet.Awake, so whether this is the server and which world's saved
        // bosses to load are not known yet. Checking there skipped the load on every world start: the bosses' pins never
        // came back after a logout or restart, and the empty registry let the caps be filled again on top of them.
        private IEnumerator SetupOnceWorldIsKnown() {
            while (ZNet.instance == null || ZNet.instance.GetWorld() == null) { yield return null; }
            // The registry and the ZDO reconcile are server-only state; a pure client never reads either.
            if (ZNet.instance.IsServer()) {
                NemesisRemoteSpawnControl.LoadState();
                NemesisRemoteSpawnControl.ShowActiveBossPinsLocally();
                StartCoroutine(NemesisRemoteSpawnControl.ReconcileFromSpawnerZDOs());
            }
            // Give players a couple of minutes after load before the first placement wave.
            nextCheckTime = ZNet.instance.GetTimeSeconds() + 120;
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
            StartCoroutine(RunSpawnCycleAsync(settings));
        }

        private IEnumerator RunSpawnCycleAsync(RemoteNemesisSpawnSettings settings) {
            // Re-add any dormant spawners missing from the in-memory registry so caps stay accurate.
            // The reconcile pumps the world ZDO table one slice per frame; the rest of the cycle waits
            // on it so the cap counts below are accurate.
            yield return NemesisRemoteSpawnControl.ReconcileFromSpawnerZDOs();

            if (CountWithPending() >= settings.MaxConcurrentTotal) { yield break; }
            if (settings.MaxConcurrentPerBiome == null || settings.MaxConcurrentPerBiome.Count == 0) { yield break; }

            List<Heightmap.Biome> needy = new List<Heightmap.Biome>();
            foreach (KeyValuePair<Heightmap.Biome, int> kv in settings.MaxConcurrentPerBiome) {
                // A name that is not a biome in this world has nowhere to scout. Left in, it would spend this interval's
                // budget and force-load far zones to find that out.
                if (BiomeRegistry.Resolve(kv.Key, out Heightmap.Biome target) == false) { continue; }
                if (CountWithPending(target) < kv.Value) { needy.Add(target); }
            }
            if (needy.Count == 0) { yield break; }
            needy = needy.ShuffleList();

            int budget = settings.MaxSpawnsPerInterval;
            foreach (Heightmap.Biome biome in needy) {
                if (budget <= 0) { break; }
                if (CountWithPending() >= settings.MaxConcurrentTotal) { break; }

                NemesisMiniboss boss = SelectOrGenerateBoss(biome);
                if (boss == null) {
                    Logger.LogNemesis($"No pool or generated boss available for biome {biome}, skipping.");
                    continue;
                }
                budget--;
                pendingScouts[biome] = PendingIn(biome) + 1;
                StartCoroutine(ScoutAndPlace(biome, boss, reserved: true));
            }
        }

        private int PendingIn(Heightmap.Biome biome) {
            return pendingScouts.TryGetValue(biome, out int n) ? n : 0;
        }

        private int CountWithPending() {
            return NemesisRemoteSpawnControl.CountActiveTotal() + pendingScouts.Values.Sum();
        }

        private int CountWithPending(Heightmap.Biome biome) {
            return NemesisRemoteSpawnControl.CountActivePerBiome(biome) + PendingIn(biome);
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

        private IEnumerator ScoutAndPlace(Heightmap.Biome biome, NemesisMiniboss boss, bool reserved) {
            bool success = false;
            Vector3 point = Vector3.zero;
            yield return NemesisRemoteSpawnControl.ScoutBiomeLocation(biome, (ok, p) => { success = ok; point = p; });
            if (reserved) { pendingScouts[biome] = Mathf.Max(0, PendingIn(biome) - 1); }

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
            StartCoroutine(ScoutAndPlace(biome, boss, reserved: false));
        }

        public void ForceRun() { forceRun = true; }
    }
}
