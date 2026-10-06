using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // How much of a location a reset may touch right now, and when not all of it, why.
    //
    // A location used to be judged by its chunk: anybody within PlayerSafeRadius of the chunk centre,
    // or any player build in the chunk or near it in a neighbour, held back everything there. And
    // because those checks are XZ-only, a tombstone or a dropped sword INSIDE a dungeon -- parked 5000m
    // straight up, in the same sector -- held back the very dungeon it was lying in. On a busy server
    // that meant crypts and camps almost never came back.
    //
    // A location is now judged on its own terms, in two halves:
    //   the interior  -- held back only while a player is inside it, or while it holds something the
    //                    rebuild keeps (a tombstone, an item, a player's build, a pet) and a player is
    //                    close enough to have it loaded; see InteriorKeepsSomething for why.
    //   the surface   -- held back by blocking player property within LocationBuffer of the ground
    //                    the reset touches (ZoneProtectionScan.ScanFootprint), or by a player on the
    //                    surface within LocationPlayerRadius.
    // A dungeon whose surface is held still gets its interior rebuilt: Scope.Interior.
    //
    // Asked twice per reset. Once before the zone is loaded, to decide whether loading it is worth
    // it; then again inside RegenerateLocationWith, right before the synchronous clear, because the
    // load and the prefab wait between the two can take seconds and a player can walk in meanwhile.
    // The second answer is the one that counts, capped by the first.
    internal static class LocationGate {

        // Ordered: a lower value is a narrower reset, which is what lets a later answer be capped by
        // an earlier one with a plain comparison.
        internal enum Scope {
            None = 0,
            // The sky interior only; the surface is left exactly as it was.
            Interior = 1,
            Full = 2,
        }

        internal sealed class Verdict {
            internal Scope Scope;
            // No location work in this chunk at all: nothing configured, not due, no proxy. Distinct
            // from a hold, which is due work being refused.
            internal bool NothingToDo;
            // The location judged, for the chunk record. Null with NothingToDo.
            internal string Name;
            internal string GroupName;
            internal bool HasInterior;
            // Held back only by a player, who will move on. The sweep gives these a short retry
            // instead of a whole cycle.
            internal bool Transient;
            // Why the location -- or, at Scope.Interior, its surface -- is held back. Null when
            // nothing is.
            internal string Reason;

            internal bool Held { get { return NothingToDo == false && Scope != Scope.Full; } }
        }

        private static readonly List<ZDO> zdoBuffer = new List<ZDO>();

        // Whether a location prefab has a sky interior, by location hash. A prefab's layout cannot
        // change within a session, and loading the asset to look is not free.
        private static readonly Dictionary<int, bool> interiorByLocation = new Dictionary<int, bool>();

        // The pre-flight question for a chunk: is there location work here, and how much of it may
        // happen. Everything RegenerateLocationWith would refuse before reaching the gate is refused
        // here as NothingToDo, so a chunk is never loaded for a location that would be passed over.
        //
        // force bypasses the timer and the surface player rule, as a manual reset always has. It never
        // bypasses protection, and never a player inside the dungeon.
        internal static Verdict Evaluate(Vector2s zone, LocationResetConfigSnapshot cfg, bool force, float rate) {
            if (ResetTargets.GoverningLocation(zone, cfg, out ZoneSystem.LocationInstance instance,
                                               out LocationResetData.ResolvedResetEntry entry) == false) {
                return Nothing();
            }
            if (LocationResetData.HardBlockedLocations.Contains(entry.Name)) { return Nothing(); }

            ZDO proxy = ResetTargets.FindLocationProxy(zone, instance.m_location.Hash);
            if (proxy == null) { return Nothing(); }
            if (force == false) {
                long lastReset = proxy.GetLong(DataObjects.SLS_LOC_RESET, 0L);
                // Never stamped: the sweep stamps it in the fast lane and resets it a cycle later.
                if (lastReset <= 0L) { return Nothing(); }
                if (entry.IsDue(lastReset, cfg.Now, rate) == false) { return Nothing(); }
            }
            return EvaluateFor(zone, instance.m_location, proxy.GetPosition(), entry, cfg, force);
        }

        // The verdict for a location already known to be due. position is the proxy's, which is where
        // the clear and the rebuild measure from.
        internal static Verdict EvaluateFor(Vector2s zone, ZoneSystem.ZoneLocation location, Vector3 position,
                                            LocationResetData.ResolvedResetEntry entry, LocationResetConfigSnapshot cfg,
                                            bool force) {
            Verdict verdict = new Verdict() { Name = entry.Name, GroupName = entry.GroupName };
            bool terrainOnly = entry.Mode == LocationResetMode.TerrainOnly;
            verdict.HasInterior = HasInterior(zone, location);
            bool touchesInterior = verdict.HasInterior && entry.ResetInterior && terrainOnly == false;
            long ownerKey = LocationOwnership.KeyFor(zone);

            // The interior first, and for force as well: a dungeon is never rebuilt around somebody
            // standing in it. The floor vanishes under them and they fall 5000m.
            if (touchesInterior) {
                if (PlayerInside(zone)) { return Hold(verdict, "a player is inside it", true); }
                if (ZoneActiveForAnyPlayer(zone) && InteriorKeepsSomething(zone, entry, ownerKey)) {
                    return Hold(verdict, "a player nearby has it loaded, and it holds a tombstone, item or build " +
                        "that would fall while its rooms are rebuilt", true);
                }
            }

            // Then the surface. Protection before presence, because it is the one that lasts: a dungeon
            // held by a build goes straight to an interior-only rebuild, while one held only by a
            // passer-by is worth a short wait for the whole thing.
            float exterior = location.m_exteriorRadius;
            bool resetsTerrain = terrainOnly || entry.ResetTerrain;
            float baseRadius = resetsTerrain ? Mathf.Max(exterior, ResetTargets.TerrainRadiusFor(entry, exterior)) : exterior;
            ZoneProtectionScan.ProtectionResult protection =
                ZoneProtectionScan.ScanFootprint(position, baseRadius, entry, ownerKey, out float radius);

            string surfaceHeld = null;
            bool surfaceTransient = false;
            if (protection.Blocked) {
                surfaceHeld = $"{ZoneProtectionScan.DescribeBlock(protection)}, within {radius:0}m";
            } else if (force == false && SurfacePlayerNear(position, cfg.LocationPlayerRadius)) {
                surfaceHeld = $"a player is within {cfg.LocationPlayerRadius:0}m";
                surfaceTransient = true;
            }

            if (surfaceHeld == null) {
                verdict.Scope = Scope.Full;
                return verdict;
            }
            if (touchesInterior == false) { return Hold(verdict, surfaceHeld, surfaceTransient); }
            // A passer-by would otherwise cost the surface its whole cycle, since an interior-only
            // rebuild restamps the location. Spend the zone's short retries waiting for them first,
            // and only then settle for the interior.
            if (surfaceTransient && LocationResetState.RetriesRemaining(zone)) { return Hold(verdict, surfaceHeld, true); }

            verdict.Scope = Scope.Interior;
            verdict.Reason = surfaceHeld;
            return verdict;
        }

        private static Verdict Nothing() {
            return new Verdict() { Scope = Scope.None, NothingToDo = true };
        }

        private static Verdict Hold(Verdict verdict, string reason, bool transient) {
            verdict.Scope = Scope.None;
            verdict.Reason = reason;
            verdict.Transient = transient;
            return verdict;
        }

        // Whether this location type has a sky interior. Read from the prefab rather than the zone:
        // any ZDO in the sky would answer "yes" for the zone, including a tombstone, and an
        // interior-only clear of a location with no interior to rebuild would only ever destroy.
        //
        // Location.m_hasInterior is vanilla's own flag; a networked child authored above the sky
        // threshold catches a modded location that parks content up there without setting it. Falls
        // back to the zone answer, uncached, when the prefab cannot be read.
        internal static bool HasInterior(Vector2s zone, ZoneSystem.ZoneLocation location) {
            int hash;
            // ZoneLocation.Hash resolves m_prefab.Name, which throws for an unassigned soft reference.
            try { hash = location.Hash; }
            catch (System.Exception) { return ResetTargets.HasSkyInterior(zone); }

            if (interiorByLocation.TryGetValue(hash, out bool cached)) { return cached; }
            bool? answer = PrefabHasInterior(location);
            if (answer.HasValue == false) { return ResetTargets.HasSkyInterior(zone); }
            interiorByLocation[hash] = answer.Value;
            return answer.Value;
        }

        private static bool? PrefabHasInterior(ZoneSystem.ZoneLocation location) {
            bool loaded = false;
            try {
                location.m_prefab.Load();
                loaded = true;
                GameObject asset = location.m_prefab.Asset;
                if (asset == null) { return null; }
                Location component = asset.GetComponent<Location>();
                if (component != null && component.m_hasInterior) { return true; }
                ZNetView[] views = asset.GetComponentsInChildren<ZNetView>(true);
                for (int i = 0; i < views.Length; i++) {
                    if (views[i] == null) { continue; }
                    if (asset.transform.InverseTransformPoint(views[i].transform.position).y > ZoneProtectionScan.SkyThreshold) { return true; }
                }
                return false;
            } catch (System.Exception e) {
                Logger.LogLocationResetWarning($"Could not read whether '{location.m_prefabName}' has an interior: {e.Message}");
                return null;
            } finally {
                if (loaded) { location.m_prefab.Release(); }
            }
        }

        // ---- Who is where ----
        //
        // Each peer's own reference position, as LocationResetManager.PlayersNearby reads it and for
        // the same reasons, plus the listen-server host. A client's reference position is its player's
        // transform position (Player.Update), so its y says whether it is inside a dungeon -- vanilla's
        // Character.InInterior is exactly y > 3000.

        // A player inside this zone's dungeon. Interiors are confined to their entrance's zone
        // (DungeonGenerator bounds it to the 64m zone square), so the zone is the whole test.
        internal static bool PlayerInside(Vector2s zone) {
            List<ZNetPeer> peers = ZNet.instance != null ? ZNet.instance.m_peers : null;
            if (peers != null) {
                for (int i = 0; i < peers.Count; i++) {
                    ZNetPeer peer = peers[i];
                    if (peer == null || peer.IsReady() == false) { continue; }
                    if (InsideZoneInterior(peer.m_refPos, zone)) { return true; }
                }
            }
            Player host = Player.m_localPlayer;
            return host != null && InsideZoneInterior(host.transform.position, zone);
        }

        private static bool InsideZoneInterior(Vector3 position, Vector2s zone) {
            return position.y > ZoneProtectionScan.SkyThreshold && ZoneSystem.GetZone(position) == zone;
        }

        // A player on the surface within radius of a point. A player in a dungeon is not on the
        // surface, whatever their XZ says: they are held to their own interior's rule instead.
        internal static bool SurfacePlayerNear(Vector3 center, float radius) {
            List<ZNetPeer> peers = ZNet.instance != null ? ZNet.instance.m_peers : null;
            if (peers != null) {
                for (int i = 0; i < peers.Count; i++) {
                    ZNetPeer peer = peers[i];
                    if (peer == null || peer.IsReady() == false) { continue; }
                    if (OnSurfaceWithin(peer.m_refPos, center, radius)) { return true; }
                }
            }
            Player host = Player.m_localPlayer;
            return host != null && OnSurfaceWithin(host.transform.position, center, radius);
        }

        private static bool OnSurfaceWithin(Vector3 position, Vector3 center, float radius) {
            return position.y <= ZoneProtectionScan.SkyThreshold && Utils.DistanceXZ(position, center) <= radius;
        }

        // Whether any player's client has this zone in its active area -- the area whose objects that
        // client instantiates and, for anything it owns, simulates.
        internal static bool ZoneActiveForAnyPlayer(Vector2s zone) {
            if (ZNet.instance == null || ZoneSystem.instance == null) { return false; }
            Vector3 zonePos = ZoneSystem.GetZonePos(zone);
            List<ZNetPeer> peers = ZNet.instance.m_peers;
            if (peers != null) {
                for (int i = 0; i < peers.Count; i++) {
                    ZNetPeer peer = peers[i];
                    if (peer == null || peer.IsReady() == false) { continue; }
                    if (ZNetScene.InActiveArea(zonePos, ZoneSystem.GetZone(peer.m_refPos))) { return true; }
                }
            }
            Player host = Player.m_localPlayer;
            return host != null && ZNetScene.InActiveArea(zonePos, ZoneSystem.GetZone(host.transform.position));
        }

        // Whether the interior holds anything the clear keeps. That matters only while a client has the
        // zone loaded: the rebuild destroys the old DungeonGenerator, its rooms -- floors included --
        // vanish from that client at once, and the new rooms arrive through an async prefab load. A
        // tombstone or item is a physics body the owning client simulates, and a player's build loses
        // its support, so anything kept would fall out of the dungeon or break in that gap. Nobody
        // near means nobody simulating, and the new rooms are in place long before anyone arrives.
        private static bool InteriorKeepsSomething(Vector2s zone, LocationResetData.ResolvedResetEntry entry, long ownerKey) {
            if (ZDOMan.instance == null) { return false; }
            ZoneProtectionScan.BuildPrefabSets();

            zdoBuffer.Clear();
            ZoneObjects.FindObjects(zone, zdoBuffer);
            bool keeps = false;
            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (zdo.GetPosition().y <= ZoneProtectionScan.SkyThreshold) { continue; }
                if (ResetTargets.SurvivesInteriorClear(zdo, entry, ownerKey)) { keeps = true; break; }
            }
            zdoBuffer.Clear();
            return keeps;
        }

        // A human-readable name for a scope, for query results.
        internal static string Describe(Verdict verdict) {
            if (verdict == null || verdict.NothingToDo) { return "none"; }
            switch (verdict.Scope) {
                case Scope.Full: return "full";
                case Scope.Interior: return "interior";
                default: return "held";
            }
        }
    }
}
