using HarmonyLib;
using Splatform;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.modules.Raids.RaidControl;

namespace StarLevelSystem.modules.Raids
{
    internal static class RaidPatches
    {
        internal static List<ActiveRaid> ActiveRaids = new List<ActiveRaid>();

        [HarmonyPatch(typeof(Player), nameof(Player.AddUniqueKey))]
        internal static class UpdatePlayerPrivateKeys {
            public static void Postfix() {
                if (ZNet.instance.IsServer() == false || Player.m_localPlayer == null) { return; }
                TaskRunner.Instance.StartCoroutine(ValConfig.OnClientRecieveRequestForPrivatekeys(1, null));
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RemoveUniqueKey))]
        internal static class RemovePlayerPrivateKey {
            public static void Postfix() {
                if (ZNet.instance.IsServer() == false || Player.m_localPlayer == null) { return; }
                TaskRunner.Instance.StartCoroutine(ValConfig.OnClientRecieveRequestForPrivatekeys(1, null));
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        internal static class SyncPlayerPrivateKeysOnLoad {
            public static void Postfix() {
                if (ZNet.instance.IsServer() == false || Player.m_localPlayer == null) { return; }
                TaskRunner.Instance.StartCoroutine(ValConfig.OnClientRecieveRequestForPrivatekeys(1, null));
            }
        }

        // Maybe we just disable this whole class?
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.Awake))]
        public static class RandEventSystemAwakePatch {
            public static void Postfix(RandEventSystem __instance) {
                //if (ZNet.instance.IsServer() == false) { return; }
                Logger.LogDebug("Adding custom raid manager");
                RaidControl.RaidMan = __instance.gameObject.AddComponent<RaidManager>();
                RaidControl.RaidMan.Setup();   
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEvent))]
        public static class SetRandomCustomEvent {
            public static bool Prefix(RandEventSystem __instance, RandomEvent ev, Vector3 pos) {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }
                if (ev == null) { return true; }

                Logger.LogDebug($"Checking for random Raid {ev.m_name}");
                RaidsData.RaidsByName.TryGetValue(ev.m_name, out RaidDefinition raidDef);
                if (raidDef == null) {
                    Logger.LogWarning($"SetRandomEvent called for '{ev.m_name}' but no matching SLS raid definition found — event dropped. Add it to RaidSettings.yaml or enable UseVanillaRaidConfiguration.");
                    return false;
                }

                // Special case for when the server itself tries to start a raid, as it does not have a player
                if (ZNet.instance != null && ZNet.instance.IsDedicated()) {
                    if (StartNetworkedRaidRunner(raidDef, pos) == false) {
                        Logger.LogWarning($"Networked raid dispatch failed for '{raidDef.Name}' at {pos}; event will be skipped this cycle.");
                    }
                    return false;
                }

                StartRaidRunner(raidDef, pos);

                if (Player.m_localPlayer) {
                    Player.m_localPlayer.ShowTutorial("randomevent", false);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.StartRandomEvent))]
        public static class RandEventSystemStartEvent {
            public static bool Prefix(RandEventSystem __instance) {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }

                if (RaidMan != null) {
                    RaidMan.ForceRaidStart();
                } else {
                    RaidManager rm = __instance.GetComponent<RaidManager>();
                    if (rm != null) {
                        RaidMan = rm;
                        RaidMan.ForceRaidStart();
                        return false;
                    }
                    RaidControl.RaidMan = __instance.gameObject.AddComponent<RaidManager>();
                    RaidControl.RaidMan.Setup();
                    RaidMan.ForceRaidStart();
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.ResetRandomEvent))]
        public static class ResetRandomEvents {
            public static bool Prefix() {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }
                // We override the entire raid selection process if SLS raids are enabled

                foreach (ZNetPeer peer in ZNet.instance.GetPeers()) {
                    if (peer == null || peer.IsReady() == false) { continue; }

                    ZPackage package = new ZPackage();
                    Logger.LogDebug($"Sending reset event RPC to {peer.m_playerName}");
                    ValConfig.ClientClearNearbyEventsRPC.SendPackage(peer.m_uid, package);
                }

                if (ZNet.instance.IsDedicated() == false) {
                    Logger.LogDebug("Running integrated server removal");
                    RaidControl.RemoveNearbyRunningEvents();
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.UpdateRandomEvent))]
        public static class OverrideRaidSelectionSystem {
            public static bool Prefix() {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }
                // We override the entire raid selection process if SLS raids are enabled
                return false;
            }
        }
    }
}
