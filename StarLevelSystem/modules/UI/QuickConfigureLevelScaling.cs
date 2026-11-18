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
using static StarLevelSystem.modules.UI.QuickConfigPrefabs;

namespace StarLevelSystem.modules.UI {
    // Everything that raises level chances on top of the curve the Level Distribution page sets: distance rings, zones, regions
    // and boss tiers, and on the right what each of them does to that curve.
    internal static partial class QuickConfigureTool {
        private const float ScalingChartX = 458f;
        private const float ScalingChartTop = 102f;
        private const float ScalingChartBottom = 390f;
        // Where the summary starts, and the pickers stacked above it, as the prefab places them.
        private const float SummaryTop = 488f;
        private const float PickerH = 28f;
        private const float PickerStep = 30f;
        private const int PreviewLevelMax = 20;
        // A biome's stored 1 means it has no strength of its own and takes the All biome's (see the merge in SLSExtensions), so
        // an actual 1 has to be stored as something else.
        private const float BiomeStrengthOfOne = 1.001f;

        private static readonly Color DistanceLineColor = new Color(0.35f, 0.7f, 1f, 1f);
        private static readonly Color ZoneLineColor = new Color(1f, 0.45f, 0.25f, 1f);
        private static readonly Color RegionLineColor = new Color(0.4f, 0.9f, 0.45f, 1f);
        private static readonly Color BossLineColor = new Color(0.85f, 0.5f, 1f, 1f);

        private static readonly string[] DistanceSectionRows = { "DistanceOverlay", "DistanceFog", "DistanceTemple", "DistanceReadout", "DistanceColors", "DistanceStrength", "BiomeStrengthHeader", "BiomeStrengths", "RingsHelp", "RingsHeader", "RingsNone", "AddRingRow" };
        private static readonly string[] ZoneSectionRows = { "ZoneOverlay", "ZoneFog", "ZoneReadout", "ZoneBonus", "ZoneKills", "ZoneDecay", "ZoneClock", "ZoneMaxSize", "ZoneMinSize", "ZoneSizeNote", "ZoneColors", "ZoneOpacity", "ZoneReportInterval" };
        private static readonly string[] RegionSectionRows = { "RegionOverlay", "RegionFog", "RegionReadout", "RegionBonus", "RegionKills", "RegionDecay", "RegionTarget", "RegionMin", "RegionMountain", "RegionSizeNote", "RegionOpacity" };
        private static readonly string[] ConditionalSectionRows = { "ConditionalDelay", "ConditionalIncreases", "ConditionalEvenSteps", "ConditionalChanceIncrease", "ConditionalLevelBonusAuto", "ConditionalLevelBonusHeader", "ConditionalLevelBonus" };

        private static LevelDistributionChart scalingChart;
        private static float scalingChartWidth;
        private static Text scalingSummaryText;
        private static GameObject ringPickerRow;
        private static GameObject zonePickerRow;
        private static GameObject regionPickerRow;
        private static GameObject bossPickerRow;
        private static GameObject biomePickerRow;
        private static Slider extraStarsSlider;
        private static Transform increaseBonusGrid;
        private static readonly List<LevelChanceCell> increaseBonusCells = new List<LevelChanceCell>();
        // What the chart previews. Only for looking: a zone or region's level comes from play, and boss kills from the world.
        private static int previewZoneLevel = 3;
        private static int previewRegionLevel = 3;
        // The index in the boss order of the latest boss defeated, -1 for none.
        private static int previewBossIndex = 0;
        private static Heightmap.Biome previewBossBiome = Heightmap.Biome.Meadows;

        private static void ClearLevelScalingReferences() {
            ClearDistanceRingReferences();
            scalingChart = null;
            scalingSummaryText = null;
            ringPickerRow = null;
            zonePickerRow = null;
            regionPickerRow = null;
            bossPickerRow = null;
            biomePickerRow = null;
            extraStarsSlider = null;
            increaseBonusGrid = null;
            increaseBonusCells.Clear();
        }

        private static void BuildLevelScalingPage(Transform parent) {
            Transform content = ScrollContent(parent, "Sections");
            levelRingContent = content;
            Get<Image>(content, "DistanceExample").sprite = DistanceExample;
            Get<Image>(content, "ZoneExample").sprite = ZoneExample;
            Get<Image>(content, "RegionExample").sprite = RegionExample;

            BuildDistanceSection(content);
            BuildZoneSection(content);
            BuildRegionSection(content);
            BuildConditionalSection(content);
            ShowEnabledSections();

            scalingChartWidth = PageSize(parent).x - ScalingChartX;
            scalingChart = new LevelDistributionChart(parent, ScalingChartX, ScalingChartTop, scalingChartWidth, ScalingChartBottom - ScalingChartTop);
            scalingChart.SetLegend(new List<KeyValuePair<string, Color>> {
                new KeyValuePair<string, Color>(ConfigUI.L("$sls_cfg_chart_base"), LevelDistributionChart.BaseBarColor),
            });
            scalingSummaryText = Get<Text>(parent, "Summary");

            ringPickerRow = BindPicker(parent, "RingPicker", StepPreviewRing, "$sls_cfg_picker_ring", "$sls_cfg_tip_picker_ring");
            zonePickerRow = BindPicker(parent, "ZonePicker", step => previewZoneLevel = StepPreviewLevel(previewZoneLevel, step), "$sls_cfg_picker_zone", "$sls_cfg_tip_picker_level");
            regionPickerRow = BindPicker(parent, "RegionPicker", step => previewRegionLevel = StepPreviewLevel(previewRegionLevel, step), "$sls_cfg_picker_region", "$sls_cfg_tip_picker_level");
            bossPickerRow = BindPicker(parent, "BossPicker", StepPreviewBoss, "$sls_cfg_picker_boss", "$sls_cfg_tip_picker_boss");
            biomePickerRow = BindPicker(parent, "BiomePicker", StepPreviewBiome, "$sls_cfg_picker_biome", "$sls_cfg_tip_picker_biome");
            // Biome names run long in some languages.
            foreach (GameObject row in new[] { bossPickerRow, biomePickerRow }) {
                Text value = Get<Text>(row.transform, "Value");
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 9;
                value.resizeTextMaxSize = value.fontSize;
            }

            RefreshLevelScaling();
        }

        private static void BuildConditionalSection(Transform content) {
            WithTip(BindToggleRow(content, "Conditional", staged.enableConditional, on => {
                staged.enableConditional = on;
                ShowEnabledSections();
                RefreshLevelScaling();
            }), YamlTip(typeof(CreatureLevelSettings), nameof(CreatureLevelSettings.EnableConditionalCreatureLevelupChance)));
            WithTip(BindSliderRow(content, "ConditionalDelay", staged.conditionalDelay, v => {
                staged.conditionalDelay = (int)v;
                RefreshLevelScaling();
            }, max: Mathf.Max(7f, staged.conditionalDelay)), YamlTip(typeof(CreatureLevelSettings), nameof(CreatureLevelSettings.ConditionalBiomeDelay)));
            WithTip(BindSliderRow(content, "ConditionalIncreases", staged.conditionalIncreases, v => {
                staged.conditionalIncreases = (int)v;
                RefreshLevelScaling();
            }, max: Mathf.Max(8f, staged.conditionalIncreases)), YamlTip(typeof(CreatureLevelSettings), nameof(CreatureLevelSettings.ConditionalDifficultyIncreases)));

            string starsTip = YamlTip(typeof(ConditionalIncreaseBonus), nameof(ConditionalIncreaseBonus.ExtraStars));
            WithTip(BindToggleRow(content, "ConditionalEvenSteps", staged.conditionalExtraStars < 0, on => {
                // Off goes back to the stars the slider shows, so switching back and forth keeps a number already picked.
                staged.conditionalExtraStars = on ? ConditionalScaleSystem.EvenStepsToMaxLevel : Mathf.RoundToInt(extraStarsSlider.value);
                ShowEnabledSections();
                RefreshLevelScaling();
            }), starsTip);
            GameObject starsRow = WithTip(BindSliderRow(content, "ConditionalExtraStars", staged.conditionalExtraStars < 0 ? 1 : staged.conditionalExtraStars, v => {
                if (staged.conditionalExtraStars < 0) { return; }
                staged.conditionalExtraStars = (int)v;
                RefreshLevelScaling();
            }, max: Mathf.Max(5f, staged.conditionalExtraStars), whole: true), starsTip);
            extraStarsSlider = Get<Slider>(starsRow.transform, "Slider");
            // whole: false, because the row is a copy of a whole-number one and its value box would drop the decimal point.
            WithTip(BindSliderRow(content, "ConditionalChanceIncrease", staged.conditionalChanceIncrease, v => {
                staged.conditionalChanceIncrease = Mathf.Round(v * 100f) / 100f;
                RefreshLevelScaling();
            }, max: Mathf.Max(1f, staged.conditionalChanceIncrease), whole: false), YamlTip(typeof(ConditionalIncreaseBonus), nameof(ConditionalIncreaseBonus.ChanceIncrease)));
            GameObject spreadRow = WithTip(BindSliderRow(content, "ConditionalLevelBonusSpread", staged.conditionalBonusSpread, v => {
                staged.conditionalBonusSpread = Mathf.Round(v * 100f) / 100f;
                staged.SpreadBonus();
                RefreshLevelScaling();
            }, max: Mathf.Max(20f, staged.conditionalBonusSpread), whole: false), Tip("$sls_cfg_conditional_level_bonus_spread", "$sls_cfg_tip_conditional_level_bonus_spread"));
            Slider spreadSlider = Get<Slider>(spreadRow.transform, "Slider");
            WithTip(BindToggleRow(content, "ConditionalLevelBonusAuto", staged.conditionalBonusSpreadOn, on => {
                staged.conditionalBonusSpreadOn = on;
                if (on) {
                    // Spreads what the boxes already add up to, so turning it on keeps the bonus about as strong.
                    float total = Mathf.Round(Mathf.Max(0f, staged.conditionalLevelBonus.Values.Sum()) * 100f) / 100f;
                    spreadSlider.maxValue = Mathf.Max(spreadSlider.maxValue, total);
                    staged.conditionalBonusSpread = total;
                    spreadSlider.value = total;
                    staged.SpreadBonus();
                }
                ShowEnabledSections();
                RefreshLevelScaling();
            }), Tip("$sls_cfg_conditional_base_bonus_spread", "$sls_cfg_tip_conditional_level_bonus_auto"));
            WithTip(Find(content, "ConditionalLevelBonusHeader"), YamlTip(typeof(ConditionalIncreaseBonus), nameof(ConditionalIncreaseBonus.LevelBonus)));
            increaseBonusGrid = Find(content, "ConditionalLevelBonus").transform;
        }

        // Each star gets half of the star before it, and together they come to total: 3.5 over 3 stars is 2, 1 and 0.5.
        // Halving keeps a high star from catching up with a low one, which an even split would do once the curve has fallen
        // away. A share too small to write is left out, as a 0 box is.
        private static SortedDictionary<int, float> SpreadLevelBonus(float total, int stars) {
            SortedDictionary<int, float> spread = new SortedDictionary<int, float>();
            if (total <= 0f || stars <= 0) { return spread; }
            float first = total / (2f - Mathf.Pow(2f, 1 - stars));
            for (int star = 1; star <= stars; star++) {
                float share = Mathf.Round(first / Mathf.Pow(2f, star - 1) * 10000f) / 10000f;
                if (share > 0f) { spread[star] = share; }
            }
            return spread;
        }

        // A box per star a biome's creatures can reach, made as that grows. Each box's tip says what the chart has for its star
        // now, so the number to type has something to be measured against.
        private static void RefreshIncreaseBonusCells(SortedDictionary<int, float> bars, SortedDictionary<int, float> bossLine) {
            if (increaseBonusGrid == null) { return; }
            int highest = HighestBonusStar();
            for (int star = 1; star <= highest; star++) {
                if (star > increaseBonusCells.Count) { increaseBonusCells.Add(NewIncreaseBonusCell(star)); }
                LevelChanceCell cell = increaseBonusCells[star - 1];
                cell.Root.SetActive(true);
                cell.Field.interactable = staged.conditionalBonusSpreadOn == false;
                // The chart redraws on every key, and rewriting the box being typed in would move its caret.
                if (cell.Field.isFocused == false) { cell.Field.SetTextWithoutNotify(FormatIncreaseBonus(star)); }
                WithTip(cell.Root, Tip(nameof(ConditionalIncreaseBonus.LevelBonus), L("$sls_cfg_tip_conditional_level_bonus", star,
                    FormatShare(ShareWithStars(bars, star)), FormatShare(ShareWithStars(bossLine, star)), FormatRingValue(MaxLevelRingBonus))));
            }
            for (int i = highest; i < increaseBonusCells.Count; i++) { increaseBonusCells[i].Root.SetActive(false); }
        }

        // A star typed past every cap keeps its box, so nothing the file holds is out of reach. A spread is only ever over the caps.
        private static int HighestBonusStar() {
            int highest = staged.HighestCapStars();
            if (staged.conditionalBonusSpreadOn == false && staged.conditionalLevelBonus.Count > 0) { highest = Mathf.Max(highest, staged.conditionalLevelBonus.Keys.Last()); }
            return highest;
        }

        private static LevelChanceCell NewIncreaseBonusCell(int star) {
            Transform row = Spawn("SLS_QC_Row_LevelChance", increaseBonusGrid).transform;
            LevelChanceCell cell = new LevelChanceCell { Root = row.gameObject, Star = star, Field = Get<InputField>(row, "Field") };
            Get<Text>(row, "Label").text = star.ToString();
            cell.Field.onValueChanged.AddListener(text => {
                // Half-typed text such as "-" is not a number yet, and leaves the chart on the last one that was.
                if (staged.conditionalBonusSpreadOn || ReadIncreaseBonus(text, out float bonus) == false) { return; }
                if (bonus == 0f) { staged.conditionalLevelBonus.Remove(star); } else { staged.conditionalLevelBonus[star] = bonus; }
                RefreshLevelScaling();
            });
            // Rewrites the box as staged, and hides it if it was a star past every cap whose bonus was just cleared.
            cell.Field.onEndEdit.AddListener(_ => RefreshLevelScaling());
            return cell;
        }

        // Blank counts as 0, so clearing a box to type a new number drops its bonus until one is typed.
        private static bool ReadIncreaseBonus(string text, out float bonus) {
            bonus = 0f;
            if (string.IsNullOrWhiteSpace(text)) { return true; }
            if (float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out bonus) == false) { return false; }
            bonus = Mathf.Clamp(bonus, -MaxLevelRingBonus, MaxLevelRingBonus);
            return true;
        }

        private static string FormatIncreaseBonus(int star) {
            return FormatRingValue(staged.conditionalLevelBonus.TryGetValue(star, out float bonus) ? bonus : 0f);
        }

        private static void BuildDistanceSection(Transform content) {
            WithTip(BindToggleRow(content, "DistanceOn", staged.enableDistance, on => {
                staged.enableDistance = on;
                ShowEnabledSections();
                RefreshLevelScaling();
            }), Tip(ValConfig.EnableDistanceLevelScalingBonus));
            WithTip(BindToggleRow(content, "DistanceOverlay", staged.enableDistanceOverlay, v => staged.enableDistanceOverlay = v), Tip(ValConfig.EnableMapRingsForDistanceBonus));
            WithTip(BindToggleRow(content, "DistanceFog", staged.ringsAboveFog, v => staged.ringsAboveFog = v), Tip(ValConfig.MapRingsAboveFog));
            WithTip(BindToggleRow(content, "DistanceTemple", staged.ringsFromTemple, v => staged.ringsFromTemple = v), Tip(ValConfig.DistanceBonusIsFromStarterTemple));
            WithTip(BindToggleRow(content, "DistanceReadout", staged.showNoMapRing, v => staged.showNoMapRing = v), Tip(ValConfig.ShowNoMapRingLevel));
            WithTip(BindFieldRow(content, "DistanceColors", staged.ringColors, v => staged.ringColors = v), Tip(ValConfig.DistanceRingColorOptions));
            WithTip(BindSliderRow(content, "DistanceStrength", staged.allDistanceScale, v => {
                staged.allDistanceScale = v;
                RefreshLevelScaling();
            }, max: Mathf.Max(5f, staged.allDistanceScale)), YamlTip(typeof(BiomeSpecificSetting), nameof(BiomeSpecificSetting.DistanceScaleModifier)));
            BuildBiomeStrengthRows(content);

            WithTip(BindButton(content, "AddRing", AddLevelRing), Tip("$sls_cfg_button_add_ring", "$sls_cfg_tip_add_ring"));
            BuildLevelRingRows();
        }

        private static void BuildZoneSection(Transform content) {
            WithTip(BindToggleRow(content, "ZoneOn", staged.enableZone, on => {
                staged.enableZone = on;
                ShowEnabledSections();
                RefreshLevelScaling();
            }), Tip(ValConfig.EnableZoneScalingBonus));
            WithTip(BindToggleRow(content, "ZoneOverlay", staged.enableZoneOverlay, v => staged.enableZoneOverlay = v), Tip(ValConfig.EnableZoneMapOverlay));
            WithTip(BindToggleRow(content, "ZoneFog", staged.zonesAboveFog, v => staged.zonesAboveFog = v), Tip(ValConfig.ZoneOverlayAboveFog));
            WithTip(BindToggleRow(content, "ZoneReadout", staged.showNoMapZone, v => staged.showNoMapZone = v), Tip(ValConfig.ShowNoMapZoneLevel));
            WithTip(BindSliderRow(content, "ZoneBonus", staged.zoneBonusPerLevel, v => {
                staged.zoneBonusPerLevel = v;
                RefreshLevelScaling();
            }), Tip(ValConfig.ZoneLevelBonusPerLevel));
            WithTip(BindSliderRow(content, "ZoneKills", staged.zoneKillsPerLevel, v => staged.zoneKillsPerLevel = (int)v, max: Mathf.Max(1000f, staged.zoneKillsPerLevel)), Tip(ValConfig.ZoneKillsPerLevelUp));
            WithTip(BindSliderRow(content, "ZoneDecay", staged.zoneDecayPerHour, v => staged.zoneDecayPerHour = v, max: Mathf.Max(5f, staged.zoneDecayPerHour)), Tip(ValConfig.ZoneDecayLevelsPerHour));
            string[] clocks = Enum.GetNames(typeof(ZoneDecayClockSource));
            WithTip(BindEnumRow(content, "ZoneClock", clocks, Mathf.Max(0, Array.IndexOf(clocks, staged.zoneDecayClock)), i => staged.zoneDecayClock = clocks[i]), Tip(ValConfig.ZoneDecayClock));
            WithTip(BindSliderRow(content, "ZoneMaxSize", staged.zoneMaxSize, v => staged.zoneMaxSize = v), Tip(ValConfig.MaxZoneSize));
            WithTip(BindSliderRow(content, "ZoneMinSize", staged.zoneMinSize, v => staged.zoneMinSize = v), Tip(ValConfig.MinZoneSize));
            WithTip(BindFieldRow(content, "ZoneColors", staged.zoneColors, v => staged.zoneColors = v), Tip(ValConfig.ZoneOverlayColorOptions));
            WithTip(BindSliderRow(content, "ZoneOpacity", staged.zoneOpacity, v => staged.zoneOpacity = v), Tip(ValConfig.ZoneOverlayColorTransparency));
            WithTip(BindSliderRow(content, "ZoneReportInterval", staged.killReportInterval, v => staged.killReportInterval = v, max: Mathf.Max(60f, staged.killReportInterval)), Tip(ValConfig.KillReportFlushIntervalSeconds));
        }

        private static void BuildRegionSection(Transform content) {
            WithTip(BindToggleRow(content, "RegionOn", staged.enableRegion, on => {
                staged.enableRegion = on;
                ShowEnabledSections();
                RefreshLevelScaling();
            }), Tip(ValConfig.EnableRegionScalingBonus));
            WithTip(BindToggleRow(content, "RegionOverlay", staged.enableRegionOverlay, v => staged.enableRegionOverlay = v), Tip(ValConfig.EnableRegionMapOverlay));
            WithTip(BindToggleRow(content, "RegionFog", staged.regionsAboveFog, v => staged.regionsAboveFog = v), Tip(ValConfig.RegionOverlayAboveFog));
            WithTip(BindToggleRow(content, "RegionReadout", staged.showNoMapRegion, v => staged.showNoMapRegion = v), Tip(ValConfig.ShowNoMapRegionLevel));
            WithTip(BindSliderRow(content, "RegionBonus", staged.regionBonusPerLevel, v => {
                staged.regionBonusPerLevel = v;
                RefreshLevelScaling();
            }), Tip(ValConfig.RegionLevelBonusPerLevel));
            WithTip(BindSliderRow(content, "RegionKills", staged.regionKillsPerLevel, v => staged.regionKillsPerLevel = (int)v, max: Mathf.Max(1000f, staged.regionKillsPerLevel)), Tip(ValConfig.RegionKillsPerLevelUp));
            WithTip(BindSliderRow(content, "RegionDecay", staged.regionDecayPerHour, v => staged.regionDecayPerHour = v, max: Mathf.Max(5f, staged.regionDecayPerHour)), Tip(ValConfig.RegionDecayLevelsPerHour));
            WithTip(BindSliderRow(content, "RegionTarget", staged.regionTargetSize, v => staged.regionTargetSize = v), Tip(ValConfig.RegionTargetSize));
            WithTip(BindSliderRow(content, "RegionMin", staged.regionMinSize, v => staged.regionMinSize = v), Tip(ValConfig.RegionMinSize));
            WithTip(BindSliderRow(content, "RegionMountain", staged.regionMountainMinSize, v => staged.regionMountainMinSize = v), Tip(ValConfig.RegionMountainMinSize));
            WithTip(BindSliderRow(content, "RegionOpacity", staged.regionOpacity, v => staged.regionOpacity = v), Tip(ValConfig.RegionOverlayOpacity));
        }

        // A section's settings show while its system is on. The rows are all children of the one list, so the rings are too.
        private static void ShowEnabledSections() {
            if (levelRingContent == null || staged == null) { return; }
            SetRowsActive(DistanceSectionRows, staged.enableDistance);
            SetRowsActive(ZoneSectionRows, staged.enableZone);
            SetRowsActive(RegionSectionRows, staged.enableRegion);
            SetRowsActive(ConditionalSectionRows, staged.enableConditional);
            Find(levelRingContent, "ConditionalExtraStars").SetActive(staged.enableConditional && staged.conditionalExtraStars >= 0);
            Find(levelRingContent, "ConditionalLevelBonusSpread").SetActive(staged.enableConditional && staged.conditionalBonusSpreadOn);
            // A picture of a system for as long as its settings aren't taking the room.
            Find(levelRingContent, "DistanceExample").SetActive(staged.enableDistance == false);
            Find(levelRingContent, "ZoneExample").SetActive(staged.enableZone == false);
            Find(levelRingContent, "RegionExample").SetActive(staged.enableRegion == false);
            foreach (GameObject row in levelRingRows) { row.SetActive(staged.enableDistance); }
            if (staged.enableDistance) { ShowRingsListState(); }
        }

        private static void SetRowsActive(string[] names, bool active) {
            foreach (string name in names) { Find(levelRingContent, name).SetActive(active); }
        }

        private static void ShowRingsListState() {
            Find(levelRingContent, "RingsNone").SetActive(staged.levelRings.Count == 0);
            Find(levelRingContent, "RingsHeader").SetActive(staged.levelRings.Count > 0);
        }

        // --- strength per biome -------------------------------------------------------------------------------------------

        private static void BuildBiomeStrengthRows(Transform content) {
            Transform grid = Find(content, "BiomeStrengths").transform;
            foreach (Heightmap.Biome biome in staged.biomeStrengths.Keys.ToList()) {
                Heightmap.Biome rowBiome = biome;   // capture per iteration
                Transform cell = Spawn("SLS_QC_Row_BiomeCap", grid).transform;
                // A shared row whose label is sized for short numbers beside it, and biome names run long in some languages.
                Text label = SetText(cell, "Label", BiomeName(biome));
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = label.fontSize;
                // The row was made for whole star caps. A strength has decimals, and blank is an answer.
                InputField field = Get<InputField>(cell, "Field");
                field.contentType = InputField.ContentType.DecimalNumber;
                field.characterLimit = 5;
                ((Text)field.placeholder).text = "";
                BindField(field, FormatBiomeStrength(staged.biomeStrengths[biome]), text => {
                    staged.biomeStrengths[rowBiome] = ParseBiomeStrength(text, staged.biomeStrengths[rowBiome]);
                    field.SetTextWithoutNotify(FormatBiomeStrength(staged.biomeStrengths[rowBiome]));
                });
                WithTip(field.gameObject, YamlTip(typeof(BiomeSpecificSetting), nameof(BiomeSpecificSetting.DistanceScaleModifier))
                    + "\n" + ConfigUI.L("$sls_cfg_tip_biome_strength"));
            }
        }

        // Blank for a biome with nothing of its own, and the rest as the admin would have typed them.
        private static string FormatBiomeStrength(float stored) {
            if (stored == 1f) { return ""; }
            return (stored == BiomeStrengthOfOne ? 1f : stored).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static float ParseBiomeStrength(string text, float current) {
            if (string.IsNullOrWhiteSpace(text)) { return 1f; }
            if (float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float typed) == false) { return current; }
            typed = Mathf.Clamp(typed, 0f, 10f);
            return typed == 1f ? BiomeStrengthOfOne : typed;
        }

        // --- the preview --------------------------------------------------------------------------------------------------

        private static GameObject BindPicker(Transform parent, string name, Action<int> step, string title, string tip) {
            GameObject row = Find(parent, name);
            BindButton(row.transform, "Previous", () => {
                step(-1);
                RefreshLevelScaling();
            });
            BindButton(row.transform, "Next", () => {
                step(1);
                RefreshLevelScaling();
            });
            WithTip(row, Tip(title, tip));
            return row;
        }

        private static int StepPreviewLevel(int level, int step) {
            return Mathf.Clamp(level + step, 1, PreviewLevelMax);
        }

        private static void StepPreviewRing(int step) {
            List<StagedLevelRing> rings = staged.levelRings;
            if (rings.Count == 0) { return; }
            int at = rings.IndexOf(shownLevelRing);
            shownLevelRing = rings[Mathf.Clamp(at + step, 0, rings.Count - 1)];
        }

        private static void StepPreviewBoss(int step) {
            int last = ConditionalScaleSystem.BossKeyOrder(LevelSystemData.SLE_Level_Settings).Count - 1;
            previewBossIndex = Mathf.Clamp(previewBossIndex + step, -1, last);
        }

        private static void StepPreviewBiome(int step) {
            List<Heightmap.Biome> biomes = BossBiomes();
            int at = Mathf.Max(0, biomes.IndexOf(previewBossBiome));
            previewBossBiome = biomes[Mathf.Clamp(at + step, 0, biomes.Count - 1)];
        }

        // The biomes that have a boss of their own, earliest first.
        private static List<Heightmap.Biome> BossBiomes() {
            return BiomeListOrder.Where(LevelSystemData.OwnBossOfBiome.ContainsKey).ToList();
        }

        private static void SetPickerValue(GameObject row, bool visible, string value) {
            row.SetActive(visible);
            if (visible) { Get<Text>(row.transform, "Value").text = value; }
        }

        // The pickers that show stack up from the summary with no gaps, and the chart gives up room only when they need it.
        private static void LayOutPickers() {
            float top = SummaryTop - 6f;
            foreach (GameObject row in new[] { biomePickerRow, bossPickerRow, regionPickerRow, zonePickerRow, ringPickerRow }) {
                if (row.activeSelf == false) { continue; }
                top -= PickerH;
                RectTransform rect = (RectTransform)row.transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -top);
                top -= PickerStep - PickerH;
            }
            float bottom = Mathf.Min(ScalingChartBottom, top - 2f);
            scalingChart.SetRect(ScalingChartX, ScalingChartTop, scalingChartWidth, bottom - ScalingChartTop);
        }

        // A biome's star cap as the page stages it, in stars: its own, else the All biome's, else Max stars.
        private static int StagedBiomeCapStars(Heightmap.Biome biome) {
            int cap = staged.CapFor(biome);
            if (cap > 0) { return cap; }
            return staged.biomeCapForUnset > 0 ? staged.biomeCapForUnset : staged.maxStars;
        }

        // What the picked boss kills come to in the picked biome, from what is staged on this page and Level Distribution.
        private struct BossPreview {
            internal int Increases;
            internal int RaisedCapLevel;
            internal float Multiplier;
            internal SortedDictionary<int, float> Curve;
        }

        private static BossPreview PreviewBossKills(SortedDictionary<int, float> curve) {
            List<string> order = ConditionalScaleSystem.BossKeyOrder(LevelSystemData.SLE_Level_Settings);
            previewBossIndex = Mathf.Clamp(previewBossIndex, -1, order.Count - 1);
            ConditionalIncreaseBonus bonus = staged.IncreaseBonus();
            BossPreview preview = new BossPreview();
            preview.Increases = ConditionalScaleSystem.IncreasesEarned(order, LevelSystemData.OwnBossOfBiome[previewBossBiome], previewBossIndex, staged.conditionalDelay, staged.conditionalIncreases);
            preview.RaisedCapLevel = ConditionalScaleSystem.RaisedCap(StagedBiomeCapStars(previewBossBiome) + 1, staged.maxStars + 1, preview.Increases, bonus, order.Count);
            preview.Multiplier = ConditionalScaleSystem.ChanceMultiplier(bonus, preview.Increases);
            preview.Curve = ConditionalScaleSystem.ApplyIncreases(curve, bonus, preview.Increases, preview.RaisedCapLevel);
            return preview;
        }

        private static string PreviewBossName() {
            List<string> order = ConditionalScaleSystem.BossKeyOrder(LevelSystemData.SLE_Level_Settings);
            return previewBossIndex < 0 ? ConfigUI.L("$sls_cfg_picker_boss_none") : L("$sls_cfg_picker_boss_value", BossKeyName(order[previewBossIndex]));
        }

        // Worked on the curve the Level Distribution page shows, capped at its Max stars, by daylight and before biome caps. Each
        // line is one system on its own; the words below say what they come to together, as they do on a spawn. Boss kills are
        // the exception: they belong to a biome, so their line is that biome, with its own cap.
        private static void RefreshLevelScaling() {
            if (staged == null || baseline == null || scalingChart == null) { return; }
            staged.KeepBonusSpread();
            int maxLevel = staged.maxStars + 1;
            SortedDictionary<int, float> curve = ShownCreatureCurve();
            SortedDictionary<int, float> withBase = LevelSelection.ComputeLevelDistribution(curve, maxLevel);

            List<StagedLevelRing> rings = staged.levelRings;
            if (shownLevelRing == null || rings.Contains(shownLevelRing) == false) { shownLevelRing = rings.FirstOrDefault(); }
            StagedLevelRing ring = staged.enableDistance ? shownLevelRing : null;
            float strength = staged.allDistanceScale;
            float zoneBonus = staged.enableZone ? LeveledAreas.BonusFor(previewZoneLevel, staged.zoneBonusPerLevel) : 1f;
            float regionBonus = staged.enableRegion ? LeveledAreas.BonusFor(previewRegionLevel, staged.regionBonusPerLevel) : 1f;
            SortedDictionary<int, float> ringCurve = ring != null ? LevelSelection.ApplyLevelupBonus(curve, ring.Bonus, strength) : curve;

            List<LevelChartLine> lines = new List<LevelChartLine>();
            List<KeyValuePair<string, Color>> legend = new List<KeyValuePair<string, Color>> {
                new KeyValuePair<string, Color>(ConfigUI.L("$sls_cfg_chart_base"), LevelDistributionChart.BaseBarColor),
            };
            if (ring != null) {
                lines.Add(new LevelChartLine { Color = DistanceLineColor, Chances = ToStarSeries(LevelSelection.ComputeLevelDistribution(ringCurve, maxLevel)) });
                legend.Add(new KeyValuePair<string, Color>(ConfigUI.L("$sls_cfg_chart_distance"), DistanceLineColor));
            }
            if (staged.enableZone) {
                lines.Add(new LevelChartLine { Color = ZoneLineColor, Chances = ToStarSeries(LevelSelection.ComputeLevelDistribution(curve, maxLevel, 1f, zoneBonus)) });
                legend.Add(new KeyValuePair<string, Color>(ConfigUI.L("$sls_cfg_chart_zone"), ZoneLineColor));
            }
            if (staged.enableRegion) {
                lines.Add(new LevelChartLine { Color = RegionLineColor, Chances = ToStarSeries(LevelSelection.ComputeLevelDistribution(curve, maxLevel, 1f, regionBonus)) });
                legend.Add(new KeyValuePair<string, Color>(ConfigUI.L("$sls_cfg_chart_region"), RegionLineColor));
            }
            BossPreview boss = PreviewBossKills(curve);
            SortedDictionary<int, float> bossLine = LevelSelection.ComputeLevelDistribution(boss.Curve, boss.RaisedCapLevel, 1f, boss.Multiplier);
            if (staged.enableConditional) {
                lines.Add(new LevelChartLine { Color = BossLineColor, Chances = ToStarSeries(bossLine) });
                legend.Add(new KeyValuePair<string, Color>(ConfigUI.L("$sls_cfg_chart_bosses"), BossLineColor));
            }
            RefreshIncreaseBonusCells(withBase, bossLine);

            SetPickerValue(ringPickerRow, staged.enableDistance, ring != null ? L("$sls_cfg_picker_ring_value", ring.Distance) : ConfigUI.L("$sls_cfg_picker_ring_none"));
            SetPickerValue(zonePickerRow, staged.enableZone, L("$sls_cfg_picker_level_value", previewZoneLevel, FormatBonus(zoneBonus)));
            SetPickerValue(regionPickerRow, staged.enableRegion, L("$sls_cfg_picker_level_value", previewRegionLevel, FormatBonus(regionBonus)));
            SetPickerValue(bossPickerRow, staged.enableConditional, PreviewBossName());
            SetPickerValue(biomePickerRow, staged.enableConditional, L("$sls_cfg_picker_biome_value", BiomeName(previewBossBiome), boss.RaisedCapLevel - 1, FormatBonus(boss.Multiplier)));
            LayOutPickers();

            scalingChart.SetLegend(legend);
            scalingChart.SetData(ToStarSeries(withBase), lines);

            if (lines.Count == 0) {
                scalingSummaryText.text = ConfigUI.L("$sls_cfg_scaling_summary_off");
                return;
            }
            float multiplier = zoneBonus * regionBonus;
            int togetherCap = maxLevel;
            if (staged.enableConditional) {
                ringCurve = ring != null ? LevelSelection.ApplyLevelupBonus(boss.Curve, ring.Bonus, strength) : boss.Curve;
                multiplier *= boss.Multiplier;
                togetherCap = boss.RaisedCapLevel;
            }
            SortedDictionary<int, float> together = LevelSelection.ComputeLevelDistribution(ringCurve, togetherCap, 1f, multiplier);
            scalingSummaryText.text = L("$sls_cfg_scaling_summary", FormatShare(ShareWithStars(together, 1)), AverageStars(together).ToString("0.0"),
                FormatShare(ShareWithStars(withBase, 1)), AverageStars(withBase).ToString("0.0"));
        }

        private static string FormatBonus(float bonus) => bonus.ToString("0.##", CultureInfo.InvariantCulture);

        // --- defaults, for Reset page -----------------------------------------------------------------------------------------

        private static void ResetLevelScalingPage() {
            ResetScalingPage();
            ResetDistanceRingsPage();
            staged.ringsAboveFog = DefaultOf(ValConfig.MapRingsAboveFog);
            staged.ringsFromTemple = DefaultOf(ValConfig.DistanceBonusIsFromStarterTemple);
            staged.ringColors = DefaultOf(ValConfig.DistanceRingColorOptions);
            staged.zonesAboveFog = DefaultOf(ValConfig.ZoneOverlayAboveFog);
            staged.zoneBonusPerLevel = DefaultOf(ValConfig.ZoneLevelBonusPerLevel);
            staged.zoneKillsPerLevel = DefaultOf(ValConfig.ZoneKillsPerLevelUp);
            staged.zoneDecayPerHour = DefaultOf(ValConfig.ZoneDecayLevelsPerHour);
            staged.zoneDecayClock = DefaultOf(ValConfig.ZoneDecayClock);
            staged.zoneMaxSize = DefaultOf(ValConfig.MaxZoneSize);
            staged.zoneMinSize = DefaultOf(ValConfig.MinZoneSize);
            staged.zoneColors = DefaultOf(ValConfig.ZoneOverlayColorOptions);
            staged.zoneOpacity = DefaultOf(ValConfig.ZoneOverlayColorTransparency);
            staged.killReportInterval = DefaultOf(ValConfig.KillReportFlushIntervalSeconds);
            staged.regionsAboveFog = DefaultOf(ValConfig.RegionOverlayAboveFog);
            staged.regionBonusPerLevel = DefaultOf(ValConfig.RegionLevelBonusPerLevel);
            staged.regionKillsPerLevel = DefaultOf(ValConfig.RegionKillsPerLevelUp);
            staged.regionDecayPerHour = DefaultOf(ValConfig.RegionDecayLevelsPerHour);
            staged.regionTargetSize = DefaultOf(ValConfig.RegionTargetSize);
            staged.regionMinSize = DefaultOf(ValConfig.RegionMinSize);
            staged.regionMountainMinSize = DefaultOf(ValConfig.RegionMountainMinSize);
            staged.regionOpacity = DefaultOf(ValConfig.RegionOverlayOpacity);

            CreatureLevelSettings shipped = ShippedDefaults(YamlConfigManager.LevelSettings);
            BiomeSpecificSetting all = null;
            shipped?.BiomeConfiguration?.TryGetValue(Heightmap.Biome.All, out all);
            staged.allDistanceScale = all?.DistanceScaleModifier ?? 1f;
            foreach (Heightmap.Biome biome in staged.biomeStrengths.Keys.ToList()) {
                BiomeSpecificSetting setting = null;
                shipped?.BiomeConfiguration?.TryGetValue(biome, out setting);
                staged.biomeStrengths[biome] = setting?.DistanceScaleModifier ?? 1f;
            }
        }
    }
}
