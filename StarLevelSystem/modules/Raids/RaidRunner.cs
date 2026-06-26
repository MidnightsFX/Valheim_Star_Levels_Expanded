using Jotunn.Managers;
using Splatform;
using StarLevelSystem.common;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Raids {
    public class RaidRunner : MonoBehaviour {
        internal ZNetView Znet;

        internal RaidZNetProperty RunningRaid;
        internal DoubleZNetProperty RaitStartTime;
        internal ListVectorZNetProperty RaidSpawnPoints;
        internal BoolZNetProperty RaidSpawnPointsReady;
        internal BoolZNetProperty RaidSpawnPointsGenerating;
        internal RaidMonitorListZNetProperty ActiveRaidSpawns;
        // True only once the raid has actually committed (spawn points validated + start message sent). Gates the
        // forced environment so a raid that aborts during spawn-point search never changes the weather.
        internal BoolZNetProperty RaidStarted;
        // Time (ZNet seconds) at which the raid finished and began winding down; 0 while the raid is still running.
        // ZDO-backed so wind-down survives owner-handoff and so all in-range clients can stop forcing the environment.
        internal DoubleZNetProperty RaidWindDownStart;

        private bool networkReady;
        private double Endtime = 0;
        // Set when a wind-down completes with stragglers intentionally left to despawn on their own, so OnDestroy
        // skips the force-delete cleanup. Hard teardowns (admin reset, shutdown) leave this false and still clean up.
        private bool skipCreatureCleanup = false;
        private List<RaidMonitor> RaidSpawners = new List<RaidMonitor>();

        // Should map pins be persisted between clients? probably
        private Minimap.PinData AreaPin;
        private Minimap.PinData IconPin;

        public void Awake() {
            Znet = this.GetComponent<ZNetView>();

            if ((bool)Znet) {
                ConnectZData();
            }
        }


        public void Update() {
            if (ValConfig.UseVanillaRaidConfiguration.Value == true || RunningRaid == null || Znet.IsValid() == false) { return; }

            // Force the raid environment only once the raid has actually committed, so an aborted raid (e.g. no
            // valid spawn points) never flips the weather and then snaps it back.
            RaidDefinition raid = RunningRaid.Get();
            // While the raid runs, force its environment for all in-range clients. Once it winds down, revert to Clear
            // (mirrors OnDestroy) so the weather returns to normal immediately instead of lingering until the runner
            // is finally destroyed at the end of the wind-down window.
            if (RaidStarted.Get()) {
                EnvMan.instance.m_forceEnv = IsWindingDown()
                    ? DataObjects.Environment.Clear.ToString()
                    : raid.ForceEnvironment.ToString();
            }

            if (Znet.IsOwner() == false) { return; }

            // Network data is required before we start performing actions
            if (networkReady == false) { ConnectZData(); }

            // Re-assert active-raid registration each owner tick while the raid is running (idempotent). Covers the
            // owner-handoff/reconnect case where a new owner picks up an already-committed raid.
            if (RaidStarted.Get() && IsWindingDown() == false) { RaidControl.RegisterActiveRaid(this); }

            // TODO: fallback for if/when the owner who starts generating points exits the game immediately etc
            if (RaidSpawnPointsReady.Get() == false && RaidSpawnPointsGenerating.Get() == false) {
                TaskRunner.Run().StartCoroutine(RaidControl.DetermineRemoteSpawnLocations(this.transform.position, RaidSpawnPoints, RunningRaid.Get().SpawnPoints, RaidSpawnPointsReady, RunningRaid.Get().EventRange));
                RaidSpawnPointsGenerating.Set(true);
                return;
            }

            // Wait until raid positions are identified.
            if (RaidSpawnPointsReady.Get() == false && RaidSpawnPointsGenerating.Get() == true) {
                return;
            }

            if (RaidSpawnPointsReady.Get()) {
                List<SerializableVector3> determinedSpawnPoints = RaidSpawnPoints.Get();
                if (determinedSpawnPoints == null || determinedSpawnPoints.Count == 0) {
                    Logger.LogRaid($"Raid failed to find any valid spawn points, stopping raid.");
                    RemoveExistingMapPins();
                    ZNetScene.Destroy(this);
                    return;
                }
            }

            // Raid is resuming, reconnecting or continuing to run
            if (ActiveRaidSpawns.Get().Count > 0) {
                if (Endtime == 0) { Endtime = RaitStartTime.Get() + RunningRaid.Get().Duration; }
                if (RaidSpawners.Count != ActiveRaidSpawns.Get().Count) {
                    RaidSpawners = ActiveRaidSpawns.Get();
                }

                bool spawnWindowClosed = Endtime < ZNet.instance.GetTimeSeconds();

                // Spawn creatures
                foreach (RaidMonitor rmonitor in RaidSpawners) {
                    if (spawnWindowClosed) { continue; }
                    if (rmonitor.RaidSpawnDef.MaxSpawnTriggers > 0
                        && rmonitor.TriggerCount >= rmonitor.RaidSpawnDef.MaxSpawnTriggers) {
                        continue;
                    }
                    if (rmonitor.NextSpawn > ZNet.instance.GetTimeSeconds()) {
                        continue;
                    }

                    Logger.LogRaid($"Checking {rmonitor.RaidSpawnDef.PrefabName} spawn timer: {rmonitor.NextSpawn} < {ZNet.instance.GetTimeSeconds()}");
                    rmonitor.NextSpawn = ZNet.instance.GetTimeSeconds() + rmonitor.RaidSpawnDef.SpawnInterval;
                    // Update/remove null entries in the tracked ZDOIDs
                    List<ZDOID> connectedSpawns = rmonitor.GetSpawnedZDOIDs().Where(x => ZDOMan.instance.GetZDO(x) != null).ToList();
                    Logger.LogRaid($"Found {connectedSpawns.Count} alive creatures");

                    if (connectedSpawns.Count <= rmonitor.RaidSpawnDef.MaxSpawned) {
                        List<SerializableVector3> spawnPoints = RaidSpawnPoints.Get();
                        GameObject creaturePrefab = PrefabManager.Instance.GetPrefab(rmonitor.RaidSpawnDef.PrefabName);
                        if (creaturePrefab == null) {
                            Logger.LogWarning($"The creature defined for this wave is invalid and will be skipped. |{rmonitor.RaidSpawnDef.PrefabName}|");
                        }

                        // Check spawn chance
                        float chance = UnityEngine.Random.Range(0, 100f);
                        if (rmonitor.RaidSpawnDef.SpawnChance < chance) {
                            Logger.LogRaid($"{rmonitor.RaidSpawnDef.PrefabName} Failed spawn chance roll {rmonitor.RaidSpawnDef.SpawnChance} < {chance}");
                            continue;
                        }
                        rmonitor.TriggerCount += 1;
                        Vector3 selectedSpawn = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Count - 1)];
                        // Do custom level if custom level chances are set. Level generators (inline or referenced)
                        // take precedence and overwrite the spawn's configured levelup chances when present.
                        SortedDictionary<int, float> levelupChance = LevelGeneratorResolver.BuildLevelupChance(rmonitor.RaidSpawnDef.LevelupGenerators, rmonitor.RaidSpawnDef.LevelupGeneratorRefs)
                            ?? LevelSelection.DetermineLevelupChance(customLevelup: rmonitor.RaidSpawnDef.CustomCreatureLevelUpChance);
                        SortedDictionary<int, float> levelupDistanceBonus = LevelSelection.DetermineDistanceBonus(selectedSpawn);

                        int spawns = 0;
                        while(spawns < rmonitor.RaidSpawnDef.SpawnGroupSize) {
                            int level = 0;
                            if (rmonitor.RaidSpawnDef.UseRaidLevelSystem) {
                                level = LevelSelection.DetermineLevelRollResult(UnityEngine.Random.Range(0f, 100f), rmonitor.RaidSpawnDef.LevelMax, levelupChance, levelupDistanceBonus, 1);
                                Logger.LogRaid($"Spawning {rmonitor.RaidSpawnDef.PrefabName} at {selectedSpawn} level {level}");
                            } else {
                                Logger.LogRaid($"Spawning {rmonitor.RaidSpawnDef.PrefabName} at {selectedSpawn}");
                            }
                            GameObject spawnedCreature = GameObject.Instantiate(creaturePrefab, selectedSpawn, UnityEngine.Random.rotation);
                            spawns += 1;
                            MonsterAI mAI = spawnedCreature.GetComponent<MonsterAI>();
                            mAI.SetEventCreature(true);
                            CreatureSetupControl.ApplySpawnAI(mAI, rmonitor.RaidSpawnDef.CreatureAI);

                            Character chara = spawnedCreature.GetComponent<Character>();
                            if (rmonitor.RaidSpawnDef.Faction != Character.Faction.TrainingDummy) {
                                chara.m_faction = rmonitor.RaidSpawnDef.Faction;
                            }

                            CreatureSetupControl.CreatureSpawnerSetup(chara, level, false, requiredModifiers: rmonitor.RaidSpawnDef.RequiredModifiers, notAllowedModifiers: rmonitor.RaidSpawnDef.ModifiersNotAllowed);

                            connectedSpawns.Add(spawnedCreature.GetComponent<ZNetView>().GetZDO().m_uid);
                            rmonitor.StoreZDOIDS(connectedSpawns);
                        }
                    }
                }

                // Persist any per-spawner state mutations (NextSpawn, TriggerCount) so they survive owner-handoff
                ActiveRaidSpawns.Set(RaidSpawners);

                // Raid is over (or waiting on defeat)
                if (spawnWindowClosed) {
                    bool raidComplete = !RunningRaid.Get().RaidActiveTillDefeated;
                    bool spawnedMaxOnce = false;
                    foreach (RaidMonitor raidspawn in RaidSpawners) {
                        if (raidspawn.RaidSpawnDef.MaxSpawnTriggers > 0 && raidspawn.TriggerCount >= raidspawn.RaidSpawnDef.MaxSpawnTriggers) {
                            raidComplete = true;
                        }
                        if (raidspawn.RaidSpawnDef.MaxSpawnTriggers == 0) { raidComplete = true; }
                        if (raidspawn.RaidSpawnDef.MaxSpawned > 0 && raidspawn.TriggerCount >= raidspawn.RaidSpawnDef.MaxSpawned) {
                            spawnedMaxOnce = true;
                        }
                        if (raidspawn.RaidSpawnDef.MaxSpawned == 0) { spawnedMaxOnce = true; }
                    }
                    
                    if (raidComplete && spawnedMaxOnce) {
                        if (IsWindingDown() == false) {
                            BeginWindDown(raid);
                        } else {
                            UpdateWindDown();
                        }
                    }
                }

                // If we are maintaining a raid, we skip to prevent multiple starts etc
                return;
            }

            // Spawn is setup, let the raid commence
            RaitStartTime.Set(ZNet.instance.GetTimeSeconds());
            Endtime = RaitStartTime.Get() + raid.Duration;
            AddMapPins(this.transform.position, raid);
            Player.MessageAllInRange(this.transform.position, raid.EventRange * 1.5f, MessageHud.MessageType.Center, raid.StartMessage);

            // The raid is now committed. Flip the flag (gates the forced environment above), start our own music,
            // and tell the server to set the cooldown + broadcast music to nearby clients. Everything above this
            // point is side-effect-free, so a raid that aborted before here left no visible trace.
            RaidStarted.Set(true);
            RaidControl.RegisterActiveRaid(this);
            if (MusicMan.instance != null) { MusicMan.instance.TriggerMusic(raid.ForceMusic.ToString()); }
            SendRaidCommitConfirmation(raid, this.transform.position);

            // Start all of the spawners
            RaidSpawners.Clear();
            foreach (var spawner in raid.Spawns) {
                RaidSpawners.Add(new RaidMonitor() { RaidSpawnDef = spawner, NextSpawn = ZNet.instance.GetTimeSeconds() + spawner.InitalSpawnDelay });
            }
            ActiveRaidSpawns.Set(RaidSpawners);

            foreach(Player player in SLSExtensions.GetPlayersInRange(this.transform.position, raid.EventRange * 1.5f)) {
                player.ShowTutorial("randomevent", false);
            }
        }

        public void OnDestroy() {
            // No longer an active raid; drop registration so SLS stops reporting an active event for its creatures.
            RaidControl.UnregisterActiveRaid(this);

            // Remove existing pins
            RemoveExistingMapPins();

            // Stop the music
            if (MusicMan.instance != null) {
                MusicMan.instance.StopMusic();
            }


            // Clear the environment
            if (EnvMan.instance != null) {
                EnvMan.instance.m_forceEnv = DataObjects.Environment.Clear.ToString();
            }

            // A wind-down that intentionally left its stragglers to despawn on their own asks us to skip the
            // force-delete. Hard teardowns (admin reset, shutdown) leave skipCreatureCleanup false and still clean up.
            if (skipCreatureCleanup == false) {
                ForceDestroyTrackedCreatures();
            }
        }

        // Whether the raid has finished and entered its wind-down phase (creatures dispersing). ZDO-backed so it is
        // consistent across owner-handoff and readable by all in-range clients.
        private bool IsWindingDown() {
            return RaidWindDownStart != null && RaidWindDownStart.Get() > 0;
        }

        // Called once when the raid completes. Performs the player-facing teardown (message, pins, music, weather) and
        // unregisters the raid so vanilla MonsterAI starts wandering the event creatures off and despawning them.
        // The runner stays alive afterwards to manage pruning and the force-delete backstop (see UpdateWindDown).
        private void BeginWindDown(RaidDefinition raid) {
            Logger.LogRaid($"{raid.Name} ending — creatures dispersing.");
            RaidWindDownStart.Set(ZNet.instance.GetTimeSeconds());
            RaidControl.UnregisterActiveRaid(this);

            RemoveExistingMapPins();
            Player.MessageAllInRange(this.transform.position, raid.EventRange * 1.5f, MessageHud.MessageType.Center, raid.EndMessage);
            if (MusicMan.instance != null) { MusicMan.instance.StopMusic(); }
            if (EnvMan.instance != null) { EnvMan.instance.m_forceEnv = DataObjects.Environment.Clear.ToString(); }
        }

        // Owner-side per-tick wind-down management: prune creatures that have already wandered off and self-despawned,
        // finish early once none remain, and at the end of the configured window either force-delete any stragglers or
        // (when disabled) leave them to despawn on their own.
        private void UpdateWindDown() {
            // Prune despawned creatures so the tracked set shrinks as vanilla MoveAwayAndDespawn removes them.
            int remaining = 0;
            foreach (RaidMonitor rmonitor in RaidSpawners) {
                List<ZDOID> stillAlive = rmonitor.GetSpawnedZDOIDs().Where(x => ZDOMan.instance.GetZDO(x) != null).ToList();
                rmonitor.StoreZDOIDS(stillAlive);
                remaining += stillAlive.Count;
            }
            ActiveRaidSpawns.Set(RaidSpawners);

            if (remaining == 0) {
                // Everything wandered off and despawned on its own; nothing left to clean up.
                Logger.LogRaid("Raid wind-down complete, all creatures dispersed.");
                skipCreatureCleanup = true;
                ZNetScene.Destroy(this);
                return;
            }

            double windDownDeadline = RaidWindDownStart.Get() + ValConfig.RaidWindDownSeconds.Value;
            if (ZNet.instance.GetTimeSeconds() <= windDownDeadline) { return; }

            if (ValConfig.RaidForceDeleteStragglers.Value) {
                Logger.LogRaid($"Raid wind-down window elapsed; force-deleting {remaining} remaining creature(s).");
                ForceDestroyTrackedCreatures();
            } else {
                Logger.LogRaid($"Raid wind-down window elapsed; leaving {remaining} remaining creature(s) to despawn on their own.");
                skipCreatureCleanup = true;
            }
            ZNetScene.Destroy(this);
        }

        // Force-deletes every tracked raid creature. Falls back to the ZDO-backed spawner list when the in-memory
        // cache is empty (e.g. console-command teardown before Update populated RaidSpawners, or after owner-handoff).
        private void ForceDestroyTrackedCreatures() {
            // Skip if the network is shutting down.
            if (ZDOMan.instance == null || ZNetScene.instance == null) { return; }
            List<RaidMonitor> spawnersToClean = (RaidSpawners != null && RaidSpawners.Count > 0)
                ? RaidSpawners
                : (networkReady && ActiveRaidSpawns != null ? ActiveRaidSpawns.Get() : null);
            if (spawnersToClean == null) { return; }
            foreach (var raidmon in spawnersToClean) {
                foreach (ZDOID spawned in raidmon.GetSpawnedZDOIDs() ) {
                    ZDO zdo = ZDOMan.instance.GetZDO(spawned);
                    if (zdo == null) { continue; }
                    ZNetView nv = ZNetScene.instance.FindInstance(zdo);
                    if (nv == null) { continue; }
                    if (nv != null) {
                        nv.ClaimOwnership();
                        ZNetScene.instance.Destroy(nv.gameObject);
                    }
                }
            }
        }

        private void ConnectZData() {
            RunningRaid = new RaidZNetProperty("SLS_RAID", Znet, null);
            RaitStartTime = new DoubleZNetProperty("SLS_RAID_START", Znet, 0);
            RaidSpawnPoints = new ListVectorZNetProperty("SLS_RAID_SPAWN_POINTS", Znet, null);
            RaidSpawnPointsReady = new BoolZNetProperty("SLS_RAID_SPAWN_READY", Znet, false);
            RaidSpawnPointsGenerating = new BoolZNetProperty("SLS_RAID_SPAWN_GEN", Znet, false);
            ActiveRaidSpawns = new RaidMonitorListZNetProperty("SLS_RAID_SPAWNS_ACTIVE", Znet, new List<RaidMonitor>());
            RaidStarted = new BoolZNetProperty("SLS_RAID_STARTED", Znet, false);
            RaidWindDownStart = new DoubleZNetProperty("SLS_RAID_WINDDOWN", Znet, 0);
            networkReady = true;
        }

        // The owner is the player being raided. If that's the integrated host, finalize the commit directly;
        // otherwise tell the server (over RaidCommittedRPC) so it sets the cooldown and broadcasts music.
        private void SendRaidCommitConfirmation(RaidDefinition raid, Vector3 pos) {
            if (ZNet.instance == null) { return; }
            if (ZNet.instance.IsServer()) {
                RaidControl.FinalizeRaidCommit(SLSExtensions.GetLocalUserPlatformAndID(), raid.Name, pos);
                return;
            }
            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null) {
                Logger.LogWarning($"Raid '{raid.Name}' committed but no server peer was available to confirm it; cooldown/music may not be applied.");
                return;
            }
            ZPackage pkg = new ZPackage();
            pkg.Write(raid.Name);
            pkg.Write(pos.x);
            pkg.Write(pos.y);
            pkg.Write(pos.z);
            ValConfig.RaidCommittedRPC.SendPackage(serverPeer.m_uid, pkg);
        }

        public void StartRaid(DataObjects.RaidDefinition raid, Player player) {
            Znet.ClaimOwnership();
            RunningRaid.ForceSet(raid);
            RaitStartTime.ForceSet(ZNet.instance.GetTimeSeconds());
            Logger.LogRaid($"Starting Raid {raid.Name}");
        }

        public void AddMapPins(Vector3 pos, RaidDefinition raid) {
            RemoveExistingMapPins();

            // Add the Area pin
            AreaPin = Minimap.instance.AddPin(pos, Minimap.PinType.EventArea, "", false, false, author: new PlatformUserID());
            AreaPin.m_worldSize = raid.EventRange * 2f;
            //AreaPin.m_worldSize *= 0.9f;

            // Add the exclamation
            IconPin = Minimap.instance.AddPin(pos, Minimap.PinType.RandomEvent, "", false, false, author: new PlatformUserID());
            IconPin.m_animate = true;
            IconPin.m_doubleSize = true;
        }

        public void RemoveExistingMapPins() {
            if (AreaPin != null) {
                Minimap.instance.RemovePin(AreaPin);
                AreaPin = null;
            }
            if (IconPin != null) {
                Minimap.instance.RemovePin(IconPin);
                IconPin = null;
            }
        }
    }
}
