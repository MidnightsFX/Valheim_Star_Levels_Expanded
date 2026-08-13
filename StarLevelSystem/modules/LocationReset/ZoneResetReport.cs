using StarLevelSystem.common;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StarLevelSystem.modules.LocationReset {
    // What happened to one chunk during one pass.
    //
    // The reset tiers used to each log their own line, which meant a zone's story was scattered across
    // the log and the negative cases -- the location whose timer was not due, the vegetation entry with
    // nothing missing -- were never recorded at all. Instead every tier now fills in this accumulator
    // and the driver emits a single record for the zone.
    //
    // One instance is allocated per zone that actually reaches ProcessZone with work to consider, never
    // for the tens of thousands filtered out by EvaluateZone each lap. Details is only built when the
    // detail flag is on, so the string work stays off the hot path.
    internal class ZoneResetReport {

        internal enum LocationOutcome {
            // The zone holds no location, or none this config tracks.
            None = 0,
            Rebuilt,
            TerrainOnly,
            NotDue,
            FirstSightStamped,
            Disabled,
            HardBlocked,
            NoProxy,
            // ResetInterior is off and this location has one, so it was left entirely alone.
            InteriorPreserved,
        }

        internal Vector2i Zone;
        internal Vector3 Center;
        internal Heightmap.Biome Biome;
        // Came from sls-loc-reset rather than the background sweep.
        internal bool Forced;
        internal bool Verbose;
        // The zone was already live and we worked on it in place instead of poke-loading it.
        internal bool ZoneAdopted;

        // Set when nothing was attempted. Mutually exclusive with the counters below.
        internal string SkipReason;

        // Combined biome x band rate applied to every timer in this chunk. 1 = unmodified,
        // ZoneRates.Excluded = the sweep will never touch this chunk. Forced resets leave it at 1,
        // since force bypasses timers and these rates are timers.
        internal float RateMultiplier = 1f;
        // Pre-rendered biome/band/rate text, so the log can show WHY a chunk is on the schedule it is.
        internal string RateDescription;

        // Player-placed prefabs on the ignore list that were destroyed here.
        internal int IgnoredPiecesCleared;
        // Terrain vertices and modifiers actually reverted. Reported separately so "0 undone" is
        // distinguishable from "the terrain pass never ran", which is exactly the failure mode that
        // hid terrain resets not working on unloaded chunks.
        internal int TerrainModificationsUndone;
        // Effective terrain reset radius actually used, after the extra radius and its clamp.
        internal float TerrainRadius;
        // Spawners found by walking the location's own layout rather than by the radius sweep, i.e.
        // ones sitting outside the location's declared radius. Reported separately because a non-zero
        // count is the targeted pass earning its keep.
        internal int SpawnersRemoved;

        // Tier 1, split by family so a record shows which kind of content came back.
        internal int PickablesRefreshed, PickablesNotDue;
        internal int MineRocksRefreshed, MineRocksNotDue;
        internal int ContainersRefreshed, ContainersNotDue;

        // Tier 2. VegetationDuplicatesRejected is the replayed nodes that landed on something still
        // standing and were dropped again; a non-zero count here is the system working, not failing.
        internal int VegetationEntriesReset, VegetationEntriesSkipped, VegetationObjects;
        internal int VegetationDuplicatesRejected;

        // Reset group the location's timer came from, so the log answers "why is this on a 48h
        // timer" and shows when a distance-scoped group took over.
        internal string GroupName;

        // Tier 3
        internal string LocationName;
        internal LocationOutcome LocationResult = LocationOutcome.None;
        // Objects destroyed by the clear, or terrain modifications undone in TerrainOnly mode.
        internal int LocationCleared;
        internal int LocationSpawned;

        // Block ZDO totals either side of a regeneration; a faithful restore leaves them equal.
        // Interior is tracked apart from the surface because a regenerated dungeon legitimately comes
        // back with a different room layout, so its delta is reported but never treated as drift.
        internal int ZdoBefore, ZdoAfter;
        internal int ZdoInteriorBefore, ZdoInteriorAfter;
        internal bool ZdoCounted;

        private List<string> details;

        internal static ZoneResetReport For(Vector2i zone, bool forced) {
            ZoneResetReport report = new ZoneResetReport();
            report.Zone = zone;
            report.Center = ZoneSystem.GetZonePos(zone);
            // GetBiome reads world generation directly, so this works for a zone that was never loaded.
            report.Biome = WorldGenerator.instance != null
                ? WorldGenerator.instance.GetBiome(report.Center)
                : Heightmap.Biome.None;
            report.Forced = forced;
            report.Verbose = ValConfig.EnableDebugLocationResetDetails.Value;
            return report;
        }

        // Per-entry breakdown. A no-op unless the detail flag is on, so callers can hand it interpolated
        // strings without paying for them in the common case -- but only inside an `if (Verbose)` guard
        // when building the string itself is expensive.
        internal void Detail(string line) {
            if (Verbose == false || line == null) { return; }
            if (details == null) { details = new List<string>(); }
            details.Add(line);
        }

        internal bool DidAnything {
            get {
                return PickablesRefreshed > 0 || MineRocksRefreshed > 0 || ContainersRefreshed > 0
                    || VegetationObjects > 0 || IgnoredPiecesCleared > 0 || TerrainModificationsUndone > 0
                    || LocationResult == LocationOutcome.Rebuilt
                    || LocationResult == LocationOutcome.TerrainOnly;
            }
        }

        internal void RecordLocation(string name, LocationOutcome outcome) {
            LocationName = name;
            LocationResult = outcome;
        }

        // "Zone -12,34 @ x=-768 z=2176 (BlackForest, band 0m-3000m, rate x0.25)"
        private string Where() {
            string context = string.IsNullOrEmpty(RateDescription) ? Biome.ToString() : RateDescription;
            return $"Zone {Zone.x},{Zone.y} @ x={Center.x:0} z={Center.z:0} ({context})";
        }

        internal string ToSummaryLine() {
            List<string> parts = new List<string>();

            // Work reported first even when something later went wrong: a zone whose pickables came
            // back but whose regeneration timed out did do something, and hiding that behind the
            // failure would misreport it.
            if (DidAnything) {
                List<string> inPlace = new List<string>();
                if (PickablesRefreshed > 0) { inPlace.Add($"pickables {PickablesRefreshed}"); }
                if (MineRocksRefreshed > 0) { inPlace.Add($"minerock {MineRocksRefreshed}"); }
                if (ContainersRefreshed > 0) { inPlace.Add($"containers {ContainersRefreshed}"); }
                if (inPlace.Count > 0) { parts.Add("refreshed " + string.Join(", ", inPlace)); }

                if (VegetationObjects > 0 || VegetationDuplicatesRejected > 0) {
                    string duplicates = VegetationDuplicatesRejected > 0
                        ? $" ({VegetationDuplicatesRejected} duplicates rejected)" : "";
                    parts.Add($"vegetation {VegetationObjects} objects across {VegetationEntriesReset} entries{duplicates}");
                }
                if (IgnoredPiecesCleared > 0) { parts.Add($"cleared {IgnoredPiecesCleared} ignored pieces"); }
                if (TerrainModificationsUndone > 0) { parts.Add($"terrain {TerrainModificationsUndone} reverted"); }
                parts.Add(DescribeLocation());
                if (ZdoCounted) { parts.Add($"ZDO {ZdoBefore}->{ZdoAfter}"); }
                // Only when there is an interior to talk about; most chunks have none.
                if (ZdoCounted && (ZdoInteriorBefore > 0 || ZdoInteriorAfter > 0)) {
                    parts.Add($"interior {ZdoInteriorBefore}->{ZdoInteriorAfter}");
                }
                if (ZoneAdopted) { parts.Add("adopted while loaded"); }
                if (string.IsNullOrEmpty(SkipReason) == false) { parts.Add($"incomplete: {SkipReason}"); }
                parts.RemoveAll(string.IsNullOrEmpty);
                return $"{Where()} reset: {string.Join(" | ", parts)}";
            }

            if (string.IsNullOrEmpty(SkipReason) == false) {
                return $"{Where()} skipped: {SkipReason}";
            }

            // Nothing came back. Say why, so an admin can tell "protected" from "not due yet" from
            // "nothing here is configured".
            string locationNote = DescribeLocation();
            if (string.IsNullOrEmpty(locationNote) == false) { parts.Add(locationNote); }
            if (VegetationEntriesSkipped > 0) { parts.Add($"{VegetationEntriesSkipped} vegetation entries not due"); }
            int notDue = PickablesNotDue + MineRocksNotDue + ContainersNotDue;
            if (notDue > 0) { parts.Add($"{notDue} in-place targets not due"); }
            if (parts.Count == 0) { parts.Add("no configured targets in this chunk"); }
            return $"{Where()} nothing reset: {string.Join(", ", parts)}";
        }

        private string DescribeLocation() {
            if (LocationResult == LocationOutcome.None) { return ""; }
            string name = string.IsNullOrEmpty(LocationName) ? "?" : LocationName;
            if (string.IsNullOrEmpty(GroupName) == false) { name = $"{name}' via group '{GroupName}"; }
            switch (LocationResult) {
                case LocationOutcome.Rebuilt:
                    string strays = SpawnersRemoved > 0 ? $", +{SpawnersRemoved} stray spawners" : "";
                    return $"location '{name}' rebuilt (cleared {LocationCleared}{strays}, spawned {LocationSpawned}{DescribeTerrain()})";
                case LocationOutcome.TerrainOnly:
                    return $"location '{name}' terrain-only ({LocationCleared} modifications undone{DescribeTerrain()})";
                case LocationOutcome.NotDue:
                    return $"location '{name}' not due";
                case LocationOutcome.FirstSightStamped:
                    return $"location '{name}' first sight, stamped";
                case LocationOutcome.Disabled:
                    return $"location '{name}' not enabled";
                case LocationOutcome.HardBlocked:
                    return $"location '{name}' can never be reset";
                case LocationOutcome.NoProxy:
                    return $"location '{name}' has no proxy to time from";
                case LocationOutcome.InteriorPreserved:
                    return $"location '{name}' left alone (ResetInterior off; its interior would be rebuilt regardless)";
                default:
                    return "";
            }
        }

        private string DescribeTerrain() {
            if (TerrainRadius <= 0f) { return ""; }
            return $", terrain {TerrainRadius:0}m";
        }

        // Summary plus, when the detail flag is on, the indented per-entry breakdown.
        internal string ToRecord() {
            if (details == null || details.Count == 0) { return ToSummaryLine(); }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ToSummaryLine());
            for (int i = 0; i < details.Count; i++) {
                sb.AppendLine("    " + details[i]);
            }
            // Trim the trailing newline; the log writer adds its own line breaks.
            return sb.ToString().TrimEnd('\r', '\n');
        }
    }
}
