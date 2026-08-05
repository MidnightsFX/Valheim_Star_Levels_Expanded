using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // The three reset tiers.
    //
    //  Tier 1 (RefreshZoneInPlace) mutates surviving ZDOs and never loads a zone or destroys
    //         anything, so it can never touch player property.
    //  Tier 2 (vegetation) and Tier 3 (locations) run inside RegenerateZone, which poke-loads the
    //         zone because PlaceVegetation and SpawnLocation need a live Heightmap and colliders.
    internal static class ResetTargets {

        private static readonly List<ZDO> zdoBuffer = new List<ZDO>();

        // -----------------------------------------------------------------------------------
        // Tier 1 - in-place ZDO refresh
        // -----------------------------------------------------------------------------------
        //
        // Everything here is a ZDO write that vanilla's own Awake path picks up the next time the
        // object loads. No zone is loaded, nothing is destroyed and nothing is created, so this
        // cannot duplicate items or damage a build. It is also where most of the throughput comes
        // from: harvested-but-still-present content is the common case on a busy server.
        internal static void RefreshZoneInPlace(Vector2i zone, LocationResetConfigSnapshot cfg) {
            if (ZDOMan.instance == null) { return; }
            if (cfg.RefreshPickables == false && cfg.RefreshMineRocks == false && cfg.RefreshContainerLoot == false) { return; }

            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(zone, zdoBuffer);

            int refreshed = 0;
            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                int prefab = zdo.m_prefab;

                if (cfg.RefreshPickables && ZoneProtectionScan.PickableHashes.Contains(prefab)) {
                    if (DueForRefresh(zone, prefab, cfg) && RefreshPickable(zdo)) { refreshed++; }
                    continue;
                }
                if (cfg.RefreshMineRocks && ZoneProtectionScan.MineRock5Hashes.Contains(prefab)) {
                    if (DueForRefresh(zone, prefab, cfg) && RefreshMineRock5(zdo)) { refreshed++; }
                    continue;
                }
                if (cfg.RefreshMineRocks && ZoneProtectionScan.MineRockAreaCounts.ContainsKey(prefab)) {
                    if (DueForRefresh(zone, prefab, cfg) && RefreshMineRock(zdo, prefab)) { refreshed++; }
                    continue;
                }
                if (cfg.RefreshContainerLoot && ZoneProtectionScan.containerHashes.Contains(prefab)) {
                    if (DueForRefresh(zone, prefab, cfg) && RefreshContainerLoot(zdo)) { refreshed++; }
                }
            }

            zdoBuffer.Clear();
            if (refreshed > 0) {
                Logger.LogLocationReset($"Zone {zone.x},{zone.y}: refreshed {refreshed} objects in place.");
            }
        }

        // A prefab with its own Vegetation config entry uses that entry's timer and opt-in flag;
        // anything else falls back to the global default interval measured from the zone stamp.
        private static bool DueForRefresh(Vector2i zone, int prefabHash, LocationResetConfigSnapshot cfg) {
            if (LocationResetData.TryGetVegetationEntry(prefabHash, out LocationResetData.ResolvedResetEntry entry)) {
                if (entry.Enabled == false) { return false; }
                if (LocationResetState.TryGetEntry(zone, prefabHash, out LocationResetState.EntryRecord record) && record.Stamp > 0) {
                    return LocationResetState.Now - record.Stamp >= entry.ResetSeconds;
                }
                return true;
            }
            if (LocationResetState.TryGetZone(zone, out LocationResetState.ZoneRecord zoneRecord) == false) { return false; }
            return LocationResetState.Now - zoneRecord.ZoneStamp >= cfg.DefaultIntervalSeconds;
        }

        // Pickables that hid instead of despawning keep their ZDO with picked=true. Clearing it is a
        // complete restore: Pickable.Awake reads both keys and re-enables m_hideWhenPicked itself.
        // Pickables with no m_hideWhenPicked destroy their ZDO on pick, so they are never seen here
        // and are handled by Tier 2 instead.
        private static bool RefreshPickable(ZDO zdo) {
            if (zdo.GetBool(ZDOVars.s_picked, false) == false) { return false; }
            TakeOwnership(zdo);
            zdo.Set(ZDOVars.s_picked, false);
            zdo.Set(ZDOVars.s_pickedTime, 0L);
            return true;
        }

        // MineRock5 packs every hit area's health into one base64 string. LoadHealth only applies it
        // when the string is non-empty, so blanking it restores each area to its prefab default.
        private static bool RefreshMineRock5(ZDO zdo) {
            string health = zdo.GetString(ZDOVars.s_health, "");
            if (string.IsNullOrEmpty(health)) { return false; }
            TakeOwnership(zdo);
            zdo.Set(ZDOVars.s_health, "");
            return true;
        }

        // Old-style MineRock keeps one float per hit area. Write explicit full health rather than
        // removing the keys, because ZDO.RemoveFloat does not bump the data revision.
        private static bool RefreshMineRock(ZDO zdo, int prefabHash) {
            if (ZoneProtectionScan.MineRockAreaCounts.TryGetValue(prefabHash, out int areas) == false) { return false; }
            ZoneProtectionScan.MineRockBaseHealth.TryGetValue(prefabHash, out float baseHealth);
            if (baseHealth <= 0f) { return false; }

            float fullHealth = baseHealth;
            if (Game.instance != null) {
                fullHealth += Game.m_worldLevel * baseHealth * Game.instance.m_worldLevelMineHPMultiplier;
            }

            bool changed = false;
            for (int area = 0; area < areas; area++) {
                string key = "Health" + area.ToString();
                if (zdo.GetFloat(key, fullHealth) >= fullHealth) { continue; }
                if (changed == false) { TakeOwnership(zdo); changed = true; }
                zdo.Set(key, fullHealth);
            }
            return changed;
        }

        // Re-roll a container's default loot. Only ever applied to containers with no creator, so a
        // player's chest is never touched even if the category action was relaxed.
        private static bool RefreshContainerLoot(ZDO zdo) {
            if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L) { return false; }
            if (zdo.GetBool(ZDOVars.s_addedDefaultItems, false) == false) { return false; }
            TakeOwnership(zdo);
            zdo.Set(ZDOVars.s_addedDefaultItems, false);
            return true;
        }

        // Vanilla gates replication on ownership, so a write from a non-owner is a local-only edit
        // another peer will overwrite. Claim the ZDO first.
        internal static void TakeOwnership(ZDO zdo) {
            if (zdo.IsOwner()) { return; }
            zdo.SetOwner(ZDOMan.GetSessionID());
        }

        // -----------------------------------------------------------------------------------
        // Tiers 3 and 2 - regeneration (requires a loaded zone)
        // -----------------------------------------------------------------------------------

        // onComplete reports whether the regeneration finished cleanly. A failure must NOT be
        // stamped as done: the clear and the respawn are one operation, and abandoning it in the
        // middle would leave the location permanently empty.
        internal static IEnumerator RegenerateZone(Vector2i zone, LocationResetConfigSnapshot cfg,
                                                   ZoneProtectionScan.ProtectionResult protection,
                                                   bool force, System.Action<bool> onComplete) {
            if (ZoneSystem.instance == null || ZDOMan.instance == null) { onComplete?.Invoke(false); yield break; }

            int zdosBefore = ZoneProtectionScan.SectorZdoCount(zone);

            bool loaded = false;
            yield return ZoneLoader.Load(zone, cfg.MaxZoneLoadWaitSeconds, (ok) => { loaded = ok; });
            if (loaded == false) { onComplete?.Invoke(false); yield break; }

            bool succeeded = true;
            try {
                // Vanilla's own ordering: locations first so vegetation sees the fresh clear areas.
                RegenerateLocation(zone, cfg, force);
                RegenerateVegetation(zone, cfg, force);
            } catch (System.Exception e) {
                succeeded = false;
                Logger.LogError($"[LocationReset] Reset of zone {zone.x},{zone.y} failed and will be retried: {e}");
            } finally {
                ZoneLoader.Release(zone);
            }

            // Balanced accounting. A faithful restore returns the sector to its original ZDO count;
            // sustained growth is how a reset system silently bloats a world save over months.
            int zdosAfter = ZoneProtectionScan.SectorZdoCount(zone);
            int growth = zdosAfter - zdosBefore;
            if (growth != 0) { LocationResetManager.ZdoGrowthTotal += growth; }

            if (succeeded == false) {
                // Clear and respawn are one operation. Retry soon rather than leaving a location
                // cleared but not rebuilt.
                LocationResetState.BackoffZone(zone, 60f);
            } else if (growth > cfg.ZdoGrowthTolerance) {
                succeeded = false;
                Logger.LogError($"[LocationReset] Zone {zone.x},{zone.y} gained {growth} ZDOs during a reset " +
                    $"(before {zdosBefore}, after {zdosAfter}). Backing this zone off for a day to avoid world bloat.");
                LocationResetState.BackoffZone(zone, 86400f);
            }

            // Reported last so this method owns every backoff decision; the caller only stamps the
            // zone as done when we report success, and never overwrites a backoff we just applied.
            onComplete?.Invoke(succeeded);
        }

        // -----------------------------------------------------------------------------------
        // Tier 3 - location reset
        // -----------------------------------------------------------------------------------
        //
        // Vanilla PlaceLocations rolls a location's rotation from the LIVE Random state before it
        // seeds anything, so replaying it gives a different orientation every time -- which is why
        // Upgrade World's resets visibly rotate buildings. We bypass PlaceLocations entirely:
        // capture the existing LocationProxy's transform, clear, then call SpawnLocation directly
        // with that exact position and rotation. The result is a restore rather than a re-roll.
        //
        // SpawnLocation also re-runs DungeonGenerator.Generate internally, and Generate seeds
        // Random.InitState(seed) before laying out rooms, so interiors come back deterministically
        // too. (Radial camps still vary slightly: their wall placement collision-tests against live
        // colliders, which differ between runs.)
        private static void RegenerateLocation(Vector2i zone, LocationResetConfigSnapshot cfg, bool force) {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { return; }
            if (instance.m_location == null) { return; }

            int locationHash = instance.m_location.Hash;
            if (LocationResetData.TryGetLocationEntry(locationHash, out LocationResetData.ResolvedResetEntry entry) == false) { return; }
            if (entry.Enabled == false) { return; }
            // Hard-blocked locations are never resettable, no matter the config or a force command.
            if (LocationResetData.HardBlockedLocations.Contains(entry.Name)) { return; }

            ZDO proxy = FindLocationProxy(zone, locationHash);
            if (proxy == null) { return; }

            if (force == false) {
                // Per-location timer rides on the proxy ZDO so it survives even if the state file is lost.
                long lastReset = proxy.GetLong(DataObjects.SLS_LOC_RESET, 0L);
                if (lastReset > 0 && LocationResetState.Now - lastReset < entry.ResetSeconds) { return; }
                if (lastReset == 0) {
                    // First sight of this location: stamp it and let the next cycle do the work, so
                    // installing the mod never resets everything at once.
                    TakeOwnership(proxy);
                    proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
                    return;
                }
            }

            Vector3 position = proxy.GetPosition();
            Quaternion rotation = proxy.GetRotation();
            float exteriorRadius = instance.m_location.m_exteriorRadius;
            float terrainRadius = entry.TerrainRadius > 0f ? entry.TerrainRadius : exteriorRadius;

            // Boss altars and similar: undo the crater players dug, leave the location itself alone.
            if (entry.Mode == LocationResetMode.TerrainOnly) {
                int undone = TerrainResetter.Reset(position, terrainRadius);
                TakeOwnership(proxy);
                proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
                Logger.LogLocationReset($"Zone {zone.x},{zone.y}: terrain-only reset of '{entry.Name}' ({undone} modifications undone).");
                return;
            }

            int cleared = ClearLocation(zone, position, exteriorRadius, instance.m_location, entry);
            if (entry.ResetTerrain) { TerrainResetter.Reset(position, terrainRadius); }

            int seed = WorldGenerator.instance.GetSeed() + (zone.x * 4271) + (zone.y * 9187);
            List<GameObject> ghosts = new List<GameObject>();
            try {
                instance.m_location.m_prefab.Load();
                zs.SpawnLocation(instance.m_location, seed, position, rotation, ZoneSystem.SpawnMode.Ghost, ghosts);
            } finally {
                // SpawnLocation balances StartGhostInit/FinishGhostInit itself, but the flag is a
                // global static: if vanilla ever throws between them, every object spawned anywhere
                // in the game afterwards becomes a ghost. Force it closed.
                ZNetView.FinishGhostInit();
                for (int i = 0; i < ghosts.Count; i++) {
                    if (ghosts[i] != null) { Object.Destroy(ghosts[i]); }
                }
                instance.m_location.m_prefab.Release();
            }

            TakeOwnership(proxy);
            proxy.Set(DataObjects.SLS_LOC_RESET, LocationResetState.Now);
            Logger.LogLocationReset($"Zone {zone.x},{zone.y}: reset location '{entry.Name}' " +
                $"(cleared {cleared}, respawned {ghosts.Count} objects).");
        }

        internal static ZDO FindLocationProxy(Vector2i zone, int locationHash) {
            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(zone, zdoBuffer);
            ZDO found = null;
            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (zdo.m_prefab != ZoneProtectionScan.LocationProxyHash) { continue; }
                if (zdo.GetInt(ZDOVars.s_location, 0) != locationHash) { continue; }
                found = zdo;
                break;
            }
            zdoBuffer.Clear();
            return found;
        }

        // Destroy the location's contents so SpawnLocation can lay them down fresh.
        //
        // The exterior scan covers the zone AND its 8 neighbours, because a location's radius
        // routinely crosses a zone boundary -- Upgrade World's single-sector scan is a documented
        // source of leftover objects on the far side of the line.
        //
        // The interior of a sky dungeon lives at y > 4000. It is cleared too when configured, since
        // SpawnLocation regenerates it via DungeonGenerator.Generate and would otherwise duplicate it.
        private static int ClearLocation(Vector2i zone, Vector3 center, float exteriorRadius,
                                         ZoneSystem.ZoneLocation location, LocationResetData.ResolvedResetEntry entry) {
            // Whether this location has an interior is decided by what is actually in the world: a
            // DungeonGenerator parked above the sky threshold. ZoneLocation carries m_interiorRadius
            // but not m_hasInterior (that lives on the Location component, which would mean loading
            // the asset), and this also covers locations whose interior sits somewhere unexpected.
            Vector3 interiorCenter = Vector3.zero;
            bool clearInterior = entry.ResetInterior && TryFindInteriorCenter(zone, out interiorCenter);
            float interiorRadius = location.m_interiorRadius > 0f ? location.m_interiorRadius : exteriorRadius;

            List<ZDO> doomed = new List<ZDO>();
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    CollectClearable(new Vector2i(zone.x + dx, zone.y + dy), center, exteriorRadius,
                        clearInterior, interiorCenter, interiorRadius, entry, doomed);
                }
            }

            for (int i = 0; i < doomed.Count; i++) { DestroyZdo(doomed[i]); }
            return doomed.Count;
        }

        // Sky interiors sit directly above their entrance for vanilla locations, but a location using
        // a custom interior transform can place it elsewhere. Locating the live DungeonGenerator
        // handles both, and its absence is a reliable "this location has no interior".
        private static bool TryFindInteriorCenter(Vector2i zone, out Vector3 center) {
            center = Vector3.zero;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    zdoBuffer.Clear();
                    ZDOMan.instance.FindObjects(new Vector2i(zone.x + dx, zone.y + dy), zdoBuffer);
                    for (int i = 0; i < zdoBuffer.Count; i++) {
                        ZDO zdo = zdoBuffer[i];
                        if (zdo == null || zdo.IsValid() == false) { continue; }
                        if (ZoneProtectionScan.DungeonGeneratorHashes.Contains(zdo.m_prefab) == false) { continue; }
                        Vector3 pos = zdo.GetPosition();
                        if (pos.y <= SkyThreshold) { continue; }
                        center = pos;
                        zdoBuffer.Clear();
                        return true;
                    }
                    zdoBuffer.Clear();
                }
            }
            return false;
        }

        // Vanilla parks dungeon interiors several thousand metres up; 4000 is the same cut-off both
        // reference mods use to tell interior from exterior.
        private const float SkyThreshold = 4000f;

        private static void CollectClearable(Vector2i sector, Vector3 center, float exteriorRadius,
                                             bool clearInterior, Vector3 interiorCenter, float interiorRadius,
                                             LocationResetData.ResolvedResetEntry entry, List<ZDO> doomed) {
            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(sector, zdoBuffer);

            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (IsStructural(zdo) || IsPlayer(zdo)) { continue; }

                Vector3 pos = zdo.GetPosition();
                bool inSky = pos.y > SkyThreshold;

                if (inSky) {
                    if (clearInterior == false) { continue; }
                    if (Utils.DistanceXZ(pos, interiorCenter) > interiorRadius) { continue; }
                } else {
                    if (Utils.DistanceXZ(pos, center) > exteriorRadius) { continue; }
                }

                // Anything the protection policy marked Preserve stays; Block never reaches here
                // because a blocking hit aborts the whole zone before it is loaded.
                if (ShouldPreserve(zdo, entry)) { continue; }

                doomed.Add(zdo);
            }

            zdoBuffer.Clear();
        }

        // The zone controller and terrain compiler ARE the zone, and the location proxy is the
        // identity and timestamp carrier for the location we are resetting.
        private static bool IsStructural(ZDO zdo) {
            return zdo.m_prefab == ZoneProtectionScan.ZoneCtrlHash
                || zdo.m_prefab == ZoneProtectionScan.TerrainCompilerHash
                || zdo.m_prefab == ZoneProtectionScan.LocationProxyHash;
        }

        private static bool IsPlayer(ZDO zdo) {
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetZDOID() == zdo.m_uid) { return true; }
            if (ZNet.instance == null || ZNet.instance.m_peers == null) { return false; }
            for (int i = 0; i < ZNet.instance.m_peers.Count; i++) {
                if (ZNet.instance.m_peers[i].m_characterID == zdo.m_uid) { return true; }
            }
            return false;
        }

        private static bool ShouldPreserve(ZDO zdo, LocationResetData.ResolvedResetEntry entry) {
            if (LocationResetData.ExtraProtectedPrefabHashes.Contains(zdo.m_prefab)) { return true; }
            if (entry == null) { return false; }
            // Player-built content inside a location that the admin chose to reset around.
            if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L) {
                return entry.ActionFor(ProtectionCategory.PlayerBuiltPiece) != ProtectionAction.Ignore;
            }
            return false;
        }

        // Seizing ownership first is what makes the delete network-authoritative. Vanilla's
        // ZNetScene.Destroy only tears down the ZDO when the caller owns it; without this the object
        // merely despawns locally and another peer re-broadcasts it.
        private static void DestroyZdo(ZDO zdo) {
            if (zdo == null || zdo.IsValid() == false) { return; }
            TakeOwnership(zdo);
            if (ZNetScene.instance != null && ZNetScene.instance.m_instances.TryGetValue(zdo, out ZNetView view) && view != null) {
                ZNetScene.instance.Destroy(view.gameObject);
                return;
            }
            ZDOMan.instance.DestroyZDO(zdo);
        }

        // Tier 2. Re-runs vanilla PlaceVegetation with only the due prefabs enabled.
        //
        // PlaceVegetation seeds each entry with
        //   worldSeed + zoneX*4271 + zoneY*9187 + prefabName.GetStableHashCode()
        // so replaying it for one prefab in one zone reproduces that prefab's ORIGINAL positions,
        // rotations and scales exactly. Surviving nodes still have colliders, so vanilla's IsBlocked
        // raycast skips their spots and nothing is duplicated -- which is also why surviving
        // instances must NOT be pre-deleted.
        private static void RegenerateVegetation(Vector2i zone, LocationResetConfigSnapshot cfg, bool force) {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs.m_vegetation == null || zs.m_vegetation.Count == 0) { return; }
            if (zs.m_zones.TryGetValue(zone, out ZoneSystem.ZoneData zoneData) == false || zoneData?.m_root == null) { return; }

            Heightmap heightmap = zoneData.m_root.GetComponentInChildren<Heightmap>();
            if (heightmap == null) { return; }

            List<ZoneSystem.ZoneVegetation> due = SelectDueVegetation(zone, force, out List<int> dueHashes);
            if (due.Count == 0) { return; }

            List<ZoneSystem.ZoneVegetation> original = zs.m_vegetation;
            List<GameObject> ghosts = new List<GameObject>();
            Vector3 zonePos = ZoneSystem.GetZonePos(zone);

            try {
                zs.m_vegetation = due;
                zs.m_tempClearAreas.Clear();
                AddLocationClearArea(zone, zs.m_tempClearAreas);

                // Ghost mode creates the ZDOs but leaves the GameObjects unregistered, which is how
                // vanilla pre-generates zones. The objects are throwaway; the ZDOs are the result.
                zs.PlaceVegetation(zone, zonePos, zoneData.m_root.transform, heightmap,
                    zs.m_tempClearAreas, ZoneSystem.SpawnMode.Ghost, ghosts);
            } finally {
                // Restoring the shared vegetation list is not optional: leaving the filtered list in
                // place would break normal world generation for the rest of the session.
                zs.m_vegetation = original;
                zs.m_tempClearAreas.Clear();
                // Destroy in finally so a throw mid-loop cannot leak orphan GameObjects holding
                // live ZDOs.
                for (int i = 0; i < ghosts.Count; i++) {
                    if (ghosts[i] != null) { Object.Destroy(ghosts[i]); }
                }
            }

            if (ghosts.Count > 0) {
                Logger.LogLocationReset($"Zone {zone.x},{zone.y}: regenerated {ghosts.Count} vegetation objects across {due.Count} entries.");
                ApplyVegetationTerrainReset(due, dueHashes, ghosts);
            }

            for (int i = 0; i < dueHashes.Count; i++) {
                LocationResetState.StampEntry(zone, dueHashes[i], 0);
            }
        }

        // Clone the vegetation list with only the due entries enabled. Cloning matters because
        // PlaceVegetation reads m_enable off the shared entries; mutating the originals in place
        // would corrupt world generation.
        private static List<ZoneSystem.ZoneVegetation> SelectDueVegetation(Vector2i zone, bool force, out List<int> dueHashes) {
            List<ZoneSystem.ZoneVegetation> due = new List<ZoneSystem.ZoneVegetation>();
            dueHashes = new List<int>();

            foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation) {
                if (veg?.m_prefab == null) { continue; }
                int hash = veg.m_prefab.name.GetStableHashCode();
                if (LocationResetData.TryGetVegetationEntry(hash, out LocationResetData.ResolvedResetEntry entry) == false) { continue; }
                if (entry.Enabled == false) { continue; }

                if (force == false && LocationResetState.TryGetEntry(zone, hash, out LocationResetState.EntryRecord record)) {
                    if (record.Stamp > 0 && LocationResetState.Now - record.Stamp < entry.ResetSeconds) { continue; }
                    // Nothing missing here, nothing to regenerate.
                    if (record.Baseline == 0) { continue; }
                }

                ZoneSystem.ZoneVegetation clone = veg.Clone();
                clone.m_enable = true;
                // Without the block check vanilla would happily stack a fresh copy on top of the
                // surviving one every single reset. Force it on rather than growing the world.
                if (clone.m_blockCheck == false) {
                    Logger.LogLocationReset($"Vegetation '{veg.m_prefab.name}' has no block check; forcing it on to prevent duplicate placement.");
                    clone.m_blockCheck = true;
                }
                due.Add(clone);
                dueHashes.Add(hash);
            }

            return due;
        }

        // Mining leaves a crater. For entries configured with ResetTerrain, flatten it back around
        // each regenerated node.
        private static void ApplyVegetationTerrainReset(List<ZoneSystem.ZoneVegetation> due, List<int> dueHashes, List<GameObject> ghosts) {
            bool anyTerrain = false;
            for (int i = 0; i < dueHashes.Count; i++) {
                if (LocationResetData.TryGetVegetationEntry(dueHashes[i], out LocationResetData.ResolvedResetEntry entry) && entry.ResetTerrain) {
                    anyTerrain = true;
                    break;
                }
            }
            if (anyTerrain == false) { return; }

            for (int i = 0; i < ghosts.Count; i++) {
                GameObject ghost = ghosts[i];
                if (ghost == null) { continue; }
                int hash = Utils.GetPrefabName(ghost).GetStableHashCode();
                if (LocationResetData.TryGetVegetationEntry(hash, out LocationResetData.ResolvedResetEntry entry) == false) { continue; }
                if (entry.ResetTerrain == false) { continue; }
                float radius = entry.TerrainRadius > 0f ? entry.TerrainRadius : 8f;
                TerrainResetter.Reset(ghost.transform.position, radius);
            }
        }

        // Vegetation must not spawn inside a location footprint. Vanilla builds these clear areas
        // during PlaceLocations; since we are calling PlaceVegetation on its own, rebuild them.
        private static void AddLocationClearArea(Vector2i zone, List<ZoneSystem.ClearArea> clearAreas) {
            if (ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) == false) { return; }
            if (instance.m_location == null || instance.m_location.m_clearArea == false) { return; }
            clearAreas.Add(new ZoneSystem.ClearArea(instance.m_position, instance.m_location.m_exteriorRadius));
        }
    }
}
