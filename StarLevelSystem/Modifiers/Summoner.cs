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
            /// Outdoors this is the terrain height, as before. Indoors it must not be: vanilla's
            /// GetGroundHeight sets its ray origin to an absolute y = 6000 and casts against the terrain
            /// layer only, and a dungeon interior sits at its entrance's y + 5000 with none of its geometry
            /// on that layer - so from inside a crypt the first hit is the world surface ~5000m below and
            /// every summon gets teleported out of the dungeon. Indoors we use the same calls vanilla's own
            /// dungeon spawners use (SpawnArea.FindSpawnPoint, CreatureSpawner.Spawn).
            /// </summary>
            private Vector3 FindSummonPosition() {
                Vector3 origin = transform.position;

                // Vanilla's only interior test (Character.InInterior is literally y > 3000); interiors are
                // parked at their entrance's y + 5000, so nothing on the surface reaches it.
                // Outdoors keeps the existing single-shot behaviour - the terrain snap is correct there, and
                // the extra indoor validation below would change how the modifier plays in normal use.
                if (Character.InInterior(origin) == false) {
                    // A disc rather than the old square box - Range(-10, 10) also bound the int overload, so
                    // offsets were whole numbers in [-10, 9] and only 400 spots were reachable.
                    Vector2 surfaceOffset = UnityEngine.Random.insideUnitCircle * SpawnRadius;
                    Vector3 surface = origin + new Vector3(surfaceOffset.x, 0f, surfaceOffset.y);
                    surface.y = ZoneSystem.instance.GetGroundHeight(surface) + 0.5f;
                    return surface;
                }

                for (int attempt = 0; attempt < SpawnPositionAttempts; attempt++) {
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * IndoorSpawnRadius;
                    Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);

                    // Casts from candidate + up*1 down 1000 against the solid mask, so it lands on the room
                    // floor instead of the terrain thousands of metres below.
                    if (ZoneSystem.instance.FindFloor(candidate, out float floorHeight) == false) { continue; }
                    candidate.y = floorHeight + 0.1f;

                    // Vanilla's guard from SpawnAbility: a failed height query reads as 0, and this is what
                    // turns that into a rejected candidate rather than a minion dropped to y = 0.
                    if (Mathf.Abs(candidate.y - origin.y) > 100f) { continue; }

                    // Reject anything with a wall between it and the creature. Static_solid (plus terrain) is
                    // deliberate: walls and floors are static_solid, while props and furniture sit on
                    // Default/piece and should not veto an otherwise fine spot.
                    Vector3 eyeLevel = origin + Vector3.up;
                    Vector3 toCandidate = (candidate + Vector3.up) - eyeLevel;
                    if (toCandidate.sqrMagnitude > 0.01f && Physics.Raycast(eyeLevel, toCandidate.normalized, toCandidate.magnitude, ZoneSystem.instance.m_staticSolidRayMask)) { continue; }

                    // Not standing inside geometry. m_blockRayMask is the solid layers minus terrain - the
                    // same mask OfferingBowl uses for its boss-spawn clearance check.
                    if (Physics.CheckSphere(candidate + (Vector3.up * 0.5f), 0.5f, ZoneSystem.instance.m_blockRayMask)) { continue; }

                    return candidate;
                }

                // Nothing worked: put the summon on the creature itself. That spot is known good because the
                // creature is standing in it, and it is what the spawn multiplier already does indoors
                // (Spawnrate skips the offset entirely above y 3000). A stacked summon that walks itself
                // apart beats a skipped wave, which would quietly under-fill the cap.
                if (Logger.IsDebugEnabled) { Logger.LogDebug($"No clear summon position found near {origin} after {SpawnPositionAttempts} attempts; spawning on the summoner."); }
                return origin;
            }

            private ZDOID SpawnCreatureRandomly() {
                GameObject toSummon = summonableCreatures[UnityEngine.Random.Range(0, summonableCreatures.Count)];
                Vector3 spawnPosition = FindSummonPosition();
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
