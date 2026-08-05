using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
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
                if (LocationResetData.SLE_LocationReset_Settings.Enabled == false) { return false; }
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
                return LocationResetData.SLE_LocationReset_Settings.Throughput.SweepBudgetMillisecondsPerFrame;
            }
        }

        internal static void OnMasterSwitchChanged(object sender, EventArgs e) {
            if (LocationResetData.BlockedByModConflict) {
                if (ValConfig.EnableLocationReset.Value) {
                    Logger.LogWarning("[LocationReset] Location Reset cannot be enabled while a conflicting reset mod is installed. See the earlier compatibility warning.");
                }
                return;
            }
            Logger.LogInfo($"[LocationReset] Location Reset master switch set to {ValConfig.EnableLocationReset.Value}.");
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
            LocationResetState.Load();
            Ready = true;

            if (LocationResetData.BlockedByModConflict) {
                Logger.LogLocationReset("Sweep is disabled: a conflicting reset mod is installed.");
                return;
            }
            Logger.LogLocationReset($"World ready. Sweep allowed: {SweepAllowed}, tracked zones: {LocationResetState.TrackedZoneCount}.");
        }

        // Awake runs long before ZoneSystem exists, so the config file created there has empty
        // Locations/Vegetation maps. Once the catalogue is available, fill it in so admins get a
        // complete, mod-aware list to opt into.
        //
        // Only runs while both maps are still empty, so a hand-edited file is never clobbered, and
        // it merges the catalogue into the EXISTING settings rather than regenerating the file, so
        // any Defaults/Throughput/InPlaceRefresh values the admin already changed are preserved.
        private static void EnsureConfigCatalogPopulated() {
            try {
                LocationResetConfiguration current = LocationResetData.SLE_LocationReset_Settings;
                if (current == null) { return; }
                bool hasLocations = current.Locations != null && current.Locations.Count > 0;
                bool hasVegetation = current.Vegetation != null && current.Vegetation.Count > 0;
                if (hasLocations || hasVegetation) { return; }
                if (ZoneSystem.instance == null) { return; }

                LocationResetConfiguration catalog = LocationResetData.BuildPopulatedDefault();
                if (catalog.Locations.Count == 0 && catalog.Vegetation.Count == 0) { return; }

                current.Locations = catalog.Locations;
                current.Vegetation = catalog.Vegetation;

                Logger.LogInfo($"[LocationReset] Populating LocationResetSettings.yaml with this world's " +
                    $"{catalog.Locations.Count} locations and {catalog.Vegetation.Count} vegetation entries (all disabled by default).");
                File.WriteAllText(ValConfig.locationResetFilePath, DataObjects.yamlSerializer.Serialize(current));
                LocationResetData.Rebuild();
            } catch (Exception e) {
                Logger.LogWarning($"[LocationReset] Could not populate the default config catalogue: {e.Message}");
            }
        }

        internal static void OnWorldUnload() {
            Ready = false;
            LocationResetState.ResetState();
            // The scene is going away; just forget the bookkeeping rather than trying to release
            // zones out of a world that is already tearing down.
            ZoneLoader.Clear();
            ZoneProtectionScan.ResetPrefabSets();
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
            sb.AppendLine($"Min reset interval : {LocationResetData.MinEnabledIntervalSeconds / 3600f:0.##} h");

            double uptimeHours = (Time.realtimeSinceStartupAsDouble - LocationResetManager.SweepStartedAt) / 3600d;
            sb.AppendLine($"--- since world load ({uptimeHours:0.##} h) ---");
            sb.AppendLine($"Zones examined     : {LocationResetManager.ZonesExamined}");
            sb.AppendLine($"First-sight stamps : {LocationResetManager.FirstSightZones}");
            sb.AppendLine($"Fast lane (no load): {LocationResetManager.FastLaneZones}");
            sb.AppendLine($"Slow lane (loaded) : {LocationResetManager.SlowLaneZones}");
            sb.AppendLine($"Blocked by players : {LocationResetManager.BlockedZones}");
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
            return sb.ToString();
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
        internal static void ForceResetAround(Vector3 center, float radius) {
            if (Ready == false) {
                Logger.LogInfo("[LocationReset] Not ready: no world loaded, or this is not the server.");
                return;
            }
            TaskRunner.Run().StartCoroutine(ForceResetRoutine(center, radius));
        }

        private static System.Collections.IEnumerator ForceResetRoutine(Vector3 center, float radius) {
            LocationResetConfigSnapshot cfg = LocationResetConfigSnapshot.Capture();
            Vector2i centerZone = ZoneSystem.GetZone(center);
            int span = Mathf.Max(0, Mathf.CeilToInt(radius / 64f));
            int done = 0;
            int blocked = 0;

            for (int dx = -span; dx <= span; dx++) {
                for (int dy = -span; dy <= span; dy++) {
                    Vector2i zone = new Vector2i(centerZone.x + dx, centerZone.y + dy);
                    if (ZoneSystem.instance.IsZoneGenerated(zone) == false) { continue; }

                    ZoneProtectionScan.ProtectionResult protection = ZoneProtectionScan.ScanZone(zone, null, true);
                    if (protection.Blocked) {
                        blocked++;
                        Logger.LogInfo($"[LocationReset] Zone {zone.x},{zone.y} skipped: protected by {protection.BlockingCategory}.");
                        continue;
                    }

                    ResetTargets.RefreshZoneInPlace(zone, cfg);
                    // force: an admin asking for a reset now means now, so per-target timers and the
                    // first-sight grace period are both bypassed. Protection is not.
                    bool ok = false;
                    yield return ResetTargets.RegenerateZone(zone, cfg, protection, true, (r) => { ok = r; });
                    if (ok) {
                        LocationResetState.StampZone(zone);
                        ZoneProtectionScan.RecordBaseline(zone);
                        done++;
                    }
                    yield return null;
                }
            }

            LocationResetState.Save();
            Logger.LogInfo($"[LocationReset] Forced reset complete: {done} zones reset, {blocked} skipped as protected.");
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
