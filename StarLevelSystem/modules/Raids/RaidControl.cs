using HarmonyLib;
using Jotunn.Managers;
using PlayFab.ClientModels;
using Splatform;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Heightmap;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Raids
{
    internal static class RaidControl
    {
        internal static Dictionary<string, PlayerRaidData> ServerPlayerRaidData = new Dictionary<string, PlayerRaidData>();

        internal static RaidManager RaidMan;

        internal static GameObject RaidRunnerGO;

        internal static void LoadAssets() {
            RaidRunnerGO = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>("RaidRunner.prefab");
        }

        internal static void StartRaidRunner(RaidDefinition targetRaid, Vector3 pos) {
            GameObject raidGo = GameObject.Instantiate(RaidRunnerGO, pos, Quaternion.identity);
            RaidRunner raidRun = raidGo.GetComponent<RaidRunner>();
            raidRun.StartRaid(targetRaid, Player.m_localPlayer);
        }

        public static RaidDefinition RandomSelectValidRaidForPlayer(string playerPlatformID) {
            if (RaidsData.SLE_Raid_Settings.Raids.Count == 0) {
                Logger.LogWarning("No Raids were defined.");
                return new RaidDefinition() { };
            }

            Logger.LogDebug($"Checking for raids for {playerPlatformID}");

            if (ServerPlayerRaidData.ContainsKey(playerPlatformID) == false) {
                Logger.LogWarning($"Player {playerPlatformID} was not found and an appropriate raid can't be determined, a random one will be selected. \n  Currently tracked: {string.Join(",", ServerPlayerRaidData.Keys.ToList())}");
                return RaidsData.SLE_Raid_Settings.Raids.ElementAt(UnityEngine.Random.Range(0, RaidsData.SLE_Raid_Settings.Raids.Count - 1)).Value;
            }

            return ServerPlayerRaidData[playerPlatformID].PlayerAvailableRaids.ElementAt(UnityEngine.Random.Range(0, ServerPlayerRaidData[playerPlatformID].PlayerAvailableRaids.Count - 1));
        }

        internal static void UpdateOrAddPlayerPrivateKeys(long playerID, List<string> privatekeys) {
            string playerPlatformID = SLSExtensions.GetPlatformUserID(playerID).ToString();
            if (ServerPlayerRaidData.ContainsKey(playerPlatformID)) {
                ServerPlayerRaidData[playerPlatformID].PlayerPrivatekeys = privatekeys;
            } else {
                ServerPlayerRaidData.Add(playerPlatformID, new DataObjects.PlayerRaidData() { PlayerPrivatekeys = privatekeys });
            }
            RaidsData.SaveServerRaidData(DataObjects.yamlserializer.Serialize(RaidControl.ServerPlayerRaidData));
        }

        internal static void UpdatePlayerRaidHistory(PlayerRaidData playerRaidData, RaidDefinition raidDef) {
            // Update history of this raid happening
            if (playerRaidData.LastRaidByName.ContainsKey(raidDef.Name)) {
                playerRaidData.LastRaidByName[raidDef.Name] = ZNet.instance.GetTimeSeconds();
            } else {
                playerRaidData.LastRaidByName.Add(raidDef.Name, ZNet.instance.GetTimeSeconds());
            }
            // Update cooldown
            playerRaidData.NextRaidableTime = (raidDef.RaidCoolDownMinutes * 60) + ZNet.instance.GetTimeSeconds() * RaidsData.SLE_Raid_Settings.GlobalSettings.GlobalRaidIntervalScalar;
        }

        internal static void ApplyRaidConfiguration(RandEventSystem res) {
            if (res == null) { return; }
            if (ValConfig.UseVanillaRaidConfiguration != null && ValConfig.UseVanillaRaidConfiguration.Value) {
                Logger.LogDebug("UseVanillaRaidConfiguration is true; leaving vanilla raid list untouched.");
                return;
            }

            RaidConfiguration cfg = RaidsData.SLE_Raid_Settings ?? RaidsData.DefaultConfiguration;

            if (cfg.GlobalSettings != null) {
                if (cfg.GlobalSettings.GlobalRaidIntervalScalar > 0f) {
                    res.m_eventIntervalMin *= cfg.GlobalSettings.GlobalRaidIntervalScalar;
                }
                if (cfg.GlobalSettings.DisableAllRaids) {
                    res.m_events.Clear();
                    Logger.LogInfo("SLS raid system: DisableAllRaids set, cleared all random events.");
                    return;
                }
            }

            Logger.LogInfo($"SLS raid system: applied {cfg.Raids.Count} raid definitions.");
        }

        internal static void UpdateAvailableRaidsPerPlayer() {
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) {
                if (peer.IsReady() == false) { continue; }
                string playerPlatformID = SLSExtensions.GetPlatformUserID(peer.m_uid).ToString();
                List<RaidDefinition> playerAvailableRaids = GetValidRaidsForPlayer(peer, playerPlatformID);
                if (ServerPlayerRaidData.ContainsKey(playerPlatformID)) {
                    ServerPlayerRaidData[playerPlatformID].PlayerAvailableRaids = playerAvailableRaids;
                } else {
                    ServerPlayerRaidData.Add(playerPlatformID, new DataObjects.PlayerRaidData() { PlayerAvailableRaids = playerAvailableRaids });
                }
            }
        }

        internal static List<RaidDefinition> GetValidRaidsForPlayer(ZNetPeer peer, string playerPlatformID) {
            List<RaidDefinition> playerAvailableRaids = new List<RaidDefinition>();
            Vector3 position = peer.GetRefPos();
            bool inBase = EffectArea.IsPointInsideArea(position, EffectArea.Type.PlayerBase, 30f);
            Heightmap heightmap = Heightmap.FindHeightmap(position);
            Heightmap.Biome biome = heightmap.GetBiome(position);
            

            foreach(KeyValuePair<string, RaidDefinition> raid in RaidsData.SLE_Raid_Settings.Raids) {
                if (raid.Value.Activation == null || raid.Value.Enabled == false) { continue; }

                // Biome Check
                if (raid.Value.Activation.Biomes != null && raid.Value.Activation.Biomes.Contains(biome) == false) {
                    Logger.LogDebug($"Player is not in a target biome, skipping selection of Raid: {raid.Key}");
                    continue;
                }
                // BaseCheck
                if (raid.Value.Activation.NearBaseOnly && inBase == false ) {
                    Logger.LogDebug($"Player is not in base, skipping selection of Raid: {raid.Key}");
                    continue;
                }
                // Required Global Key Check
                if (raid.Value.Activation.RequiredGlobalKeys != null) {
                    bool hasRequiredGlobalKeys = true;
                    List<string> currentGlobalKeys = ZoneSystem.instance.GetGlobalKeys();
                    foreach (string gkey in raid.Value.Activation.RequiredGlobalKeys) {
                        if (currentGlobalKeys.Contains(gkey) == false) {
                            hasRequiredGlobalKeys = false;
                            break;
                        }
                    }
                    if (hasRequiredGlobalKeys == false) {
                        Logger.LogDebug($"Server does not have a required global key, skipping Raid: {raid.Key}");
                        continue;
                    }
                }

                PlayerRaidData playerData = new PlayerRaidData();
                if (ServerPlayerRaidData.ContainsKey(playerPlatformID)) {
                    playerData = ServerPlayerRaidData[playerPlatformID];
                }

                List<string> playerPrivateKeys = playerData.PlayerPrivatekeys;


                // Player Private keys will require an RPC requeast from the client for the data, since it is not stored server side.
                // Required private key check
                if (raid.Value.Activation.RequiredPlayerKeys != null && playerPrivateKeys.Count > 0) {
                    bool hasRequiredPlayerKeys = true;
                    foreach (string pkey in raid.Value.Activation.RequiredPlayerKeys) {
                        if (playerPrivateKeys.Contains(pkey) == false) {
                            hasRequiredPlayerKeys = false;
                            break;
                        }
                    }
                    if (hasRequiredPlayerKeys == false) {
                        Logger.LogDebug($"Player {peer.m_playerName} does not have a required private key, skipping Raid: {raid.Key}");
                        continue;
                    }
                }

                // Check for partial match player keys
                if (raid.Value.Activation.AnyRequiredPlayerKeys != null) {
                    bool hasAnyRequiredPlayerKeys = false;
                    foreach (string pkey in raid.Value.Activation.RequiredPlayerKeys) {
                        if (playerPrivateKeys.Contains(pkey)) {
                            hasAnyRequiredPlayerKeys = true;
                            break;
                        }
                    }
                    if (hasAnyRequiredPlayerKeys == false) {
                        Logger.LogDebug($"Player {peer.m_playerName} does not have any of the required private keys, skipping Raid: {raid.Key}");
                        continue;
                    }
                }

                // Check if the raid has been activated too recently
                if (playerData.LastRaidByName.Count > 0) {
                    if (playerData.NextRaidableTime > ZNet.instance.GetTimeSeconds()) {
                        Logger.LogDebug($"Player {peer.m_playerName} has a NextRaidableTime of {playerData.NextRaidableTime} which is in the future, skipping Raid: {raid.Key}");
                        continue;
                    }
                    if (playerData.LastRaidByName != null && playerData.LastRaidByName.ContainsKey(raid.Key) && (playerData.LastRaidByName[raid.Key] + raid.Value.RaidCoolDownMinutes) > ZNet.instance.GetTimeSeconds()) {
                        Logger.LogDebug($"Player {peer.m_playerName} has activated Raid {raid.Key} too recently, skipping. Next possible activation time: {playerData.LastRaidByName[raid.Key] + raid.Value.RaidCoolDownMinutes}");
                        continue;
                    }
                }

                Logger.LogDebug($"Raid {raid.Key} valid for player {peer.m_playerName}");
                playerAvailableRaids.Add(raid.Value);
            }


            return playerAvailableRaids;
        }

        public static IEnumerator DetermineRemoteSpawnLocations(Vector3 origin, ListVectorZNetProperty resultset,  int numTargets, BoolZNetProperty pointsReady, float maxDistance = 300f, Heightmap.Biome targetBiome = Heightmap.Biome.None) {
            List<Vector3> spawn_locations = new List<Vector3>();
            //Logger.LogDebug($"Starting spawn destination in incrments of {range_increment} from x{origin.x} y{origin.y} z{origin.z}");
            int spawn_location_attempts = 0;
            Vector3 determinedSpawn = origin;

            while (spawn_locations.Count < numTargets || spawn_location_attempts > 200) {
                var offset = UnityEngine.Random.insideUnitCircle * (maxDistance * 0.8f);
                determinedSpawn = origin + new Vector3(offset.x, 0, offset.y);

                // Sleep to avoid locking the thread
                if (spawn_location_attempts > 1 && spawn_location_attempts % 10 == 0) { yield return new WaitForSeconds(0.5f); }

                ZoneSystem.instance.GetGroundData(ref determinedSpawn, out var normal, out var foundBiome, out var biomeArea, out var hmap);

                // Prevent spawns that are in the wrong biome if we are targeting a biome
                if (targetBiome != Heightmap.Biome.None) {
                    if (hmap == null || foundBiome != targetBiome) {
                        spawn_location_attempts += 1;
                        Logger.LogDebug($"Spawn location in the wrong biome, skipping. {foundBiome} | {determinedSpawn}");
                        continue;
                    }
                }

                // Prevent spawns that are inside of objects
                float terrainHeight = determinedSpawn.y;
                float solidHeight = 1000f; // This stars high in the sky for the raycast down, gets modified next
                if (ZoneSystem.instance.FindFloor(new Vector3(determinedSpawn.x, determinedSpawn.y + 100f, determinedSpawn.z), out solidHeight)) {
                    float terrainDiff = solidHeight - terrainHeight;

                    // Prevent spawns in objects and too high off the ground
                    if (terrainDiff > 1f) {
                        Logger.LogDebug($"Spawn location blocked by an existing object skipping. {terrainDiff} | {determinedSpawn}");
                        spawn_location_attempts += 1;
                        continue;
                    }

                    if (terrainDiff > 0f) {
                        determinedSpawn.y = solidHeight;
                    }
                } else {
                    spawn_location_attempts += 1;
                    continue;
                }

                // Prevent spawns in a players base | This does not work in the ashlands as all of the existing fortresses, POIs etc are considered "player bases"
                if (foundBiome != Heightmap.Biome.AshLands && (bool)EffectArea.IsPointInsideArea(determinedSpawn, EffectArea.Type.PlayerBase)) {
                    Logger.LogDebug($"Spawn location in a players base zone, skipping. | {determinedSpawn}");
                    spawn_location_attempts += 1;
                    continue;
                }

                // Prevent water spawns
                if (determinedSpawn.y < 27) {
                    Logger.LogDebug($"Spawn location below water level, skipping. | {determinedSpawn}");
                    spawn_location_attempts += 1;
                    continue;
                }

                // Prevent spawning in Lava unless a last resort
                if (foundBiome == Heightmap.Biome.AshLands && hmap.GetVegetationMask(determinedSpawn) > 0.45f) {
                    spawn_location_attempts += 1;
                    Logger.LogDebug($"Spawn location is in lava, skipping. | {determinedSpawn}");
                    continue;
                }

                Logger.LogDebug($"Determined valid spawn target: {determinedSpawn}");
                spawn_locations.Add(determinedSpawn);
            }

            if (spawn_locations.Count < numTargets) {
                Logger.LogWarning("Unable to find the requested number of spawn points.");
            }
            resultset.ForceSet(spawn_locations);
            pointsReady.ForceSet(true);
            yield break;
        }

    }
}
