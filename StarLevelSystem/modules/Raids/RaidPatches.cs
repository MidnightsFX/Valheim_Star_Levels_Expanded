using HarmonyLib;
using StarLevelSystem.Data;
using System.Collections.Generic;
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

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.UpdateRandomEvent))]
        public static class OverrideRaidSelectionSystem {
            public static bool Prefix(RandEventSystem __instance, float dt) {
                if (ValConfig.UseVanillaRaidConfiguration.Value) { return true; }

                return false;
            }
        }
    }
}
