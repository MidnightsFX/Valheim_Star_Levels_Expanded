using Splatform;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static ZNet;

namespace StarLevelSystem.modules.Raids {
    // This is the monobehavior that takes over control of raid management.
    // It primarily runs on the server.

    public class RaidManager : MonoBehaviour {
        bool setup = false;
        double nextCheckForRaidsTime = 0;
        bool forceRaidStart = false;

        public void FixedUpdate() {
            if (setup == false) { return; }
            if (ValConfig.UseVanillaRaidConfiguration.Value == true) { return; }
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            if (RaidsData.SLE_Raid_Settings.GlobalSettings.DisableAllRaids == true) { return; }


            if (forceRaidStart || ZNet.instance.GetTimeSeconds() >= nextCheckForRaidsTime) {
                // Update time backoff
                nextCheckForRaidsTime = ZNet.instance.GetTimeSeconds() + (ValConfig.ServerTimeBetweenRaidStartChecks.Value * 60);
                Logger.LogDebug($"Raid check happening. Next check will be at: {nextCheckForRaidsTime} currentTime: {ZNet.instance.GetTimeSeconds()}");
                // Nothing to do if no one is connected
                int players = ZNet.instance.GetNrOfPlayers();
                if (players <= 0) {
                    Logger.LogDebug("No Players online, skipping raids.");
                    return;
                }

                // Get updates requested for all of the existing players who do not have private key entries already
                // Ideally this should never get hit, as we should already get this information when the players connect
                bool waitForPeerUpdates = false;
                foreach (ZNetPeer zpeer in ZNet.instance.GetPeers()) {
                    string playerPlatformID = SLSExtensions.GetPlatformUserID(zpeer.m_uid).ToString();
                    if (RaidControl.ServerPlayerRaidData.Keys.Contains(playerPlatformID) == false) {
                        ZPackage package = new ZPackage();
                        ValConfig.ClientSendPlayerPrivateKeysRPC.SendPackage(zpeer.m_uid, package);
                        waitForPeerUpdates = true;
                    }
                }
                if (waitForPeerUpdates) {
                    Logger.LogInfo("Networked players data is needed to ensure accurate raids, delaying raid initilaization and awaiting updated client data.");
                    nextCheckForRaidsTime = ZNet.instance.GetTimeSeconds() + 60;
                    return;
                }
                // This is a non-networked player running the server
                bool isIntegratedServer = false;
                string localPlayerPlatformAndID = null;
                if (ZNet.instance.IsServer() && ZNet.instance.IsDedicated() == false && Player.m_localPlayer != null) {
                    Logger.LogDebug("Integrated server mode enabled, local player will be checked for configuration data. Networked players already validated.");
                    localPlayerPlatformAndID = SLSExtensions.GetLocalUserPlatformAndID();
                    RaidControl.UpdateOrAddPlayerPrivateKeys(localPlayerPlatformAndID, Player.m_localPlayer.GetPrivateKeysSanitize());
                    isIntegratedServer = true;
                }


                int numRaids = UnityEngine.Random.Range(1, Mathf.Min(ValConfig.MaxActiveRaids.Value, players));
                int activatingRaids = 0;
                int raidsChecked = 0;
                double currentTime = ZNet.instance.GetTimeSeconds();
                Logger.LogDebug($"Starting raid init check potential num raids: {numRaids} start-time: {currentTime} checking {RaidControl.ServerPlayerRaidData.Count} players for raid availability.");
                List<string> peers = new List<string>();
                foreach (PlayerInfo player in ZNet.instance.GetPlayerList()) {
                    peers.Add($"{player.m_userInfo.m_id.m_platform}_{player.m_userInfo.m_id.m_userID}");
                }
                Logger.LogDebug($"Available players for raids:\n{string.Join("\n", peers)}\nAvailable Player data:\n{string.Join("\n", RaidControl.ServerPlayerRaidData.Keys)}");
                foreach (KeyValuePair<string, PlayerRaidData> playerRaids in RaidControl.ServerPlayerRaidData) {
                    Logger.LogDebug($"Checking raids for {playerRaids.Key}");

                    if (SLSExtensions.PlatformAndIDIsPlayerOnline(playerRaids.Key) == false) {
                        Logger.LogDebug($"Client {playerRaids.Key} was not online, skipping raid checks for them.");
                        continue;
                    }
                    if (forceRaidStart == false && playerRaids.Value.NextRaidableTime >= currentTime) {
                        Logger.LogDebug($"{playerRaids.Key} is not currently raidable, still on cooldown: {playerRaids.Value.NextRaidableTime} >= {currentTime}");
                        continue;
                    }
                    if (activatingRaids >= numRaids) {
                        Logger.LogDebug($"Number of raids activating now matches: activating {activatingRaids} == target {numRaids}");
                        break;
                    }

                    if (ZNet.TryGetPlayerByPlatformUserID(new PlatformUserID(playerRaids.Key), out ZNet.PlayerInfo playerInfo) == false) {
                        Logger.LogInfo($"Could not find player by platform ID {playerRaids.Key}, this player will be skipped.");
                        continue;
                    }
                    Vector3 raidPosition = SLSExtensions.GetPlayerPosition(playerInfo.m_characterID);

                    if (raidPosition == Vector3.zero) {
                        Logger.LogDebug($"Player {playerRaids.Key} position was not found, they will not get raided.");
                        continue;
                    }
                    // Check distance to existing raids
                    bool tooClose = false;
                    foreach (KeyValuePair<string, PlayerRaidData> playerRaid in RaidControl.ServerPlayerRaidData) {
                        // Skip distance check if the player is waiting for a raid still
                        if (playerRaid.Value.NextRaidableTime < currentTime) { continue; }

                        // Last raid of the active raid type, is within its active duration
                        if (playerRaid.Value.LastRaidByName.ContainsKey(playerRaid.Key)) {
                            double lastRaidTime = playerRaid.Value.LastRaidByName[playerRaid.Key];

                            // Check if the raid is too close
                            if ((lastRaidTime + playerRaid.Value.ActiveRaid.Duration) > currentTime) {
                                if (Vector3.Distance(playerRaid.Value.CurrentRaidPosition, raidPosition) < playerRaid.Value.ActiveRaid.EventRange * 3) {
                                    tooClose = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (tooClose) {
                        Logger.LogDebug("Potential raid would be too close to an existing raid, skipping.");
                        break;
                    }


                    // Check available raids to see which one could activate
                    Logger.LogDebug($"Updating available raids for {playerRaids.Key}");
                    playerRaids.Value.PlayerAvailableRaids = RaidControl.GetValidRaidsForPlayer(raidPosition, playerRaids.Key);
                    Logger.LogDebug($"Shuffling {playerRaids.Value.PlayerAvailableRaids.Count} potential raids for player...");

                    foreach (RaidDefinition raid in playerRaids.Value.PlayerAvailableRaids.ShuffleList()) {
                        if (raidsChecked >= ValConfig.MaxRaidAttemptsPerPlayer.Value) {
                            Logger.LogDebug($"Reached max raid attempts per player ({ValConfig.MaxRaidAttemptsPerPlayer.Value}), stopping checks for player {playerRaids.Key}");
                            break;
                        }
                        raidsChecked++;


                        float randv = UnityEngine.Random.Range(0f, 100f);
                        Logger.LogDebug($"Raid {raid} checking activation chance: {randv} <= {raid.Activation.Chance * RaidsData.SLE_Raid_Settings.GlobalSettings.GlobalRaidChanceScalar} | Forced? {forceRaidStart}");
                        if (forceRaidStart || randv <= raid.Activation.Chance * RaidsData.SLE_Raid_Settings.GlobalSettings.GlobalRaidChanceScalar) {
                            Logger.LogDebug($"Activating Raid {raid.Name} for player {playerRaids.Key}");
                            RaidControl.UpdatePlayerRaidHistory(playerRaids.Value, raid, raid.Name);
                            playerRaids.Value.CurrentRaidPosition = raidPosition;
                            // Send RPC to player to start their raid
                            Logger.LogDebug($"Determining raid init style: integrated? {isIntegratedServer} && {localPlayerPlatformAndID} == {playerRaids.Key}");
                            if (isIntegratedServer && localPlayerPlatformAndID == playerRaids.Key) {
                                Logger.LogDebug("Starting integrated raid runner.");
                                RaidControl.StartRaidRunner(raid, raidPosition);
                                MusicMan.instance.TriggerMusic(raid.ForceMusic.ToString());
                            } else {
                                Logger.LogDebug("Starting networked raid runner.");
                                ZPackage zpack = new ZPackage();
                                zpack.Write(DataObjects.yamlserializer.Serialize(raid));
                                ZNetPeer zpeer = SLSExtensions.GetPeerByPlatformID(playerRaids.Key);
                                ValConfig.ClientStartRaidRPC.SendPackage(zpeer.m_uid, zpack);
                            }
                            RaidControl.ForceMusicForClientsInArea(raid.ForceMusic, raidPosition, raid.EventRange * 1.5f);

                            activatingRaids++;
                            break;
                        }
                    }
                }
                // Save player raid data after a set of raids has been run, this will have the most accurate cooldown information
                forceRaidStart = false;
                RaidsData.SaveServerRaidData(DataObjects.yamlserializer.Serialize(RaidControl.ServerPlayerRaidData));
            }
        }

        public void Setup() {
            Logger.LogDebug("Starting setup for RaidManager.");
            try {
                RaidControl.ServerPlayerRaidData = yamldeserializer.Deserialize<Dictionary<string, PlayerRaidData>>(RaidsData.LoadServerRaidData());
            } catch (Exception e) {
                Logger.LogWarning($"There was an error loading saved player raid data. New data will be requested from players. Exception: {e}");
            }
            setup = true;
        }

        public void ForceRaidStart() {
            forceRaidStart = true;
        }

        public void OnDestroy() {
            RaidsData.SaveServerRaidData(DataObjects.yamlserializer.Serialize(RaidControl.ServerPlayerRaidData));
        }
    }
}
