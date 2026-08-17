using HarmonyLib;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {

    // CreatureSetupControl.ForceAwake wakes an SLS-spawned creature and zeroes m_fallAsleepDistance on the clone,
    // but that field isn't networked and doesn't persist: any client that later instantiates the creature (ZDO
    // ownership handoff, zone reload, relog) builds it from the prefab again and gets the sleeping cave-dweller
    // defaults back. The SLS_NO_SLEEP ZDO flag is the durable half of the decision; these two patches honour it.
    //
    // Same shape as the SLS_NEMESIS_BOSS handling in CreatureSetupPatches: re-apply non-networked component state
    // from an SLS_* ZDO bool in Awake.
    internal static class CreatureSleepPatches {

        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.Awake))]
        public static class KeepSlsSpawnsAwake {
            public static void Postfix(MonsterAI __instance) {
                ZDO zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
                if (zdo == null || zdo.GetBool(SLS_NO_SLEEP, false) == false) { return; }
                __instance.m_fallAsleepDistance = 0f;
                if (__instance.m_sleeping == false) { return; }
                // Set the state directly rather than calling Wakeup(): this runs on every machine that ever
                // instantiates the creature, and Wakeup() would replay the wakeup effect and an RPC each time.
                __instance.m_sleeping = false;
                __instance.m_animator.SetBool(MonsterAI.s_sleeping, false);
                if (__instance.m_nview.IsOwner()) { zdo.Set(ZDOVars.s_sleeping, false); }
            }
        }

        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.Sleep))]
        public static class SlsSpawnsNeverFallAsleep {
            public static bool Prefix(MonsterAI __instance) {
                ZDO zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
                if (zdo == null || zdo.GetBool(SLS_NO_SLEEP, false) == false) { return true; }
                // Re-assert it here too: this is the one call site that would have put the creature under.
                __instance.m_fallAsleepDistance = 0f;
                return false;
            }
        }
    }
}
