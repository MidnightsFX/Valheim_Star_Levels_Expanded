using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Raids {
    // This is the monobehavior that takes over control of raid management.
    // It primarily runs on the server.

    public class RaidManager : MonoBehaviour {
        bool setup = false;
        double lastCheckForRaidStart = 0;

        public void FixedUpdate() {
            if (setup == false) { return; }
            if (ValConfig.UseVanillaRaidConfiguration.Value == true) { return; }
            if (ZNet.instance.IsServer() == false) { return; }

            // Raid kick off check
            if (Time.timeAsDouble >= lastCheckForRaidStart) {
                // Update time backoff
                lastCheckForRaidStart += (ValConfig.ServerTimeBetweenRaidStartChecks.Value * 60);
                // Nothing to do if no one is connected
                if (ZNet.instance.m_peers.Count <= 0) { return; }

                Logger.LogDebug("Starting raid init check");

                int numRaids = UnityEngine.Random.Range(1, Mathf.Min(ValConfig.MaxActiveRaids.Value, ZNet.instance.GetNrOfPlayers()));
                int activatingRaids = 0;
                int raidsChecked = 0;
                foreach (KeyValuePair<string, PlayerRaidData> playerRaids in RaidControl.ServerPlayerRaidData) {
                    ZNetPeer zpeer = ZNet.instance.GetPeerByHostName(playerRaids.Key);
                    if (zpeer == null) {
                        Logger.LogDebug($"Client {playerRaids.Key} was not online, skipping raid checks for them.");
                        continue;
                    }
                    if (playerRaids.Value.ActiveRaid != null) { continue; }

                    foreach (RaidDefinition raid in playerRaids.Value.PlayerAvailableRaids.ShuffleList()) {
                        if (raidsChecked >= ValConfig.MaxRaidAttemptsPerPlayer.Value) {
                            Logger.LogDebug($"Reached max raid attempts per player ({ValConfig.MaxRaidAttemptsPerPlayer.Value}), stopping checks for player {playerRaids.Key}");
                            break;
                        }
                        raidsChecked++;
                        float randv = UnityEngine.Random.Range(0f, 100f);
                        if (randv <= raid.Activation.Chance * RaidsData.SLE_Raid_Settings.GlobalSettings.GlobalRaidChanceScalar) {
                            Logger.LogDebug($"Activating Raid for player {playerRaids.Key}");
                            RaidControl.UpdatePlayerRaidHistory(playerRaids.Value, raid);
                            // Send RPC to player to start their raid
                            ZPackage zpack = new ZPackage();
                            zpack.Write(DataObjects.yamlserializer.Serialize(raid));
                            ValConfig.ClientStartRaidRPC.SendPackage(zpeer.m_uid, zpack);

                            activatingRaids++;
                            if (activatingRaids >= numRaids) { break; }
                        }
                    }
                }
            }
        }

        public void Setup() {
            Logger.LogDebug("Starting setup for RaidManager.");
            try {
                yamldeserializer.Deserialize<Dictionary<string, PlayerRaidData>>(RaidsData.LoadServerRaidData());
                setup = true;
            } catch {
                Logger.LogError("There was an error loading server Raid Data. The raid controller will not startup.");
            }
            
        }

        public void OnDestroy() {
            RaidsData.SaveServerRaidData(DataObjects.yamlserializer.Serialize(RaidControl.ServerPlayerRaidData));
        }
    }
}
