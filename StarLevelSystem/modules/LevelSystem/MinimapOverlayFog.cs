using Jotunn.Managers;
using StarLevelSystem.common;
using System;
using System.Reflection;
using UnityEngine.SceneManagement;

namespace StarLevelSystem.modules.LevelSystem {
    // Jotunn's MinimapManager.MapOverlay.IgnoreFog is internal and only honoured at overlay creation
    // time (GetMapOverlay's ignoreFog argument is ignored once an overlay with that name already
    // exists). To let the "above/below fog" config be flipped at runtime we reflectively sync the
    // existing overlay's flag with config, then let the normal rebuild re-apply the texture so the
    // compose pass (which reads IgnoreFog) picks up the change. Recreating the overlay instead would
    // leak duplicate toggles into Jotunn's overlay panel, so we mutate the flag in place.
    internal static class MinimapOverlayFog {
        private static readonly FieldInfo IgnoreFogField =
            typeof(MinimapManager.MapOverlay).GetField("IgnoreFog", BindingFlags.NonPublic | BindingFlags.Instance);

        // Readiness gate shared by the ring and zone overlay systems. Returns true only when we're in
        // a fully loaded world with a live minimap. Overlay (re)draws are gated on this so config
        // changes (fog toggle, colors, ring center) that fire while the player is in the main menu or
        // on a loading screen don't run against a world/minimap that doesn't exist yet. Once the map is
        // ready the overlays are (re)built from MinimapManager.OnVanillaMapDataLoaded.
        internal static bool CanDrawOverlays() {
            return ZNet.instance != null
                && Minimap.instance != null
                && SceneManager.GetActiveScene().name == "main";
        }

        // Force an existing overlay's IgnoreFog flag to match the desired value. The caller should
        // follow this with a texture Apply/redraw so the flag change is composed into the map.
        internal static void SetIgnoreFog(MinimapManager.MapOverlay overlay, bool ignoreFog) {
            if (overlay == null || IgnoreFogField == null) { return; }
            try {
                if ((bool)IgnoreFogField.GetValue(overlay) == ignoreFog) { return; }
                IgnoreFogField.SetValue(overlay, ignoreFog);
            } catch (Exception e) {
                Logger.LogWarning($"Failed to set minimap overlay fog flag: {e.Message}");
            }
        }
    }
}
