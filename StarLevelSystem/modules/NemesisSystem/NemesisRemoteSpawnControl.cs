using Jotunn.Entities;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    // Server-authoritative logic for the remote (ambient, world-wide) Nemesis boss spawning system:
    // the spawner prefab, spawn-point scouting, placement, the active-boss registry (for caps + pins),
    // and the shared boss/minion spawn routine reused by the reactive Nemesis path.
    internal static class NemesisRemoteSpawnControl {

        internal const string SpawnerPrefabName = "SLS_NemesisRemoteSpawner";
        internal static GameObject SpawnerPrefab;
        internal static NemesisRemoteSpawnManager Manager;

        // Height above the validated ground point at which the dormant "sky object" spawner is placed.
        private const float PlacementHeightOffset = 100f;

        // Server-side registry of live remote bosses keyed by pin id. Seeded on startup from the persisted
        // state file and reconciled against live dormant-spawner ZDOs. Drives the server-wide caps and the
        // shared map-pin set.
        internal static Dictionary<string, NemesisBossPin> ActiveRemoteBosses = new Dictionary<string, NemesisBossPin>();

        // -------------------------------------------------------------------------------------------------
        // Assets
        // -------------------------------------------------------------------------------------------------

        internal static void LoadAssets() {
            if (SpawnerPrefab != null) { return; }

            // Prefab authored in the embedded asset bundle (a ZNetView holder + NemesisRemoteSpawner component).
            GameObject bundlePrefab = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>(SpawnerPrefabName + ".prefab");
            if (bundlePrefab == null) {
                Logger.LogWarning($"[NemesisRemote] LoadAssets: bundle prefab '{SpawnerPrefabName}.prefab' NOT FOUND in the asset bundle.");
                return;
            }

            ZNetView pv = bundlePrefab.GetComponent<ZNetView>();
            bool hasController = bundlePrefab.GetComponent<NemesisRemoteSpawner>() != null;
            Logger.LogInfo($"[NemesisRemote] LoadAssets: loaded '{bundlePrefab.name}' znetview={(pv != null)} persistent={(pv != null && pv.m_persistent)} hasController={hasController}");
            if (pv != null) { pv.m_persistent = true; }   // enforce persistence so the placed ZDO survives unload
            if (!hasController) { bundlePrefab.AddComponent<NemesisRemoteSpawner>(); }

            // Register with ZNetScene so the persistent ZDO can be re-instantiated when a player loads the area.
            if (PrefabManager.Instance.GetPrefab(SpawnerPrefabName) == null) {
                PrefabManager.Instance.AddPrefab(new CustomPrefab(bundlePrefab, false));
            }
            SpawnerPrefab = PrefabManager.Instance.GetPrefab(SpawnerPrefabName);
            Logger.LogInfo($"[NemesisRemote] LoadAssets: registered={(SpawnerPrefab != null)} name='{(SpawnerPrefab != null ? SpawnerPrefab.name : "null")}'");
        }

        // -------------------------------------------------------------------------------------------------
        // Registry / persistence (server only)
        // -------------------------------------------------------------------------------------------------

        internal static void LoadState() {
            ActiveRemoteBosses = new Dictionary<string, NemesisBossPin>();
            try {
                if (System.IO.File.Exists(ValConfig.nemesisRemoteStateFilePath)) {
                    var loaded = DataObjects.yamlDeserializer.Deserialize<Dictionary<string, NemesisBossPin>>(System.IO.File.ReadAllText(ValConfig.nemesisRemoteStateFilePath));
                    if (loaded != null) { ActiveRemoteBosses = loaded; }
                }
            } catch (Exception e) {
                Logger.LogWarning($"Failed to load Nemesis remote spawn state, starting fresh. {e.Message}");
                ActiveRemoteBosses = new Dictionary<string, NemesisBossPin>();
            }
        }

        internal static void SaveState() {
            try {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                System.IO.File.WriteAllText(ValConfig.nemesisRemoteStateFilePath, DataObjects.yamlSerializer.Serialize(ActiveRemoteBosses));
            } catch (Exception e) {
                Logger.LogWarning($"Failed to save Nemesis remote spawn state. {e.Message}");
            }
        }

        internal static void RegisterActiveBoss(NemesisBossPin pin) {
            if (pin == null || string.IsNullOrEmpty(pin.Id)) { return; }
            ActiveRemoteBosses[pin.Id] = pin;
            SaveState();
        }

        // Remove a boss from the registry by its pin id and broadcast pin removal to clients.
        internal static void RemoveActiveBoss(string pinId) {
            if (string.IsNullOrEmpty(pinId)) { return; }
            if (ActiveRemoteBosses.Remove(pinId)) {
                SaveState();
            }
            ValConfig.BroadcastNemesisBossPinRemove(pinId);
        }

        // Re-add any dormant spawner ZDOs (placed but missing from the in-memory registry, e.g. after a
        // registry-file loss) so their reserved slots are still counted against the caps.
        internal static void ReconcileFromSpawnerZDOs() {
            if (ZDOMan.instance == null) { return; }
            List<ZDO> zdos = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(SpawnerPrefabName, zdos, ref index)) { }
            foreach (ZDO zdo in zdos) {
                if (zdo == null || !zdo.IsValid()) { continue; }
                string pinId = zdo.GetString(SLS_NEMESIS_PIN, "");
                if (string.IsNullOrEmpty(pinId) || ActiveRemoteBosses.ContainsKey(pinId)) { continue; }
                Vector3 pos = zdo.GetPosition();
                ActiveRemoteBosses[pinId] = new NemesisBossPin() {
                    Id = pinId,
                    Position = pos,
                    Biome = Heightmap.FindBiome(pos),
                    Name = zdo.GetString(SLS_NAME, "")
                };
            }
        }

        internal static int CountActiveTotal() {
            return ActiveRemoteBosses.Count;
        }

        internal static int CountActivePerBiome(Heightmap.Biome biome) {
            return ActiveRemoteBosses.Values.Count(p => p.Biome == biome);
        }

        internal static List<NemesisBossPin> GetActiveBossPins() {
            return ActiveRemoteBosses.Values.ToList();
        }

        // -------------------------------------------------------------------------------------------------
        // Placement
        // -------------------------------------------------------------------------------------------------

        // Instantiate a dormant spawner at a validated ground point, register the boss, and broadcast its pin.
        internal static void PlaceSpawner(NemesisMiniboss boss, Vector3 groundPoint, Heightmap.Biome biome) {
            if (boss == null) { return; }
            string pinId = Guid.NewGuid().ToString("N");
            string bossName = boss.BossSpawn != null ? boss.BossSpawn.CustomName : "";

            Vector3 placePos = groundPoint + Vector3.up * PlacementHeightOffset;
            GameObject go = GameObject.Instantiate(SpawnerPrefab, placePos, Quaternion.identity);
            NemesisRemoteSpawner spawner = go.GetComponent<NemesisRemoteSpawner>();
            spawner.Setup(boss, biome, pinId, bossName);

            ZNetView goView = go.GetComponent<ZNetView>();
            ZDO goZdo = goView != null ? goView.GetZDO() : null;
            Logger.LogDebug($"[NemesisRemote] PlaceSpawner: instantiated '{SpawnerPrefab.name}' at {placePos} biome={biome} boss='{boss.BossSpawn?.Prefab}' " +
                $"zdoValid={(goZdo != null && goZdo.IsValid())} persistent={(goZdo != null && goZdo.Persistent)} owner={(goZdo != null && goView.IsOwner())} pin={pinId}");

            NemesisBossPin pin = new NemesisBossPin() { Id = pinId, Position = groundPoint, Biome = biome, Name = bossName };
            RegisterActiveBoss(pin);
            if (NemesisSystemData.SLE_Nemesis_Settings.RemoteSpawning.ShowMapPin) {
                ValConfig.BroadcastNemesisBossPinAdd(pin);
            }
        }

        // -------------------------------------------------------------------------------------------------
        // Scouting (server, async): find a valid ground point in the target biome, force-loading far zones.
        // -------------------------------------------------------------------------------------------------

        private static readonly int MaxScoutTries = 120;

        internal static IEnumerator ScoutBiomeLocation(Heightmap.Biome biome, Action<bool, Vector3> onComplete) {
            if (ZoneSystem.instance == null) { onComplete?.Invoke(false, Vector3.zero); yield break; }

            Tuple<float, float> range = GetBiomeRadiusRange(biome);
            int tries = 0;

            while (tries < MaxScoutTries) {
                tries++;

                // Yield periodically to avoid stalling the main thread while we force-load zones.
                if (tries % 5 == 0) { yield return new WaitForSeconds(0.1f); }

                Vector3 candidate = SelectWorldPoint(range, tries, biome);

                // Force the target zone to generate so ground data is available server-side.
                Vector2i zoneId = ZoneSystem.GetZone(candidate);
                int zoneWait = 0;
                while (!ZoneSystem.instance.SpawnZone(zoneId, ZoneSystem.SpawnMode.Client, out _)) {
                    zoneWait++;
                    if (zoneWait > 30) { break; }
                    yield return new WaitForEndOfFrame();
                }

                if (IsSpawnLocationValid(candidate, biome, out Vector3 groundPoint)) {
                    Logger.LogInfo($"[NemesisRemote] scout: found valid {biome} location at {groundPoint} after {tries} tries.");
                    onComplete?.Invoke(true, groundPoint);
                    yield break;
                }
            }

            Logger.LogInfo($"[NemesisRemote] scout: exhausted {MaxScoutTries} tries for biome {biome}, no valid location found.");
            onComplete?.Invoke(false, Vector3.zero);
        }

        private static Tuple<float, float> GetBiomeRadiusRange(Heightmap.Biome biome) {
            var ranges = NemesisSystemData.SLE_Nemesis_Settings.RemoteSpawning.BiomeRadiusRanges;
            if (ranges != null && ranges.TryGetValue(biome, out BiomeSpawnRadius r)) {
                return new Tuple<float, float>(r.Min, r.Max);
            }
            return new Tuple<float, float>(500f, WorldGenerator.waterEdge);
        }

        // Mirrors EpicLoot BountyLocationEarlyCache.SelectWorldPoint: Ashlands/DeepNorth are banded to the
        // far south/north, everything else is scattered across the ring.
        private static Vector3 SelectWorldPoint(Tuple<float, float> range, int intervalRange, Heightmap.Biome biome) {
            float min = range.Item1;
            float max = range.Item2;

            if (biome == Heightmap.Biome.AshLands || biome == Heightmap.Biome.DeepNorth) {
                float direction = biome == Heightmap.Biome.AshLands ? -1f : 1f;
                float naturalY = UnityEngine.Random.Range(min + (intervalRange * 90), min + (intervalRange * 90) + 100f);
                float yDirection = naturalY * direction;
                float xDirection = UnityEngine.Random.Range(-1f * (min / 2f), (min / 2f));
                return new Vector3(xDirection, 0, yDirection);
            }

            Vector2 randomPoint = UnityEngine.Random.insideUnitCircle;
            float magnitude = Mathf.Lerp(min, max, randomPoint.magnitude);
            randomPoint = randomPoint * magnitude;
            return new Vector3(randomPoint.x, 0, randomPoint.y);
        }

        // Combines EpicLoot's biome/water/base/ward checks with SLS's FindFloor object-overlap and lava checks.
        private static bool IsSpawnLocationValid(Vector3 location, Heightmap.Biome targetBiome, out Vector3 groundPoint) {
            groundPoint = location;
            ZoneSystem.instance.GetGroundData(ref location, out var normal, out var foundBiome, out var biomeArea, out var hmap);

            if (hmap == null || foundBiome == Heightmap.Biome.None || foundBiome != targetBiome) { return false; }

            float terrainHeight = location.y;
            if (!ZoneSystem.instance.FindFloor(new Vector3(location.x, location.y + 100f, location.z), out float solidHeight)) {
                return false;
            }
            float terrainDiff = solidHeight - terrainHeight;
            // Blocked by an existing object / too far off the ground.
            if (terrainDiff > 1f) { return false; }
            if (terrainDiff > 0f) { location.y = solidHeight; }

            // Below/near water level (allow shallow ocean spawns only for the Ocean biome).
            float waterLevel = ZoneSystem.instance.m_waterLevel;
            if (foundBiome != Heightmap.Biome.Ocean && waterLevel > location.y + 2f) { return false; }
            if (location.y < 27f) { return false; }

            // Avoid lava in the Ashlands.
            if (foundBiome == Heightmap.Biome.AshLands && hmap.IsLava(location)) { return false; }

            // Avoid player bases and warded areas.
            if ((bool)EffectArea.IsPointInsideArea(location, EffectArea.Type.PlayerBase)) { return false; }
            if (PrivateArea.m_allAreas.Any(a => a.IsInside(location, 0f))) { return false; }

            location.y += 0.5f;
            groundPoint = location;
            return true;
        }

        // -------------------------------------------------------------------------------------------------
        // Shared boss/minion spawn routine (reused by the reactive Nemesis path)
        // -------------------------------------------------------------------------------------------------

        // Spawn a full miniboss group (minions + boss) at a point. Applies the biome loot table; the boss
        // carries the SLS_NEMESIS_BOSS flag and (when provided) the pin id so its death removes the pin.
        internal static void SpawnMinibossGroup(NemesisMiniboss boss, Vector3 point, Heightmap.Biome biome, int extraLevelBonus, string pinId) {
            if (boss == null) { return; }
            List<ExtendedCharacterDrop> biomeLoot = null;
            NemesisSystemData.SLE_Nemesis_Settings.NemesisBossLootTables?.TryGetValue(biome, out biomeLoot);
            Quaternion rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            Logger.LogInfo($"[NemesisRemote] SpawnMinibossGroup at {point} biome={biome} boss='{boss.BossSpawn?.Prefab}' minions={boss.Minions?.Count ?? 0} biomeLoot={(biomeLoot?.Count ?? 0)}");

            if (boss.Minions != null) {
                foreach (NemesisSpawn minion in boss.Minions) {
                    SpawnNemesisSpawn(minion, point, rot, extraLevelBonus, biomeLoot, null);
                }
            }
            if (boss.BossSpawn != null) {
                SpawnNemesisSpawn(boss.BossSpawn, point, rot, extraLevelBonus, biomeLoot, pinId);
            }
        }

        // Instantiate and fully set up one NemesisSpawn group (extracted from NemesisActions.NemesisRandomSpawner).
        internal static void SpawnNemesisSpawn(NemesisSpawn spawn, Vector3 basePoint, Quaternion rot, int levelBonus, List<ExtendedCharacterDrop> extraBiomeLoot, string pinId) {
            if (spawn == null || string.IsNullOrEmpty(spawn.Prefab)) { return; }
            var offset = UnityEngine.Random.insideUnitCircle * 0.8f;
            Vector3 determinedSpawn = basePoint + new Vector3(offset.x, 0, offset.y);
            int count = 0;
            while (count < spawn.SpawnGroupSize) {
                count++;
                GameObject go = PrefabManager.Instance.GetPrefab(spawn.Prefab);
                if (go == null) { Logger.LogWarning($"[NemesisRemote] spawn prefab '{spawn.Prefab}' not found, skipping."); continue; }
                GameObject cgo = GameObject.Instantiate(go, determinedSpawn, rot);
                Character spawnChara = cgo.GetComponent<Character>();
                if (spawnChara == null) { Logger.LogWarning($"[NemesisRemote] spawn '{spawn.Prefab}' has no Character component, skipping."); continue; }
                Logger.LogInfo($"[NemesisRemote] instantiated '{spawn.Prefab}' (boss={spawn.IsBoss}) at {determinedSpawn}");

                if (spawn.Faction != Character.Faction.TrainingDummy) {
                    spawnChara.m_faction = spawn.Faction;
                }
                if (spawn.IsBoss) {
                    spawnChara.m_boss = true;
                    // Match vanilla bosses: keep the wide boss healthbar up within range even when the boss
                    // de-alerts (otherwise EnemyHud.TestShow drops it the moment it loses aggro).
                    spawnChara.m_dontHideBossHud = true;
                    // Persist boss status so the wide boss healthbar survives reloads / shows on other clients.
                    if (spawnChara.m_nview != null) {
                        spawnChara.m_nview.GetZDO().Set(SLS_NEMESIS_BOSS, true);
                        if (string.IsNullOrEmpty(pinId) == false) {
                            spawnChara.m_nview.GetZDO().Set(SLS_NEMESIS_PIN, pinId);
                        }
                    }
                }
                if (string.IsNullOrEmpty(spawn.CustomName) == false && spawnChara.m_nview != null) {
                    spawnChara.m_nview.GetZDO().Set(SLS_NAME, spawn.CustomName);
                }

                MonsterAI spawnAI = cgo.GetComponent<MonsterAI>();
                if (spawnAI != null) {
                    CreatureSetupControl.ApplySpawnAI(spawnAI, spawn.CreatureAI);
                    if (spawn.DespawnIfNotAlerted) {
                        spawnAI.SetEventCreature(true);
                    }
                }

                // Level generators (inline or referenced) roll a level that overrides ForcedLevel when configured.
                int rolledLevel = LevelGeneratorResolver.RollLevel(spawn.LevelupGenerators, spawn.LevelupGeneratorRefs);
                int spawnLevelOverride = rolledLevel > 0 ? rolledLevel : spawn.ForcedLevel;
                CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(spawnChara, requiredModifiers: spawn.RequiredModifiers, leveloverride: spawnLevelOverride);
                cce.Level = Mathf.Min(levelBonus + cce.Level, ValConfig.MaxLevel.Value);
                // Merge the spawn's overrides onto the fully-seeded cache dictionaries rather than replacing them,
                // so partial config (e.g. only BaseHealth) doesn't drop default keys like AttackSpeed/SpeedPerLevel
                // that other systems index directly.
                if (spawn.CreaturePerLevelValueModifiers != null) {
                    foreach (var entry in spawn.CreaturePerLevelValueModifiers) {
                        cce.CreaturePerLevelValueModifiers[entry.Key] = entry.Value;
                    }
                }
                if (spawn.CreatureBaseValueModifiers != null) {
                    foreach (var entry in spawn.CreatureBaseValueModifiers) {
                        cce.CreatureBaseValueModifiers[entry.Key] = entry.Value;
                    }
                }
                if (spawn.RequiredModifiers != null && spawn.RequiredModifiers.Count > 0) {
                    cce.CreatureModifiers = spawn.RequiredModifiers;
                    CompositeLazyCache.SetCreatureModifiers(spawnChara, spawn.RequiredModifiers);
                }
                CreatureSetupControl.CreatureSetup(spawnChara, cce.Level, delay: 0);

                // Merge the biome boss loot with any spawn-specific custom loot and persist to the ZDO.
                List<ExtendedCharacterDrop> mergedLoot = MergeLoot(spawn.CustomLoot, extraBiomeLoot);
                if (mergedLoot != null && mergedLoot.Count > 0) {
                    LootSystemData.SetCustomLoot(spawnChara, mergedLoot);
                }
            }
        }

        private static List<ExtendedCharacterDrop> MergeLoot(List<ExtendedCharacterDrop> primary, List<ExtendedCharacterDrop> extra) {
            bool hasPrimary = primary != null && primary.Count > 0;
            bool hasExtra = extra != null && extra.Count > 0;
            if (!hasPrimary && !hasExtra) { return null; }
            List<ExtendedCharacterDrop> merged = new List<ExtendedCharacterDrop>();
            if (hasPrimary) { merged.AddRange(primary); }
            if (hasExtra) { merged.AddRange(extra); }
            return merged;
        }
    }
}
