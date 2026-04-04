using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StarLevelSystem.modules.CreatureSetup {
    internal static class CreatureSetupPatches {

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class CreatureCharacterExtension {
            public static void Postfix(Character __instance) {
                CreatureSetupControl.CreatureSetup(__instance, delay: ValConfig.InitialDelayBeforeSetup.Value);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        public static class ModifyCharacterVisualsToLevel {
            public static void Prefix(Character __instance, ref int level) {
                if (level <= 1) { level = 1; }
            }
        }
    }
}
