using StarLevelSystem.common;
using StarLevelSystem.Data;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Which location created a given ZDO.
    //
    // The clear used to answer that question with a radius: destroy everything within
    // m_exteriorRadius and hope nothing else was standing there. It always was -- world generation
    // plants trees inside a location's radius from the neighbouring zone's own vegetation pass -- and
    // the radius simultaneously MISSED the location's own content, because DungeonGenerator places
    // CampRadial perimeter sections at a radius it chooses rather than the one ZoneLocation declares.
    //
    // So ownership is recorded rather than inferred: every ZDO born during a location spawn is
    // stamped with the location's identity, and the clear destroys exactly those.
    //
    // Identity is the location's ZONE COORDINATES, packed into a long. Not a ZDOID -- raw ZDOIDs are
    // session-scoped, which is why vanilla persists spawner links as ZDOConnectionHashData and
    // re-pairs them in ZDOMan.ConnectSpawners -- and not the LocationProxy's id either, since the
    // proxy is replaced on every reset. A zone hosts at most one location and never moves, so its
    // coordinates are the only part of a location's identity that survives everything.
    internal static class LocationOwnership {

        // Zone (0,0) packs to 0, and that zone is the Start Temple's. So "no owner" cannot be 0, and
        // every read has to pass this as the default -- the obvious GetLong(key, 0L) would report the
        // whole starting area as owned by itself.
        internal const long NoOwner = long.MinValue;

        // The location whose spawn is currently running, or NoOwner. Read by the ZDOMan.CreateNewZDO
        // postfix, which is where every ZDO in the game is born.
        internal static long ActiveOwner = NoOwner;

        // ZoneSystem.SpawnLocation nests: the Full-mode path ends in CreateLocationProxy, which
        // instantiates the proxy with spawnNow = true, whose LocationProxy.Awake calls
        // SpawnProxyLocation -> SpawnLocation(..., SpawnMode.Client, ...) in the same frame. Client
        // mode creates no ZDOs so the nesting is harmless today; the counter is what keeps it harmless
        // if that ever changes, since a plain flag would be cleared by the inner call's exit and leave
        // the rest of the outer spawn untagged.
        private static int depth;

        internal static long KeyFor(Vector2i zone) {
            return ((long)zone.x << 32) | (uint)zone.y;
        }

        internal static Vector2i ZoneFor(long key) {
            return new Vector2i((int)(key >> 32), (int)(uint)key);
        }

        internal static void Begin(long owner) {
            depth++;
            if (depth == 1) { ActiveOwner = owner; }
        }

        internal static void End() {
            depth--;
            if (depth <= 0) {
                depth = 0;
                ActiveOwner = NoOwner;
            }
        }

        // Hard reset of the context, for world teardown. Begin/End are balanced by a Harmony finalizer
        // so they cannot leak within a session, but a world that unloads mid-spawn would otherwise
        // carry a stale owner into the next one.
        internal static void Disarm() {
            depth = 0;
            ActiveOwner = NoOwner;
        }

        internal static long OwnerOf(ZDO zdo) {
            if (zdo == null) { return NoOwner; }
            return zdo.GetLong(SLS_LOC_OWNER, NoOwner);
        }

        internal static bool IsOwnedBy(ZDO zdo, long owner) {
            return OwnerOf(zdo) == owner;
        }

        // Repair path only. The birth-time write in the CreateNewZDO postfix needs no ownership claim
        // because CreateNewZDO has already set this session as the owner; anything writing to a ZDO
        // that has been around must claim it first or the write is dropped on the next sync.
        internal static void Tag(ZDO zdo, long owner) {
            if (zdo == null || zdo.IsValid() == false) { return; }
            if (IsOwnedBy(zdo, owner)) { return; }
            ResetTargets.TakeOwnership(zdo);
            zdo.Set(SLS_LOC_OWNER, owner);
        }

        // The location whose footprint a point falls in, for content that predates the stamp.
        //
        // Used for SPAWNERS ONLY, and the restriction is the whole justification. A spawner is placed
        // by its location and never moves, so "inside the radius of the location in this zone" is a
        // safe read for one -- while for scenery it is precisely the guess this change exists to
        // remove, since a tree standing in the radius is exactly what must NOT be claimed.
        //
        // Without it an existing world would record no creature links at all until every location had
        // been reset once, which is the one moment those links are most wanted.
        internal static long InferOwnerAt(Vector3 position) {
            if (ZoneSystem.instance == null) { return NoOwner; }

            // Neighbours included: a location's exterior radius crosses zone boundaries routinely, and
            // its spawners cross with it.
            Vector2i zone = ZoneSystem.GetZone(position);
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    Vector2i candidate = new Vector2i(zone.x + dx, zone.y + dy);
                    if (ZoneSystem.instance.m_locationInstances.TryGetValue(candidate, out ZoneSystem.LocationInstance instance) == false) { continue; }
                    if (instance.m_location == null) { continue; }
                    if (Utils.DistanceXZ(instance.m_position, position) > instance.m_location.m_exteriorRadius) { continue; }
                    if (ShouldTag(instance.m_location) == false) { continue; }
                    return KeyFor(candidate);
                }
            }
            return NoOwner;
        }

        // Whether this location's content is worth stamping at all.
        //
        // Only locations the configuration actually names as reset targets, so a server that resets
        // three location types does not grow its save by a long on every ZDO of every location in the
        // world. The cost of the gate is that enabling a NEW location type later leaves its existing
        // instances untagged for exactly one cycle -- they get the radius rule, which is what they get
        // today, and their own rebuild tags them for good.
        internal static bool ShouldTag(ZoneSystem.ZoneLocation location) {
            if (location == null) { return false; }
            int hash;
            // ZoneLocation.Hash resolves m_prefab.Name, which throws for the unassigned soft references
            // vanilla's disabled placeholder entries carry -- the same guard SpawnerChildrenFor needs.
            try { hash = location.Hash; }
            catch (System.Exception) { return false; }
            return LocationResetData.TryGetLocationEntry(hash, out _);
        }
    }
}
