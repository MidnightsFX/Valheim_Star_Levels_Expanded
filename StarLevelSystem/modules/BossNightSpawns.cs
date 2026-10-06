using BepInEx.Configuration;
using HarmonyLib;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;

namespace StarLevelSystem.modules
{
    // The night spawns a boss unlocks. Vanilla's spawn lists hold entries that spawn only at night, and only once a boss's
    // defeat key is set: Greydwarfs roam the Meadows after Eikthyr, Skeletons after Bonemass, Seekers after the Queen. Each
    // boss has a server setting that stops its tier. The tiers are read from the spawn data itself, so a mod's night spawn
    // gated on a boss key is covered the same way, and turning one off takes effect on the next spawn pass.
    //
    // Spawning runs on whichever client owns the zone (SpawnSystem.UpdateSpawning needs a local player, so a dedicated
    // server never does it), which is why these are synced server settings rather than anything the server enforces.
    internal static class BossNightSpawns
    {
        // What each boss's night spawns are in vanilla, for where the live spawn lists cannot be read: the setting
        // descriptions, and the quick configure page on the main menu. Taken from the shipped spawn lists through the
        // Queen; nothing was found for Moder, and Fader came later than the lists that were checked. Empty means none known.
        internal static readonly Dictionary<string, string> VanillaSpawns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "defeated_eikthyr", "Greydwarfs in the Meadows" },
            { "defeated_gdking", "Greydwarf brutes and shamans in the Meadows, and Draugr in fog across the Meadows, Black Forest, Mountain and Plains" },
            { "defeated_bonemass", "Skeletons across the Meadows, Black Forest, Swamp, Mountain and Plains" },
            { "defeated_dragon", "" },
            { "defeated_goblinking", "Fulings across the Meadows, Black Forest and Mountain" },
            { "defeated_queen", "Seekers, Seeker broods and Ticks across the Meadows, Black Forest, Mountain and Plains" },
            { "defeated_fader", "" },
        };

        internal static string SettingDescription(string bossKey, string bossName) {
            string vanilla = VanillaSpawns.TryGetValue(bossKey, out string spawns) && spawns.Length > 0
                ? $"In vanilla that is {spawns}."
                : "None are known in vanilla, but other mods can add them.";
            return $"Allows the creatures that only spawn at night once {bossName} is defeated. {vanilla} Turn off to stop them; any already out still leave at dawn. Nothing else the boss unlocks is affected.";
        }

        private static Dictionary<string, ConfigEntry<bool>> toggles;

        // Boss defeat key to the setting that allows that boss's night spawns. Built on first use, after the config is bound.
        private static Dictionary<string, ConfigEntry<bool>> Toggles {
            get {
                if (toggles == null) {
                    toggles = new Dictionary<string, ConfigEntry<bool>>(StringComparer.OrdinalIgnoreCase) {
                        { "defeated_eikthyr", ValConfig.EikthyrNightSpawns },
                        { "defeated_gdking", ValConfig.ElderNightSpawns },
                        { "defeated_bonemass", ValConfig.BonemassNightSpawns },
                        { "defeated_dragon", ValConfig.ModerNightSpawns },
                        { "defeated_goblinking", ValConfig.YagluthNightSpawns },
                        { "defeated_queen", ValConfig.QueenNightSpawns },
                        { "defeated_fader", ValConfig.FaderNightSpawns },
                    };
                }
                return toggles;
            }
        }

        internal static ConfigEntry<bool> ToggleFor(string bossKey) {
            if (string.IsNullOrEmpty(bossKey)) { return null; }
            return Toggles.TryGetValue(bossKey, out ConfigEntry<bool> toggle) ? toggle : null;
        }

        private static bool AnyTurnedOff() {
            foreach (ConfigEntry<bool> toggle in Toggles.Values) {
                if (toggle != null && toggle.Value == false) { return true; }
            }
            return false;
        }

        // A creature that spawns only at night, once a boss with a setting here is defeated. Odin's visit after the Elder is
        // gated the same way, but he is not a creature and frightens nobody, so he is left alone.
        internal static bool IsBossNightSpawn(SpawnSystem.SpawnData spawn, out ConfigEntry<bool> toggle) {
            toggle = null;
            if (spawn == null || spawn.m_spawnAtDay || spawn.m_spawnAtNight == false || string.IsNullOrEmpty(spawn.m_requiredGlobalKey)) { return false; }
            toggle = ToggleFor(spawn.m_requiredGlobalKey);
            return toggle != null && spawn.m_prefab != null && spawn.m_prefab.GetComponent<Character>() != null;
        }

        // The night spawns this world's spawn lists hold for one boss. False when no world is loaded, so there are no lists
        // to read. A live zone's lists are preferred over the prefab's, in case a spawn mod edits them as zones load.
        internal static bool TryGetLiveSpawns(string bossKey, out List<SpawnSystem.SpawnData> spawns) {
            spawns = new List<SpawnSystem.SpawnData>();
            SpawnSystem source = SpawnSystem.m_instances.Count > 0 ? SpawnSystem.m_instances[0] : null;
            if (source == null && ZoneSystem.instance != null && ZoneSystem.instance.m_zoneCtrlPrefab != null) {
                source = ZoneSystem.instance.m_zoneCtrlPrefab.GetComponent<SpawnSystem>();
            }
            if (source == null || source.m_spawnLists == null) { return false; }
            foreach (SpawnSystemList list in source.m_spawnLists) {
                if (list == null || list.m_spawners == null) { continue; }
                foreach (SpawnSystem.SpawnData spawn in list.m_spawners) {
                    if (spawn.m_enabled && IsBossNightSpawn(spawn, out _) && string.Equals(spawn.m_requiredGlobalKey, bossKey, StringComparison.OrdinalIgnoreCase)) {
                        spawns.Add(spawn);
                    }
                }
            }
            return true;
        }

        // Turned-off entries are switched off for the one pass and back on after it. Switching them off, rather than handing
        // the pass a shorter list, keeps every entry at its index: each entry's spawn timer is stored under a hash of its
        // position, so a shorter list would hand the timers after it to the wrong entries.
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.UpdateSpawnList))]
        public static class SkipTurnedOffNightSpawns {
            private static readonly List<SpawnSystem.SpawnData> switchedOff = new List<SpawnSystem.SpawnData>();

            // Event spawners are a raid's own list, started by the raid's keys rather than by these settings.
            private static void Prefix(List<SpawnSystem.SpawnData> spawners, bool eventSpawners) {
                if (eventSpawners || spawners == null || AnyTurnedOff() == false) { return; }
                foreach (SpawnSystem.SpawnData spawn in spawners) {
                    if (spawn == null || spawn.m_enabled == false) { continue; }
                    if (IsBossNightSpawn(spawn, out ConfigEntry<bool> toggle) == false || toggle.Value) { continue; }
                    spawn.m_enabled = false;
                    switchedOff.Add(spawn);
                }
            }

            // A finalizer, so the entries come back even when the pass throws: the spawn data is shared by every zone.
            private static void Finalizer() {
                if (switchedOff.Count == 0) { return; }
                foreach (SpawnSystem.SpawnData spawn in switchedOff) { spawn.m_enabled = true; }
                switchedOff.Clear();
            }
        }
    }
}
