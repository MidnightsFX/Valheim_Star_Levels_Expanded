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
                Logger.LogRaid("Adding custom raid manager");
                RaidControl.RaidMan = __instance.gameObject.AddComponent<RaidManager>();
                RaidControl.RaidMan.Setup();   
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEvent))]
        public static class SetRandomCustomEvent {
            public static bool Prefix(RandEventSystem __instance, RandomEvent ev, Vector3 pos) {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }
                if (ev == null) { return true; }

                Logger.LogRaid($"Checking for random Raid {ev.m_name}");
                RaidsData.RaidsByName.TryGetValue(ev.m_name, out RaidDefinition raidDef);
                if (raidDef == null) {
                    // Not an SLS raid. When CustomRaids compat is active, let the vanilla/CustomRaids pipeline handle it.
                    if (Compatibility.CustomRaidsCompatActive) {
                        Logger.LogRaid($"'{ev.m_name}' is not an SLS raid; passing through to the vanilla/CustomRaids pipeline.");
                        return true;
                    }
                    Logger.LogWarning($"SetRandomEvent called for '{ev.m_name}' but no matching SLS raid definition found — event dropped. Add it to RaidSettings.yaml or enable UseVanillaRaidConfiguration.");
                    return false;
                }

                RaidControl.DispatchForcedRaid(raidDef, pos);
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
                        return Compatibility.CustomRaidsCompatActive;
                    }
                    RaidControl.RaidMan = __instance.gameObject.AddComponent<RaidManager>();
                    RaidControl.RaidMan.Setup();
                    RaidMan.ForceRaidStart();
                }

                // When CustomRaids compat is active, also let the vanilla/CustomRaids forced-event path run.
                return Compatibility.CustomRaidsCompatActive;
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
                    Logger.LogRaid($"Sending reset event RPC to {peer.m_playerName}");
                    ValConfig.ClientClearNearbyEventsRPC.SendPackage(peer.m_uid, package);
                }

                if (ZNet.instance.IsDedicated() == false) {
                    Logger.LogRaid("Running integrated server removal");
                    RaidControl.RemoveNearbyRunningEvents();
                }

                // When CustomRaids compat is active, also let vanilla reset its active event.
                return Compatibility.CustomRaidsCompatActive;
            }
        }

        // The vanilla `event <name>` console command guards on HaveEvent before starting. SLS raids are defined in
        // RaidsData.RaidsByName, not vanilla m_events, so allow the guard to pass for SLS raid names.
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.HaveEvent))]
        public static class AllowSLSRaidEventNames {
            public static void Postfix(string name, ref bool __result) {
                if (__result || ValConfig.UseVanillaRaidConfiguration.Value) { return; }
                if (RaidsData.RaidsByName.ContainsKey(name)) { __result = true; }
            }
        }

        // Route the `event <name>` console command to the SLS raid system when the name is an SLS raid.
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEventByName))]
        public static class RouteNamedEventToSLS {
            public static bool Prefix(string name, Vector3 pos) {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }
                if (RaidsData.RaidsByName.TryGetValue(name, out RaidDefinition raidDef) == false) {
                    // Vanilla / CustomRaids event (present in m_events) — let vanilla resolve and start it.
                    return true;
                }
                Logger.LogRaid($"Console force-start routing '{name}' to SLS raid system at {pos}");
                RaidControl.DispatchForcedRaid(raidDef, pos);
                // Skip vanilla GetEvent/SetRandomEvent, which would no-op for an SLS-only event name.
                return false;
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.UpdateRandomEvent))]
        public static class OverrideRaidSelectionSystem {
            public static bool Prefix() {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }
                // When CustomRaids compat is active, let the vanilla selection loop run so CustomRaids raids can fire.
                // SLS's own RaidManager continues selecting raids independently, so both systems run in parallel.
                if (Compatibility.CustomRaidsCompatActive) { return true; }
                // We override the entire raid selection process if SLS raids are enabled
                return false;
            }
        }
    }
}
