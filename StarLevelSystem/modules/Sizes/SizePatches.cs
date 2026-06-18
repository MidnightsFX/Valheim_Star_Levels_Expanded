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
    internal static class SizePatches {

        //[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        //public static class CreatureSizeSyncEquipItems {
        //    public static void Postfix(Character __instance) {
        //        if (__instance.IsPlayer()) { return; }
        //        // Logger.LogDebug($"Character Awake called for {__instance.name} with level {__instance.m_level}");
        //        CharacterCacheEntry cDetails = CompositeLazyCache.GetCacheEntry(__instance);
        //        ApplySizeModifications(__instance.gameObject, cDetails);
        //    }
        //}


        // Locally sync size to the creatures set size
        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachItem))]
        public static class VisualEquipmentScaleToFit {
            public static void Postfix(VisEquipment __instance, GameObject __result) {
                if (__instance == null || __instance.m_isPlayer || __result == null) { return; }
                Transform parent = __result.transform.parent;
                if (parent == null) { return; }
                __result.transform.localScale = Vector3.Scale(__result.transform.localScale, parent.lossyScale);
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
                    Vector3 size = __instance.m_nview.m_zdo.GetVec3(SLS_SIZE, Vector3.zero);
                    if (size != Vector3.zero) {
                        ragdoll.transform.localScale = GetSizeReferenceForObject(ragdoll.gameObject.name) * size.x;
                    }

                    if (cDetails.Colorization != null) {
                        Colorization.ApplyColorizationWithoutLevelEffects(ragdoll.gameObject, cDetails.Colorization);
                    }
                }
                CompositeLazyCache.ClearCachedCreature(__instance);
            }
        }
    }
}
