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

        private bool networkReady;
        private double Endtime = 0;
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
            if (RunningRaid == null || Znet.IsValid() == false) { return; }

            // Clients all get this to set their music
            RaidDefinition raid = RunningRaid.Get();
            EnvMan.instance.m_forceEnv = raid.ForceEnvironment.ToString();

            if (Znet.IsOwner() == false) { return; }

            // Network data is required before we start performing actions
            if (networkReady == false) { ConnectZData(); }

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

            // Raid is resuming, reconnecting or continuing to run
            if (ActiveRaidSpawns.Get().Count > 0) {
                if (Endtime == 0) { Endtime = RaitStartTime.Get() + RunningRaid.Get().Duration; }
                if (RaidSpawners.Count != ActiveRaidSpawns.Get().Count) {
                    RaidSpawners = ActiveRaidSpawns.Get();
                }

                // Spawn creatures
                foreach (var rmonitor in RaidSpawners) {
                    if (rmonitor.NextSpawn > ZNet.instance.GetTimeSeconds()) {
                        continue;
                    }

                    Logger.LogDebug($"Checking {rmonitor.RaidSpawnDef.PrefabName} spawn timer: {rmonitor.NextSpawn} < {ZNet.instance.GetTimeSeconds()}");
                    rmonitor.NextSpawn = ZNet.instance.GetTimeSeconds() + rmonitor.RaidSpawnDef.SpawnInterval;
                    // Update/remove null entries in the tracked ZDOIDs
                    List<ZDOID> connectedSpawns = rmonitor.GetSpawnedZDOIDs().Where(x => ZDOMan.instance.GetZDO(x) != null).ToList();
                    Logger.LogDebug($"Found {connectedSpawns.Count} alive creatures");

                    if (connectedSpawns.Count <= rmonitor.RaidSpawnDef.MaxSpawned) {
                        List<Vector3> spawnPoints = RaidSpawnPoints.Get();
                        GameObject creaturePrefab = PrefabManager.Instance.GetPrefab(rmonitor.RaidSpawnDef.PrefabName);
                        if (creaturePrefab == null) {
                            Logger.LogWarning($"The creature defined for this wave is invalid and will be skipped. |{rmonitor.RaidSpawnDef.PrefabName}|");
                        }

                        // Check spawn chance
                        float chance = UnityEngine.Random.Range(0, 100f);
                        if (rmonitor.RaidSpawnDef.SpawnChance < chance) {
                            Logger.LogDebug($"{rmonitor.RaidSpawnDef.PrefabName} Failed spawn chance roll {rmonitor.RaidSpawnDef.SpawnChance} < {chance}");
                            continue;
                        }
                        Vector3 selectedSpawn = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Count - 1)];
                        // Do custom level if custom level chances are set
                        SortedDictionary<int, float> levelupChance = LevelSelection.DetermineLevelupChance(customLevelup: rmonitor.RaidSpawnDef.CustomCreatureLevelUpChance);
                        SortedDictionary<int, float> levelupDistanceBonus = LevelSelection.DetermineDistanceBonus(selectedSpawn);

                        int spawns = 0;
                        while(spawns < rmonitor.RaidSpawnDef.SpawnGroupSize) {
                            int level = 0;
                            if (rmonitor.RaidSpawnDef.UseRaidLevelSystem) {
                                level = LevelSelection.DetermineLevelRollResult(UnityEngine.Random.Range(0f, 100f), rmonitor.RaidSpawnDef.LevelMax, levelupChance, levelupDistanceBonus, 1);
                                Logger.LogDebug($"Spawning {rmonitor.RaidSpawnDef.PrefabName} at {selectedSpawn} level {level}");
                            } else {
                                Logger.LogDebug($"Spawning {rmonitor.RaidSpawnDef.PrefabName} at {selectedSpawn}");
                            }
                            GameObject spawnedCreature = GameObject.Instantiate(creaturePrefab, selectedSpawn, UnityEngine.Random.rotation);
                            spawns += 1;
                            MonsterAI mAI = spawnedCreature.GetComponent<MonsterAI>();
                            switch (rmonitor.RaidSpawnDef.CreatureAI) {
                                case AI.HuntPlayer:
                                    mAI.SetHuntPlayer(true);
                                    break;
                                case AI.Alerted:
                                    mAI.SetAlerted(true);
                                    break;
                                case AI.AgitatedByBuild:
                                    mAI.SetAggravated(true, BaseAI.AggravatedReason.Building);
                                    break;
                                default:
                                    mAI.SetAlerted(true);
                                    break;
                            }

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

                // Raid is over
                if (Endtime < ZNet.instance.GetTimeSeconds()) {
                    RemoveExistingMapPins();
                    Player.MessageAllInRange(this.transform.position, RunningRaid.Get().EventRange * 1.5f, MessageHud.MessageType.Center, RunningRaid.Get().EndMessage);
                    ZNetScene.Destroy(this);
                }

                // If we are maintaining a raid, we skip to prevent multiple starts etc
                return;
            }

            // Spawn is setup, let the raid commence
            RaitStartTime.Set(ZNet.instance.GetTimeSeconds());
            Endtime = RaitStartTime.Get() + raid.Duration;
            AddMapPins(this.transform.position, raid);
            Player.MessageAllInRange(this.transform.position, raid.EventRange * 1.5f, MessageHud.MessageType.Center, raid.StartMessage);

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
            // Remove existing pins
            RemoveExistingMapPins();

            // Stop the music
            MusicMan.instance.StopMusic();

            // Clear the environment
            EnvMan.instance.m_forceEnv = DataObjects.Environment.Clear.ToString();

            // Clean up any spawns
            if (RaidSpawners == null) { return; }
            foreach (var raidmon in RaidSpawners) {
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
            networkReady = true;
        }

        public void StartRaid(DataObjects.RaidDefinition raid, Player player) {
            Znet.ClaimOwnership();
            RunningRaid.ForceSet(raid);
            RaitStartTime.ForceSet(ZNet.instance.GetTimeSeconds());
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
