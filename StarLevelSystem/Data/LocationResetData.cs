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
        // resetting it can strand every player on the server and reset trophy progress.
        internal static readonly HashSet<string> HardBlockedLocations = new HashSet<string>() {
            "StartTemple",
        };

        // Boss altars, shipped as the BossAltars group in DefaultResetGroups(). Enabled by default in
        // Full mode with the terrain reset on, because the usual reason to touch one is to undo the
        // crater players dig around the summoning circle. Nothing resets until both master switches
        // (EnableLocationReset and the yaml Enabled) are on.
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

        // Throughput and InPlaceRefresh are omitted from the generated config (see
        // LocationResetConfiguration), so they are null on any file that never mentioned them. These
        // hand out a shared all-defaults instance instead, which is why nothing writes the fallback
        // back onto the config -- doing so would resurrect `Throughput: {}` on the next rewrite.
        private static readonly LocationResetThroughput ThroughputFallback = new LocationResetThroughput();
        private static readonly LocationResetInPlace InPlaceFallback = new LocationResetInPlace();
        internal static LocationResetThroughput Throughput {
            get { return SLE_LocationReset_Settings?.Throughput ?? ThroughputFallback; }
        }
        internal static LocationResetInPlace InPlaceRefresh {
            get { return SLE_LocationReset_Settings?.InPlaceRefresh ?? InPlaceFallback; }
        }

        // Shortest enabled interval across every target, in seconds. A zone is only considered due
        // when this much time has passed since its stamp, which keeps the due-queue scan cheap.
        internal static float MinEnabledIntervalSeconds = float.MaxValue;

        // Defaults.ResetSchedule, parsed once per config load. Null when Defaults uses ResetHours.
        // Only the fallback path matters here -- prefabs with no config entry of their own, which are
        // timed off the zone stamp rather than a resolved entry.
        internal static CronSchedule DefaultSchedule { get; private set; }

        // Ceiling on ExtraTerrainRadius, and not an arbitrary number: ScanZone covers the chunk plus
        // its 8 neighbours (+/-96m from the chunk centre) and a location sits within 32m of that
        // centre, so 64m is the largest extra reach the protection scan provably covers. Past it we
        // would be flattening ground that was never checked for player property.
        internal const float MaxExtraTerrainRadius = 64f;

        // Set when a conflicting reset mod is installed. Hard-gates the sweep independently of the
        // yaml Enabled flag so nothing can turn it back on mid-session.
        internal static bool BlockedByModConflict = false;

        // The yaml master switch. Nullable on disk so it stays visible in the generated file; absent
        // means OFF, unlike a reset group, because this is the switch that lets the sweep delete
        // things at all.
        internal static bool ConfigEnabled {
            get { return SLE_LocationReset_Settings?.Enabled.GetValueOrDefault(false) ?? false; }
        }

        internal static bool SweepEnabled {
            get { return ConfigEnabled && BlockedByModConflict == false; }
        }

        // A single target with its fallbacks already folded in.
        internal class ResolvedResetEntry {
            internal string Name;
            internal int PrefabHash;
            internal bool Enabled;
            internal float ResetSeconds;
            // Set instead of ResetSeconds when this target is on a cron schedule. Never both: the
            // two are resolved as one unit by PickFrequency.
            internal CronSchedule Schedule;
            internal LocationResetMode Mode;
            internal bool ResetTerrain;
            internal float TerrainRadius;
            internal float ExtraTerrainRadius;
            internal bool ResetInterior;
            internal Dictionary<ProtectionCategory, ProtectionRule> Protection;
            // Group this resolution came from, for the chunk log. Null when nothing but the entry and
            // Defaults were involved.
            internal string GroupName;

            // Distance scope, only meaningful on a scoped variant. MaxDistance 0 = no outer limit.
            internal float MinDistance;
            internal float MaxDistance;

            // Distance-scoped alternatives, shortest interval first. Null in the common case, which
            // keeps ForDistance free for every target no scoped group covers.
            internal List<ResolvedResetEntry> Scoped;

            // The resolution that applies at this distance from the reset centre: the first scoped
            // variant whose range contains it, otherwise this (unscoped) entry. Selection has to
            // happen per chunk rather than once at config load, because a target can be covered by an
            // unscoped group AND a tighter scoped one -- flint is in Foraging everywhere and in
            // FlintNearSpawn only within 3000m.
            internal ResolvedResetEntry ForDistance(float distance) {
                if (Scoped == null) { return this; }
                for (int i = 0; i < Scoped.Count; i++) {
                    ResolvedResetEntry variant = Scoped[i];
                    if (distance < variant.MinDistance) { continue; }
                    if (variant.MaxDistance > 0f && distance >= variant.MaxDistance) { continue; }
                    return variant;
                }
                return this;
            }

            // The single due-check for every tier. Was five copies of
            // "Now - stamp >= modules.LocationReset.ZoneRates.ScaleSeconds(ResetSeconds, rate)" before cron existed.
            internal bool IsDue(long stamp, long now, float rate) {
                // An excluded biome or band means never, on both kinds of schedule.
                if (rate <= modules.LocationReset.ZoneRates.Excluded) { return false; }
                // Biome and band multipliers scale an interval, and there is nothing coherent to
                // scale on a calendar time -- halving "every Tuesday at 3am" is meaningless. A cron
                // target therefore ignores the multipliers and honours only the exclusion above.
                if (Schedule != null) { return Schedule.HasElapsedSince(stamp, now); }
                return now - stamp >= modules.LocationReset.ZoneRates.ScaleSeconds(ResetSeconds, rate);
            }

            // How long until this target could next come due, for the chunk log. Meaningless for
            // cron, which is why callers use DescribeSchedule instead of formatting hours.
            internal string DescribeSchedule(long now, float rate) {
                if (Schedule != null) { return Schedule.Describe(now); }
                return $"{modules.LocationReset.ZoneRates.ScaleSeconds(ResetSeconds, rate) / 3600f:0.#}h";
            }

            internal ProtectionAction ActionFor(ProtectionCategory category) {
                if (Protection != null && Protection.TryGetValue(category, out ProtectionRule rule) && rule != null) { return rule.Action; }
                return ProtectionAction.Block;
            }

            internal bool Ignores(ProtectionCategory category, int prefabHash) {
                if (Protection != null && Protection.TryGetValue(category, out ProtectionRule rule) && rule != null) {
                    return rule.IgnoresHash(prefabHash);
                }
                return false;
            }
        }

        // Throughput and InPlaceRefresh are left null on purpose; read them through the accessors
        // above. Rebuild() must never fill them in here -- this is a shared static instance that
        // Init() hands straight to SLE_LocationReset_Settings.
        public static readonly LocationResetConfiguration DefaultConfiguration = new LocationResetConfiguration() {
            Enabled = false,
            PlayerSafeRadius = 256f,
            StampOnFirstSight = true,
            MaxZoneLoadWaitSeconds = 10f,
            Defaults = new LocationResetDefaults(),
            Locations = new Dictionary<string, LocationResetEntry>(),
            Vegetation = new Dictionary<string, LocationResetEntry>(),
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

        // Append a batch of chunk records to the Location Reset action log. Same shape as
        // NemesisSystemData.UpdateNemesisLog: the caller buffers and hands over a whole batch, and the
        // file is truncated rather than rotated once it gets large. The cap is well below the Nemesis
        // one because a sweep produces far more lines than Nemesis actions do.
        internal static void UpdateLocationResetLog(string data) {
            const long maxLogSizeBytes = 32L * 1024L * 1024L; // 32 MB
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                FileInfo logInfo = new FileInfo(ValConfig.locationResetLogFilePath);
                if (logInfo.Exists && logInfo.Length > maxLogSizeBytes) {
                    Logger.LogLocationReset($"Location Reset log exceeded {maxLogSizeBytes} bytes, overwriting with the most recent batch.");
                    File.WriteAllText(ValConfig.locationResetLogFilePath, data);
                    return;
                }
                File.AppendAllText(ValConfig.locationResetLogFilePath, data);
            } catch (Exception e) {
                // Logging must never take the sweep down with it.
                Logger.LogLocationResetWarning($"Failed to write the Location Reset log: {e.Message}");
            }
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
        // from names so no game state is required. Group-only targets need the world catalogue and so
        // only materialise on the world-ready pass -- see AddGroupOnlyTargets.
        //
        // Never writes fallback objects back onto cfg: SLE_LocationReset_Settings can be the shared
        // DefaultConfiguration instance, and a filled-in Throughput would be re-serialized as
        // `Throughput: {}` the next time the file is rewritten.
        internal static void Rebuild() {
            LocationResetConfiguration cfg = SLE_LocationReset_Settings;
            if (cfg == null) { return; }
            if (cfg.Defaults == null) { cfg.Defaults = new LocationResetDefaults(); }
            if (cfg.Defaults.Protection == null) { cfg.Defaults.Protection = LocationResetDefaults.DefaultProtection(); }

            LocationsByHash.Clear();
            VegetationByPrefabHash.Clear();
            ExtraProtectedPrefabHashes.Clear();
            MinEnabledIntervalSeconds = float.MaxValue;
            // Both are per-load: a reload must re-parse whatever the file now says, and must warn
            // again about anything still wrong with it.
            scheduleCache.Clear();
            scheduleWarnings.Clear();
            DefaultSchedule = ParseSchedule(cfg.Defaults.ResetSchedule, "Defaults", "Defaults");

            BuildWorldCatalogIndex();
            GroupMembership membership = ExpandGroups(cfg);

            if (cfg.Locations != null) {
                foreach (KeyValuePair<string, LocationResetEntry> kvp in cfg.Locations) {
                    if (HardBlockedLocations.Contains(kvp.Key)) {
                        if (kvp.Value != null && kvp.Value.Enabled) {
                            Logger.LogLocationResetWarning($"'{kvp.Key}' cannot be reset and will be ignored even though it is enabled in the config.");
                        }
                        continue;
                    }
                    ResolvedResetEntry resolved = ResolveWithGroups(kvp.Key, kvp.Value, cfg.Defaults, membership);
                    LocationsByHash[resolved.PrefabHash] = resolved;
                    TrackInterval(resolved);
                }
            }

            if (cfg.Vegetation != null) {
                foreach (KeyValuePair<string, LocationResetEntry> kvp in cfg.Vegetation) {
                    ResolvedResetEntry resolved = ResolveWithGroups(kvp.Key, kvp.Value, cfg.Defaults, membership);
                    VegetationByPrefabHash[resolved.PrefabHash] = resolved;
                    TrackInterval(resolved);
                }
            }

            AddGroupOnlyTargets(cfg, membership);
            membership.WarnOnUnmatchedMembers();

            if (cfg.ProtectedPrefabs != null) {
                foreach (string prefab in cfg.ProtectedPrefabs) {
                    if (string.IsNullOrEmpty(prefab)) { continue; }
                    ExtraProtectedPrefabHashes.Add(prefab.GetStableHashCode());
                }
            }

            if (MinEnabledIntervalSeconds == float.MaxValue) { MinEnabledIntervalSeconds = 0f; }

            WarnOnProtectionConflicts(cfg);
            WarnOnOversizedTerrainRadius();

            Logger.LogLocationReset($"Config rebuilt: {LocationsByHash.Values.Count(e => e.Enabled)} enabled locations, " +
                $"{VegetationByPrefabHash.Values.Count(e => e.Enabled)} enabled vegetation entries, " +
                $"min interval {MinEnabledIntervalSeconds / 3600f:0.##}h, sweep enabled: {SweepEnabled}.");
        }

        // A prefab listed in both ProtectedPrefabs and a category's Ignored list is contradictory.
        // Protection wins (fail closed), but say so rather than letting an admin believe the ignore
        // took effect and wonder why chunks are still blocked.
        private static void WarnOnProtectionConflicts(LocationResetConfiguration cfg) {
            if (cfg.ProtectedPrefabs == null || cfg.Defaults?.Protection == null) { return; }
            foreach (string prefab in cfg.ProtectedPrefabs) {
                if (string.IsNullOrWhiteSpace(prefab)) { continue; }
                int hash = prefab.Trim().GetStableHashCode();
                foreach (KeyValuePair<ProtectionCategory, ProtectionRule> kvp in cfg.Defaults.Protection) {
                    if (kvp.Value == null || kvp.Value.IgnoresHash(hash) == false) { continue; }
                    Logger.LogLocationResetWarning($"'{prefab}' is in ProtectedPrefabs and also ignored under " +
                        $"{kvp.Key}. Protection wins: it will keep blocking resets. Remove it from one of the two.");
                }
            }
        }

        private static void WarnOnOversizedTerrainRadius() {
            foreach (ResolvedResetEntry entry in LocationsByHash.Values) {
                if (entry.Enabled == false || entry.ExtraTerrainRadius <= MaxExtraTerrainRadius) { continue; }
                Logger.LogLocationResetWarning($"'{entry.Name}' sets ExtraTerrainRadius {entry.ExtraTerrainRadius:0}m, " +
                    $"above the {MaxExtraTerrainRadius:0}m limit the protection scan covers. It will be clamped to {MaxExtraTerrainRadius:0}m.");
            }
        }

        // Default-path ignore lookup, for the zone-wide protection scan which runs before any specific
        // location entry is known and therefore consults Defaults.Protection.
        internal static bool DefaultIgnores(ProtectionCategory category, int prefabHash) {
            Dictionary<ProtectionCategory, ProtectionRule> defaults = SLE_LocationReset_Settings?.Defaults?.Protection;
            if (defaults != null && defaults.TryGetValue(category, out ProtectionRule rule) && rule != null) {
                return rule.IgnoresHash(prefabHash);
            }
            return false;
        }

        // Every prefab hash ignored under any category, for the pre-vegetation sweep, which has to
        // decide before it knows which category a given ZDO would classify as.
        internal static bool AnyCategoryIgnores(int prefabHash) {
            Dictionary<ProtectionCategory, ProtectionRule> defaults = SLE_LocationReset_Settings?.Defaults?.Protection;
            if (defaults == null) { return false; }
            foreach (KeyValuePair<ProtectionCategory, ProtectionRule> kvp in defaults) {
                if (kvp.Value != null && kvp.Value.IgnoresHash(prefabHash)) { return true; }
            }
            return false;
        }

        private static void TrackInterval(ResolvedResetEntry entry) {
            if (entry.Enabled) {
                float floor = FloorContribution(entry);
                if (floor > 0f && floor < MinEnabledIntervalSeconds) { MinEnabledIntervalSeconds = floor; }
            }
            // Scoped variants count too, and are checked even when the base is disabled: a prefab
            // covered ONLY by a distance-scoped group has a disabled base by design, and skipping its
            // variants here would leave the 6h group sitting behind a 72h floor, never coming due.
            if (entry.Scoped == null) { return; }
            for (int i = 0; i < entry.Scoped.Count; i++) { TrackInterval(entry.Scoped[i]); }
        }

        // How low this target needs the sweep's examination floor to be. A cron target has no
        // interval, so it contributes the tightest gap its expression can produce -- without this it
        // would contribute nothing and an hourly cron would sit behind, say, FlintNearSpawn's 6h
        // floor and never fire.
        //
        // The slack matters: the floor is measured from ZoneStamp (when the zone was last EXAMINED)
        // while cron fires against the per-target stamp. A daily 03:00 whose zone was examined at
        // 03:05 is 23h55m from its own fire, so an exact 24h floor would hold it back another five
        // minutes every single day.
        private const float CronFloorSlackSeconds = 600f;

        private static float FloorContribution(ResolvedResetEntry entry) {
            if (entry.Schedule == null) { return entry.ResetSeconds; }
            return Math.Max(60f, entry.Schedule.MinGapSeconds - CronFloorSlackSeconds);
        }

        // ------------------------------------------------------------------------------------------
        // World catalogue index
        // ------------------------------------------------------------------------------------------
        //
        // What this world can actually place, by name hash. Two jobs, both of which used to be done
        // by the exhaustive Locations/Vegetation lists in the config file:
        //
        //   1. Tell a group member apart as a location (found by LocationInstance.m_location.Hash)
        //      or a prefab-hash ZDO (found by ZDO.m_prefab). The two lookups are not interchangeable.
        //   2. Decide whether a member name matched anything at all, which is the check that catches
        //      a game update renaming a prefab out from under a curated list.
        private static readonly HashSet<int> KnownLocationHashes = new HashSet<int>();
        private static readonly HashSet<int> KnownVegetationHashes = new HashSet<int>();
        private static readonly Dictionary<int, string> KnownNames = new Dictionary<int, string>();
        private static bool worldCatalogBuilt = false;

        // Needs BOTH ZoneSystem (placement lists) and ZNetScene (prefabs). Requiring both matters:
        // with ZNetScene missing, every dungeon-only pickable would look like a member that matched
        // nothing and produce a false warning. A no-op before then, and LocationResetControl re-runs
        // Rebuild at world ready -- the same pass that resolves $Mineable / $Pickable.
        internal static void BuildWorldCatalogIndex() {
            if (worldCatalogBuilt) { return; }
            if (ZoneSystem.instance == null || ZNetScene.instance == null) { return; }

            KnownLocationHashes.Clear();
            KnownVegetationHashes.Clear();
            KnownNames.Clear();
            modules.LocationReset.ZoneProtectionScan.BuildPrefabSets();

            if (ZoneSystem.instance.m_locations != null) {
                foreach (ZoneSystem.ZoneLocation location in ZoneSystem.instance.m_locations) {
                    if (location == null) { continue; }
                    string name = SafePrefabName(location);
                    if (string.IsNullOrEmpty(name)) { continue; }
                    int hash = name.GetStableHashCode();
                    KnownLocationHashes.Add(hash);
                    KnownNames[hash] = name;
                }
            }

            if (ZoneSystem.instance.m_vegetation != null) {
                foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation) {
                    if (veg == null || veg.m_prefab == null) { continue; }
                    string name = veg.m_prefab.name;
                    if (string.IsNullOrEmpty(name)) { continue; }
                    int hash = name.GetStableHashCode();
                    KnownVegetationHashes.Add(hash);
                    KnownNames[hash] = name;
                }
            }

            worldCatalogBuilt = true;
            Logger.LogLocationReset($"World catalogue indexed: {KnownLocationHashes.Count} locations, " +
                $"{KnownVegetationHashes.Count} vegetation prefabs.");
        }

        internal static void ResetWorldCatalogIndex() {
            KnownLocationHashes.Clear();
            KnownVegetationHashes.Clear();
            KnownNames.Clear();
            worldCatalogBuilt = false;
        }

        // A name this world has something for: a placeable location, placeable vegetation, or any
        // registered prefab. The last case covers dungeon-only pickables and mine rocks, which never
        // appear in ZoneSystem's placement lists but are perfectly valid reset targets.
        internal static bool IsKnownTargetName(int prefabHash) {
            if (KnownLocationHashes.Contains(prefabHash) || KnownVegetationHashes.Contains(prefabHash)) { return true; }
            return modules.LocationReset.ZoneProtectionScan.PrefabNamesByHash.ContainsKey(prefabHash);
        }

        private static string ResolveKnownName(int prefabHash) {
            if (KnownNames.TryGetValue(prefabHash, out string name)) { return name; }
            if (modules.LocationReset.ZoneProtectionScan.PrefabNamesByHash.TryGetValue(prefabHash, out string prefabName)) { return prefabName; }
            return null;
        }

        // Groups stand on their own: a member resolves whether or not Locations/Vegetation carries a
        // key for it, which is what lets the generated config ship with both lists empty instead of
        // one entry per prefab in the world. A per-entry key still wins where one exists -- those
        // were already resolved by the two passes above and are skipped here.
        private static void AddGroupOnlyTargets(LocationResetConfiguration cfg, GroupMembership membership) {
            if (worldCatalogBuilt == false) { return; }

            foreach (KeyValuePair<int, List<KeyValuePair<string, LocationResetGroup>>> kvp in membership.ByPrefab) {
                int hash = kvp.Key;
                bool isLocation = KnownLocationHashes.Contains(hash);
                bool isVegetation = KnownVegetationHashes.Contains(hash);
                if (isLocation == false && isVegetation == false) {
                    // Neither placement list mentions it. If a prefab exists it is something like a
                    // dungeon pickable, which the sweep finds by ZDO prefab hash -- the vegetation
                    // lookup. If no prefab exists either, WarnOnUnmatchedMembers already covers it.
                    //
                    // Named members only. A $token must not reach past the placement lists: it is a
                    // wildcard shipped ENABLED (Foraging), and quietly extending it to every Pickable
                    // in the game would start regrowing one-off dungeon and quest pickups.
                    if (membership.NamedMembers.Contains(hash) == false) { continue; }
                    if (modules.LocationReset.ZoneProtectionScan.PrefabNamesByHash.ContainsKey(hash) == false) { continue; }
                    isVegetation = true;
                }

                string name = ResolveKnownName(hash);
                if (string.IsNullOrEmpty(name)) { continue; }
                if (isLocation && HardBlockedLocations.Contains(name)) { isLocation = false; }

                bool needLocation = isLocation && LocationsByHash.ContainsKey(hash) == false;
                bool needVegetation = isVegetation && VegetationByPrefabHash.ContainsKey(hash) == false;
                if (needLocation == false && needVegetation == false) { continue; }

                // A name registered in both catalogues (LeviathanLava) shares one resolution rather
                // than resolving twice -- ResolvedResetEntry is immutable once built, and a second
                // ResolveWithGroups call would repeat its overlapping-groups warning.
                if (LocationsByHash.TryGetValue(hash, out ResolvedResetEntry resolved) == false
                        && VegetationByPrefabHash.TryGetValue(hash, out resolved) == false) {
                    resolved = ResolveWithGroups(name, null, cfg.Defaults, membership);
                    TrackInterval(resolved);
                }

                if (needLocation) { LocationsByHash[hash] = resolved; }
                if (needVegetation) { VegetationByPrefabHash[hash] = resolved; }
            }
        }

        // Category tokens usable in a group's Members list. They resolve from the component-derived
        // prefab sets the protection scan already builds, so they cover modded content that a
        // hand-written name list never would.
        private const string MineableToken = "$Mineable";
        private const string PickableToken = "$Pickable";

        // Which groups claim which prefab, built once per config load.
        private class GroupMembership {
            internal readonly Dictionary<int, List<KeyValuePair<string, LocationResetGroup>>> ByPrefab = new Dictionary<int, List<KeyValuePair<string, LocationResetGroup>>>();
            // group name -> member names this world has nothing for. Checked against the world
            // catalogue index rather than against the config's own entry lists: now that groups stand
            // alone, "no key in Locations/Vegetation" is the normal case and says nothing about
            // whether the name is real.
            internal readonly Dictionary<string, List<string>> Unmatched = new Dictionary<string, List<string>>();
            // Prefabs a group named outright, as opposed to ones a $token swept up. Only these may
            // become targets that no ZoneSystem placement list mentions -- see AddGroupOnlyTargets.
            internal readonly HashSet<int> NamedMembers = new HashSet<int>();

            internal void Add(int prefabHash, string groupName, LocationResetGroup group) {
                if (ByPrefab.TryGetValue(prefabHash, out List<KeyValuePair<string, LocationResetGroup>> list) == false) {
                    list = new List<KeyValuePair<string, LocationResetGroup>>();
                    ByPrefab[prefabHash] = list;
                }
                list.Add(new KeyValuePair<string, LocationResetGroup>(groupName, group));
            }

            internal void NoteUnmatched(string groupName, string member) {
                if (Unmatched.TryGetValue(groupName, out List<string> list) == false) {
                    list = new List<string>();
                    Unmatched[groupName] = list;
                }
                list.Add(member);
            }

            // A curated member list is exactly what a game update silently breaks by renaming a
            // prefab, so a member that matched nothing has to be loud rather than quietly inert.
            internal void WarnOnUnmatchedMembers() {
                foreach (KeyValuePair<string, List<string>> kvp in Unmatched) {
                    Logger.LogLocationResetWarning($"Reset group '{kvp.Key}' lists {kvp.Value.Count} member(s) that match " +
                        $"nothing in this world and will do nothing: {string.Join(", ", kvp.Value)}");
                }
            }
        }

        private static GroupMembership ExpandGroups(LocationResetConfiguration cfg) {
            GroupMembership membership = new GroupMembership();
            if (cfg.ResetGroups == null) { return membership; }

            foreach (KeyValuePair<string, LocationResetGroup> kvp in cfg.ResetGroups) {
                LocationResetGroup group = kvp.Value;
                if (group == null || group.Members == null) { continue; }
                // A disabled group contributes nothing at all, rather than contributing settings
                // without the enable -- that would be a confusing half-state. Absent means enabled.
                if (group.Enabled.GetValueOrDefault(true) == false) { continue; }

                foreach (string member in group.Members) {
                    if (string.IsNullOrWhiteSpace(member)) { continue; }
                    string trimmed = member.Trim();

                    if (trimmed.StartsWith("$", StringComparison.Ordinal)) {
                        ExpandCategoryToken(trimmed, kvp.Key, group, membership);
                        continue;
                    }
                    int hash = trimmed.GetStableHashCode();
                    membership.Add(hash, kvp.Key, group);
                    membership.NamedMembers.Add(hash);
                    // Only meaningful once the catalogue is indexed; before that everything would
                    // look unmatched.
                    if (worldCatalogBuilt && IsKnownTargetName(hash) == false) {
                        membership.NoteUnmatched(kvp.Key, trimmed);
                    }
                }
            }
            return membership;
        }

        private static void ExpandCategoryToken(string token, string groupName, LocationResetGroup group, GroupMembership membership) {
            // Needs ZNetScene, which does not exist at Awake. LocationResetControl re-runs Rebuild
            // once the world is ready, and that pass is where tokens actually resolve.
            modules.LocationReset.ZoneProtectionScan.BuildPrefabSets();

            HashSet<int> source = null;
            if (string.Equals(token, PickableToken, StringComparison.OrdinalIgnoreCase)) {
                source = modules.LocationReset.ZoneProtectionScan.PickableHashes;
            } else if (string.Equals(token, MineableToken, StringComparison.OrdinalIgnoreCase)) {
                source = modules.LocationReset.ZoneProtectionScan.MineRock5Hashes;
            } else {
                Logger.LogLocationResetWarning($"Reset group '{groupName}' uses unknown category token '{token}'. " +
                    $"Supported tokens are {MineableToken} and {PickableToken}.");
                return;
            }

            foreach (int hash in source) { membership.Add(hash, groupName, group); }
            if (string.Equals(token, MineableToken, StringComparison.OrdinalIgnoreCase)) {
                // Old-style MineRock deposits are tracked in a separate map from MineRock5.
                foreach (int hash in modules.LocationReset.ZoneProtectionScan.MineRockAreaCounts.Keys) {
                    membership.Add(hash, groupName, group);
                }
            }
        }

        // The group layer sits between the entry and Defaults. Groups covering this prefab are split
        // into unscoped (which resolve the base entry) and distance-scoped (which become variants
        // selected per chunk), each ordered shortest-interval-first so overlap resolution is
        // deterministic regardless of Dictionary iteration order -- which .NET does not guarantee.
        private static ResolvedResetEntry ResolveWithGroups(string name, LocationResetEntry entry,
                                                            LocationResetDefaults defaults, GroupMembership membership) {
            int hash = name.GetStableHashCode();
            if (membership.ByPrefab.TryGetValue(hash, out List<KeyValuePair<string, LocationResetGroup>> groups) == false) {
                return Resolve(name, entry, null, defaults);
            }

            List<KeyValuePair<string, LocationResetGroup>> unscoped = new List<KeyValuePair<string, LocationResetGroup>>();
            List<KeyValuePair<string, LocationResetGroup>> scoped = new List<KeyValuePair<string, LocationResetGroup>>();
            for (int i = 0; i < groups.Count; i++) {
                if (IsScoped(groups[i].Value)) { scoped.Add(groups[i]); } else { unscoped.Add(groups[i]); }
            }

            SortByFrequency(unscoped, name, defaults);
            SortByFrequency(scoped, name, defaults);

            if (unscoped.Count > 1) {
                Logger.LogLocationResetWarning($"'{name}' is claimed by {unscoped.Count} groups " +
                    $"({string.Join(", ", unscoped.ConvertAll(g => g.Key))}); using '{unscoped[0].Key}' as it resets most often.");
            }

            LocationResetGroup winner = unscoped.Count > 0 ? unscoped[0].Value : null;
            string winnerName = unscoped.Count > 0 ? unscoped[0].Key : null;
            ResolvedResetEntry resolved = Resolve(name, entry, winner, defaults);
            resolved.GroupName = winnerName;

            for (int i = 0; i < scoped.Count; i++) {
                ResolvedResetEntry variant = Resolve(name, entry, scoped[i].Value, defaults);
                variant.GroupName = scoped[i].Key;
                variant.MinDistance = Math.Max(0f, scoped[i].Value.MinDistance ?? 0f);
                variant.MaxDistance = Math.Max(0f, scoped[i].Value.MaxDistance ?? 0f);
                if (resolved.Scoped == null) { resolved.Scoped = new List<ResolvedResetEntry>(); }
                resolved.Scoped.Add(variant);
            }
            return resolved;
        }

        private static bool IsScoped(LocationResetGroup group) {
            if (group == null) { return false; }
            return (group.MinDistance.HasValue && group.MinDistance.Value > 0f)
                || (group.MaxDistance.HasValue && group.MaxDistance.Value > 0f);
        }

        // Most-frequent-first. A cron group is ranked by the tightest gap its expression can produce,
        // so "every 15 minutes" still beats "every 48 hours" whichever way each is written.
        private static void SortByFrequency(List<KeyValuePair<string, LocationResetGroup>> groups,
                                            string target, LocationResetDefaults defaults) {
            groups.Sort((a, b) => {
                float sa = GroupPeriodSeconds(a.Value, target, defaults);
                float sb = GroupPeriodSeconds(b.Value, target, defaults);
                int byPeriod = sa.CompareTo(sb);
                // Name tie-break so the outcome cannot depend on dictionary ordering.
                return byPeriod != 0 ? byPeriod : string.CompareOrdinal(a.Key, b.Key);
            });
        }

        private static float GroupPeriodSeconds(LocationResetGroup group, string target, LocationResetDefaults defaults) {
            CronSchedule schedule = ParseSchedule(group.ResetSchedule, target, "group");
            if (schedule != null) { return schedule.MinGapSeconds; }
            return (group.ResetHours ?? defaults.ResetHours) * 3600f;
        }

        // entry ?? group ?? defaults, per field. group is null when nothing claims this prefab.
        private static ResolvedResetEntry Resolve(string name, LocationResetEntry entry,
                                                  LocationResetGroup group, LocationResetDefaults defaults) {
            if (entry == null) { entry = new LocationResetEntry(); }

            // Whole-rule override per category, so an entry that customises an Action does not
            // silently inherit nothing for Ignored (and vice versa). Group sits between the two.
            Dictionary<ProtectionCategory, ProtectionRule> protection =
                new Dictionary<ProtectionCategory, ProtectionRule>(defaults.Protection);
            if (group?.Protection != null) {
                foreach (KeyValuePair<ProtectionCategory, ProtectionRule> over in group.Protection) {
                    if (over.Value == null) { continue; }
                    protection[over.Key] = over.Value;
                }
            }
            if (entry.Protection != null) {
                foreach (KeyValuePair<ProtectionCategory, ProtectionRule> over in entry.Protection) {
                    if (over.Value == null) { continue; }
                    protection[over.Key] = over.Value;
                }
            }

            PickFrequency(name, entry, group, defaults, out float hours, out CronSchedule schedule);

            return new ResolvedResetEntry() {
                Name = name,
                PrefabHash = name.GetStableHashCode(),
                // A group turns its members on. An entry can still enable something no group covers;
                // to exclude one member, drop it from the group's Members list.
                Enabled = entry.Enabled || (group != null && group.Enabled.GetValueOrDefault(true)),
                ResetSeconds = Math.Max(0f, hours) * 3600f,
                Schedule = schedule,
                Mode = entry.Mode,
                ResetTerrain = entry.ResetTerrain ?? group?.ResetTerrain ?? defaults.ResetTerrain,
                TerrainRadius = entry.TerrainRadius ?? group?.TerrainRadius ?? defaults.TerrainRadius,
                ExtraTerrainRadius = Math.Max(0f, entry.ExtraTerrainRadius ?? group?.ExtraTerrainRadius ?? defaults.ExtraTerrainRadius),
                ResetInterior = entry.ResetInterior,
                Protection = protection,
            };
        }

        // Reset frequency resolves as ONE unit, not two independent fallback chains: the first level
        // that says anything about timing owns both fields. Chaining them separately would let a
        // group's ResetSchedule survive a per-entry ResetHours written specifically to override it,
        // which is the opposite of what a per-entry override is for.
        //
        // Within a level, ResetSchedule wins over ResetHours.
        private static void PickFrequency(string name, LocationResetEntry entry, LocationResetGroup group,
                                          LocationResetDefaults defaults, out float hours, out CronSchedule schedule) {
            if (entry.ResetSchedule != null || entry.ResetHours.HasValue) {
                hours = entry.ResetHours ?? defaults.ResetHours;
                schedule = ParseSchedule(entry.ResetSchedule, name, "entry");
                WarnIfBoth(entry.ResetSchedule, entry.ResetHours.HasValue, name, "entry", schedule);
                return;
            }
            if (group != null && (group.ResetSchedule != null || group.ResetHours.HasValue)) {
                hours = group.ResetHours ?? defaults.ResetHours;
                schedule = ParseSchedule(group.ResetSchedule, name, "group");
                WarnIfBoth(group.ResetSchedule, group.ResetHours.HasValue, name, "group", schedule);
                return;
            }
            hours = defaults.ResetHours;
            schedule = ParseSchedule(defaults.ResetSchedule, name, "Defaults");
        }

        // Parsed expressions are cached for the life of a config load: Rebuild resolves every target
        // separately, so a group of 30 members would otherwise re-parse the same string 30 times.
        private static readonly Dictionary<string, CronSchedule> scheduleCache = new Dictionary<string, CronSchedule>();
        // Names already warned about, so one bad expression is one log line rather than one per member.
        private static readonly HashSet<string> scheduleWarnings = new HashSet<string>();

        private static CronSchedule ParseSchedule(string expression, string target, string level) {
            if (string.IsNullOrWhiteSpace(expression)) { return null; }
            if (scheduleCache.TryGetValue(expression, out CronSchedule cached)) { return cached; }

            if (CronSchedule.TryParse(expression, out CronSchedule parsed, out string error)) {
                scheduleCache[expression] = parsed;
                return parsed;
            }
            // Fall back to the interval rather than rejecting the file: a typo should make a reset
            // slower, never open one up, and never take the whole config down with it.
            if (scheduleWarnings.Add(expression)) {
                Logger.LogLocationResetWarning($"ResetSchedule '{expression}' on {level} '{target}' is not a valid cron " +
                    $"expression ({error}). Falling back to ResetHours for everything using it.");
            }
            scheduleCache[expression] = null;
            return null;
        }

        private static void WarnIfBoth(string expression, bool hasHours, string target, string level, CronSchedule schedule) {
            if (expression == null || hasHours == false || schedule == null) { return; }
            if (scheduleWarnings.Add($"both:{level}:{target}") == false) { return; }
            Logger.LogLocationResetWarning($"{level} '{target}' sets both ResetSchedule and ResetHours; " +
                $"the schedule '{expression}' wins and ResetHours is ignored.");
        }

        // Every biome at 1.0, so the generated config shows the knob exists and what to write in it.
        // Writing them all out changes nothing on its own; DistanceBands is deliberately left empty
        // because any example band WOULD change behaviour.
        internal static Dictionary<Heightmap.Biome, float> DefaultBiomeRates() {
            return new Dictionary<Heightmap.Biome, float>() {
                { Heightmap.Biome.Meadows, 1f },
                { Heightmap.Biome.BlackForest, 1f },
                { Heightmap.Biome.Swamp, 1f },
                { Heightmap.Biome.Mountain, 1f },
                { Heightmap.Biome.Plains, 1f },
                { Heightmap.Biome.Mistlands, 1f },
                { Heightmap.Biome.AshLands, 1f },
                { Heightmap.Biome.DeepNorth, 1f },
                { Heightmap.Biome.Ocean, 1f },
            };
        }

        // Curated groups shipped enabled, so the feature is usable without opening the 300+ entry
        // per-prefab lists at all. Nothing resets until BOTH master switches (EnableLocationReset and
        // the yaml Enabled) are turned on, which is what makes shipping them enabled safe.
        //
        // The prefab names here are the load-bearing part. Valheim's naming is irregular enough that
        // no pattern would work -- copper is rock4_copper while rock4_forest is worthless scenery,
        // silver is rock3_silver AND silvervein, swamp iron is mudpile_beacon -- so these lists are
        // hand-verified against the live catalogue and any name that stops matching is warned about
        // at config load rather than silently doing nothing.
        internal static Dictionary<string, LocationResetGroup> DefaultResetGroups() {
            return new Dictionary<string, LocationResetGroup>() {
                // Enabled and ResetHours are left unset deliberately: absent means enabled, and the
                // interval falls through to Defaults.ResetHours. Together with Mode (no group
                // equivalent, and Full is the LocationResetEntry default) that reproduces exactly
                // what the old per-entry boss handling in BuildPopulatedDefault wrote.
                { "BossAltars", new LocationResetGroup() {
                    ResetTerrain = true,
                    ExtraTerrainRadius = 32f,
                    Members = new List<string>(BossAltarLocations),
                } },
                { "Ores", new LocationResetGroup() {
                    ResetHours = 48f,
                    // Mining leaves craters; mudpile_beacon in particular needs the ground back.
                    ResetTerrain = true,
                    ExtraTerrainRadius = 16f,
                    Members = new List<string>() {
                        "rock4_copper", "MineRock_Tin", "silvervein", "rock3_silver",
                        "MineRock_Obsidian", "mudpile_beacon", "UnstableLavaRock",
                        // Flametal. Registered as both a vegetation and a location entry, which is
                        // fine -- the member name applies to whichever catalogue holds it, and here
                        // that is deliberately both.
                        "LeviathanLava",
                        "giant_helmet1", "giant_helmet2", "giant_sword1", "giant_sword2",
                        // Locations rather than vegetation: destroyed and rebuilt, not refreshed.
                        "Mistlands_Giant1", "Mistlands_Giant2",
                        "Mistlands_Swords1", "Mistlands_Swords2", "Mistlands_Swords3",
                    },
                } },
                // Berry bushes are NOT Pickable_* prefabs, so $Pickable misses every one of them.
                { "Berries", new LocationResetGroup() {
                    ResetHours = 128f,
                    Members = new List<string>() { "RaspberryBush", "BlueberryBush", "CloudberryBush", "GlowingMushroom" },
                } },
                { "Foraging", new LocationResetGroup() {
                    ResetHours = 12f,
                    Members = new List<string>() { PickableToken },
                } },
                // Flint is also in Foraging. Near spawn it returns faster; further out it falls back
                // to Foraging's 12h rather than stopping entirely.
                { "FlintNearSpawn", new LocationResetGroup() {
                    ResetHours = 6f,
                    MaxDistance = 3000f,
                    Members = new List<string>() { "Pickable_Flint" },
                } },
                { "StoneNearSpawn", new LocationResetGroup() {
                    ResetHours = 6f,
                    MaxDistance = 3000f,
                    Members = new List<string>() { "Pickable_Stone" },
                } },
                { "MeadowsLocations", new LocationResetGroup() {
                    ResetHours = 24f,
                    Members = new List<string>() {
                        "WoodHouse1", "WoodHouse2", "WoodHouse3",
                        "WoodHouse4", "WoodHouse5", "WoodHouse6",
                        "WoodHouse7", "WoodHouse8", "WoodHouse9",
                        "WoodHouse10", "WoodHouse11", "WoodHouse12",
                        "WoodHouse13", "CombatRuin01", "WoodFarm1", "WoodVillage1"
                    },
                } },
                { "Leviathans", new LocationResetGroup() {
                    ResetHours = 128f,
                    Members = new List<string>() { "Leviathan" },
                } },
                { "MinorMeadowsLocations", new LocationResetGroup() {
                    ResetHours = 128f,
                    Members = new List<string>() { "ShipSetting01", "BigRockClearing" },
                } },
                { "BlackforestDungeons", new LocationResetGroup() {
                    ResetHours = 128f,
                    Members = new List<string>() { 
                        "Crypt2", "TrollCave02", "Crypt3", "Crypt4", "Greydwarf_camp1",
                        "StoneTowerRuins03", "StoneTowerRuins07", "StoneTowerRuins08",
                        "StoneTowerRuins09", "StoneTowerRuins10"
                    },
                } },
                { "Shipwrecks", new LocationResetGroup() {
                    ResetHours = 128f,
                    Members = new List<string>() { "ShipWreck01", "ShipWreck02", "ShipWreck03", "ShipWreck04" },
                } },
                { "SwampDungeonResources", new LocationResetGroup() {
                    ResetHours = 128f,
                    Members = new List<string>() { 
                        "InfestedTree01", "SunkenCrypt4", "SwampHut5", "SwampHut1", 
                        "SwampHut2", "SwampHut3", "SwampHut4", "SwampRuin1", "SwampRuin2", 
                        "SwampWell1", "MountainWell1" },
                } },
                { "MountainLocations", new LocationResetGroup() {
                    ResetHours = 48f,
                    Members = new List<string>() {
                        "MountainCave02", "MountainGrave01", "DrakeNest01", "AbandonedLogCabin02", "AbandonedLogCabin03", 
                        "AbandonedLogCabin04", "StoneTowerRuins04", "StoneTowerRuins05"
                    },
                } },
                { "PlainsLocations", new LocationResetGroup() {
                    ResetHours = 48f,
                    Members = new List<string>() {
                        "TarPit1", "TarPit2", "TarPit3", "GoblinCamp2", "Ruin3", "StoneTower1", "StoneTower3",
                        "StoneHenge1", "StoneHenge2", "StoneHenge3", "StoneHenge4", "StoneHenge5", "StoneHenge6",
                    },
                } },
                // Hildir's quest dungeons and the Ashlands mystery sites.
                { "QuestSites", new LocationResetGroup() {
                    ResetHours = 24f,
                    Members = new List<string>() {
                        "Hildir_crypt", "Hildir_cave", "Hildir_plainsfortress",
                        "PlaceofMystery1", "PlaceofMystery2", "PlaceofMystery3",
                    },
                } },
                { "MistlandMinorLocations", new LocationResetGroup() {
                    ResetHours = 128f,
                    Members = new List<string>() { "Mistlands_RoadPost1", "Mistlands_Viaduct1", "Mistlands_Viaduct2" },
                } },
                { "MistlandLocations", new LocationResetGroup() {
                    ResetHours = 48f,
                    Members = new List<string>() { 
                        "Mistlands_GuardTower1_ruined_new2", "Mistlands_GuardTower3_new", "Mistlands_GuardTower3_ruined_new",
                        "Mistlands_GuardTower1_new", "Mistlands_GuardTower2_new", "Mistlands_GuardTower1_ruined_new", "Mistlands_Lighthouse1_new",
                        "Mistlands_Excavation1", "Mistlands_Excavation2", "Mistlands_Excavation3", "Mistlands_Harbour1",
                    },
                } },
                // Mistlands_DvergrBossEntrance1 belongs to BossAltars, not here: two unscoped groups
                // claiming it would resolve deterministically but warn on every config load.
                { "MistlandDungeons", new LocationResetGroup() {
                    ResetHours = 48f,
                    Members = new List<string>() {
                        "Mistlands_DvergrTownEntrance1", "Mistlands_DvergrTownEntrance2"
                    },
                } },
                // Regular reset of Ashland forts, as they are a very limited resource
                { "AshlandsForts", new LocationResetGroup() {
                    ResetHours = 8f,
                    ResetTerrain = true,
                    ExtraTerrainRadius = 24f,
                    Members = new List<string>() {
                        "CharredFortress"
                    },
                } },
                { "CharredSpawners", new LocationResetGroup() {
                    ResetHours = 72f,
                    Members = new List<string>() { "CharredStone_Spawner", "MorgenHole1", "MorgenHole2", "MorgenHole3" },
                } },
                { "AshlandRuins", new LocationResetGroup() {
                    ResetHours = 72f,
                    Members = new List<string>() {
                        "FortressRuins", "CharredTowerRuins1_dvergr",
                        "CharredRuins1", "CharredRuins4", "FortressRuins",
                        "CharredTowerRuins1", "CharredTowerRuins2", "CharredTowerRuins3",
                        "SulfurArch", "CharredRuins2", "CharredRuins3", "VoltureNest"
                    },
                } },
            };
        }

        // How many configured entries a category token covers, for the status report.
        internal static int CountCategoryMembers(string token) {
            modules.LocationReset.ZoneProtectionScan.BuildPrefabSets();
            int count = 0;
            if (string.Equals(token, PickableToken, StringComparison.OrdinalIgnoreCase)) {
                foreach (int hash in modules.LocationReset.ZoneProtectionScan.PickableHashes) {
                    if (VegetationByPrefabHash.ContainsKey(hash) || LocationsByHash.ContainsKey(hash)) { count++; }
                }
                return count;
            }
            if (string.Equals(token, MineableToken, StringComparison.OrdinalIgnoreCase)) {
                foreach (int hash in modules.LocationReset.ZoneProtectionScan.MineRock5Hashes) {
                    if (VegetationByPrefabHash.ContainsKey(hash) || LocationsByHash.ContainsKey(hash)) { count++; }
                }
                foreach (int hash in modules.LocationReset.ZoneProtectionScan.MineRockAreaCounts.Keys) {
                    if (VegetationByPrefabHash.ContainsKey(hash) || LocationsByHash.ContainsKey(hash)) { count++; }
                }
                return count;
            }
            return 0;
        }

        internal static bool TryGetVegetationEntry(int prefabHash, out ResolvedResetEntry entry) {
            return VegetationByPrefabHash.TryGetValue(prefabHash, out entry);
        }

        internal static bool TryGetLocationEntry(int locationHash, out ResolvedResetEntry entry) {
            return LocationsByHash.TryGetValue(locationHash, out entry);
        }

        // What gets written to LocationResetSettings.yaml. Groups stand alone, so this carries no
        // per-prefab entries at all: Locations and Vegetation ship empty and exist only for
        // overrides. Needs no game state, so the file is complete from the very first write rather
        // than being a skeleton that gets rewritten once a world loads.
        //
        // The exhaustive catalogue lives in BuildPopulatedDefault, which SLS-loc-reset-dump writes to
        // SavedData/LocationResetCatalog.yaml on request.
        internal static LocationResetConfiguration BuildDefaultConfig() {
            return new LocationResetConfiguration() {
                BiomeRates = DefaultBiomeRates(),
                ResetGroups = DefaultResetGroups(),
            };
        }

        // Every location and vegetation entry this server has loaded, including ones other mods add,
        // each disabled. This is the REFERENCE dump behind SLS-loc-reset-dump, not the config
        // default -- see BuildDefaultConfig. Falls back to groups and biome rates alone when
        // ZoneSystem is not up yet.
        internal static LocationResetConfiguration BuildPopulatedDefault() {
            LocationResetConfiguration cfg = BuildDefaultConfig();
            if (ZoneSystem.instance == null) { return cfg; }

            if (ZoneSystem.instance.m_locations != null) {
                foreach (ZoneSystem.ZoneLocation location in ZoneSystem.instance.m_locations) {
                    if (location == null) { continue; }
                    // ZoneLocation.Hash is GetStableHashCode(m_prefab.Name), and that is what
                    // LocationProxy stores in ZDOVars.s_location. Key off the same name so the
                    // config hash and the in-world hash always agree. m_prefabName is only
                    // populated for enabled locations at SetupLocations time, so prefer m_prefab.Name.
                    string name = SafePrefabName(location);
                    if (string.IsNullOrEmpty(name)) { continue; }
                    if (HardBlockedLocations.Contains(name)) { continue; }
                    if (cfg.Locations.ContainsKey(name)) { continue; }

                    // Uniformly disabled. Which targets ship enabled is a question for the groups in
                    // DefaultResetGroups, not for a reference dump of the catalogue.
                    cfg.Locations[name] = new LocationResetEntry() { Enabled = false };
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

        // SoftReference<GameObject>.Name resolves the asset id against the soft-reference asset table,
        // and that lookup throws KeyNotFoundException for an unassigned reference (the all-zero
        // AssetID '00000000000000000000000000000000'). The vanilla location lists carry disabled
        // placeholder entries in exactly that state, so every vanilla read of .Name is guarded by
        // IsValid - do the same and fall back to the serialized m_prefabName.
        private static string SafePrefabName(ZoneSystem.ZoneLocation location) {
            try {
                if (location.m_prefab.IsValid) {
                    string name = location.m_prefab.Name;
                    if (string.IsNullOrEmpty(name) == false) { return name; }
                }
            }
            catch (Exception e) {
                Logger.LogDebug($"Could not resolve a location prefab name from its soft reference, falling back to m_prefabName: {e.Message}");
            }
            return location.m_prefabName;
        }
    }
}
