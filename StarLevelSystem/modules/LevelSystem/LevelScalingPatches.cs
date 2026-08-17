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
                LocationReset.LocationResetState.FlushPendingSave();
            }
        }

        // Leaving a world/server: tear down zone and ring state so the next world rebuilds and
        // re-syncs cleanly instead of reusing stale geometry (static state + coroutines otherwise
        // persist, since TaskRunner is DontDestroyOnLoad).
        //
        // Setting MinimapOverlayFog.WorldUnloading here is what stops the overlay rebuilds from being
        // restarted during teardown: Jotunn's SynchronizationManager restores every synced config
        // entry to its local value from a ZNet.OnDestroy prefix, raising SettingChanged on our overlay
        // settings. ZNet.Shutdown runs from Game.Shutdown strictly before ZNet.OnDestroy, so the flag
        // is always set first. Don't move this to a ZNet.OnDestroy prefix -- Harmony prefix ordering
        // against Jotunn is not guaranteed.
        internal static void OnWorldUnload() {
            MinimapOverlayFog.WorldUnloading = true;
            ZoneScaleSystem.ResetForWorldChange();
            DistanceScaleSystem.ResetForWorldChange();
            LocationReset.LocationResetControl.OnWorldUnload();
            // Nemesis pin registry is static; without this, pin entries from world A leak into
            // world B and RemovePin silently no-ops on their dead references.
            NemesisSystem.NemesisMinimap.ClearAll();
            ConfigNetwork.ResetServerSyncState();
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        public static class WorldUnloadTeardown {
            [HarmonyPrefix]
            static void ResetOnLeave() {
                OnWorldUnload();
            }
        }

        // Quit/suspend paths bypass Shutdown entirely.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.ShutdownWithoutSave))]
        public static class WorldUnloadTeardownNoSave {
            [HarmonyPrefix]
            static void ResetOnLeaveWithoutSave() {
                OnWorldUnload();
            }
        }

        // Entering a world: clear the teardown flag so overlays may draw again. ZNet.Awake assigns
        // ZNet.m_instance and runs well before OnVanillaMapDataLoaded triggers the redraw.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        public static class WorldLoadReset {
            [HarmonyPostfix]
            static void ClearUnloadingFlag() {
                MinimapOverlayFog.WorldUnloading = false;
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
