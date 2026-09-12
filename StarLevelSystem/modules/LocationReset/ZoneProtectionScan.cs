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
        // Every spawner type a location can contain, matching ResetTargets.IsSpawnerChild. Wider than
        // CreatureSpawnerHashes on purpose: that set exists for vanilla's one-connection link, while
        // this one is what SpawnerLinks arms its spawn context on and what its reconnect pass walks.
        // The gap between the two is where SpawnArea nests used to leave their creatures behind.
        internal static readonly HashSet<int> SpawnerHashes = new HashSet<int>();
        // The same set by name, because ZDOMan.GetAllZDOsWithPrefabIterative takes a prefab name.
        internal static readonly List<string> SpawnerPrefabNames = new List<string>();
        // Player terraforming ops that persist as their own ZDOs. They have to be instantiated before
        // a terrain reset can see them, because TerrainModifier.GetAllInstances only reports live
        // components -- see ZoneLoader.CreateTerrainObjects.
        internal static readonly HashSet<int> TerrainModifierHashes = new HashSet<int>();
        // A door that needs an item to open stays open forever once used: Door.CanInteract refuses
        // every later interaction while m_keyItem is set and state != 0, so a player cannot close it
        // again either. Only a fresh ZDO -- or an explicit write of state 0 -- re-seals one. Vanilla's
        // are the Sunken Crypt entrance (sunken_crypt_gate) and the Queen's citadel
        // (dungeon_queen_door); classified by component so a modded keyed entrance is covered too.
        internal static readonly HashSet<int> KeyedDoorHashes = new HashSet<int>();
        // Prefabs carrying a vanilla PlayerBase EffectArea -- the area that stops monsters spawning --
        // mapped to how far it reaches from the piece in XZ. Read from the prefab rather than the live
        // component, because EffectArea only registers itself in Awake and the chunks the sweep scans
        // have nothing instantiated.
        internal static readonly Dictionary<int, float> PlayerBaseRadius = new Dictionary<int, float>();

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
            // Who built it, 0 for world-generated. Reported because the creator is the whole basis on
            // which most categories block, and without it a skip line cannot be audited: a log full of
            // "protected by Ward 'dverger_guardstone'" reads identically whether the ward is a player's
            // or world generation's, which is exactly how that bug survived.
            internal long BlockingCreator;
            // Location occupying the blocked chunk, when the scan happened to pass its proxy. The
            // protection scan runs before any location is resolved (ScanZone is called with a null
            // entry), so without this a blocked chunk never records WHICH location is being starved --
            // in one 28h log only 18 of 14,542 blocked zones could be tied to a location at all.
            internal string BlockingLocationName;
            // No Preserve set here. This scan decides whether the zone may be touched at all, judged
            // against the zone's governing entries (GoverningEntries) or Defaults when it has none.
            // Which individual objects survive a clear is decided per object by
            // ResetTargets.ShouldPreserve, against the resolved entry's own rules.
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
            SpawnerHashes.Clear();
            SpawnerPrefabNames.Clear();
            TerrainModifierHashes.Clear();
            KeyedDoorHashes.Clear();
            PlayerBaseRadius.Clear();
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
                if (prefab.GetComponent<CreatureSpawner>() != null
                    || prefab.GetComponent<SpawnArea>() != null
                    || prefab.GetComponent<TriggerSpawner>() != null) {
                    if (SpawnerHashes.Add(hash)) { SpawnerPrefabNames.Add(prefab.name); }
                }
                if (prefab.GetComponent<TerrainModifier>() != null) { TerrainModifierHashes.Add(hash); }
                Door door = prefab.GetComponent<Door>();
                if (door != null && door.m_keyItem != null) { KeyedDoorHashes.Add(hash); }
                float baseReach = PlayerBaseReach(prefab);
                if (baseReach > 0f) { PlayerBaseRadius[hash] = baseReach; }
                MineRock mineRock = prefab.GetComponent<MineRock>();
                if (mineRock != null && mineRock.m_hitAreas != null) {
                    MineRockAreaCounts[hash] = mineRock.m_hitAreas.Length;
                    MineRockBaseHealth[hash] = mineRock.m_health;
                }
            }

            prefabSetsBuilt = true;
            Logger.LogLocationReset($"Protection prefab sets built: {pieceHashes.Count} pieces, {tombstoneHashes.Count} tombstones, " +
                $"{wardHashes.Count} wards, {portalHashes.Count} portals, {containerHashes.Count} containers, " +
                $"{itemDropHashes.Count} item drops, {KeyedDoorHashes.Count} keyed doors, " +
                $"{SpawnerHashes.Count} spawners, {PlayerBaseRadius.Count} player-base pieces.");
            // Named with their reach, because which pieces make a base is prefab data no config file
            // shows -- and it is the first thing to check when a chunk reports being inside one.
            if (PlayerBaseRadius.Count > 0) {
                List<string> bases = new List<string>();
                foreach (KeyValuePair<int, float> kvp in PlayerBaseRadius) {
                    bases.Add($"{PrefabNameFor(kvp.Key)} {kvp.Value:0.#}m");
                }
                bases.Sort(string.CompareOrdinal);
                Logger.LogLocationReset($"Player-base pieces: {string.Join(", ", bases)}");
            }
        }

        // How far a prefab's PlayerBase area reaches from the piece in XZ, or 0 when it has none. The
        // largest wins when a prefab carries several.
        //
        // Read off the collider directly: EffectArea.GetRadius goes through m_collider, which Awake
        // assigns and a prefab never ran. The area's horizontal offset from the piece is added on, so a
        // sphere set off to one side is still inside the circle drawn around the piece. A box counts
        // as half its longer horizontal side; any other collider shape is not a base area we can size.
        private static float PlayerBaseReach(GameObject prefab) {
            float reach = 0f;
            EffectArea[] areas = prefab.GetComponentsInChildren<EffectArea>(true);
            for (int i = 0; i < areas.Length; i++) {
                EffectArea area = areas[i];
                if (area == null || (area.m_type & EffectArea.Type.PlayerBase) == EffectArea.Type.None) { continue; }

                Transform t = area.transform;
                Vector3 scale = t.lossyScale;
                float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                Collider collider = area.GetComponent<Collider>();
                float radius;
                Vector3 localCenter;
                if (collider is SphereCollider sphere) {
                    radius = sphere.radius * horizontalScale;
                    localCenter = sphere.center;
                } else if (collider is CapsuleCollider capsule) {
                    radius = capsule.radius * horizontalScale;
                    localCenter = capsule.center;
                } else if (collider is BoxCollider box) {
                    radius = 0.5f * Mathf.Max(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.z * scale.z));
                    localCenter = box.center;
                } else {
                    continue;
                }

                Vector3 offset = t.TransformPoint(localCenter) - prefab.transform.position;
                offset.y = 0f;
                reach = Mathf.Max(reach, radius + offset.magnitude);
            }
            return reach;
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

            long creator = zdo.GetLong(ZDOVars.s_creator, 0L);

            // Everything below the tombstone case gates on a creator, because the same prefabs appear
            // in world generation as in player bases and only the creator tells them apart. A ward is
            // no exception: dverger_guardstone is a PrivateArea that ships inside generated Dvergr
            // structures with creator 0, and treating it as a player ward made every Dvergr-adjacent
            // chunk permanently self-immunizing -- 29% of all skips in a 28h server log, and 78.5% of
            // every skip in the Mistlands.
            bool classified = true;
            if (wardHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.Ward; }
            else if (portalHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.Portal; }
            else if (bedHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.Bed; }
            // Vanilla loot chests inside locations have no creator and are meant to be reset; a chest
            // a player placed does, and holds their items.
            else if (containerHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.Container; }
            else if (tameableHashes.Contains(prefab) && zdo.GetBool(ZDOVars.s_tamed, false)) { category = ProtectionCategory.TamedCreature; }
            else if (itemDropHashes.Contains(prefab)) { category = ProtectionCategory.DroppedItem; }
            // Generic player construction. Checked last so the more specific categories win.
            else if (pieceHashes.Contains(prefab) && creator != 0L) { category = ProtectionCategory.PlayerBuiltPiece; }
            else { classified = false; }

            // Anything the admin listed explicitly protects regardless of creator, and still beats an
            // ignore list, so a prefab in both fails closed. Applied as an overlay AFTER detection
            // rather than as an early return: returning early left `category` at its PlayerBuiltPiece
            // initialiser, so a listed prefab was reported under a category it does not belong to and
            // an admin reading the log could not tell which rule had actually fired.
            //
            // When the creator gate above rejected it, fall back to what the prefab IS rather than
            // what it was allowed to block as -- an admin who protects dverger_guardstone deliberately
            // should see "protected by Ward", not "protected by PlayerBuiltPiece".
            if (LocationResetData.ExtraProtectedPrefabHashes.Contains(prefab)) {
                if (classified == false) { category = CategoryForType(prefab); }
                return true;
            }

            return classified;
        }

        // Which category a prefab belongs to on type alone, ignoring the creator and tamed gates that
        // decide whether it actually blocks. Only used to label an explicitly protected prefab, so it
        // keeps the same precedence order as TryClassify and never widens what blocks.
        private static ProtectionCategory CategoryForType(int prefab) {
            if (tombstoneHashes.Contains(prefab)) { return ProtectionCategory.Tombstone; }
            if (wardHashes.Contains(prefab)) { return ProtectionCategory.Ward; }
            if (portalHashes.Contains(prefab)) { return ProtectionCategory.Portal; }
            if (bedHashes.Contains(prefab)) { return ProtectionCategory.Bed; }
            if (containerHashes.Contains(prefab)) { return ProtectionCategory.Container; }
            if (tameableHashes.Contains(prefab)) { return ProtectionCategory.TamedCreature; }
            if (itemDropHashes.Contains(prefab)) { return ProtectionCategory.DroppedItem; }
            return ProtectionCategory.PlayerBuiltPiece;
        }

        // The resolved entries whose content this zone actually holds: the location recorded for the
        // zone, plus every tracked vegetation prefab whose baseline says it exists here. These are the
        // rules the protection scan judges against, which is what lets a reset group relax protection
        // for ITS content -- "player builds do not block ore resets" -- without loosening anything for
        // the rest of the world.
        //
        // Deliberately NOT filtered by due-ness: an entry whose timer has not elapsed can still veto a
        // reset that would happen around its content, which is the conservative direction. And when
        // two groups share a chunk, the strictest one wins per object (see ObjectBlocks) -- a group's
        // ignore only takes effect where every entry with content in the chunk shares it.
        internal static List<LocationResetData.ResolvedResetEntry> GoverningEntries(Vector2s zone) {
            List<LocationResetData.ResolvedResetEntry> entries = new List<LocationResetData.ResolvedResetEntry>();
            float distance = ZoneRates.DistanceFor(zone);

            if (ZoneSystem.instance != null
                && ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance)
                && instance.m_location != null
                && LocationResetData.TryGetLocationEntry(instance.m_location.Hash, out LocationResetData.ResolvedResetEntry location)) {
                location = location.ForDistance(distance);
                // A disabled entry does no work here, so it gets no vote; the zone's other content
                // still judges under its own rules, and Defaults cover a zone with no entries at all.
                if (location.Enabled) { entries.Add(location); }
            }

            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> tracked in LocationResetData.VegetationByPrefabHash) {
                LocationResetData.ResolvedResetEntry entry = tracked.Value.ForDistance(distance);
                if (entry.Enabled == false) { continue; }
                if (LocationResetState.TryGetEntry(zone, tracked.Key, out LocationResetState.EntryRecord record) == false) { continue; }
                if (record.Baseline == 0) { continue; }
                entries.Add(entry);
            }
            return entries;
        }

        // Scan a zone and its 8 neighbours for player property. Neighbours are included because a
        // location's exterior radius routinely crosses a zone boundary, and a base just over the line
        // is still a base -- Upgrade World's single-sector scan is a documented source of stale
        // objects and half-cleared locations.
        //
        // entries are the zone's governing entries (see GoverningEntries); pass null or empty to judge
        // purely against Defaults, which is also what the entries themselves fall back to for any
        // category they do not override.
        internal static ProtectionResult ScanZone(Vector2s zone, List<LocationResetData.ResolvedResetEntry> entries, bool includeNeighbours) {
            ProtectionResult result = new ProtectionResult();
            if (ZDOMan.instance == null) { return result; }
            BuildPrefabSets();

            // Neighbour hits are distance-tested from the chunk centre; the chunk's own sector is not,
            // so a build inside the chunk always protects it however tight the radius is set. Player
            // bases are measured by their own reach instead -- see ScanSector.
            Vector3 center3 = ZoneSystem.GetZonePos(zone);
            Vector2 center = new Vector2(center3.x, center3.z);
            float radius = LocationResetData.ProtectionRadius;

            int range = includeNeighbours ? 1 : 0;
            for (int dx = -range; dx <= range; dx++) {
                for (int dy = -range; dy <= range; dy++) {
                    bool isCenter = dx == 0 && dy == 0;
                    if (ScanSector(new Vector2s(zone.x + dx, zone.y + dy), entries, result, center, isCenter, radius)) {
                        // A Block hit is decisive; no point scanning the rest. Name the location being
                        // starved before returning: this is the only moment it is cheap to find, and
                        // the caller abandons the zone immediately afterwards. Off the hot path, since
                        // a blocked zone is a zone that gets a log line written for it either way.
                        result.BlockingLocationName = FindLocationName(zone);
                        return result;
                    }
                }
            }
            return result;
        }

        // The location occupying a chunk. m_locationInstances is the generation-time record and needs
        // no ZDO pass at all; the LocationProxy scan below it covers locations placed by other means,
        // and the NoProxy case falls out naturally -- the instance still names it.
        //
        // Only the centre zone is consulted: a location in a neighbour is a different location and
        // would misattribute the block.
        //
        // Safe to reuse the shared buffer -- ScanSector clears it before handing back a blocking hit,
        // and this only ever runs after that.
        private static string FindLocationName(Vector2s zone) {
            if (ZoneSystem.instance != null
                && ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance)
                && instance.m_location != null) {
                string known = LocationResetData.ResolveKnownName(instance.m_location.Hash);
                if (string.IsNullOrEmpty(known) == false) { return known; }
            }

            if (ZDOMan.instance == null) { return null; }

            zdoBuffer.Clear();
            ZoneObjects.FindObjects(zone, zdoBuffer);

            string name = null;
            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (zdo.m_prefab != LocationProxyHash) { continue; }
                // ZoneLocation.Hash is GetStableHashCode(prefab name), and that is what LocationProxy
                // stores here, so the same lookup that names a configured entry names this.
                int locationHash = zdo.GetInt(ZDOVars.s_location, 0);
                if (locationHash == 0) { continue; }
                name = LocationResetData.ResolveKnownName(locationHash);
                if (string.IsNullOrEmpty(name) == false) { break; }
            }

            zdoBuffer.Clear();
            return name;
        }

        // Returns true if this sector produced a blocking hit.
        //
        // Every object in the chunk's own sector (isCenter) counts. In a neighbour an object only counts
        // within radius of the chunk centre: the 3x3 sweep otherwise let one forgotten build protect
        // nine chunks. A player base is the exception, judged by its own reach against the chunk's
        // footprint instead (see InsidePlayerBase).
        private static bool ScanSector(Vector2s sector, List<LocationResetData.ResolvedResetEntry> entries, ProtectionResult result,
                                       Vector2 center, bool isCenter, float radius) {
            zdoBuffer.Clear();
            ZoneObjects.FindObjects(sector, zdoBuffer);

            for (int i = 0; i < zdoBuffer.Count; i++) {
                ZDO zdo = zdoBuffer[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }

                // XZ only, and built explicitly: a dungeon interior is parked at its entrance's
                // y + 5000, so anything that let altitude into this comparison would put every
                // interior object out of range of its own chunk.
                Vector3 p = zdo.GetPosition();
                Vector2 position = new Vector2(p.x, p.z);

                if (TryClassify(zdo, out ProtectionCategory category)
                        && (isCenter || Vector2.Distance(position, center) <= radius)) {
                    // ProtectedPrefabs blocks unconditionally, whatever its category's action says --
                    // that is the documented contract ("always block a reset, whatever category detection
                    // says"), and it beats every ignore list, matching ShouldPreserve and
                    // WarnOnProtectionConflicts. Routing it through the category action instead would let
                    // a listed ItemDrop slip through on DroppedItem's Preserve default.
                    bool blocks = LocationResetData.ExtraProtectedPrefabHashes.Contains(zdo.m_prefab)
                        || ObjectBlocks(entries, category, zdo.m_prefab);

                    // Only Block is decided here. Preserve and Ignore both mean "this zone may be reset",
                    // and which objects inside it survive is ShouldPreserve's call at clear time.
                    if (blocks) { return RecordBlock(result, category, zdo); }
                }

                // Asked of every object, whatever it classified as and however far it stands from the
                // centre: whether this chunk lies in somebody's base is a question of the base's reach,
                // not of where the piece stands. After the category check, so a piece that blocks in
                // its own right is still reported under that category.
                if (InsidePlayerBase(zdo, position, center)
                        && ObjectBlocks(entries, ProtectionCategory.PlayerBaseEffect, zdo.m_prefab)) {
                    return RecordBlock(result, ProtectionCategory.PlayerBaseEffect, zdo);
                }
            }

            zdoBuffer.Clear();
            return false;
        }

        private static bool RecordBlock(ProtectionResult result, ProtectionCategory category, ZDO zdo) {
            result.Blocked = true;
            result.BlockingCategory = category;
            result.BlockingPrefabHash = zdo.m_prefab;
            result.BlockingPosition = zdo.GetPosition();
            result.BlockingCreator = zdo.GetLong(ZDOVars.s_creator, 0L);
            zdoBuffer.Clear();
            return true;
        }

        // Half a chunk's side: ZoneSystem.GetZone floors (x + 32) / 64, so a chunk spans +/-32m of its
        // centre on both horizontal axes.
        private const float ChunkHalfSize = 32f;

        // Whether this is a player-built base piece whose PlayerBase area overlaps the chunk. Circle
        // against the chunk's square rather than its centre point, so a base reaching over the edge
        // counts as well as one covering the middle.
        //
        // Creator-gated like every other player category: world generation places some of the same
        // pieces, and those are nobody's base.
        //
        // The 3x3 scan bounds what can be seen. A base reaching this chunk from beyond its neighbours
        // would need more than 64m of reach, and is not found.
        private static bool InsidePlayerBase(ZDO zdo, Vector2 position, Vector2 center) {
            if (PlayerBaseRadius.TryGetValue(zdo.m_prefab, out float reach) == false) { return false; }
            if (zdo.GetLong(ZDOVars.s_creator, 0L) == 0L) { return false; }
            float dx = Mathf.Max(0f, Mathf.Abs(position.x - center.x) - ChunkHalfSize);
            float dz = Mathf.Max(0f, Mathf.Abs(position.y - center.y) - ChunkHalfSize);
            return (dx * dx) + (dz * dz) <= reach * reach;
        }

        // Whether one classified object blocks the zone, judged against every governing entry.
        //
        // Fail closed: each entry votes under its own rules, and any single Block wins. An object is
        // exempt only when every entry either ignores it (per-category Ignored list, applied after
        // classification so listing a prefab under PlayerBuiltPiece cannot make it ignorable as a
        // Tombstone) or maps its category to a non-Block action. That is what makes a group-level
        // "PlayerBuiltPiece: Ignore" safe: it only takes effect in chunks where nothing else claims
        // the content, so relaxing the Ores group cannot expose a crypt that shares the chunk.
        //
        // With no governing entries the zone holds nothing configured beyond the zone stamp itself,
        // and Defaults judge alone -- the same rules every entry starts from before its overrides.
        private static bool ObjectBlocks(List<LocationResetData.ResolvedResetEntry> entries, ProtectionCategory category, int prefabHash) {
            if (entries == null || entries.Count == 0) {
                if (LocationResetData.DefaultIgnores(category, prefabHash)) { return false; }
                return DefaultActionFor(category) == ProtectionAction.Block;
            }

            for (int i = 0; i < entries.Count; i++) {
                LocationResetData.ResolvedResetEntry entry = entries[i];
                if (entry.Ignores(category, prefabHash)) { continue; }
                if (entry.ActionFor(category) == ProtectionAction.Block) { return true; }
            }
            return false;
        }

        // "protected by PlayerBuiltPiece 'wood_floor' (built by 8FA31C02) at x=-742 z=2251, holding
        // location 'Crypt3'". Only ever called for a chunk that is actually being reported, so
        // resolving the prefab here costs nothing on the hot path.
        //
        // The creator is printed because it is the basis on which most categories block, and a line
        // without it cannot be audited: "protected by Ward 'dverger_guardstone'" reads the same
        // whether the ward is a player's or world generation's, which is how that misclassification
        // went unnoticed across an entire server log.
        internal static string DescribeBlock(ProtectionResult result) {
            if (result == null || result.Blocked == false) { return "not blocked"; }
            string prefab = result.BlockingPrefabHash.ToString();
            if (ZNetScene.instance != null) {
                GameObject go = ZNetScene.instance.GetPrefab(result.BlockingPrefabHash);
                if (go != null) { prefab = go.name; }
            }
            string holding = string.IsNullOrEmpty(result.BlockingLocationName)
                ? ""
                : $", holding location '{result.BlockingLocationName}'";
            return $"protected by {result.BlockingCategory} '{prefab}' ({DescribeOwner(result)}) " +
                $"at x={result.BlockingPosition.x:0} z={result.BlockingPosition.z:0}{holding}";
        }

        // Three genuinely different states, and collapsing them would undo the point of logging the
        // creator at all. Tombstone, DroppedItem and TamedCreature do not gate on a creator and
        // normally have none, so "no creator" there is unremarkable. For every other category a
        // creator is exactly what makes it block, so reaching this code with none means the admin
        // force-protected the prefab -- worth saying, because it is now the only route to it.
        private static string DescribeOwner(ProtectionResult result) {
            if (result.BlockingCreator != 0L) { return $"built by {result.BlockingCreator:X}"; }
            switch (result.BlockingCategory) {
                case ProtectionCategory.Tombstone:
                case ProtectionCategory.DroppedItem:
                case ProtectionCategory.TamedCreature:
                    return "no creator";
                default:
                    return "no creator, admin-protected";
            }
        }

        private static ProtectionAction DefaultActionFor(ProtectionCategory category) {
            Dictionary<ProtectionCategory, ProtectionRule> defaults =
                LocationResetData.SLE_LocationReset_Settings?.Defaults?.Protection;
            if (defaults != null && defaults.TryGetValue(category, out ProtectionRule rule) && rule != null) { return rule.Action; }
            return ProtectionAction.Block;
        }

        // Live counts of every configured vegetation prefab present in a sector, keyed by prefab hash.
        // Only prefabs the config tracks are counted, so the dictionary stays small.
        internal static Dictionary<int, ushort> CensusZone(Vector2s zone) {
            Dictionary<int, ushort> counts = new Dictionary<int, ushort>();
            if (ZDOMan.instance == null) { return counts; }
            if (LocationResetData.VegetationByPrefabHash.Count == 0) { return counts; }

            zdoBuffer.Clear();
            ZoneObjects.FindObjects(zone, zdoBuffer);

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
        // Also where each of the zone's timers starts, if it never has -- see LocationResetState.SetBaseline.
        //
        // The sweep's post-reset call passes passStart and the chunk's rate, so that a loss the pass
        // was not allowed to restore stays owed. SelectDueVegetation skips an entry whose timer is still
        // running, so when a location in the chunk comes due first, a vein mined out since copper last
        // came back is still missing afterwards -- and re-recording the lower count forgot it for good.
        // Timers now also start at first sight and on an in-place refresh, which turned that from an
        // occasional loss into the usual outcome. Every other caller passes 0 and records the zone
        // exactly as it stands.
        internal static void RecordBaseline(Vector2s zone, long passStart = 0L, float rate = 1f) {
            Dictionary<int, ushort> counts = CensusZone(zone);
            float distance = passStart > 0L ? ZoneRates.DistanceFor(zone) : 0f;
            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> tracked in LocationResetData.VegetationByPrefabHash) {
                counts.TryGetValue(tracked.Key, out ushort present);
                if (passStart > 0L && LossStillOwed(zone, tracked.Key, tracked.Value.ForDistance(distance), present, passStart, rate)) { continue; }
                LocationResetState.SetBaseline(zone, tracked.Key, present);
            }
            // The timer shared by everything with no entry of its own has nothing to census, but starts
            // at the same moment.
            LocationResetState.StartTimer(zone, LocationResetState.DefaultsTimerKey);
        }

        // Below baseline because the pass that just ended left it alone on purpose: the entry's timer
        // was still running when the pass began. A stamp at or after passStart means the pass regrew
        // it, and then the new count is the one to keep, whatever it came to. A due entry the pass
        // could not regrow is re-recorded as before, or it would hold the chunk in the slow lane.
        private static bool LossStillOwed(Vector2s zone, int prefabHash, LocationResetData.ResolvedResetEntry entry,
                                          ushort present, long passStart, float rate) {
            if (entry.Enabled == false) { return false; }
            if (LocationResetState.TryGetEntry(zone, prefabHash, out LocationResetState.EntryRecord record) == false) { return false; }
            if (present >= record.Baseline) { return false; }
            if (record.Stamp <= 0L || record.Stamp >= passStart) { return false; }
            return entry.IsDue(record.Stamp, passStart, rate) == false;
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
        internal static int BlockZdoCount(Vector2s zone, out int interiorCount,
                                          Dictionary<int, int> surfaceByPrefab = null,
                                          Dictionary<int, int> interiorByPrefab = null) {
            interiorCount = 0;
            if (ZDOMan.instance == null) { return 0; }

            int surfaceCount = 0;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    zdoBuffer.Clear();
                    ZoneObjects.FindObjects(new Vector2s(zone.x + dx, zone.y + dy), zdoBuffer);
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
        internal static Dictionary<int, List<Vector2>> TrackedVegetationPositions(Vector2s zone) {
            Dictionary<int, List<Vector2>> positions = new Dictionary<int, List<Vector2>>();
            if (ZDOMan.instance == null) { return positions; }
            if (LocationResetData.VegetationByPrefabHash.Count == 0) { return positions; }

            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    zdoBuffer.Clear();
                    ZoneObjects.FindObjects(new Vector2s(zone.x + dx, zone.y + dy), zdoBuffer);

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
