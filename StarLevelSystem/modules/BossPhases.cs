using HarmonyLib;
using System;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    // The Deep North's final boss is fought as three creatures. FrozenKing's death effects spawn FrozenKing_p2, and
    // FrozenKing_p2's spawn FrozenKing_p3, so each phase is a fresh ZDO that would roll its own level and modifiers.
    //
    // Phase 2 is immune to every damage type but nonPlayer, and the only nonPlayer damage in the fight is the explosion
    // each Aspect_* spirit makes when it dies: 1000 against phase 2's 7000 health, seven aspects in all. The aspects come
    // in a fixed chain, each spawned by the death effects of the one before it. So phase 2 has to keep its vanilla
    // health and every aspect has to die exactly once: a starred or healing phase 2, a cloned aspect, or one removed
    // without dying leaves phase 2 alive and immune to everything the players can do.
    internal static class BossPhases
    {
        // Held at level 1 with no modifiers and vanilla health, whatever the config says.
        private static readonly HashSet<string> PinnedPrefabs = new HashSet<string>() { "FrozenKing_p2" };

        // Each boss phase, and the phase its death effects spawn. The first phase's level is handed down the chain,
        // past a pinned phase, so the boss comes back with the stars it started with.
        private static readonly Dictionary<string, string> NextPhase = new Dictionary<string, string>() {
            { "FrozenKing", "FrozenKing_p2" },
            { "FrozenKing_p2", "FrozenKing_p3" },
        };

        private const string AspectPrefix = "Aspect_";

        // Set only while a boss phase's OnDeath runs on its owner: vanilla instantiates the next phase inside it, and
        // that instance's Character.Awake runs there and then, before Instantiate returns.
        private static string pendingPhase;
        private static int pendingLevel;

        internal static bool IsPinned(Character chara) {
            return chara != null && PinnedPrefabs.Contains(Utils.GetPrefabName(chara.gameObject));
        }

        internal static bool IsAspect(Character chara) {
            return chara != null && Utils.GetPrefabName(chara.gameObject).StartsWith(AspectPrefix, StringComparison.Ordinal);
        }

        // Called from the Character.Awake postfix, ahead of SLS's setup. Writes the level the next phase inherits onto
        // its ZDO, which DetermineLevel then reads as a level already rolled. A pinned phase keeps level 1 and only
        // holds the level for the phase after it. The phase was just instantiated on this machine, so this machine owns it.
        internal static void TakeCarriedLevel(Character chara) {
            if (pendingPhase == null || chara == null || chara.m_nview == null || chara.m_nview.IsValid() == false || chara.m_nview.IsOwner() == false) { return; }
            if (Utils.GetPrefabName(chara.gameObject) != pendingPhase) { return; }
            ZDO zdo = chara.m_nview.GetZDO();
            zdo.Set(SLS_PHASE_LEVEL, pendingLevel);
            if (IsPinned(chara) || zdo.GetInt(ZDOVars.s_level, 0) > 0) { return; }
            zdo.Set(ZDOVars.s_level, pendingLevel);
            chara.m_level = pendingLevel;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class CarryLevelToNextPhase {
            // Owner only, as vanilla creates the death effects on the owner alone.
            private static void Prefix(Character __instance) {
                pendingPhase = null;
                if (__instance == null || __instance.m_nview == null || __instance.m_nview.IsValid() == false || __instance.m_nview.IsOwner() == false) { return; }
                if (NextPhase.TryGetValue(Utils.GetPrefabName(__instance.gameObject), out string next) == false) { return; }
                int level = __instance.m_nview.GetZDO().GetInt(SLS_PHASE_LEVEL, 0);
                if (level <= 0) { level = __instance.GetLevel(); }
                pendingPhase = next;
                pendingLevel = level;
            }

            // A finalizer, so a throw inside OnDeath cannot leave the next creature of that name to inherit the level.
            private static void Finalizer() {
                pendingPhase = null;
            }
        }
    }
}
