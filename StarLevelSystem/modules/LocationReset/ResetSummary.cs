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

        // Outcomes. Distinct from Completed because the three failures need different handling by the
        // caller and all three leave the world untouched: a refusal will keep being refused until
        // something changes, a deferral is worth retrying when players move, and a failure is a bug
        // to report.
        internal const string OutcomeCompleted = "completed";
        internal const string OutcomeDeferred = "deferred";
        internal const string OutcomeRefused = "refused";
        internal const string OutcomeFailed = "failed";

        // Machine-readable refusal reasons. A caller deciding what to do next should branch on these
        // rather than on Reason, which is prose written for a human and will be reworded.
        internal const string CodeNone = "";
        internal const string CodeModConflict = "mod_conflict";
        internal const string CodeNotReady = "not_ready";
        internal const string CodeAlreadyRunning = "already_running";
        internal const string CodeNoSuchLocation = "no_such_location";
        internal const string CodeHardBlocked = "hard_blocked";
        internal const string CodeTooFar = "too_far";
        internal const string CodeCooldown = "cooldown";
        internal const string CodeNoConnection = "no_connection";
        internal const string CodeNoName = "no_name";
        internal const string CodeTimeout = "timeout";
        internal const string CodeDisconnected = "disconnected";
        internal const string CodeServerError = "server_error";

        internal bool Completed;
        internal string Outcome = OutcomeCompleted;
        internal string Reason = "";
        // Empty unless Outcome is "refused".
        internal string RefusalCode = CodeNone;

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
                { "ownedCleared", report.OwnedCleared },
                { "vegetationPreserved", report.VegetationPreserved },
                { "spawned", report.LocationSpawned },
                { "terrain", report.TerrainModificationsUndone },
                { "adopted", report.ZoneAdopted },
            };
        }

        // A request that never started. It still gets a full summary rather than a bare error, because
        // the callback is the one place a caller's follow-up logic lives: reading result["completed"]
        // and result["refusalCode"] has to work the same whether the reset ran, waited and gave up,
        // or was turned away at the door. Every counter is legitimately zero.
        internal static ResetSummary Refused(string code, string reason, Vector3 center, float radius,
                                             int safety, string target) {
            return new ResetSummary() {
                Completed = false,
                Outcome = OutcomeRefused,
                RefusalCode = code,
                Reason = reason ?? "",
                Center = center,
                Radius = radius,
                Safety = safety,
                Target = target ?? "",
            };
        }

        // The minimum every answer carries, so a caller can read the same four keys off a reset
        // summary, a refused reset, and a query the server turned away.
        internal static Dictionary<string, object> RefusalDictionary(string code, string reason) {
            return new Dictionary<string, object>() {
                { "completed", false },
                { "outcome", OutcomeRefused },
                { "refusalCode", code ?? CodeNone },
                { "reason", reason ?? "" },
            };
        }

        internal Dictionary<string, object> ToDictionary() {
            return new Dictionary<string, object>() {
                { "completed", Completed },
                { "outcome", Outcome ?? "" },
                { "refusalCode", RefusalCode ?? CodeNone },
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
                string code = string.IsNullOrEmpty(RefusalCode) ? "" : $" [{RefusalCode}]";
                return $"Reset {Outcome}{code}: {Reason}";
            }
            string target = string.IsNullOrEmpty(Target) ? "" : $" of '{Target}'";
            return $"Reset{target} complete: {ZonesReset} chunks reset ({ZonesAdopted} adopted while loaded), " +
                $"{LocationsRebuilt} locations rebuilt, {ZonesBlocked} skipped as protected, " +
                $"{ZonesUngenerated} never generated, ZDO drift {ZdoGrowth}.";
        }
    }
}
