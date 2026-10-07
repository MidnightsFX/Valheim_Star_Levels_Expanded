using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StarLevelSystem.common
{
    // Every GameObject prefab SLS resolves by name goes through here, config-supplied names above all.
    //
    // Same order as Jotunn's PrefabManager.GetPrefab -- its custom prefabs, then ZNetScene, then ObjectDB --
    // but a miss stops there. Jotunn's last step is PrefabManager.Cache, which runs
    // Resources.FindObjectsOfTypeAll(typeof(GameObject)) and reads transform.parent on every result, so a
    // single misspelled name in a config walked every GameObject in memory. That walk crashed a client inside
    // Mono when it reached a Transform whose native object was already gone (a Nemesis event naming
    // Charred_Ranged, which does not exist; the vanilla prefab is Charred_Archer).
    internal static class PrefabLookup
    {
        // PrefabManager.Prefabs is internal. Networked custom prefabs are also in ZNetScene once ZNetScene.Awake
        // has run, but this covers the non-networked ones and lookups made before a world loads.
        private static Func<PrefabManager, Dictionary<string, CustomPrefab>> customPrefabsGetter;
        private static bool customPrefabsUnavailable = false;

        // Name + what was skipped, so a broken name on a hot path (every kill, every spawn) warns once per world
        // rather than on every call. Repeats still go to the debug log.
        private static readonly HashSet<string> WarnedMisses = new HashSet<string>();

        // Hooked to OnPrefabsRegistered, so each world load reports its broken names afresh.
        internal static void ResetWarnings() {
            WarnedMisses.Clear();
        }

        // Quiet lookup: null on a miss, nothing logged. Only for callers that expect to miss, like a
        // "register it if it is not there yet" check.
        internal static GameObject Find(string name) {
            if (string.IsNullOrEmpty(name)) { return null; }

            Dictionary<string, CustomPrefab> customPrefabs = CustomPrefabs();
            if (customPrefabs != null && customPrefabs.TryGetValue(name, out CustomPrefab custom) && custom.Prefab != null) {
                return custom.Prefab;
            }

            int hash = name.GetStableHashCode();
            if (ZNetScene.instance != null && ZNetScene.instance.m_namedPrefabs.TryGetValue(hash, out GameObject prefab) && prefab != null) {
                return prefab;
            }
            if (ObjectDB.instance != null && ObjectDB.instance.m_itemByHash.TryGetValue(hash, out GameObject item) && item != null) {
                return item;
            }
            return null;
        }

        // Lookup for everything else: null on a miss, with a warning naming the prefab and what the caller skips
        // because of it. The caller must skip that action when this returns null.
        internal static GameObject FindOrWarn(string name, string skipping) {
            GameObject prefab = Find(name);
            if (prefab != null) { return prefab; }

            // ZNetScene only exists in a world, so before one loads (startup config load, the main menu) a creature
            // name cannot resolve yet. Those callers resolve again once it is up, and a real miss warns then.
            if (ZNetScene.instance == null) {
                Logger.LogDebug($"Prefab '{name}' is not available yet, skipping {skipping}.");
                return null;
            }
            if (WarnedMisses.Add($"{name}|{skipping}")) {
                Logger.LogWarning($"Prefab '{name}' was not found, skipping {skipping}. Check that it is spelled correctly and that the mod adding it is installed.");
            } else if (Logger.IsDebugEnabled) {
                Logger.LogDebug($"Prefab '{name}' was not found, skipping {skipping}.");
            }
            return null;
        }

        private static Dictionary<string, CustomPrefab> CustomPrefabs() {
            if (customPrefabsGetter == null) {
                if (customPrefabsUnavailable) { return null; }
                try {
                    MethodInfo getter = AccessTools.PropertyGetter(typeof(PrefabManager), "Prefabs");
                    if (getter != null) {
                        customPrefabsGetter = AccessTools.MethodDelegate<Func<PrefabManager, Dictionary<string, CustomPrefab>>>(getter);
                    }
                } catch (Exception e) {
                    Logger.LogDebug($"Reading Jotunn's custom prefab list failed: {e.Message}");
                }
                if (customPrefabsGetter == null) {
                    customPrefabsUnavailable = true;
                    Logger.LogWarning("Jotunn's custom prefab list could not be read; prefab lookups will only search ZNetScene and ObjectDB.");
                    return null;
                }
            }
            return customPrefabsGetter(PrefabManager.Instance);
        }
    }
}
