using HarmonyLib;
using StarLevelSystem.modules.Sizes;
using System;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Loot
{
    // Some creatures leave remains that are mined for their loot. A Gammeltroll drops nothing itself: its death effects
    // spawn TrollFrost_Dead, a petrified corpse, and a pickaxe breaks that into TrollFrost_Frac, a gold vein whose areas
    // each roll their own ore. Vanilla hands neither object anything from the troll, so the troll's level is carried
    // down both steps on the ZDO, and MineDrop scales the vein's ore by it.
    //
    // The corpse is also sized to the troll, which vanilla spawns at its prefab size whatever the troll's size. Both
    // objects sync their initial scale on the ZDO, and Destructible.Destroy already gives the vein the corpse's scale,
    // so sizing the corpse once sizes the vein too, for every peer and after a reload.
    //
    // Each step runs on one machine: the troll's owner creates the corpse, and the corpse's owner creates the vein. The
    // object written to was just instantiated there, so that machine owns it.
    internal static class CreatureRemains
    {
        // Each creature, and the remains its death effects leave.
        private static readonly Dictionary<string, string> RemainsOf = new Dictionary<string, string>() {
            { "TrollFrost", "TrollFrost_Dead" },
        };

        // Filler the remains drop alongside their ore, kept at the vanilla amount.
        internal static readonly HashSet<string> UnscaledDrops = new HashSet<string>() { "Stone" };

        // Set only while a creature with remains dies on its owner: vanilla instantiates the remains inside OnDeath, and
        // their Destructible.Awake runs there and then.
        private static string pendingRemains;
        private static int pendingRemainsLevel;
        private static float pendingRemainsScale;

        // Set only while remains that carry a level are destroyed. Vanilla instantiates what they break into inside
        // Destructible.Destroy and hits it with the same blow before Destroy returns, so the level has to be on it by Awake.
        private static string pendingBreakInto;
        private static int pendingBreakIntoHash;
        private static int pendingBreakLevel;

        // The level of the creature that left these remains, or 0 when they were not left by one.
        internal static int CarriedLevel(ZNetView nview) {
            if (nview == null || nview.IsValid() == false) { return 0; }
            return nview.GetZDO().GetInt(SLS_REMAINS_LEVEL, 0);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class CarryLevelToRemains {
            // Owner only, as vanilla creates the death effects on the owner alone.
            private static void Prefix(Character __instance) {
                pendingRemains = null;
                if (__instance == null || __instance.m_nview == null || __instance.m_nview.IsValid() == false || __instance.m_nview.IsOwner() == false) { return; }
                if (RemainsOf.TryGetValue(Utils.GetPrefabName(__instance.gameObject), out string remains) == false) { return; }
                pendingRemains = remains;
                pendingRemainsLevel = __instance.GetLevel();
                // Read off the troll's transform, so the corpse matches what players saw: star growth, Big and the
                // world's size factor alike.
                pendingRemainsScale = SizeModifications.CurrentScaleMultiplier(__instance.gameObject);
            }

            // A finalizer, so a throw inside OnDeath cannot leave the next object of that name to take the level.
            private static void Finalizer() {
                pendingRemains = null;
            }
        }

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Awake))]
        public static class TakeRemainsLevel {
            // Runs for every destructible that loads, so nothing is looked up unless a death is handing a level down.
            private static void Postfix(Destructible __instance) {
                if (pendingRemains == null) { return; }
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || __instance.m_nview.IsOwner() == false) { return; }
                if (Utils.GetPrefabName(__instance.gameObject) != pendingRemains) { return; }
                __instance.m_nview.GetZDO().Set(SLS_REMAINS_LEVEL, pendingRemainsLevel);
                // The death effect leaves the corpse's scale alone, so this holds. SetLocalScale stores it on the ZDO.
                if (Mathf.Approximately(pendingRemainsScale, 1f) == false) {
                    __instance.m_nview.SetLocalScale(__instance.transform.localScale * pendingRemainsScale);
                    TaskRunner.RequestPhysicsSync();
                }
                if (Logger.IsDebugEnabled) { Logger.LogDebug($"{pendingRemains} keeps the level {pendingRemainsLevel} and the size x{pendingRemainsScale} of the creature that left it."); }
            }
        }

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Destroy), new Type[] { typeof(HitData) })]
        public static class HandLevelToBrokenRemains {
            private static void Prefix(Destructible __instance) {
                pendingBreakInto = null;
                if (__instance.m_spawnWhenDestroyed == null) { return; }
                int level = CarriedLevel(__instance.m_nview);
                if (level <= 0) { return; }
                pendingBreakInto = __instance.m_spawnWhenDestroyed.name;
                pendingBreakIntoHash = pendingBreakInto.GetStableHashCode();
                pendingBreakLevel = level;
            }

            // A finalizer, for the same reason as CarryLevelToRemains.
            private static void Finalizer() {
                pendingBreakInto = null;
            }
        }

        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Awake))]
        public static class TakeBrokenRemainsLevel {
            // Runs for every MineRock5 that loads, so nothing is looked up unless broken remains are handing a level down.
            private static void Postfix(MineRock5 __instance) {
                if (pendingBreakInto == null) { return; }
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || __instance.m_nview.IsOwner() == false) { return; }
                // Matched on the ZDO's prefab, not the object's name: vanilla's Awake has just renamed the object
                // "___MineRock5 m_meshFilter" by naming the MeshFilter it adds to it.
                if (__instance.m_nview.GetZDO().GetPrefab() != pendingBreakIntoHash) { return; }
                __instance.m_nview.GetZDO().Set(SLS_REMAINS_LEVEL, pendingBreakLevel);
                if (Logger.IsDebugEnabled) { Logger.LogDebug($"{pendingBreakInto} keeps the level {pendingBreakLevel} of the creature whose remains it came from."); }
            }
        }
    }
}
