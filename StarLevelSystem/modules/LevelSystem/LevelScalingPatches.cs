using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class LevelScalingPatches {

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class ZoneTracker {
            [HarmonyPrefix]
            static void TrackZoneDeath(Character __instance) {
                ZoneScaleSystem.OnCreatureKilled(__instance.transform.position);
            }
        }

        // Server side save flush, relatively larger save
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.SaveWorld))]
        public static class ZoneDataSaveFlush {
            [HarmonyPrefix]
            static void FlushZoneData() {
                ZoneScaleSystemData.FlushPendingSave();
            }
        }

        // Leaving a world/server: tear down zone state so the next world rebuilds and re-syncs
        // cleanly instead of reusing stale geometry (static state + coroutines otherwise persist).
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        public static class ZoneWorldUnload {
            [HarmonyPrefix]
            static void ResetZonesOnLeave() {
                ZoneScaleSystem.ResetForWorldChange();
            }
        }

        // Dedicated server entry point, as it does not need to generate maps
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        public static class DedicatedServerZoneInit {
            [HarmonyPostfix]
            static void InitZonesOnServer() {
                if (ZNet.instance != null && ZNet.instance.IsDedicated()) {
                    ZoneScaleSystem.Initialize();
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddUniqueKey))]
        internal static class UpdatePlayerPrivateKeys {
            public static void Postfix() {
                ConditionalScaleSystem.ResetCache();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RemoveUniqueKey))]
        internal static class RemovePlayerPrivateKey {
            public static void Postfix() {
                ConditionalScaleSystem.ResetCache();
            }
        }

    }
}
