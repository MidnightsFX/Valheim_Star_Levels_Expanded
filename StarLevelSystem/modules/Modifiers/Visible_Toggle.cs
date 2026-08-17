using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace StarLevelSystem.modules.Modifiers {
    internal class Visible_Toggle {

        internal static readonly string SLSVISUALS = "SLS_Visuals(Clone)";

        private class VisualsCacheEntry {
            public Transform Holder;
            public bool Searched;
            public bool HasLastApplied;
            public bool LastApplied;
        }

        // Keyed on the Character instance so entries die with the creature - no eviction pass needed.
        // Transform.Find is a string-compare walk of the children, and the SetVisible postfix below
        // runs for every character on every FixedUpdate, the vast majority of which have no SLS
        // visuals; resolving once and remembering the result removes that walk from the hot path.
        private static readonly ConditionalWeakTable<Character, VisualsCacheEntry> visualsCache = new ConditionalWeakTable<Character, VisualsCacheEntry>();

        // Called by SetupCreatureVFX when it attaches a visuals holder, since this cache may already
        // have recorded "no holder" for the creature from before the modifier visuals were built.
        internal static void InvalidateVisualsCache(Character chara) {
            if (chara != null) { visualsCache.Remove(chara); }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetVisible))]
        static class Patch_Character_SetVisible {

            static void Postfix(Character __instance, bool visible) {
                // Toggle SLS visuals when outside of the view range set by the client
                // This prevents visuals being rendered when the creature is hidden
                VisualsCacheEntry entry = visualsCache.GetOrCreateValue(__instance);
                if (entry.Searched == false) {
                    entry.Holder = __instance.transform.Find(SLSVISUALS);
                    entry.Searched = true;
                }
                // Unity's overloaded == also catches a holder that has been destroyed.
                if (entry.Holder == null) { return; }
                // SetActive on an unchanged value still costs a managed->native call; skip it.
                if (entry.HasLastApplied && entry.LastApplied == visible) { return; }
                entry.Holder.gameObject.SetActive(visible);
                entry.HasLastApplied = true;
                entry.LastApplied = visible;
            }
        }
    }
}
