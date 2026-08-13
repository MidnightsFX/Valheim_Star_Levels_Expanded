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
                // Through ValConfig rather than a bare File.WriteAllText: the latter dropped the
                // explanatory comment block on every rewrite, which is most of what makes the file
                // approachable.
                ValConfig.RewriteConfigFileWithHeader(ValConfig.locationResetFilePath,
                    DataObjects.yamlSerializer.Serialize(current));
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
            return sb.ToString();
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

        // Force an immediate reset of the zones around a point, ignoring timers and the
        // player-proximity rule (but never the protection scan). Admin escape hatch and the main way
        // to test a configuration without waiting hours.
        internal static void ForceResetAround(Vector3 center, float radius, TerminalOutput output) {
            // Two mods resetting the same objects on their own timers corrupts both. The background
            // sweep already refuses through SweepAllowed; the manual path has to refuse too.
            if (LocationResetData.BlockedByModConflict) {
                Announce(output, "Refusing: a conflicting reset mod (VentureValheim LocationReset) is installed.");
                return;
            }
            if (Ready == false) {
                Announce(output, "Not ready: no world loaded, or this is not the server.");
                return;
            }
            if (ValConfig.EnableLocationReset.Value == false) {
                // Still allowed: this is the escape hatch admins use to test a configuration without
                // switching the sweep on for the whole server.
                Announce(output, "Note: EnableLocationReset is off, so the background sweep will not follow up on these chunks.");
            }
            TaskRunner.Run().StartCoroutine(ForceResetRoutine(center, radius, output));
        }

        private static System.Collections.IEnumerator ForceResetRoutine(Vector3 center, float radius, TerminalOutput output) {
            LocationResetConfigSnapshot cfg = LocationResetConfigSnapshot.Capture();
            Vector2i centerZone = ZoneSystem.GetZone(center);
            int span = Mathf.Max(0, Mathf.CeilToInt(radius / 64f));
            int done = 0;
            int blocked = 0;
            int ungenerated = 0;
            int adopted = 0;
            string source = $"sls-loc-reset r={radius:0}";

            for (int dx = -span; dx <= span; dx++) {
                for (int dy = -span; dy <= span; dy++) {
                    Vector2i zone = new Vector2i(centerZone.x + dx, centerZone.y + dy);
                    ZoneResetReport report = ZoneResetReport.For(zone, true);
                    // Force means force: biome and band rates scale timers, and force already bypasses
                    // every timer, so the rate stays at 1 here. The description is still recorded so an
                    // admin testing a config can see a chunk the background sweep would never reach.
                    report.RateMultiplier = 1f;
                    report.RateDescription = ZoneRates.MultiplierFor(zone, cfg) <= ZoneRates.Excluded
                        ? ZoneRates.Describe(zone, cfg) + ", forced anyway"
                        : ZoneRates.Describe(zone, cfg);

                    if (ZoneSystem.instance.IsZoneGenerated(zone) == false) {
                        ungenerated++;
                        report.SkipReason = "never generated";
                        Announce(output, report, source);
                        continue;
                    }

                    ZoneProtectionScan.ProtectionResult protection = ZoneProtectionScan.ScanZone(zone, null, true);
                    if (protection.Blocked) {
                        blocked++;
                        report.SkipReason = ZoneProtectionScan.DescribeBlock(protection);
                        Announce(output, report, source);
                        continue;
                    }

                    // force: an admin asking for a reset now means now, so per-target timers and the
                    // first-sight grace period are bypassed in BOTH the in-place refresh and the
                    // regeneration tiers, and a zone that is already loaded is worked on in place
                    // rather than skipped. Protection is never bypassed.
                    ResetTargets.RefreshZoneInPlace(zone, cfg, true, report);
                    bool ok = false;
                    yield return ResetTargets.RegenerateZone(zone, cfg, true, report, (r) => { ok = r; });
                    if (ok) {
                        LocationResetState.StampZone(zone);
                        ZoneProtectionScan.RecordBaseline(zone);
                        done++;
                        if (report.ZoneAdopted) { adopted++; }
                    }
                    Announce(output, report, source);
                    yield return null;
                }
            }

            LocationResetState.Save();
            Announce(output, $"Forced reset complete: {done} chunks reset ({adopted} adopted while loaded), " +
                $"{blocked} skipped as protected, {ungenerated} never generated.");
            LocationResetLog.Note($"Forced reset around x={center.x:0} z={center.z:0} r={radius:0}: {done} reset, " +
                $"{adopted} adopted, {blocked} protected, {ungenerated} ungenerated.", source);
            LocationResetLog.Flush();
            // This routine outlives the command call that started it, so the tail of the sweep would
            // otherwise sit in the sink's buffer until something else pushed it out.
            output?.Flush();
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
