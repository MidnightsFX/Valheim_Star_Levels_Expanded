using HarmonyLib;
using StarLevelSystem.common;
using UnityEngine;

namespace StarLevelSystem.modules.LocationReset {
    internal static class LocationResetPatches {

        // Server-side world entry point. ZoneSystem.Start runs on dedicated servers too (unlike the
        // MinimapManager callbacks), and by this point ZNet.instance and the world name exist, so it
        // is the right place to load per-world reset state and finish config bootstrapping.
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        public static class LocationResetWorldInit {
            [HarmonyPostfix]
            static void InitLocationResetOnServer() {
                LocationResetControl.OnZoneSystemReady();
            }
        }

        // RandEventSystem is the same host the Raid and Nemesis managers use: it exists on a loaded
        // world and on dedicated servers, and dies with the world.
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.Awake))]
        public static class AttachLocationResetManager {
            [HarmonyPostfix]
            static void AttachManager(RandEventSystem __instance) {
                if (__instance.gameObject.GetComponent<LocationResetManager>() != null) { return; }
                __instance.gameObject.AddComponent<LocationResetManager>();
            }
        }

        // Arm the ownership context around a location spawn, so every ZDO born inside it can be
        // stamped with the location that made it (see LocationOwnership).
        //
        // Full and Ghost are the only modes that create ZDOs. Client mode instantiates the prefab with
        // every ZNetView child SetActive(false), so their Awake never runs and no ZDO is born -- which
        // is also why the nested SpawnProxyLocation call the Full path makes is harmless.
        //
        // The exit is a FINALIZER, not a postfix. A postfix does not run when the patched method
        // throws, and a leaked context is the worst failure this system can have: every ZDO created
        // for the rest of the session would be stamped as this location's, and the next reset of that
        // location would delete the world around it.
        [HarmonyPatch(typeof(ZoneSystem), "SpawnLocation")]
        public static class TagLocationSpawn {
            private static bool Armable(ZoneSystem.ZoneLocation location, ZoneSystem.SpawnMode mode) {
                if (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost) { return false; }
                // The same composite gate every world-mutating path consults: server, both config
                // switches, no conflicting mod, world ready. A server that never enables location
                // reset writes no tags and grows its save by nothing.
                if (LocationResetControl.SweepAllowed == false) { return false; }
                return LocationOwnership.ShouldTag(location);
            }

            [HarmonyPrefix]
            static void Arm(ZoneSystem.ZoneLocation location, Vector3 pos, ZoneSystem.SpawnMode mode, out bool __state) {
                __state = Armable(location, mode);
                if (__state == false) { return; }
                LocationOwnership.Begin(LocationOwnership.KeyFor(ZoneSystem.GetZone(pos)));
            }

            // __state rather than re-deriving the verdict: SpawnLocation loads the location prefab, so
            // ShouldTag could legitimately answer differently on the way out and orphan a Begin.
            [HarmonyFinalizer]
            static void Release(bool __state) {
                if (__state == false) { return; }
                LocationOwnership.End();
            }
        }

        // Every ZDO in the game is born here: ZNetView.Awake calls
        // ZDOMan.CreateNewZDO(transform.position, prefabHash) for anything without an existing one.
        // Stamping at birth is the only site that also reaches a DungeonGenerator interior and a
        // CampRadial/CampGrid perimeter -- both are generated inside SpawnLocation and neither can be
        // enumerated from the location prefab, which is why every position-matching approach to this
        // problem misses them.
        //
        // The 2-argument overload specifically. The 3-argument one is also the network receive path
        // (ZDOMan.RPC_ZDOData), so patching it would let a ZDO a client just sent us be stamped as
        // location content.
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.CreateNewZDO), new System.Type[] { typeof(Vector3), typeof(int) })]
        public static class TagNewZdoWithLocationOwner {
            [HarmonyPostfix]
            static void Tag(ZDO __result) {
                if (__result == null) { return; }
                // No ownership claim needed at either site, unlike LocationOwnership.Tag: CreateNewZDO
                // has already made this session the owner, so the writes land cleanly.
                if (LocationOwnership.ActiveOwner != LocationOwnership.NoOwner) {
                    __result.Set(DataObjects.SLS_LOC_OWNER, LocationOwnership.ActiveOwner);
                }
                // Location first, so a creature spawned during a location build is attributed to the
                // location being built rather than to its spawner's older stamp.
                SpawnerLinks.TagNewZdo(__result);
            }
        }

        // Arm the spawner context around each spawner's spawn call, so SpawnerLinks can stamp the
        // creature as it is born (see SpawnerLinks for why the link needs two halves).
        //
        // A context rather than a per-type transpiler because the creature is a local variable in
        // every one of these: SpawnArea.SpawnOne and TriggerSpawner.Spawn both return a bool and keep
        // the GameObject to themselves, so there is no postfix argument that can reach it.
        // CreatureSpawner.Spawn does return its ZNetView, but routing it through the same context
        // keeps one mechanism instead of two that can drift.
        //
        // Finalizers again, for the reason spelled out on TagLocationSpawn: a leaked context would
        // attribute every ZDO created afterwards to a spawner that did not make it.
        //
        // One class per target, matching the rest of this file. LevelPatches already transpiles all
        // three of these methods; a prefix and a finalizer from a different patch class compose with
        // that fine, and the SpawnArea transpiler's forward branch skips vanilla's level code long
        // after the Instantiate that creates the ZDO we are here for.
        [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
        public static class TagCreatureSpawnerOutput {
            [HarmonyPrefix]
            static void Arm(CreatureSpawner __instance) { SpawnerLinks.Begin(__instance.m_nview); }
            [HarmonyFinalizer]
            static void Release() { SpawnerLinks.End(); }
        }

        // The one that actually needed this. SpawnArea is greydwarf nests, bone piles, draugr piles
        // and EvilHearts, it spawns many creatures over its life, and it records not one of them --
        // its own cap is a live proximity scan over BaseAI.BaseAIInstances, which sees only what is
        // loaded right now. Destroying such a nest used to leave its brood standing forever.
        [HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
        public static class TagSpawnAreaOutput {
            [HarmonyPrefix]
            static void Arm(SpawnArea __instance) { SpawnerLinks.Begin(__instance.m_nview); }
            [HarmonyFinalizer]
            static void Release() { SpawnerLinks.End(); }
        }

        [HarmonyPatch(typeof(TriggerSpawner), "Spawn")]
        public static class TagTriggerSpawnerOutput {
            [HarmonyPrefix]
            static void Arm(TriggerSpawner __instance) { SpawnerLinks.Begin(__instance.m_nview); }
            [HarmonyFinalizer]
            static void Release() { SpawnerLinks.End(); }
        }

        // Protection classification is keyed by prefab hash, resolved from ZNetScene's prefab list.
        // Rebuilding here covers dedicated servers, where the vanilla-prefab Jotunn callbacks that
        // other SLS systems use never fire.
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        public static class BuildLocationResetPrefabSets {
            [HarmonyPostfix]
            static void BuildSets() {
                ZoneProtectionScan.ResetPrefabSets();
                ZoneProtectionScan.BuildPrefabSets();
            }
        }
    }
}
