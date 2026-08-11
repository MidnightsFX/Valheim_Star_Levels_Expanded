using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Resolves a chunk's reset rate: its biome multiplier times its distance-band multiplier, applied
    // on top of whatever ResetHours each target configures.
    //
    // The point is targeting. On a busy server depletion concentrates by biome and by distance from
    // spawn, but every timer is otherwise uniform, so the hub the whole server strips weekly recovers
    // no faster than an Ashlands corner nobody has visited in a month.
    internal static class ZoneRates {

        // A rate of exactly this means "never reset this chunk". It is a sentinel rather than a plain
        // 0 multiplier because multiplying a timer by zero yields "always due" -- the precise opposite
        // of what an admin writing `Meadows: 0` means. Every consumer checks for it before multiplying.
        internal const float Excluded = 0f;

        // Biome and distance are both fixed for a chunk (deterministic from the world seed and the
        // centre), so this is filled once per chunk per session. Only the geometry is cached, never
        // the resolved multiplier -- rates come from the per-tick config snapshot, so a yaml reload
        // can never leave a stale rate behind and Rebuild() needs no invalidation hook.
        private struct ZoneGeo {
            internal Heightmap.Biome Biome;
            internal float Distance;
        }

        private static readonly Dictionary<Vector2i, ZoneGeo> geoCache = new Dictionary<Vector2i, ZoneGeo>();

        internal static int CachedChunkCount { get { return geoCache.Count; } }

        internal static void ResetCache() {
            geoCache.Clear();
        }

        // Moving the centre invalidates every cached distance.
        internal static void OnCenterChanged(object sender, System.EventArgs e) {
            // Server only. On a client the map-ring handler owns `center`, and ZoneSystem's location
            // table is empty there anyway (vanilla only generates locations on the server), so this
            // would resolve nothing and could fight the other handler.
            if (ZNet.instance != null && ZNet.instance.IsServer()) {
                DistanceScaleSystem.TryResolveCenterFromWorld();
            }
            ResetCache();
        }

        private static ZoneGeo GeoFor(Vector2i zone) {
            if (geoCache.TryGetValue(zone, out ZoneGeo cached)) { return cached; }

            Vector3 center = ZoneSystem.GetZonePos(zone);
            ZoneGeo geo = new ZoneGeo();
            // GetBiome reads world generation directly, so this works for a chunk that was never
            // loaded -- which is the whole population the sweep walks.
            geo.Biome = WorldGenerator.instance != null
                ? WorldGenerator.instance.GetBiome(center)
                : Heightmap.Biome.None;
            // Radial distance is on the (x, z) plane. Vector2.Distance on two Vector3s would silently
            // use altitude as the second axis.
            Vector3 origin = DistanceScaleSystem.center;
            geo.Distance = Vector2.Distance(new Vector2(center.x, center.z), new Vector2(origin.x, origin.z));

            geoCache[zone] = geo;
            return geo;
        }

        internal static Heightmap.Biome BiomeFor(Vector2i zone) {
            return GeoFor(zone).Biome;
        }

        internal static float DistanceFor(Vector2i zone) {
            return GeoFor(zone).Distance;
        }

        // Combined rate for a chunk. Returns Excluded when either the biome or the band opts out.
        internal static float MultiplierFor(Vector2i zone, LocationResetConfigSnapshot cfg) {
            // Nothing is actually being targeted, so skip the geometry lookup entirely. This runs
            // against every generated chunk on every lap, and the generated config lists every biome
            // at 1.0, so without this a default install would pay for a biome sample per chunk to
            // learn that nothing changes.
            if (cfg.RatesActive == false) { return 1f; }

            ZoneGeo geo = GeoFor(zone);

            float biomeRate = BiomeRate(geo.Biome, cfg);
            if (biomeRate <= 0f) { return Excluded; }

            float bandRate = BandRate(geo.Distance, cfg, out _);
            if (bandRate <= 0f) { return Excluded; }

            return biomeRate * bandRate;
        }

        private static float BiomeRate(Heightmap.Biome biome, LocationResetConfigSnapshot cfg) {
            if (cfg.BiomeRates == null || cfg.BiomeRates.Count == 0) { return 1f; }
            // Exact key lookup, then the Biome.All fallback. Heightmap.Biome is a [Flags] enum, but
            // the rest of the mod treats All as a plain fallback key rather than matching bitwise, and
            // GetBiome always returns a single biome, so exact lookup is correct here too.
            if (cfg.BiomeRates.TryGetValue(biome, out float rate)) { return rate; }
            if (cfg.BiomeRates.TryGetValue(Heightmap.Biome.All, out float all)) { return all; }
            return 1f;
        }

        // First band containing the distance wins. A chunk matching no band is unaffected, so a
        // partial band list never silently disables the rest of the world.
        private static float BandRate(float distance, LocationResetConfigSnapshot cfg, out LocationResetBand matched) {
            matched = null;
            if (cfg.DistanceBands == null || cfg.DistanceBands.Count == 0) { return 1f; }
            for (int i = 0; i < cfg.DistanceBands.Count; i++) {
                LocationResetBand band = cfg.DistanceBands[i];
                if (band == null) { continue; }
                if (distance < band.Inner) { continue; }
                // Outer 0 means no outer limit, the same sentinel LocationResetEntry.TerrainRadius uses.
                if (band.Outer > 0f && distance >= band.Outer) { continue; }
                matched = band;
                return band.Multiplier;
            }
            return 1f;
        }

        // Scale a configured interval by a chunk's rate. Excluded chunks are never due, expressed as
        // an interval nothing can outlive rather than as a zero that would read as "due now".
        internal static float ScaleSeconds(float baseSeconds, float multiplier) {
            if (multiplier <= 0f) { return float.MaxValue; }
            return baseSeconds * multiplier;
        }

        // Human-readable rate summary for the chunk log. Only called for chunks being reported.
        internal static string Describe(Vector2i zone, LocationResetConfigSnapshot cfg) {
            ZoneGeo geo = GeoFor(zone);
            float biomeRate = BiomeRate(geo.Biome, cfg);
            float bandRate = BandRate(geo.Distance, cfg, out LocationResetBand band);

            if (biomeRate <= 0f) { return $"{geo.Biome}, excluded by BiomeRates"; }
            if (bandRate <= 0f) { return $"{geo.Biome}, {DescribeBand(band)} excluded by DistanceBands"; }

            float combined = biomeRate * bandRate;
            if (band == null) { return $"{geo.Biome}, rate x{combined:0.##}"; }
            return $"{geo.Biome}, {DescribeBand(band)} rate x{combined:0.##}";
        }

        private static string DescribeBand(LocationResetBand band) {
            if (band == null) { return ""; }
            string outer = band.Outer > 0f ? $"{band.Outer:0}m" : "unbounded";
            return $"band {band.Inner:0}m-{outer},";
        }
    }
}
