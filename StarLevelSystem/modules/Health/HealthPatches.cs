using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StarLevelSystem.modules.Health {
    internal static class HealthPatches {
        // For debugging full details on damage calculations
        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class CharacterHealthFix {
            private static void Postfix(Character __instance) {
                if (float.IsNaN(__instance.GetHealth())) {
                    Logger.LogWarning($"NaN health detected on {__instance.name}, resetting to max health.");
                    __instance.SetHealth(__instance.GetMaxHealth());
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetHealth))]
        public static class CharacterSetHealthPreventNaN {
            private static bool Prefix(Character __instance, float health) {
                if (float.IsNaN(health)) {
                    Logger.LogWarning($"Preventing NaN health on {__instance.name}");
                    return false; // Skip original method since we do not want to set NaN health
                }
                return true; // Continue with original method
            }
        }
    }
}
