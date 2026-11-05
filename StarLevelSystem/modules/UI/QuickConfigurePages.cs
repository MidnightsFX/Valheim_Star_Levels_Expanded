using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.modules.UI.QuickConfigPrefabs;

namespace StarLevelSystem.modules.UI {
    // The pages of the quick configure panel. Each Build* method joins what its page's prefab holds to the static `staged`
    // config and adds what depends on the data; the shell in QuickConfigureTool.cs owns navigation and saving.
    internal static partial class QuickConfigureTool {

        // Sample creatures for the stat examples | TODO: allow selecting different creature examples?
        private const float TrollHp = 600f;
        private const float TrollDmg = 70f;
        private const float TheElderHP = 2500f;
        private const float TheElderDmg = 60f;

        private static readonly string[] CalcStyleOptions = Enum.GetNames(typeof(LevelupCalculationStyle));
        private static readonly string[] DisplayStyleOptions = Enum.GetNames(typeof(ModifierDisplayStyle));

        // Brief, player-facing descriptions for each modifier (see Package/README.md), keyed by ModifierNames. Tokens.
        private static readonly Dictionary<string, string> ModifierDescriptions = new Dictionary<string, string>() {
            { "BossSummoner", "$sls_cfg_mod_boss_summoner" },
            { "SoulEater", "$sls_cfg_mod_soul_eater" },
            { "LifeLink", "$sls_cfg_mod_life_link" },
            { "Splitter", "$sls_cfg_mod_splitter" },
            { "Lootbags", "$sls_cfg_mod_lootbags" },
            { "Fire", "$sls_cfg_mod_fire" },
            { "Frost", "$sls_cfg_mod_frost" },
            { "Poison", "$sls_cfg_mod_poison" },
            { "Lightning", "$sls_cfg_mod_lightning" },
            { "FireNova", "$sls_cfg_mod_fire_nova" },
            { "FrostNova", "$sls_cfg_mod_frost_nova" },
            { "PoisonNova", "$sls_cfg_mod_poison_nova" },
            { "LightningNova", "$sls_cfg_mod_lightning_nova" },
            { "Evolving", "$sls_cfg_mod_evolving" },
            { "ResistSlash", "$sls_cfg_mod_resist_slash" },
            { "ResistBlunt", "$sls_cfg_mod_resist_blunt" },
            { "ResistPierce", "$sls_cfg_mod_resist_pierce" },
            { "ResistFire", "$sls_cfg_mod_resist_fire" },
            { "ResistFrost", "$sls_cfg_mod_resist_frost" },
            { "ResistPoison", "$sls_cfg_mod_resist_poison" },
            { "ResistSpirit", "$sls_cfg_mod_resist_spirit" },
            { "Alert", "$sls_cfg_mod_alert" },
            { "Big", "$sls_cfg_mod_big" },
            { "Small", "$sls_cfg_mod_small" },
            { "Fast", "$sls_cfg_mod_fast" },
            { "StaminaDrain", "$sls_cfg_mod_stamina_drain" },
            { "EitrDrain", "$sls_cfg_mod_eitr_drain" },
            { "Brutal", "$sls_cfg_mod_brutal" },
            { "ElementalChaos", "$sls_cfg_mod_elemental_chaos" },
            { "Reflection", "$sls_cfg_mod_reflection" },
            { "Stalward", "$sls_cfg_mod_stalward" },
            { "Regenerating", "$sls_cfg_mod_regenerating" },
            { "Vampiric", "$sls_cfg_mod_vampiric" },
            { "Shielded", "$sls_cfg_mod_shielded" },
            { "EarthCaller", "$sls_cfg_mod_earth_caller" },
        };

        // Each boss's defeat key to vanilla's own token for its name, so it reads as it does in game.
        private static readonly Dictionary<string, string> BossKeyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "defeated_eikthyr", "$enemy_eikthyr" },
            { "defeated_gdking", "$enemy_gdking" },
            { "defeated_bonemass", "$enemy_bonemass" },
            { "defeated_dragon", "$enemy_dragon" },
            { "defeated_goblinking", "$enemy_goblinking" },
            { "defeated_queen", "$enemy_seekerqueen" },
            { "defeated_fader", "$enemy_fader" },
            { "defeated_frozenking_p3", "$enemy_frozenking" },
        };

        private static readonly Color HarderColor = new Color(0.97f, 0.47f, 0.42f);
        private static readonly Color EasierColor = new Color(0.55f, 0.85f, 0.5f);
        private static readonly Color NemesisColor = new Color(0.8f, 0.58f, 0.95f);
        private static readonly Color InactiveColor = new Color(0.6f, 0.6f, 0.6f);
        private const string WarningColorTag = "<color=#FBBF24>";
        // Beige, for text that sits beside a brighter label rather than replacing it.
        private const string MutedColorTag = "<color=#CDBE91>";

        private static Sprite DistanceExample;
        private static Sprite ZoneExample;
        private static Sprite RegionExample;

        // --- page widgets that are updated after they are built ---
        private static Slider minStarsSlider;
        private static Slider maxStarsSlider;
        private static Slider bossMinStarsSlider;
        private static Slider bossMaxStarsSlider;
        private static LevelDistributionChart bossChart;
        private static int shownBossMinStars;
        private static int shownBossMaxStars;
        // The last value each star slider reported. Its value box re-reports the same value whenever it loses focus.
        private static int shownMinStars;
        private static int shownMaxStars;
        private static LevelDistributionChart distributionChart;
        private static readonly List<BiomeCapField> biomeCapFields = new List<BiomeCapField>();
        private static Text distributionNotesText;
        private static InputField tableThresholdField;
        private static Text tableStatusText;
        // The Exponential values shown in the threshold box when the span has no table yet. They are a suggestion, so
        // tabbing through the box without changing them is not an edit.
        private static string tablePrefillText;
        private static InputField bossTableThresholdField;
        private static Text bossTableStatusText;
        private static string bossTablePrefillText;
        private static Text trollExampleText;
        private static Text elderExampleText;
        private static Text multiplayerExampleText;
        private static Text nemesisWarningText;
        private static readonly List<NemesisActionView> nemesisActionViews = new List<NemesisActionView>();

        private class BiomeCapField {
            internal Heightmap.Biome Biome;
            internal InputField Field;
            internal Color TextColor;
        }

        private class NemesisActionView {
            internal StagedNemesisAction Action;
            internal Text Tag;
            internal Text Description;
        }

        private static void ClearPageReferences() {
            ClearSimpleReferences();
            ClearDistributionReferences();
            ClearLevelScalingReferences();
            ClearStatsReferences();
            ClearNemesisReferences();
            ClearRaidPageReferences();
            ClearLocationResetReferences();
            ClearLootPageReferences();
        }

        private static void ClearDistributionReferences() {
            minStarsSlider = null;
            maxStarsSlider = null;
            bossMinStarsSlider = null;
            bossMaxStarsSlider = null;
            distributionChart = null;
            bossChart = null;
            biomeCapFields.Clear();
            distributionNotesText = null;
            tableThresholdField = null;
            tableStatusText = null;
            tablePrefillText = null;
            bossTableThresholdField = null;
            bossTableStatusText = null;
            bossTablePrefillText = null;
        }

        private static void ClearStatsReferences() {
            trollExampleText = null;
            elderExampleText = null;
            multiplayerExampleText = null;
        }

        private static void ClearNemesisReferences() {
            nemesisWarningText = null;
            nemesisActionViews.Clear();
        }

        // ------------------------------------------------------------------------------------------------
        //  Level progression systems
        // ------------------------------------------------------------------------------------------------

        private static void ResetScalingPage() {
            staged.enableDistance = DefaultOf(ValConfig.EnableDistanceLevelScalingBonus);
            staged.enableDistanceOverlay = DefaultOf(ValConfig.EnableMapRingsForDistanceBonus);
            staged.showNoMapRing = DefaultOf(ValConfig.ShowNoMapRingLevel);
            staged.enableZone = DefaultOf(ValConfig.EnableZoneScalingBonus);
            staged.enableZoneOverlay = DefaultOf(ValConfig.EnableZoneMapOverlay);
            staged.showNoMapZone = DefaultOf(ValConfig.ShowNoMapZoneLevel);
            staged.enableRegion = DefaultOf(ValConfig.EnableRegionScalingBonus);
            staged.enableRegionOverlay = DefaultOf(ValConfig.EnableRegionMapOverlay);
            staged.showNoMapRegion = DefaultOf(ValConfig.ShowNoMapRegionLevel);
            CreatureLevelSettings shipped = ShippedDefaults(YamlConfigManager.LevelSettings);
            staged.enableConditional = shipped != null && shipped.EnableConditionalCreatureLevelupChance;
            staged.conditionalDelay = shipped != null ? shipped.ConditionalBiomeDelay : 1;
            staged.conditionalIncreases = shipped != null ? shipped.ConditionalDifficultyIncreases : 7;
        }

        // ------------------------------------------------------------------------------------------------
        //  Level distribution
        // ------------------------------------------------------------------------------------------------

        private static void BuildDistributionPage(Transform parent) {
            const float ChartX = 420f;
            const float ColTop = 46f;
            // Biome caps: a cell per capped biome, either auto-tuned from Max stars or typed in by hand.
            const float CellW = 169f;
            const float CapHeaderH = 26f;
            const float CapRowH = 30f;
            const int PerRow = 5;
            // The notes under the caps are usually empty, so the block is measured from the bottom of the page rather
            // than parked under a full-height chart. Everything the caps do not need goes to the charts and the column.
            const float NotesH = 38f;
            float chartW = PageSize(parent).x - ChartX;

            List<Heightmap.Biome> cappedBiomes = staged.biomeCapOriginals.Keys
                .OrderBy(b => staged.biomeCapOriginals[b]).ThenBy(b => (int)b).ToList();
            int capRows = Mathf.Max(1, Mathf.CeilToInt(cappedBiomes.Count / (float)PerRow));
            float capsY = PageSize(parent).y - NotesH - CapHeaderH - capRows * CapRowH;
            float chartTop = ColTop + 40f;
            float chartFullH = capsY - 10f - chartTop;
            float chartHalfH = (chartFullH - 8f) * 0.5f;

            // The caps and their notes run the full width of the page, so the settings column stops above them. It still
            // outgrows that once bosses have a section of their own, so it scrolls. Rows that only apply to some curve
            // styles - or only while bosses are configured separately - are switched off rather than laid out again: a
            // vertical layout group skips inactive children, so the space they took collapses.
            SetDistributionColumnHeight(Find(parent, "Settings"), capsY - ColTop - 6f);
            ConfigUI.PositionRow(Find(parent, "CapsBar"), 0f, capsY);

            shownMinStars = staged.MinStars;
            shownMaxStars = staged.maxStars;
            shownBossMinStars = staged.BossMinStars;
            shownBossMaxStars = staged.maxBossLevel;

            GameObject chanceRow = null, gaussianRow = null, tableRow = null, tableStatusRow = null;
            GameObject bossChanceRow = null, bossGaussianRow = null, bossTableRow = null, bossTableStatusRow = null;
            List<GameObject> bossRows = new List<GameObject>();

            void ShowStyleRows() {
                LevelupCalculationStyle style = staged.generator.LevelupCalculationStyle;
                chanceRow.SetActive(style != LevelupCalculationStyle.Table);
                gaussianRow.SetActive(style == LevelupCalculationStyle.Gaussian);
                tableRow.SetActive(style == LevelupCalculationStyle.Table);
                tableStatusRow.SetActive(style == LevelupCalculationStyle.Table);
            }

            void ShowBossRows() {
                bool separate = staged.bossCurveOn;
                foreach (GameObject row in bossRows) { row.SetActive(separate); }
                if (separate) {
                    LevelupCalculationStyle style = staged.bossGenerator.LevelupCalculationStyle;
                    bossChanceRow.SetActive(style != LevelupCalculationStyle.Table);
                    bossGaussianRow.SetActive(style == LevelupCalculationStyle.Gaussian);
                    bossTableRow.SetActive(style == LevelupCalculationStyle.Table);
                    bossTableStatusRow.SetActive(style == LevelupCalculationStyle.Table);
                }
                // One tall chart while bosses follow the creature curve, two half-height ones once they have their own.
                distributionChart.SetRect(ChartX, chartTop, chartW, separate ? chartHalfH : chartFullH);
                distributionChart.SetTitle(separate ? ConfigUI.L("$sls_cfg_dist_chart_creatures") : "");
                bossChart.SetVisible(separate);
                if (separate) { bossChart.SetRect(ChartX, chartTop + chartHalfH + 8f, chartW, chartHalfH); }
                RefreshDistribution();
            }

            GameObject minRow = WithTip(BindSliderRow(parent, "MinStars", staged.MinStars, v => OnMinStarsChanged((int)v)),
                Tip("MinLevel", "$sls_cfg_tip_min_stars"));
            minStarsSlider = Get<Slider>(minRow.transform, "Slider");
            GameObject maxRow = WithTip(BindSliderRow(parent, "MaxStars", staged.maxStars, v => OnMaxStarsChanged((int)v)),
                Tip(ValConfig.MaxLevel));
            maxStarsSlider = Get<Slider>(maxRow.transform, "Slider");

            WithTip(BindEnumRow(parent, "CurveStyle", CalcStyleOptions, (int)staged.generator.LevelupCalculationStyle, i => {
                staged.generator.LevelupCalculationStyle = (LevelupCalculationStyle)i;
                staged.tableCurve = null;
                ShowStyleRows();
                RefreshTableRows(true);
                RefreshDistribution();
            }), Tip("LevelupCalculationStyle", "$sls_cfg_tip_curve_style"));
            chanceRow = WithTip(BindSliderRow(parent, "LevelupChance", staged.generator.LevelUpChance, v => {
                if (v != staged.generator.LevelUpChance) { staged.tableCurve = null; }
                staged.generator.LevelUpChance = v;
                RefreshDistribution();
            }), Tip("LevelUpChance", "$sls_cfg_tip_levelup_chance"));
            gaussianRow = WithTip(BindSliderRow(parent, "GaussianOffset", staged.generator.GaussianOffset, v => {
                if (v != staged.generator.GaussianOffset) { staged.tableCurve = null; }
                staged.generator.GaussianOffset = v;
                RefreshDistribution();
            }), Tip("GaussianOffset", "$sls_cfg_tip_gaussian_offset"));
            tableRow = WithTip(BindFieldRow(parent, "TableThresholds", "", CommitTableThresholds),
                Tip("LevelupChanceTablesBySpan", "$sls_cfg_tip_table_thresholds"));
            tableThresholdField = Get<InputField>(tableRow.transform, "Field");
            tableStatusText = Get<Text>(parent, "TableStatus");
            tableStatusRow = tableStatusText.gameObject;
            WithTip(BindSliderRow(parent, "NightMultiplier", staged.generator.NightMultiplier, v => {
                if (v != staged.generator.NightMultiplier) { staged.tableCurve = null; }
                staged.generator.NightMultiplier = v;
                RefreshDistribution();
            }), Tip("NightMultiplier", "$sls_cfg_tip_night_multiplier"));

            GameObject bossMaxRow = WithTip(BindSliderRow(parent, "MaxBossStars", staged.maxBossLevel, v => OnBossMaxStarsChanged((int)v)),
                Tip(ValConfig.MaxBossLevel));
            bossMaxStarsSlider = Get<Slider>(bossMaxRow.transform, "Slider");
            WithTip(BindToggleRow(parent, "BossOwnCurve", staged.bossCurveOn, on => {
                staged.bossCurveOn = on;
                RefreshTableRows(true);
                ShowBossRows();
            }), YamlTip(typeof(CreatureLevelSettings), nameof(CreatureLevelSettings.BossLevelupGenerators)));

            GameObject bossMinRow = WithTip(BindSliderRow(parent, "BossMinStars", staged.BossMinStars, v => OnBossMinStarsChanged((int)v)),
                Tip("MinLevel", "$sls_cfg_tip_boss_min_stars"));
            bossMinStarsSlider = Get<Slider>(bossMinRow.transform, "Slider");
            bossRows.Add(bossMinRow);
            bossRows.Add(WithTip(BindEnumRow(parent, "BossCurveStyle", CalcStyleOptions, (int)staged.bossGenerator.LevelupCalculationStyle, i => {
                staged.bossGenerator.LevelupCalculationStyle = (LevelupCalculationStyle)i;
                RefreshTableRows(true);
                ShowBossRows();
            }), Tip("LevelupCalculationStyle", "$sls_cfg_tip_boss_curve_style_own")));
            bossChanceRow = WithTip(BindSliderRow(parent, "BossLevelupChance", staged.bossGenerator.LevelUpChance, v => {
                staged.bossGenerator.LevelUpChance = v;
                RefreshDistribution();
            }), Tip("LevelUpChance", "$sls_cfg_tip_boss_levelup_chance"));
            bossRows.Add(bossChanceRow);
            bossGaussianRow = WithTip(BindSliderRow(parent, "BossGaussianOffset", staged.bossGenerator.GaussianOffset, v => {
                staged.bossGenerator.GaussianOffset = v;
                RefreshDistribution();
            }), Tip("GaussianOffset", "$sls_cfg_tip_boss_gaussian_offset"));
            bossRows.Add(bossGaussianRow);
            bossTableRow = WithTip(BindFieldRow(parent, "BossTableThresholds", "", CommitBossTableThresholds),
                Tip("BossLevelupChanceTablesBySpan", "$sls_cfg_tip_boss_table_thresholds"));
            bossTableThresholdField = Get<InputField>(bossTableRow.transform, "Field");
            bossRows.Add(bossTableRow);
            bossTableStatusText = Get<Text>(parent, "BossTableStatus");
            bossTableStatusRow = bossTableStatusText.gameObject;
            bossRows.Add(bossTableStatusRow);
            bossRows.Add(WithTip(BindSliderRow(parent, "BossNightMultiplier", staged.bossGenerator.NightMultiplier, v => staged.bossGenerator.NightMultiplier = v),
                Tip("NightMultiplier", "$sls_cfg_tip_boss_night_multiplier")));

            // Size per star does nothing while growth is off, so its row goes with the toggle.
            GameObject sizePerStarRow = null;
            WithTip(BindToggleRow(parent, "GrowPerStar", staged.scaleSizePerStar, on => {
                staged.scaleSizePerStar = on;
                sizePerStarRow.SetActive(on);
            }), Tip(ValConfig.EnableCreatureScalingPerLevel));
            sizePerStarRow = WithTip(BindSliderRow(parent, "SizePerStar", staged.sizePerStar, v => staged.sizePerStar = v),
                Tip(ValConfig.PerLevelScaleBonus));
            sizePerStarRow.SetActive(staged.scaleSizePerStar);
            // The slider stops at 20x; a larger cap set in the .cfg widens it rather than being clamped on save.
            WithTip(BindSliderRow(parent, "MaxSize", staged.maxCreatureScale, v => staged.maxCreatureScale = v, max: Mathf.Max(20f, staged.maxCreatureScale)),
                Tip(ValConfig.MaximumCreatureScale));

            RefreshTableRows(true);
            ShowStyleRows();

            distributionChart = new LevelDistributionChart(parent, ChartX, chartTop, chartW, chartFullH);
            bossChart = new LevelDistributionChart(parent, ChartX, chartTop + chartHalfH + 8f, chartW, chartHalfH);
            bossChart.SetTitle(ConfigUI.L("$sls_cfg_dist_bosses"));

            BindToggle(parent, "CapsAuto", staged.biomeCapAuto, OnBiomeCapAutoChanged);
            WithTip(Find(parent, "CapsAutoLabel"), Tip(ValConfig.AutoTuneBiomeStarCaps));

            int cell = 0;
            foreach (Heightmap.Biome capped in cappedBiomes) {
                Heightmap.Biome biome = capped;   // capture per iteration
                Transform capCell = Spawn("SLS_QC_Row_BiomeCap", parent).transform;
                ConfigUI.PositionRow(capCell.gameObject, (cell % PerRow) * CellW, capsY + CapHeaderH + (cell / PerRow) * CapRowH);
                SetText(capCell, "Label", BiomeName(biome));
                InputField field = BindField(capCell, "Field", staged.CapFor(biome).ToString(), text => CommitBiomeCap(biome, text));
                WithTip(field.gameObject, YamlTip(typeof(BiomeSpecificSetting), nameof(BiomeSpecificSetting.BiomeMaxLevelOverride))
                    + "\n" + ConfigUI.L("$sls_cfg_tip_biome_cap_manual"));
                biomeCapFields.Add(new BiomeCapField { Biome = biome, Field = field, TextColor = field.textComponent != null ? field.textComponent.color : Color.white });
                cell++;
            }

            distributionNotesText = Get<Text>(parent, "Notes");

            // Lays the charts out for the current mode and draws everything.
            ShowBossRows();
        }

        // How tall the settings column is depends on how many rows of biome caps sit under it. Each part of the scroll view
        // follows, with the sizes the prefab gives them at any height: the bar and its sliding area stop 8 short of each end.
        private static void SetDistributionColumnHeight(GameObject column, float h) {
            Transform canvas = column.transform.Find("Canvas");
            Transform view = canvas.Find("Scroll View");
            Transform bar = view.Find("Scrollbar Vertical");
            foreach (Transform part in new[] { column.transform, canvas, view, view.Find("Viewport") }) {
                ((RectTransform)part).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            }
            foreach (Transform part in new[] { bar, bar.Find("Sliding Area") }) {
                ((RectTransform)part).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h - 16f);
            }
        }

        private static void ResetDistributionPage() {
            staged.ResetDistribution(ShippedDefaults(YamlConfigManager.LevelSettings));
            staged.scaleSizePerStar = DefaultOf(ValConfig.EnableCreatureScalingPerLevel);
            staged.sizePerStar = DefaultOf(ValConfig.PerLevelScaleBonus);
            staged.maxCreatureScale = DefaultOf(ValConfig.MaximumCreatureScale);
        }

        // Min and Max both rewrite the generator's range, so whichever curve is picked always tapers across exactly the
        // stars shown. Each keeps Min <= Max by moving the other slider. The value boxes also report their value when
        // they merely lose focus, so a value the slider already showed is not an edit.
        private static void OnMinStarsChanged(int minStars) {
            if (staged == null || minStars == shownMinStars) { return; }
            shownMinStars = minStars;
            // Max stars pulling Min down sets the generator before it moves this slider. Only a Min picked here is a
            // change to the curve itself.
            if (staged.generator.MinLevel != minStars + 1) { staged.tableCurve = null; }
            staged.generator.MinLevel = minStars + 1;
            if (minStars > staged.maxStars && maxStarsSlider != null) {
                maxStarsSlider.value = minStars;   // its listener moves maxStars up to match
            }
            staged.generator.MaxLevel = staged.maxStars + 1;
            RefreshTableRows(true);
            RefreshDistribution();
        }

        private static void OnMaxStarsChanged(int maxStars) {
            if (staged == null || maxStars == shownMaxStars) { return; }
            shownMaxStars = maxStars;
            staged.maxStars = maxStars;
            staged.generator.MaxLevel = maxStars + 1;
            if (staged.generator.MinLevel > maxStars + 1) {
                // Set first, so the slider's listener finds Min already in place and only redraws its box.
                staged.generator.MinLevel = maxStars + 1;
                if (minStarsSlider != null) { minStarsSlider.value = maxStars; }
            }
            RefreshTableRows(true);
            RefreshDistribution();
            UpdateExampleMath();
        }

        // The boss pair, which behave the same way. Max boss stars is also the cap the roll is clamped to, so it applies
        // whether or not bosses have a curve of their own.
        private static void OnBossMinStarsChanged(int minStars) {
            if (staged == null || minStars == shownBossMinStars) { return; }
            shownBossMinStars = minStars;
            staged.bossGenerator.MinLevel = minStars + 1;
            if (minStars > staged.maxBossLevel && bossMaxStarsSlider != null) {
                bossMaxStarsSlider.value = minStars;
            }
            staged.bossGenerator.MaxLevel = staged.maxBossLevel + 1;
            RefreshTableRows(true);
            RefreshDistribution();
        }

        private static void OnBossMaxStarsChanged(int maxStars) {
            if (staged == null || maxStars == shownBossMaxStars) { return; }
            shownBossMaxStars = maxStars;
            staged.maxBossLevel = maxStars;
            staged.bossGenerator.MaxLevel = maxStars + 1;
            if (staged.bossGenerator.MinLevel > maxStars + 1) {
                staged.bossGenerator.MinLevel = maxStars + 1;
                if (bossMinStarsSlider != null) { bossMinStarsSlider.value = maxStars; }
            }
            RefreshTableRows(true);
            RefreshDistribution();
            UpdateExampleMath();
        }

        // The creature curve the page shows. Until it is changed that is what the world rolls today, which may be a
        // hand-written table that no generator describes; after Reset page or a difficulty, the table they set.
        private static SortedDictionary<int, float> ShownCreatureCurve() {
            if (staged.tableCurve != null) { return staged.tableCurve; }
            if (staged.CurveDiffers(baseline) == false) { return LiveDefaultTable(); }
            staged.tables.TryGetValue(staged.TableSpan, out List<float> values);
            return StagedCurveTable(staged.generator, staged.TableSpan, values);
        }

        private static void RefreshDistribution() {
            if (staged == null || baseline == null || distributionChart == null) { return; }

            // The cap matches LevelSelection.GetMaxCreatureLevel before biome overrides.
            SortedDictionary<int, float> table = ShownCreatureCurve();
            distributionChart.SetData(ToStarSeries(LevelSelection.ComputeLevelDistribution(table, staged.maxStars + 1)));

            if (bossChart != null && staged.bossCurveOn) {
                SortedDictionary<int, float> bossTable = staged.BossCurveDiffers(baseline)
                    ? StagedCurveTable(staged.bossGenerator, staged.BossTableSpan, staged.BossTableFor(staged.BossTableSpan))
                    : LiveBossTable();
                bossChart.SetData(ToStarSeries(LevelSelection.ComputeLevelDistribution(bossTable, staged.maxBossLevel + 1)));
            }

            // Auto-tuned caps are shown but not editable: they follow Max stars, and a typed value would be overwritten
            // by the next move of that slider.
            foreach (BiomeCapField cap in biomeCapFields) {
                cap.Field.SetTextWithoutNotify(staged.CapFor(cap.Biome).ToString());
                cap.Field.readOnly = staged.biomeCapAuto;
                cap.Field.interactable = staged.biomeCapAuto == false;
                if (cap.Field.textComponent != null) {
                    Color color = cap.TextColor;
                    cap.Field.textComponent.color = staged.biomeCapAuto ? new Color(color.r, color.g, color.b, color.a * 0.5f) : color;
                }
            }

            if (distributionNotesText != null) {
                List<string> notes = new List<string>();
                int minStars = staged.MinStars;
                if (staged.biomeCapOriginals.Keys.Any(b => staged.CapFor(b) < minStars)) {
                    notes.Add(ConfigUI.L("$sls_cfg_distribution_caps_below_min"));
                }
                if (staged.enableConditional) {
                    notes.Add(ConfigUI.L("$sls_cfg_distribution_conditional_raises_note"));
                }
                CreatureLevelSettings live = LevelSystemData.SLE_Level_Settings;
                if ((live?.DefaultLevelupGenerators?.Count ?? 0) > 1 || (live?.DefaultLevelupGeneratorRefs?.Count ?? 0) > 0) {
                    notes.Add(ConfigUI.L("$sls_cfg_distribution_merge_note"));
                }
                string worldNote = WorldCombatModifierNote();
                if (worldNote != null) { notes.Add(worldNote); }
                distributionNotesText.text = string.Join("\n", notes.ToArray());
            }
        }

        // The world's Combat modifier raises star odds and creature size on top of everything on this page, and is set
        // when the world is made rather than here. Read on the client like every global key, so a remote admin sees the
        // server's value. Null when there is nothing to say.
        private static string WorldCombatModifierNote() {
            if (ZoneSystem.instance == null) {
                return ConfigUI.L("$sls_cfg_dist_world_note_menu");
            }
            float levelUp = WorldRates.LevelUpRate;
            // Outdoors: creatures in dungeons and other interiors keep their size, as in vanilla.
            float size = WorldRates.CreatureSizeFactor(Vector3.zero);
            List<string> effects = new List<string>();
            if (Mathf.Approximately(levelUp, 1f) == false) {
                effects.Add(L("$sls_cfg_dist_world_star_chances", levelUp.ToString("0.##", CultureInfo.InvariantCulture)));
            }
            if (Mathf.Approximately(size, 1f) == false) {
                effects.Add(L("$sls_cfg_dist_world_size", size.ToString("0.##", CultureInfo.InvariantCulture)));
            }
            if (effects.Count == 0) { return null; }
            return L("$sls_cfg_dist_world_note", JoinWithAnd(effects));
        }

        // Level keys (stars + 1) to star keys, folding everything at or below level 1 into 0 stars.
        private static List<KeyValuePair<int, float>> ToStarSeries(SortedDictionary<int, float> byLevel) {
            List<KeyValuePair<int, float>> byStar = new List<KeyValuePair<int, float>>();
            foreach (KeyValuePair<int, float> level in byLevel) {
                int star = Mathf.Max(0, level.Key - 1);
                int last = byStar.Count - 1;
                if (last >= 0 && byStar[last].Key == star) {
                    byStar[last] = new KeyValuePair<int, float>(star, byStar[last].Value + level.Value);
                } else {
                    byStar.Add(new KeyValuePair<int, float>(star, level.Value));
                }
            }
            return byStar;
        }

        private static void OnBiomeCapAutoChanged(bool auto) {
            if (staged == null || staged.biomeCapAuto == auto) { return; }
            // Seeded from what auto-tuning is showing right now, before the mode changes.
            if (auto == false) { staged.HoldCapsForManualEditing(); }
            staged.biomeCapAuto = auto;
            RefreshDistribution();
        }

        private static void CommitBiomeCap(Heightmap.Biome biome, string text) {
            if (staged == null || staged.biomeCapAuto) { return; }
            if (int.TryParse(text, out int cap)) {
                staged.biomeCapManual[biome] = Mathf.Clamp(cap, 1, 200);
                staged.biomeCapUnset.Remove(biome);
            }
            // Redrawn either way, so an unreadable or clamped entry snaps back to the cap in force.
            RefreshDistribution();
        }

        private static SortedDictionary<int, float> LiveDefaultTable() {
            return LevelSystemData.SLE_Level_Settings?.DefaultCreatureLevelUpChance ?? LevelSystemData.DefaultConfiguration.DefaultCreatureLevelUpChance;
        }

        // What bosses roll from today: their own configured table, or the creature one when they have none.
        private static SortedDictionary<int, float> LiveBossTable() {
            SortedDictionary<int, float> boss = LevelSystemData.SLE_Level_Settings?.BossCreatureLevelUpChance;
            return boss != null && boss.Count > 0 ? boss : LiveDefaultTable();
        }

        // The staged generator expanded to a threshold table. Table style is expanded here from the STAGED table it reads,
        // since LevelGenerator reads the live settings, with the same Exponential fallback the runtime uses when it has none.
        private static SortedDictionary<int, float> StagedCurveTable(LevelGenerator g, int span, List<float> values) {
            if (g.LevelupCalculationStyle != LevelupCalculationStyle.Table) { return g.GetLevelUpDefinition(); }

            int lowest = Mathf.Min(g.MinLevel, g.MaxLevel);
            if (span < 2) { return new SortedDictionary<int, float> { { lowest, 0f } }; }
            if (values != null && values.Count > 0) {
                SortedDictionary<int, float> table = new SortedDictionary<int, float>();
                for (int i = 0; i < values.Count && i < span; i++) { table[lowest + i] = values[i]; }
                return table;
            }
            return ExponentialFallback(g).GetLevelUpDefinition();
        }

        private static LevelGenerator ExponentialFallback(LevelGenerator g) {
            return new LevelGenerator {
                MinLevel = g.MinLevel,
                MaxLevel = g.MaxLevel,
                LevelUpChance = g.LevelUpChance,
                LevelupCalculationStyle = LevelupCalculationStyle.Exponential,
            };
        }

        private static string FormatThresholds(IEnumerable<float> values) {
            return string.Join(", ", values.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)).ToArray());
        }

        // Shows the staged tables for both curves' current spans and whether they will work. rewriteField is false after a
        // rejected entry, so the typing stays in the box to be corrected. Always both boxes, since a boss curve with no
        // table of its own shows the general one, which the creature box can change.
        private static void RefreshTableRows(bool rewriteField) {
            if (staged == null) { return; }
            staged.tables.TryGetValue(staged.TableSpan, out List<float> creature);
            RefreshCurveTableRows(staged.generator, creature, false, tableThresholdField, tableStatusText, ref tablePrefillText, rewriteField);
            List<float> boss = staged.BossTableFor(staged.BossTableSpan);
            bool inherited = boss != null && staged.tables.TryGetValue(staged.BossTableSpan, out List<float> general) && boss == general;
            RefreshCurveTableRows(staged.bossGenerator, boss, inherited, bossTableThresholdField, bossTableStatusText, ref bossTablePrefillText, rewriteField);
        }

        // inherited: values belong to the general tables, so they are shown as a suggestion and passing over them is no edit.
        private static void RefreshCurveTableRows(LevelGenerator g, List<float> values, bool inherited, InputField field, Text status, ref string prefill, bool rewriteField) {
            if (field == null || status == null) { return; }
            int span = Mathf.Abs(g.MaxLevel - g.MinLevel) + 1;
            int minStars = Mathf.Max(0, Mathf.Min(g.MinLevel, g.MaxLevel) - 1);
            int maxStars = Mathf.Max(0, Mathf.Max(g.MinLevel, g.MaxLevel) - 1);
            bool hasTable = values != null && values.Count > 0;

            if (rewriteField) {
                if (hasTable) {
                    prefill = inherited ? FormatThresholds(values) : null;
                    field.SetTextWithoutNotify(FormatThresholds(values));
                } else {
                    prefill = span >= 2 ? FormatThresholds(ExponentialFallback(g).GetLevelUpDefinition().Values) : "";
                    field.SetTextWithoutNotify(prefill);
                }
            }

            if (span < 2) {
                SetTableStatus(status, ConfigUI.L("$sls_cfg_table_single_level"), false);
            } else if (hasTable == false) {
                SetTableStatus(status, L("$sls_cfg_table_none", span), false);
            } else if (IsStrictlyDecreasing(values) == false) {
                SetTableStatus(status, ConfigUI.L("$sls_cfg_table_not_decreasing"), false);
            } else {
                string ok = L("$sls_cfg_table_ok", minStars, maxStars);
                SetTableStatus(status, inherited ? ok + " " + ConfigUI.L("$sls_cfg_table_boss_inherited") : ok, true);
            }
        }

        private static void SetTableStatus(Text status, string message, bool ok) {
            status.text = message;
            status.color = ok ? GUIManager.Instance.ValheimBeige : GUIManager.Instance.ValheimOrange;
        }

        private static bool IsStrictlyDecreasing(List<float> values) {
            for (int i = 1; i < values.Count; i++) {
                if (values[i] >= values[i - 1]) { return false; }
            }
            return true;
        }

        // Parses "30, 15, 5, 0.01" into target's table for that many levels and stages it. The entry count sets the span, so
        // the caller moves its curve's max stars to cover exactly that many levels from its min stars. Null when there is
        // nothing to move: the box holds the untouched suggestion, could not be read, or repeats the table in use.
        private static List<float> StageTableThresholds(string text, string prefill, int currentSpan, Text status, Dictionary<int, List<float>> target) {
            // onEndEdit also fires when the box merely loses focus; suggested values passed over untouched are not a table.
            if (prefill != null && text == prefill) { return null; }
            if (string.IsNullOrWhiteSpace(text)) { RefreshTableRows(true); return null; }

            List<float> values = new List<float>();
            foreach (string token in text.Split(',')) {
                string trimmed = token.Trim();
                if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) == false) {
                    SetTableStatus(status, L("$sls_cfg_table_unreadable", trimmed), false);
                    return null;
                }
                values.Add(value);
            }
            if (values.Count < 2) {
                SetTableStatus(status, ConfigUI.L("$sls_cfg_table_too_few"), false);
                return null;
            }

            bool known = target.TryGetValue(values.Count, out List<float> existing) && existing.Count == values.Count
                && existing.Zip(values, (a, b) => Mathf.Abs(a - b) < 0.0001f).All(same => same);
            // The same table read back from the box (which rounds to 4 places) for the span already in use changes nothing.
            if (known && values.Count == currentSpan) {
                RefreshTableRows(true);
                return null;
            }
            if (known == false) {
                target[values.Count] = values;
            }
            return values;
        }

        private static void CommitTableThresholds(string text) {
            if (staged == null) { return; }
            List<float> values = StageTableThresholds(text, tablePrefillText, staged.TableSpan, tableStatusText, staged.tables);
            if (values == null) { return; }

            int minStars = staged.MinStars;
            staged.generator.MinLevel = minStars + 1;
            int wantedMax = Mathf.Min(200, minStars + values.Count - 1);
            if (maxStarsSlider != null && (int)maxStarsSlider.value != wantedMax) {
                maxStarsSlider.value = wantedMax;   // its listener stores the range and refreshes the rows and chart
            } else {
                staged.maxStars = wantedMax;
                staged.generator.MaxLevel = wantedMax + 1;
            }
            RefreshTableRows(true);
            RefreshDistribution();
        }

        private static void CommitBossTableThresholds(string text) {
            if (staged == null) { return; }
            List<float> values = StageTableThresholds(text, bossTablePrefillText, staged.BossTableSpan, bossTableStatusText, staged.bossTables);
            if (values == null) { return; }

            int minStars = staged.BossMinStars;
            staged.bossGenerator.MinLevel = minStars + 1;
            int wantedMax = Mathf.Min(200, minStars + values.Count - 1);
            if (bossMaxStarsSlider != null && (int)bossMaxStarsSlider.value != wantedMax) {
                bossMaxStarsSlider.value = wantedMax;   // its listener stores the range and refreshes the rows and chart
            } else {
                staged.maxBossLevel = wantedMax;
                staged.bossGenerator.MaxLevel = wantedMax + 1;
            }
            RefreshTableRows(true);
            RefreshDistribution();
        }

        // ------------------------------------------------------------------------------------------------
        //  Health, damage and multiplayer
        // ------------------------------------------------------------------------------------------------

        private static void BuildStatsPage(Transform parent) {
            WithTip(BindSliderRow(parent, "CreatureHp", staged.creatureHpPerLevel, v => { staged.creatureHpPerLevel = v; UpdateExampleMath(); }), Tip(ValConfig.EnemyHealthMultiplier));
            WithTip(BindSliderRow(parent, "CreatureDmg", staged.creatureDmgPerLevel, v => { staged.creatureDmgPerLevel = v; UpdateExampleMath(); }), Tip(ValConfig.EnemyDamageLevelMultiplier));
            WithTip(BindSliderRow(parent, "BossHp", staged.bossHpPerLevel, v => { staged.bossHpPerLevel = v; UpdateExampleMath(); }), Tip(ValConfig.BossEnemyHealthMultiplier));
            WithTip(BindSliderRow(parent, "BossDmg", staged.bossDmgPerLevel, v => { staged.bossDmgPerLevel = v; UpdateExampleMath(); }), Tip(ValConfig.BossEnemyDamageMultiplier));
            WithTip(BindToggleRow(parent, "MpHpToggle", staged.mpHealth, v => { staged.mpHealth = v; UpdateExampleMath(); }), Tip(ValConfig.EnableMultiplayerEnemyHealthScaling));
            WithTip(BindSliderRow(parent, "MpHp", staged.mpHealthMod, v => { staged.mpHealthMod = v; UpdateExampleMath(); }), Tip(ValConfig.MultiplayerEnemyHealthModifier));
            WithTip(BindToggleRow(parent, "MpDmgToggle", staged.mpDamage, v => { staged.mpDamage = v; UpdateExampleMath(); }), Tip(ValConfig.EnableMultiplayerEnemyDamageScaling));
            WithTip(BindSliderRow(parent, "MpDmg", staged.mpDamageMod, v => { staged.mpDamageMod = v; UpdateExampleMath(); }), Tip(ValConfig.MultiplayerEnemyDamageModifier));
            WithTip(BindSliderRow(parent, "MpPlayers", staged.mpRequiredPlayers, v => { staged.mpRequiredPlayers = (int)v; UpdateExampleMath(); }), Tip(ValConfig.MultiplayerScalingRequiredPlayersNearby));

            // Right column - worked examples. The Troll uses Max stars from the distribution page, The Elder the boss cap.
            trollExampleText = BindExampleCard(parent, "TrollCard", "TrophyForestTroll");
            elderExampleText = BindExampleCard(parent, "ElderCard", "TrophyTheElder");
            multiplayerExampleText = Get<Text>(parent, "GroupExample");

            UpdateExampleMath();
        }

        private static void ResetStatsPage() {
            staged.creatureHpPerLevel = DefaultOf(ValConfig.EnemyHealthMultiplier);
            staged.creatureDmgPerLevel = DefaultOf(ValConfig.EnemyDamageLevelMultiplier);
            staged.bossHpPerLevel = DefaultOf(ValConfig.BossEnemyHealthMultiplier);
            staged.bossDmgPerLevel = DefaultOf(ValConfig.BossEnemyDamageMultiplier);
            staged.mpHealth =DefaultOf(ValConfig.EnableMultiplayerEnemyHealthScaling);
            staged.mpHealthMod = DefaultOf(ValConfig.MultiplayerEnemyHealthModifier);
            staged.mpDamage = DefaultOf(ValConfig.EnableMultiplayerEnemyDamageScaling);
            staged.mpDamageMod = DefaultOf(ValConfig.MultiplayerEnemyDamageModifier);
            staged.mpRequiredPlayers = DefaultOf(ValConfig.MultiplayerScalingRequiredPlayersNearby);
        }

        private static Text BindExampleCard(Transform parent, string name, string trophyPrefab) {
            Transform card = Find(parent, name).transform;
            Sprite icon = LoadItemIcon(trophyPrefab);
            Image image = Get<Image>(card, "Icon");
            image.sprite = icon;
            image.gameObject.SetActive(icon != null);
            return Get<Text>(card, "Body");
        }

        // ObjectDB exists on the main menu too (FejdStartup copies it in), so this works before a world is loaded. Jotunn's
        // icon atlas is the fallback, and a missing icon just leaves the card without one.
        private static Sprite LoadItemIcon(string prefabName) {
            try {
                if (ObjectDB.instance != null) {
                    GameObject item = ObjectDB.instance.GetItemPrefab(prefabName);
                    ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
                    if (drop != null && drop.m_itemData != null) {
                        Sprite icon = drop.m_itemData.GetIcon();
                        if (icon != null) { return icon; }
                    }
                }
                return GUIManager.Instance.GetSprite(prefabName);
            } catch (Exception e) {
                Logger.LogDebug($"QuickConfigureTool could not load the {prefabName} icon: {e.Message}");
                return null;
            }
        }

        private static void UpdateExampleMath() {
            if (staged == null) { return; }
            if (trollExampleText != null) {
                trollExampleText.text = FormatCreatureExample(TrollName(), TrollHp, TrollDmg * staged.allBaseDamage, staged.maxStars, staged.creatureHpPerLevel, staged.creatureDmgPerLevel);
            }
            if (elderExampleText != null) {
                elderExampleText.text = FormatCreatureExample(L("$sls_cfg_stats_example_boss", LocalizedOr("$enemy_gdking", "The Elder")), TheElderHP, TheElderDmg * staged.allBaseDamage, staged.maxBossLevel, staged.bossHpPerLevel, staged.bossDmgPerLevel);
            }
            if (multiplayerExampleText != null) {
                multiplayerExampleText.text = FormatMultiplayerExample();
            }
        }

        private static string TrollName() => LocalizedOr("$enemy_troll", "Troll");

        private static string Stars(int stars) => stars == 1 ? ConfigUI.L("$sls_cfg_stars_one") : L("$sls_cfg_stars_many", stars);

        // Same shape as the runtime: base * (1 + per-star multiplier * stars), for health and for damage.
        private static string FormatCreatureExample(string name, float baseHp, float baseDmg, int maxStars, float hpMul, float dmgMul) {
            maxStars = Mathf.Max(0, maxStars);
            int medStars = Mathf.CeilToInt(maxStars / 2f);
            string Line(int stars) => L("$sls_cfg_stats_example_line", Stars(stars), (baseHp * (1f + hpMul * stars)).ToString("0"), (baseDmg * (1f + dmgMul * stars)).ToString("0"));
            return $"{L("$sls_cfg_stats_example_base", name, baseHp.ToString("0"), baseDmg.ToString("0"))}\n{Line(0)}\n{Line(medStars)}\n{Line(maxStars)}";
        }

        // Mirrors MultiplayerDamageMod: enemies take max(1 - players * healthMod, MinDamageTaken) damage and deal
        // 1 + (1 + players) * damageMod, once enough players are nearby. Valheim Plus's difficulty, when on, multiplies
        // onto both from its live settings, as it is not part of what this page stages.
        private static string FormatMultiplayerExample() {
            bool valheimPlus = Compatibility.TryGetValheimPlusDifficulty(out float valheimPlusDamagePerPlayer, out float valheimPlusHealthPerPlayer);
            if (staged.mpHealth == false && staged.mpDamage == false && valheimPlus == false) {
                return ConfigUI.L("$sls_cfg_stats_mp_off");
            }
            int players = Mathf.Max(1, staged.mpRequiredPlayers);
            float taken = staged.mpHealth ? Mathf.Max(1f - players * staged.mpHealthMod, ValConfig.MultiplayerEnemyMinDamageTaken.Value) : 1f;
            float dealt = staged.mpDamage ? 1f + (1f + players) * staged.mpDamageMod : 1f;
            if (valheimPlus) {
                taken *= MultiplayerHealthMod.ValheimPlusDamageTaken(players, valheimPlusHealthPerPlayer);
                dealt *= MultiplayerDamageMod.ValheimPlusDamageDealt(players, valheimPlusDamagePerPlayer);
            }
            taken = Mathf.Max(taken, 0.01f);
            int stars = Mathf.Max(0, staged.maxStars);
            float trollHp = TrollHp * (1f + staged.creatureHpPerLevel * stars);
            float trollDmg = TrollDmg * staged.allBaseDamage * (1f + staged.creatureDmgPerLevel * stars);
            return L("$sls_cfg_stats_mp_group", players, taken.ToString("0.00"), (1f / taken).ToString("0.0"), dealt.ToString("0.00")) + "\n" +
                   L("$sls_cfg_stats_mp_troll", Stars(stars), TrollName(), (trollHp / taken).ToString("0"), (trollDmg * dealt).ToString("0")) +
                   (valheimPlus ? "\n" + ConfigUI.L("$sls_cfg_stats_mp_valheim_plus") : "");
        }

        // ------------------------------------------------------------------------------------------------
        //  Modifiers
        // ------------------------------------------------------------------------------------------------

        private static void BuildModifiersPage(Transform parent) {
            WithTip(BindSliderRow(parent, "CreatureMaxMajor", staged.maxMajor, v => staged.maxMajor = (int)v, max: Mathf.Max(6f, staged.maxMajor)), Tip(ValConfig.MaxMajorModifiersPerCreature));
            WithTip(BindSliderRow(parent, "CreatureMaxMinor", staged.maxMinor, v => staged.maxMinor = (int)v, max: Mathf.Max(6f, staged.maxMinor)), Tip(ValConfig.MaxMinorModifiersPerCreature));
            WithTip(BindSliderRow(parent, "CreatureMaxBoss", staged.maxBossOnCreature, v => staged.maxBossOnCreature = (int)v, max: Mathf.Max(6f, staged.maxBossOnCreature)), Tip(ValConfig.MaxBossModifiersPerCreature));
            WithTip(BindSliderRow(parent, "CreatureChanceMajor", staged.chanceMajor, v => staged.chanceMajor = v), Tip(ValConfig.ChanceMajorModifier));
            WithTip(BindSliderRow(parent, "CreatureChanceMinor", staged.chanceMinor, v => staged.chanceMinor = v), Tip(ValConfig.ChanceMinorModifier));
            WithTip(BindSliderRow(parent, "CreatureChanceBoss", staged.chanceBossOnCreature, v => staged.chanceBossOnCreature = v), Tip(ValConfig.ChanceOfBossModifierOnCreature));
            WithTip(BindToggleRow(parent, "LimitToStars", staged.limitToStarLevel, v => staged.limitToStarLevel = v), Tip(ValConfig.LimitCreatureModifiersToCreatureStarLevel));
            WithTip(BindToggleRow(parent, "BossesCanHave", staged.enableBossMods, v => staged.enableBossMods = v), Tip(ValConfig.EnableBossModifiers));
            WithTip(BindSliderRow(parent, "BossMaxBoss", staged.maxBossMods, v => staged.maxBossMods = (int)v, max: Mathf.Max(6f, staged.maxBossMods)), Tip(ValConfig.MaxBossModifiersPerBoss));
            WithTip(BindSliderRow(parent, "BossChanceBoss", staged.chanceBoss, v => staged.chanceBoss = v), Tip(ValConfig.ChanceOfBossModifier));
            WithTip(BindSliderRow(parent, "BossMaxMajor", staged.maxMajorOnBoss, v => staged.maxMajorOnBoss = (int)v, max: Mathf.Max(6f, staged.maxMajorOnBoss)), Tip(ValConfig.MaxMajorModifiersPerBoss));
            WithTip(BindSliderRow(parent, "BossChanceMajor", staged.chanceMajorOnBoss, v => staged.chanceMajorOnBoss = v), Tip(ValConfig.ChanceOfMajorModifierOnBoss));
            WithTip(BindSliderRow(parent, "BossMaxMinor", staged.maxMinorOnBoss, v => staged.maxMinorOnBoss = (int)v, max: Mathf.Max(6f, staged.maxMinorOnBoss)), Tip(ValConfig.MaxMinorModifiersPerBoss));
            WithTip(BindSliderRow(parent, "BossChanceMinor", staged.chanceMinorOnBoss, v => staged.chanceMinorOnBoss = v), Tip(ValConfig.ChanceOfMinorModifierOnBoss));
            WithTip(BindSliderRow(parent, "MaxPrefixes", staged.prefixLimit, v => staged.prefixLimit = (int)v, max: Mathf.Max(6f, staged.prefixLimit)), Tip(ValConfig.LimitCreatureModifierPrefixes));
            WithTip(BindToggleRow(parent, "MinorFirst", staged.minorFirst, v => staged.minorFirst = v), Tip(ValConfig.MinorModifiersFirstInName));
            WithTip(BindEnumRow(parent, "IconStyle", DisplayStyleOptions, (int)staged.displayStyle, i => staged.displayStyle = (ModifierDisplayStyle)i), Tip(ValConfig.ModifierIconDisplayStyle));

            // Right side - scrollable list of every modifier defined in Modifiers.yaml, grouped by category,
            // each with an enable/disable toggle and a brief description.
            Transform content = ScrollContent(parent, "ModifierList");
            if (content != null) {
                AddModifierCategory(content, "$sls_cfg_mods_header_boss", ModifierType.Boss, staged.modifierSource?.BossModifiers);
                AddModifierCategory(content, "$sls_cfg_mods_header_major", ModifierType.Major, staged.modifierSource?.MajorModifiers);
                AddModifierCategory(content, "$sls_cfg_mods_header_minor", ModifierType.Minor, staged.modifierSource?.MinorModifiers);
            }
        }

        private static void ResetModifiersPage() {
            staged.maxMajor = DefaultOf(ValConfig.MaxMajorModifiersPerCreature);
            staged.maxMinor = DefaultOf(ValConfig.MaxMinorModifiersPerCreature);
            staged.chanceMajor = DefaultOf(ValConfig.ChanceMajorModifier);
            staged.chanceMinor = DefaultOf(ValConfig.ChanceMinorModifier);
            staged.maxBossOnCreature = DefaultOf(ValConfig.MaxBossModifiersPerCreature);
            staged.chanceBossOnCreature = DefaultOf(ValConfig.ChanceOfBossModifierOnCreature);
            staged.limitToStarLevel = DefaultOf(ValConfig.LimitCreatureModifiersToCreatureStarLevel);
            staged.enableBossMods = DefaultOf(ValConfig.EnableBossModifiers);
            staged.chanceBoss = DefaultOf(ValConfig.ChanceOfBossModifier);
            staged.maxBossMods = DefaultOf(ValConfig.MaxBossModifiersPerBoss);
            staged.maxMajorOnBoss = DefaultOf(ValConfig.MaxMajorModifiersPerBoss);
            staged.chanceMajorOnBoss = DefaultOf(ValConfig.ChanceOfMajorModifierOnBoss);
            staged.maxMinorOnBoss = DefaultOf(ValConfig.MaxMinorModifiersPerBoss);
            staged.chanceMinorOnBoss = DefaultOf(ValConfig.ChanceOfMinorModifierOnBoss);
            staged.prefixLimit = DefaultOf(ValConfig.LimitCreatureModifierPrefixes);
            staged.minorFirst = DefaultOf(ValConfig.MinorModifiersFirstInName);
            staged.displayStyle = Enum.TryParse(DefaultOf(ValConfig.ModifierIconDisplayStyle), out ModifierDisplayStyle style) ? style : ModifierDisplayStyle.Stars;

            // Each listed modifier takes its shipped on/off. One the shipped file does not have (added by hand or by
            // another mod) has no default to go back to, so it keeps its setting.
            CreatureModifierCollection shipped = ShippedDefaults(YamlConfigManager.ModifierSettings);
            ResetModifierToggles(ModifierType.Boss, staged.modifierSource?.BossModifiers, shipped?.BossModifiers);
            ResetModifierToggles(ModifierType.Major, staged.modifierSource?.MajorModifiers, shipped?.MajorModifiers);
            ResetModifierToggles(ModifierType.Minor, staged.modifierSource?.MinorModifiers, shipped?.MinorModifiers);
        }

        private static void ResetModifierToggles(ModifierType type, Dictionary<string, CreatureModifierConfiguration> listed, Dictionary<string, CreatureModifierConfiguration> shipped) {
            if (listed == null || shipped == null) { return; }
            HashSet<string> on = staged.modifierOn[type];
            foreach (string name in listed.Keys) {
                if (shipped.TryGetValue(name, out CreatureModifierConfiguration config) == false || config == null) { continue; }
                if (config.Enabled) { on.Add(name); } else { on.Remove(name); }
            }
        }

        // Adds a category header followed by one toggle row per modifier defined in that category.
        private static void AddModifierCategory(Transform content, string label, ModifierType type, Dictionary<string, CreatureModifierConfiguration> dict) {
            if (dict == null || dict.Count == 0) { return; }
            SetText(Spawn("SLS_QC_Row_ModifierHeader", content).transform, "Label", label);
            HashSet<string> enabled = staged.modifierOn[type];
            foreach (string name in dict.Keys.OrderBy(n => n)) {
                string modName = name;   // capture for the closure
                string desc = ModifierDescriptions.TryGetValue(modName, out string d) ? d : "";
                // Keyed by the internal name, which is what Modifiers.yaml holds, with the name creatures carry in game
                // beside it: an admin who has only seen "Burning" in a creature's name would not otherwise find "Fire".
                string rowLabel = Prettify(modName);
                string inGame = ModifierInGameName(modName);
                if (inGame.Length > 0 && string.Equals(inGame, rowLabel, StringComparison.OrdinalIgnoreCase) == false) {
                    rowLabel += $"  {MutedColorTag}({inGame})</color>";
                }
                AddToggleEntry(content, rowLabel, desc, enabled.Contains(modName), on => {
                    if (on) { staged.modifierOn[type].Add(modName); }
                    else { staged.modifierOn[type].Remove(modName); }
                });
            }
        }

        // A single list line inside a scroll view: enable toggle on the left, name and a brief description to its right.
        private static void AddToggleEntry(Transform content, string name, string description, bool enabled, Action<bool> onChange) {
            Transform row = Spawn("SLS_QC_Row_ModifierEntry", content).transform;
            BindToggle(row, "Toggle", enabled, onChange);
            SetText(row, "Name", name);
            SetText(row, "Description", description);
        }

        // "ResistPierce" -> "Resist Pierce", "BossSummoner" -> "Boss Summoner".
        private static string Prettify(string name) => Regex.Replace(name, "(\\B[A-Z])", " $1");

        // What this modifier calls a creature in game: the prefix it puts in front of the name ("Burning Greydwarf"), or
        // its suffix for one that only ever takes a suffix ("Greydwarf of the Flames"). Localized, so it reads as it does
        // in the world. Empty when the modifier names nothing, or when the language file has no translation for its
        // token - the list then shows the internal name on its own rather than a raw token.
        private static string ModifierInGameName(string modName) {
            if (CreatureModifiersData.ModifierDefinitions.TryGetValue(modName, out CreatureModifierDefinition def) == false || def == null) { return ""; }
            string token = def.NamingConvention == NameSelectionStyle.RandomLast ? def.NameSuffix : def.NamePrefix;
            if (string.IsNullOrEmpty(token)) { token = def.NameSuffix; }
            if (string.IsNullOrEmpty(token)) { return ""; }

            string localized = ConfigUI.L(token);
            // Valheim answers with "[token]" for a token it has no translation for.
            if (string.IsNullOrEmpty(localized) || localized.StartsWith("[") || localized.Contains("$")) { return ""; }
            return localized;
        }

        // ------------------------------------------------------------------------------------------------
        //  Nemesis
        // ------------------------------------------------------------------------------------------------

        private static void BuildNemesisPage(Transform parent) {
            nemesisWarningText = Get<Text>(parent, "Warning");

            // Left - core settings and the score system.
            WithTip(BindToggleRow(parent, "Enable", staged.enableNemesis, v => staged.enableNemesis = v), Tip(ValConfig.EnableNemesisSystem));
            WithTip(BindToggleRow(parent, "Remote", staged.enableNemesisRemote, v => staged.enableNemesisRemote = v), Tip("EnableNemesisRemoteSpawning", "$sls_cfg_tip_nem_remote"));
            WithTip(BindSliderRow(parent, "Cooldown", staged.nemCooldown, v => staged.nemCooldown = v, Mathf.Min(0f, staged.nemCooldown), Mathf.Max(120f, staged.nemCooldown)), Tip("NemesisActionCooldownSeconds", "$sls_cfg_tip_nem_cooldown"));
            WithTip(BindSliderRow(parent, "Influence", staged.nemInfluence, v => staged.nemInfluence = v, Mathf.Min(0f, staged.nemInfluence), Mathf.Max(1000f, staged.nemInfluence)), Tip("NemesisInfluenceRadius", "$sls_cfg_tip_nem_influence"));
            WithTip(BindSliderRow(parent, "MinSpawn", staged.nemMinSpawn, v => staged.nemMinSpawn = v, Mathf.Min(0f, staged.nemMinSpawn), Mathf.Max(500f, staged.nemMinSpawn)), Tip("NemesisMinSpawnDistance", "$sls_cfg_tip_nem_min_spawn"));
            WithTip(BindSliderRow(parent, "NeutralScore", staged.neutralScore, v => { staged.neutralScore = v; RefreshNemesisDescriptions(); }, Mathf.Min(0f, staged.neutralScore), Mathf.Max(20000f, staged.neutralScore), IsWhole(staged.neutralScore)), Tip("NeutralScore", "$sls_cfg_tip_nem_neutral_score"));
            WithTip(BindSliderRow(parent, "MinScore", staged.minScore, v => { staged.minScore = v; RefreshNemesisDescriptions(); }, Mathf.Min(0f, staged.minScore), Mathf.Max(20000f, staged.minScore), IsWhole(staged.minScore)), Tip("MinScore", "$sls_cfg_tip_nem_min_score"));
            WithTip(BindSliderRow(parent, "MaxScore", staged.maxScore, v => { staged.maxScore = v; RefreshNemesisDescriptions(); }, Mathf.Min(0f, staged.maxScore), Mathf.Max(20000f, staged.maxScore), IsWhole(staged.maxScore)), Tip("MaxScore", "$sls_cfg_tip_nem_max_score"));
            WithTip(BindSliderRow(parent, "Decay", staged.decayPerUpdate, v => staged.decayPerUpdate = v, Mathf.Min(0f, staged.decayPerUpdate), Mathf.Max(2000f, staged.decayPerUpdate), IsWhole(staged.decayPerUpdate)), Tip("DecayPerUpdate", "$sls_cfg_tip_nem_decay"));
            WithTip(BindSliderRow(parent, "Interval", staged.scoreInterval, v => staged.scoreInterval = v, Mathf.Min(1f, staged.scoreInterval), Mathf.Max(120f, staged.scoreInterval), IsWhole(staged.scoreInterval)), Tip("ScoreIntervalSeconds", "$sls_cfg_tip_nem_interval"));
            WithTip(BindSliderRow(parent, "BossKill", staged.bossKillBonus, v => staged.bossKillBonus = v, Mathf.Min(0f, staged.bossKillBonus), Mathf.Max(5000f, staged.bossKillBonus), IsWhole(staged.bossKillBonus)), Tip("BossKillBonus", "$sls_cfg_tip_nem_boss_kill"));
            WithTip(BindSliderRow(parent, "Death", staged.deathReduction, v => staged.deathReduction = v, Mathf.Min(0f, staged.deathReduction), Mathf.Max(5000f, staged.deathReduction), IsWhole(staged.deathReduction)), Tip("DeathScoreReduction", "$sls_cfg_tip_nem_death"));

            // Right - every configured action, in the order they are checked.
            Transform right = ScrollContent(parent, "Actions");
            foreach (KeyValuePair<string, StagedNemesisAction> action in staged.nemesisActions) {
                AddNemesisActionEntry(right, action.Key, action.Value);
            }

            RefreshNemesisDescriptions();
        }

        private static void ResetNemesisPage() {
            staged.enableNemesis = DefaultOf(ValConfig.EnableNemesisSystem);
            NemesisConfiguration shipped = ShippedDefaults(YamlConfigManager.NemesisSettings);
            // Read as the snapshot reads it: on only when both switches ship on.
            staged.enableNemesisRemote = DefaultOf(ValConfig.EnableNemesisRemoteSpawning) && shipped?.RemoteSpawning?.Enabled == true;
            if (shipped == null) { return; }
            staged.nemCooldown = shipped.NemesisActionCooldownSeconds;
            staged.nemInfluence = shipped.NemesisInfluenceRadius;
            staged.nemMinSpawn = shipped.NemesisMinSpawnDistance;
            NemesisScore score = shipped.ScoreSystem ?? new NemesisScore();
            staged.neutralScore = score.NeutralScore;
            staged.minScore = score.MinScore;
            staged.maxScore = score.MaxScore;
            staged.decayPerUpdate = score.DecayPerUpdate;
            staged.scoreInterval = score.ScoreIntervalSeconds;
            staged.bossKillBonus = score.BossKillBonus;
            staged.deathReduction = score.DeathScoreReduction;
            // Actions the shipped file does not have keep their settings, as a modifier the shipped file lacks does.
            Dictionary<string, NemesisChanceEntry> shippedOps = shipped.ChanceChanges?.CreatureOps;
            if (shippedOps == null) { return; }
            foreach (KeyValuePair<string, StagedNemesisAction> action in staged.nemesisActions) {
                if (shippedOps.TryGetValue(action.Key, out NemesisChanceEntry op) == false || op == null) { continue; }
                action.Value.Enabled = op.Enabled;
                action.Value.Chance = op.Chance;
                action.Value.Threshold = op.ScoreThreshold;
                action.Value.LevelBonus = op.LevelBonus;
            }
        }

        private static void AddNemesisActionEntry(Transform content, string name, StagedNemesisAction action) {
            Transform entry = Spawn("SLS_QC_Row_NemesisAction", content).transform;

            BindToggle(entry, "Toggle", action.Enabled, on => { action.Enabled = on; RefreshNemesisDescriptions(); });
            SetText(entry, "Name", Prettify(name));
            Text tag = Get<Text>(entry, "Tag");
            Text description = Get<Text>(entry, "Description");

            WithTip(BindSliderRow(entry, "Chance", action.Chance, v => { action.Chance = v; RefreshNemesisDescriptions(); }), Tip("Chance", "$sls_cfg_tip_nem_chance"));
            // Same range as the score sliders, widened if the file already holds something outside it, so building the page
            // never clamps a configured value.
            WithTip(BindSliderRow(entry, "Threshold", action.Threshold, v => { action.Threshold = v; RefreshNemesisDescriptions(); }, Mathf.Min(0f, action.Threshold), Mathf.Max(20000f, action.Threshold), IsWhole(action.Threshold)), Tip("ScoreThreshold", "$sls_cfg_tip_nem_threshold"));
            WithTip(BindSliderRow(entry, "LevelBonus", action.LevelBonus, v => { action.LevelBonus = (int)v; RefreshNemesisDescriptions(); }, Mathf.Min(-10, action.LevelBonus), Mathf.Max(10, action.LevelBonus)), Tip("LevelBonus", "$sls_cfg_tip_nem_level_bonus"));

            nemesisActionViews.Add(new NemesisActionView { Action = action, Tag = tag, Description = description });
        }

        private static void RefreshNemesisDescriptions() {
            if (staged == null) { return; }
            foreach (NemesisActionView view in nemesisActionViews) {
                view.Description.text = DescribeNemesisAction(view.Action, out string tag, out Color tagColor);
                view.Tag.text = tag;
                view.Tag.color = tagColor;
            }
            if (nemesisWarningText != null) {
                bool ordered = staged.minScore <= staged.neutralScore && staged.neutralScore <= staged.maxScore;
                nemesisWarningText.text = ordered ? "" : ConfigUI.L("$sls_cfg_nemesis_score_order");
            }
        }

        // One line of what an action does, when, and how likely, generated from its staged values so it follows the
        // sliders. Mirrors NemesisActions: a threshold above Neutral needs the score at or above it, below Neutral at or
        // below it, and equal to Neutral always passes. Spawn actions also skip a threshold of 0; level changes do not.
        private static string DescribeNemesisAction(StagedNemesisAction action, out string tag, out Color tagColor) {
            NemesisChanceEntry source = action.Source;
            float neutral = staged.neutralScore;
            bool isLevelChange = source.Action == NemesisAction.ChangeLevel;
            bool anyScore = action.Threshold == neutral || (isLevelChange == false && action.Threshold == 0f);
            bool harderSide = action.Threshold > neutral;
            string threshold = action.Threshold.ToString("0");
            string gate = anyScore ? ConfigUI.L("$sls_cfg_nem_gate_any") : L(harderSide ? "$sls_cfg_nem_gate_above" : "$sls_cfg_nem_gate_below", threshold);

            string effect;
            switch (source.Action) {
                case NemesisAction.ChangeLevel:
                    if (action.LevelBonus == 0) {
                        effect = ConfigUI.L("$sls_cfg_nem_effect_no_change");
                        tag = ConfigUI.L("$sls_cfg_nem_tag_no_effect");
                        tagColor = InactiveColor;
                    } else {
                        effect = L("$sls_cfg_nem_effect_level", LevelDelta(action.LevelBonus));
                        tag = ConfigUI.L(action.LevelBonus > 0 ? "$sls_cfg_nem_tag_harder" : "$sls_cfg_nem_tag_easier");
                        tagColor = action.LevelBonus > 0 ? HarderColor : EasierColor;
                    }
                    break;
                case NemesisAction.Spawn: {
                        effect = L("$sls_cfg_nem_effect_spawn", DescribeSpawns(source.SpawnConfig)) + LevelSuffix(action.LevelBonus);
                        bool reward = source.SpawnConfig != null && source.SpawnConfig.Count > 0 && source.SpawnConfig.All(sp => sp != null && sp.CustomLoot != null && sp.CustomLoot.Count > 0);
                        tag = ConfigUI.L(reward ? "$sls_cfg_nem_tag_reward" : "$sls_cfg_nem_tag_harder");
                        tagColor = reward ? GUIManager.Instance.ValheimYellow : HarderColor;
                        break;
                    }
                case NemesisAction.SpawnMiniboss:
                    effect = ConfigUI.L("$sls_cfg_nem_effect_miniboss") + LevelSuffix(action.LevelBonus);
                    tag = ConfigUI.L("$sls_cfg_nem_tag_nemesis");
                    tagColor = NemesisColor;
                    break;
                default:
                    effect = L("$sls_cfg_nem_effect_unimplemented", source.Action);
                    tag = ConfigUI.L("$sls_cfg_nem_tag_inactive");
                    tagColor = InactiveColor;
                    break;
            }

            string text = L("$sls_cfg_nem_action_line", effect, gate, (action.Chance * 100f).ToString("0.#"));
            string conditions = DescribeConditions(source);
            if (conditions.Length > 0) { text += " " + conditions; }

            if (anyScore == false && (action.Threshold < staged.minScore || action.Threshold > staged.maxScore)) {
                text += $" {WarningColorTag}{ConfigUI.L("$sls_cfg_nem_never_fires")}</color>";
            }
            if (isLevelChange && anyScore == false && action.LevelBonus != 0 && (action.LevelBonus > 0) != harderSide) {
                text += $" {WarningColorTag}{ConfigUI.L(action.LevelBonus > 0 ? "$sls_cfg_nem_raises_struggling" : "$sls_cfg_nem_lowers_doing_well")}</color>";
            }
            if (action.Enabled == false) {
                tag = ConfigUI.L("$sls_cfg_tag_off");
                tagColor = InactiveColor;
            }
            return text;
        }

        // "+1 level", "-3 levels".
        private static string LevelDelta(int levelBonus) {
            int amount = Mathf.Abs(levelBonus);
            if (amount == 1) { return ConfigUI.L(levelBonus > 0 ? "$sls_cfg_levels_plus_one" : "$sls_cfg_levels_minus_one"); }
            return L(levelBonus > 0 ? "$sls_cfg_levels_plus" : "$sls_cfg_levels_minus", amount);
        }

        private static string LevelSuffix(int levelBonus) {
            return levelBonus == 0 ? "" : $" ({LevelDelta(levelBonus)})";
        }

        private static string DescribeSpawns(List<NemesisSpawn> spawns) {
            if (spawns == null || spawns.Count == 0) { return ConfigUI.L("$sls_cfg_nem_spawns_nothing"); }
            List<string> parts = new List<string>();
            foreach (IGrouping<string, NemesisSpawn> group in spawns.Where(sp => sp != null).GroupBy(sp => string.IsNullOrEmpty(sp.CustomName) ? PrettyPrefab(sp.Prefab) : sp.CustomName)) {
                parts.Add($"{group.Sum(sp => Mathf.Max(1, sp.SpawnGroupSize))}x {group.Key}");
            }
            return string.Join(", ", parts.ToArray());
        }

        // Level changes ignore biomes, keys and player state, so only spawns get conditions.
        private static string DescribeConditions(NemesisChanceEntry source) {
            if (source.Action == NemesisAction.ChangeLevel) { return ""; }
            List<string> parts = new List<string>();
            List<Heightmap.Biome> allowed = source.AllowedBiomes?.Where(b => b != Heightmap.Biome.None).ToList();
            if (allowed != null && allowed.Count > 0) { parts.Add(L("$sls_cfg_nem_cond_in", string.Join(", ", allowed.Select(BiomeName).ToArray()))); }
            List<Heightmap.Biome> denied = source.DeniedBiomes?.Where(b => b != Heightmap.Biome.None).ToList();
            if (denied != null && denied.Count > 0) { parts.Add(L("$sls_cfg_nem_cond_not_in", string.Join(", ", denied.Select(BiomeName).ToArray()))); }
            if (source.RequiredGlobalKeys != null && source.RequiredGlobalKeys.Count > 0) {
                parts.Add(L("$sls_cfg_nem_cond_after", string.Join(", ", source.RequiredGlobalKeys.Select(BossKeyName).ToArray())));
            }
            if (source.NotRequiredGlobalKeys != null && source.NotRequiredGlobalKeys.Count > 0) {
                // Blocked once any of these is defeated, so the earliest one in progression is the one that matters.
                List<string> order = LevelSystemData.VanillaBossKeyOrder;
                string earliest = source.NotRequiredGlobalKeys
                    .OrderBy(k => { int i = order.FindIndex(o => string.Equals(o, k, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; })
                    .First();
                parts.Add(L("$sls_cfg_nem_cond_only_before", BossKeyName(earliest)));
            }
            return parts.Count == 0 ? "" : "(" + string.Join("; ", parts.ToArray()) + ")";
        }

        private static string BossKeyName(string key) {
            return key != null && BossKeyNames.TryGetValue(key, out string token) ? LocalizedOr(token, key) : key;
        }

        // The biome's name as the map shows it. A name in a config file that no mod has made into a biome says so, since
        // whatever is set for it does nothing.
        private static string BiomeName(Heightmap.Biome biome) {
            if (BiomeRegistry.IsVanilla(biome)) {
                return LocalizedOr("$biome_" + biome.ToString().ToLowerInvariant(), biome.ToString());
            }
            string name = BiomeRegistry.DisplayName(biome);
            return BiomeRegistry.Resolve(biome, out _) ? name : L("$sls_cfg_biome_not_loaded", name);
        }

        // "army_eikthyr" -> "Army Eikthyr", "gjall_ambush" -> "Gjall Ambush".
        private static string PrettifyRaidName(string name) {
            if (string.IsNullOrEmpty(name)) { return name; }
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' '));
        }

        // "Draugr_Elite" -> "Draugr Elite", "GoblinShaman" -> "Goblin Shaman".
        private static string PrettyPrefab(string prefab) {
            if (string.IsNullOrEmpty(prefab)) { return "?"; }
            return Prettify(prefab.Replace('_', ' '));
        }
    }
}
