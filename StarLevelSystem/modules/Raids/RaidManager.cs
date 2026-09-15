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
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            if (ValConfig.UseVanillaRaidConfiguration.Value == true) { return; }
            // The schedule lives in a per-world file that cannot be resolved until ZNet knows which world
            // is running, which is well after RandEventSystem.Awake ran Setup. Nothing may consult a
            // cooldown before this succeeds, or the check would run against an empty registry.
            if (RaidControl.EnsureRegistryLoaded() == false) { return; }

            // Re-base first if the cooldown clock was changed, then advance every online player's own
            // clock. Both have to happen before any stamp is read this tick.
            RaidControl.EnsureCooldownClock();
            RaidControl.AccruePlayerTime();

            // Persist any player-key registry changes accumulated since the last tick (key changes
            // only mark the registry dirty rather than writing the file per event).
            RaidControl.FlushPlayerRaidData();

            if (RaidsData.SLE_Raid_Settings.GlobalSettings.DisableAllRaids == true) { return; }


            if (forceRaidStart || ZNet.instance.GetTimeSeconds() >= RaidControl.NextRaidCheckTime) {
                // Update time backoff. Persisted with the registry, so logging back in resumes the
                // schedule instead of granting a fresh raid roll 30 seconds into every session.
                RaidControl.NextRaidCheckTime = ZNet.instance.GetTimeSeconds() + (ValConfig.ServerTimeBetweenRaidStartChecks.Value * 60);
                Logger.LogDebug($"Raid check happening. Next check will be at: {RaidControl.NextRaidCheckTime} currentTime: {ZNet.instance.GetTimeSeconds()}");
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
                    PlatformUserID peerPlatformUserID = SLSExtensions.GetPeerPlatformUserID(zpeer);
                    // An unresolvable peer would key the registry off PlatformUserID.None, whose string form
                    // is empty -- a junk entry no online player ever matches, persisted to disk forever.
                    if (peerPlatformUserID.IsValid == false) {
                        Logger.LogWarning($"Could not resolve a platform ID for peer {zpeer.m_playerName} (uid {zpeer.m_uid}) on backend {ZNet.m_onlineBackend}; they will not be considered for raids.");
                        continue;
                    }
                    string playerPlatformID = peerPlatformUserID.ToString();
                    if (RaidControl.ServerPlayerRaidData.ContainsKey(playerPlatformID)) { continue; }

                    Logger.LogRaid($"No raid data held for peer {zpeer.m_playerName} ({playerPlatformID}), requesting their private keys.");
                    ValConfig.ClientSendPlayerPrivateKeysRPC.SendPackage(zpeer.m_uid, new ZPackage());
                    waitForPeerUpdates = true;
                }
                if (waitForPeerUpdates) {
                    // Come back promptly to pick the pending peers up, but only abort the cycle if there is
                    // genuinely nobody to raid yet. A single un-synced peer used to block raids for every other
                    // player on the server, indefinitely if that client never answered the request.
                    RaidControl.NextRaidCheckTime = ZNet.instance.GetTimeSeconds() + 60;
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
                double worldTime = ZNet.instance.GetTimeSeconds();
                Logger.LogRaid($"Starting raid init check potential num raids: {numRaids} start-time: {worldTime} checking {RaidControl.ServerPlayerRaidData.Count} players for raid availability.");
                List<string> peers = new List<string>();
                foreach (PlayerInfo player in ZNet.instance.GetPlayerList()) {
                    peers.Add(player.m_userInfo.m_id.ToString());
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
                    // Under RaidCooldownClockSource.PlayerTime each player's stamps are in their own clock,
                    // so there is no single "current time" to compare a whole registry against.
                    double playerNow = RaidControl.CooldownNow(playerRaids.Value);
                    if (forceRaidStart == false && playerRaids.Value.NextRaidableTime >= playerNow) {
                        Logger.LogRaid($"{playerRaids.Key} is not currently raidable, still on cooldown: {playerRaids.Value.NextRaidableTime} >= {playerNow}");
                        continue;
                    }
                    if (activatingRaids >= numRaids) {
                        Logger.LogRaid($"Number of raids activating now matches: activating {activatingRaids} == target {numRaids}");
                        break;
                    }

                    // TryParse rather than new PlatformUserID(key): an unparseable key yields
                    // PlatformUserID.None, and TryGetPlayerByPlatformUserID compares with an equality that
                    // treats two invalid ids as equal -- so a junk key would match the first player whose own
                    // id failed to resolve.
                    if (PlatformUserID.TryParse(playerRaids.Key, out PlatformUserID trackedPlatformUserID) == false) {
                        Logger.LogInfo($"Tracked raid player '{playerRaids.Key}' is not a valid platform ID, this entry will be skipped.");
                        continue;
                    }
                    if (ZNet.TryGetPlayerByPlatformUserID(trackedPlatformUserID, out ZNet.PlayerInfo playerInfo) == false) {
                        Logger.LogInfo($"Could not find player by platform ID {playerRaids.Key}, this player will be skipped.");
                        continue;
                    }
                    Vector3 raidPosition = SLSExtensions.GetPlayerPosition(playerInfo.m_characterID);

                    if (raidPosition == Vector3.zero) {
                        Logger.LogRaid($"Player {playerRaids.Key} position was not found, they will not get raided.");
                        continue;
                    }
                    // The first raid in an area is the only raid. Asked of the world itself (runner ZDOs, plus raids
                    // dispatched moments ago) rather than the per-player bookkeeping this used to consult, which
                    // only knew a raid for its Duration, never learned of force-started ones, and could not see the
                    // raid handed to the previous player in this same loop. See RaidControl.CanStartRaidAt. A
                    // continue rather than the old break: a player elsewhere can still be raided this check.
                    if (RaidControl.CanStartRaidAt(raidPosition, out string blockedBy) == false) {
                        Logger.LogRaid($"Skipping raids for {playerRaids.Key}: {blockedBy}.");
                        continue;
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
                                Logger.LogRaid($"Dispatched raid {raid.Name} to peer {zpeer.m_playerName} (uid {zpeer.m_uid}).");
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
                RaidControl.FlushPlayerRaidData(force: true);
            }
        }

        public void Setup() {
            Logger.LogRaid("Starting setup for RaidManager.");
            setup = true;
            // This runs from RandEventSystem.Awake, where ZNet.instance is normally still null and the
            // world name -- which the saved-data path is keyed on -- is not resolvable. Loading here read
            // the legacy shared file instead of this world's, so every world came up holding some other
            // world's cooldowns. Try anyway (a dedicated server can already be up), and otherwise let the
            // raid check load it on its first tick.
            bool loaded = RaidControl.EnsureRegistryLoaded();
            // Peer identity resolution is backend-dependent (see SLSExtensions.GetPeerPlatformUserID), so
            // naming the backend here makes any future raid-dispatch report self-identifying.
            Logger.LogInfo($"SLS raid manager ready. Online backend: {ZNet.m_onlineBackend}, dedicated: {(ZNet.instance == null ? "unknown" : ZNet.instance.IsDedicated().ToString())}, " +
                (loaded ? $"saved players: {RaidControl.ServerPlayerRaidData.Count}." : "raid schedule will be loaded once the world is known."));
        }

        public void ForceRaidStart() {
            forceRaidStart = true;
        }

        public void OnDestroy() {
            // FlushPlayerRaidData is itself server-only: a client leaving a world would otherwise overwrite
            // the legacy shared file (its world name is null) with a registry it never loaded.
            RaidControl.FlushPlayerRaidData(force: true);
        }
    }
}
