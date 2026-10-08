using HarmonyLib;
using StarLevelSystem.common;

namespace StarLevelSystem.modules.Sizes {
    // Hooks for MountScaling, which holds the logic. Gated on EnableRidableCreatureSizeFixes.
    internal static class MountScalingPatches {

        // Taming only happens on the owner, so this refits the tame there; other peers refit when the saddle goes on.
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_SetTamed))]
        public static class RefitMountOnTame {
            private static void Postfix(Character __instance, bool tamed) {
                if (tamed == false || __instance.m_tamed == false) { return; }
                SizeModifications.UpdateRidingCreaturesForSizeScaling(__instance.gameObject);
            }
        }

        // Every peer runs SetSaddle: from Tameable.Awake and from the SetSaddle RPC sent to everybody.
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.SetSaddle))]
        public static class RefitMountOnSaddle {
            private static void Postfix(Tameable __instance, bool enabled) {
                if (enabled == false) { return; }
                // From Tameable.Awake the Character may not have run its own Awake yet (component order is not fixed),
                // and IsTamed would throw on its missing ZNetView. The setup pass that sizes the creature fits it anyway.
                Character character = __instance.m_character;
                if (character == null || character.m_nview == null || character.m_nview.IsValid() == false) { return; }
                SizeModifications.UpdateRidingCreaturesForSizeScaling(__instance.gameObject);
            }
        }

        // Vanilla returns 45 only for a ridden creature (38 for players, 90 for the rest), and this runs on every
        // physics step, so everything else leaves on the first compare.
        [HarmonyPatch(typeof(Character), nameof(Character.GetSlideAngle))]
        public static class RiddenMountSlideAngle {
            private static void Postfix(Character __instance, ref float __result) {
                if (__result != 45f || ValConfig.EnableRidableCreatureSizeFixes.Value == false) { return; }
                __result = MountScaling.RiddenSlideAngle(__instance, __result);
            }
        }

        // The prefix sees the attach point, which AttachStop clears.
        [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
        public static class DismountBesideRaisedMount {
            private static void Prefix(Player __instance, out Sadle __state) {
                __state = MountScaling.RaisedSaddleBeingLeft(__instance);
            }

            private static void Postfix(Player __instance, Sadle __state) {
                if (__state == null) { return; }
                MountScaling.PlaceBesideMount(__instance, __state);
            }
        }

        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
        public static class RaisedMountCameraDistance {
            private static void Prefix(GameCamera __instance) {
                MountScaling.UpdateCameraDistance(__instance);
            }
        }
    }
}
