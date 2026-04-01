using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.Modifiers.Control {
    internal class Visible_Toggle {

        [HarmonyPatch(typeof(Character), nameof(Character.SetVisible))]
        static class Patch_Character_SetVisible {

            internal static readonly string SLSVISUALS = "SLS_Visuals(Clone)";

            static void Postfix(Character __instance, bool visible) {
                // Toggle SLS visuals when outside of the view range set by the client
                // This prevents visuals being rendered when the creature is hidden
                Transform visualHolder = __instance.transform.Find(SLSVISUALS);
                if (visualHolder != null) {
                    visualHolder.gameObject.SetActive(visible);
                }
            }
        }
    }
}
