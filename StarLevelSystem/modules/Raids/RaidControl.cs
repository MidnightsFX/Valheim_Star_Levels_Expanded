using Jotunn.Managers;
using PlayFab.ClientModels;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Raids
{
    internal static class RaidControl
    {
        internal static Dictionary<string, RaidDefinition> RaidsByName = new Dictionary<string, RaidDefinition>();
        internal static Dictionary<ZNetPeer, List<RaidDefinition>> AvailableRaidsPerPlayer = new Dictionary<ZNetPeer, List<RaidDefinition>>();
        
        // TODO, these should likely use platform ID+playername
        internal static Dictionary<long, PlayerPrivatekeys> PlayerPrivateKeys = new Dictionary<long, PlayerPrivatekeys>();
        internal static Dictionary<long, PlayerRaidHistory> PlayerRaidActivationHistory = new Dictionary<long, PlayerRaidHistory>();


        internal static void UpdateOrAddPlayerPrivateKeys(long playerID, List<string> privatekeys) {
            if (PlayerPrivateKeys.ContainsKey(playerID)) {
                PlayerPrivateKeys[playerID].PrivateKeys = privatekeys;
                PlayerPrivateKeys[playerID].LastUpdatedAt = Time.time;
            } else {
                PlayerPrivateKeys.Add(playerID, new PlayerPrivatekeys() { LastUpdatedAt = Time.time, PrivateKeys = privatekeys });
            }
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
                if (cfg.GlobalSettings.GlobalRaidChanceScalar > 0f) {
                    res.m_eventChance *= cfg.GlobalSettings.GlobalRaidChanceScalar;
                }
                if (cfg.GlobalSettings.DisableAllRaids) {
                    res.m_events.Clear();
                    Logger.LogInfo("SLS raid system: DisableAllRaids set, cleared all random events.");
                    return;
                }
            }

            RaidsByName.Clear();
            foreach (KeyValuePair<string, RaidDefinition> kv in cfg.Raids) {
                if (RaidsByName.ContainsKey(kv.Key)) {
                    Logger.LogWarning($"Duplicate Raid definition found {kv.Key} it will be ignored.");
                    continue;
                }
                if (kv.Value == null || kv.Value.Enabled == false) { continue; }

                RaidsByName.Add(kv.Key, kv.Value);
            }

            Logger.LogInfo($"SLS raid system: applied {RaidsByName.Count} raid definitions.");
        }

        internal static void UpdateAvailableRaidsPerPlayer() {
            AvailableRaidsPerPlayer.Clear();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) {
                if (peer.IsReady() == false) { continue; }
                List<RaidDefinition> playerAvailableRaids = GetValidRaidsForPlayer(peer);
            }
        }

        internal static List<RaidDefinition> GetValidRaidsForPlayer(ZNetPeer peer) {
            List<RaidDefinition> playerAvailableRaids = new List<RaidDefinition>();
            Vector3 position = peer.GetRefPos();
            bool inBase = EffectArea.IsPointInsideArea(position, EffectArea.Type.PlayerBase, 30f);
            Heightmap heightmap = Heightmap.FindHeightmap(position);
            Heightmap.Biome biome = heightmap.GetBiome(position);
            

            foreach(KeyValuePair<string, RaidDefinition> raid in RaidsByName) {
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

                PlayerPrivateKeys.TryGetValue(peer.m_uid, out PlayerPrivatekeys playerPrivateKeys);


                // Player Private keys will require an RPC requeast from the client for the data, since it is not stored server side.
                // Required private key check
                if (raid.Value.Activation.RequiredPlayerKeys != null && playerPrivateKeys.PrivateKeys.Count > 0) {
                    bool hasRequiredPlayerKeys = true;
                    foreach (string pkey in raid.Value.Activation.RequiredPlayerKeys) {
                        if (playerPrivateKeys.PrivateKeys.Contains(pkey) == false) {
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
                        if (playerPrivateKeys.PrivateKeys.Contains(pkey)) {
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
                if (PlayerRaidActivationHistory.ContainsKey(peer.m_uid)) {
                    PlayerRaidHistory prh = PlayerRaidActivationHistory[peer.m_uid];
                    if (prh.NextRaidableTime > Time.time) {
                        Logger.LogDebug($"Player {peer.m_playerName} has a NextRaidableTime of {prh.NextRaidableTime} which is in the future, skipping Raid: {raid.Key}");
                        continue;
                    }
                    if (prh.LastRaidByName != null && prh.LastRaidByName.ContainsKey(raid.Key) && (prh.LastRaidByName[raid.Key] + raid.Value.RaidCoolDownMinutes) > Time.time) {
                        Logger.LogDebug($"Player {peer.m_playerName} has activated Raid {raid.Key} too recently, skipping. Next possible activation time: {prh.LastRaidByName[raid.Key] + raid.Value.RaidCoolDownMinutes}");
                        continue;
                    }
                }

                Logger.LogDebug($"Raid {raid.Key} valid for player {peer.m_playerName}");
                playerAvailableRaids.Add(raid.Value);
            }


            return playerAvailableRaids;
        }
    }
}
