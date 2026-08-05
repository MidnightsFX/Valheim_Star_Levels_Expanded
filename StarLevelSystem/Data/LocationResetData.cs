using StarLevelSystem.common;
using StarLevelSystem.modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data {
    // Owns the LocationResetSettings.yaml configuration and the derived lookups the sweep uses.
    // Nothing here touches game state; see modules/LocationReset for that.
    public static class LocationResetData {

        public static LocationResetConfiguration SLE_LocationReset_Settings = DefaultConfiguration;

        // Never resettable, regardless of configuration. StartTemple is the world spawn point:
        // resetting it can strand every player on the server.
        internal static readonly HashSet<string> HardBlockedLocations = new HashSet<string>() {
            "StartTemple",
        };

        // Shipped disabled because they hold one-time or vendor content players do not expect to
        // churn. Admins can still opt in.
        internal static readonly string[] DefaultDisabledLocations = new string[] {
            "BogWitch_Camp",
            "Hildir_camp",
            "Vendor_BlackForest",
        };

        // Boss altars. Default off; when enabled they default to TerrainOnly, since the usual reason
        // to touch them is to undo the crater players dig around the summoning circle.
        internal static readonly string[] BossAltarLocations = new string[] {
            "Eikthyrnir",
            "GDKing",
            "Bonemass",
            "Dragonqueen",
            "GoblinKing",
            "Mistlands_DvergrBossEntrance1",
            "FaderLocation",
        };

        // Resolved once per config load so the sweep never re-parses per zone.
        internal static readonly Dictionary<int, ResolvedResetEntry> VegetationByPrefabHash = new Dictionary<int, ResolvedResetEntry>();
        internal static readonly Dictionary<int, ResolvedResetEntry> LocationsByHash = new Dictionary<int, ResolvedResetEntry>();
        internal static readonly HashSet<int> ExtraProtectedPrefabHashes = new HashSet<int>();

        // Shortest enabled interval across every target, in seconds. A zone is only considered due
        // when this much time has passed since its stamp, which keeps the due-queue scan cheap.
        internal static float MinEnabledIntervalSeconds = float.MaxValue;

        // Set when a conflicting reset mod is installed. Hard-gates the sweep independently of the
        // yaml Enabled flag so nothing can turn it back on mid-session.
        internal static bool BlockedByModConflict = false;

        internal static bool SweepEnabled {
            get { return SLE_LocationReset_Settings.Enabled && BlockedByModConflict == false; }
        }

        // A single target with its fallbacks already folded in.
        internal class ResolvedResetEntry {
            internal string Name;
            internal int PrefabHash;
            internal bool Enabled;
            internal float ResetSeconds;
            internal LocationResetMode Mode;
            internal bool ResetTerrain;
            internal float TerrainRadius;
            internal bool ResetInterior;
            internal Dictionary<ProtectionCategory, ProtectionAction> Protection;

            internal ProtectionAction ActionFor(ProtectionCategory category) {
                if (Protection != null && Protection.TryGetValue(category, out ProtectionAction action)) { return action; }
                return ProtectionAction.Block;
            }
        }

        public static readonly LocationResetConfiguration DefaultConfiguration = new LocationResetConfiguration() {
            Enabled = false,
            PlayerSafeRadius = 256f,
            StampOnFirstSight = true,
            MaxZoneLoadWaitSeconds = 10f,
            Throughput = new LocationResetThroughput(),
            Defaults = new LocationResetDefaults(),
            InPlaceRefresh = new LocationResetInPlace(),
            Locations = new Dictionary<string, LocationResetEntry>(),
            Vegetation = new Dictionary<string, LocationResetEntry>(),
            ProtectedPrefabs = new List<string>(),
        };

        internal static void Init() {
            SLE_LocationReset_Settings = DefaultConfiguration;
            try {
                if (File.Exists(ValConfig.locationResetFilePath)) {
                    UpdateYamlConfig(File.ReadAllText(ValConfig.locationResetFilePath));
                } else {
                    Rebuild();
                }
            }
            catch (Exception e) { Jotunn.Logger.LogWarning($"There was an error updating the Location Reset values, defaults will be used. Exception: {e}"); }
        }

        public static string YamlDefaultConfig() {
            return DataObjects.yamlSerializer.Serialize(DefaultConfiguration);
        }

        public static bool UpdateYamlConfig(string yaml) {
            try {
                Logger.LogDebug("Loaded new Location Reset settings...");
                SLE_LocationReset_Settings = DataObjects.yamlDeserializer.Deserialize<LocationResetConfiguration>(yaml);
                if (SLE_LocationReset_Settings == null) { SLE_LocationReset_Settings = DefaultConfiguration; }
                Rebuild();
            }
            catch (Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse LocationResetSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }

        // Flatten the config into the hash-keyed lookups the sweep uses, applying Defaults for any
        // unset per-entry value. Safe to call before ZoneSystem exists; prefab hashes are computed
        // from names so no game state is required.
        internal static void Rebuild() {
            LocationResetConfiguration cfg = SLE_LocationReset_Settings;
            if (cfg == null) { return; }
            if (cfg.Defaults == null) { cfg.Defaults = new LocationResetDefaults(); }
            if (cfg.Defaults.Protection == null) { cfg.Defaults.Protection = LocationResetDefaults.DefaultProtection(); }
            if (cfg.Throughput == null) { cfg.Throughput = new LocationResetThroughput(); }
            if (cfg.InPlaceRefresh == null) { cfg.InPlaceRefresh = new LocationResetInPlace(); }

            LocationsByHash.Clear();
            VegetationByPrefabHash.Clear();
            ExtraProtectedPrefabHashes.Clear();
            MinEnabledIntervalSeconds = float.MaxValue;

            if (cfg.Locations != null) {
                foreach (KeyValuePair<string, LocationResetEntry> kvp in cfg.Locations) {
                    if (HardBlockedLocations.Contains(kvp.Key)) {
                        if (kvp.Value != null && kvp.Value.Enabled) {
                            Logger.LogWarning($"[LocationReset] '{kvp.Key}' cannot be reset and will be ignored even though it is enabled in the config.");
                        }
                        continue;
                    }
                    ResolvedResetEntry resolved = Resolve(kvp.Key, kvp.Value, cfg.Defaults);
                    LocationsByHash[resolved.PrefabHash] = resolved;
                    TrackInterval(resolved);
                }
            }

            if (cfg.Vegetation != null) {
                foreach (KeyValuePair<string, LocationResetEntry> kvp in cfg.Vegetation) {
                    ResolvedResetEntry resolved = Resolve(kvp.Key, kvp.Value, cfg.Defaults);
                    VegetationByPrefabHash[resolved.PrefabHash] = resolved;
                    TrackInterval(resolved);
                }
            }

            if (cfg.ProtectedPrefabs != null) {
                foreach (string prefab in cfg.ProtectedPrefabs) {
                    if (string.IsNullOrEmpty(prefab)) { continue; }
                    ExtraProtectedPrefabHashes.Add(prefab.GetStableHashCode());
                }
            }

            if (MinEnabledIntervalSeconds == float.MaxValue) { MinEnabledIntervalSeconds = 0f; }

            Logger.LogLocationReset($"Config rebuilt: {LocationsByHash.Values.Count(e => e.Enabled)} enabled locations, " +
                $"{VegetationByPrefabHash.Values.Count(e => e.Enabled)} enabled vegetation entries, " +
                $"min interval {MinEnabledIntervalSeconds / 3600f:0.##}h, sweep enabled: {SweepEnabled}.");
        }

        private static void TrackInterval(ResolvedResetEntry entry) {
            if (entry.Enabled == false) { return; }
            if (entry.ResetSeconds > 0f && entry.ResetSeconds < MinEnabledIntervalSeconds) {
                MinEnabledIntervalSeconds = entry.ResetSeconds;
            }
        }

        private static ResolvedResetEntry Resolve(string name, LocationResetEntry entry, LocationResetDefaults defaults) {
            if (entry == null) { entry = new LocationResetEntry(); }

            Dictionary<ProtectionCategory, ProtectionAction> protection =
                new Dictionary<ProtectionCategory, ProtectionAction>(defaults.Protection);
            if (entry.Protection != null) {
                foreach (KeyValuePair<ProtectionCategory, ProtectionAction> over in entry.Protection) {
                    protection[over.Key] = over.Value;
                }
            }

            float hours = entry.ResetHours ?? defaults.ResetHours;

            return new ResolvedResetEntry() {
                Name = name,
                PrefabHash = name.GetStableHashCode(),
                Enabled = entry.Enabled,
                ResetSeconds = Math.Max(0f, hours) * 3600f,
                Mode = entry.Mode,
                ResetTerrain = entry.ResetTerrain ?? defaults.ResetTerrain,
                TerrainRadius = entry.TerrainRadius ?? defaults.TerrainRadius,
                ResetInterior = entry.ResetInterior,
                Protection = protection,
            };
        }

        internal static bool TryGetVegetationEntry(int prefabHash, out ResolvedResetEntry entry) {
            return VegetationByPrefabHash.TryGetValue(prefabHash, out entry);
        }

        internal static bool TryGetLocationEntry(int locationHash, out ResolvedResetEntry entry) {
            return LocationsByHash.TryGetValue(locationHash, out entry);
        }

        // Build a complete config from whatever locations and vegetation this server actually has
        // loaded, so the generated yaml covers modded entries too. Falls back to a bare default when
        // ZoneSystem is not up yet (e.g. first launch before a world is loaded).
        internal static LocationResetConfiguration BuildPopulatedDefault() {
            LocationResetConfiguration cfg = new LocationResetConfiguration();
            if (ZoneSystem.instance == null) { return cfg; }

            HashSet<string> bossAltars = new HashSet<string>(BossAltarLocations);
            HashSet<string> disabled = new HashSet<string>(DefaultDisabledLocations);

            if (ZoneSystem.instance.m_locations != null) {
                foreach (ZoneSystem.ZoneLocation location in ZoneSystem.instance.m_locations) {
                    if (location == null) { continue; }
                    // ZoneLocation.Hash is GetStableHashCode(m_prefab.Name), and that is what
                    // LocationProxy stores in ZDOVars.s_location. Key off the same name so the
                    // config hash and the in-world hash always agree. m_prefabName is only
                    // populated for enabled locations at SetupLocations time, so prefer m_prefab.Name.
                    string name = location.m_prefab.Name;
                    if (string.IsNullOrEmpty(name)) { name = location.m_prefabName; }
                    if (string.IsNullOrEmpty(name)) { continue; }
                    if (HardBlockedLocations.Contains(name)) { continue; }
                    if (cfg.Locations.ContainsKey(name)) { continue; }

                    LocationResetEntry entry = new LocationResetEntry() { Enabled = false };
                    if (bossAltars.Contains(name)) {
                        // Terrain-only by default: the point is undoing terraforming, not re-rolling the altar.
                        entry.Mode = LocationResetMode.TerrainOnly;
                        entry.ResetTerrain = true;
                    } else if (disabled.Contains(name)) {
                        entry.Enabled = false;
                    }
                    cfg.Locations[name] = entry;
                }
            }

            if (ZoneSystem.instance.m_vegetation != null) {
                foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation) {
                    if (veg == null || veg.m_prefab == null) { continue; }
                    string name = veg.m_prefab.name;
                    if (string.IsNullOrEmpty(name) || cfg.Vegetation.ContainsKey(name)) { continue; }
                    cfg.Vegetation[name] = new LocationResetEntry() { Enabled = false };
                }
            }

            return cfg;
        }
    }
}
