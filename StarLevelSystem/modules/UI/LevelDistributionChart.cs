using StarLevelSystem.common;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.modules.UI.QuickConfigPrefabs;

namespace StarLevelSystem.modules.UI {
    // A bar chart of spawn chance per star level, built out of plain uGUI Images for the quick configure panel. Its look is
    // the prefab SLS_QC_Chart's; the code keeps what follows from the data and from the rect the page gives it.
    //
    // Lives here rather than in common/ConfigUI, which has to stay textually identical to the template copy. Bars and
    // labels are pooled, cloned from the prefab's hidden templates, and re-laid out on every SetData, because the page
    // redraws it on every slider tick.
    internal class LevelDistributionChart {
        private const float LabelH = 18f;
        private const float TitleH = 16f;
        private const float PadX = 6f;
        private const int MaxValueLabels = 16;   // above this, bars are too narrow to carry a percentage each
        private const int MaxAxisLabels = 12;
        private const float MinSlotForDots = 8f;
        private const float LegendGap = 10f;

        // The tallest bar. The others keep the colour their template has in the prefab.
        private static readonly Color PeakColor = new Color(1f, 0.82f, 0.35f, 1f);
        // Every bar, on a chart comparing them to other series.
        internal static readonly Color BaseBarColor = new Color(1f, 0.82f, 0.2f, 0.9f);

        private readonly GameObject root;
        private readonly RectTransform rootRect;
        private readonly RectTransform plot;
        private readonly RectTransform axisRect;
        private readonly Transform labelRoot;
        private readonly Text emptyText;
        private readonly Text titleText;
        private readonly Image barTemplate;
        private readonly Text labelTemplate;
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
        private readonly List<Text> legendLabels = new List<Text>();
        private readonly List<ChartLineGraphic> chartLines = new List<ChartLineGraphic>();

        internal LevelDistributionChart(Transform parent, float x, float y, float w, float h) {
            root = Spawn("SLS_QC_Chart", parent);
            rootRect = (RectTransform)root.transform;
            labelRoot = root.transform;
            plot = (RectTransform)Find(root.transform, "Plot").transform;
            axisRect = (RectTransform)Find(plot, "Axis").transform;
            barTemplate = Get<Image>(plot, "BarTemplate");
            labelTemplate = Get<Text>(root.transform, "LabelTemplate");
            emptyText = Get<Text>(root.transform, "EmptyText");
            titleText = Get<Text>(root.transform, "ChartTitle");

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
            Draw(chances, null, PeakOf(chances), null);
        }

        // The same bars, all in the base colour, with a line over them for each series. A series can reach stars the bars don't
        // (a distance ring can add levels to a curve), so all of them are laid out on every star any of them has, on one scale.
        internal void SetData(IList<KeyValuePair<int, float>> chances, IList<LevelChartLine> lines) {
            if (lines == null || lines.Count == 0) {
                Draw(chances, null, PeakOf(chances), BaseBarColor);
                return;
            }
            SortedSet<int> stars = new SortedSet<int>();
            if (chances != null) { foreach (KeyValuePair<int, float> entry in chances) { stars.Add(entry.Key); } }
            foreach (LevelChartLine line in lines) {
                foreach (KeyValuePair<int, float> entry in line.Chances) { stars.Add(entry.Key); }
            }

            float peak = Mathf.Max(PeakOf(chances), lines.Max(line => PeakOf(line.Chances)));
            Draw(OnStars(chances, stars), lines, peak, BaseBarColor);

            float slot = stars.Count > 0 ? plotW / stars.Count : plotW;
            for (int i = 0; i < lines.Count; i++) {
                List<Vector2> points = new List<Vector2>(stars.Count);
                IList<KeyValuePair<int, float>> aligned = OnStars(lines[i].Chances, stars);
                for (int s = 0; s < aligned.Count; s++) {
                    points.Add(new Vector2((s + 0.5f) * slot, peak > 0f ? plotH * aligned[s].Value / peak : 0f));
                }
                LineAt(i).Show(points, lines[i].Color, plotW, plotH, slot >= MinSlotForDots);
            }
            for (int i = lines.Count; i < chartLines.Count; i++) { chartLines[i].gameObject.SetActive(false); }
        }

        // Words in the title strip, each in its series' colour, running back from the right edge. Null clears it.
        internal void SetLegend(IList<KeyValuePair<string, Color>> entries) {
            int count = entries?.Count ?? 0;
            float right = rectW - PadX;
            for (int i = count - 1; i >= 0; i--) {
                Text label = LabelAt(legendLabels, i);
                label.text = entries[i].Key;
                label.color = entries[i].Value;
                label.alignment = TextAnchor.UpperRight;
                float width = label.preferredWidth + LegendGap;
                label.rectTransform.sizeDelta = new Vector2(width, TitleH);
                label.rectTransform.anchoredPosition = new Vector2(right - width, -2f);
                right -= width;
            }
            for (int i = count; i < legendLabels.Count; i++) { legendLabels[i].gameObject.SetActive(false); }
        }

        private static float PeakOf(IList<KeyValuePair<int, float>> chances) {
            float peak = 0f;
            if (chances != null) {
                for (int i = 0; i < chances.Count; i++) { peak = Mathf.Max(peak, chances[i].Value); }
            }
            return peak;
        }

        private static IList<KeyValuePair<int, float>> OnStars(IList<KeyValuePair<int, float>> chances, SortedSet<int> stars) {
            Dictionary<int, float> byStar = new Dictionary<int, float>();
            if (chances != null) { foreach (KeyValuePair<int, float> entry in chances) { byStar[entry.Key] = entry.Value; } }
            List<KeyValuePair<int, float>> aligned = new List<KeyValuePair<int, float>>(stars.Count);
            foreach (int star in stars) {
                byStar.TryGetValue(star, out float chance);
                aligned.Add(new KeyValuePair<int, float>(star, chance));
            }
            return aligned;
        }

        // barColor null is the plain chart: the prefab's colour, with the tallest bar picked out.
        private void Draw(IList<KeyValuePair<int, float>> chances, IList<LevelChartLine> lines, float peak, Color? barColor) {
            if (lines == null) {
                foreach (ChartLineGraphic line in chartLines) { line.gameObject.SetActive(false); }
            }
            int n = chances?.Count ?? 0;
            emptyText.text = n == 0 ? ConfigUI.L("$sls_cfg_chart_no_data") : "";

            float slot = n > 0 ? plotW / n : plotW;
            float gap = slot >= 8f ? 2f : (slot >= 4f ? 1f : 0f);
            // Over the lines they would only get in the way, and the page says what the lines come to in words.
            bool showValues = n <= MaxValueLabels && lines == null;
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
                bar.color = barColor ?? (chance >= peak && peak > 0f ? PeakColor : barTemplate.color);
                bar.gameObject.SetActive(true);

                float centerX = PadX + i * slot + slot * 0.5f;
                if (showValues) {
                    Text value = LabelAt(valueLabels, i);
                    value.text = FormatPercent(chance);
                    // Just above the bar's top, measured from the chart root's top-left.
                    PlaceLabel(value, centerX, plotTop + (plotH - barH) - LabelH);
                }
                bool stepped = i % axisStep == 0;
                if (stepped || i == n - 1) {
                    // The top star is always labelled. When it falls between steps and would touch the previous
                    // label, it takes that label over instead.
                    if (stepped == false && lastLabeled >= 0 && (i - lastLabeled) * slot < 28f) { axisUsed--; }
                    Text axisLabel = LabelAt(axisLabels, axisUsed);
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
                bars.Add(UnityEngine.Object.Instantiate(barTemplate, plot, false));
            }
            return bars[index];
        }

        // Bars are pooled as they are needed and each new one lands on top, so a line goes back to the front every time.
        private ChartLineGraphic LineAt(int index) {
            while (chartLines.Count <= index) {
                GameObject holder = new GameObject("Line", typeof(RectTransform));
                holder.transform.SetParent(plot, false);
                chartLines.Add(holder.AddComponent<ChartLineGraphic>());
            }
            chartLines[index].transform.SetAsLastSibling();
            return chartLines[index];
        }

        private Text LabelAt(List<Text> pool, int index) {
            while (pool.Count <= index) {
                pool.Add(UnityEngine.Object.Instantiate(labelTemplate, labelRoot, false));
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

    // One secondary series over a chart's bars.
    internal struct LevelChartLine {
        internal Color Color;
        internal IList<KeyValuePair<int, float>> Chances;
    }

    // A polyline, with a dot on each point when there is room. Added from code rather than kept on the chart's prefab, since a
    // prefab can't reference a script in this mod's assembly from the asset bundle.
    internal class ChartLineGraphic : MaskableGraphic {
        private const float Thickness = 2f;
        private const float DotSize = 6f;

        private readonly List<Vector2> points = new List<Vector2>();
        private bool dots;

        // Points run from the plot's bottom-left corner, which is where this is laid over it.
        internal void Show(List<Vector2> newPoints, Color lineColor, float width, float height, bool withDots) {
            gameObject.SetActive(true);
            raycastTarget = false;
            RectTransform rt = rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
            points.Clear();
            points.AddRange(newPoints);
            dots = withDots;
            color = lineColor;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            for (int i = 0; i + 1 < points.Count; i++) {
                Vector2 along = points[i + 1] - points[i];
                if (along.sqrMagnitude <= 0f) { continue; }
                Vector2 side = new Vector2(-along.y, along.x).normalized * (Thickness * 0.5f);
                AddQuad(vh, points[i] - side, points[i] + side, points[i + 1] + side, points[i + 1] - side);
            }
            if (points.Count == 1 || dots) {
                Vector2 half = Vector2.one * (DotSize * 0.5f);
                foreach (Vector2 point in points) {
                    AddQuad(vh, point - half, new Vector2(point.x - half.x, point.y + half.y), point + half, new Vector2(point.x + half.x, point.y - half.y));
                }
            }
        }

        private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d) {
            int start = vh.currentVertCount;
            Color32 tint = color;
            vh.AddVert(a, tint, Vector2.zero);
            vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero);
            vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
