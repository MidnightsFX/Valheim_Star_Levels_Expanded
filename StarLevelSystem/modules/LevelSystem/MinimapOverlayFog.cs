using HarmonyLib;
using StarLevelSystem.common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarLevelSystem.modules.LevelSystem {
    // Jotunn's built-in "below fog" overlay masking (MapOverlay.IgnoreFog=false) does not work in this
    // game/Jotunn build: even a freshly created below-fog overlay renders across the whole map. Only
    // IgnoreFog=true (draw everywhere) is reliable. So SLS always creates its overlays above-fog and
    // masks the content itself -- in "below fog" mode the ring/zone builders only write pixels for
    // explored map locations (see IsPixelExplored). Exploration reveals new terrain over time, so a
    // Harmony hook on Minimap.Explore flags new exploration and MaybeRefreshForExploration throttle-
    // redraws the below-fog overlays.
    internal static class MinimapOverlayFog {
        // Set true whenever the player uncovers a new fog pixel; consumed (throttled) to redraw the
        // self-masked below-fog overlays so newly explored areas gain their outlines.
        internal static bool ExplorationChanged = false;

        // Set from ZNet.Shutdown/ShutdownWithoutSave (leaving a world) and cleared on ZNet.Awake
        // (entering one). The instance/scene checks below cannot detect logout on their own: vanilla
        // ZNet.Shutdown only calls StopAll, so ZNet.instance and Minimap.instance both stay alive and
        // the active scene is still "main" until the scene actually unloads a few frames later. This
        // flag is what distinguishes "in a world" from "leaving one".
        internal static bool WorldUnloading = false;

        private static float nextFogRefresh = 0f;
        private const float FogRefreshInterval = 10f;

        // Readiness gate shared by the ring and zone overlay systems. Returns true only when we're in
        // a fully loaded world with a live minimap. Overlay (re)draws are gated on this so config
        // changes (fog toggle, colors, ring center) that fire while the player is in the main menu or
        // on a loading screen don't run against a world/minimap that doesn't exist yet. Once the map is
        // ready the overlays are (re)built from MinimapManager.OnVanillaMapDataLoaded.
        //
        // WorldUnloading matters most on logout: Jotunn's SynchronizationManager restores every synced
        // config entry to its cached local value from a ZNet.OnDestroy prefix, which raises
        // SettingChanged on our overlay settings while the world is being torn down. Without the flag
        // those events start an overlay rebuild on the DontDestroyOnLoad TaskRunner, which then keeps
        // running into the main menu and dereferences a destroyed Minimap.
        internal static bool CanDrawOverlays() {
            return !WorldUnloading
                && ZNet.instance != null
                && Minimap.instance != null
                && SceneManager.GetActiveScene().name == "main";
        }

        // Mirrors vanilla Minimap.IsExplored but takes overlay pixel coords directly (the ring/zone
        // builders already work in this grid). SLS overlay drawing assumes the vanilla map texture and
        // Jotunn's overlay texture are the same size (m_textureSize == overlay.TextureSize == 2048), so
        // a pixel index maps straight into the exploration arrays. Returns true when there is no live
        // map yet so a premature draw doesn't hide everything.
        internal static bool IsPixelExplored(int px, int py) {
            Minimap mm = Minimap.instance;
            if (mm == null || mm.m_explored == null) { return true; }
            int size = mm.m_textureSize;
            if (px < 0 || py < 0 || px >= size || py >= size) { return false; }
            int idx = py * size + px;
            return mm.m_explored[idx] || mm.m_exploredOthers[idx];
        }

        // Called from the Minimap.Update patch. When the player has uncovered new terrain and a
        // below-fog overlay is active, throttle-redraw those overlays so the new area gains its
        // outlines. Above-fog overlays already draw everywhere, so they never need this. Standing still
        // uncovers nothing (ExplorationChanged stays false) and therefore costs nothing.
        internal static void MaybeRefreshForExploration() {
            if (!ExplorationChanged) { return; }
            if (Time.unscaledTime < nextFogRefresh) { return; }
            nextFogRefresh = Time.unscaledTime + FogRefreshInterval;
            if (!CanDrawOverlays()) { return; }

            bool ringsBelowFog = ValConfig.EnableMapRingsForDistanceBonus.Value && !ValConfig.MapRingsAboveFog.Value;
            bool zonesBelowFog = ValConfig.EnableZoneScalingBonus.Value && ValConfig.EnableZoneMapOverlay.Value && !ValConfig.ZoneOverlayAboveFog.Value;
            if (!ringsBelowFog && !zonesBelowFog) { return; }

            ExplorationChanged = false;
            if (ringsBelowFog) { DistanceScaleSystem.DelayedMinimapSetup(); }
            if (zonesBelowFog) { ZoneScaleSystem.DrawMinimapOverlay(); }
        }

        // Vanilla Minimap.Explore(int,int) returns true only when it reveals a previously-fogged pixel,
        // so this fires exactly on new exploration -- not while standing in already-explored terrain.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Explore), typeof(int), typeof(int))]
        internal static class Minimap_Explore_Patch {
            private static void Postfix(bool __result) {
                if (__result) { ExplorationChanged = true; }
            }
        }
    }
}
