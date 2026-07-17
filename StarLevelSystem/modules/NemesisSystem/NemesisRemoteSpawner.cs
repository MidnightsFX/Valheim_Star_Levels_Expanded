using StarLevelSystem.common;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    // The dormant "sky object" placed remotely by the server. When its zone loads and the owner's area is
    // ready, it materializes the stored miniboss (+ minions) on the ground below and destroys itself,
    // mirroring EpicLoot's AdventureSpawnController.
    internal class NemesisRemoteSpawner : MonoBehaviour {
        private ZNetView znv;

        private const string KeyBoss = "SLS_NRS_BOSS";
        private const string KeyBiome = "SLS_NRS_BIOME";
        private const string KeyPlaced = "SLS_NRS_PLACED";

        // Updates the owner waits after the area is ready before spawning (lets ground/objects settle).
        private const int WarmupTicks = 120;
        // A non-owner that has the area loaded waits longer, giving the authoritative owner (if any) first
        // chance; if the ZDO is ownerless (owner released after the placer moved away) it then claims + spawns.
        private const int NonOwnerClaimTicks = 240;
        private int warmup = 0;
        private int diagTick = 0;

        public void Awake() {
            znv = GetComponent<ZNetView>();
            ZDO zdo = znv != null ? znv.GetZDO() : null;
            bool hasBoss = zdo != null && !string.IsNullOrEmpty(zdo.GetString(KeyBoss, ""));
            Logger.LogInfo($"[NemesisRemote] spawner Awake at {transform.position} znv={(znv != null)} zdoValid={(zdo != null && zdo.IsValid())} owner={(znv != null && znv.IsOwner())} hasBossData={hasBoss}");
        }

        public void OnDestroy() {
            bool placed = znv != null && znv.IsValid() && znv.GetZDO() != null && znv.GetZDO().GetBool(KeyPlaced, false);
            Logger.LogInfo($"[NemesisRemote] spawner OnDestroy at {transform.position} warmup={warmup} placed={placed} (placed=false here means culled while dormant, ZDO persists)");
        }

        // Server-side, immediately after Instantiate: write the boss payload onto the ZDO.
        public void Setup(NemesisMiniboss boss, Heightmap.Biome biome, string pinId, string bossName) {
            if (znv == null) { znv = GetComponent<ZNetView>(); }
            if (znv == null || znv.GetZDO() == null) { return; }
            znv.GetZDO().Set(KeyBoss, DataObjects.yamlSerializer.Serialize(boss));
            znv.GetZDO().Set(KeyBiome, (int)biome);
            znv.GetZDO().Set(SLS_NEMESIS_PIN, pinId ?? "");
            znv.GetZDO().Set(SLS_NAME, bossName ?? "");
            znv.GetZDO().Set(KeyPlaced, false);
        }

        public void Update() {
            if (znv == null || !znv.IsValid()) { Diag("znv null/invalid"); return; }
            // Wait until the zone/objects around this spawner are fully loaded on this machine.
            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(transform.position)) { Diag("area not ready"); return; }

            // Not the owner: give the authoritative owner a grace period, then take over and claim it so the
            // machine that actually loaded this area drives the spawn (covers an ownerless ZDO after recreation).
            if (!znv.IsOwner()) {
                if (warmup < NonOwnerClaimTicks) {
                    if (warmup == 0) { Logger.LogInfo($"[NemesisRemote] spawner loaded but not owner at {transform.position} (owner={znv.GetZDO()?.GetOwner()}); grace period before claiming."); }
                    warmup++;
                    return;
                }
                Logger.LogInfo($"[NemesisRemote] spawner claiming ownership at {transform.position} (owner was {znv.GetZDO()?.GetOwner()}).");
                znv.ClaimOwnership();
                return; // re-enter next frame as owner
            }

            //if (znv.GetZDO().GetBool(KeyPlaced, false)) { DestroySelf(); return; }
            if (warmup < WarmupTicks) {
                if (warmup == 0) { Logger.LogInfo($"[NemesisRemote] spawner ready (owner + area ready) at {transform.position}, warming up {WarmupTicks} ticks."); }
                warmup++;
                return;
            }
            Logger.LogInfo($"[NemesisRemote] spawner warmup complete at {transform.position}, spawning boss now.");
            SpawnAndDestroy();
            return;
        }

        // Throttled diagnostic so we can see which gate blocks the spawner without flooding the log.
        private void Diag(string reason) {
            diagTick++;
            if (diagTick % 180 == 1) { Logger.LogInfo($"[NemesisRemote] spawner waiting at {transform.position}: {reason}"); }
        }

        private void SpawnAndDestroy() {
            string bossYaml = znv.GetZDO().GetString(KeyBoss, "");
            NemesisMiniboss boss = null;
            if (!string.IsNullOrEmpty(bossYaml)) {
                try { boss = DataObjects.yamlDeserializer.Deserialize<NemesisMiniboss>(bossYaml); }
                catch (System.Exception ex) { Logger.LogWarning($"Nemesis remote spawner failed to read its boss data: {ex.Message}"); }
            }
            Heightmap.Biome biome = (Heightmap.Biome)znv.GetZDO().GetInt(KeyBiome, 0);
            string pinId = znv.GetZDO().GetString(SLS_NEMESIS_PIN, "");

            // Drop from the sky placeholder down to the ground beneath it.
            Vector3 point = transform.position;
            if (ZoneSystem.instance != null && ZoneSystem.instance.FindFloor(transform.position, out float floorHeight)) {
                point.y = floorHeight;
            }

            if (boss != null) {
                NemesisRemoteSpawnControl.SpawnMinibossGroup(boss, point, biome, 0, pinId);
                Logger.LogNemesis($"Remote spawner materialized boss '{boss.BossSpawn?.CustomName}' ({biome}) at {point}");
            } else {
                Logger.LogWarning("Nemesis remote spawner had no boss data; destroying without spawning.");
            }

            znv.GetZDO().Set(KeyPlaced, true);
            DestroySelf();
        }

        private void DestroySelf() {
            if (ZNetScene.instance != null) {
                znv.ClaimOwnership();
                ZNetScene.instance.Destroy(gameObject);
            }
        }
    }
}
