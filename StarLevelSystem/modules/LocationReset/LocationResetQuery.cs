using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Read-only answers to "when was this last reset, and when does it come round again".
    //
    // Two timestamps live in this system and they mean different things, which is the whole reason
    // this file is separate from the counters in sls-loc-status:
    //
    //   A LOCATION's last reset is the SLS_LOC_RESET long on its LocationProxy ZDO. It is the
    //   authority -- it rides on the world data itself, so it survives losing the state file, and it
    //   is what RegenerateLocation actually times against.
    //
    //   A CHUNK's stamp is LocationResetState.ZoneRecord.ZoneStamp, and it records when the sweep
    //   last EXAMINED the chunk, which is not the same as resetting anything in it. Most chunks in a
    //   world carry a recent stamp and have never had a thing reset in them.
    //
    // Conflating the two is the obvious mistake here, so nothing below reports one under the other's
    // name.
    internal static class LocationResetQuery {

        // No location of that name in range, or nothing to read it from.
        internal const long NotFound = -1L;
        // Never stamped: the location exists but no reset or first-sight pass has touched it yet.
        internal const long NeverReset = 0L;

        // ---------------------------------------------------------------------------------------
        // Locations
        // ---------------------------------------------------------------------------------------

        // Unix seconds UTC of the named location's last reset. NotFound when there is no such
        // location within radius, NeverReset when it has never been stamped.
        internal static long GetLocationLastReset(string locationName, Vector3 center, float radius) {
            if (TryFindLocation(locationName, center, radius, out Vector2i zone, out _, out int hash) == false) {
                return NotFound;
            }
            ZDO proxy = ResetTargets.FindLocationProxy(zone, hash);
            // No proxy means nothing is carrying a timestamp for it. Distinct from "never reset":
            // the location is there but unresettable until a proxy exists, which is what the
            // NoProxy outcome reports during a sweep.
            if (proxy == null) { return NotFound; }
            return proxy.GetLong(DataObjects.SLS_LOC_RESET, NeverReset);
        }

        // Seconds until the named location is next due. -1 when unknown (not found, no proxy, or
        // nothing has it configured), 0 when it is due now.
        internal static double GetSecondsUntilDue(string locationName, Vector3 center, float radius) {
            if (TryFindLocation(locationName, center, radius, out Vector2i zone, out _, out int hash) == false) { return -1d; }
            if (LocationResetData.TryGetLocationEntry(hash, out LocationResetData.ResolvedResetEntry entry) == false) { return -1d; }

            float rate = RateFor(zone);
            entry = entry.ForDistance(ZoneRates.DistanceFor(zone));
            if (entry.Enabled == false) { return -1d; }

            ZDO proxy = ResetTargets.FindLocationProxy(zone, hash);
            if (proxy == null) { return -1d; }
            long last = proxy.GetLong(DataObjects.SLS_LOC_RESET, NeverReset);
            // Never stamped means the next pass stamps it and the one after resets it, so "due" is
            // not a meaningful number yet -- reporting 0 here would promise a reset that will not
            // happen on the next pass.
            if (last <= 0) { return -1d; }

            long now = LocationResetState.Now;
            if (entry.IsDue(last, now, rate)) { return 0d; }
            return SecondsUntilDue(entry, last, now, rate);
        }

        internal static Dictionary<string, object> GetLocationInfo(string locationName, Vector3 center, float radius) {
            Dictionary<string, object> info = new Dictionary<string, object>() {
                { "found", false },
                { "name", locationName ?? "" },
            };
            if (TryFindLocation(locationName, center, radius, out Vector2i zone,
                                out ZoneSystem.LocationInstance instance, out int hash) == false) {
                return info;
            }

            string name = locationName.Trim();
            float rate = RateFor(zone);
            long now = LocationResetState.Now;

            info["found"] = true;
            info["name"] = name;
            info["zoneX"] = zone.x;
            info["zoneZ"] = zone.y;
            info["positionX"] = instance.m_position.x;
            info["positionY"] = instance.m_position.y;
            info["positionZ"] = instance.m_position.z;
            Vector3 delta = instance.m_position - center;
            delta.y = 0f;
            info["distance"] = delta.magnitude;
            info["hardBlocked"] = LocationResetData.HardBlockedLocations.Contains(name);
            info["rateMultiplier"] = rate;
            info["rateDescription"] = DescribeRate(zone);
            info["source"] = LocationResetData.DescribeResolutionSource(name);

            bool configured = LocationResetData.TryGetLocationEntry(hash, out LocationResetData.ResolvedResetEntry entry);
            if (configured) { entry = entry.ForDistance(ZoneRates.DistanceFor(zone)); }
            info["configured"] = configured;
            info["enabled"] = configured && entry.Enabled;
            info["groupName"] = configured ? (entry.GroupName ?? "") : "";
            info["schedule"] = configured ? entry.DescribeSchedule(now, rate) : "";
            info["mode"] = configured ? (int)entry.Mode : 0;
            info["resetTerrain"] = configured && entry.ResetTerrain;
            info["resetInterior"] = configured == false || entry.ResetInterior;

            ZDO proxy = ResetTargets.FindLocationProxy(zone, hash);
            info["hasProxy"] = proxy != null;
            // Whether this location's content carries an ownership stamp yet, and how much of it does.
            // The clear is precise only for stamped content; an unstamped location still gets the old
            // radius rule for one cycle, so this is the difference between "reset will be surgical"
            // and "reset will fall back". Without it the only way to tell is a save-file dump.
            long ownerKey = LocationOwnership.KeyFor(zone);
            info["owned"] = proxy != null && LocationOwnership.IsOwnedBy(proxy, ownerKey);
            info["ownedZdos"] = CountOwnedZdos(zone, ownerKey);
            long last = proxy != null ? proxy.GetLong(DataObjects.SLS_LOC_RESET, NeverReset) : NotFound;
            info["lastResetUnix"] = last;
            info["secondsSinceReset"] = last > 0 ? (double)(now - last) : -1d;

            bool dueNow = configured && entry.Enabled && last > 0 && entry.IsDue(last, now, rate);
            info["dueNow"] = dueNow;
            info["secondsUntilDue"] = configured == false || entry.Enabled == false || last <= 0
                ? -1d
                : (dueNow ? 0d : SecondsUntilDue(entry, last, now, rate));
            return info;
        }

        // How many ZDOs across the location's 3x3 block carry its ownership stamp. The same footprint
        // the clear sweeps, so the number an admin reads here is the number the next reset will act on.
        private static int CountOwnedZdos(Vector2i zone, long ownerKey) {
            if (ZDOMan.instance == null) { return 0; }

            List<ZDO> buffer = new List<ZDO>();
            int owned = 0;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    buffer.Clear();
                    ZDOMan.instance.FindObjects(new Vector2i(zone.x + dx, zone.y + dy), buffer);
                    for (int i = 0; i < buffer.Count; i++) {
                        ZDO zdo = buffer[i];
                        if (zdo == null || zdo.IsValid() == false) { continue; }
                        if (LocationOwnership.IsOwnedBy(zdo, ownerKey)) { owned++; }
                    }
                }
            }
            return owned;
        }

        // ---------------------------------------------------------------------------------------
        // Chunks
        // ---------------------------------------------------------------------------------------

        internal static Dictionary<string, object> GetChunkInfo(Vector3 position, bool includePrefabs) {
            Vector2i zone = ZoneSystem.GetZone(position);
            Vector3 zoneCenter = ZoneSystem.GetZonePos(zone);
            long now = LocationResetState.Now;

            Dictionary<string, object> info = new Dictionary<string, object>() {
                { "zoneX", zone.x },
                { "zoneZ", zone.y },
                { "centerX", zoneCenter.x },
                { "centerZ", zoneCenter.z },
                { "biome", WorldGenerator.instance != null
                    ? WorldGenerator.instance.GetBiome(zoneCenter).ToString()
                    : Heightmap.Biome.None.ToString() },
                { "generated", ZoneSystem.instance != null && ZoneSystem.instance.IsZoneGenerated(zone) },
                { "loaded", ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(zone) },
                { "rateMultiplier", RateFor(zone) },
                { "rateDescription", DescribeRate(zone) },
            };

            bool tracked = LocationResetState.TryGetZone(zone, out LocationResetState.ZoneRecord record);
            info["tracked"] = tracked;
            if (tracked) {
                // A stamp in the FUTURE is not corruption: BackoffZone parks a blocked chunk by
                // writing Now + offset into the same field. Reporting it as a negative "seconds
                // since examined" would be nonsense, so the deferral is surfaced under its own name
                // and the elapsed figure is only reported when it actually elapsed.
                bool deferred = record.ZoneStamp > now;
                info["lastExaminedUnix"] = deferred ? NeverReset : record.ZoneStamp;
                info["secondsSinceExamined"] = deferred ? -1d : (double)(now - record.ZoneStamp);
                info["deferredUntilUnix"] = deferred ? record.ZoneStamp : 0L;
                // In-memory only; deliberately not serialized, so these reset on restart.
                info["retryAtUnix"] = record.RetryAt;
                info["retryCount"] = (int)record.RetryCount;
            } else {
                info["lastExaminedUnix"] = NeverReset;
                info["secondsSinceExamined"] = -1d;
                info["deferredUntilUnix"] = 0L;
                info["retryAtUnix"] = 0L;
                info["retryCount"] = 0;
            }

            // The location occupying this chunk, if any, plus its own timestamp -- the question
            // "why has this crypt not come back" starts here.
            string locationName = "";
            long locationLast = NotFound;
            double locationDue = -1d;
            if (ZoneSystem.instance != null
                    && ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance)
                    && instance.m_location != null) {
                try {
                    int hash = instance.m_location.Hash;
                    locationName = LocationResetData.ResolveKnownName(hash) ?? instance.m_location.m_prefabName ?? "";
                    ZDO proxy = ResetTargets.FindLocationProxy(zone, hash);
                    if (proxy != null) {
                        locationLast = proxy.GetLong(DataObjects.SLS_LOC_RESET, NeverReset);
                        if (LocationResetData.TryGetLocationEntry(hash, out LocationResetData.ResolvedResetEntry entry)) {
                            entry = entry.ForDistance(ZoneRates.DistanceFor(zone));
                            float rate = RateFor(zone);
                            if (entry.Enabled && locationLast > 0) {
                                locationDue = entry.IsDue(locationLast, now, rate) ? 0d : SecondsUntilDue(entry, locationLast, now, rate);
                            }
                        }
                    }
                } catch (Exception) {
                    // An unresolvable location definition. Nothing to report about it, and not a
                    // reason to fail the whole chunk report.
                }
            }
            info["locationName"] = locationName;
            info["locationLastResetUnix"] = locationLast;
            info["locationSecondsUntilDue"] = locationDue;

            // Answers "is a player build holding this chunk back", which is the other half of the
            // same question. Same call the sweep makes, so it cannot disagree with the sweep.
            ZoneProtectionScan.ProtectionResult protection =
                ZoneProtectionScan.ScanZone(zone, ZoneProtectionScan.GoverningEntries(zone), true);
            info["protectionBlocked"] = protection.Blocked;
            info["protectionReason"] = protection.Blocked ? ZoneProtectionScan.DescribeBlock(protection) : "";

            if (includePrefabs) { info["prefabs"] = DescribeTrackedPrefabs(zone); }
            return info;
        }

        // Per-prefab census for one chunk: what the sweep recorded as its baseline against what is
        // actually standing there now. Opt-in because it is a full ZDO pass over the chunk.
        private static List<Dictionary<string, object>> DescribeTrackedPrefabs(Vector2i zone) {
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            Dictionary<int, ushort> live = ZoneProtectionScan.CensusZone(zone);

            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> tracked in LocationResetData.VegetationByPrefabHash) {
                if (LocationResetState.TryGetEntry(zone, tracked.Key, out LocationResetState.EntryRecord record) == false) { continue; }
                live.TryGetValue(tracked.Key, out ushort present);
                rows.Add(new Dictionary<string, object>() {
                    { "name", ZoneProtectionScan.PrefabNameFor(tracked.Key) },
                    { "lastResetUnix", record.Stamp },
                    { "baseline", (int)record.Baseline },
                    { "live", (int)present },
                });
            }
            return rows;
        }

        // ---------------------------------------------------------------------------------------
        // Shared
        // ---------------------------------------------------------------------------------------

        private static bool TryFindLocation(string locationName, Vector3 center, float radius,
                                            out Vector2i zone, out ZoneSystem.LocationInstance instance, out int hash) {
            zone = default(Vector2i);
            instance = default(ZoneSystem.LocationInstance);
            hash = 0;
            if (string.IsNullOrWhiteSpace(locationName)) { return false; }
            if (ZoneSystem.instance == null) { return false; }

            hash = locationName.Trim().GetStableHashCode();
            List<Vector2i> zones = LocationResetControl.FindNamedLocationZones(center, radius, hash);
            if (zones.Count == 0) { return false; }
            // Nearest first, so an unqualified question about "the crypt near me" answers about the
            // one the caller is looking at.
            zone = zones[0];
            return ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out instance);
        }

        // How long until an entry stamped at `last` comes due. Cron and interval targets answer this
        // differently, and neither is derivable from the other.
        private static double SecondsUntilDue(LocationResetData.ResolvedResetEntry entry, long last, long now, float rate) {
            if (rate <= ZoneRates.Excluded) { return -1d; }
            if (entry.Schedule != null) {
                // Measured from the last stamp, not from now: cron asks "has a fire landed since the
                // stamp", so the next fire after the stamp is the one that makes this target due.
                long? next = entry.Schedule.NextAfterUnix(last);
                if (next.HasValue == false) { return -1d; }
                return Math.Max(0d, (double)(next.Value - now));
            }
            return Math.Max(0d, ZoneRates.ScaleSeconds(entry.ResetSeconds, rate) - (now - last));
        }

        private static float RateFor(Vector2i zone) {
            return ZoneRates.MultiplierFor(zone, LocationResetConfigSnapshot.Capture());
        }

        private static string DescribeRate(Vector2i zone) {
            return ZoneRates.Describe(zone, LocationResetConfigSnapshot.Capture());
        }
    }
}
