using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Raids
{
    internal static class RaidControl
    {

        internal static void ApplyRaidConfiguration(RandEventSystem res) {
            if (res == null) { return; }
            if (ValConfig.UseVanillaRaidConfiguration != null && ValConfig.UseVanillaRaidConfiguration.Value) {
                Logger.LogDebug("UseVanillaRaidConfiguration is true; leaving vanilla raid list untouched.");
                return;
            }

            RaidConfiguration cfg = RaidsData.SLE_Raid_Settings ?? RaidsData.DefaultConfiguration;

            if (cfg.GlobalSettings != null) {
                if (cfg.GlobalSettings.GlobalRaidIntervalScalar > 0f) {
                    res.m_eventIntervalMin *= cfg.GlobalSettings.GlobalRaidIntervalScalar;
                }
                if (cfg.GlobalSettings.GlobalRaidChanceScalar > 0f) {
                    res.m_eventChance *= cfg.GlobalSettings.GlobalRaidChanceScalar;
                }
                if (cfg.GlobalSettings.DisableAllRaids) {
                    res.m_events.Clear();
                    Logger.LogInfo("SLS raid system: DisableAllRaids set, cleared all random events.");
                    return;
                }
            }

            if (cfg.Raids == null || cfg.Raids.Count == 0) {
                Logger.LogDebug("SLS raid system: no raids configured, leaving vanilla raid list untouched.");
                return;
            }

            List<RandomEvent> newEvents = new List<RandomEvent>();
            foreach (KeyValuePair<string, RaidDefinition> kv in cfg.Raids) {
                if (kv.Value == null || kv.Value.Enabled == false) { continue; }
                if (re != null) { newEvents.Add(kv.Value.ToRaid(kv.Value)); }
            }

            res.m_events = newEvents;
            Logger.LogInfo($"SLS raid system: applied {newEvents.Count} raid definitions.");
        }

        private static RandomEvent BuildRandomEvent(string name, RaidDefinition def, RandEventSystem rs) {
            RandomEvent match = rs.m_events.Find(e => e.m_name == name);
            RandomEvent re = match != null ? match : new RandomEvent();
            re.m_name = name;
            re.m_enabled = true;
            RaidActivation a = def.Activation ?? new RaidActivation();
            re.m_random = a.Random;
            re.m_duration = a.Duration;
            re.m_eventRange = a.EventRange;
            re.m_nearBaseOnly = a.NearBaseOnly;
            re.m_pauseIfNoPlayerInArea = a.PauseIfNoPlayerInArea;
            re.m_standaloneInterval = a.StandaloneInterval;
            re.m_standaloneChance = a.StandaloneChance;
            re.m_spawnerDelay = a.SpawnerDelay;
            re.m_biome = CombineBiomes(a.Biomes);
            re.m_requiredGlobalKeys = a.RequiredGlobalKeys ?? new List<string>();
            re.m_notRequiredGlobalKeys = a.NotRequiredGlobalKeys ?? new List<string>();
            re.m_startMessage = a.StartMessage ?? string.Empty;
            re.m_endMessage = a.EndMessage ?? string.Empty;
            re.m_forceMusic = a.ForceMusic ?? string.Empty;
            re.m_forceEnvironment = a.ForceEnvironment ?? string.Empty;

            re.m_spawn = new List<SpawnSystem.SpawnData>();
            if (def.Spawns != null) {
                foreach (RaidSpawnEntry s in def.Spawns) {
                    SpawnSystem.SpawnData sd = BuildSpawnData(s, a);
                    if (sd != null) { re.m_spawn.Add(sd); }
                }
            }

            return re;
        }

        private static SpawnSystem.SpawnData BuildSpawnData(RaidSpawnEntry entry, RaidActivation a) {
            if (string.IsNullOrEmpty(entry.PrefabName)) { return null; }
            GameObject prefab = PrefabManager.Instance.GetPrefab(entry.PrefabName);
            if (prefab == null) {
                Logger.LogWarning($"SLS raid system: prefab '{entry.PrefabName}' not found, skipping spawn entry.");
                return null;
            }

            SpawnSystem.SpawnData sd = new SpawnSystem.SpawnData();
            sd.m_name = entry.PrefabName;
            sd.m_prefab = prefab;
            sd.m_enabled = true;
            sd.m_biome = entry.Biome ?? CombineBiomes(a.Biomes);
            sd.m_biomeArea = Heightmap.BiomeArea.Everything;
            sd.m_maxSpawned = entry.MaxSpawned;
            sd.m_spawnInterval = entry.SpawnInterval;
            sd.m_spawnChance = entry.SpawnChance;
            sd.m_minLevel = entry.LevelMin;
            sd.m_maxLevel = entry.LevelMax;
            sd.m_spawnDistance = entry.SpawnDistance;
            sd.m_spawnRadiusMin = entry.SpawnRadiusMin;
            sd.m_spawnRadiusMax = entry.SpawnRadiusMax;
            sd.m_groupSizeMin = entry.GroupSizeMin;
            sd.m_groupSizeMax = entry.GroupSizeMax;
            sd.m_groupRadius = entry.GroupRadius;
            return sd;
        }

        private static Heightmap.Biome CombineBiomes(List<Heightmap.Biome> biomes) {
            if (biomes == null || biomes.Count == 0) { return Heightmap.Biome.None; }
            Heightmap.Biome combined = Heightmap.Biome.None;
            foreach (Heightmap.Biome b in biomes) { combined |= b; }
            return combined;
        }

        internal static RaidDefinition GetActiveRaidForPosition(Vector3 pos) {
            if (RandEventSystem.instance == null) { return null; }
            RandomEvent re = RandEventSystem.instance.GetCurrentRandomEvent();
            if (re == null) { return null; }
            if (!RandEventSystem.instance.IsInsideRandomEventArea(re, pos)) { return null; }

            RaidConfiguration cfg = RaidsData.SLE_Raid_Settings;
            if (cfg == null || cfg.Raids == null) { return null; }
            if (!cfg.Raids.TryGetValue(re.m_name, out RaidDefinition def)) { return null; }
            return def;
        }
    }
}
