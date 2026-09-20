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

namespace StarLevelSystem.modules.UI {
    // The pages of the quick configure panel. Each Build* method lays its widgets out inside a PageW x PageH page root
    // and binds them to the static `staged` config; the shell in QuickConfigureTool.cs owns navigation and saving.
    internal static partial class QuickConfigureTool {

        // Sample creatures for the stat examples | TODO: allow selecting different creature examples?
        private const float TrollHp = 600f;
        private const float TrollDmg = 70f;
        private const float TheElderHP = 2500f;
        private const float TheElderDmg = 60f;

        private static readonly string[] CalcStyleOptions = Enum.GetNames(typeof(LevelupCalculationStyle));
        private static readonly string[] DisplayStyleOptions = Enum.GetNames(typeof(ModifierDisplayStyle));

        private static readonly string[] WelcomeBullets = {
            "$sls_cfg_welcome_bullet_levels",
            "$sls_cfg_welcome_bullet_scaling",
            "$sls_cfg_welcome_bullet_modifiers",
            "$sls_cfg_welcome_bullet_raids",
            "$sls_cfg_welcome_bullet_nemesis",
            "$sls_cfg_welcome_bullet_loot",
            "$sls_cfg_welcome_bullet_resets",
        };

        // Brief, player-facing descriptions for each modifier (see Package/README.md), keyed by ModifierNames.
        private static readonly Dictionary<string, string> ModifierDescriptions = new Dictionary<string, string>() {
            { "BossSummoner", "Summons minion creatures at regular intervals." },
            { "SoulEater", "Grows stronger as nearby creatures die; self-heals." },
            { "LifeLink", "Redirects some damage taken to a nearby creature." },
            { "Splitter", "Spawns replacement creatures when it dies." },
            { "Lootbags", "Tankier and faster; drops extra loot." },
            { "Fire", "Adds fire damage to its attacks." },
            { "Frost", "Adds frost damage to its attacks." },
            { "Poison", "Adds poison damage to its attacks." },
            { "Lightning", "Adds lightning damage to its attacks." },
            { "FireNova", "Explodes in fire on death, damaging nearby targets." },
            { "FrostNova", "Explodes in frost on death, damaging nearby targets." },
            { "PoisonNova", "Explodes in poison on death, damaging nearby targets." },
            { "LightningNova", "Explodes in lightning on death, damaging nearby targets." },
            { "Evolving", "Gains a level after enough kills." },
            { "ResistSlash", "Reduces damage taken from slash." },
            { "ResistBlunt", "Reduces damage taken from blunt." },
            { "ResistPierce", "Reduces damage taken from pierce (e.g. arrows)." },
            { "ResistFire", "Reduces damage taken from fire." },
            { "ResistFrost", "Reduces damage taken from frost." },
            { "ResistPoison", "Reduces damage taken from poison." },
            { "ResistSpirit", "Reduces damage taken from spirit." },
            { "Alert", "Increases the creature's hearing range." },
            { "Big", "Increases the creature's size." },
            { "Fast", "Increases the creature's movement speed." },
            { "StaminaDrain", "Its attacks drain your stamina. Dodging avoids it; blocking or parrying lessens it." },
            { "EitrDrain", "Its attacks drain your eitr. Dodging avoids it; blocking or parrying lessens it." },
            { "Brutal", "Increases the creature's attack speed." },
            { "ElementalChaos", "Adds random elemental damage on each hit." },
        };

        private static readonly Dictionary<string, string> BossKeyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "defeated_eikthyr", "Eikthyr" },
            { "defeated_gdking", "The Elder" },
            { "defeated_bonemass", "Bonemass" },
            { "defeated_dragon", "Moder" },
            { "defeated_goblinking", "Yagluth" },
            { "defeated_queen", "The Queen" },
            { "defeated_fader", "Fader" },
        };

        private static readonly Color HarderColor = new Color(0.97f, 0.47f, 0.42f);
        private static readonly Color EasierColor = new Color(0.55f, 0.85f, 0.5f);
        private static readonly Color NemesisColor = new Color(0.8f, 0.58f, 0.95f);
        private static readonly Color InactiveColor = new Color(0.6f, 0.6f, 0.6f);
        private const string WarningColorTag = "<color=#FBBF24>";

        private static Sprite DistanceExample;
        private static Sprite ZoneExample;

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
            trollExampleText = null;
            elderExampleText = null;
            multiplayerExampleText = null;
            nemesisWarningText = null;
            nemesisActionViews.Clear();
            ClearRaidPageReferences();
            ClearLocationResetReferences();
        }

        // ------------------------------------------------------------------------------------------------
        //  Welcome (first-time setup only)
        // ------------------------------------------------------------------------------------------------

        private static void BuildWelcomePage(Transform parent) {
            const float TextX = 30f;
            float w = PageW - 2 * TextX;
            string bullets = string.Join("\n", WelcomeBullets.Select(token => "•  " + ConfigUI.L(token)).ToArray());

            List<GameObject> rows = new List<GameObject> {
                ConfigUI.AddTextRow(parent, w, 40f, "$sls_cfg_welcome_title", 22, GUIManager.Instance.ValheimYellow, TextAnchor.MiddleCenter),
                ConfigUI.AddTextRow(parent, w, 44f, "$sls_cfg_welcome_intro", 16, GUIManager.Instance.ValheimBeige),
                ConfigUI.AddTextRow(parent, w, 240f, bullets, 15, GUIManager.Instance.ValheimBeige),
                ConfigUI.AddDividerRow(parent, w),
                ConfigUI.AddTextRow(parent, w, 44f, "$sls_cfg_welcome_skip", 16, GUIManager.Instance.ValheimOrange),
                ConfigUI.AddTextRow(parent, w, 44f, "$sls_cfg_welcome_later", 14, GUIManager.Instance.ValheimBeige),
                ConfigUI.AddTextRow(parent, w, 44f, "$sls_cfg_welcome_server", 14, GUIManager.Instance.ValheimBeige),
            };
            ConfigUI.LayoutColumn(rows, TextX, 6f, 8f);
        }

        // ------------------------------------------------------------------------------------------------
        //  Level progression systems
        // ------------------------------------------------------------------------------------------------

        private static void BuildScalingPage(Transform parent) {
            const float ColWidth = 760f;   // left config column + gap + image(300)
            const float ImgW = 300f;
            const float ImgH = 168f;

            // Build each row as its own container, collect them in order, then space the column out in one pass.
            List<GameObject> column = new List<GameObject> {
                ConfigUI.AddTextRow(parent, ColWidth, 40f, "$sls_cfg_scaling_intro", 13, GUIManager.Instance.ValheimBeige, TextAnchor.UpperCenter),
                WithTip(AddScalingFeatureRow(parent, ColWidth, ImgW, ImgH,
                    DistanceExample,
                    "$sls_cfg_distance_scale_header",
                    "$sls_cfg_distance_scale_desc",
                    staged.enableDistance,
                    v => staged.enableDistance = v,
                    "$sls_cfg_distance_overlay_toggle",
                    staged.enableDistanceOverlay,
                    v => staged.enableDistanceOverlay = v,
                    "$sls_cfg_nomap_ring_toggle",
                    staged.showNoMapRing,
                    v => staged.showNoMapRing = v), Tip(ValConfig.EnableDistanceLevelScalingBonus)),
                ConfigUI.AddDividerRow(parent, ColWidth),
                WithTip(AddScalingFeatureRow(parent, ColWidth, ImgW, ImgH,
                    ZoneExample,
                    "$sls_cfg_zone_scale_header",
                    "$sls_cfg_zone_scale_desc",
                    staged.enableZone,
                    v => staged.enableZone = v,
                    "$sls_cfg_zone_overlay_toggle",
                    staged.enableZoneOverlay,
                    v => staged.enableZoneOverlay = v,
                    "$sls_cfg_nomap_zone_toggle",
                    staged.showNoMapZone,
                    v => staged.showNoMapZone = v), Tip(ValConfig.EnableZoneScalingBonus)),
                ConfigUI.AddDividerRow(parent, ColWidth),
                WithTip(ConfigUI.AddToggleRow(parent, ColWidth, 360f, "$sls_cfg_conditional_scale_header", staged.enableConditional, v => staged.enableConditional = v, true), YamlTip(typeof(CreatureLevelSettings), nameof(CreatureLevelSettings.EnableConditionalCreatureLevelupChance))),
                ConfigUI.AddTextRow(parent, ColWidth, 34f, "$sls_cfg_conditional_scale_desc", 13, GUIManager.Instance.ValheimBeige),
                ConfigUI.AddTextRow(parent, ColWidth, 20f, "$sls_cfg_conditional_scale_note", 13, GUIManager.Instance.ValheimOrange),
            };
            // Center the column within the page root so it isn't left-biased.
            float colOffsetX = Mathf.Max(0f, (PageW - ColWidth) * 0.5f);
            ConfigUI.LayoutColumn(column, colOffsetX, 2f);
        }

        // One scaling system: a main toggle with a description and an example image, over one or two indented sub
        // toggles (the map overlay, and the client-side no-map readout). With both sub toggles the block is exactly
        // as tall as the example image, so the row does not grow - but it sizes itself to whichever is taller, so a
        // longer description or a bigger font cannot spill into the divider below.
        private static GameObject AddScalingFeatureRow(Transform parent, float colWidth, float imgW, float imgH, Sprite sprite, string mainLabel, string description, bool mainValue, Action<bool> onMain, string subLabel, bool subValue, Action<bool> onSub, string sub2Label = null, bool sub2Value = false, Action<bool> onSub2 = null) {
            const float SubIndent = 24f;
            const float MainToggleSize = 26f;
            const float SubToggleSize = 22f;
            const float ToggleGap = 8f;   // gap between a toggle and the title to its right
            const float DescH = 72f;      // up to ~4 wrapped lines; the row is tall so the description has room
            float leftColW = colWidth - imgW;   // configuration area to the left of the example image
            bool hasSub2 = sub2Label != null;
            float blockH = RowHeight + 2f + DescH + 4f + SubRowHeight + (hasSub2 ? 4f + SubRowHeight : 0f);
            float rowH = Mathf.Max(imgH, blockH);
            GameObject row = ConfigUI.NewRow(parent, colWidth, rowH);

            // Example image on the right, centered against the configuration block to its left
            GameObject go = ConfigUI.NewUI("Image", row.transform, typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(imgW, imgH);
            rt.anchoredPosition = new Vector2(colWidth - imgW, -(rowH - imgH) * 0.5f);
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;   // 256x171 letterboxes inside the box
            img.raycastTarget = false;

            // Vertically center the (main toggle + description + sub toggles) block in the row
            float topY = Mathf.Max(0f, (rowH - blockH) * 0.5f);

            // Main toggle, directly to the left of its title
            ConfigUI.AddToggle(row.transform, 0f, topY + 3f, MainToggleSize, mainValue, onMain);
            float mainLabelX = MainToggleSize + ToggleGap;
            ConfigUI.AddText(row.transform, mainLabelX, topY, leftColW - mainLabelX - 12f, RowHeight, mainLabel, 18, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimOrange);

            // Description directly under the main label (full left column up to the image)
            float descY = topY + RowHeight + 2f;
            ConfigUI.AddText(row.transform, 0f, descY, leftColW - 12f, DescH, description, 14, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);

            // Sub toggle (smaller, indented, below the description), directly to the left of its title
            float subY = descY + DescH + 4f;
            ConfigUI.AddToggle(row.transform, SubIndent, subY + 2f, SubToggleSize, subValue, onSub);
            float subLabelX = SubIndent + SubToggleSize + ToggleGap;
            ConfigUI.AddText(row.transform, subLabelX, subY, leftColW - subLabelX - 12f, SubRowHeight, subLabel, 14, TextAnchor.MiddleLeft);

            if (hasSub2) {
                float sub2Y = subY + SubRowHeight + 4f;
                ConfigUI.AddToggle(row.transform, SubIndent, sub2Y + 2f, SubToggleSize, sub2Value, onSub2);
                ConfigUI.AddText(row.transform, subLabelX, sub2Y, leftColW - subLabelX - 12f, SubRowHeight, sub2Label, 14, TextAnchor.MiddleLeft);
            }

            return row;
        }

        // ------------------------------------------------------------------------------------------------
        //  Level distribution
        // ------------------------------------------------------------------------------------------------

        private static void BuildDistributionPage(Transform parent) {
            const float LeftColWidth = 400f;
            const float LabelWidth = 150f, SliderWidth = 150f, ValueWidth = 60f;
            const float ChartX = 420f;
            const float ColTop = 46f;
            // Biome caps: a cell per capped biome, either auto-tuned from Max stars or typed in by hand.
            const float CellW = 169f;
            const float CapLabelW = 104f;
            const float CapFieldW = 48f;
            const float CapHeaderH = 26f;
            const float CapRowH = 30f;
            const int PerRow = 5;
            // The notes under the caps are usually empty, so the block is measured from the bottom of the page rather
            // than parked under a full-height chart. Everything the caps do not need goes to the charts and the column.
            const float NotesH = 38f;
            float chartW = PageW - ChartX;

            List<Heightmap.Biome> cappedBiomes = staged.biomeCapOriginals.Keys
                .OrderBy(b => staged.biomeCapOriginals[b]).ThenBy(b => (int)b).ToList();
            int capRows = Mathf.Max(1, Mathf.CeilToInt(cappedBiomes.Count / (float)PerRow));
            float capsY = PageH - NotesH - CapHeaderH - capRows * CapRowH;
            float chartTop = ColTop + 40f;
            float chartFullH = capsY - 10f - chartTop;
            float chartHalfH = (chartFullH - 8f) * 0.5f;

            GameObject intro = ConfigUI.AddTextRow(parent, PageW, 40f, "$sls_cfg_distribution_intro", 14, GUIManager.Instance.ValheimBeige, TextAnchor.UpperCenter);
            ConfigUI.PositionRow(intro, 0f, 0f);

            // The caps and their notes run the full width of the page, so the settings column stops above them. It still
            // outgrows that once bosses have a section of their own, so it scrolls. Rows that only apply to some curve
            // styles - or only while bosses are configured separately - are switched off rather than laid out again: a
            // vertical layout group skips inactive children, so the space they took collapses.
            ConfigUI.CreateScroll(parent, 0f, ColTop, LeftColWidth, capsY - ColTop - 6f, out Transform left, out float lw);

            shownMinStars = staged.MinStars;
            shownMaxStars = staged.maxStars;
            shownBossMinStars = staged.BossMinStars;
            shownBossMaxStars = staged.maxBossLevel;

            GameObject chanceRow = null, gaussianRow = null, tableRow = null, tableStatusRow = null;
            GameObject bossChanceRow = null, bossGaussianRow = null;
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
                }
                // One tall chart while bosses follow the creature curve, two half-height ones once they have their own.
                distributionChart.SetRect(ChartX, chartTop, chartW, separate ? chartHalfH : chartFullH);
                distributionChart.SetTitle(separate ? "Creatures" : "");
                bossChart.SetVisible(separate);
                if (separate) { bossChart.SetRect(ChartX, chartTop + chartHalfH + 8f, chartW, chartHalfH); }
                RefreshDistribution();
            }

            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Creature star range"));
            GameObject minRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Min stars", 0f, 200f, staged.MinStars, true, v => OnMinStarsChanged((int)v)),
                Tip("MinLevel", "The lowest star level a new creature can spawn with. The curve starts here, and distance bonuses no longer reach below it.")));
            minStarsSlider = minRow.GetComponentInChildren<Slider>();
            GameObject maxRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Max stars", 1f, 200f, staged.maxStars, true, v => OnMaxStarsChanged((int)v)),
                Tip(ValConfig.MaxLevel)));
            maxStarsSlider = maxRow.GetComponentInChildren<Slider>();

            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Creature curve"));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddEnumCycleRow(t, lw, LabelWidth, 150f, "Curve style", CalcStyleOptions, (int)staged.generator.LevelupCalculationStyle, i => {
                staged.generator.LevelupCalculationStyle = (LevelupCalculationStyle)i;
                ShowStyleRows();
                RefreshTableRows(true);
                RefreshDistribution();
            }), Tip("LevelupCalculationStyle", "How the chance falls away from Min to Max stars. Linear spreads it evenly, Exponential makes high stars rare, Gaussian favours the middle of the range, and Table uses the values you type below.")));
            chanceRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Level-up chance", 0f, 1f, staged.generator.LevelUpChance, false, v => {
                staged.generator.LevelUpChance = v;
                RefreshDistribution();
            }), Tip("LevelUpChance", "How likely a creature is to pass the first star level, which sets how far up the curve creatures usually get. Gaussian uses it to narrow the bell instead.")));
            gaussianRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Gaussian offset", -1f, 1f, staged.generator.GaussianOffset, false, v => {
                staged.generator.GaussianOffset = v;
                RefreshDistribution();
            }), Tip("GaussianOffset", "Moves the peak of the bell curve: -1 towards Min stars, +1 towards Max stars.")));
            tableRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddTextFieldRow(t, lw, LabelWidth, SliderWidth + ValueWidth + 10f, "Table thresholds", "", CommitTableThresholds, "30, 15, 5, 0.01"),
                Tip("LevelupChanceTablesBySpan", "The % chance to roll past each star level, starting at Min stars. Values have to go down, and how many you enter sets Max stars.")));
            tableThresholdField = tableRow.GetComponentInChildren<InputField>();
            tableStatusRow = ScrollRow(left, lw, 50f, t => ConfigUI.AddTextRow(t, lw, 50f, "", 12, GUIManager.Instance.ValheimBeige));
            tableStatusText = tableStatusRow.GetComponentInChildren<Text>();
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Night multiplier", 0f, 5f, staged.generator.NightMultiplier, false, v => staged.generator.NightMultiplier = v),
                Tip("NightMultiplier", "Multiplies these chances at night only, so creatures spawn with more stars after dark. 1 leaves them alone.")));

            // Bosses. The star cap applies whether or not they have a curve of their own, so it sits outside the toggle.
            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Bosses"));
            GameObject bossMaxRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Max boss stars", 1f, 200f, staged.maxBossLevel, true, v => OnBossMaxStarsChanged((int)v)),
                Tip(ValConfig.MaxBossLevel)));
            bossMaxStarsSlider = bossMaxRow.GetComponentInChildren<Slider>();
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelWidth + 120f, "Own curve for bosses", staged.bossCurveOn, on => {
                staged.bossCurveOn = on;
                ShowBossRows();
            }), YamlTip(typeof(CreatureLevelSettings), nameof(CreatureLevelSettings.BossLevelupGenerators))));

            GameObject bossMinRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Boss min stars", 0f, 200f, staged.BossMinStars, true, v => OnBossMinStarsChanged((int)v)),
                Tip("MinLevel", "The lowest star level a boss can spawn with once bosses have their own curve.")));
            bossMinStarsSlider = bossMinRow.GetComponentInChildren<Slider>();
            bossRows.Add(bossMinRow);
            bossRows.Add(ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddEnumCycleRow(t, lw, LabelWidth, 150f, "Boss curve style", CalcStyleOptions, (int)staged.bossGenerator.LevelupCalculationStyle, i => {
                staged.bossGenerator.LevelupCalculationStyle = (LevelupCalculationStyle)i;
                ShowBossRows();
            }), Tip("LevelupCalculationStyle", "How the boss chance falls away from Boss min to Max boss stars. Table style reads the same LevelupChanceTablesBySpan entry as any other curve of that length."))));
            bossChanceRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Boss level-up chance", 0f, 1f, staged.bossGenerator.LevelUpChance, false, v => {
                staged.bossGenerator.LevelUpChance = v;
                RefreshDistribution();
            }), Tip("LevelUpChance", "How likely a boss is to pass its first star level.")));
            bossRows.Add(bossChanceRow);
            bossGaussianRow = ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Boss Gaussian offset", -1f, 1f, staged.bossGenerator.GaussianOffset, false, v => {
                staged.bossGenerator.GaussianOffset = v;
                RefreshDistribution();
            }), Tip("GaussianOffset", "Moves the peak of the boss bell curve: -1 towards Boss min stars, +1 towards Max boss stars.")));
            bossRows.Add(bossGaussianRow);
            bossRows.Add(ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelWidth, SliderWidth, ValueWidth, "Boss night multiplier", 0f, 5f, staged.bossGenerator.NightMultiplier, false, v => staged.bossGenerator.NightMultiplier = v),
                Tip("NightMultiplier", "Multiplies the boss chances at night only. 1 leaves them alone."))));

            RefreshTableRows(true);
            ShowStyleRows();

            GameObject caption = ConfigUI.AddTextRow(parent, chartW, 36f, "$sls_cfg_distribution_caption", 13, GUIManager.Instance.ValheimBeige, TextAnchor.MiddleCenter);
            ConfigUI.PositionRow(caption, ChartX, ColTop);
            distributionChart = new LevelDistributionChart(parent, ChartX, chartTop, chartW, chartFullH);
            bossChart = new LevelDistributionChart(parent, ChartX, chartTop + chartHalfH + 8f, chartW, chartHalfH);
            bossChart.SetTitle("Bosses");

            ConfigUI.AddText(parent, 0f, capsY, 170f, 24f, "$sls_cfg_distribution_caps_header", 15, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimYellow);
            ConfigUI.AddToggle(parent, 176f, capsY + 1f, 22f, staged.biomeCapAuto, OnBiomeCapAutoChanged);
            WithTip(ConfigUI.AddText(parent, 204f, capsY, 300f, 24f, "$sls_cfg_distribution_caps_auto", 13, TextAnchor.MiddleLeft).gameObject,
                Tip(ValConfig.AutoTuneBiomeStarCaps));

            int cell = 0;
            foreach (Heightmap.Biome capped in cappedBiomes) {
                Heightmap.Biome biome = capped;   // capture per iteration
                float cellX = (cell % PerRow) * CellW;
                float cellY = capsY + CapHeaderH + (cell / PerRow) * CapRowH;
                ConfigUI.AddText(parent, cellX, cellY, CapLabelW, 28f, BiomeName(biome), 13, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimBeige);
                InputField field = ConfigUI.AddTextField(parent, cellX + CapLabelW + 2f, cellY, CapFieldW, staged.CapFor(biome).ToString(),
                    text => CommitBiomeCap(biome, text), InputField.ContentType.IntegerNumber);
                WithTip(field.gameObject, YamlTip(typeof(BiomeSpecificSetting), nameof(BiomeSpecificSetting.BiomeMaxLevelOverride))
                    + "\nEditable once the caps stop scaling with Max stars.");
                biomeCapFields.Add(new BiomeCapField { Biome = biome, Field = field, TextColor = field.textComponent != null ? field.textComponent.color : Color.white });
                cell++;
            }

            float belowCaps = capsY + CapHeaderH + capRows * CapRowH + 4f;
            distributionNotesText = ConfigUI.AddText(parent, 0f, belowCaps, PageW, PageH - belowCaps, "", 13, TextAnchor.UpperLeft, GUIManager.Instance.ValheimOrange);

            // Lays the charts out for the current mode and draws everything.
            ShowBossRows();
        }

        // Min and Max both rewrite the generator's range, so whichever curve is picked always tapers across exactly the
        // stars shown. Each keeps Min <= Max by moving the other slider. The value boxes also report their value when
        // they merely lose focus, so a value the slider already showed is not an edit.
        private static void OnMinStarsChanged(int minStars) {
            if (staged == null || minStars == shownMinStars) { return; }
            shownMinStars = minStars;
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
            RefreshDistribution();
            UpdateExampleMath();
        }

        private static void RefreshDistribution() {
            if (staged == null || baseline == null || distributionChart == null) { return; }

            // Until the curve is changed, show what the world rolls today, which may be a hand-written table that no
            // generator describes. The cap matches LevelSelection.GetMaxCreatureLevel before biome overrides.
            SortedDictionary<int, float> table = staged.CurveDiffers(baseline) ? StagedCurveTable(staged.generator, staged.TableSpan) : LiveDefaultTable();
            distributionChart.SetData(ToStarSeries(LevelSelection.ComputeLevelDistribution(table, staged.maxStars + 1)));

            if (bossChart != null && staged.bossCurveOn) {
                SortedDictionary<int, float> bossTable = staged.BossCurveDiffers(baseline)
                    ? StagedCurveTable(staged.bossGenerator, staged.BossTableSpan)
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
                    notes.Add(ConfigUI.L("$sls_cfg_distribution_conditional_note"));
                }
                CreatureLevelSettings live = LevelSystemData.SLE_Level_Settings;
                if ((live?.DefaultLevelupGenerators?.Count ?? 0) > 1 || (live?.DefaultLevelupGeneratorRefs?.Count ?? 0) > 0) {
                    notes.Add(ConfigUI.L("$sls_cfg_distribution_merge_note"));
                }
                distributionNotesText.text = string.Join("\n", notes.ToArray());
            }
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

        // The staged generator expanded to a threshold table. Table style is expanded here from the STAGED tables, since
        // LevelGenerator reads the live settings, with the same Exponential fallback the runtime uses when a span has none.
        private static SortedDictionary<int, float> StagedCurveTable(LevelGenerator g, int span) {
            if (g.LevelupCalculationStyle != LevelupCalculationStyle.Table) { return g.GetLevelUpDefinition(); }

            int lowest = Mathf.Min(g.MinLevel, g.MaxLevel);
            if (span < 2) { return new SortedDictionary<int, float> { { lowest, 0f } }; }
            if (staged.tables.TryGetValue(span, out List<float> values) && values.Count > 0) {
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

        // Shows the staged table for the generator's current span and whether it will work. rewriteField is false after a
        // rejected entry, so the typing stays in the box to be corrected.
        private static void RefreshTableRows(bool rewriteField) {
            if (staged == null || tableThresholdField == null || tableStatusText == null) { return; }
            LevelGenerator g = staged.generator;
            int span = staged.TableSpan;
            int minStars = staged.MinStars;
            int maxStars = Mathf.Max(0, Mathf.Max(g.MinLevel, g.MaxLevel) - 1);
            staged.tables.TryGetValue(span, out List<float> values);
            bool hasTable = values != null && values.Count > 0;

            if (rewriteField) {
                if (hasTable) {
                    tablePrefillText = null;
                    tableThresholdField.SetTextWithoutNotify(FormatThresholds(values));
                } else {
                    tablePrefillText = span >= 2 ? FormatThresholds(ExponentialFallback(g).GetLevelUpDefinition().Values) : "";
                    tableThresholdField.SetTextWithoutNotify(tablePrefillText);
                }
            }

            if (span < 2) {
                SetTableStatus("A single star level always rolls that level; widen Min/Max stars to use a table.", false);
            } else if (hasTable == false) {
                SetTableStatus($"No table for {span} levels yet, so rolls use the Exponential curve shown. Edit these values and press Enter to save a table.", false);
            } else if (IsStrictlyDecreasing(values) == false) {
                SetTableStatus("Values must decrease: each is the % chance to roll past a star level, so one at or above the value before it is never rolled.", false);
            } else {
                SetTableStatus($"{minStars}-{maxStars} stars: each value is the % chance to roll past that star level, starting at {minStars}.", true);
            }
        }

        private static void SetTableStatus(string message, bool ok) {
            tableStatusText.text = message;
            tableStatusText.color = ok ? GUIManager.Instance.ValheimBeige : GUIManager.Instance.ValheimOrange;
        }

        private static bool IsStrictlyDecreasing(List<float> values) {
            for (int i = 1; i < values.Count; i++) {
                if (values[i] >= values[i - 1]) { return false; }
            }
            return true;
        }

        // Parses "30, 15, 5, 0.01" into the table for that many levels. The entry count sets the span, so Max stars moves
        // to cover exactly that many levels from Min stars.
        private static void CommitTableThresholds(string text) {
            if (staged == null) { return; }
            // onEndEdit also fires when the box merely loses focus; suggested values passed over untouched are not a table.
            if (tablePrefillText != null && text == tablePrefillText) { return; }
            if (string.IsNullOrWhiteSpace(text)) { RefreshTableRows(true); return; }

            List<float> values = new List<float>();
            foreach (string token in text.Split(',')) {
                string trimmed = token.Trim();
                if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) == false) {
                    SetTableStatus($"Could not read '{trimmed}'. Enter numbers separated by commas, e.g. 30, 15, 5, 0.01.", false);
                    return;
                }
                values.Add(value);
            }
            if (values.Count < 2) {
                SetTableStatus("Enter at least two values; a table covers two or more levels.", false);
                return;
            }

            bool known = staged.tables.TryGetValue(values.Count, out List<float> existing) && existing.Count == values.Count
                && existing.Zip(values, (a, b) => Mathf.Abs(a - b) < 0.0001f).All(same => same);
            // The same table read back from the box (which rounds to 4 places) for the span already in use changes nothing.
            if (known && values.Count == staged.TableSpan) {
                RefreshTableRows(true);
                return;
            }
            if (known == false) {
                staged.tables[values.Count] = values;
            }

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

        // ------------------------------------------------------------------------------------------------
        //  Health, damage and multiplayer
        // ------------------------------------------------------------------------------------------------

        private static void BuildStatsPage(Transform parent) {
            const float LeftColWidth = 430f;
            const float LabelWidth = 190f;
            const float SliderWidth = 150f;
            const float ValueWidth = 60f;
            const float RightColumnX = 450f;
            const float StartY = 2f;
            float rightColWidth = PageW - RightColumnX;

            List<GameObject> left = new List<GameObject> {
                ConfigUI.AddHeaderRow(parent, LeftColWidth, "Per-level stats"),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Creature HP / star", 0f, 5f, staged.creatureHpPerLevel, false, v => { staged.creatureHpPerLevel = v; UpdateExampleMath(); }), Tip(ValConfig.EnemyHealthMultiplier)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Creature dmg / star", 0f, 2f, staged.creatureDmgPerLevel, false, v => { staged.creatureDmgPerLevel = v; UpdateExampleMath(); }), Tip(ValConfig.EnemyDamageLevelMultiplier)),
                ConfigUI.AddDividerRow(parent, LeftColWidth),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Boss HP / star", 0f, 5f, staged.bossHpPerLevel, false, v => { staged.bossHpPerLevel = v; UpdateExampleMath(); }), Tip(ValConfig.BossEnemyHealthMultiplier)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Boss dmg / star", 0f, 5f, staged.bossDmgPerLevel, false, v => { staged.bossDmgPerLevel = v; UpdateExampleMath(); }), Tip(ValConfig.BossEnemyDamageMultiplier)),
                ConfigUI.AddSpacerRow(parent, LeftColWidth, 4f),
                ConfigUI.AddHeaderRow(parent, LeftColWidth, "Multiplayer scaling"),
                WithTip(ConfigUI.AddToggleRow(parent, LeftColWidth, LabelWidth + 170f, "Enemies gain HP with more players", staged.mpHealth, v => { staged.mpHealth = v; UpdateExampleMath(); }), Tip(ValConfig.EnableMultiplayerEnemyHealthScaling)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "HP per extra player", 0f, 0.99f, staged.mpHealthMod, false, v => { staged.mpHealthMod = v; UpdateExampleMath(); }), Tip(ValConfig.MultiplayerEnemyHealthModifier)),
                WithTip(ConfigUI.AddToggleRow(parent, LeftColWidth, LabelWidth + 170f, "Enemies gain dmg with more players", staged.mpDamage, v => { staged.mpDamage = v; UpdateExampleMath(); }), Tip(ValConfig.EnableMultiplayerEnemyDamageScaling)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Dmg per extra player", 0f, 2f, staged.mpDamageMod, false, v => { staged.mpDamageMod = v; UpdateExampleMath(); }), Tip(ValConfig.MultiplayerEnemyDamageModifier)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Players needed nearby", 1f, 20f, staged.mpRequiredPlayers, true, v => { staged.mpRequiredPlayers = (int)v; UpdateExampleMath(); }), Tip(ValConfig.MultiplayerScalingRequiredPlayersNearby)),
            };
            ConfigUI.LayoutColumn(left, 0f, StartY);

            // Right column - worked examples. The Troll uses Max stars from the distribution page, The Elder the boss cap.
            const float CardH = 112f;
            GameObject header = ConfigUI.AddHeaderRow(parent, rightColWidth, "Examples");
            ConfigUI.PositionRow(header, RightColumnX, StartY);
            float y = StartY + RowHeight + RowGap;
            trollExampleText = AddExampleCard(parent, RightColumnX, y, rightColWidth, CardH, "TrophyForestTroll");
            y += CardH + 8f;
            elderExampleText = AddExampleCard(parent, RightColumnX, y, rightColWidth, CardH, "TrophyTheElder");
            y += CardH + 8f;
            GameObject mpHeader = ConfigUI.AddHeaderRow(parent, rightColWidth, "In a group");
            ConfigUI.PositionRow(mpHeader, RightColumnX, y);
            y += RowHeight + RowGap;
            multiplayerExampleText = ConfigUI.AddText(parent, RightColumnX, y, rightColWidth, PageH - y, "", 14, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);

            UpdateExampleMath();
        }

        private static Text AddExampleCard(Transform parent, float x, float y, float w, float h, string trophyPrefab) {
            const float IconSize = 64f;
            const float TextX = IconSize + 12f;
            GameObject card = ConfigUI.NewRect("ExampleCard", parent, x, y, w, h);

            Sprite icon = LoadItemIcon(trophyPrefab);
            if (icon != null) {
                GameObject iconGO = ConfigUI.NewUI("Icon", card.transform, typeof(Image));
                RectTransform rt = (RectTransform)iconGO.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(IconSize, IconSize);
                rt.anchoredPosition = new Vector2(0f, -8f);
                Image img = iconGO.GetComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            return ConfigUI.AddText(card.transform, TextX, 0f, w - TextX, h, "", 14, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);
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
                trollExampleText.text = FormatCreatureExample("Troll", TrollHp, TrollDmg, staged.maxStars, staged.creatureHpPerLevel, staged.creatureDmgPerLevel);
            }
            if (elderExampleText != null) {
                elderExampleText.text = FormatCreatureExample("The Elder (boss)", TheElderHP, TheElderDmg, staged.maxBossLevel, staged.bossHpPerLevel, staged.bossDmgPerLevel);
            }
            if (multiplayerExampleText != null) {
                multiplayerExampleText.text = FormatMultiplayerExample();
            }
        }

        private static string Stars(int stars) => stars == 1 ? "1 star" : $"{stars} stars";

        // Same shape as the runtime: base * (1 + per-star multiplier * stars), for health and for damage.
        private static string FormatCreatureExample(string name, float baseHp, float baseDmg, int maxStars, float hpMul, float dmgMul) {
            maxStars = Mathf.Max(0, maxStars);
            int medStars = Mathf.CeilToInt(maxStars / 2f);
            string Line(int stars) => $"{Stars(stars)}:   {baseHp * (1f + hpMul * stars):0} HP   {baseDmg * (1f + dmgMul * stars):0} damage";
            return $"{name}  (base {baseHp:0} HP, {baseDmg:0} damage)\n{Line(0)}\n{Line(medStars)}\n{Line(maxStars)}";
        }

        // Mirrors MultiplayerDamageMod: enemies take max(1 - players * healthMod, MinDamageTaken) damage and deal
        // 1 + (1 + players) * damageMod, once enough players are nearby.
        private static string FormatMultiplayerExample() {
            if (staged.mpHealth == false && staged.mpDamage == false) {
                return "Multiplayer scaling is off: groups face the same creatures as a solo player.";
            }
            int players = Mathf.Max(1, staged.mpRequiredPlayers);
            float taken = staged.mpHealth ? Mathf.Max(1f - players * staged.mpHealthMod, ValConfig.MultiplayerEnemyMinDamageTaken.Value) : 1f;
            taken = Mathf.Max(taken, 0.01f);
            float dealt = staged.mpDamage ? 1f + (1f + players) * staged.mpDamageMod : 1f;
            int stars = Mathf.Max(0, staged.maxStars);
            float trollHp = TrollHp * (1f + staged.creatureHpPerLevel * stars);
            float trollDmg = TrollDmg * (1f + staged.creatureDmgPerLevel * stars);
            return $"With {players} or more players nearby, creatures take {taken:0.00}x damage (about {1f / taken:0.0}x the health) and deal {dealt:0.00}x damage.\n" +
                   $"A {Stars(stars)} Troll then takes about {trollHp / taken:0} damage to kill and hits for about {trollDmg * dealt:0}.";
        }

        // ------------------------------------------------------------------------------------------------
        //  Modifiers
        // ------------------------------------------------------------------------------------------------

        private static void BuildModifiersPage(Transform parent) {
            // Left column holds all of the numeric/toggle config; sliders are kept narrow so their value
            // boxes don't run into the scroll view on the right.
            const float LeftColWidth = 430f;
            const float LeftLabelWidth = 200f, LeftSliderWidth = 120f, LeftValueWidth = 56f;
            const float ToggleLabelWidth = 300f;
            const float StartY = 4f;

            List<GameObject> left = new List<GameObject> {
                ConfigUI.AddHeaderRow(parent, LeftColWidth, "Creature modifiers"),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Max major modifiers", 0f, 6f, staged.maxMajor, true, v => staged.maxMajor = (int)v), Tip(ValConfig.MaxMajorModifiersPerCreature)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Max minor modifiers", 0f, 6f, staged.maxMinor, true, v => staged.maxMinor = (int)v), Tip(ValConfig.MaxMinorModifiersPerCreature)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Major modifier chance", 0f, 1f, staged.chanceMajor, false, v => staged.chanceMajor = v), Tip(ValConfig.ChanceMajorModifier)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Minor modifier chance", 0f, 1f, staged.chanceMinor, false, v => staged.chanceMinor = v), Tip(ValConfig.ChanceMinorModifier)),
                WithTip(ConfigUI.AddToggleRow(parent, LeftColWidth, ToggleLabelWidth, "Limit modifier count to star level", staged.limitToStarLevel, v => staged.limitToStarLevel = v), Tip(ValConfig.LimitCreatureModifiersToCreatureStarLevel)),
                ConfigUI.AddHeaderRow(parent, LeftColWidth, "Boss modifiers"),
                WithTip(ConfigUI.AddToggleRow(parent, LeftColWidth, ToggleLabelWidth, "Bosses can have modifiers", staged.enableBossMods, v => staged.enableBossMods = v), Tip(ValConfig.EnableBossModifiers)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Boss modifier chance", 0f, 1f, staged.chanceBoss, false, v => staged.chanceBoss = v), Tip(ValConfig.ChanceOfBossModifier)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Max boss modifiers", 0f, 6f, staged.maxBossMods, true, v => staged.maxBossMods = (int)v), Tip(ValConfig.MaxBossModifiersPerBoss)),
                ConfigUI.AddHeaderRow(parent, LeftColWidth, "Modifier display"),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Max name prefixes", 0f, 6f, staged.prefixLimit, true, v => staged.prefixLimit = (int)v), Tip(ValConfig.LimitCreatureModifierPrefixes)),
                WithTip(ConfigUI.AddToggleRow(parent, LeftColWidth, ToggleLabelWidth, "Minor modifiers first in name", staged.minorFirst, v => staged.minorFirst = v), Tip(ValConfig.MinorModifiersFirstInName)),
                WithTip(ConfigUI.AddEnumCycleRow(parent, LeftColWidth, LeftLabelWidth, 150f, "Icon display style", DisplayStyleOptions, (int)staged.displayStyle, i => staged.displayStyle = (ModifierDisplayStyle)i), Tip(ValConfig.ModifierIconDisplayStyle)),
            };
            ConfigUI.LayoutColumn(left, 0f, StartY);

            // Right side - scrollable list of every modifier defined in Modifiers.yaml, grouped by category,
            // each with an enable/disable toggle and a brief description.
            const float ScrollX = 446f;
            const float ScrollW = 400f;
            float scrollH = PageH - StartY - RowHeight - 8f;
            ConfigUI.AddText(parent, ScrollX, StartY, ScrollW, RowHeight, "Enable / disable modifiers", 16, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimYellow);
            ConfigUI.CreateScroll(parent, ScrollX, StartY + RowHeight, ScrollW, scrollH, out Transform content, out float contentW);
            if (content != null) {
                AddModifierCategory(content, contentW, "Boss modifiers", ModifierType.Boss, staged.modifierSource?.BossModifiers);
                AddModifierCategory(content, contentW, "Major modifiers", ModifierType.Major, staged.modifierSource?.MajorModifiers);
                AddModifierCategory(content, contentW, "Minor modifiers", ModifierType.Minor, staged.modifierSource?.MinorModifiers);
            }
        }

        // Adds a category header followed by one toggle row per modifier defined in that category.
        private static void AddModifierCategory(Transform content, float width, string label, ModifierType type, Dictionary<string, CreatureModifierConfiguration> dict) {
            if (dict == null || dict.Count == 0) { return; }
            GameObject header = ConfigUI.NewLayoutRow(content, width, 30f);
            ConfigUI.AddText(header.transform, 2f, 4f, width - 4f, 24f, label, 16, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimYellow);
            HashSet<string> enabled = staged.modifierOn[type];
            foreach (string name in dict.Keys.OrderBy(n => n)) {
                string modName = name;   // capture for the closure
                string desc = ModifierDescriptions.TryGetValue(modName, out string d) ? d : "";
                AddToggleEntry(content, width, 48f, Prettify(modName), desc, enabled.Contains(modName), on => {
                    if (on) { staged.modifierOn[type].Add(modName); }
                    else { staged.modifierOn[type].Remove(modName); }
                });
            }
        }

        // A single list line inside a scroll view: enable toggle on the left, name and a brief description to its right.
        private static void AddToggleEntry(Transform content, float width, float height, string name, string description, bool enabled, Action<bool> onChange) {
            const float TextX = 32f;
            GameObject row = ConfigUI.NewLayoutRow(content, width, height);
            ConfigUI.AddToggle(row.transform, 2f, 3f, 22f, enabled, onChange);
            ConfigUI.AddText(row.transform, TextX, 0f, width - TextX - 4f, 20f, name, 14, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimOrange);
            ConfigUI.AddText(row.transform, TextX, 20f, width - TextX - 4f, height - 22f, description, 12, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);
        }

        // "ResistPierce" -> "Resist Pierce", "BossSummoner" -> "Boss Summoner".
        private static string Prettify(string name) => Regex.Replace(name, "(\\B[A-Z])", " $1");

        // ------------------------------------------------------------------------------------------------
        //  Nemesis
        // ------------------------------------------------------------------------------------------------

        private static void BuildNemesisPage(Transform parent) {
            const float IntroH = 92f;
            const float LeftW = 392f;
            const float RightX = 408f;
            const float LabelW = 180f, SliderW = 120f, ValueW = 60f;
            float rightW = PageW - RightX;

            GameObject intro = ConfigUI.AddTextRow(parent, PageW, IntroH, "$sls_cfg_nemesis_intro", 14, GUIManager.Instance.ValheimBeige);
            ConfigUI.PositionRow(intro, 0f, 0f);
            nemesisWarningText = ConfigUI.AddText(parent, 0f, IntroH, PageW, 20f, "", 13, TextAnchor.MiddleCenter, GUIManager.Instance.ValheimOrange);
            float scrollY = IntroH + 24f;
            float scrollH = PageH - scrollY;

            // Left - core settings and the score system, in a scroll view since together they outgrow the page.
            ConfigUI.CreateScroll(parent, 0f, scrollY, LeftW, scrollH, out Transform left, out float lw);
            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Nemesis settings"));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "Enable Nemesis system", staged.enableNemesis, v => staged.enableNemesis = v), Tip(ValConfig.EnableNemesisSystem)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Action cooldown (sec)", 0f, 120f, staged.nemCooldown, false, v => staged.nemCooldown = v), Tip("NemesisActionCooldownSeconds", "Seconds every Nemesis action waits after any one of them fires, level changes and spawns alike.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Influence radius (m)", 0f, 1000f, staged.nemInfluence, false, v => staged.nemInfluence = v), Tip("NemesisInfluenceRadius", "How far from you a creature can spawn and still trigger a Nemesis spawn. Level changes ignore this.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Min spawn distance (m)", 0f, 500f, staged.nemMinSpawn, false, v => staged.nemMinSpawn = v), Tip("NemesisMinSpawnDistance", "Nemesis spawns are kept at least this far away, so an ambush never lands in your lap.")));
            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Score system"));
            ScrollRow(left, lw, 96f, t => ConfigUI.AddTextRow(t, lw, 96f, "$sls_cfg_nemesis_score_help", 12, GUIManager.Instance.ValheimBeige));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Neutral score", 0f, 20000f, staged.neutralScore, true, v => { staged.neutralScore = v; RefreshNemesisDescriptions(); }), Tip("NeutralScore", "The score everything drifts back towards. Actions with a threshold above it make the world harder, below it easier.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Min score", 0f, 20000f, staged.minScore, true, v => { staged.minScore = v; RefreshNemesisDescriptions(); }), Tip("MinScore", "The lowest the score can fall. A threshold outside the Min to Max range never fires.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Max score", 0f, 20000f, staged.maxScore, true, v => { staged.maxScore = v; RefreshNemesisDescriptions(); }), Tip("MaxScore", "The highest the score can climb. A threshold outside the Min to Max range never fires.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Decay per update", 0f, 2000f, staged.decayPerUpdate, true, v => staged.decayPerUpdate = v), Tip("DecayPerUpdate", "How far the score moves back towards Neutral on every update, so a good or bad run fades with time.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Score interval (sec)", 1f, 120f, staged.scoreInterval, true, v => staged.scoreInterval = v), Tip("ScoreIntervalSeconds", "Seconds between score recalculations. Damage dealt and taken are averaged over the last few of these.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Boss-kill bonus", 0f, 5000f, staged.bossKillBonus, true, v => staged.bossKillBonus = v), Tip("BossKillBonus", "Score gained for killing a boss near you, which pushes the world towards its harder actions.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Death score reduction", 0f, 5000f, staged.deathReduction, true, v => staged.deathReduction = v), Tip("DeathScoreReduction", "Score lost when you die. A large value makes the system ease off quickly after a bad fight.")));

            // Right - every configured action, in the order they are checked.
            ConfigUI.CreateScroll(parent, RightX, scrollY, rightW, scrollH, out Transform right, out float rw);
            ScrollRow(right, rw, RowHeight, t => ConfigUI.AddHeaderRow(t, rw, "Nemesis actions"));
            ScrollRow(right, rw, 50f, t => ConfigUI.AddTextRow(t, rw, 50f, "$sls_cfg_nemesis_actions_help", 12, GUIManager.Instance.ValheimBeige));
            foreach (KeyValuePair<string, StagedNemesisAction> action in staged.nemesisActions) {
                AddNemesisActionEntry(right, rw, action.Key, action.Value);
            }

            RefreshNemesisDescriptions();
        }

        // Rows inside a scroll view must size themselves through a LayoutElement (see ConfigUI.NewLayoutRow). This hosts
        // an ordinary positioned row inside one, where it self-positions at (0,0).
        private static GameObject ScrollRow(Transform content, float width, float height, Action<Transform> build) {
            GameObject holder = ConfigUI.NewLayoutRow(content, width, height);
            build(holder.transform);
            return holder;
        }

        private static void AddNemesisActionEntry(Transform content, float width, string name, StagedNemesisAction action) {
            const float TextX = 30f;
            const float TagW = 110f;
            const float SliderTop = 76f;
            const float LabelW = 120f, SliderW = 170f, ValueW = 60f;
            GameObject entry = ConfigUI.NewLayoutRow(content, width, SliderTop + 3 * RowHeight + 10f);

            ConfigUI.AddToggle(entry.transform, 2f, 1f, 22f, action.Enabled, on => { action.Enabled = on; RefreshNemesisDescriptions(); });
            ConfigUI.AddText(entry.transform, TextX, 0f, width - TextX - TagW - 8f, 24f, Prettify(name), 15, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimOrange);
            Text tag = ConfigUI.AddText(entry.transform, width - TagW - 4f, 0f, TagW, 24f, "", 13, TextAnchor.MiddleRight);
            Text description = ConfigUI.AddText(entry.transform, TextX, 26f, width - TextX - 4f, SliderTop - 28f, "", 12, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);

            float rowW = width - TextX;
            GameObject chance = WithTip(ConfigUI.AddSliderRow(entry.transform, rowW, LabelW, SliderW, ValueW, "Chance", 0f, 1f, action.Chance, false, v => { action.Chance = v; RefreshNemesisDescriptions(); }), Tip("Chance", "How likely this action is to fire once its score threshold is met and the cooldown is up."));
            ConfigUI.PositionRow(chance, TextX, SliderTop);
            // Same range as the score sliders, widened if the file already holds something outside it, so building the page
            // never clamps a configured value.
            GameObject threshold = WithTip(ConfigUI.AddSliderRow(entry.transform, rowW, LabelW, SliderW, ValueW, "Score threshold", Mathf.Min(0f, action.Threshold), Mathf.Max(20000f, action.Threshold), action.Threshold, true, v => { action.Threshold = v; RefreshNemesisDescriptions(); }), Tip("ScoreThreshold", "The score this action needs. Above Neutral it fires when you are at or above it, below Neutral when you are at or below it."));
            ConfigUI.PositionRow(threshold, TextX, SliderTop + RowHeight);
            GameObject level = WithTip(ConfigUI.AddSliderRow(entry.transform, rowW, LabelW, SliderW, ValueW, "Level bonus", Mathf.Min(-10, action.LevelBonus), Mathf.Max(10, action.LevelBonus), action.LevelBonus, true, v => { action.LevelBonus = (int)v; RefreshNemesisDescriptions(); }), Tip("LevelBonus", "Levels added to the creatures this action touches. Negative values take levels away."));
            ConfigUI.PositionRow(level, TextX, SliderTop + 2 * RowHeight);

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
            string gate = anyScore ? "at any score" : (harderSide ? $"when score >= {action.Threshold:0}" : $"when score <= {action.Threshold:0}");

            string effect;
            switch (source.Action) {
                case NemesisAction.ChangeLevel:
                    if (action.LevelBonus == 0) {
                        effect = "Leaves new creature levels unchanged";
                        tag = "No effect";
                        tagColor = InactiveColor;
                    } else {
                        int amount = Mathf.Abs(action.LevelBonus);
                        effect = $"New creatures {(action.LevelBonus > 0 ? "+" : "-")}{amount} level{(amount == 1 ? "" : "s")}";
                        tag = action.LevelBonus > 0 ? "Harder" : "Easier";
                        tagColor = action.LevelBonus > 0 ? HarderColor : EasierColor;
                    }
                    break;
                case NemesisAction.Spawn: {
                        effect = "Spawns " + DescribeSpawns(source.SpawnConfig) + LevelSuffix(action.LevelBonus);
                        bool reward = source.SpawnConfig != null && source.SpawnConfig.Count > 0 && source.SpawnConfig.All(sp => sp != null && sp.CustomLoot != null && sp.CustomLoot.Count > 0);
                        tag = reward ? "Reward" : "Harder";
                        tagColor = reward ? GUIManager.Instance.ValheimYellow : HarderColor;
                        break;
                    }
                case NemesisAction.SpawnMiniboss:
                    effect = "Brings back a creature that killed a player as a named nemesis, with minions" + LevelSuffix(action.LevelBonus);
                    tag = "Nemesis";
                    tagColor = NemesisColor;
                    break;
                default:
                    effect = $"{source.Action} is not implemented yet, so this never fires";
                    tag = "Inactive";
                    tagColor = InactiveColor;
                    break;
            }

            string text = $"{effect} {gate}, {action.Chance * 100f:0.#}% chance.";
            string conditions = DescribeConditions(source);
            if (conditions.Length > 0) { text += " " + conditions; }

            if (anyScore == false && (action.Threshold < staged.minScore || action.Threshold > staged.maxScore)) {
                text += $" {WarningColorTag}Never fires: the threshold is outside the score range.</color>";
            }
            if (isLevelChange && anyScore == false && action.LevelBonus != 0 && (action.LevelBonus > 0) != harderSide) {
                text += action.LevelBonus > 0
                    ? $" {WarningColorTag}Raises levels while the player is struggling.</color>"
                    : $" {WarningColorTag}Lowers levels while the player is doing well.</color>";
            }
            if (action.Enabled == false) {
                tag = "Off";
                tagColor = InactiveColor;
            }
            return text;
        }

        private static string LevelSuffix(int levelBonus) {
            return levelBonus == 0 ? "" : $" ({(levelBonus > 0 ? "+" : "-")}{Mathf.Abs(levelBonus)} levels)";
        }

        private static string DescribeSpawns(List<NemesisSpawn> spawns) {
            if (spawns == null || spawns.Count == 0) { return "nothing"; }
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
            if (allowed != null && allowed.Count > 0) { parts.Add("in " + string.Join(", ", allowed.Select(BiomeName).ToArray())); }
            List<Heightmap.Biome> denied = source.DeniedBiomes?.Where(b => b != Heightmap.Biome.None).ToList();
            if (denied != null && denied.Count > 0) { parts.Add("not in " + string.Join(", ", denied.Select(BiomeName).ToArray())); }
            if (source.RequiredGlobalKeys != null && source.RequiredGlobalKeys.Count > 0) {
                parts.Add("after " + string.Join(", ", source.RequiredGlobalKeys.Select(BossKeyName).ToArray()));
            }
            if (source.NotRequiredGlobalKeys != null && source.NotRequiredGlobalKeys.Count > 0) {
                // Blocked once any of these is defeated, so the earliest one in progression is the one that matters.
                List<string> order = LevelSystemData.VanillaBossKeyOrder;
                string earliest = source.NotRequiredGlobalKeys
                    .OrderBy(k => { int i = order.FindIndex(o => string.Equals(o, k, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; })
                    .First();
                parts.Add("only before " + BossKeyName(earliest));
            }
            return parts.Count == 0 ? "" : "(" + string.Join("; ", parts.ToArray()) + ")";
        }

        private static string BossKeyName(string key) {
            return key != null && BossKeyNames.TryGetValue(key, out string name) ? name : key;
        }

        private static string BiomeName(Heightmap.Biome biome) {
            switch (biome) {
                case Heightmap.Biome.AshLands: return "Ashlands";
                case Heightmap.Biome.BlackForest: return "Black Forest";
                case Heightmap.Biome.DeepNorth: return "Deep North";
                default: return biome.ToString();
            }
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
