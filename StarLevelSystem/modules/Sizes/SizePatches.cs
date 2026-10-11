using HarmonyLib;
using PlayFab.EconomyModels;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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


        // Vanilla parents a freshly instantiated item to its joint keeping its world scale, so a creature SLS has scaled
        // up holds a vanilla-size weapon (or helmet). Scale the new instance by the factor SLS has scaled the creature by.
        // That factor is its localScale over the prefab's own: the joint's lossyScale also carries the rig's bone scales,
        // which is why multiplying by it massively over-scaled. Every AttachItem call returns a new instance, so
        // re-equipping never compounds, and an item attached before a later resize already follows its joint.
        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.AttachItem))]
        public static class VisualEquipmentScaleToFit {
            public static void Postfix(VisEquipment __instance, Transform joint, GameObject __result) {
                if (__result == null || __instance == null || __instance.m_isPlayer || joint == null) { return; }
                // An attach_skin item is parented to the body model and skinned to the creature's own bones, so it
                // already follows the creature's scale.
                if (__result.transform.parent != joint) { return; }
                // Creatures only: SLS never resizes the other VisEquipment holders, such as armor stands.
                if (__instance.TryGetComponent(out Character character) == false || character.IsPlayer()) { return; }
                float multiplier = CurrentScaleMultiplier(character.gameObject);
                if (Mathf.Approximately(multiplier, 1f)) { return; }
                __result.transform.localScale *= multiplier;
            }
        }

        // Vanilla calls OnRagdollCreated right after Ragdoll.Setup, on the dying creature's owner only. Humanoid
        // overrides it without calling the base, so patching only Humanoid's version missed every creature that is a
        // plain Character (hare, deer, seal, ...), whose corpses kept vanilla's level tint and size. Patching both
        // runs this exactly once per corpse.
        // NOTE: Because this is where we are cleaning up the cache, it is possible that the cache will not be cleaned up
        [HarmonyPatch]
        public static class ModifyRagdoll {
            static IEnumerable<MethodBase> TargetMethods() {
                yield return AccessTools.Method(typeof(Character), nameof(Character.OnRagdollCreated));
                yield return AccessTools.Method(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated));
            }

            public static void Postfix(Character __instance, Ragdoll ragdoll) {
                if (__instance == null || ragdoll == null || __instance.IsPlayer() || __instance.m_nview == null) { return; }


                CharacterCacheEntry cDetails = CompositeLazyCache.GetAndSetLocalCache(__instance);
                //Logger.LogDebug($"Ragdoll created for {__instance.name} - cdetails? {cDetails != null} with level {__instance.m_level}");
                if (__instance.m_level > 1 && cDetails != null) {
                    // The corpse is a networked object of its own. Its scale isn't synced and vanilla writes its own
                    // level tint to its ZDO, so what is set here is stored on that ZDO too, for every other peer's copy.
                    ZDO ragdollZdo = ragdoll.m_nview != null ? ragdoll.m_nview.GetZDO() : null;
                    Vector3 size = __instance.m_nview.m_zdo.GetVec3(SLS_SIZE, Vector3.zero);
                    if (size != Vector3.zero) {
                        // SLS_SIZE stores the creature's sized scale, not a bare multiplier. Divide by the
                        // creature's own reference scale to recover the multiplier before applying it to the
                        // ragdoll's reference scale, otherwise any creature whose prefab is not unit-scaled
                        // (lox, troll, ...) gets a ragdoll scaled by its own base size a second time. The world's
                        // Combat size factor is not in SLS_SIZE, so it goes on here as it went on the creature.
                        float creatureRef = GetSizeReferenceForObject(__instance.gameObject.name).x;
                        float multiplier = Mathf.Approximately(creatureRef, 0f) ? size.x : size.x / creatureRef;
                        multiplier *= WorldRates.CreatureSizeFactor(__instance.transform.position);
                        ragdoll.transform.localScale = GetSizeReferenceForObject(ragdoll.gameObject.name) * multiplier;
                        ragdollZdo?.Set(SLS_SIZE, ragdoll.transform.localScale);
                    }

                    ColorDef colorization = cDetails.Colorization;
                    if (colorization != null) {
                        Colorization.ApplyColorizationWithoutLevelEffects(ragdoll.gameObject, colorization);
                        if (ragdollZdo != null) {
                            ragdollZdo.Set(ZDOVars.s_hue, colorization.Hue);
                            ragdollZdo.Set(ZDOVars.s_saturation, colorization.Saturation);
                            ragdollZdo.Set(ZDOVars.s_value, colorization.Value);
                            ragdollZdo.Set(SLS_RAGDOLL_COLOR, colorization.IsEmissive ? 2 : 1);
                        }
                    }
                }
                CompositeLazyCache.ClearCachedCreature(__instance);
            }
        }

        // Puts the size and colour ModifyRagdoll stored back on a corpse loaded from its ZDO: every other peer's copy,
        // and the dying peer's own when the corpse streams back in. On the peer the creature dies on this runs inside
        // Instantiate, before Setup and ModifyRagdoll have written anything, so it does nothing there.
        [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Awake))]
        public static class ApplyStoredRagdollLook {
            public static void Postfix(Ragdoll __instance) {
                ZDO zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
                if (zdo == null) { return; }

                Vector3 size = zdo.GetVec3(SLS_SIZE, Vector3.zero);
                if (size != Vector3.zero) { __instance.transform.localScale = size; }

                int stored = zdo.GetInt(SLS_RAGDOLL_COLOR, 0);
                if (stored <= 0) { return; }
                ColorDef colorization = new ColorDef(zdo.GetFloat(ZDOVars.s_hue), zdo.GetFloat(ZDOVars.s_saturation), zdo.GetFloat(ZDOVars.s_value), stored == 2);
                Colorization.ApplyColorizationWithoutLevelEffects(__instance.gameObject, colorization);
            }
        }
    }
}
