using HarmonyLib;
using StarLevelSystem.Data;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Raids
{
    internal static class RaidPatches
    {
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.Awake))]
        public static class RandEventSystemAwakePatch {
            public static void Postfix(RandEventSystem __instance) {
                RaidControl.ApplyRaidConfiguration(__instance);
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.StartRandomEvent))]
        public static class RandEventSystemStartEvent {
            public static bool Prefix(RandEventSystem __instance) {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }

                if (ZNet.instance.IsServer() == false) {
                    return false;
                }
                List<KeyValuePair<RandomEvent, Vector3>> possibleRandomEvents = __instance.GetPossibleRandomEvents();
                if (possibleRandomEvents.Count == 0) {
                    Logger.LogDebug("No valid raid events found for current player positions.");
                    return false;
                }

                KeyValuePair<RandomEvent, Vector3> keyValuePair = possibleRandomEvents[UnityEngine.Random.Range(0, possibleRandomEvents.Count)];
                __instance.SetRandomEvent(keyValuePair.Key, keyValuePair.Value);
                Logger.LogDebug("Starting event: " + keyValuePair.Key.m_name);

                return false;
            }
        }


        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.UpdateRandomEvent))]
        public static class OverrideRaidSelectionSystem {
            public static bool Prefix(RandEventSystem __instance, float dt) {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }
                // Do not run on clients, unless this is the server client
                if (ZNet.instance.IsServer() == false) { return false; }

                __instance.m_eventTimer += dt;
                if (ValConfig.RaidEventRate.Value > 0f && __instance.m_eventTimer > __instance.m_eventIntervalMin * 60f * ValConfig.RaidEventRate.Value) {
                    __instance.m_eventTimer = 0f;
                    if (Random.Range(0f, 100f) <= __instance.m_eventChance) {
                        __instance.StartRandomEvent();
                    }
                }
                if (RandEventSystem.s_randomEventNeedsRefresh) { RandEventSystem.RefreshPlayerEventData(); }
                foreach (RandomEvent randomEvent in __instance.m_events) {
                    if (randomEvent.m_enabled && randomEvent.m_standaloneInterval > 0f && __instance.m_activeEvent != randomEvent) {
                        randomEvent.m_time += dt;
                        if (randomEvent.m_time > randomEvent.m_standaloneInterval * Game.m_eventRate) {
                            if (__instance.HaveGlobalKeys(randomEvent, RandEventSystem.s_playerEventDatas)) {
                                List<Vector3> validEventPoints = __instance.GetValidEventPoints(randomEvent, RandEventSystem.s_playerEventDatas);
                                if (validEventPoints.Count > 0 && Random.Range(0f, 100f) <= randomEvent.m_standaloneChance / Game.m_eventRate) {
                                    __instance.SetRandomEvent(randomEvent, validEventPoints[Random.Range(0, validEventPoints.Count)]);
                                }
                            }
                            randomEvent.m_time = 0f;
                        }
                    }
                }
                __instance.m_sendTimer += dt;
                if (__instance.m_sendTimer > 2f) {
                    __instance.m_sendTimer = 0f;
                    __instance.SendCurrentRandomEvent();
                }

                // We override the entire raid selection process if SLS raids are enabled
                return false;
            }
        }
    }
}
