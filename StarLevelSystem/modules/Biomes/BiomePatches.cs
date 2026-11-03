using HarmonyLib;

namespace StarLevelSystem.modules {
    internal static class BiomePatches {

        // The server's Expand World data is loaded by now, so this is the point a biome name that is still unknown can be
        // reported, and it catches a table change if the patches on Expand World itself could not be applied.
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        private static class CheckBiomesAtWorldStart {
            private static void Postfix() {
                BiomeRegistry.OnWorldStart();
            }
        }
    }
}
