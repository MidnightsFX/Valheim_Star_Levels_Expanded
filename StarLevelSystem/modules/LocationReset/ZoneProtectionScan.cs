using StarLevelSystem.Data;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LocationReset {
    // Reads a zone's ZDOs straight out of ZDOMan's sector index, without instantiating anything.
    // Everything here must stay load-free: this is the filter that decides whether a zone is worth
    // the expensive poke-load path at all, so it runs against every candidate zone in the world.
    internal static class ZoneProtectionScan {

        // Reused across calls; the sweep is single-threaded so a shared buffer avoids per-zone garbage.
        private static readonly List<ZDO> zdoBuffer = new List<ZDO>();

        // Prefab-hash sets, built once from ZNetScene's prefab list. Classifying by component up
        // front means the per-ZDO check during a scan is a hash-set lookup rather than a prefab
        // resolve, which matters when this runs over every zone in the world.
        private static readonly HashSet<int> pieceHashes = new HashSet<int>();
        private static readonly HashSet<int> tombstoneHashes = new HashSet<int>();
        private static readonly HashSet<int> wardHashes = new HashSet<int>();
        private static readonly HashSet<int> portalHashes = new HashSet<int>();
        private static readonly HashSet<int> bedHashes = new HashSet<int>();
        internal static readonly HashSet<int> containerHashes = new HashSet<int>();
        private static readonly HashSet<int> tameableHashes = new HashSet<int>();
        private static readonly HashSet<int> itemDropHashes = new HashSet<int>();

        // Tier 1 targets. Kept as separate sets so the in-place refresh never has to resolve a
        // prefab at scan time.
        internal static readonly HashSet<int> PickableHashes = new HashSet<int>();
        internal static readonly HashSet<int> MineRock5Hashes = new HashSet<int>();
        // Old-style MineRock stores per-area health as floats under "Health<index>", defaulting to
        // full when absent. Removing those keys does not bump the ZDO data revision, so the refresh
        // writes explicit full-health values instead -- which needs the area count and base health
        // from the prefab.
        internal static readonly Dictionary<int, int> MineRockAreaCounts = new Dictionary<int, int>();
        internal static readonly Dictionary<int, float> MineRockBaseHealth = new Dictionary<int, float>();
        // Every registered prefab name by hash. Lets a reset group name a target that appears in no
        // ZoneSystem placement list -- a dungeon pickable, say -- and still get a real name on the
        // resolved entry for logs, plus tells LocationResetData whether a member name exists at all.
        internal static readonly Dictionary<int, string> PrefabNamesByHash = new Dictionary<int, string>();
        // A one-shot CreatureSpawner records what it spawned as a ZDO connection, so clearing a
        // location means following those links and taking each spawner's creature with it.
        internal static readonly HashSet<int> CreatureSpawnerHashes = new HashSet<int>();
        // Player terraforming ops that persist as their own ZDOs. They have to be instantiated before
        // a terrain reset can see them, because TerrainModifier.GetAllInstances only reports live
        // components -- see ZoneLoader.CreateTerrainObjects.
        internal static readonly HashSet<int> TerrainModifierHashes = new HashSet<int>();

        private static bool prefabSetsBuilt = false;

        // Objects a reset must never destroy for structural reasons rather than ownership: the zone
        // controller and the terrain compiler are the zone itself, and the location proxy is the
        // identity and timestamp carrier for the location being reset.
        internal static readonly int ZoneCtrlHash = "_ZoneCtrl".GetStableHashCode();
        internal static readonly int TerrainCompilerHash = "_TerrainCompiler".GetStableHashCode();
        internal static readonly int LocationProxyHash = "LocationProxy".GetStableHashCode();

        // Above this altitude a ZDO belongs to a dungeon interior rather than the surface. Vanilla
        // parks interiors at their entrance's y + 5000 (Location.Awake) and defines "indoors" as
        // y > 3000 (Character.InInterior), so this matches the engine rather than guessing. Nothing
        // legitimate sits between the highest terrain (~350m) and here.
        internal const float SkyThreshold = 3000f;

        // Outcome of scanning a zone for player property.
        internal class ProtectionResult {
            // At least one object mapped to a Block action; the zone must not be touched.
            internal bool Blocked;
            // What blocked it, and where, so an admin can walk to the structure that is holding a
            // chunk back rather than guessing which of their builds is in range.
            internal ProtectionCategory BlockingCategory;
            internal int BlockingPrefabHash;
            internal Vector3 BlockingPosition;
            // No Preserve set here. This scan runs zone-wide with entry: null, so it can only judge
            // against Defaults, and it happens before any location is chosen. Which individual
            // objects survive a clear is decided per object by ResetTargets.ShouldPreserve, against
            // the resolved entry's own rules.
        }

        internal static void BuildPrefabSets() {
            if (prefabSetsBuilt) { return; }
            if (ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null) { return; }

            pieceHashes.Clear();
            tombstoneHashes.Clear();
            wardHashes.Clear();
            portalHashes.Clear();
            bedHashes.Clear();
            containerHashes.Clear();
            tameableHashes.Clear();
            itemDropHashes.Clear();
            PickableHashes.Clear();
            MineRock5Hashes.Clear();
            MineRockAreaCounts.Clear();
            MineRockBaseHealth.Clear();
            CreatureSpawnerHashes.Clear();
            TerrainModifierHashes.Clear();
            PrefabNamesByHash.Clear();

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs) {
                if (prefab == null) { continue; }
                int hash = prefab.name.GetStableHashCode();
                PrefabNamesByHash[hash] = prefab.name;
                if (prefab.GetComponent<Piece>() != null) { pieceHashes.Add(hash); }
                if (prefab.GetComponent<TombStone>() != null) { tombstoneHashes.Add(hash); }
                if (prefab.GetComponent<PrivateArea>() != null) { wardHashes.Add(hash); }
                if (prefab.GetComponent<Teleport>() != null) { portalHashes.Add(hash); }
                if (prefab.GetComponent<Bed>() != null) { bedHashes.Add(hash); }
                if (prefab.GetComponent<Container>() != null) { containerHashes.Add(hash); }
                if (prefab.GetComponent<Tameable>() != null) { tameableHashes.Add(hash); }
                if (prefab.GetComponent<ItemDrop>() != null) { itemDropHashes.Add(hash); }

                if (prefab.GetComponent<Pickable>() != null) { PickableHashes.Add(hash); }
                if (prefab.GetComponent<MineRock5>() != null) { MineRock5Hashes.Add(hash); }
                if (prefab.GetComponent<CreatureSpawner>() != null) { CreatureSpawnerHashes.Add(hash); }
                if (prefab.GetComponent<TerrainModifier>() != null) { TerrainModifierHashes.Add(hash); }
                MineRock mineRock = prefab.GetComponent<MineRock>();
                if (mineRock != null && mineRock.m_hitAreas != null) {
                    MineRockAreaCounts[hash] = mineRock.m_hitAreas.Length;
                    MineRockBaseHealth[hash] = mineRock.m_health;
                }
            }

            prefabSetsBuilt = true;
            Logger.LogLocationReset($"Protection prefab sets built: {pieceHashes.Count} pieces, {tombstoneHashes.Count} tombstones, " +
                $"{wardHashes.Count} wards, {portalHashes.Count} portals, {containerHashes.Count} containers, {itemDropHashes.Count} item drops.");
        }

        internal static void ResetPrefabSets() {
            prefabSetsBuilt = false;
        }

        // Classify a single ZDO. Returns false when the object is ordinary resettable content.
        // Ownership checks read the ZDO directly so this works on unloaded zones.
        //
        // internal because ResetTargets.ShouldPreserve needs the same classification: the scan decides
        // whether a zone may be touched at all, the clear decides which individual objects survive it,
        // and the two must agree about what a thing IS even when they disagree about what to do.
        // Callers must have run BuildPrefabSets.
        internal static bool TryClassify(ZDO zdo, out ProtectionCategory category) {
            category = ProtectionCategory.PlayerBuiltPiece;
            int prefab = zdo.m_prefab;

            // Tombstones are unconditional: a player's dropped gear is in there regardless of who
            // "created" it. Checked FIRST so no ignore list can ever expose one -- this is why the
            // ignore list needs no separate "never ignorable" blocklist.
            if (tombstoneHashes.Contains(prefab)) { category = ProtectionCategory.Tombstone; return true; }

            // Anything the admin listed explicitly is treated as a player-built piece. Ahead of the
            // ignore check below, so a prefab in both lists fails closed and keeps protecting.
            if (LocationResetData.ExtraProtectedPrefabHashes.Contains(prefab)) { return true; }

            long creator = zdo.GetLong(ZDOVars.s_creator, 0L);

            if (wardHashes.Contains(prefab)) { category = ProtectionCategory.Ward; return true; }
            if (portalHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.Portal; return true; }
            if (bedHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.Bed; return true; }
            // Vanilla loot chests inside locations have no creator and are meant to be reset; a chest
            // a player placed does, and holds their items.
            if (containerHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.Container; return true; }
            if (tameableHashes.Contains(prefab) && zdo.GetBool(ZDOVars.s_tamed, false)) { category = ProtectionCategory.TamedCreature; return true; }
            if (itemDropHashes.Contains(prefab)) { category = ProtectionCategory.DroppedItem; return true; }
            // Generic player construction. Checked last so the more specific categories win.
            if (pieceHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.PlayerBuiltPiece; return true; }

            return false;
        }

        // Scan a zone and its 8 neighbours for player property. Neighbours are included because a
        // location's exterior radius routinely crosses a zone boundary, and a base just over the line
        // is still a base -- Upgrade World's single-sector scan is a documented source of stale
        // objects and half-cleared locations.
        internal static ProtectionResult ScanZone(Vector2i zone, LocationResetData.ResolvedResetEntry entry, bool includeNeighbours) {
            ProtectionResult result = new ProtectionResult();
            if (ZDOMan.instance == null) { return result; }
            BuildPrefabSets();

            int range = includeNeighbours ? 1 : 0;
            for (int dx = -range; dx <= range; dx++) {
                for (int dy = -range; dy <= range; dy++) {
                    if (ScanSector(new Vector2i(zone.x + dx, zone.y + dy), entry, result)) {
                        // A Block hit is decisive; no point scanning the rest.
                        return result;
                    }
                }
            }
            return result;
        }

        // Returns true if this sector produced a blocking hit.
        private static bool ScanSector(Vector2i sector, LocationResetData.ResolvedResetEntry entry, ProtectionResult result) {
            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(sector, zdoBuffer);

            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (TryClassify(zdo, out ProtectionCategory category) == false) { continue; }

                // Ignored for this category: ordinary resettable content. Applied after classification
                // so an exemption is scoped to the category it was written under -- listing a prefab
                // under PlayerBuiltPiece cannot make it ignorable as a Tombstone.
                if (IsIgnored(entry, category, zdo.m_prefab)) { continue; }

                ProtectionAction action = entry != null
                    ? entry.ActionFor(category)
                    : DefaultActionFor(category);

                // Only Block is decided here. Preserve and Ignore both mean "this zone may be reset",
                // and which objects inside it survive is ShouldPreserve's call at clear time.
                if (action == ProtectionAction.Block) {
                    result.Blocked = true;
                    result.BlockingCategory = category;
                    result.BlockingPrefabHash = zdo.m_prefab;
                    result.BlockingPosition = zdo.GetPosition();
                    zdoBuffer.Clear();
                    return true;
                }
            }

            zdoBuffer.Clear();
            return false;
        }

        // "protected by PlayerBuiltPiece 'wood_floor' at x=-742 z=2251". Only ever called for a chunk
        // that is actually being reported, so resolving the prefab here costs nothing on the hot path.
        internal static string DescribeBlock(ProtectionResult result) {
            if (result == null || result.Blocked == false) { return "not blocked"; }
            string prefab = result.BlockingPrefabHash.ToString();
            if (ZNetScene.instance != null) {
                GameObject go = ZNetScene.instance.GetPrefab(result.BlockingPrefabHash);
                if (go != null) { prefab = go.name; }
            }
            return $"protected by {result.BlockingCategory} '{prefab}' at x={result.BlockingPosition.x:0} z={result.BlockingPosition.z:0}";
        }

        // The zone-wide scan runs before any specific location entry is known (ScanZone is called with
        // a null entry), so the default rules are what actually decide whether a chunk is blocked.
        internal static bool IsIgnored(LocationResetData.ResolvedResetEntry entry, ProtectionCategory category, int prefabHash) {
            if (entry != null) { return entry.Ignores(category, prefabHash); }
            return LocationResetData.DefaultIgnores(category, prefabHash);
        }

        private static ProtectionAction DefaultActionFor(ProtectionCategory category) {
            Dictionary<ProtectionCategory, ProtectionRule> defaults =
                LocationResetData.SLE_LocationReset_Settings?.Defaults?.Protection;
            if (defaults != null && defaults.TryGetValue(category, out ProtectionRule rule) && rule != null) { return rule.Action; }
            return ProtectionAction.Block;
        }

        // Live counts of every configured vegetation prefab present in a sector, keyed by prefab hash.
        // Only prefabs the config tracks are counted, so the dictionary stays small.
        internal static Dictionary<int, ushort> CensusZone(Vector2i zone) {
            Dictionary<int, ushort> counts = new Dictionary<int, ushort>();
            if (ZDOMan.instance == null) { return counts; }
            if (LocationResetData.VegetationByPrefabHash.Count == 0) { return counts; }

            zdoBuffer.Clear();
            ZDOMan.instance.FindObjects(zone, zdoBuffer);

            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (LocationResetData.VegetationByPrefabHash.ContainsKey(zdo.m_prefab) == false) { continue; }
                counts.TryGetValue(zdo.m_prefab, out ushort current);
                if (current < ushort.MaxValue) { counts[zdo.m_prefab] = (ushort)(current + 1); }
            }

            zdoBuffer.Clear();
            return counts;
        }

        // Record the current contents of a zone as its baseline. Called on first sight and after a
        // successful reset, so "below baseline" always means "a player destroyed something since".
        internal static void RecordBaseline(Vector2i zone) {
            Dictionary<int, ushort> counts = CensusZone(zone);
            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> tracked in LocationResetData.VegetationByPrefabHash) {
                counts.TryGetValue(tracked.Key, out ushort present);
                LocationResetState.SetBaseline(zone, tracked.Key, present);
            }
        }

        // ZDO count over a chunk AND its 8 neighbours, for the before/after accounting that catches a
        // reset leaking ZDOs into the world save. The 3x3 block is the footprint a reset actually
        // mutates: ClearLocation sweeps it, and a location's spawned children routinely land in a
        // neighbouring sector. An earlier single-sector version compared two different areas.
        //
        // Surface and interior are counted SEPARATELY, and the split is the whole point. A dungeon
        // interior shares its entrance's sector -- ZoneSystem.GetZone is xz-only and vanilla parks the
        // interior directly overhead -- so it lands in this count, and a regenerated dungeon comes
        // back with a legitimately different room layout and object count. Folding that into the
        // drift figure reported every chunk containing a dungeon as leaking.
        //
        // Pass the per-prefab dictionaries to also break each side down by prefab hash. That costs a
        // dictionary write per ZDO, so the sweep leaves them null and only the debug growth
        // breakdown asks for them.
        internal static int BlockZdoCount(Vector2i zone, out int interiorCount,
                                          Dictionary<int, int> surfaceByPrefab = null,
                                          Dictionary<int, int> interiorByPrefab = null) {
            interiorCount = 0;
            if (ZDOMan.instance == null) { return 0; }

            int surfaceCount = 0;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    zdoBuffer.Clear();
                    ZDOMan.instance.FindObjects(new Vector2i(zone.x + dx, zone.y + dy), zdoBuffer);
                    for (int i = 0; i < zdoBuffer.Count; i++) {
                        ZDO zdo = zdoBuffer[i];
                        if (zdo == null) { continue; }
                        bool interior = zdo.GetPosition().y > SkyThreshold;
                        if (interior) { interiorCount++; } else { surfaceCount++; }

                        Dictionary<int, int> breakdown = interior ? interiorByPrefab : surfaceByPrefab;
                        if (breakdown == null) { continue; }
                        breakdown.TryGetValue(zdo.m_prefab, out int current);
                        breakdown[zdo.m_prefab] = current + 1;
                    }
                }
            }
            zdoBuffer.Clear();
            return surfaceCount;
        }

        // Readable name for a prefab hash, for logs. Falls back to the hash itself, which still
        // identifies the prefab even when ZNetScene has not been walked yet.
        internal static string PrefabNameFor(int prefabHash) {
            if (PrefabNamesByHash.TryGetValue(prefabHash, out string name)) { return name; }
            return $"#{prefabHash}";
        }

        // Where every configured vegetation prefab currently sits, keyed by prefab hash. Positions are
        // XZ only: vegetation placement is seeded per prefab per zone, so a surviving node replays at
        // an identical XZ, while its y is re-snapped to terrain that may have moved.
        //
        // Taken BEFORE a regeneration so the freshly placed ghosts cannot match themselves -- a ZDO
        // enters the sector index synchronously on creation.
        //
        // Scans the 3x3 block, not just this sector. Vanilla insets group centres by
        // 32 - m_groupRadius so members stay within +/-32 of the zone centre, but GetZone floors
        // (x + 32) / 64, so a node right on the +32 edge rounds into the NEIGHBOURING sector. A
        // survivor this index cannot see is a survivor the replay duplicates -- and permanently,
        // because each new copy lands in the same blind spot and is missed again next pass. That was
        // a deterministic +1 per reset on chunks with group-spawned prefabs like MineRock_Tin.
        //
        // Matching stays prefab hash + XZ within a tight epsilon, which is unambiguous across a 3x3:
        // zones are 64m apart, so a same-prefab node from a neighbour cannot sit that close to ours
        // unless it IS ours.
        internal static Dictionary<int, List<Vector2>> TrackedVegetationPositions(Vector2i zone) {
            Dictionary<int, List<Vector2>> positions = new Dictionary<int, List<Vector2>>();
            if (ZDOMan.instance == null) { return positions; }
            if (LocationResetData.VegetationByPrefabHash.Count == 0) { return positions; }

            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    zdoBuffer.Clear();
                    ZDOMan.instance.FindObjects(new Vector2i(zone.x + dx, zone.y + dy), zdoBuffer);

                    for (int i = 0; i < zdoBuffer.Count; i++) {
                        ZDO zdo = zdoBuffer[i];
                        if (zdo == null || zdo.IsValid() == false) { continue; }
                        if (LocationResetData.VegetationByPrefabHash.ContainsKey(zdo.m_prefab) == false) { continue; }
                        if (positions.TryGetValue(zdo.m_prefab, out List<Vector2> list) == false) {
                            list = new List<Vector2>();
                            positions[zdo.m_prefab] = list;
                        }
                        Vector3 p = zdo.GetPosition();
                        list.Add(new Vector2(p.x, p.z));
                    }
                }
            }

            zdoBuffer.Clear();
            return positions;
        }
    }
}
