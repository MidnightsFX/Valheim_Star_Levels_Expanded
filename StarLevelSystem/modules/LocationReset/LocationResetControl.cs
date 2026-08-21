using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Owns the enable/disable state of the Location Reset sweep and the entry points other systems
    // call into. The sweep itself lives in LocationResetManager; this class is the seam between
    // configuration and that runtime.
    internal static class LocationResetControl {

        // True once the world state has been loaded and the sweep is allowed to run.
        internal static bool Ready = false;

        // Composite gate. Every path that mutates the world must consult this rather than reading
        // the yaml/BepInEx flags directly, so a mod conflict or an unloaded world cannot be
        // bypassed by a config edit mid-session.
        internal static bool SweepAllowed {
            get {
                if (LocationResetData.BlockedByModConflict) { return false; }
                if (ValConfig.EnableLocationReset.Value == false) { return false; }
                if (LocationResetData.ConfigEnabled == false) { return false; }
                if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return false; }
                return Ready;
            }
        }

        // Frame-time budget for one sweep tick. The BepInEx entry wins when set so admins can retune
        // throughput live without editing yaml; 0 defers to the yaml value.
        internal static float SweepBudgetMs {
            get {
                float bepinex = ValConfig.LocationResetSweepBudgetMs.Value;
                if (bepinex > 0f) { return bepinex; }
                return LocationResetData.Throughput.SweepBudgetMillisecondsPerFrame;
            }
        }

        internal static void OnMasterSwitchChanged(object sender, EventArgs e) {
            if (LocationResetData.BlockedByModConflict) {
                if (ValConfig.EnableLocationReset.Value) {
                    Logger.LogLocationResetWarning("Location Reset cannot be enabled while a conflicting reset mod is installed. See the earlier compatibility warning.");
                }
                return;
            }
            Logger.LogLocationResetAlways($"Location Reset master switch set to {ValConfig.EnableLocationReset.Value}.");
        }

        // ZoneSystem.Start on the server: the first point where the world name and the full
        // location/vegetation catalogue exist. Loads per-world state and, if the config file was
        // written before a world was ever loaded, rewrites it now that the catalogue is known.
        internal static void OnZoneSystemReady() {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) {
                Ready = false;
                return;
            }

            EnsureConfigCatalogPopulated();
            // Rebuild again now that ZNetScene exists. The Awake-time pass could not expand $Mineable
            // or $Pickable, because those resolve from the component-derived prefab sets and there
            // were no prefabs yet.
            LocationResetData.Rebuild();
            LocationResetState.Load();
            // Distance bands measure from here. Resolved server-side, before any chunk is evaluated,
            // so the geometry cache is never built against a stale origin.
            modules.LevelSystem.DistanceScaleSystem.TryResolveCenterFromWorld();
            ZoneRates.ResetCache();
            Ready = true;

            if (LocationResetData.BlockedByModConflict) {
                Logger.LogLocationReset("Sweep is disabled: a conflicting reset mod is installed.");
                return;
            }
            Logger.LogLocationReset($"World ready. Sweep allowed: {SweepAllowed}, tracked zones: {LocationResetState.TrackedZoneCount}.");
        }

        // Reset groups stand alone and the default config is complete from its first write, so there
        // is no longer a catalogue to merge in here -- the exhaustive per-prefab list moved to
        // sls-loc-dump. All that remains is backfilling groups onto a config written before
        // they existed.
        private static void EnsureConfigCatalogPopulated() {
            try {
                LocationResetConfiguration current = LocationResetData.SLE_LocationReset_Settings;
                if (current == null) { return; }
                if (ZoneSystem.instance == null) { return; }
                if (current.ResetGroups != null && current.ResetGroups.Count > 0) { return; }

                // Groups arrive ENABLED, which is only safe because both master switches still gate
                // the sweep -- so say so loudly rather than letting an admin discover new reset
                // targets in the world.
                current.ResetGroups = LocationResetData.DefaultResetGroups();
                foreach (KeyValuePair<string, LocationResetGroup> kvp in current.ResetGroups) {
                    Logger.LogLocationResetWarning($"Added reset group '{kvp.Key}' ({kvp.Value.ResetHours ?? 0f:0.#}h, " +
                        $"{kvp.Value.Members.Count} members, enabled). It does nothing until EnableLocationReset is on.");
                }
                // Through the config manager rather than a bare File.WriteAllText: the latter dropped the
                // explanatory comment block on every rewrite, which is most of what makes the file
                // approachable. WriteCurrentToDisk keeps it, and re-stamps the watcher so this write is not
                // mistaken for a hand edit.
                YamlConfigManager.WriteCurrentToDisk(YamlConfigManager.LocationResetSettings);
                LocationResetData.Rebuild();
            } catch (Exception e) {
                Logger.LogLocationResetWarning($"Could not backfill the default reset groups: {e.Message}");
            }
        }

        internal static void OnWorldUnload() {
            Ready = false;
            // Flush before the state goes away so a shutdown never loses the last tick's chunk records.
            LocationResetLog.Clear();
            LocationResetState.ResetState();
            ZoneRates.ResetCache();
            // The scene is going away; just forget the bookkeeping rather than trying to release
            // zones out of a world that is already tearing down.
            ZoneLoader.Clear();
            ZoneProtectionScan.ResetPrefabSets();
            LocationResetData.ResetWorldCatalogIndex();
        }

        // -----------------------------------------------------------------------------------
        // Admin operations
        // -----------------------------------------------------------------------------------

        internal static string BuildStatusReport() {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== SLS Location Reset status ===");

            if (LocationResetData.BlockedByModConflict) {
                sb.AppendLine("DISABLED: a conflicting reset mod (VentureValheim LocationReset) is installed.");
            }
            sb.AppendLine($"Sweep allowed      : {SweepAllowed}");
            sb.AppendLine($"Frame budget       : {SweepBudgetMs:0.##} ms");
            sb.AppendLine($"Tracked zones      : {LocationResetState.TrackedZoneCount}");
            sb.AppendLine($"Generated zones    : {(ZoneSystem.instance != null ? ZoneSystem.instance.m_generatedZones.Count : 0)}");
            sb.AppendLine($"Enabled locations  : {CountEnabled(LocationResetData.LocationsByHash)}");
            sb.AppendLine($"Enabled vegetation : {CountEnabled(LocationResetData.VegetationByPrefabHash)}");
            // The sweep's examination floor. With a cron target enabled this is derived from the
            // tightest gap its expression can produce, not from a configured interval.
            sb.AppendLine($"Sweep floor        : {LocationResetData.MinEnabledIntervalSeconds / 3600f:0.##} h");
            if (LocationResetData.DefaultSchedule != null) {
                sb.AppendLine($"Default schedule   : {LocationResetData.DefaultSchedule.Describe(LocationResetState.Now)}");
            } else {
                float defaultHours = LocationResetData.SLE_LocationReset_Settings?.Defaults?.ResetHours ?? 0f;
                sb.AppendLine($"Default interval   : {defaultHours:0.##} h");
            }

            double uptimeHours = (Time.realtimeSinceStartupAsDouble - LocationResetManager.SweepStartedAt) / 3600d;
            sb.AppendLine($"--- since world load ({uptimeHours:0.##} h) ---");
            sb.AppendLine($"Zones examined     : {LocationResetManager.ZonesExamined}");
            sb.AppendLine($"First-sight stamps : {LocationResetManager.FirstSightZones}");
            sb.AppendLine($"Fast lane (no load): {LocationResetManager.FastLaneZones}");
            sb.AppendLine($"Slow lane (loaded) : {LocationResetManager.SlowLaneZones}");
            sb.AppendLine($"Blocked by players : {LocationResetManager.PlayerBlockedZones}");
            sb.AppendLine($"Blocked by builds  : {LocationResetManager.ProtectionBlockedZones}");
            sb.AppendLine($"Short retries      : {LocationResetManager.RetriedZones} " +
                $"({LocationResetManager.RetriesExhaustedZones} gave up after {LocationResetState.MaxTransientRetries})");
            sb.AppendLine($"Cumulative ZDO drift: {LocationResetManager.ZdoGrowthTotal} (expected 0)");

            // Projected time to work through every generated zone at the current observed rate. This
            // is the number to check against the "half a world per week" target.
            if (uptimeHours > 0.01d && LocationResetManager.ZonesExamined > 0 && ZoneSystem.instance != null) {
                double zonesPerHour = LocationResetManager.ZonesExamined / uptimeHours;
                double total = ZoneSystem.instance.m_generatedZones.Count;
                if (zonesPerHour > 0d) {
                    double days = (total / zonesPerHour) / 24d;
                    sb.AppendLine($"Examination rate   : {zonesPerHour:0} zones/hour");
                    sb.AppendLine($"Full-world pass    : {days:0.##} days");
                }
            }
            sb.AppendLine($"Last action        : {LocationResetManager.LastAction}");
            AppendTargetingReport(sb);
            AppendAPIRegistrations(sb);
            return sb.ToString();
        }

        // Targets other mods registered through the API. They never appear in the yaml, so without
        // this block a registration that lost a precedence fight -- or matched nothing in this world
        // -- is invisible to the admin who has to explain why it is not resetting.
        internal static void AppendAPIRegistrations(System.Text.StringBuilder sb) {
            if (LocationResetData.APIAdded.Count == 0) { return; }

            sb.AppendLine("--- API registrations ---");
            foreach (KeyValuePair<string, LocationResetData.APIResetRegistration> kvp in LocationResetData.APIAdded) {
                LocationResetData.APIResetRegistration api = kvp.Value;
                string frequency = api.Entry.ResetSchedule != null ? api.Entry.ResetSchedule
                    : api.Entry.ResetHours.HasValue ? $"{api.Entry.ResetHours.Value:0.#}h"
                    : "default";
                string scope = "";
                if (api.MinDistance > 0f || api.MaxDistance > 0f) {
                    string outer = api.MaxDistance > 0f ? $"{api.MaxDistance:0}m" : "unbounded";
                    scope = $", {api.MinDistance:0}m-{outer}";
                }

                // Which layer actually won. "api" means the registration is in effect; anything else
                // means the admin's file is overriding it, which is legitimate and worth seeing.
                string source = LocationResetData.DescribeResolutionSource(kvp.Key);
                string effective = source == "api" ? "in effect" : $"overridden by {source}";
                bool known = LocationResetData.IsKnownTargetName(kvp.Key.GetStableHashCode());

                sb.AppendLine($"  {kvp.Key,-24} {(api.Entry.Enabled ? "on " : "off")} {frequency}{scope} " +
                    $"from '{api.SourceId}' ({effective}{(known ? "" : ", MATCHES NOTHING IN THIS WORLD")})");
            }
        }

        // Makes "why is the Meadows never resetting" and "what is it allowed to delete" answerable
        // without opening the yaml.
        private static void AppendTargetingReport(System.Text.StringBuilder sb) {
            LocationResetConfiguration cfg = LocationResetData.SLE_LocationReset_Settings;
            if (cfg == null) { return; }

            sb.AppendLine("--- targeting ---");
            Vector3 center = modules.LevelSystem.DistanceScaleSystem.center;
            sb.AppendLine($"Distance centre    : x={center.x:0} z={center.z:0}" +
                (ValConfig.DistanceBonusIsFromStarterTemple.Value ? " (starter temple)" : " (world origin)"));
            sb.AppendLine($"Chunk geo cached   : {ZoneRates.CachedChunkCount}");

            if (cfg.BiomeRates != null && cfg.BiomeRates.Count > 0) {
                List<string> adjusted = new List<string>();
                foreach (KeyValuePair<Heightmap.Biome, float> kvp in cfg.BiomeRates) {
                    // Only the ones actually doing something; a full list of 1.0s is noise.
                    if (Mathf.Approximately(kvp.Value, 1f)) { continue; }
                    adjusted.Add(kvp.Value <= 0f ? $"{kvp.Key}=excluded" : $"{kvp.Key}=x{kvp.Value:0.##}");
                }
                sb.AppendLine($"Biome rates        : {(adjusted.Count == 0 ? "all 1.0" : string.Join(", ", adjusted))}");
            } else {
                sb.AppendLine("Biome rates        : all 1.0");
            }

            if (cfg.DistanceBands != null && cfg.DistanceBands.Count > 0) {
                foreach (LocationResetBand band in cfg.DistanceBands) {
                    if (band == null) { continue; }
                    string outer = band.Outer > 0f ? $"{band.Outer:0}m" : "unbounded";
                    string rate = band.Multiplier <= 0f ? "excluded" : $"x{band.Multiplier:0.##}";
                    sb.AppendLine($"Band               : {band.Inner:0}m-{outer} {rate}");
                }
            } else {
                sb.AppendLine("Distance bands     : none");
            }

            if (cfg.ResetGroups != null && cfg.ResetGroups.Count > 0) {
                sb.AppendLine("--- reset groups ---");
                foreach (KeyValuePair<string, LocationResetGroup> kvp in cfg.ResetGroups) {
                    LocationResetGroup group = kvp.Value;
                    if (group == null) { continue; }
                    // matched/total is the number that matters: a shortfall means a member name no
                    // longer exists in this world, which is how a game update silently breaks a
                    // curated list.
                    int matched = CountMatchedMembers(group);
                    int total = group.Members != null ? group.Members.Count : 0;
                    string scope = "";
                    if (group.MinDistance.GetValueOrDefault() > 0f || group.MaxDistance.GetValueOrDefault() > 0f) {
                        string outer = group.MaxDistance.GetValueOrDefault() > 0f ? $"{group.MaxDistance.Value:0}m" : "unbounded";
                        scope = $", {group.MinDistance.GetValueOrDefault():0}m-{outer}";
                    }
                    // A cron group has no hours figure to print, so show the expression instead.
                    string frequency = group.ResetSchedule != null ? group.ResetSchedule
                        : group.ResetHours.HasValue ? $"{group.ResetHours.Value:0.#}h"
                        : "default";
                    sb.AppendLine($"  {kvp.Key,-16} {(group.Enabled.GetValueOrDefault(true) ? "on " : "off")} {frequency}{scope}, matched {matched}/{total}");
                }
            }

            if (cfg.Defaults?.Protection != null) {
                List<string> ignored = new List<string>();
                foreach (KeyValuePair<ProtectionCategory, ProtectionRule> kvp in cfg.Defaults.Protection) {
                    if (kvp.Value?.Ignored == null || kvp.Value.Ignored.Count == 0) { continue; }
                    ignored.Add($"{kvp.Key}: {string.Join(", ", kvp.Value.Ignored)}");
                }
                sb.AppendLine($"Ignored prefabs    : {(ignored.Count == 0 ? "none" : string.Join(" | ", ignored))}");
            }
        }

        // How many of a group's members actually resolve to something this world has. Category tokens
        // count as however many entries they expanded to.
        private static int CountMatchedMembers(LocationResetGroup group) {
            if (group.Members == null) { return 0; }
            int matched = 0;
            foreach (string member in group.Members) {
                if (string.IsNullOrWhiteSpace(member)) { continue; }
                string trimmed = member.Trim();
                if (trimmed.StartsWith("$", StringComparison.Ordinal)) {
                    matched += LocationResetData.CountCategoryMembers(trimmed);
                    continue;
                }
                // Against the world catalogue, not the resolved lookups: a member is "matched" when
                // this world has something by that name, which is the check that catches a game
                // update renaming a prefab.
                if (LocationResetData.IsKnownTargetName(trimmed.GetStableHashCode())) { matched++; }
            }
            return matched;
        }

        private static int CountEnabled(System.Collections.Generic.Dictionary<int, LocationResetData.ResolvedResetEntry> map) {
            int count = 0;
            foreach (System.Collections.Generic.KeyValuePair<int, LocationResetData.ResolvedResetEntry> kvp in map) {
                if (kvp.Value.Enabled) { count++; }
            }
            return count;
        }

        // Only one manual reset at a time, whoever asked for it.
        //
        // ZoneLoader.manuallyLoaded is a single shared set, so two overlapping routines can release
        // each other's zones -- and releasing a zone another routine is halfway through destroys the
        // zone root out from under its terrain compiler, which is the one failure this subsystem
        // guards against everywhere else. The background sweep stands down on this flag too.
        //
        // Refused rather than queued: the bool a caller gets back has to mean something, and "your
        // request was accepted and will start in an unknown number of minutes" is not an answer a
        // mod can act on.
        internal static bool ManualResetRunning { get; private set; }

        // Safety modes, matching the API's int parameter.
        internal const int SafetySafe = 0;
        internal const int SafetyForce = 1;

        // How often the Safe path re-checks whether players have cleared out, and how long it waits
        // before giving up. Constants rather than yaml knobs, for the same reason
        // LocationResetState's retry delays are: two more config keys would buy very little and cost
        // the config file's readability.
        //
        // The poll interval matches the sweep's own tick, so Safe never reacts faster than the system
        // it is standing in for.
        private const float SafePollSeconds = 5f;
        private const float DefaultSafeWaitSeconds = 300f;
        private const float SafeWaitLogIntervalSeconds = 60f;

        // One manual reset request. Built by the callers below, consumed by ResetZonesRoutine.
        internal class ResetRequest {
            internal Vector3 Center;
            internal float Radius;
            internal int Safety = SafetySafe;
            // Empty for a whole-radius reset; set when a specific location was named.
            internal string LocationName = "";
            // Named resets take the nearest match by default. "Reset the crypt in front of me" is the
            // common case, and a generous search radius should not quietly reset three of them.
            internal bool ResetAllMatches;
            internal float SafeWaitSeconds;
            internal bool IncludeDetail;
            // Log tag, so a chunk record says which route asked for it.
            internal string Source = "manual";
        }

        // Force an immediate reset of the zones around a point, ignoring timers and the
        // player-proximity rule (but never the protection scan). Admin escape hatch and the main way
        // to test a configuration without waiting hours.
        //
        // Signature unchanged: sls-loc-reset calls this directly. Force rather than Safe because an
        // admin typing the command is standing there deliberately -- see the note on the API's
        // opposite default in APIReciever.
        internal static void ForceResetAround(Vector3 center, float radius, TerminalOutput output) {
            bool accepted = RequestReset(new ResetRequest() {
                Center = center,
                Radius = radius,
                Safety = SafetyForce,
                Source = $"sls-loc-reset r={radius:0}",
            }, output, null);
            // On acceptance the routine flushes when it finishes. On a refusal nothing else will,
            // and a remote admin would be left waiting on a batched line that never gets pushed.
            if (accepted == false) { output?.Flush(); }
        }

        // The single entry point for every manual reset: the admin commands and both API invokes.
        // Returns whether the request was accepted; a false return means nothing was started and the
        // callback will never fire.
        internal static bool RequestReset(ResetRequest request, TerminalOutput output,
                                          Action<Dictionary<string, object>> onComplete) {
            if (request == null) { return false; }

            if (TryRefuse(request, output, out string refusal)) {
                Announce(output, $"Refusing: {refusal}");
                return false;
            }
            if (ValConfig.EnableLocationReset.Value == false) {
                // Still allowed: this is the escape hatch admins use to test a configuration without
                // switching the sweep on for the whole server.
                Announce(output, "Note: EnableLocationReset is off, so the background sweep will not follow up on these chunks.");
            }

            LocationResetConfigSnapshot cfg = LocationResetConfigSnapshot.Capture();
            List<Vector2i> zones;

            if (string.IsNullOrEmpty(request.LocationName) == false) {
                int hash = request.LocationName.GetStableHashCode();
                zones = FindNamedLocationZones(request.Center, request.Radius, hash);
                if (zones.Count == 0) {
                    Announce(output, $"No location named '{request.LocationName}' within {request.Radius:0}m.");
                    return false;
                }
                // FindNamedLocationZones returns nearest first, so trimming to one is "the one the
                // caller is looking at".
                if (request.ResetAllMatches == false && zones.Count > 1) {
                    Announce(output, $"{zones.Count} '{request.LocationName}' locations are within {request.Radius:0}m; " +
                        $"resetting the nearest only.");
                    zones = new List<Vector2i>() { zones[0] };
                }
                // Resolved once for the request rather than per chunk. Distance bands are thousands
                // of metres wide and a targeted request spans a few hundred at most, so every matched
                // chunk falls in the same band as the centre.
                LocationResetData.ResolvedResetEntry target =
                    LocationResetData.ResolveExplicitTarget(request.LocationName, ZoneRates.DistanceFor(ZoneSystem.GetZone(request.Center)));
                if (target == null) {
                    Announce(output, $"'{request.LocationName}' can never be reset.");
                    return false;
                }
                cfg.TargetPrefabHash = hash;
                cfg.TargetOverride = target;
            } else {
                zones = SquareOfZones(request.Center, request.Radius);
            }

            ManualResetRunning = true;
            TaskRunner.Run().StartCoroutine(ResetZonesRoutine(zones, cfg, request, output, onComplete));
            return true;
        }

        // Every reason a manual reset cannot start, in the order a caller most needs to hear them.
        private static bool TryRefuse(ResetRequest request, TerminalOutput output, out string reason) {
            reason = null;
            // Two mods resetting the same objects on their own timers corrupts both. The background
            // sweep already refuses through SweepAllowed; the manual path has to refuse too.
            if (LocationResetData.BlockedByModConflict) {
                reason = "a conflicting reset mod (VentureValheim LocationReset) is installed.";
                return true;
            }
            if (Ready == false || ZoneSystem.instance == null) {
                reason = "not ready - no world loaded, or this is not the server.";
                return true;
            }
            if (ManualResetRunning) {
                reason = "another reset is already running. Wait for it to finish.";
                return true;
            }
            return false;
        }

        internal static List<Vector2i> SquareOfZones(Vector3 center, float radius) {
            List<Vector2i> zones = new List<Vector2i>();
            Vector2i centerZone = ZoneSystem.GetZone(center);
            int span = Mathf.Max(0, Mathf.CeilToInt(radius / 64f));
            for (int dx = -span; dx <= span; dx++) {
                for (int dy = -span; dy <= span; dy++) {
                    zones.Add(new Vector2i(centerZone.x + dx, centerZone.y + dy));
                }
            }
            return zones;
        }

        // Zones within `radius` of `center` holding a location of this name, nearest first.
        //
        // Deliberately NOT ZoneSystem.FindClosestLocation or FindLocations. Both walk every location
        // instance in the world with no early-out, and both read m_location.m_prefab.Name with no
        // null guard on m_location and no IsValid guard on the soft reference -- the same unguarded
        // read LocationResetData.SafePrefabName exists to work around, because vanilla's disabled
        // placeholder entries carry an all-zero AssetID whose lookup throws. Walking the chunk square
        // and using m_locationInstances as the zone-keyed index it already is costs a dictionary hit
        // per chunk and touches no soft references at all.
        internal static List<Vector2i> FindNamedLocationZones(Vector3 center, float radius, int nameHash) {
            List<KeyValuePair<float, Vector2i>> matches = new List<KeyValuePair<float, Vector2i>>();
            if (ZoneSystem.instance == null) { return new List<Vector2i>(); }

            List<Vector2i> candidates = SquareOfZones(center, radius);
            for (int i = 0; i < candidates.Count; i++) {
                Vector2i zone = candidates[i];
                if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { continue; }
                if (instance.m_location == null) { continue; }

                int hash;
                try {
                    hash = instance.m_location.Hash;
                } catch (Exception) {
                    // A location definition whose prefab reference cannot be resolved. Nothing to
                    // identify it by, so it cannot be what was asked for.
                    continue;
                }
                if (hash != nameHash) { continue; }

                // Measured from the location itself, not the chunk centre, so a tight radius does not
                // sweep in a location most of a chunk away.
                Vector3 delta = instance.m_position - center;
                delta.y = 0f;
                if (delta.magnitude > radius) { continue; }
                matches.Add(new KeyValuePair<float, Vector2i>(delta.magnitude, zone));
            }

            matches.Sort((a, b) => a.Key.CompareTo(b.Key));
            List<Vector2i> zones = new List<Vector2i>();
            for (int i = 0; i < matches.Count; i++) { zones.Add(matches[i].Value); }
            return zones;
        }

        // One driver for the admin command, the API radius reset and the API named reset. The only
        // differences between them are the zone list and the snapshot's targeting fields.
        private static System.Collections.IEnumerator ResetZonesRoutine(
                List<Vector2i> zones, LocationResetConfigSnapshot cfg, ResetRequest request,
                TerminalOutput output, Action<Dictionary<string, object>> onComplete) {

            ResetSummary summary = new ResetSummary() {
                IncludeDetail = request.IncludeDetail,
                Target = request.LocationName ?? "",
                Center = request.Center,
                Radius = request.Radius,
                Safety = request.Safety,
            };
            float startedAt = Time.realtimeSinceStartup;
            string source = request.Source;

            try {
                if (request.Safety == SafetySafe) {
                    bool clear = false;
                    yield return WaitForPlayersToClear(zones, cfg, request, output, (ok) => { clear = ok; });
                    summary.WaitedSeconds = Time.realtimeSinceStartup - startedAt;
                    if (clear == false) {
                        summary.Completed = false;
                        summary.Outcome = "deferred";
                        summary.Reason = $"a player stayed within {cfg.PlayerSafeRadius:0}m for " +
                            $"{summary.WaitedSeconds:0}s. Nothing was reset.";
                        Announce(output, summary.ToLine());
                        yield break;
                    }
                }

                for (int i = 0; i < zones.Count; i++) {
                    Vector2i zone = zones[i];
                    ZoneResetReport report = ZoneResetReport.For(zone, true);
                    // Force means force: biome and band rates scale timers, and force already bypasses
                    // every timer, so the rate stays at 1 here. The description is still recorded so an
                    // admin testing a config can see a chunk the background sweep would never reach.
                    report.RateMultiplier = 1f;
                    report.RateDescription = ZoneRates.MultiplierFor(zone, cfg) <= ZoneRates.Excluded
                        ? ZoneRates.Describe(zone, cfg) + ", forced anyway"
                        : ZoneRates.Describe(zone, cfg);

                    if (ZoneSystem.instance.IsZoneGenerated(zone) == false) {
                        summary.ZonesUngenerated++;
                        report.SkipReason = "never generated";
                        summary.Add(report);
                        Announce(output, report, source);
                        continue;
                    }

                    // Same governing-entry rules as the background sweep: force bypasses timers, never
                    // protection, and an admin testing a group's ignores wants force to behave the way
                    // the sweep will. This holds in Safe mode too -- protection is not a timer.
                    ZoneProtectionScan.ProtectionResult protection =
                        ZoneProtectionScan.ScanZone(zone, ZoneProtectionScan.GoverningEntries(zone), true);
                    if (protection.Blocked) {
                        summary.ZonesBlocked++;
                        report.SkipReason = ZoneProtectionScan.DescribeBlock(protection);
                        summary.Add(report);
                        Announce(output, report, source);
                        continue;
                    }

                    // force: asking for a reset now means now, so per-target timers and the
                    // first-sight grace period are bypassed in BOTH the in-place refresh and the
                    // regeneration tiers, and a zone that is already loaded is worked on in place
                    // rather than skipped. Protection is never bypassed.
                    //
                    // Passed true in Safe mode as well. Safe is about not resetting the ground under
                    // somebody's feet, which the wait above has already settled; splitting this bool
                    // into "bypass timers" and "adopt a loaded zone" would touch seven call sites in
                    // the most delicate code here to express something the gate already handles.
                    ResetTargets.RefreshZoneInPlace(zone, cfg, true, report);
                    bool ok = false;
                    yield return ResetTargets.RegenerateZone(zone, cfg, true, report, (r) => { ok = r; });
                    if (ok) {
                        LocationResetState.StampZone(zone);
                        ZoneProtectionScan.RecordBaseline(zone);
                        summary.ZonesReset++;
                    }
                    summary.Add(report);
                    Announce(output, report, source);
                    yield return null;
                }

                summary.Completed = true;
                summary.Outcome = "completed";
                LocationResetState.Save();
                Announce(output, summary.ToLine());
                LocationResetLog.Note($"Manual reset around x={request.Center.x:0} z={request.Center.z:0} " +
                    $"r={request.Radius:0}: {summary.ToLine()}", source);
            } finally {
                // In a finally so a throw anywhere above cannot strand the flag and permanently wedge
                // both the background sweep and every future manual request.
                ManualResetRunning = false;
                summary.ElapsedSeconds = Time.realtimeSinceStartup - startedAt;
                LocationResetLog.Flush();
                // This routine outlives the command call that started it, so the tail of the sweep
                // would otherwise sit in the sink's buffer until something else pushed it out.
                output?.Flush();
                SafeInvoke(onComplete, summary);
            }
        }

        // Wait until no player is standing in or near any target chunk. onResult receives false when
        // the wait ran out, in which case nothing has been touched.
        //
        // Polls rather than borrowing LocationResetState.TryScheduleRetry. That is per-zone state the
        // background sweep owns -- a two-attempt budget on 5 and 15 minute delays that EvaluateZone
        // reads -- so waiting on it here would spend the sweep's retry budget for those chunks and
        // leave them deferred for a full cycle afterwards. This wait belongs to the request, not the
        // zone, so it lives and dies with the request.
        private static System.Collections.IEnumerator WaitForPlayersToClear(
                List<Vector2i> zones, LocationResetConfigSnapshot cfg, ResetRequest request,
                TerminalOutput output, Action<bool> onResult) {

            float limit = request.SafeWaitSeconds > 0f ? request.SafeWaitSeconds : DefaultSafeWaitSeconds;
            float deadline = Time.realtimeSinceStartup + limit;
            float nextLog = 0f;
            bool announced = false;

            while (true) {
                Vector2i blocking = default(Vector2i);
                bool blocked = false;
                for (int i = 0; i < zones.Count; i++) {
                    // Both halves matter. A player just outside the safe radius can still have the
                    // chunk loaded, and a loaded chunk is one whose objects are live in somebody's
                    // scene -- including the dungeon interior, which shares this chunk's coordinates
                    // 5000m up.
                    if (LocationResetManager.PlayersNearby(zones[i], cfg.PlayerSafeRadius) == false
                            && ZoneSystem.instance.IsZoneLoaded(zones[i]) == false) { continue; }
                    blocking = zones[i];
                    blocked = true;
                    break;
                }

                if (blocked == false) {
                    if (announced) { Announce(output, "Players have cleared the area; starting the reset."); }
                    onResult?.Invoke(true);
                    yield break;
                }

                if (Time.realtimeSinceStartup >= deadline) {
                    onResult?.Invoke(false);
                    yield break;
                }

                // Once on entry, then at a slow interval: a five minute wait should be five or six
                // lines, not sixty.
                if (announced == false || Time.realtimeSinceStartup >= nextLog) {
                    float remaining = deadline - Time.realtimeSinceStartup;
                    Announce(output, $"Waiting: chunk {blocking.x},{blocking.y} is occupied or loaded. " +
                        $"Retrying every {SafePollSeconds:0}s for up to {remaining:0}s more. " +
                        $"Use force to reset anyway.");
                    announced = true;
                    nextLog = Time.realtimeSinceStartup + SafeWaitLogIntervalSeconds;
                }

                yield return new WaitForSeconds(SafePollSeconds);
            }
        }

        // A consumer's callback runs inside our coroutine frame, so an exception in it would abort
        // the rest of that frame -- which is the finally block that clears ManualResetRunning and
        // flushes the log. Never let a third-party delegate reach any of that.
        private static void SafeInvoke(Action<Dictionary<string, object>> onComplete, ResetSummary summary) {
            if (onComplete == null) { return; }
            try {
                onComplete(summary.ToDictionary());
            } catch (Exception e) {
                Logger.LogLocationResetWarning($"A mod's location reset callback threw and was ignored: {e}");
            }
        }

        // A manual command reports unconditionally: to the chunk log, to the BepInEx log, and to
        // whichever terminal the admin typed into (or back over the network to the admin who asked).
        // Only the per-entry detail block stays behind the EnableDebugLocationResetDetails flag, via
        // ToRecord. log: false because the line has already gone to the tagged Location Reset logger.
        private static void Announce(TerminalOutput output, ZoneResetReport report, string source) {
            string record = report.ToRecord();
            LocationResetLog.Record(record, source);
            Logger.LogLocationResetAlways(record);
            output?.Detail(record, log: false);
        }

        private static void Announce(TerminalOutput output, string message) {
            Logger.LogLocationResetAlways(message);
            output?.Info(message, log: false);
        }

        // Baseline an already-explored world so timers start now rather than firing everywhere at
        // once. Also the recovery path if the state file is lost. Server only; safe to re-run.
        internal static int StampAllGeneratedZones() {
            if (ZoneSystem.instance == null || ZNet.instance == null || ZNet.instance.IsServer() == false) { return 0; }

            int stamped = 0;
            foreach (Vector2i zone in ZoneSystem.instance.m_generatedZones) {
                LocationResetState.StampZone(zone);
                ZoneProtectionScan.RecordBaseline(zone);
                stamped++;
            }
            LocationResetState.Save();
            Logger.LogLocationReset($"Stamped and censused {stamped} generated zones.");
            return stamped;
        }
    }
}
