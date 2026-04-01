using HarmonyLib;
using PlayFab.EconomyModels;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.modules.Sizes.SizeModifications;

namespace StarLevelSystem.modules.Sizes {
    internal static class Patches {

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        public static class CreatureSizeSyncEquipItems {
            public static void Postfix(Character __instance) {
                if (__instance.IsPlayer()) { return; }
                // Logger.LogDebug($"Character Awake called for {__instance.name} with level {__instance.m_level}");
                CharacterCacheEntry cDetails = CompositeLazyCache.GetCacheEntry(__instance);
                ApplySaveSizeModifications(__instance.gameObject, __instance.m_nview, cDetails);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachItem))]
        public static class VisualEquipmentScaleToFit {
            public static void Postfix(VisEquipment __instance, GameObject __result) {
                if (__instance.m_isPlayer == true || __instance.m_nview == null || __instance.m_nview.GetZDO() == null || __result == null) { return; }
                CharacterCacheEntry cDetails = CompositeLazyCache.GetCacheEntry(__instance.m_nview.GetZDO().m_uid.ID);
                if (cDetails == null) { return; }
                ApplySaveSizeModifications(__result, __instance.m_nview, cDetails);
            }
        }

        // NOTE: Because this is where we are cleaning up the cache, it is possible that the cache will not be cleaned up
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
        public static class ModifyRagdollHumanoid {
            public static void Postfix(Character __instance, Ragdoll ragdoll) {
                if (__instance == null || __instance.IsPlayer() || __instance.m_nview == null) { return; }


                CharacterCacheEntry cDetails = CompositeLazyCache.GetAndSetLocalCache(__instance);
                //Logger.LogDebug($"Ragdoll Humanoid created for {__instance.name} - cdetails? {cDetails != null} with level {__instance.m_level}");
                if (__instance.m_level > 1 && cDetails != null) {
                    ApplySaveSizeModifications(ragdoll.gameObject, __instance.m_nview, cDetails, true);

                    if (cDetails.Colorization != null) {
                        Colorization.ApplyColorizationWithoutLevelEffects(ragdoll.gameObject, cDetails.Colorization);
                    }
                }
                CompositeLazyCache.ClearCachedCreature(__instance);
            }
        }
    }
}
