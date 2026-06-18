using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {
    internal static class CreatureSetupPatches {

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class CreatureCharacterExtension {
            public static void Postfix(Character __instance) {
                // Re-apply persisted boss status: Character.m_boss isn't networked/saved, so without this a
                // nemesis miniboss would fall back to the narrow enemy healthbar on clients / after reload.
                // Done before EnemyHud.ShowHud picks a template so it gets the wide boss bar.
                ZDO zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
                if (zdo != null && zdo.GetBool(SLS_NEMESIS_BOSS, false)) {
                    __instance.m_boss = true;
                }

                CreatureSetupControl.CreatureSetup(__instance, delay: ValConfig.InitialDelayBeforeSetup.Value);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        public static class ModifyCharacterVisualsToLevel {
            public static void Prefix(Character __instance, ref int level) {
                if (level <= 1) { level = 1; }
            }
        }
    }
}
