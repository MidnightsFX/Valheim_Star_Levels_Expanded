using Jotunn.Managers;
using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.modules.AnimationAndSpeed {

    // The Ulv is rigged as a Mecanim "generic" avatar, but its animator's "In Water" blend tree was built from the
    // shared humanoid set: it plays the Player's "Swimming" and "Treading Water", which hold nothing but humanoid
    // muscle curves. A generic avatar has nothing to bind those to, so a swimming Ulv drops into its bind pose and
    // glides across the water. Vanilla never notices because its Ulvs sleep in frost caves; raids bring them to shore.
    //
    // Fixed once on the prefab, which every instance then copies, rather than per instance: swapping an instance's
    // controller resets its animator parameters, and MonsterAI.Awake has already written "sleeping" into them.
    internal static class SwimAnimationModifications {

        // Creature prefab -> one of its own clips to play wherever its controller asks for a humanoid clip. For the
        // Ulv those are only the two swim clips, and its walk cycle reads as paddling.
        private static readonly Dictionary<string, string> StandInClips = new Dictionary<string, string>() {
            { "Ulv", "Walk" },
        };

        // Hooked to OnVanillaPrefabsAvailable, which fires on every main menu load, so this must be idempotent: a
        // prefab that already carries the fix has no humanoid clips left to replace and is left alone.
        internal static void ApplyToPrefabs() {
            foreach (KeyValuePair<string, string> entry in StandInClips) {
                GameObject prefab = PrefabManager.Instance.GetPrefab(entry.Key);
                if (prefab == null) { continue; }
                ReplaceHumanoidClips(prefab, entry.Value);
            }
        }

        private static void ReplaceHumanoidClips(GameObject prefab, string standInName) {
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null) { return; }
            // A humanoid avatar retargets these clips; only a generic one leaves them dead.
            if (animator.avatar != null && animator.avatar.isHuman) { return; }

            // Build on the base controller rather than nesting override controllers, and carry over any overrides
            // already in place (another mod's, or this fix from an earlier menu load).
            AnimatorOverrideController existing = animator.runtimeAnimatorController as AnimatorOverrideController;
            RuntimeAnimatorController baseController = existing != null ? existing.runtimeAnimatorController : animator.runtimeAnimatorController;
            if (baseController == null) { return; }

            AnimatorOverrideController fixedController = new AnimatorOverrideController(baseController) { name = $"{baseController.name}_SLS" };
            List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(fixedController.overridesCount);
            (existing != null ? existing : fixedController).GetOverrides(overrides);

            AnimationClip standIn = null;
            foreach (KeyValuePair<AnimationClip, AnimationClip> pair in overrides) {
                AnimationClip playing = pair.Value != null ? pair.Value : pair.Key;
                if (pair.Key != null && pair.Key.name == standInName && playing != null && playing.humanMotion == false) {
                    standIn = playing;
                    break;
                }
            }
            if (standIn == null) {
                Logger.LogWarning($"{prefab.name} has no '{standInName}' clip to stand in for its humanoid animations; leaving its animator as it is.");
                return;
            }

            int replaced = 0;
            for (int i = 0; i < overrides.Count; i++) {
                AnimationClip playing = overrides[i].Value != null ? overrides[i].Value : overrides[i].Key;
                if (playing == null || playing.humanMotion == false) { continue; }
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, standIn);
                replaced++;
            }
            if (replaced == 0) { return; }

            fixedController.ApplyOverrides(overrides);
            animator.runtimeAnimatorController = fixedController;
            Logger.LogDebug($"{prefab.name}: {replaced} humanoid clip(s) its generic rig cannot play now use '{standInName}'.");
        }
    }
}
