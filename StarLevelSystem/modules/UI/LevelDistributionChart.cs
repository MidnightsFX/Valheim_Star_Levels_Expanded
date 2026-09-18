using Jotunn.Managers;
using StarLevelSystem.common;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StarLevelSystem.modules.UI {
    // A bar chart of spawn chance per star level, built out of plain uGUI Images for the quick configure panel.
    //
    // Lives here rather than in common/ConfigUI, which has to stay textually identical to the template copy. Bars and
    // labels are pooled and re-laid out on every SetData, because the page redraws it on every slider tick.
    internal class LevelDistributionChart {
        private const float LabelH = 18f;
        private const float TitleH = 16f;
        private const float PadX = 6f;
        private const int MaxValueLabels = 16;   // above this, bars are too narrow to carry a percentage each
        private const int MaxAxisLabels = 12;

        private static readonly Color BarColor = new Color(0.86f, 0.62f, 0.26f, 0.95f);
        private static readonly Color PeakColor = new Color(1f, 0.82f, 0.35f, 1f);

        private readonly GameObject root;
        private readonly RectTransform rootRect;
        private readonly RectTransform plot;
        private readonly RectTransform axisRect;
        private readonly Transform labelRoot;
        private readonly Text emptyText;
        private readonly Text titleText;
        // Not readonly: the page splits one full-height chart into two half-height ones when bosses get their own curve.
        private float plotW;
        private float plotH;
        // Where the plot starts inside the chart, which is also where a value label sitting on top of a full-height bar
        // ends. A title takes a strip of its own above that, so the two cannot collide.
        private float plotTop;
        private float rectX, rectY, rectW, rectH;
        private readonly List<Image> bars = new List<Image>();
        private readonly List<Text> valueLabels = new List<Text>();
        private readonly List<Text> axisLabels = new List<Text>();

        internal LevelDistributionChart(Transform parent, float x, float y, float w, float h) {
            root = ConfigUI.NewRect("LevelDistributionChart", parent, x, y, w, h);
            rootRect = (RectTransform)root.transform;
            Image background = root.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.35f);
            background.raycastTarget = false;
            labelRoot = root.transform;

            // Value labels sit above the plot and star numbers below it.
            GameObject plotGO = ConfigUI.NewRect("Plot", root.transform, PadX, LabelH + 4f, w, h);
            plot = (RectTransform)plotGO.transform;

            // Baseline under the bars.
            GameObject axis = ConfigUI.NewUI("Axis", plot, typeof(Image));
            axisRect = (RectTransform)axis.transform;
            axisRect.anchorMin = axisRect.anchorMax = axisRect.pivot = Vector2.zero;
            axisRect.anchoredPosition = new Vector2(0f, -1f);
            Image axisImg = axis.GetComponent<Image>();
            axisImg.color = new Color(0.6f, 0.5f, 0.35f, 0.8f);
            axisImg.raycastTarget = false;

            emptyText = ConfigUI.AddText(root.transform, 0f, 0f, w, h, "", 14, TextAnchor.MiddleCenter);
            emptyText.raycastTarget = false;
            titleText = ConfigUI.AddText(root.transform, PadX + 2f, 1f, 160f, LabelH, "", 12, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimYellow);
            titleText.raycastTarget = false;

            SetRect(x, y, w, h);
        }

        // Moves and resizes the whole chart. The caller redraws with SetData afterwards, since bar and label positions
        // are worked out from the plot size there.
        internal void SetRect(float x, float y, float w, float h) {
            rectX = x;
            rectY = y;
            rectW = w;
            rectH = h;
            ApplyRect();
        }

        private void ApplyRect() {
            float titleStrip = string.IsNullOrEmpty(titleText.text) ? 0f : TitleH;
            rootRect.sizeDelta = new Vector2(rectW, rectH);
            rootRect.anchoredPosition = new Vector2(rectX, -rectY);
            plotTop = titleStrip + LabelH + 4f;
            plotW = rectW - 2 * PadX;
            plotH = rectH - plotTop - LabelH - 4f;
            plot.sizeDelta = new Vector2(plotW, plotH);
            plot.anchoredPosition = new Vector2(PadX, -plotTop);
            axisRect.sizeDelta = new Vector2(plotW, 1f);
            emptyText.rectTransform.sizeDelta = new Vector2(rectW, rectH);
        }

        internal void SetVisible(bool visible) {
            root.SetActive(visible);
        }

        // A short label in a strip of its own above the bars, for telling two charts apart. Empty gives the strip back.
        internal void SetTitle(string text) {
            titleText.text = text ?? "";
            ApplyRect();
        }

        // chances: star level -> chance (0-1), in ascending star order.
        internal void SetData(IList<KeyValuePair<int, float>> chances) {
            int n = chances?.Count ?? 0;
            emptyText.text = n == 0 ? "No level data" : "";

            float peak = 0f;
            for (int i = 0; i < n; i++) { peak = Mathf.Max(peak, chances[i].Value); }

            float slot = n > 0 ? plotW / n : plotW;
            float gap = slot >= 8f ? 2f : (slot >= 4f ? 1f : 0f);
            bool showValues = n <= MaxValueLabels;
            int axisStep = Mathf.Max(1, Mathf.CeilToInt(n / (float)MaxAxisLabels));

            int axisUsed = 0;
            int lastLabeled = -1;
            for (int i = 0; i < n; i++) {
                float chance = chances[i].Value;
                float barH = peak > 0f ? plotH * chance / peak : 0f;
                if (chance > 0f) { barH = Mathf.Max(barH, 1f); }

                Image bar = BarAt(i);
                RectTransform rt = bar.rectTransform;
                rt.sizeDelta = new Vector2(Mathf.Max(1f, slot - gap), barH);
                rt.anchoredPosition = new Vector2(i * slot + gap * 0.5f, 0f);
                bar.color = chance >= peak && peak > 0f ? PeakColor : BarColor;
                bar.gameObject.SetActive(true);

                float centerX = PadX + i * slot + slot * 0.5f;
                if (showValues) {
                    Text value = LabelAt(valueLabels, i, 11);
                    value.text = FormatPercent(chance);
                    // Just above the bar's top, measured from the chart root's top-left.
                    PlaceLabel(value, centerX, plotTop + (plotH - barH) - LabelH);
                }
                bool stepped = i % axisStep == 0;
                if (stepped || i == n - 1) {
                    // The top star is always labelled. When it falls between steps and would touch the previous
                    // label, it takes that label over instead.
                    if (stepped == false && lastLabeled >= 0 && (i - lastLabeled) * slot < 28f) { axisUsed--; }
                    Text axisLabel = LabelAt(axisLabels, axisUsed, 11);
                    axisLabel.text = chances[i].Key.ToString();
                    PlaceLabel(axisLabel, centerX, plotTop + plotH + 3f);
                    axisUsed++;
                    lastLabeled = i;
                }
            }

            for (int i = n; i < bars.Count; i++) { bars[i].gameObject.SetActive(false); }
            for (int i = showValues ? n : 0; i < valueLabels.Count; i++) { valueLabels[i].gameObject.SetActive(false); }
            for (int i = axisUsed; i < axisLabels.Count; i++) { axisLabels[i].gameObject.SetActive(false); }
        }

        private static string FormatPercent(float chance) {
            float pct = chance * 100f;
            if (pct <= 0f) { return "0%"; }
            if (pct < 0.1f) { return "<0.1%"; }
            return pct < 10f ? pct.ToString("0.#") + "%" : pct.ToString("0") + "%";
        }

        private Image BarAt(int index) {
            while (bars.Count <= index) {
                GameObject go = ConfigUI.NewUI("Bar", plot, typeof(Image));
                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
                Image img = go.GetComponent<Image>();
                img.raycastTarget = false;
                bars.Add(img);
            }
            return bars[index];
        }

        private Text LabelAt(List<Text> pool, int index, int fontSize) {
            while (pool.Count <= index) {
                Text label = ConfigUI.AddText(labelRoot, 0f, 0f, 44f, LabelH, "", fontSize, TextAnchor.MiddleCenter, GUIManager.Instance.ValheimBeige);
                label.raycastTarget = false;
                pool.Add(label);
            }
            Text found = pool[index];
            found.gameObject.SetActive(true);
            return found;
        }

        private static void PlaceLabel(Text label, float centerX, float top) {
            RectTransform rt = label.rectTransform;
            rt.anchoredPosition = new Vector2(centerX - rt.sizeDelta.x * 0.5f, -top);
        }
    }
}
