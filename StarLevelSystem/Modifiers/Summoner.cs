using StarLevelSystem.common;
using StarLevelSystem.modules.CreatureSetup;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal class Summoner
    {
        public static void Setup(Character creature = null, CreatureModConfig config = null, CharacterCacheEntry ccache = null) {
            if (creature == null || config == null || ccache == null) { return; }

            // Resolve the summon pool BEFORE touching the component. The old order added the component
            // first and only configured it when the biome had an entry, so a creature set up before its
            // biome resolved (or in a biome with no BiomeObjects) kept an inert SLSSummoner forever: every
            // later Setup saw a non-null component and returned without ever initializing it.
            if (config.BiomeObjects == null) { return; }
            if (config.BiomeObjects.TryGetValue(ccache.Biome, out List<string> summonPrefabs) == false) { return; }
            if (summonPrefabs == null || summonPrefabs.Count == 0) { return; }

            SLSSummoner summoner = creature.GetComponent<SLSSummoner>();
            if (summoner == null) { summoner = creature.gameObject.AddComponent<SLSSummoner>(); }
            // Always re-run rather than only on first attach: SetupSummoner is idempotent, and this is what
            // picks up a config reload that changed the cap, the interval or the summon pool.
            if (Logger.IsDebugEnabled) { Logger.LogDebug($"Setting up Summoner for {creature.name} with {summonPrefabs.Count} summonable prefabs"); }
            summoner.SetupSummoner(creature, summonPrefabs, Mathf.RoundToInt(config.BasePower), config.PerlevelPower);
        }

        // Wired as the BossSummoner TeardownEvent. Removing the modifier used to leave the component and its
        // InvokeRepeating running, so the creature kept summoning after it stopped being a summoner.
        public static void Teardown(Character creature = null) {
            if (creature == null) { return; }

            SLSSummoner summoner = creature.GetComponent<SLSSummoner>();
            if (summoner != null) {
                // Cancelled before Destroy, not left to OnDestroy: Destroy only takes effect at the end of
                // the frame, so a tick could still fire and re-write the key cleared below.
                summoner.CancelInvoke();
                UnityEngine.Object.Destroy(summoner);
            }

            ZNetView nview = creature.m_nview;
            if (nview == null || nview.IsValid() == false || nview.IsOwner() == false) { return; }
            // Drop the tracked list so re-adding the modifier later starts from a clean cap.
            nview.GetZDO().Set(SLS_SUMMONED, string.Empty);
        }

        public class SLSSummoner : MonoBehaviour {
            // timeBetweenSummons comes straight from the config's PerlevelPower, and InvokeRepeating with a
            // repeat rate of 0 fires every frame - an unbounded-spawn path of its own. The configured value
            // is clamped up to this.
            const float MinTimeBetweenSummons = 5f;
            // Horizontal spread of a summon around the creature. The indoor radius is much tighter
            // because 10m inside a dungeon room is usually through a wall.
            const float SpawnRadius = 10f;
            const float IndoorSpawnRadius = 4f;
            // Placement attempts before falling back to the creature's own position. Matches vanilla's
            // SpawnArea.FindSpawnPoint, which is the dungeon spawner this mirrors.
            const int SpawnPositionAttempts = 10;
            // Outdoors: how far above the terrain the topmost solid surface at a candidate may sit before
            // that candidate reads as a rock, an altar piece or a roof rather than open ground.
            const float MaxSolidAboveTerrain = 0.5f;
            // How far the summon is lifted off whatever it was placed on, so it is not spawned exactly
            // tangent to it. Indoors is tighter because dungeon ceilings are low.
            const float SpawnGroundOffset = 0.5f;
            const float IndoorSpawnGroundOffset = 0.1f;
            // Bounds on the body the clearance test has to fit. Clamped rather than exact: an exact body
            // test for the largest creatures rejects most of a dungeon room and would push every summon
            // onto the fallback, and the question being asked is only whether the spot is occupied.
            const float MinClearanceRadius = 0.5f;
            const float MaxClearanceRadius = 2f;
            const float MinClearanceHeight = 1f;
            const float MaxClearanceHeight = 4f;
            // The clearance capsule is started this far above the ground so a floor piece the candidate is
            // standing on cannot register as an overlap through floating-point slack.
            const float ClearanceGroundMargin = 0.1f;

            // How good a candidate spot is; lower is better. Anything past Clear is only used when no
            // clear spot turned up in the attempts allowed.
            private enum PlacementTier {
                Clear = 0,
                // Open ground, but a wall or a rock stands between it and the summoner.
                OutOfSight = 1,
                // Standing on top of solid geometry (a rock, an altar, a roof) rather than on the ground.
                OnSolid = 2,
                None = 3
            }

            readonly List<GameObject> summonableCreatures = new List<GameObject>();
            ZNetView creature_znet = null;
            int maxSummoned = 10;
            int summonBatchSize = 2;
            float timeBetweenSummons = 30f;
            bool started = false;

            public void OnDestroy() {
                CancelInvoke();
            }

            public void SpawnCreaturesBatch() {
                // Strict ZDO-owner authority. RunCharacterSetup runs on every peer, not just the roller, so
                // without this gate every client with the creature loaded runs its own loop against its own
                // private cap (N peers = N x the summons) and they all fight over the ZDO write below.
                // The invoke stays scheduled on non-owners so a peer that later takes ownership picks the
                // loop up without needing a fresh setup pass; until then the tick is a no-op.
                if (creature_znet == null || creature_znet.IsValid() == false || creature_znet.IsOwner() == false) { return; }
                if (summonableCreatures.Count == 0 || maxSummoned <= 0) { return; }
                // The invoke outlives logout/shutdown until the GameObject is destroyed.
                if (ZDOMan.instance == null || ZoneSystem.instance == null) { return; }

                ZDO zdo = creature_znet.GetZDO();
                // The tracked list lives on the ZDO, not on this component: the component dies with the
                // GameObject every time the creature streams out, and the cap died with it - which is what
                // let a boss summon another full batch after every unload/reload.
                string stored = zdo.GetString(SLS_SUMMONED, string.Empty);
                List<ZDOID> spawned = SLSExtensions.UnpackZDOIDs(stored);
                // Pruned every tick. The old code only pruned once the cap was already hit, so the list
                // drifted out of sync with what was actually alive.
                spawned.RemoveAll(x => ZDOMan.instance.GetZDO(x) == null);

                for (int i = 0; i < summonBatchSize; i++) {
                    // Re-checked per spawn so a batch cannot overshoot the cap by up to summonBatchSize - 1.
                    if (spawned.Count >= maxSummoned) { break; }
                    ZDOID summoned = SpawnCreatureRandomly();
                    if (summoned != ZDOID.None) { spawned.Add(summoned); }
                }

                // Only write when the value actually changed: an unconditional Set bumps the ZDO's
                // DataRevision and re-replicates the list to every peer on every tick.
                string packed = SLSExtensions.PackZDOIDs(spawned);
                if (packed != stored) { zdo.Set(SLS_SUMMONED, packed); }
            }

            /// <summary>
            /// Pick a spot near the creature to drop a summon on.
            ///
            /// Both branches try several random points and grade each one, rather than taking the first.
            /// The outdoor branch used to take the first: one point in the disc, snapped to the terrain
            /// height and returned. GetGroundHeight only ever casts against the terrain layer, so a point
            /// inside the Yagluth altar's stonework came back as the sand underneath it and the summon was
            /// instantiated inside the rock, where it is trapped, hidden and hard to hit.
            ///
            /// Indoors the terrain snap cannot be used at all: vanilla's GetGroundHeight sets its ray origin
            /// to an absolute y = 6000, and a dungeon interior sits at its entrance's y + 5000 with none of
            /// its geometry on the terrain layer - so from inside a crypt the first hit is the world surface
            /// ~5000m below and every summon gets teleported out of the dungeon. Indoors we use the same
            /// calls vanilla's own dungeon spawners use (SpawnArea.FindSpawnPoint, CreatureSpawner.Spawn).
            /// </summary>
            private Vector3 FindSummonPosition(GameObject toSummon) {
                Vector3 origin = transform.position;

                // Vanilla's only interior test (Character.InInterior is literally y > 3000); interiors are
                // parked at their entrance's y + 5000, so nothing on the surface reaches it.
                bool indoors = Character.InInterior(origin);
                float searchRadius = indoors ? IndoorSpawnRadius : SpawnRadius;
                float groundOffset = indoors ? IndoorSpawnGroundOffset : SpawnGroundOffset;
                GetSummonClearance(toSummon, out float bodyRadius, out float bodyHeight);

                Vector3 fallback = origin;
                PlacementTier fallbackTier = PlacementTier.None;

                for (int attempt = 0; attempt < SpawnPositionAttempts; attempt++) {
                    // A disc rather than the old square box - Range(-10, 10) also bound the int overload, so
                    // offsets were whole numbers in [-10, 9] and only 400 spots were reachable.
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * searchRadius;
                    Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);

                    bool onSolid = false;
                    if (indoors) {
                        if (GroundIndoors(ref candidate, origin) == false) { continue; }
                    } else if (GroundOutdoors(ref candidate, out onSolid) == false) {
                        continue;
                    }

                    // The check the outdoor path never had: a random point in the disc lands inside the
                    // altar's stonework as readily as on the sand beside it.
                    if (IsOccupied(candidate, bodyRadius, bodyHeight)) { continue; }

                    // A spot on top of a rock is already ranked below an out-of-sight one, so the line of
                    // sight it has cannot change the outcome and the raycast is skipped.
                    PlacementTier tier = PlacementTier.Clear;
                    if (onSolid) {
                        tier = PlacementTier.OnSolid;
                    } else if (IsPathBlocked(origin, candidate)) {
                        // Indoors a blocked line means the candidate is through a wall in another room, so
                        // it stays a hard reject the way it always was. Outdoors it usually only means the
                        // far side of a boulder or a ridge, which is still open ground worth taking over
                        // stacking the summon on the creature - so it is demoted rather than thrown away.
                        if (indoors) { continue; }
                        tier = PlacementTier.OutOfSight;
                    }

                    candidate.y += groundOffset;
                    if (tier == PlacementTier.Clear) { return candidate; }
                    if (tier < fallbackTier) {
                        fallback = candidate;
                        fallbackTier = tier;
                    }
                }

                if (fallbackTier != PlacementTier.None) {
                    if (Logger.IsDebugEnabled) { Logger.LogDebug($"No clear summon position found near {origin} after {SpawnPositionAttempts} attempts; using a {fallbackTier} spot at {fallback}."); }
                    return fallback;
                }

                // Nothing worked: put the summon on the creature itself. That spot is known good because the
                // creature is standing in it, and it is what the spawn multiplier already does indoors
                // (Spawnrate skips the offset entirely above y 3000). A stacked summon that walks itself
                // apart beats a skipped wave, which would quietly under-fill the cap.
                if (Logger.IsDebugEnabled) { Logger.LogDebug($"No usable summon position found near {origin} after {SpawnPositionAttempts} attempts; spawning on the summoner."); }
                return origin;
            }

            /// <summary>
            /// Snap a candidate to the dungeon floor under it. Casts from candidate + up*1 down 1000 against
            /// the solid mask, so it lands on the room floor instead of the terrain thousands of metres below.
            /// </summary>
            private static bool GroundIndoors(ref Vector3 candidate, Vector3 origin) {
                if (ZoneSystem.instance.FindFloor(candidate, out float floorHeight) == false) { return false; }
                candidate.y = floorHeight;

                // Vanilla's guard from SpawnAbility: a failed height query reads as 0, and this is what
                // turns that into a rejected candidate rather than a minion dropped to y = 0.
                if (Mathf.Abs(candidate.y - origin.y) > 100f) { return false; }
                return true;
            }

            /// <summary>
            /// Snap a candidate to what it would actually stand on outdoors, and report whether that is bare
            /// terrain or something sitting on top of it.
            ///
            /// FindFloor casts against the solid layers as well as terrain, which is what tells a rock from
            /// the ground it rests on; it is started 100m up so a candidate that is already inside a rock
            /// finds the top of that rock rather than whatever is below it. This is the same grading
            /// EpicLoot's bounty placement runs, and the same one the raid and Nemesis spawners already use.
            /// </summary>
            private static bool GroundOutdoors(ref Vector3 candidate, out bool onSolid) {
                onSolid = false;
                Vector3 sample = candidate;
                ZoneSystem.instance.GetGroundData(ref sample, out _, out _, out _, out Heightmap hmap);
                // No heightmap means the zone has not loaded, and every solid test below would then pass
                // straight through geometry that has not spawned yet.
                if (hmap == null) { return false; }

                float terrainHeight = sample.y;
                if (ZoneSystem.instance.FindFloor(new Vector3(sample.x, terrainHeight + 100f, sample.z), out float solidHeight) == false) { return false; }

                // Half a metre of slack for what sits flush with the ground (paths, roots, the ray's own
                // disagreement with the heightmap); past that it is a rock, an altar piece or a roof, and
                // standing on those is a last resort rather than the open ground a summon wants.
                float aboveTerrain = solidHeight - terrainHeight;
                onSolid = aboveTerrain > MaxSolidAboveTerrain;
                sample.y = aboveTerrain > 0f ? solidHeight : terrainHeight;
                candidate = sample;
                return true;
            }

            /// <summary>
            /// Whether the summon's own body would be inside something solid at this spot. The indoor path
            /// used a fixed half-metre sphere, which a troll or an abomination clears comfortably while
            /// standing waist-deep in a wall, so the capsule is sized from the prefab instead.
            /// m_blockRayMask is the solid layers minus terrain - the same mask OfferingBowl uses for its
            /// boss-spawn clearance check - so the ground just snapped to cannot veto itself.
            /// </summary>
            private static bool IsOccupied(Vector3 groundPoint, float bodyRadius, float bodyHeight) {
                Vector3 bottom = groundPoint + (Vector3.up * (ClearanceGroundMargin + bodyRadius));
                Vector3 top = groundPoint + (Vector3.up * (ClearanceGroundMargin + Mathf.Max(bodyRadius, bodyHeight - bodyRadius)));
                return Physics.CheckCapsule(bottom, top, bodyRadius, ZoneSystem.instance.m_blockRayMask);
            }

            /// <summary>
            /// Whether a wall or a rock stands between the creature and a candidate. Static_solid (plus
            /// terrain) is deliberate: walls, floors and the world's big rocks are static_solid, while props
            /// and furniture sit on Default/piece and should not veto an otherwise fine spot.
            /// </summary>
            private static bool IsPathBlocked(Vector3 origin, Vector3 candidate) {
                Vector3 eyeLevel = origin + Vector3.up;
                Vector3 toCandidate = (candidate + Vector3.up) - eyeLevel;
                if (toCandidate.sqrMagnitude <= 0.01f) { return false; }
                return Physics.Raycast(eyeLevel, toCandidate.normalized, toCandidate.magnitude, ZoneSystem.instance.m_staticSolidRayMask);
            }

            /// <summary>
            /// The body the clearance test has to fit, read off the prefab's own capsule. Character.GetRadius
            /// and GetHeight cannot be used here: Character.m_collider is assigned in Awake, which has not run
            /// on a prefab, so both would throw. Awake reads the capsule off the same GameObject, so this does
            /// too.
            /// </summary>
            private static void GetSummonClearance(GameObject prefab, out float bodyRadius, out float bodyHeight) {
                bodyRadius = MinClearanceRadius;
                bodyHeight = MinClearanceHeight;
                CapsuleCollider capsule = prefab == null ? null : prefab.GetComponent<CapsuleCollider>();
                if (capsule == null) { return; }

                // How Unity scales a capsule collider: the radius by the larger of the two horizontal axes,
                // the height by the vertical one. SLS's own size modifiers are applied after the spawn, so
                // this is the base body - close enough for "is this spot occupied", which is all it asks.
                Vector3 scale = prefab.transform.localScale;
                float horizontal = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                bodyRadius = Mathf.Clamp(capsule.radius * horizontal, MinClearanceRadius, MaxClearanceRadius);
                bodyHeight = Mathf.Clamp(capsule.height * Mathf.Abs(scale.y), MinClearanceHeight, MaxClearanceHeight);
            }

            private ZDOID SpawnCreatureRandomly() {
                GameObject toSummon = summonableCreatures[UnityEngine.Random.Range(0, summonableCreatures.Count)];
                Vector3 spawnPosition = FindSummonPosition(toSummon);
                GameObject spawnedCreature = Instantiate(toSummon, spawnPosition, Quaternion.identity);
                if (spawnedCreature == null) { return ZDOID.None; }

                // Cave dwellers (Ulv, Fenring_Cultist, crypt Draugr) ship with MonsterAI.m_sleeping set on
                // the prefab, so a bare Instantiate left them lying there inert. Same treatment the raid and
                // nemesis spawners give their spawns.
                MonsterAI mAI = spawnedCreature.GetComponent<MonsterAI>();
                if (mAI != null) { CreatureSetupControl.ApplySpawnAI(mAI, AI.Alerted); }

                Character character = spawnedCreature.GetComponent<Character>();
                if (character != null) {
                    // multiply: false - spawn multiplication applied to a summon would multiply the summons.
                    // Barring BossSummoner mirrors Splitter's guard against recursive self-replication: boss
                    // modifiers only roll on IsBoss() creatures, but a RequiredModifiers entry bypasses that.
                    List<string> notAllowed = new List<string>() { ModifierNames.BossSummoner.ToString() };
                    CreatureSetupControl.CreatureSpawnerSetup(character, 0, multiply: false, notAllowedModifiers: notAllowed);
                }

                // Tracked off the ZNetView rather than the Character: BiomeObjects takes any prefab name, and
                // a non-creature entry there would otherwise spawn a fresh copy every wave forever because
                // nothing ever counted against the cap. Same reason the raid spawner tracks its non-creature
                // spawns.
                ZNetView spawnedView = spawnedCreature.GetComponent<ZNetView>();
                if (spawnedView == null || spawnedView.IsValid() == false) {
                    Logger.LogWarning($"Summon '{toSummon.name}' has no valid ZNetView and cannot be counted against the summon cap.");
                    return ZDOID.None;
                }
                return spawnedView.GetZDO().m_uid;
            }

            public void SetupSummoner(Character character, List<string> summonPrefabs, int max_summoned = 10, float time_between_summons = 60f) {
                float previousInterval = timeBetweenSummons;
                timeBetweenSummons = Mathf.Max(MinTimeBetweenSummons, time_between_summons);
                maxSummoned = Mathf.Max(0, max_summoned);
                creature_znet = character.m_nview;
                if (creature_znet == null) {
                    creature_znet = this.gameObject.GetComponent<ZNetView>();
                }

                // Rebuilt, not appended to: SetupSummoner now re-runs on every setup pass, and appending
                // would grow the pool by a full copy each time.
                summonableCreatures.Clear();
                foreach (var prefabname in summonPrefabs) {
                    GameObject prefab = Jotunn.Managers.PrefabManager.Instance.GetPrefab(prefabname);
                    if (prefab != null) {
                        summonableCreatures.Add(prefab);
                    }
                }

                // Rescheduled when the interval changed, so a config reload takes effect without needing the
                // creature to unload first; otherwise the running invoke is left alone so re-running setup
                // does not keep pushing the next wave further out.
                if (started && Mathf.Approximately(previousInterval, timeBetweenSummons) == false) {
                    CancelInvoke(nameof(SpawnCreaturesBatch));
                    started = false;
                }
                if (started == false) {
                    InvokeRepeating(nameof(SpawnCreaturesBatch), timeBetweenSummons, timeBetweenSummons);
                    started = true;
                }
            }
        }

    }
}
