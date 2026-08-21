using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.modules.LocationReset {
    // What a whole manual reset did, accumulated one chunk at a time and handed back to whoever asked
    // for it.
    //
    // ZoneResetReport answers "what happened to this chunk" and is built for a log line; this answers
    // "what happened to my request" and is built to cross an assembly boundary. Every value in
    // ToDictionary is a BCL primitive or a BCL collection of them -- no SLS type, no Unity type, no
    // enum -- because the API hands it to mods that hold no reference to this assembly and resolve
    // everything through reflection. LocationOutcome in particular is internal, so it crosses as a
    // string.
    internal class ResetSummary {

        // A 512m request walks 289 chunks. Per-chunk detail is opt-in for that reason: a caller that
        // wants a progress readout can have one, and a caller that just wants counters is not handed
        // a few hundred dictionaries it never reads.
        internal bool IncludeDetail;

        internal bool Completed;
        // completed | deferred | failed. Distinct from Completed because "we waited and gave up" and
        // "we tried and something broke" need different handling by the caller, and both leave the
        // world untouched.
        internal string Outcome = "completed";
        internal string Reason = "";

        internal string Target = "";
        internal Vector3 Center;
        internal float Radius;
        internal int Safety;

        internal int ZonesConsidered;
        internal int ZonesReset;
        internal int ZonesBlocked;
        internal int ZonesUngenerated;
        internal int ZonesAdopted;

        internal int LocationsRebuilt;
        internal int LocationsTerrainOnly;
        internal int LocationsSkipped;
        internal readonly List<string> LocationNames = new List<string>();

        internal int ObjectsCleared;
        internal int ObjectsSpawned;
        internal int VegetationObjects;
        internal int PickablesRefreshed;
        internal int MineRocksRefreshed;
        internal int ContainersRefreshed;
        internal int TerrainReverted;
        internal int DoorsSealed;
        internal int ZdoGrowth;

        internal float WaitedSeconds;
        internal float ElapsedSeconds;

        private readonly List<Dictionary<string, object>> zones = new List<Dictionary<string, object>>();

        internal void Add(ZoneResetReport report) {
            if (report == null) { return; }
            ZonesConsidered++;

            if (report.ZoneAdopted) { ZonesAdopted++; }
            ObjectsCleared += report.LocationCleared;
            ObjectsSpawned += report.LocationSpawned;
            VegetationObjects += report.VegetationObjects;
            PickablesRefreshed += report.PickablesRefreshed;
            MineRocksRefreshed += report.MineRocksRefreshed;
            ContainersRefreshed += report.ContainersRefreshed;
            TerrainReverted += report.TerrainModificationsUndone;
            DoorsSealed += report.DoorsSealed;

            // Same rule the sweep applies to its own cumulative figure: an adopted chunk is live,
            // so creatures spawn and items despawn between the two samples and that noise is not
            // drift this reset caused.
            if (report.ZdoCounted && report.ZoneAdopted == false) {
                ZdoGrowth += report.ZdoAfter - report.ZdoBefore;
            }

            switch (report.LocationResult) {
                case ZoneResetReport.LocationOutcome.Rebuilt:
                    LocationsRebuilt++;
                    AddLocationName(report.LocationName);
                    break;
                case ZoneResetReport.LocationOutcome.TerrainOnly:
                    LocationsTerrainOnly++;
                    AddLocationName(report.LocationName);
                    break;
                case ZoneResetReport.LocationOutcome.None:
                    // No location here at all, which is most of the world. Not a skip.
                    break;
                default:
                    LocationsSkipped++;
                    break;
            }

            if (IncludeDetail) { zones.Add(Describe(report)); }
        }

        private void AddLocationName(string name) {
            if (string.IsNullOrEmpty(name)) { return; }
            if (LocationNames.Contains(name)) { return; }
            LocationNames.Add(name);
        }

        private static Dictionary<string, object> Describe(ZoneResetReport report) {
            return new Dictionary<string, object>() {
                { "zoneX", report.Zone.x },
                { "zoneZ", report.Zone.y },
                { "biome", report.Biome.ToString() },
                { "location", report.LocationName ?? "" },
                { "outcome", report.LocationResult.ToString() },
                { "skipReason", report.SkipReason ?? "" },
                { "cleared", report.LocationCleared },
                { "spawned", report.LocationSpawned },
                { "terrain", report.TerrainModificationsUndone },
                { "adopted", report.ZoneAdopted },
            };
        }

        internal Dictionary<string, object> ToDictionary() {
            return new Dictionary<string, object>() {
                { "completed", Completed },
                { "outcome", Outcome ?? "" },
                { "reason", Reason ?? "" },
                { "target", Target ?? "" },
                { "centerX", Center.x },
                { "centerY", Center.y },
                { "centerZ", Center.z },
                { "radius", Radius },
                { "safety", Safety },

                { "zonesConsidered", ZonesConsidered },
                { "zonesReset", ZonesReset },
                { "zonesBlocked", ZonesBlocked },
                { "zonesUngenerated", ZonesUngenerated },
                { "zonesAdopted", ZonesAdopted },

                { "locationsRebuilt", LocationsRebuilt },
                { "locationsTerrainOnly", LocationsTerrainOnly },
                { "locationsSkipped", LocationsSkipped },
                // A copy, not the live list: this dictionary outlives the routine that built it.
                { "locationNames", new List<string>(LocationNames) },

                { "objectsCleared", ObjectsCleared },
                { "objectsSpawned", ObjectsSpawned },
                { "vegetationObjects", VegetationObjects },
                { "pickablesRefreshed", PickablesRefreshed },
                { "mineRocksRefreshed", MineRocksRefreshed },
                { "containersRefreshed", ContainersRefreshed },
                { "terrainReverted", TerrainReverted },
                { "doorsSealed", DoorsSealed },
                { "zdoGrowth", ZdoGrowth },

                { "waitedSeconds", WaitedSeconds },
                { "elapsedSeconds", ElapsedSeconds },
                { "zones", new List<Dictionary<string, object>>(zones) },
            };
        }

        // One line for a console caller, mirroring what ForceResetRoutine used to announce.
        internal string ToLine() {
            if (Completed == false) {
                return $"Reset {Outcome}: {Reason}";
            }
            string target = string.IsNullOrEmpty(Target) ? "" : $" of '{Target}'";
            return $"Reset{target} complete: {ZonesReset} chunks reset ({ZonesAdopted} adopted while loaded), " +
                $"{LocationsRebuilt} locations rebuilt, {ZonesBlocked} skipped as protected, " +
                $"{ZonesUngenerated} never generated, ZDO drift {ZdoGrowth}.";
        }
    }
}
