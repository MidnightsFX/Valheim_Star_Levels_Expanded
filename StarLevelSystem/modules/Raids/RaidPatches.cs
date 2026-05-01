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
                //if (ValConfig.UseVanillaRaidConfiguration.Value) { return; }
                if (ZNet.instance.IsServer() == false || Player.m_localPlayer == null) { return; }
                TaskRunner.Instance.StartCoroutine(ValConfig.OnClientRecieveRequestForPrivatekeys(1, null));
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RemoveUniqueKey))]
        internal static class RemovePlayerPrivateKey {
            public static void Postfix() {
                //if (ValConfig.UseVanillaRaidConfiguration.Value) { return; }
                if (ZNet.instance.IsServer() == false || Player.m_localPlayer == null) { return; }
                TaskRunner.Instance.StartCoroutine(ValConfig.OnClientRecieveRequestForPrivatekeys(1, null));
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        internal static class SyncPlayerPrivateKeysOnLoad {
            public static void Postfix() {
                //if (ValConfig.UseVanillaRaidConfiguration.Value) { return; }
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

        // Both need to be patched to use the new monobehavior controller instead of the vanilla methods- if enabled.
        // ConsoleStartRandomEvent
        // ConsoleResetRandomEvent

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.FixedUpdate))]
        public static class ToggleCustomRaids {
            // Skip the update loop for the randEvent system if we are using the custom raid system
            public static bool Prefix() {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }
                return false;
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEvent))]
        public static class SetRandomCustomEvent {
            public static bool Prefix(RandEventSystem __instance, RandomEvent ev, Vector3 pos) {
                Logger.LogDebug($"Checking for random Raid {ev.m_name}");
                RaidsData.SLE_Raid_Settings.Raids.TryGetValue(ev.m_name, out RaidDefinition raidDef);
                if (raidDef == null) { return false; }

                StartRaidRunner(raidDef, pos);

                if (Player.m_localPlayer) {
                    Player.m_localPlayer.ShowTutorial("randomevent", false);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.StartRandomEvent))]
        public static class RandEventSystemStartEvent {
            public static bool Prefix() {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }

                // This should connect to the custom event system and start an event?
                

                if (Player.m_localPlayer == null) {
                    Logger.LogInfo("Random Event start requires a local player. Use sls-start-event to trigger an event for a specific user instead");
                    return false;
                }
                Player.m_localPlayer.ShowTutorial("randomevent", false);
                PlatformUserID id = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID;
                StartRaidRunner(RandomSelectValidRaidForPlayer(id.m_userID), Player.m_localPlayer.transform.position);

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
