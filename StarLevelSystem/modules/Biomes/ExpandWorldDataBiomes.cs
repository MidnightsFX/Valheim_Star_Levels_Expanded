using HarmonyLib;
using System;
using System.Reflection;

namespace StarLevelSystem.modules {

    // What SLS reads from Expand World Data's BiomeManager that the Enum patches it applies do not expose: which vanilla
    // biome a custom one builds its terrain from, which biomes have lava, and when its table changed. Everything is looked
    // up by name so SLS carries no reference to the mod, and each member binds on its own so a rename only loses that one.
    internal static class ExpandWorldDataBiomes {
        internal const string PluginGuid = "expand_world_data";

        // The first is 1.79's; it moved there from the second on 2026-10-19.
        private static readonly string[] ManagerTypeNames = { "ExpandWorldData.Biome.BiomeManager", "ExpandWorldData.BiomeManager" };

        // NamesFromFile is the mod's Start, SetNames a client's join, and Set every load of biome data after that.
        private static readonly string[] TableChangeMethods = { "NamesFromFile", "SetNames", "Set" };

        private static Type manager;
        private static Func<Heightmap.Biome, Heightmap.Biome> getTerrain;
        private static FieldInfo lavaBiomes;

        internal static void Bind(Assembly assembly) {
            foreach (string typeName in ManagerTypeNames) {
                manager = assembly.GetType(typeName);
                if (manager != null) { break; }
            }
            if (manager == null) {
                Logger.LogWarning("Expand World Data detected but its BiomeManager was not found; custom biomes will only update at world start, and terrain fallbacks and custom lava will not work.");
                return;
            }

            MethodInfo terrain = manager.GetMethod("GetTerrain", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Heightmap.Biome) }, null);
            if (terrain != null && terrain.ReturnType == typeof(Heightmap.Biome)) {
                getTerrain = (Func<Heightmap.Biome, Heightmap.Biome>)Delegate.CreateDelegate(typeof(Func<Heightmap.Biome, Heightmap.Biome>), terrain);
            } else {
                Logger.LogWarning("Expand World Data detected but BiomeManager.GetTerrain was not found; UseTerrainAsBiomeFallback will do nothing.");
            }

            lavaBiomes = manager.GetField("LavaBiomes", BindingFlags.Public | BindingFlags.Static);
            if (lavaBiomes == null || lavaBiomes.FieldType != typeof(Heightmap.Biome)) {
                lavaBiomes = null;
                Logger.LogWarning("Expand World Data detected but BiomeManager.LavaBiomes was not found; only the Ashlands will count as having lava.");
            }
            Logger.LogInfo("Expand World Data detected; its custom biomes can be named in Star Level System config files.");
        }

        internal static void ApplyPatches(Harmony harmony) {
            if (manager == null) { return; }
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(ExpandWorldDataBiomes), nameof(AfterTableChange)));
            foreach (string methodName in TableChangeMethods) {
                MethodInfo target = AccessTools.Method(manager, methodName);
                if (target == null) {
                    Logger.LogWarning($"Expand World Data's BiomeManager.{methodName} was not found; a change it makes is only noticed at world start.");
                    continue;
                }
                harmony.Patch(target, postfix: postfix);
            }
        }

        private static void AfterTableChange() {
            BiomeRegistry.Refresh();
        }

        internal static Heightmap.Biome TerrainOf(Heightmap.Biome biome) {
            return getTerrain != null ? getTerrain(biome) : biome;
        }

        internal static Heightmap.Biome LavaBiomes() {
            return lavaBiomes != null ? (Heightmap.Biome)lavaBiomes.GetValue(null) : Heightmap.Biome.AshLands;
        }
    }
}
