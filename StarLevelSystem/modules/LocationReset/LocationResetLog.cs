using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace StarLevelSystem.modules.LocationReset {
    // The Location Reset action log: one record per chunk the system worked on, buffered in memory and
    // appended to SavedData/LocationResetLog.log in batches. Same design as the Nemesis action log
    // (NemesisManager -> NemesisSystemData.UpdateNemesisLog), except the buffer is a static rather than
    // a field on the driver, because two drivers write to it: the background sweep and the
    // sls-loc-reset coroutine.
    //
    // This is deliberately separate from the BepInEx log. Reset timers run in real-world hours, so the
    // interesting question is usually "what happened to this chunk over the last week", which is a file
    // to grep rather than a stream to watch.
    internal static class LocationResetLog {

        // Batched so a full sweep tick costs one file append rather than one per chunk.
        private static readonly List<string> pending = new List<string>();
        private static string pendingSource = "sweep";
        private static int chunkCount = 0;

        // Nothing should be able to grow without bound if a flush point is ever missed.
        private const int FlushThreshold = 500;

        internal static bool Enabled {
            // Config may not exist yet during very early startup.
            get { return ValConfig.EnableLocationResetLog != null && ValConfig.EnableLocationResetLog.Value; }
        }

        // Takes the already-rendered record rather than the report: both callers also send the same
        // text to the BepInEx log and/or a terminal, so building it once and sharing it keeps the
        // per-chunk string work to a single pass.
        internal static void Record(string record, string source) {
            if (Enabled == false || string.IsNullOrEmpty(record)) { return; }
            pendingSource = source;
            pending.Add(record);
            chunkCount++;
            if (pending.Count >= FlushThreshold) { Flush(); }
        }

        // A line that is not about one chunk: sweep lifecycle, command headers and summaries.
        internal static void Note(string line, string source) {
            if (Enabled == false || string.IsNullOrEmpty(line)) { return; }
            pendingSource = source;
            pending.Add(line);
            if (pending.Count >= FlushThreshold) { Flush(); }
        }

        internal static void Flush() {
            if (pending.Count == 0) { return; }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {pendingSource} | {chunkCount} chunks ===");
            for (int i = 0; i < pending.Count; i++) {
                sb.AppendLine(pending[i]);
            }

            pending.Clear();
            chunkCount = 0;
            LocationResetData.UpdateLocationResetLog(sb.ToString());
        }

        // World teardown: flush whatever is left, then forget. Called before the state is torn down so a
        // shutdown never loses the last tick's records.
        internal static void Clear() {
            Flush();
            pending.Clear();
            chunkCount = 0;
        }
    }
}
