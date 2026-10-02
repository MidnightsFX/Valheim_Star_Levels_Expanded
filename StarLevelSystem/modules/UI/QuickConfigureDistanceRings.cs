using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    // The Distance Rings page of the quick configure panel: the level system's rings (DistanceLevelBonus in
    // LevelSettings.yaml), the ones drawn on the map. Rings can be added, removed and moved, and each one's bonus edited.
    //
    // The right half works a ring through the creature curve from the Level Distribution page, beside the same curve with
    // no ring, so a bonus can be judged by the stars it produces. It adds the ring the way the roll does
    // (LevelSelection.ApplyLevelupBonus) rather than approximating it.
    //
    // The add/remove helpers at the bottom are shared with the Loot page's rings.
    internal static partial class QuickConfigureTool {

        // Beyond twice the vanilla world radius a ring is off the edge of even an enlarged world.
        private const int MinRingDistance = 1;
        private const int MaxRingDistance = 20000;
        // Where the first ring goes when there are none, and how far apart the next one is when there is only one.
        private const int DefaultLevelRingStep = 1000;
        // A ring's bonus is added to a chance in %, so past 100 either way it can only pin that chance.
        private const float MaxLevelRingBonus = 100f;

        private const float LevelRingPickX = 4f;
        private const float LevelRingDistanceX = 34f;
        private const float LevelRingDistanceW = 80f;
        private const float LevelRingBonusX = 122f;
        private const float LevelRingBonusW = 250f;

        private static Text levelRingWarningText;
        private static Text levelRingSummaryText;
        private static LevelDistributionChart levelRingCenterChart;
        private static LevelDistributionChart levelRingChart;
        // The scroll content the ring rows live in, rebuilt whenever a ring is added, removed or moves past another.
        private static Transform levelRingContent;
        private static float levelRingContentW;
        private static readonly List<GameObject> levelRingRows = new List<GameObject>();
        private static readonly List<LevelRingPicker> levelRingPickers = new List<LevelRingPicker>();
        // The ring shown on the right. Kept by reference, so it follows the ring when a new distance re-sorts the list.
        private static StagedLevelRing shownLevelRing;

        private class LevelRingPicker {
            internal StagedLevelRing Ring;
            internal Toggle Toggle;
        }

        private static void ClearDistanceRingReferences() {
            levelRingWarningText = null;
            levelRingSummaryText = null;
            levelRingCenterChart = null;
            levelRingChart = null;
            levelRingContent = null;
            levelRingContentW = 0f;
            levelRingRows.Clear();
            levelRingPickers.Clear();
            shownLevelRing = null;
        }

        // ------------------------------------------------------------------------------------------------
        //  Staged configuration
        // ------------------------------------------------------------------------------------------------

        // One ring: from Distance outwards (up to the next ring) a spawn's level-up chances get Bonus added, keyed by
        // level as the chance tables are, so level 1 is the roll from 0 stars to 1.
        private class StagedLevelRing {
            internal int Distance;
            internal SortedDictionary<int, float> Bonus;

            internal bool SameAs(StagedLevelRing other) {
                return other != null && Distance == other.Distance && ChanceTablesEqual(Bonus, other.Bonus);
            }
        }

        // Nearest first, the order the file keeps them in and the order a spawn is matched against them. A ring written
        // with no table at all comes through with an empty one: it still draws on the map, it just adds nothing.
        private static List<StagedLevelRing> LevelRingsOf(CreatureLevelSettings settings) {
            List<StagedLevelRing> rings = new List<StagedLevelRing>();
            if (settings?.DistanceLevelBonus == null) { return rings; }
            foreach (KeyValuePair<int, SortedDictionary<int, float>> ring in settings.DistanceLevelBonus) {
                rings.Add(new StagedLevelRing {
                    Distance = ring.Key,
                    Bonus = ring.Value != null ? new SortedDictionary<int, float>(ring.Value) : new SortedDictionary<int, float>(),
                });
            }
            return rings;
        }

        private static bool LevelRingsMatch(List<StagedLevelRing> a, List<StagedLevelRing> b) {
            if (a == null || b == null) { return a == b; }
            if (a.Count != b.Count) { return false; }
            for (int i = 0; i < a.Count; i++) {
                if (a[i].SameAs(b[i]) == false) { return false; }
            }
            return true;
        }

        private static SortedDictionary<int, SortedDictionary<int, float>> LevelRingsToYaml(List<StagedLevelRing> rings) {
            SortedDictionary<int, SortedDictionary<int, float>> yaml = new SortedDictionary<int, SortedDictionary<int, float>>();
            foreach (StagedLevelRing ring in rings) {
                yaml[ring.Distance] = new SortedDictionary<int, float>(ring.Bonus);
            }
            return yaml;
        }

        private static void ResetDistanceRingsPage() {
            staged.levelRings = LevelRingsOf(ShippedDefaults(YamlConfigManager.LevelSettings));
        }

        // ------------------------------------------------------------------------------------------------
        //  Page
        // ------------------------------------------------------------------------------------------------

        private static void BuildDistanceRingsPage(Transform parent) {
            const float IntroH = 44f;
            const float LeftW = 440f;
            const float RightX = 458f;
            const float CaptionH = 34f;
            const float SummaryH = 54f;
            float rightW = PageW - RightX;

            GameObject intro = ConfigUI.AddTextRow(parent, PageW, IntroH, "$sls_cfg_rings_intro", 13, GUIManager.Instance.ValheimBeige, TextAnchor.UpperCenter);
            ConfigUI.PositionRow(intro, 0f, 0f);
            levelRingWarningText = ConfigUI.AddText(parent, 0f, IntroH, PageW, 20f, "", 13, TextAnchor.MiddleCenter, GUIManager.Instance.ValheimOrange);
            float colY = IntroH + 24f;

            // Left - the rings.
            ConfigUI.CreateScroll(parent, 0f, colY, LeftW, PageH - colY, out Transform content, out float cw);
            levelRingContent = content;
            levelRingContentW = cw;
            BuildLevelRingRows();

            // Right - no ring, then the ring ticked on the left.
            GameObject caption = ConfigUI.AddTextRow(parent, rightW, CaptionH, "$sls_cfg_rings_caption", 12, GUIManager.Instance.ValheimBeige);
            ConfigUI.PositionRow(caption, RightX, colY);
            float chartTop = colY + CaptionH + 4f;
            float chartH = (PageH - chartTop - SummaryH - 14f) * 0.5f;
            levelRingCenterChart = new LevelDistributionChart(parent, RightX, chartTop, rightW, chartH);
            levelRingChart = new LevelDistributionChart(parent, RightX, chartTop + chartH + 8f, rightW, chartH);
            levelRingSummaryText = ConfigUI.AddText(parent, RightX, chartTop + 2 * chartH + 14f, rightW, SummaryH, "", 12, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);

            RefreshDistanceRings();
        }

        // The list: a help line, a header and a row per ring (or a note when there are none), and the Add button.
        private static void BuildLevelRingRows() {
            foreach (GameObject row in levelRingRows) { DiscardRow(row); }
            levelRingRows.Clear();
            levelRingPickers.Clear();
            if (levelRingContent == null || staged?.levelRings == null) { return; }

            Transform content = levelRingContent;
            float w = levelRingContentW;
            List<StagedLevelRing> rings = staged.levelRings;
            if (shownLevelRing == null || rings.Contains(shownLevelRing) == false) { shownLevelRing = rings.FirstOrDefault(); }

            levelRingRows.Add(ScrollRow(content, w, 50f, t => ConfigUI.AddTextRow(t, w, 50f, "$sls_cfg_rings_help", 12, GUIManager.Instance.ValheimBeige)));
            if (rings.Count == 0) {
                levelRingRows.Add(ScrollRow(content, w, 40f, t => ConfigUI.AddTextRow(t, w, 40f, "$sls_cfg_rings_none", 12, GUIManager.Instance.ValheimOrange)));
            } else {
                levelRingRows.Add(AddLevelRingHeaderRow(content, w));
                foreach (StagedLevelRing ring in rings) { levelRingRows.Add(AddLevelRingRow(content, w, ring)); }
            }
            levelRingRows.Add(AddRingButtonRow(content, w, AddLevelRing, Tip("Add ring",
                "Adds a ring past the furthest one, as far beyond it as the last two are apart, with the same bonus. Type over its distance to move it.")));
        }

        private static GameObject AddLevelRingHeaderRow(Transform content, float width) {
            GameObject row = ConfigUI.NewLayoutRow(content, width, 22f);
            Color color = GUIManager.Instance.ValheimYellow;
            ConfigUI.AddText(row.transform, 0f, 0f, LevelRingDistanceX, 22f, "Show", 11, TextAnchor.MiddleLeft, color);
            ConfigUI.AddText(row.transform, LevelRingDistanceX, 0f, LevelRingDistanceW, 22f, "From (m)", 11, TextAnchor.MiddleLeft, color);
            ConfigUI.AddText(row.transform, LevelRingBonusX, 0f, LevelRingBonusW, 22f, "Extra % per star, from 1 star up", 11, TextAnchor.MiddleLeft, color);
            return row;
        }

        private static GameObject AddLevelRingRow(Transform content, float width, StagedLevelRing ring) {
            GameObject row = ConfigUI.NewLayoutRow(content, width, 32f);

            Toggle pick = ConfigUI.AddToggle(row.transform, LevelRingPickX, 3f, 22f, ring == shownLevelRing, _ => ShowLevelRing(ring));
            WithTip(pick.gameObject, Tip("Show", "Shows this ring on the right, under the same curve with no ring at all."));
            levelRingPickers.Add(new LevelRingPicker { Ring = ring, Toggle = pick });

            InputField distance = null;
            distance = ConfigUI.AddTextField(row.transform, LevelRingDistanceX, 0f, LevelRingDistanceW, ring.Distance.ToString(CultureInfo.InvariantCulture),
                text => CommitLevelRingDistance(ring, distance, text), InputField.ContentType.IntegerNumber);
            WithTip(distance.gameObject, Tip("Distance",
                $"Metres from the world center (or the starter temple, see DistanceBonusIsFromStarterTemple) where this ring starts. A spawn gets the " +
                $"bonus of the furthest ring it is past, so each ring covers from here out to the next one. {MinRingDistance}-{MaxRingDistance}m, " +
                "and no two rings at the same distance."));

            InputField bonus = null;
            bonus = ConfigUI.AddTextField(row.transform, LevelRingBonusX, 0f, LevelRingBonusW, FormatRingBonus(ring.Bonus),
                text => CommitLevelRingBonus(ring, bonus, text), InputField.ContentType.Standard, "15, 5, 1");
            WithTip(bonus.gameObject, Tip("DistanceLevelBonus",
                "Percentage points added to each level-up chance past this ring: the first value to the chance of reaching 1 star, the next to " +
                "2 stars, and so on. A value past the end of the curve extends it. Where the file skips a star the ring is written as level:value " +
                "pairs, level 1 being the roll to 1 star; either form can be typed. Empty adds nothing, but the ring is still drawn on the map."));

            AddRingRemoveButton(row.transform, width, () => {
                staged.levelRings.Remove(ring);
                BuildLevelRingRows();
                RefreshDistanceRings();
            });
            return row;
        }

        private static void ShowLevelRing(StagedLevelRing ring) {
            shownLevelRing = ring;
            // Clicking the ticked box would untick it; it stays ticked, since something is always shown.
            foreach (LevelRingPicker picker in levelRingPickers) { picker.Toggle.SetIsOnWithoutNotify(picker.Ring == ring); }
            RefreshLevelRingCharts();
        }

        private static void CommitLevelRingDistance(StagedLevelRing ring, InputField field, string text) {
            if (staged?.levelRings == null) { return; }
            int distance = ReadRingDistance(text, ring.Distance, staged.levelRings.Where(r => r != ring).Select(r => r.Distance), "ring");
            field.SetTextWithoutNotify(distance.ToString(CultureInfo.InvariantCulture));
            if (distance == ring.Distance) { return; }
            ring.Distance = distance;
            // Only when it passed another ring: rebuilding under a box that was just clicked into would take the click.
            if (SortByDistance(staged.levelRings, r => r.Distance)) { BuildLevelRingRows(); }
            RefreshDistanceRings();
        }

        // An unreadable entry leaves the ring as it was and says why, rather than keeping text that is not what is staged.
        private static void CommitLevelRingBonus(StagedLevelRing ring, InputField field, string text) {
            SortedDictionary<int, float> parsed = ParseRingBonus(text, out string error);
            if (parsed == null) {
                string kept = ring.Bonus.Count > 0 ? FormatRingBonus(ring.Bonus) : "no bonus";
                SetStatus($"{error} The ring at {ring.Distance} m keeps {kept}.", false);
            } else {
                ring.Bonus = parsed;
            }
            field.SetTextWithoutNotify(FormatRingBonus(ring.Bonus));
            RefreshDistanceRings();
        }

        private static void AddLevelRing() {
            if (staged?.levelRings == null) { return; }
            List<StagedLevelRing> rings = staged.levelRings;
            if (NextRingDistance(rings.Select(r => r.Distance).ToList(), DefaultLevelRingStep, out int distance) == false) {
                SetStatus($"There is no room for another ring past {rings[rings.Count - 1].Distance} m.", false);
                return;
            }
            StagedLevelRing last = rings.LastOrDefault();
            StagedLevelRing added = new StagedLevelRing {
                Distance = distance,
                Bonus = last != null ? new SortedDictionary<int, float>(last.Bonus) : new SortedDictionary<int, float> { { 1, 10f }, { 2, 5f } },
            };
            rings.Add(added);
            shownLevelRing = added;
            BuildLevelRingRows();
            RefreshDistanceRings();
        }

        // "15, 5, 1" when the bonus runs from level 1 with no gap, which is how every shipped ring is written; level:value
        // pairs otherwise, so nothing the file holds is lost by showing it here.
        private static string FormatRingBonus(SortedDictionary<int, float> bonus) {
            if (bonus == null || bonus.Count == 0) { return ""; }
            bool fromOne = bonus.Keys.Select((level, i) => level == i + 1).All(same => same);
            return string.Join(", ", bonus.Select(entry => fromOne
                ? FormatRingValue(entry.Value)
                : $"{entry.Key}:{FormatRingValue(entry.Value)}").ToArray());
        }

        private static string FormatRingValue(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        // Reads either form FormatRingBonus writes, mixed if need be: a bare value takes the level after the one before
        // it, starting from level 1. Null, with the reason, when the text cannot be read.
        private static SortedDictionary<int, float> ParseRingBonus(string text, out string error) {
            error = null;
            SortedDictionary<int, float> bonus = new SortedDictionary<int, float>();
            if (string.IsNullOrWhiteSpace(text)) { return bonus; }
            int level = 0;
            foreach (string part in text.Split(',')) {
                string token = part.Trim();
                if (token.Length == 0) { continue; }
                string valueText = token;
                int colon = token.IndexOf(':');
                if (colon >= 0) {
                    if (int.TryParse(token.Substring(0, colon).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int typedLevel) == false || typedLevel < 1) {
                        error = $"'{token}' has to start with a level of 1 or more.";
                        return null;
                    }
                    level = typedLevel;
                    valueText = token.Substring(colon + 1).Trim();
                } else {
                    level++;
                }
                if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) == false) {
                    error = $"Could not read '{token}'. Enter percentages separated by commas, e.g. 15, 5, 1.";
                    return null;
                }
                if (bonus.ContainsKey(level)) {
                    error = $"Level {level} is given twice.";
                    return null;
                }
                bonus[level] = Mathf.Clamp(value, -MaxLevelRingBonus, MaxLevelRingBonus);
            }
            return bonus;
        }

        // ------------------------------------------------------------------------------------------------
        //  Preview
        // ------------------------------------------------------------------------------------------------

        private static void RefreshDistanceRings() {
            if (staged == null || levelRingWarningText == null) { return; }
            CreatureLevelSettings live = LevelSystemData.SLE_Level_Settings;
            if (staged.enableDistance == false) {
                levelRingWarningText.text = ConfigUI.L("$sls_cfg_rings_off");
            } else if (live != null && live.EnableDistanceLevelBonus == false) {
                levelRingWarningText.text = ConfigUI.L("$sls_cfg_rings_yaml_off");
            } else {
                levelRingWarningText.text = "";
            }
            RefreshLevelRingCharts();
        }

        // Worked on the creature curve the Level Distribution page shows, capped at its Max stars, before biome caps,
        // night and zone bonuses. The bonus is weighted the way a biome with no distance scale of its own weights it.
        private static void RefreshLevelRingCharts() {
            if (staged == null || baseline == null || levelRingCenterChart == null || levelRingChart == null) { return; }
            List<StagedLevelRing> rings = staged.levelRings;
            int maxLevel = staged.maxStars + 1;
            SortedDictionary<int, float> curve = ShownCreatureCurve();
            float strength = DefaultDistanceStrength();

            SortedDictionary<int, float> center = LevelSelection.ComputeLevelDistribution(curve, maxLevel);
            levelRingCenterChart.SetTitle(rings.Count > 0 ? $"Inside {rings[0].Distance} m (no ring)" : "Everywhere (no rings)");
            levelRingCenterChart.SetData(ToStarSeries(center));

            StagedLevelRing ring = shownLevelRing != null && rings.Contains(shownLevelRing) ? shownLevelRing : null;
            levelRingChart.SetVisible(ring != null);
            if (ring == null) {
                levelRingSummaryText.text = "With no rings, creatures spawn on the curve above wherever they are.";
                return;
            }
            SortedDictionary<int, float> withRing = LevelSelection.ComputeLevelDistribution(LevelSelection.ApplyLevelupBonus(curve, ring.Bonus, strength), maxLevel);
            levelRingChart.SetTitle($"Past {ring.Distance} m");
            levelRingChart.SetData(ToStarSeries(withRing));

            string summary = $"Past {ring.Distance} m, {FormatShare(ShareWithStars(withRing, 1))} of spawns have a star and the average spawn has " +
                $"{AverageStars(withRing):0.0}, against {FormatShare(ShareWithStars(center, 1))} and {AverageStars(center):0.0} with no ring.";
            levelRingSummaryText.text = summary + " " + DescribeDistanceStrength(strength);
        }

        // The distance scale a biome with none of its own gets: the All biome's, which is merged into every biome.
        private static float DefaultDistanceStrength() {
            Dictionary<Heightmap.Biome, BiomeSpecificSetting> biomes = LevelSystemData.SLE_Level_Settings?.BiomeConfiguration;
            if (biomes != null && biomes.TryGetValue(Heightmap.Biome.All, out BiomeSpecificSetting all) && all != null) { return all.DistanceScaleModifier; }
            return 1f;
        }

        // Which strength the chart is worked at, and the biomes that use another, grouped by it.
        private static string DescribeDistanceStrength(float strength) {
            string text = Mathf.Approximately(strength, 1f) ? "" : $"Bonuses shown x{FormatRingValue(strength)}, the All biome's distance scale.";
            Dictionary<Heightmap.Biome, BiomeSpecificSetting> biomes = LevelSystemData.SLE_Level_Settings?.BiomeConfiguration;
            if (biomes == null) { return text; }
            List<string> others = biomes
                .Where(b => b.Key != Heightmap.Biome.All && b.Value != null && b.Value.DistanceScaleModifier != 1f && Mathf.Approximately(b.Value.DistanceScaleModifier, strength) == false)
                .GroupBy(b => b.Value.DistanceScaleModifier)
                .Select(g => $"{string.Join(", ", g.Select(b => BiomeName(b.Key)).ToArray())} x{FormatRingValue(g.Key)}")
                .ToList();
            if (others.Count == 0) { return text; }
            return (text.Length > 0 ? text + " " : "") + $"{string.Join("; ", others.ToArray())}.";
        }

        // Keys are levels, so level 1 and below is no stars.
        private static float ShareWithStars(SortedDictionary<int, float> distribution, int stars) {
            return distribution.Where(entry => entry.Key - 1 >= stars).Sum(entry => entry.Value);
        }

        private static float AverageStars(SortedDictionary<int, float> distribution) {
            return distribution.Sum(entry => Mathf.Max(0, entry.Key - 1) * entry.Value);
        }

        private static string FormatShare(float share) {
            float pct = share * 100f;
            return pct > 0f && pct < 0.1f ? "<0.1%" : pct.ToString(pct < 10f ? "0.#" : "0", CultureInfo.InvariantCulture) + "%";
        }

        // ------------------------------------------------------------------------------------------------
        //  Ring lists, shared with the Loot page
        // ------------------------------------------------------------------------------------------------

        // A typed ring distance, clamped to the range a ring can have. Anything unreadable, or a distance another ring
        // already has, keeps the current one; the second says why on the status line.
        private static int ReadRingDistance(string text, int current, IEnumerable<int> others, string what) {
            if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int typed) == false) { return current; }
            int distance = Mathf.Clamp(typed, MinRingDistance, MaxRingDistance);
            if (distance != current && others.Contains(distance)) {
                SetStatus($"There is already a {what} at {distance} m, so this one stays at {current} m.", false);
                return current;
            }
            return distance;
        }

        // Past the furthest ring by as much as the last two are apart (or by step when there are fewer than two). False
        // when the furthest ring is already at the limit.
        private static bool NextRingDistance(List<int> ascending, int step, out int distance) {
            if (ascending.Count == 0) {
                distance = step;
                return true;
            }
            int last = ascending[ascending.Count - 1];
            int gap = ascending.Count >= 2 ? last - ascending[ascending.Count - 2] : step;
            distance = Mathf.Min(last + Mathf.Max(1, gap), MaxRingDistance);
            return distance > last;
        }

        // Nearest first. True when that moved a ring, so its rows have to be laid out again.
        private static bool SortByDistance<T>(List<T> rings, Func<T, int> distance) {
            List<T> sorted = rings.OrderBy(distance).ToList();
            if (sorted.SequenceEqual(rings)) { return false; }
            rings.Clear();
            rings.AddRange(sorted);
            return true;
        }

        private static GameObject AddRingButtonRow(Transform content, float width, UnityEngine.Events.UnityAction onClick, string tooltip) {
            GameObject row = ConfigUI.NewLayoutRow(content, width, 36f);
            WithTip(ConfigUI.AddButton(row.transform, 4f, 4f, 130f, "$sls_cfg_button_add_ring", onClick, 28f), tooltip);
            return row;
        }

        private static void AddRingRemoveButton(Transform row, float width, UnityEngine.Events.UnityAction onClick) {
            WithTip(ConfigUI.AddButton(row, width - 30f, 1f, 26f, "x", onClick, 26f),
                Tip("Remove", "Removes this ring. Nothing is written until you Save."));
        }

        // Hidden now, destroyed at the end of the frame: until then a layout group would still make room for it.
        private static void DiscardRow(GameObject row) {
            if (row == null) { return; }
            row.SetActive(false);
            UnityEngine.Object.Destroy(row);
        }
    }
}
