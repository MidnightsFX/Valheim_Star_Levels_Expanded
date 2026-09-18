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
        // Stamped by StartRaid on every runner started since the prefab became registered with ZNetScene. A runner
        // ZNetScene rebuilds from a ZDO without it was written by an earlier version, when no machine could
        // reconstruct it; Update deletes those. See RaidControl.LoadAssets.
        internal BoolZNetProperty RunnerRegistered;

        private bool networkReady;
        private double Endtime = 0;

        // ZDO-backed values cached against the ZDO's DataRevision. Every BinaryFormatter-based
        // ZNetProperty.Get() is a full deserialize, and Update used to run several of them on every
        // machine on every frame for the whole raid duration (plus an unconditional Set that
        // re-replicated the spawner list to all peers at frame rate). The revision only changes when
        // something was actually written, so these stay in sync at a fraction of the cost.
        private uint cachedDataRevision = uint.MaxValue;
        private RaidDefinition raidCache;
        // True when the ZDO's raid definition or spawner list threw on deserialize; see RefreshZDataCache.
        private bool raidUnreadable;
        private string raidEnvNameCache;
        private bool raidStartedCache;
        private bool runnerRegisteredCache;
        private double windDownStartCache;
        private double raidStartTimeCache;
        private bool spawnPointsReadyCache;
        private bool spawnPointsGeneratingCache;
        private List<SerializableVector3> spawnPointsCache;
        private List<RaidMonitor> activeSpawnsCache = new List<RaidMonitor>();

        private void RefreshZDataCache() {
            ZDO zdo = Znet.GetZDO();
            if (zdo == null || zdo.DataRevision == cachedDataRevision) { return; }
            cachedDataRevision = zdo.DataRevision;
            // The raid definition and the spawner list are BinaryFormatter payloads of SLS types. A runner saved by
            // a build whose shape of those types differs throws here, and an exception out of Update on every frame
            // left the runner -- with its pins on every client in range -- in place and unendable. Flag it instead;
            // the owner deletes a flagged runner (see Update).
            try {
                raidCache = RunningRaid.Get();
                raidUnreadable = false;
            } catch (Exception e) {
                if (raidUnreadable == false) { Logger.LogWarning($"The raid runner at {transform.position} holds a raid definition this build cannot read; it will be deleted. {e.Message}"); }
                raidCache = null;
                raidUnreadable = true;
            }
            raidEnvNameCache = raidCache != null ? raidCache.ForceEnvironment.ToString() : null;
            raidStartedCache = RaidStarted.Get();
            runnerRegisteredCache = RunnerRegistered.Get();
            windDownStartCache = RaidWindDownStart.Get();
            raidStartTimeCache = RaitStartTime.Get();
            spawnPointsReadyCache = RaidSpawnPointsReady.Get();
            spawnPointsGeneratingCache = RaidSpawnPointsGenerating.Get();
            spawnPointsCache = RaidSpawnPoints.Get();
            try {
                activeSpawnsCache = ActiveRaidSpawns.Get();
            } catch (Exception e) {
                if (raidUnreadable == false) { Logger.LogWarning($"The raid runner at {transform.position} holds spawner state this build cannot read; it will be deleted. {e.Message}"); }
                activeSpawnsCache = new List<RaidMonitor>();
                raidUnreadable = true;
            }
        }

        // Read-only views of the replicated raid state, for the HUD event banner and sls-raid-clear-pins.
        internal RaidDefinition CurrentRaid => raidCache;
        internal bool IsActive => raidStartedCache && raidCache != null && IsWindingDown() == false;
        internal double SecondsSinceStart => ZNet.instance == null ? 0d : ZNet.instance.GetTimeSeconds() - raidStartTimeCache;
        private List<RaidMonitor> RaidSpawners = new List<RaidMonitor>();
        // The environment name this runner last wrote into EnvMan.m_forceEnv, so teardown can release the
        // override without stomping one another system has taken over since. Null when we hold no override.
        private string forcedEnvName;

        // This machine's local map pins for the raid. Every client holding the runner draws its own from the replicated
        // raid state (see Update); nothing about them is networked.
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

            RefreshZDataCache();

            // A runner rebuilt from a ZDO written before the prefab was registered (1.13.0 and earlier) is a
            // leftover: its raid ended long ago, or its owner left before EndRaid could delete it, and until now no
            // machine could load it -- every client near it logged "Missing prefab hash" on every object pass
            // instead. There is nothing worth resuming, so the first machine to load one deletes it along with its
            // ZDO. This runs ahead of the vanilla-raid gate so the leftovers go even where SLS raids are off.
            if (runnerRegisteredCache == false) {
                Logger.LogRaid($"Deleting a raid runner left behind by an earlier version at {transform.position}.");
                EndRaid(destroyCreatures: false);
                return;
            }

            if (ValConfig.UseVanillaRaidConfiguration.Value == true) { return; }
            RaidDefinition raid = raidCache;

            // Force the raid environment only once the raid has actually committed, so an aborted raid (e.g. no
            // valid spawn points) never flips the weather and then snaps it back.
            // While the raid runs, force its environment for all in-range clients. Once it winds down, hand the
            // override back (mirrors OnDestroy) so the weather returns to normal immediately instead of lingering
            // until the runner is finally destroyed at the end of the wind-down window.
            //
            // Active-raid registration lives here, above the owner gate, because AnyActiveRaid() is a per-client
            // fact that MonsterAI consults on whichever client owns a creature's ZDO -- not necessarily the runner's
            // owner. Registering only on the owner meant that as soon as a second player took ownership of a raid
            // creature, InEvent()/HaveActiveEvent() went false on their machine and MonsterAI cleared huntplayer and
            // walked the creature off to despawn. Both calls are idempotent HashSet ops, and raidStartedCache /
            // windDownStartCache are ZDO-backed so they already replicate to non-owners via RefreshZDataCache.
            //
            // Map pins live here for the same reason. They are local Minimap entries, so adding them only on the
            // owner at commit meant no other player ever saw the raid on their map, and neither did a player who
            // inherited ownership or reloaded into a raid that was already running.
            //
            // Gated on a readable definition: a started runner whose raid cannot be read is about to be deleted by
            // its owner, and must not be reported as an active event or hold the weather in the meantime.
            if (raidStartedCache && raid != null) {
                if (IsWindingDown()) {
                    ReleaseForcedEnvironment();
                    RaidControl.UnregisterActiveRaid(this);
                    RemoveExistingMapPins();
                } else {
                    ForceEnvironment(raidEnvNameCache);
                    RaidControl.RegisterActiveRaid(this);
                    AddMapPins(this.transform.position, raid);
                }
            }

            if (Znet.IsOwner() == false) { return; }

            // Network data is required before we start performing actions
            if (networkReady == false) { ConnectZData(); }

            // A runner whose stored raid or spawner state this build cannot read has nothing it can resume. The
            // definition is written in StartRaid in the same frame the runner is created, and every peer receives
            // it with the ZDO, so a started runner with no definition is the same case. Delete it rather than hold
            // it -- and its map pins on every client in range -- open forever.
            if (raidUnreadable || (raidStartedCache && raid == null)) {
                Logger.LogWarning($"Deleting the raid runner at {transform.position}: its raid state could not be read.");
                EndRaid(destroyCreatures: false);
                return;
            }

            // Wait until the raid definition has replicated.
            if (raid == null) { return; }

            // TODO: fallback for if/when the owner who starts generating points exits the game immediately etc
            if (spawnPointsReadyCache == false && spawnPointsGeneratingCache == false) {
                TaskRunner.Run().StartCoroutine(RaidControl.DetermineRemoteSpawnLocations(this.transform.position, RaidSpawnPoints, raid.SpawnPoints, RaidSpawnPointsReady, raid.EventRange));
                RaidSpawnPointsGenerating.Set(true);
                return;
            }

            // Wait until raid positions are identified.
            if (spawnPointsReadyCache == false && spawnPointsGeneratingCache == true) {
                return;
            }

            if (spawnPointsReadyCache) {
                List<SerializableVector3> determinedSpawnPoints = spawnPointsCache;
                if (determinedSpawnPoints == null || determinedSpawnPoints.Count == 0) {
                    Logger.LogRaid($"Raid failed to find any valid spawn points, stopping raid.");
                    EndRaid(destroyCreatures: false);
                    return;
                }
            }

            // Raid is resuming, reconnecting or continuing to run
            if (activeSpawnsCache.Count > 0) {
                if (Endtime == 0) { Endtime = raidStartTimeCache + raid.Duration; }
                if (RaidSpawners.Count != activeSpawnsCache.Count) {
                    RaidSpawners = activeSpawnsCache;
                }

                bool spawnWindowClosed = Endtime < ZNet.instance.GetTimeSeconds();
                bool spawnersDirty = false;

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
                    spawnersDirty = true;
                    // Update/remove null entries in the tracked ZDOIDs
                    List<ZDOID> connectedSpawns = rmonitor.GetSpawnedZDOIDs().Where(x => ZDOMan.instance.GetZDO(x) != null).ToList();
                    Logger.LogRaid($"Found {connectedSpawns.Count} alive creatures");

                    // Strict comparison: <= let a group start while already AT the cap, so the
                    // effective ceiling was MaxSpawned + SpawnGroupSize (and MaxSpawned 0 still
                    // spawned one group).
                    if (connectedSpawns.Count < rmonitor.RaidSpawnDef.MaxSpawned) {
                        List<SerializableVector3> spawnPoints = spawnPointsCache;
                        GameObject creaturePrefab = PrefabManager.Instance.GetPrefab(rmonitor.RaidSpawnDef.PrefabName);
                        if (creaturePrefab == null) {
                            Logger.LogWarning($"The creature defined for this wave is invalid and will be skipped. |{rmonitor.RaidSpawnDef.PrefabName}|");
                            continue;
                        }

                        // Check spawn chance
                        float chance = UnityEngine.Random.Range(0, 100f);
                        if (rmonitor.RaidSpawnDef.SpawnChance < chance) {
                            Logger.LogRaid($"{rmonitor.RaidSpawnDef.PrefabName} Failed spawn chance roll {rmonitor.RaidSpawnDef.SpawnChance} < {chance}");
                            continue;
                        }
                        rmonitor.TriggerCount += 1;
                        Vector3 selectedSpawn = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Count)];
                        // Do custom level if custom level chances are set. Level generators (inline or referenced)
                        // take precedence and overwrite the spawn's configured levelup chances when present.
                        SortedDictionary<int, float> levelupChance = LevelGeneratorResolver.BuildLevelupChance(rmonitor.RaidSpawnDef.LevelupGenerators, rmonitor.RaidSpawnDef.LevelupGeneratorRefs, out float generatorNight);
                        if (levelupChance == null) {
                            levelupChance = LevelSelection.DetermineLevelupChance(null, null, rmonitor.RaidSpawnDef.CustomCreatureLevelUpChance, 1f, false, out generatorNight);
                        }
                        SortedDictionary<int, float> levelupDistanceBonus = LevelSelection.DetermineDistanceBonus(selectedSpawn);

                        int spawns = 0;
                        while(spawns < rmonitor.RaidSpawnDef.SpawnGroupSize) {
                            int level = 0;
                            if (rmonitor.RaidSpawnDef.UseRaidLevelSystem) {
                                level = LevelSelection.DetermineLevelRollResult(UnityEngine.Random.Range(0f, 100f), rmonitor.RaidSpawnDef.LevelMax, levelupChance, levelupDistanceBonus, 1, LevelGeneratorResolver.NightFactor(generatorNight));
                                Logger.LogRaid($"Spawning {rmonitor.RaidSpawnDef.PrefabName} at {selectedSpawn} level {level}");
                            } else {
                                Logger.LogRaid($"Spawning {rmonitor.RaidSpawnDef.PrefabName} at {selectedSpawn}");
                            }
                            GameObject spawnedCreature = GameObject.Instantiate(creaturePrefab, selectedSpawn, UnityEngine.Random.rotation);
                            spawns += 1;

                            // Not every configured prefab is a creature -- army_charred_spawners spawns
                            // Spawner_CharredStone, a destructible obelisk with no MonsterAI and no Character.
                            // Unguarded, that threw mid-Update and left the raid half-started. Mirrors the
                            // log-and-skip shape in NemesisRemoteSpawnControl.
                            MonsterAI mAI = spawnedCreature.GetComponent<MonsterAI>();
                            if (mAI != null) {
                                mAI.SetEventCreature(true);
                                CreatureSetupControl.ApplySpawnAI(mAI, rmonitor.RaidSpawnDef.CreatureAI);
                            }

                            Character chara = spawnedCreature.GetComponent<Character>();
                            if (chara != null) {
                                if (rmonitor.RaidSpawnDef.Faction != Character.Faction.TrainingDummy) {
                                    chara.m_faction = rmonitor.RaidSpawnDef.Faction;
                                }
                                CreatureSetupControl.CreatureSpawnerSetup(chara, level, false, requiredModifiers: rmonitor.RaidSpawnDef.RequiredModifiers, notAllowedModifiers: rmonitor.RaidSpawnDef.ModifiersNotAllowed);
                            }

                            // Still track non-creature spawns so MaxSpawned and the wind-down cleanup cover them.
                            ZNetView spawnedView = spawnedCreature.GetComponent<ZNetView>();
                            if (spawnedView == null || spawnedView.IsValid() == false) {
                                Logger.LogWarning($"Raid spawn '{rmonitor.RaidSpawnDef.PrefabName}' has no valid ZNetView and cannot be tracked by the raid.");
                                continue;
                            }
                            connectedSpawns.Add(spawnedView.GetZDO().m_uid);
                            rmonitor.StoreZDOIDS(connectedSpawns);
                        }
                    }
                }

                // Persist per-spawner state mutations (NextSpawn, TriggerCount, tracked ZDOIDs) so they
                // survive owner-handoff - but only when something actually changed. An unconditional Set
                // here rewrote and re-replicated the whole spawner list to every peer on every owner frame.
                if (spawnersDirty) { ActiveRaidSpawns.Set(RaidSpawners); }

                // Raid is over (or waiting on defeat)
                if (spawnWindowClosed) {
                    if (IsWindingDown()) {
                        UpdateWindDown();
                    } else if (ShouldWindDown(raid)) {
                        BeginWindDown(raid);
                    }
                }

                // If we are maintaining a raid, we skip to prevent multiple starts etc
                return;
            }

            // Spawn is setup, let the raid commence
            double startTime = ZNet.instance.GetTimeSeconds();
            RaitStartTime.Set(startTime);
            Endtime = startTime + raid.Duration;
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

        // Runs however the runner goes away: EndRaid here, another machine ending the raid (ZNetScene.OnZDODestroyed),
        // or ZNetScene simply unloading it because this player moved out of range or logged out. In the last case the
        // raid is still running for everyone else, so this only undoes what this machine did locally. Deleting the
        // raid's creatures used to happen here as well, which force-deleted any raid creature still loaded around a
        // player who walked away. ZNetScene resets the ZDO before destroying on all of those paths, so nothing here may
        // read a ZNetProperty.
        public void OnDestroy() {
            // No longer an active raid; drop registration so SLS stops reporting an active event for its creatures.
            RaidControl.UnregisterActiveRaid(this);
            RemoveExistingMapPins();
            StopRaidMusic();
            ReleaseForcedEnvironment();

            // Every sanctioned teardown -- EndRaid, OnZDODestroyed, range unload, ZNetScene.Shutdown on logout/quit --
            // resets the view first, so a view that still holds its ZDO means something destroyed this component (or
            // raw-destroyed its GameObject) directly, e.g. a Destroy(this). Take the GameObject and ZNetView down with it
            // so the script never goes on its own again. On the owner that also deletes the ZDO; elsewhere ZNetScene
            // just recreates a working runner from the ZDO next frame.
            if (Znet != null && Znet.IsValid() && ZNetScene.instance != null && ZDOMan.instance != null) {
                Logger.LogWarning("RaidRunner was destroyed without its network object; destroying the raid object through ZNetScene. Use EndRaid to end a raid.");
                ZNetScene.instance.Destroy(gameObject);
            }
        }

        // Ends the raid for everyone: optionally force-deletes its tracked creatures, then destroys the runner together
        // with its ZDO so every other machine's copy goes too. This is the only real teardown -- OnDestroy alone never
        // ends a raid. Creatures are cleaned up first because the ZDO-backed spawner list is unreadable once
        // ZNetScene.Destroy has reset the view.
        //
        // These paths used to call ZNetScene.Destroy(this), which binds to UnityEngine.Object.Destroy(Object) and
        // removed only this component: the GameObject and its persistent ZDO stayed behind, so every finished raid
        // left a runner in the world save that came back to life whenever its area was next loaded.
        internal void EndRaid(bool destroyCreatures) {
            if (destroyCreatures) { ForceDestroyTrackedCreatures(); }
            if (ZNetScene.instance == null) {
                Destroy(gameObject);
                return;
            }
            // ZNetScene.Destroy only deletes the ZDO for its owner.
            if (Znet != null && Znet.IsValid()) { Znet.ClaimOwnership(); }
            ZNetScene.instance.Destroy(gameObject);
        }

        // MusicMan.StopMusic stops whatever is playing, so only stop the track this raid forced. A player walking out
        // of range must not lose boss, location or another event's music.
        private void StopRaidMusic() {
            if (MusicMan.instance == null || raidCache == null) { return; }
            string raidMusic = raidCache.ForceMusic.ToString();
            if (MusicMan.instance.m_triggerMusic == raidMusic) { MusicMan.instance.m_triggerMusic = null; }
            if (MusicMan.instance.GetCurrentMusic() == raidMusic) { MusicMan.instance.StopMusic(); }
        }

        // Whether the raid has finished and entered its wind-down phase (creatures dispersing). ZDO-backed so it is
        // consistent across owner-handoff and readable by all in-range clients. Reads the DataRevision-gated
        // cache; BeginWindDown's Set bumps the revision, so the transition is picked up on the next Update.
        private bool IsWindingDown() {
            return windDownStartCache > 0;
        }

        private float nextDefeatCheck = 0f;

        // Whether a raid whose spawn window has closed should begin winding down now.
        //
        // This used to also require that some spawner had been triggered at least MaxSpawned times ("spawned its
        // full set once"). Triggers only fire while the window is open, and are held back by the alive cap and
        // the spawn chance, so any raid whose players killed slowly -- or whose SpawnInterval could never fit
        // MaxSpawned triggers into its Duration at all (foresttrolls: 9 needed, 6 possible) -- could not satisfy
        // it once the window closed, and nothing could satisfy it later. The runner then sat there for good: its
        // creatures kept hunting, its pins, music and forced weather stayed, and as a persistent ZDO it came back
        // after every restart, while each later raid at the same base stacked another set of pins on top.
        //
        // RaidActiveTillDefeated now means what the config header says: after its Duration the raid stays active
        // until its tracked creatures are dead, for at most RaidActiveTillDefeatedMaxSeconds.
        private bool ShouldWindDown(RaidDefinition raid) {
            if (raid.RaidActiveTillDefeated == false) { return true; }
            double graceSeconds = ValConfig.RaidActiveTillDefeatedMaxSeconds.Value;
            if (graceSeconds <= 0d) { return true; }
            // Once a second: the alive check re-parses every tracked ZDOID from its string form.
            if (Time.time < nextDefeatCheck) { return false; }
            nextDefeatCheck = Time.time + 1f;
            if (CountTrackedCreaturesAlive() == 0) {
                Logger.LogRaid($"{raid.Name} creatures have all been defeated.");
                return true;
            }
            if (ZNet.instance.GetTimeSeconds() > Endtime + graceSeconds) {
                Logger.LogRaid($"{raid.Name} has waited {graceSeconds:0}s past its duration for its remaining creatures to be defeated; ending it now.");
                return true;
            }
            return false;
        }

        // Tracked raid creatures whose ZDO this machine still knows. A creature that died or despawned had its ZDO
        // destroyed, so it drops out; one that streamed out of range keeps its ZDO here and still counts.
        private int CountTrackedCreaturesAlive() {
            if (ZDOMan.instance == null) { return 0; }
            int alive = 0;
            foreach (RaidMonitor rmonitor in RaidSpawners) {
                foreach (ZDOID spawned in rmonitor.GetSpawnedZDOIDs()) {
                    if (ZDOMan.instance.GetZDO(spawned) != null) { alive++; }
                }
            }
            return alive;
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
            StopRaidMusic();
            ReleaseForcedEnvironment();
        }

        // Take the vanilla environment override for this raid. Idempotent, so the per-frame Update path is cheap.
        private void ForceEnvironment(string envName) {
            if (EnvMan.instance == null || string.IsNullOrEmpty(envName)) { return; }
            if (EnvMan.instance.m_forceEnv == envName) { forcedEnvName = envName; return; }
            EnvMan.instance.m_forceEnv = envName;
            forcedEnvName = envName;
        }

        // Vanilla's release value for m_forceEnv is "" (EnvMan.m_forceEnv defaults to empty, and EnvMan only
        // consults it when non-empty). Writing "Clear" instead left a permanent hard override in place, which
        // beat biome weather and RandEventSystem.GetEnvOverride — so vanilla raid weather could never apply
        // again after the first SLS raid. Only release if the override is still the one we set; an EnvZone,
        // boss event or another raid may have taken it over in the meantime.
        private void ReleaseForcedEnvironment() {
            if (forcedEnvName == null) { return; }
            if (EnvMan.instance != null && EnvMan.instance.m_forceEnv == forcedEnvName) {
                EnvMan.instance.m_forceEnv = "";
            }
            forcedEnvName = null;
        }

        // Owner-side per-tick wind-down management: prune creatures that have already wandered off and self-despawned,
        // finish early once none remain, and at the end of the configured window either force-delete any stragglers or
        // (when disabled) leave them to despawn on their own.
        private void UpdateWindDown() {
            // Prune despawned creatures so the tracked set shrinks as vanilla MoveAwayAndDespawn removes them.
            // Only write the pruned list back when something was actually removed - this runs every owner
            // frame during wind-down, and each Set re-replicates the whole spawner list to every peer.
            int remaining = 0;
            bool pruned = false;
            foreach (RaidMonitor rmonitor in RaidSpawners) {
                List<ZDOID> tracked = rmonitor.GetSpawnedZDOIDs();
                List<ZDOID> stillAlive = tracked.Where(x => ZDOMan.instance.GetZDO(x) != null).ToList();
                if (stillAlive.Count != tracked.Count) {
                    rmonitor.StoreZDOIDS(stillAlive);
                    pruned = true;
                }
                remaining += stillAlive.Count;
            }
            if (pruned) { ActiveRaidSpawns.Set(RaidSpawners); }

            if (remaining == 0) {
                // Everything wandered off and despawned on its own; nothing left to clean up.
                Logger.LogRaid("Raid wind-down complete, all creatures dispersed.");
                EndRaid(destroyCreatures: false);
                return;
            }

            double windDownDeadline = windDownStartCache + ValConfig.RaidWindDownSeconds.Value;
            if (ZNet.instance.GetTimeSeconds() <= windDownDeadline) { return; }

            if (ValConfig.RaidForceDeleteStragglers.Value) {
                Logger.LogRaid($"Raid wind-down window elapsed; force-deleting {remaining} remaining creature(s).");
            } else {
                Logger.LogRaid($"Raid wind-down window elapsed; leaving {remaining} remaining creature(s) to despawn on their own.");
            }
            EndRaid(destroyCreatures: ValConfig.RaidForceDeleteStragglers.Value);
        }

        // Force-deletes every tracked raid creature. Prefers the ZDO-backed spawner list, which the owner keeps current:
        // RaidSpawners is only filled on a machine that has owned the raid, and on a former owner it misses anything
        // spawned after the hand-off. Must run while the view still holds its ZDO (see EndRaid).
        private void ForceDestroyTrackedCreatures() {
            // Skip if the network is shutting down.
            if (ZDOMan.instance == null || ZNetScene.instance == null) { return; }
            List<RaidMonitor> spawnersToClean = (ActiveRaidSpawns != null && ActiveRaidSpawns.IsHostValid())
                ? ActiveRaidSpawns.Get()
                : RaidSpawners;
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
            RunnerRegistered = new BoolZNetProperty("SLS_RAID_RUNNER_REGISTERED", Znet, false);
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
            // Before the first Update, which deletes any runner that lacks it.
            RunnerRegistered.ForceSet(true);
            RunningRaid.ForceSet(raid);
            RaitStartTime.ForceSet(ZNet.instance.GetTimeSeconds());
            Logger.LogRaid($"Starting Raid {raid.Name}");
        }

        // Every map pin SLS raids have drawn on this client, across all runners. Each runner removes its own in
        // RemoveExistingMapPins; this is what lets sls-raid-clear-pins also sweep up a pin whose runner is gone.
        private static readonly HashSet<Minimap.PinData> TrackedPins = new HashSet<Minimap.PinData>();

        // Idempotent, since Update calls it every frame on every machine holding the runner. A dedicated server
        // holds runners too but has no Minimap.
        public void AddMapPins(Vector3 pos, RaidDefinition raid) {
            if (Minimap.instance == null || raid == null) { return; }
            if (AreaPin != null && IconPin != null) { return; }
            RemoveExistingMapPins();

            // Add the Area pin
            AreaPin = Minimap.instance.AddPin(pos, Minimap.PinType.EventArea, "", false, false, author: new PlatformUserID());
            AreaPin.m_worldSize = raid.EventRange * 2f;
            //AreaPin.m_worldSize *= 0.9f;

            // Add the exclamation
            IconPin = Minimap.instance.AddPin(pos, Minimap.PinType.RandomEvent, "", false, false, author: new PlatformUserID());
            IconPin.m_animate = true;
            IconPin.m_doubleSize = true;

            TrackedPins.Add(AreaPin);
            TrackedPins.Add(IconPin);
        }

        public void RemoveExistingMapPins() {
            if (AreaPin != null) { TrackedPins.Remove(AreaPin); }
            if (IconPin != null) { TrackedPins.Remove(IconPin); }
            // The Minimap can already be gone when a runner is destroyed during world unload.
            if (Minimap.instance == null) {
                AreaPin = null;
                IconPin = null;
                return;
            }
            if (AreaPin != null) {
                Minimap.instance.RemovePin(AreaPin);
                AreaPin = null;
            }
            if (IconPin != null) {
                Minimap.instance.RemovePin(IconPin);
                IconPin = null;
            }
        }

        // Removes every SLS raid pin from this client's map: each live runner's own pair, then anything left in
        // the registry (a pin whose runner went away without removing it). A runner that is still active redraws
        // its pair on its next Update, so only pins of raids that have ended stay gone; activeRaids says how many
        // will come straight back. Backs sls-raid-clear-pins.
        internal static void ClearAllRaidPins(out int removed, out int activeRaids) {
            removed = 0;
            activeRaids = 0;
            Minimap map = Minimap.instance;
            // Registry entries can outlive the Minimap that drew them (a previous world this session); only count
            // pins the current map is actually showing.
            if (map != null) {
                foreach (Minimap.PinData pin in TrackedPins) {
                    if (pin != null && map.m_pins.Contains(pin)) { removed++; }
                }
            }
            // Scene objects only, not Resources.FindObjectsOfTypeAll, which would include the prefab asset.
            foreach (RaidRunner runner in UnityEngine.Object.FindObjectsByType<RaidRunner>(FindObjectsSortMode.None)) {
                if (runner == null) { continue; }
                if (runner.IsActive) { activeRaids++; }
                runner.RemoveExistingMapPins();
            }
            if (map != null) {
                foreach (Minimap.PinData pin in TrackedPins) {
                    if (pin != null) { map.RemovePin(pin); }
                }
            }
            TrackedPins.Clear();
        }
    }
}
