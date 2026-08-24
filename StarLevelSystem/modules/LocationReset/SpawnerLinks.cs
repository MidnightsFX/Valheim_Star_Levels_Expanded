using StarLevelSystem.common;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Which spawner made a given creature.
    //
    // Vanilla answers that for exactly one spawner type. CreatureSpawner records its creature as a
    // ZDO connection (ZDOExtraData.ConnectionType.Spawned) and ZDOMan.ConnectSpawners re-pairs it on
    // every world load -- but a ZDO can hold only ONE connection, and that one is spoken for. Every
    // other spawner records nothing at all: SpawnArea (greydwarf nests, bone piles, EvilHearts) caps
    // itself with a live proximity scan over BaseAI.BaseAIInstances and forgets what it made the
    // moment the creature streams out, and TriggerSpawner keeps only a timestamp.
    //
    // So a location reset that destroyed a nest left its greydwarves standing forever, and widening
    // the clear radius to catch them is exactly the blunt instrument this whole change is removing.
    //
    // The link is written from both ends because neither alone survives:
    //
    //   SLS_SPAWNER      the spawner's ZDOID. Fast, and useless after a reload -- raw ZDOIDs are
    //                    session-scoped, which is why vanilla persists its own links as
    //                    ZDOConnectionHashData rather than as ids.
    //   SLS_SPAWNER_POS  the spawner's world position. A spawner is placed by its location and never
    //                    moves, so this is a durable identity, and the mod already matches spawners
    //                    positionally at this epsilon (ResetTargets.FindZdoAt).
    //
    // ReconnectRoutine rebuilds the ZDOIDs from the positions once per world load. It is a repair and
    // an optimisation, never a correctness precondition: CollectLinked falls back to the positional
    // match, so a spawner the pass never reached is still resolved at clear time.
    internal static class SpawnerLinks {

        // Same tolerance as ResetTargets.DuplicateNodeEpsilon, and for the same reason: X and Z come
        // straight from placement while y is rewritten by SnapToGround afterwards.
        private const float MatchEpsilon = 0.25f;

        // A position no spawner can occupy, so "the field is absent" is distinguishable from a
        // spawner that happens to sit at the world origin.
        private static readonly Vector3 NoPosition = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        // ------------------------------------------------------------------------------------------
        // The armed spawn context
        // ------------------------------------------------------------------------------------------
        //
        // Same shape as LocationOwnership, and for the same reason: the creature is a local variable
        // inside a private vanilla method that returns a bool, so there is no postfix that can reach
        // it. Arming around the spawn and stamping in the ZDOMan.CreateNewZDO postfix reaches every
        // spawner type through one mechanism instead of a transpiler per type.
        private static ZDO activeSpawner;
        private static long activeOwner = LocationOwnership.NoOwner;
        private static int depth;

        // spawner ZDOID -> the creatures it made. Session-only and deliberately so: it is rebuilt from
        // the durable positions at load, and every read validates against ZDOMan before using an entry,
        // because a creature can die while streamed out and nothing tells us.
        private static readonly Dictionary<ZDOID, HashSet<ZDOID>> linked = new Dictionary<ZDOID, HashSet<ZDOID>>();

        // Separate from ResetTargets.zdoBuffer. The two are used in the same call chain and sharing
        // one would let a sweep quietly consume the buffer another loop was iterating.
        private static readonly List<ZDO> scanBuffer = new List<ZDO>();

        private static bool reconnectStarted = false;
        internal static int ReconnectedSpawners = 0;
        internal static int ReconnectedCreatures = 0;

        internal static bool ReconnectPending {
            get { return reconnectStarted == false; }
        }

        internal static void Clear() {
            depth = 0;
            activeSpawner = null;
            activeOwner = LocationOwnership.NoOwner;
            linked.Clear();
            scanBuffer.Clear();
            reconnectStarted = false;
            ReconnectedSpawners = 0;
            ReconnectedCreatures = 0;
        }

        // Nothing is armed unless the spawner belongs to a location this config resets. Otherwise every
        // wild creature in the world would gain three ZDO fields for a link nothing will ever read.
        internal static void Begin(ZNetView spawnerView) {
            depth++;
            if (depth != 1) { return; }

            activeSpawner = null;
            activeOwner = LocationOwnership.NoOwner;
            if (LocationResetControl.SweepAllowed == false) { return; }
            if (spawnerView == null || spawnerView.IsValid() == false) { return; }

            ZDO zdo = spawnerView.GetZDO();
            if (zdo == null || zdo.IsValid() == false) { return; }

            long owner = LocationOwnership.OwnerOf(zdo);
            if (owner == LocationOwnership.NoOwner) {
                // No stamp: either this spawner belongs to no location at all, or it predates the
                // stamp. Ask the world which location's footprint it stands in, and adopt it if the
                // answer is one this config resets -- a spawner never moves, so the answer cannot go
                // stale. Written back onto the spawner so this costs nine dictionary lookups once
                // rather than on every creature it ever makes, and so the next clear takes the
                // spawner itself through the ordinary stamped path.
                owner = LocationOwnership.InferOwnerAt(zdo.GetPosition());
                if (owner == LocationOwnership.NoOwner) { return; }
                LocationOwnership.Tag(zdo, owner);
            }

            activeSpawner = zdo;
            activeOwner = owner;
        }

        internal static void End() {
            depth--;
            if (depth > 0) { return; }
            depth = 0;
            activeSpawner = null;
            activeOwner = LocationOwnership.NoOwner;
        }

        internal static bool Armed {
            get { return activeSpawner != null; }
        }

        // Called from the same ZDOMan.CreateNewZDO postfix that stamps location ownership, so a
        // creature is linked at birth wherever it was spawned from.
        internal static void TagNewZdo(ZDO created) {
            if (activeSpawner == null || created == null) { return; }
            if (activeSpawner.IsValid() == false) { return; }

            created.Set(SLS_SPAWNER, activeSpawner.m_uid);
            created.Set(SLS_SPAWNER_POS, activeSpawner.GetPosition());
            // Inherit the spawner's location, so the ordinary owned pass picks this creature up while
            // it is still in the block and the link only has to cover the ones that wandered off.
            // Skipped when a location spawn is already armed -- that context is writing the same key
            // for the location actually being built, and it wins.
            if (LocationOwnership.ActiveOwner == LocationOwnership.NoOwner) {
                created.Set(SLS_LOC_OWNER, activeOwner);
            }

            Remember(activeSpawner.m_uid, created.m_uid);
        }

        private static void Remember(ZDOID spawner, ZDOID creature) {
            if (linked.TryGetValue(spawner, out HashSet<ZDOID> ids) == false) {
                ids = new HashSet<ZDOID>();
                linked[spawner] = ids;
            }
            ids.Add(creature);
        }

        // ------------------------------------------------------------------------------------------
        // Clear-time lookup
        // ------------------------------------------------------------------------------------------

        // Add every creature belonging to any of these doomed spawners to the doomed list.
        //
        // Two passes, because they cover different failures. The index reaches a creature that wandered
        // clean out of the swept block, which no positional sweep can. The positional sweep reaches a
        // creature the index never learned about -- one spawned in a previous session that the
        // reconnect pass has not got to yet, or one whose spawner ZDOID changed under it.
        //
        // The sweep runs ONCE over the block rather than once per spawner: a crypt can hold twenty
        // spawners, and twenty nine-sector scans to answer one question is not a trade worth making.
        internal static int CollectLinked(List<ZDO> spawners, Vector2i zone, List<ZDO> doomed) {
            if (ZDOMan.instance == null || spawners == null || spawners.Count == 0) { return 0; }

            HashSet<ZDO> seen = new HashSet<ZDO>(doomed);
            List<Vector3> positions = new List<Vector3>();
            int added = 0;

            for (int i = 0; i < spawners.Count; i++) {
                ZDO spawner = spawners[i];
                if (spawner == null || spawner.IsValid() == false) { continue; }
                positions.Add(spawner.GetPosition());

                if (linked.TryGetValue(spawner.m_uid, out HashSet<ZDOID> ids) == false) { continue; }
                foreach (ZDOID id in ids) {
                    ZDO creature = ZDOMan.instance.GetZDO(id);
                    if (Claimable(creature, seen) == false) { continue; }
                    doomed.Add(creature);
                    seen.Add(creature);
                    added++;
                }
            }

            if (positions.Count == 0) { return added; }
            float sqrEpsilon = MatchEpsilon * MatchEpsilon;

            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    scanBuffer.Clear();
                    ZDOMan.instance.FindObjects(new Vector2i(zone.x + dx, zone.y + dy), scanBuffer);

                    for (int i = 0; i < scanBuffer.Count; i++) {
                        ZDO zdo = scanBuffer[i];
                        if (zdo == null || zdo.IsValid() == false) { continue; }
                        Vector3 origin = zdo.GetVec3(SLS_SPAWNER_POS, NoPosition);
                        if (origin == NoPosition) { continue; }
                        if (NearAny(positions, origin, sqrEpsilon) == false) { continue; }
                        if (Claimable(zdo, seen) == false) { continue; }
                        doomed.Add(zdo);
                        seen.Add(zdo);
                        added++;
                    }
                }
            }
            scanBuffer.Clear();
            return added;
        }

        private static bool Claimable(ZDO creature, HashSet<ZDO> seen) {
            if (creature == null || creature.IsValid() == false) { return false; }
            if (seen.Contains(creature)) { return false; }
            // A pet, or a creature part-way through being tamed, is never a reset's to take -- and
            // neither is a player who happens to be standing in the link's way.
            return ResetTargets.IsProtectedLiving(creature) == false;
        }

        private static bool NearAny(List<Vector3> positions, Vector3 point, float sqrEpsilon) {
            for (int i = 0; i < positions.Count; i++) {
                float dx = positions[i].x - point.x;
                float dz = positions[i].z - point.z;
                if ((dx * dx) + (dz * dz) <= sqrEpsilon) { return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------
        // World-load reconnection
        // ------------------------------------------------------------------------------------------

        // Rebuild the session index from the durable positions.
        //
        // Coroutine, pumped one slice per frame. GetAllZDOsWithPrefabIterative is built to be driven
        // that way; draining it in a tight loop walks the entire world ZDO table in a single frame,
        // which on a mature world is a multi-hundred-millisecond hitch.
        //
        // The walk is over SPAWNERS, not creatures: there is no cheap way to enumerate every creature
        // prefab, while the spawner set is small and every spawner names the sectors worth reading.
        internal static IEnumerator ReconnectRoutine() {
            // Set before any yield: a throw or an early exit must not leave this looking un-run, or the
            // driver would start it again on the next tick, forever.
            reconnectStarted = true;
            if (ZDOMan.instance == null) { yield break; }

            ZoneProtectionScan.BuildPrefabSets();
            List<string> names = ZoneProtectionScan.SpawnerPrefabNames;
            if (names.Count == 0) { yield break; }

            // Spawners that belong to a location, indexed by the sector they stand in, so a creature's
            // recorded position resolves to a candidate list without scanning them all.
            Dictionary<Vector2i, List<ZDO>> spawnersBySector = new Dictionary<Vector2i, List<ZDO>>();
            List<ZDO> found = new List<ZDO>();

            for (int n = 0; n < names.Count; n++) {
                found.Clear();
                int index = 0;
                while (ZDOMan.instance.GetAllZDOsWithPrefabIterative(names[n], found, ref index) == false) {
                    yield return null;
                    if (ZDOMan.instance == null) { yield break; }
                }

                for (int i = 0; i < found.Count; i++) {
                    ZDO spawner = found[i];
                    if (spawner == null || spawner.IsValid() == false) { continue; }
                    if (LocationOwnership.OwnerOf(spawner) == LocationOwnership.NoOwner) { continue; }

                    Vector2i sector = ZoneSystem.GetZone(spawner.GetPosition());
                    if (spawnersBySector.TryGetValue(sector, out List<ZDO> list) == false) {
                        list = new List<ZDO>();
                        spawnersBySector[sector] = list;
                    }
                    list.Add(spawner);
                    ReconnectedSpawners++;
                }
                yield return null;
            }

            if (spawnersBySector.Count == 0) {
                Logger.LogLocationReset("Spawner reconnect: no location-owned spawners in this world yet.");
                yield break;
            }

            // Creatures live in the spawner's sector or a neighbouring one, so the sectors worth
            // reading are the 3x3 blocks around every spawner -- deduplicated, or a dense camp would
            // have us read the same sector nine times.
            HashSet<Vector2i> sectors = new HashSet<Vector2i>();
            foreach (KeyValuePair<Vector2i, List<ZDO>> entry in spawnersBySector) {
                for (int dx = -1; dx <= 1; dx++) {
                    for (int dy = -1; dy <= 1; dy++) {
                        sectors.Add(new Vector2i(entry.Key.x + dx, entry.Key.y + dy));
                    }
                }
            }

            float sqrEpsilon = MatchEpsilon * MatchEpsilon;
            int sinceYield = 0;
            foreach (Vector2i sector in sectors) {
                scanBuffer.Clear();
                ZDOMan.instance.FindObjects(sector, scanBuffer);

                for (int i = 0; i < scanBuffer.Count; i++) {
                    ZDO zdo = scanBuffer[i];
                    if (zdo == null || zdo.IsValid() == false) { continue; }

                    Vector3 origin = zdo.GetVec3(SLS_SPAWNER_POS, NoPosition);
                    if (origin == NoPosition) { continue; }

                    ZDO spawner = FindSpawnerAt(spawnersBySector, origin, sqrEpsilon);
                    if (spawner == null) { continue; }

                    // Refresh the session id as well as the index. Reading SLS_SPAWNER after a reload
                    // otherwise yields last session's ZDOID, which resolves to nothing or, worse, to an
                    // unrelated ZDO that has since been handed the same id.
                    if (zdo.GetZDOID(SLS_SPAWNER) != spawner.m_uid) {
                        ResetTargets.TakeOwnership(zdo);
                        zdo.Set(SLS_SPAWNER, spawner.m_uid);
                    }
                    Remember(spawner.m_uid, zdo.m_uid);
                    ReconnectedCreatures++;
                }

                scanBuffer.Clear();
                sinceYield++;
                if (sinceYield >= 32) {
                    sinceYield = 0;
                    yield return null;
                    if (ZDOMan.instance == null) { yield break; }
                }
            }

            Logger.LogLocationReset($"Spawner reconnect: {ReconnectedSpawners} location-owned spawners, " +
                $"{ReconnectedCreatures} creatures re-paired across {sectors.Count} sectors.");
        }

        private static ZDO FindSpawnerAt(Dictionary<Vector2i, List<ZDO>> spawnersBySector, Vector3 position, float sqrEpsilon) {
            if (spawnersBySector.TryGetValue(ZoneSystem.GetZone(position), out List<ZDO> candidates) == false) { return null; }

            for (int i = 0; i < candidates.Count; i++) {
                Vector3 at = candidates[i].GetPosition();
                float dx = at.x - position.x;
                float dz = at.z - position.z;
                if ((dx * dx) + (dz * dz) <= sqrEpsilon) { return candidates[i]; }
            }
            return null;
        }
    }
}
