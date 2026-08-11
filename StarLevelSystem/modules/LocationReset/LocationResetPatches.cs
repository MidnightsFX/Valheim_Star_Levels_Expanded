using HarmonyLib;

namespace StarLevelSystem.modules.LocationReset {
    internal static class LocationResetPatches {

        // Server-side world entry point. ZoneSystem.Start runs on dedicated servers too (unlike the
        // MinimapManager callbacks), and by this point ZNet.instance and the world name exist, so it
        // is the right place to load per-world reset state and finish config bootstrapping.
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        public static class LocationResetWorldInit {
            [HarmonyPostfix]
            static void InitLocationResetOnServer() {
                LocationResetControl.OnZoneSystemReady();
            }
        }

        // RandEventSystem is the same host the Raid and Nemesis managers use: it exists on a loaded
        // world and on dedicated servers, and dies with the world.
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.Awake))]
        public static class AttachLocationResetManager {
            [HarmonyPostfix]
            static void AttachManager(RandEventSystem __instance) {
                if (__instance.gameObject.GetComponent<LocationResetManager>() != null) { return; }
                __instance.gameObject.AddComponent<LocationResetManager>();
            }
        }

        // Protection classification is keyed by prefab hash, resolved from ZNetScene's prefab list.
        // Rebuilding here covers dedicated servers, where the vanilla-prefab Jotunn callbacks that
        // other SLS systems use never fire.
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        public static class BuildLocationResetPrefabSets {
            [HarmonyPostfix]
            static void BuildSets() {
                ZoneProtectionScan.ResetPrefabSets();
                ZoneProtectionScan.BuildPrefabSets();
            }
        }
    }
}
