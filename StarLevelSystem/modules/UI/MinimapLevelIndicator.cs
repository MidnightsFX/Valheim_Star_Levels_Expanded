using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    // Small client-side readout next to the minimap showing the current distance-ring band and the
    // current zone level. Each line is hidden when its scaling system is disabled; the whole element
    // is gated by the client-only ShowMinimapLevelIndicator toggle.
    internal static class MinimapLevelIndicator {

        private static Text indicatorText;
        private static float nextRefresh = 0f;
        private const float RefreshInterval = 5f;

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Awake))]
        public static class CreateIndicator {
            public static void Postfix(Minimap __instance) {
                if (__instance == null || __instance.m_mapImageSmall == null) { return; }
                if (indicatorText != null) { return; }
                try {
                    // Parent to the small map image so the indicator anchors to the minimap rectangle.
                    Transform parent = __instance.m_mapImageSmall.transform;
                    GameObject textObj = GUIManager.Instance.CreateText(
                        text: "",
                        parent: parent,
                        anchorMin: new Vector2(1f, 0f),
                        anchorMax: new Vector2(1f, 0f),
                        position: new Vector2(-8f, 8f),
                        font: GUIManager.Instance.AveriaSerifBold,
                        fontSize: 14,
                        color: GUIManager.Instance.ValheimYellow,
                        outline: true,
                        outlineColor: Color.black,
                        width: 200f,
                        height: 44f,
                        addContentSizeFitter: false);
                    indicatorText = textObj.GetComponent<Text>();
                    // Pin the text box's bottom-right corner to the minimap's bottom-right corner.
                    indicatorText.rectTransform.pivot = new Vector2(1f, 0f);
                    indicatorText.rectTransform.anchoredPosition = new Vector2(-8f, 8f);
                    indicatorText.alignment = TextAnchor.LowerRight;
                    indicatorText.gameObject.SetActive(false);
                } catch (Exception e) {
                    Logger.LogWarning($"Failed to create minimap level indicator: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
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
            if (!ValConfig.ShowMinimapLevelIndicator.Value || player == null) {
                if (indicatorText.gameObject.activeSelf) { indicatorText.gameObject.SetActive(false); }
                return;
            }

            Vector3 pos = player.transform.position;
            List<string> lines = new List<string>(2);
            if (ValConfig.EnableDistanceLevelScalingBonus.Value) {
                lines.Add($"Ring {DistanceScaleSystem.GetCurrentRingLevel(pos)}");
            }
            if (ValConfig.EnableZoneScalingBonus.Value) {
                ZoneData zone = ZoneScaleSystemData.GetZoneForPosition(pos);
                lines.Add($"Zone {(zone != null ? zone.ZoneLevel : 0)}");
            }

            if (lines.Count == 0) {
                if (indicatorText.gameObject.activeSelf) { indicatorText.gameObject.SetActive(false); }
                return;
            }

            indicatorText.text = string.Join("\n", lines);
            if (!indicatorText.gameObject.activeSelf) { indicatorText.gameObject.SetActive(true); }
        }

        // SettingChanged handler for the client toggle: hide immediately when disabled; the refresh
        // loop repopulates it when re-enabled.
        public static void OnShowIndicatorChanged(object s, EventArgs e) {
            if (indicatorText == null) { return; }
            if (!ValConfig.ShowMinimapLevelIndicator.Value) {
                indicatorText.gameObject.SetActive(false);
            } else {
                nextRefresh = 0f;
            }
        }
    }
}
