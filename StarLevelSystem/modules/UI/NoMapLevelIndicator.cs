using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StarLevelSystem.modules.UI {
    // The same ring/zone readout MinimapLevelIndicator pins to the minimap, for worlds (or characters)
    // playing without one. With no minimap rectangle to anchor to it sits in the top right corner of the
    // hud, in the space the minimap would have taken. Each line has its own client toggle and is still
    // hidden when its scaling system is disabled; nothing is shown at all while a map is available, so the
    // two readouts never appear at once.
    internal static class NoMapLevelIndicator {

        private static Text indicatorText;
        private static float nextRefresh = 0f;
        private const float RefreshInterval = 5f;
        // Distance from the screen's top right corner, in hud units.
        private const float CornerMargin = 24f;

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        public static class CreateIndicator {
            public static void Postfix(Hud __instance) {
                if (__instance == null || __instance.m_rootObject == null) { return; }
                // Unity reports a destroyed object as null here, so leaving a world and joining another
                // rebuilds the readout against the new hud instead of holding on to the dead one.
                if (indicatorText != null) { return; }
                try {
                    // The hud root is the full-screen container SetVisible slides offscreen, so anchoring
                    // to its top right corner also inherits hud hiding for free.
                    GameObject textObj = GUIManager.Instance.CreateText(
                        text: "",
                        parent: __instance.m_rootObject.transform,
                        anchorMin: new Vector2(1f, 1f),
                        anchorMax: new Vector2(1f, 1f),
                        position: new Vector2(-CornerMargin, -CornerMargin),
                        font: GUIManager.Instance.AveriaSerifBold,
                        fontSize: 14,
                        color: GUIManager.Instance.ValheimYellow,
                        outline: true,
                        outlineColor: Color.black,
                        width: 200f,
                        height: 44f,
                        addContentSizeFitter: false);
                    indicatorText = textObj.GetComponent<Text>();
                    // Pin the text box's top-right corner to the hud's top-right corner.
                    indicatorText.rectTransform.pivot = new Vector2(1f, 1f);
                    indicatorText.rectTransform.anchoredPosition = new Vector2(-CornerMargin, -CornerMargin);
                    indicatorText.alignment = TextAnchor.UpperRight;
                    indicatorText.gameObject.SetActive(false);
                } catch (Exception e) {
                    Logger.LogWarning($"Failed to create no-map level indicator: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
        public static class RefreshIndicator {
            public static void Postfix() {
                if (indicatorText == null) { return; }
                if (Time.time < nextRefresh) { return; }
                nextRefresh = Time.time + RefreshInterval;
                Refresh();
            }
        }

        private static void Refresh() {
            Player player = Player.m_localPlayer;
            // Game.m_noMap covers both the world's NoMap global key and the per-character map toggle, which
            // together are exactly the cases where the minimap-anchored readout has nowhere to draw.
            if (player == null || Game.m_noMap == false) {
                Hide();
                return;
            }

            Vector3 pos = player.transform.position;
            List<string> lines = new List<string>(2);
            if (ValConfig.ShowNoMapRingLevel.Value && ValConfig.EnableDistanceLevelScalingBonus.Value) {
                lines.Add(MinimapLevelIndicator.RingLine(pos));
            }
            if (ValConfig.ShowNoMapZoneLevel.Value && ValConfig.EnableZoneScalingBonus.Value) {
                lines.Add(MinimapLevelIndicator.ZoneLine(pos));
            }

            if (lines.Count == 0) {
                Hide();
                return;
            }

            indicatorText.text = string.Join("\n", lines);
            if (!indicatorText.gameObject.activeSelf) { indicatorText.gameObject.SetActive(true); }
        }

        private static void Hide() {
            if (indicatorText.gameObject.activeSelf) { indicatorText.gameObject.SetActive(false); }
        }

        // SettingChanged handler for both client toggles: hide immediately once the last line is turned
        // off, otherwise let the next refresh repopulate it.
        public static void OnShowIndicatorChanged(object s, EventArgs e) {
            if (indicatorText == null) { return; }
            if (!ValConfig.ShowNoMapRingLevel.Value && !ValConfig.ShowNoMapZoneLevel.Value) {
                indicatorText.gameObject.SetActive(false);
            } else {
                nextRefresh = 0f;
            }
        }
    }
}
