using BepInEx.Bootstrap;
using System;
using System.Reflection;

namespace StarLevelSystem.common {

    // The world's real size. WorldGenerator.worldSize and waterEdge are const, so they compile into this mod as 10000 and
    // 10500, and no mod can change them here; Expand World Size patches the game's own uses of them instead.
    //
    // Its unstretched values, because those are real metres. Its stretch setting spreads the generated world over that same
    // area by dividing positions before world generation sees them, so its "stretched" radius is in generation's coordinates.
    internal static class WorldSize {
        private const string PluginGuid = "expand_world_size";
        private const string ConfigurationTypeName = "ExpandWorldSize.Configuration";

        private static bool resolved;
        private static Func<float> readRadius;
        private static Func<float> readTotalRadius;

        // How far land can reach from the world's centre.
        internal static float Radius => Read(ref readRadius, WorldGenerator.worldSize);

        // Land plus the ocean around it, where the world ends.
        internal static float TotalRadius => Read(ref readTotalRadius, WorldGenerator.waterEdge);

        // Bigger or smaller than vanilla. What depends on this stays exactly as it was in a vanilla-sized world.
        internal static bool IsResized => Radius != WorldGenerator.worldSize;

        private static float Read(ref Func<float> reader, float vanilla) {
            if (resolved == false) { Resolve(); }
            if (reader == null) { return vanilla; }
            try {
                float value = reader();
                return value > 0f ? value : vanilla;
            } catch (Exception e) {
                // Its settings are read from a table filled when it loads, so one bad read would repeat on every call.
                reader = null;
                Logger.LogWarning($"Could not read the world size from Expand World Size, using vanilla's: {e.Message}");
                return vanilla;
            }
        }

        // Asked for during play, once every plugin has loaded. Earlier a mod that is installed may simply not have loaded
        // yet, so a miss only counts as final once a world exists.
        private static void Resolve() {
            if (Chainloader.PluginInfos.TryGetValue(PluginGuid, out BepInEx.PluginInfo info) == false || info.Instance == null) {
                resolved = ZNet.instance != null;
                return;
            }
            resolved = true;
            Type configuration = info.Instance.GetType().Assembly.GetType(ConfigurationTypeName);
            readRadius = BindGetter(configuration, "WorldRadius");
            readTotalRadius = BindGetter(configuration, "WorldTotalRadius");
            if (readRadius == null || readTotalRadius == null) {
                Logger.LogWarning("Expand World Size detected but its world size settings were not found; Star Level System will treat this world as vanilla sized.");
                readRadius = null;
                readTotalRadius = null;
            }
        }

        private static Func<float> BindGetter(Type type, string property) {
            MethodInfo getter = type?.GetProperty(property, BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
            return getter != null && getter.ReturnType == typeof(float) ? (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), getter) : null;
        }
    }
}
