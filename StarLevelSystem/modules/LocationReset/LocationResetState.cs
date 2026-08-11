using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StarLevelSystem.modules.LocationReset {
    // Per-zone reset timers and the prefab census that lets the sweep skip untouched zones without
    // ever loading them. Server-side world state, not configuration: binary, never file-watched,
    // never RPC'd to clients, flushed with the world save.
    //
    // The census is the throughput lever. At first sight of a zone we record how many of each
    // configured vegetation prefab it contains. A zone only needs the expensive poke-load path once
    // its live count drops below that baseline, i.e. once a player has actually destroyed something.
    internal static class LocationResetState {

        private const int FileVersion = 1;

        // Per-prefab timer + census baseline within one zone.
        internal struct EntryRecord {
            internal long Stamp;
            internal ushort Baseline;
        }

        internal class ZoneRecord {
            // Unix seconds of the last time this zone was examined or reset.
            internal long ZoneStamp;
            // prefab hash -> record. Only holds prefabs the config actually tracks.
            internal Dictionary<int, EntryRecord> Entries = new Dictionary<int, EntryRecord>();
        }

        private static readonly Dictionary<Vector2i, ZoneRecord> zones = new Dictionary<Vector2i, ZoneRecord>();
        private static string loadedWorld = "";
        private static bool dirty = false;

        internal static int TrackedZoneCount { get { return zones.Count; } }

        internal static long Now {
            get { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
        }

        // ---------------------------------------------------------------------------------------
        // Accessors
        // ---------------------------------------------------------------------------------------

        internal static bool TryGetZone(Vector2i zone, out ZoneRecord record) {
            return zones.TryGetValue(zone, out record);
        }

        internal static bool IsTracked(Vector2i zone) {
            return zones.ContainsKey(zone);
        }

        internal static ZoneRecord GetOrCreate(Vector2i zone) {
            if (zones.TryGetValue(zone, out ZoneRecord record) == false) {
                record = new ZoneRecord();
                zones[zone] = record;
                dirty = true;
            }
            return record;
        }

        internal static void StampZone(Vector2i zone) {
            ZoneRecord record = GetOrCreate(zone);
            record.ZoneStamp = Now;
            dirty = true;
        }

        // Push a zone's next examination out without resetting it, used when a zone is blocked by a
        // player structure or fails a reset. Prevents a permanently-blocked zone from being retried
        // every sweep cycle.
        internal static void BackoffZone(Vector2i zone, float extraSeconds) {
            ZoneRecord record = GetOrCreate(zone);
            record.ZoneStamp = Now + (long)Math.Max(0f, extraSeconds);
            dirty = true;
        }

        internal static void StampEntry(Vector2i zone, int prefabHash, ushort baseline) {
            ZoneRecord record = GetOrCreate(zone);
            record.Entries[prefabHash] = new EntryRecord() { Stamp = Now, Baseline = baseline };
            dirty = true;
        }

        // Record the census baseline without moving the timer. Used on first sight.
        internal static void SetBaseline(Vector2i zone, int prefabHash, ushort baseline) {
            ZoneRecord record = GetOrCreate(zone);
            record.Entries.TryGetValue(prefabHash, out EntryRecord existing);
            record.Entries[prefabHash] = new EntryRecord() { Stamp = existing.Stamp, Baseline = baseline };
            dirty = true;
        }

        internal static bool TryGetEntry(Vector2i zone, int prefabHash, out EntryRecord entry) {
            entry = default(EntryRecord);
            if (zones.TryGetValue(zone, out ZoneRecord record) == false) { return false; }
            return record.Entries.TryGetValue(prefabHash, out entry);
        }

        internal static void ForgetZone(Vector2i zone) {
            if (zones.Remove(zone)) { dirty = true; }
        }

        internal static IEnumerable<KeyValuePair<Vector2i, ZoneRecord>> AllZones() {
            return zones;
        }

        internal static void MarkDirty() {
            dirty = true;
        }

        // ---------------------------------------------------------------------------------------
        // Persistence
        // ---------------------------------------------------------------------------------------

        internal static void ResetState() {
            zones.Clear();
            loadedWorld = "";
            dirty = false;
        }

        internal static bool Load() {
            try {
                ResetState();
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                if (File.Exists(ValConfig.locationResetStatePath) == false) { return false; }

                byte[] raw = File.ReadAllBytes(ValConfig.locationResetStatePath);
                if (raw.Length == 0) { return false; }

                ZPackage pkg = new ZPackage(raw);
                string worldName = pkg.ReadString();
                int version = pkg.ReadInt();
                if (version != FileVersion) {
                    Logger.LogLocationResetAlways($"State file version {version} does not match {FileVersion}; starting fresh.");
                    return false;
                }

                string currentWorld = ZNet.instance?.GetWorldName() ?? "";
                if (string.IsNullOrEmpty(worldName) == false && string.IsNullOrEmpty(currentWorld) == false && worldName != currentWorld) {
                    Logger.LogLocationResetAlways($"State is for a different world ({worldName} vs {currentWorld}); starting fresh.");
                    return false;
                }

                int zoneCount = pkg.ReadInt();
                for (int i = 0; i < zoneCount; i++) {
                    Vector2i zone = new Vector2i(pkg.ReadInt(), pkg.ReadInt());
                    ZoneRecord record = new ZoneRecord() { ZoneStamp = pkg.ReadLong() };
                    int entryCount = pkg.ReadByte();
                    for (int e = 0; e < entryCount; e++) {
                        int prefabHash = pkg.ReadInt();
                        record.Entries[prefabHash] = new EntryRecord() {
                            Stamp = pkg.ReadLong(),
                            Baseline = pkg.ReadUShort(),
                        };
                    }
                    zones[zone] = record;
                }

                loadedWorld = currentWorld;
                dirty = false;
                Logger.LogLocationResetAlways($"Loaded reset state for {zones.Count} zones.");
                return true;
            } catch (Exception e) {
                Logger.LogLocationResetWarning($"Failed to load reset state, starting fresh: {e.Message}");
                ResetState();
                return false;
            }
        }

        internal static void Save() {
            try {
                if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();

                ZPackage pkg = new ZPackage();
                pkg.Write(ZNet.instance.GetWorldName() ?? loadedWorld ?? "");
                pkg.Write(FileVersion);
                pkg.Write(zones.Count);

                foreach (KeyValuePair<Vector2i, ZoneRecord> kvp in zones) {
                    pkg.Write(kvp.Key.x);
                    pkg.Write(kvp.Key.y);
                    pkg.Write(kvp.Value.ZoneStamp);
                    // Entry counts are bounded by the number of configured vegetation prefabs in a
                    // single zone, which is far below 255 in practice. Clamp rather than corrupt.
                    int count = Math.Min(kvp.Value.Entries.Count, byte.MaxValue);
                    pkg.Write((byte)count);
                    int written = 0;
                    foreach (KeyValuePair<int, EntryRecord> entry in kvp.Value.Entries) {
                        if (written >= count) { break; }
                        pkg.Write(entry.Key);
                        pkg.Write(entry.Value.Stamp);
                        pkg.Write(entry.Value.Baseline);
                        written++;
                    }
                }

                File.WriteAllBytes(ValConfig.locationResetStatePath, pkg.GetArray());
                dirty = false;
            } catch (Exception e) {
                Logger.LogLocationResetWarning($"Failed to save reset state: {e.Message}");
            }
        }

        // Called from the ZNet.SaveWorld prefix alongside the other SLS flushes.
        internal static void FlushPendingSave() {
            if (dirty == false) { return; }
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            Save();
        }
    }
}
