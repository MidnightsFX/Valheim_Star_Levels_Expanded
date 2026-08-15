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
        // Breadcrumb for the CheckForRaidUpdate error handler, so a failure names the player it was working on.
        string currentlyCheckingPlayer = null;

        public void Awake() {
            InvokeRepeating("CheckForRaidUpdate", 30, 30);
        }

        // InvokeRepeating target. Anything escaping the check would otherwise surface as a bare
        // NullReferenceException on this frame with no indication of which player or peer was being processed,
        // and would silently cost that entire cycle.
        public void CheckForRaidUpdate() {
            try {
                RunRaidCheck();
            } catch (Exception e) {
                Logger.LogError($"The raid check failed{(string.IsNullOrEmpty(currentlyCheckingPlayer) ? "" : $" while processing player {currentlyCheckingPlayer}")}, raids will be retried on the next check. Exception: {e}");
            } finally {
                currentlyCheckingPlayer = null;
            }
        }

        private void RunRaidCheck() {
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

                // Get updates requested for all of the existing players who do not have private key entries already.
                // Ideally this should never get hit, as we should already get this information when the players
                // connect (RaidPatches syncs on Player.Load and on every unique-key change).
                bool waitForPeerUpdates = false;
                foreach (ZNetPeer zpeer in ZNet.instance.GetPeers()) {
                    if (zpeer == null || zpeer.IsReady() == false) { continue; }
                    string playerPlatformID = SLSExtensions.GetPlatformUserID(zpeer.m_uid).ToString();
                    if (RaidControl.ServerPlayerRaidData.ContainsKey(playerPlatformID)) { continue; }

                    Logger.LogRaid($"No raid data held for peer {zpeer.m_playerName} ({playerPlatformID}), requesting their private keys.");
                    ValConfig.ClientSendPlayerPrivateKeysRPC.SendPackage(zpeer.m_uid, new ZPackage());
                    waitForPeerUpdates = true;
                }
                if (waitForPeerUpdates) {
                    // Come back promptly to pick the pending peers up, but only abort the cycle if there is
                    // genuinely nobody to raid yet. A single un-synced peer used to block raids for every other
                    // player on the server, indefinitely if that client never answered the request.
                    nextCheckForRaidsTime = ZNet.instance.GetTimeSeconds() + 60;
                    if (RaidControl.ServerPlayerRaidData.Count == 0) {
                        Logger.LogInfo("Networked players data is needed to ensure accurate raids, delaying raid initilaization and awaiting updated client data.");
                        return;
                    }
                    Logger.LogRaid("Some connected peers have no raid data yet; they will be considered once their client data arrives. Continuing with the players already known.");
                }
                // This is a non-networked player running the server
                bool isIntegratedServer = false;
                string localPlayerPlatformAndID = null;
                if (ZNet.instance.IsServer() && ZNet.instance.IsDedicated() == false && Player.m_localPlayer != null) {
                    Logger.LogRaid("Integrated server mode enabled, local player will be checked for configuration data. Networked players already validated.");
                    localPlayerPlatformAndID = SLSExtensions.GetLocalUserPlatformAndID();
                    RaidControl.UpdateOrAddPlayerPrivateKeys(localPlayerPlatformAndID, Player.m_localPlayer.GetPrivateKeysSanitize());
                    isIntegratedServer = true;
                }


                int numRaids = UnityEngine.Random.Range(1, Mathf.Min(ValConfig.MaxActiveRaids.Value, players));
                int activatingRaids = 0;
                int raidsChecked = 0;
                double currentTime = ZNet.instance.GetTimeSeconds();
                Logger.LogRaid($"Starting raid init check potential num raids: {numRaids} start-time: {currentTime} checking {RaidControl.ServerPlayerRaidData.Count} players for raid availability.");
                List<string> peers = new List<string>();
                foreach (PlayerInfo player in ZNet.instance.GetPlayerList()) {
                    peers.Add($"{player.m_userInfo.m_id.m_platform}_{player.m_userInfo.m_id.m_userID}");
                }
                Logger.LogRaid($"Available players for raids:\n{string.Join("\n", peers)}\nAvailable Player data:\n{string.Join("\n", RaidControl.ServerPlayerRaidData.Keys)}");
                // Snapshot: committing a raid can add a player entry (RaidControl.FinalizeRaidCommit), which would
                // invalidate a live enumerator mid-check. PlayerRaidData is a reference type, so updates still land.
                List<KeyValuePair<string, PlayerRaidData>> trackedPlayers = RaidControl.ServerPlayerRaidData.ToList();
                foreach (KeyValuePair<string, PlayerRaidData> playerRaids in trackedPlayers) {
                    currentlyCheckingPlayer = playerRaids.Key;
                    Logger.LogRaid($"Checking raids for {playerRaids.Key}");

                    if (SLSExtensions.PlatformAndIDIsPlayerOnline(playerRaids.Key) == false) {
                        Logger.LogRaid($"Client {playerRaids.Key} was not online, skipping raid checks for them.");
                        continue;
                    }
                    if (forceRaidStart == false && playerRaids.Value.NextRaidableTime >= currentTime) {
                        Logger.LogRaid($"{playerRaids.Key} is not currently raidable, still on cooldown: {playerRaids.Value.NextRaidableTime} >= {currentTime}");
                        continue;
                    }
                    if (activatingRaids >= numRaids) {
                        Logger.LogRaid($"Number of raids activating now matches: activating {activatingRaids} == target {numRaids}");
                        break;
                    }

                    if (ZNet.TryGetPlayerByPlatformUserID(new PlatformUserID(playerRaids.Key), out ZNet.PlayerInfo playerInfo) == false) {
                        Logger.LogInfo($"Could not find player by platform ID {playerRaids.Key}, this player will be skipped.");
                        continue;
                    }
                    Vector3 raidPosition = SLSExtensions.GetPlayerPosition(playerInfo.m_characterID);

                    if (raidPosition == Vector3.zero) {
                        Logger.LogRaid($"Player {playerRaids.Key} position was not found, they will not get raided.");
                        continue;
                    }
                    // Check distance to existing raids
                    bool tooClose = false;
                    foreach (KeyValuePair<string, PlayerRaidData> playerRaid in trackedPlayers) {
                        // Skip distance check if the player is waiting for a raid still
                        if (playerRaid.Value.NextRaidableTime < currentTime) { continue; }

                        // Last raid of the active raid type, is within its active duration
                        if (playerRaid.Value.ActiveRaid != null && playerRaid.Value.LastRaidByName.ContainsKey(playerRaid.Value.ActiveRaid.Name)) {
                            double lastRaidTime = playerRaid.Value.LastRaidByName[playerRaid.Value.ActiveRaid.Name];

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
                        Logger.LogRaid("Potential raid would be too close to an existing raid, skipping.");
                        break;
                    }


                    // Check available raids to see which one could activate
                    Logger.LogRaid($"Updating available raids for {playerRaids.Key}");
                    playerRaids.Value.PlayerAvailableRaids = RaidControl.GetValidRaidsForPlayer(raidPosition, playerRaids.Key);
                    Logger.LogRaid($"Shuffling {playerRaids.Value.PlayerAvailableRaids.Count} potential raids for player...");

                    foreach (RaidDefinition raid in playerRaids.Value.PlayerAvailableRaids.ShuffleList()) {
                        if (raidsChecked >= ValConfig.MaxRaidAttemptsPerPlayer.Value) {
                            Logger.LogRaid($"Reached max raid attempts per player ({ValConfig.MaxRaidAttemptsPerPlayer.Value}), stopping checks for player {playerRaids.Key}");
                            break;
                        }
                        raidsChecked++;


                        float randv = UnityEngine.Random.Range(0f, 100f);
                        Logger.LogRaid($"Raid {raid} checking activation chance: {randv} <= {raid.Activation.Chance * RaidsData.SLE_Raid_Settings.GlobalSettings.GlobalRaidChanceScalar} | Forced? {forceRaidStart}");
                        if (forceRaidStart || randv <= raid.Activation.Chance * RaidsData.SLE_Raid_Settings.GlobalSettings.GlobalRaidChanceScalar) {
                            Logger.LogRaid($"Activating Raid {raid.Name} for player {playerRaids.Key}");
                            // Send RPC to player to start their raid
                            Logger.LogRaid($"Determining raid init style: integrated? {isIntegratedServer} && {localPlayerPlatformAndID} == {playerRaids.Key}");
                            if (isIntegratedServer && localPlayerPlatformAndID == playerRaids.Key) {
                                Logger.LogRaid("Starting integrated raid runner.");
                                RaidControl.StartRaidRunner(raid, raidPosition);
                            } else {
                                Logger.LogRaid("Starting networked raid runner.");
                                ZNetPeer zpeer = SLSExtensions.GetPeerByPlatformID(playerRaids.Key);
                                if (RaidControl.StartNetworkedRaidForPeer(raid, raidPosition, zpeer) == false) {
                                    Logger.LogWarning($"Tried to start raid {raid.Name} for player {playerRaids.Key} but networked dispatch failed (peer null or unavailable).");
                                    continue;
                                }
                            }
                            // Cooldown + music are deferred until the client confirms the raid actually started
                            // (RaidControl.FinalizeRaidCommit). Mark a short pending hold so this player isn't
                            // re-dispatched before that confirmation arrives; a raid that never starts won't burn
                            // the full cooldown.
                            RaidControl.MarkRaidPending(playerRaids.Value, raid, raidPosition);

                            activatingRaids++;
                            break;
                        }
                    }
                }
                // Save player raid data after a set of raids has been run, this will have the most accurate cooldown information
                forceRaidStart = false;
                RaidsData.SaveServerRaidData(DataObjects.yamlSerializer.Serialize(RaidControl.ServerPlayerRaidData));
            }
        }

        public void Setup() {
            Logger.LogRaid("Starting setup for RaidManager.");
            Dictionary<string, PlayerRaidData> loadedRaidData = null;
            try {
                loadedRaidData = yamlDeserializer.Deserialize<Dictionary<string, PlayerRaidData>>(RaidsData.LoadServerRaidData());
            } catch (Exception e) {
                Logger.LogWarning($"There was an error loading saved player raid data. New data will be requested from players. Exception: {e}");
            }
            // An absent or empty save deserializes to null without throwing, so the catch above never sees it.
            if (loadedRaidData == null) {
                Logger.LogWarning($"No saved player raid data was found ({ValConfig.raidsServerSavedData}), starting from an empty registry. Player data will be requested from connected clients.");
            }
            RaidControl.ServerPlayerRaidData = loadedRaidData;
            setup = true;
        }

        public void ForceRaidStart() {
            forceRaidStart = true;
        }

        public void OnDestroy() {
            // Only the server owns this registry. A client leaving a world would otherwise overwrite its own
            // ServerRaidSavedData.yaml with whatever it happened to hold (often null, since Setup deserializes
            // an empty string on a client into null).
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            RaidsData.SaveServerRaidData(DataObjects.yamlSerializer.Serialize(RaidControl.ServerPlayerRaidData));
        }
    }
}
