using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Loot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    // The Loot page of the quick configure panel: how much more a creature drops for every star it has, the same for
    // trees, rocks, destructibles and birds, and the distance rings that add to all of it. Which creatures and objects
    // have a custom drop table at all stays in LootSettings.yaml.
    //
    // The right half works the numbers through on a real drop table so the sliders can be judged by what they produce
    // rather than by their own value: every drop at 0 stars, at Max stars (the level range set on the previous page)
    // and, while distance loot is on, at the outermost ring. It mirrors LootPatches.DetermineLootScale and
    // LootStyles.ModifyCharacterDrops rather than approximating them, so what it shows is what the drop path does.
    internal static partial class QuickConfigureTool {

        // The creature the estimates are worked on. Its drop table is read from the live prefab, so the estimate is of
        // this world's Troll rather than of a remembered one.
        private const string LootSampleCreature = "Troll";

        private static readonly string[] LootStyleOptions = Enum.GetNames(typeof(LootFactorType));

        // Loot per star is unbounded in the config, but a slider needs an end; 4 is the ConfigEntry's own ceiling.
        private const float MaxPerLevelLootScale = 4f;
        private const float MaxObjectLootScale = 4f;
        private const float MaxRingBonus = 5f;

        private static Text lootStyleNote;
        private static LootTableView lootTable;
        private static Text lootWarningText;
        // Rows that only apply to one loot style. Hidden rather than laid out again: the column is a scroll view, so a
        // vertical layout group collapses the space an inactive child took.
        private static readonly List<GameObject> lootChanceRampRows = new List<GameObject>();
        // The ring header and its rows, hidden together while distance loot is off.
        private static readonly List<GameObject> lootRingRows = new List<GameObject>();
        // The drop table the estimate is worked on, and what it came from, resolved once when the page is built.
        private static List<LootSampleDrop> lootSample;
        private static bool lootSampleIsLive;

        private static void ClearLootPageReferences() {
            lootStyleNote = null;
            lootTable = null;
            lootWarningText = null;
            lootChanceRampRows.Clear();
            lootRingRows.Clear();
            lootSample = null;
            lootSampleIsLive = false;
        }

        // ------------------------------------------------------------------------------------------------
        //  Staged configuration
        // ------------------------------------------------------------------------------------------------

        // One distance ring from LootSettings.yaml. The distance itself is not editable here: the rings are shared with
        // everything else that reads DistanceLootModifier, and moving a boundary is a different decision from tuning
        // what it grants.
        private class StagedLootRing {
            internal int Distance;
            internal float MinBonus;
            internal float MaxBonus;
            internal float ChanceBonus;

            internal bool SameAs(StagedLootRing other) {
                return other != null && Distance == other.Distance && MinBonus == other.MinBonus
                    && MaxBonus == other.MaxBonus && ChanceBonus == other.ChanceBonus;
            }
        }

        private class StagedLoot {
            // BepInEx ConfigEntries: the LootSystem section, then the per-level scales for world objects.
            internal LootFactorType style;
            internal float perLevelScale;
            internal float perLevelChanceScale;
            internal float chanceBase;
            internal bool scaleAllLoot;
            internal bool eggStacks;
            internal float treeScale, rockScale, destructibleScale, birdScale;

            // LootSettings.yaml.
            internal bool distanceBonus;
            internal List<StagedLootRing> rings;

            // The furthest ring that grants anything, or null when distance loot is off or none is configured. It is a
            // band, not everything past its distance: SelectDistanceFromCenterLootBonus takes the first ring whose
            // distance covers the kill, and a kill beyond the last one falls outside them all and gets no bonus.
            internal StagedLootRing OutermostRing => distanceBonus && rings.Count > 0 ? rings[rings.Count - 1] : null;

            // The ring a kill near the world center falls in: the first one covers everything from 0 out to its distance.
            internal StagedLootRing InnermostRing => distanceBonus && rings.Count > 0 ? rings[0] : null;

            internal static StagedLoot Snapshot() {
                if (Enum.TryParse(ValConfig.LootDropCalculationType.Value, out LootFactorType parsed) == false) {
                    parsed = LootFactorType.PerLevel;
                }
                StagedLoot s = new StagedLoot {
                    style = parsed,
                    perLevelScale = ValConfig.PerLevelLootScale.Value,
                    perLevelChanceScale = ValConfig.PerLevelLootChanceScale.Value,
                    chanceBase = ValConfig.ChanceBaseChancePerLevel.Value,
                    scaleAllLoot = ValConfig.ScaleAllLootByLevel.Value,
                    eggStacks = ValConfig.LootEggsDropIncreaseStacks.Value,
                    treeScale = ValConfig.PerLevelTreeLootScale.Value,
                    rockScale = ValConfig.PerLevelMineRockLootScale.Value,
                    destructibleScale = ValConfig.PerLevelDestructibleLootScale.Value,
                    birdScale = ValConfig.PerLevelBirdLootScale.Value,
                    rings = new List<StagedLootRing>(),
                };
                LootSettings loot = LootSystemData.SLS_Drop_Settings;
                s.distanceBonus = loot != null && loot.EnableDistanceLootModifier;
                if (loot?.DistanceLootModifier != null) {
                    foreach (KeyValuePair<int, DistanceLootModifier> ring in loot.DistanceLootModifier) {
                        if (ring.Value == null) { continue; }
                        s.rings.Add(new StagedLootRing {
                            Distance = ring.Key,
                            MinBonus = ring.Value.MinAmountScaleFactorBonus,
                            MaxBonus = ring.Value.MaxAmountScaleFactorBonus,
                            ChanceBonus = ring.Value.ChanceScaleFactorBonus,
                        });
                    }
                }
                return s;
            }

            // Everything that lives in LootSettings.yaml.
            internal bool YamlMatches(StagedLoot o) {
                if (distanceBonus != o.distanceBonus || rings.Count != o.rings.Count) { return false; }
                for (int i = 0; i < rings.Count; i++) {
                    if (rings[i].SameAs(o.rings[i]) == false) { return false; }
                }
                return true;
            }

            internal bool Matches(StagedLoot o) {
                return style == o.style && perLevelScale == o.perLevelScale && perLevelChanceScale == o.perLevelChanceScale
                    && chanceBase == o.chanceBase && scaleAllLoot == o.scaleAllLoot && eggStacks == o.eggStacks
                    && treeScale == o.treeScale && rockScale == o.rockScale
                    && destructibleScale == o.destructibleScale && birdScale == o.birdScale
                    && YamlMatches(o);
            }
        }

        // ------------------------------------------------------------------------------------------------
        //  Page
        // ------------------------------------------------------------------------------------------------

        private static void BuildLootPage(Transform parent) {
            const float IntroH = 44f;
            const float LeftW = 430f;
            const float RightX = 446f;
            const float LabelW = 200f, SliderW = 130f, ValueW = 56f;
            const float ToggleLabelW = 300f;
            float rightW = PageW - RightX;
            StagedLoot lt = staged.loot;

            GameObject intro = ConfigUI.AddTextRow(parent, PageW, IntroH, "$sls_cfg_loot_intro", 13, GUIManager.Instance.ValheimBeige, TextAnchor.UpperCenter);
            ConfigUI.PositionRow(intro, 0f, 0f);
            float colY = IntroH + 6f;
            float colH = PageH - colY;

            ResolveLootSample();

            // Left - everything that scales loot with a level, then the distance rings that add to it.
            ConfigUI.CreateScroll(parent, 0f, colY, LeftW, colH, out Transform left, out float lw);

            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Creature loot"));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddEnumCycleRow(t, lw, LabelW, 150f, "Loot scaling style", LootStyleOptions, (int)lt.style, i => {
                lt.style = (LootFactorType)i;
                RefreshLootEstimates();
            }), Tip(ValConfig.LootDropCalculationType)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Loot per star", 0f, MaxPerLevelLootScale, lt.perLevelScale, false, v => {
                lt.perLevelScale = v;
                RefreshLootEstimates();
            }), Tip(ValConfig.PerLevelLootScale)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Drop chance per level", 0f, 1f, lt.perLevelChanceScale, false, v => {
                lt.perLevelChanceScale = v;
                RefreshLootEstimates();
            }), Tip(ValConfig.PerLevelLootChanceScale)));
            lootChanceRampRows.Add(ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Base drop chance", 0f, 1f, lt.chanceBase, false, v => {
                lt.chanceBase = v;
                RefreshLootEstimates();
            }), Tip(ValConfig.ChanceBaseChancePerLevel))));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, ToggleLabelW, "Scale loot that never scales", lt.scaleAllLoot, v => {
                lt.scaleAllLoot = v;
                RefreshLootEstimates();
            }), Tip(ValConfig.ScaleAllLootByLevel)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, ToggleLabelW, "More eggs, not better eggs", lt.eggStacks, v => lt.eggStacks = v), Tip(ValConfig.LootEggsDropIncreaseStacks)));
            lootStyleNote = ScrollRow(left, lw, 60f, t => ConfigUI.AddTextRow(t, lw, 60f, "", 12, GUIManager.Instance.ValheimBeige)).GetComponentInChildren<Text>();

            // World objects carry their own per-level scale and their own shape: each is `1 + scale * level` applied to
            // the object's vanilla drop table, not the creature multiplier above.
            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Trees, rocks and the rest"));
            ScrollRow(left, lw, 34f, t => ConfigUI.AddTextRow(t, lw, 34f, "$sls_cfg_loot_objects_help", 12, GUIManager.Instance.ValheimBeige));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Tree wood per level", 0f, MaxObjectLootScale, lt.treeScale, false, v => lt.treeScale = v), Tip(ValConfig.PerLevelTreeLootScale)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Rock ore per level", 0f, MaxObjectLootScale, lt.rockScale, false, v => lt.rockScale = v), Tip(ValConfig.PerLevelMineRockLootScale)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Destructible loot per level", 0f, MaxObjectLootScale, lt.destructibleScale, false, v => lt.destructibleScale = v), Tip(ValConfig.PerLevelDestructibleLootScale)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Bird loot per level", 0f, MaxObjectLootScale, lt.birdScale, false, v => lt.birdScale = v), Tip(ValConfig.PerLevelBirdLootScale)));

            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Distance bonus"));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, ToggleLabelW, "Loot rises with distance", lt.distanceBonus, v => {
                lt.distanceBonus = v;
                RefreshLootEstimates();
            }), YamlTip(typeof(LootSettings), nameof(LootSettings.EnableDistanceLootModifier),
                "Adds a bonus to loot scaling the further a kill is from the world center. These rings are LootSettings.yaml's own - they are " +
                "not the distance rings the level system uses, and they are not drawn on the map.")));
            if (lt.rings.Count == 0) {
                ScrollRow(left, lw, 40f, t => ConfigUI.AddTextRow(t, lw, 40f, "$sls_cfg_loot_no_rings", 12, GUIManager.Instance.ValheimOrange));
            } else {
                lootRingRows.Add(ScrollRow(left, lw, 46f, t => ConfigUI.AddTextRow(t, lw, 46f, "$sls_cfg_loot_rings_help", 12, GUIManager.Instance.ValheimBeige)));
                lootRingRows.Add(AddLootRingHeaderRow(left, lw));
                foreach (StagedLootRing ring in lt.rings) { lootRingRows.Add(AddLootRingRow(left, lw, ring)); }
            }

            // Right - the estimate.
            GameObject header = ConfigUI.AddHeaderRow(parent, rightW, "Loot estimate");
            ConfigUI.PositionRow(header, RightX, colY);
            float estimateY = colY + RowHeight + RowGap;
            lootWarningText = ConfigUI.AddText(parent, RightX, estimateY, rightW, 40f, "", 12, TextAnchor.UpperLeft, GUIManager.Instance.ValheimOrange);
            estimateY += 44f;
            lootTable = BuildLootTable(parent, RightX, estimateY, rightW, PageH - estimateY);
            WithTip(lootTable.Root, Tip("Loot estimate",
                "Every drop the sample creature has, at no stars and at Max stars from the Level Distribution page, near the world " +
                "center; with distance loot on, a last column works the same kill inside the furthest ring. Amounts are the drop's own " +
                "min-max carried through the multiplier - vanilla rolls that range with the top end exclusive, so the real maximum of a " +
                "wide range is one step lower. A % under an amount is its chance to drop at all; the pseudo-random streak vanilla applies " +
                "to drops under 30% is not modelled, so a rare drop lands a little more evenly than its number suggests."));

            RefreshLootEstimates();
        }

        private const float RingDistanceX = 4f;
        private const float RingDistanceW = 86f;
        private const float RingMinX = 96f;
        private const float RingMaxX = 158f;
        private const float RingChanceX = 220f;
        private const float RingFieldW = 56f;

        private static GameObject AddLootRingHeaderRow(Transform content, float width) {
            GameObject row = ConfigUI.NewLayoutRow(content, width, 22f);
            Color color = GUIManager.Instance.ValheimYellow;
            ConfigUI.AddText(row.transform, RingDistanceX, 0f, RingDistanceW, 22f, "Within", 11, TextAnchor.MiddleLeft, color);
            ConfigUI.AddText(row.transform, RingMinX, 0f, RingFieldW, 22f, "Min", 11, TextAnchor.MiddleLeft, color);
            ConfigUI.AddText(row.transform, RingMaxX, 0f, RingFieldW, 22f, "Max", 11, TextAnchor.MiddleLeft, color);
            ConfigUI.AddText(row.transform, RingChanceX, 0f, RingFieldW + 10f, 22f, "Chance", 11, TextAnchor.MiddleLeft, color);
            return row;
        }

        private static GameObject AddLootRingRow(Transform content, float width, StagedLootRing ring) {
            GameObject row = ConfigUI.NewLayoutRow(content, width, 32f);
            ConfigUI.AddText(row.transform, RingDistanceX, 0f, RingDistanceW, 28f, $"{ring.Distance} m", 12, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimBeige);

            AddLootRingField(row.transform, RingMinX, ring.MinBonus, v => ring.MinBonus = v,
                YamlTip(typeof(DistanceLootModifier), nameof(DistanceLootModifier.MinAmountScaleFactorBonus),
                    "Added to the loot scale at the low end of the roll for a kill inside this ring."));
            AddLootRingField(row.transform, RingMaxX, ring.MaxBonus, v => ring.MaxBonus = v,
                YamlTip(typeof(DistanceLootModifier), nameof(DistanceLootModifier.MaxAmountScaleFactorBonus),
                    "Added to the loot scale at the high end of the roll. Set it above Min for a wider spread out here."));
            AddLootRingField(row.transform, RingChanceX, ring.ChanceBonus, v => ring.ChanceBonus = v,
                YamlTip(typeof(DistanceLootModifier), nameof(DistanceLootModifier.ChanceScaleFactorBonus),
                    "Added to the per-level drop chance ramp in this ring, so uncertain drops land more often out here."));
            return row;
        }

        private static void AddLootRingField(Transform parent, float x, float value, Action<float> onCommit, string tooltip) {
            InputField field = null;
            field = ConfigUI.AddTextField(parent, x, 0f, RingFieldW, FormatLootBonus(value), text => {
                float committed = value;
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) {
                    committed = Mathf.Clamp(parsed, 0f, MaxRingBonus);
                }
                value = committed;
                onCommit(committed);
                field.SetTextWithoutNotify(FormatLootBonus(committed));
                RefreshLootEstimates();
            }, InputField.ContentType.DecimalNumber);
            WithTip(field.gameObject, tooltip);
        }

        private static string FormatLootBonus(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------------------------------------
        //  Estimates
        // ------------------------------------------------------------------------------------------------

        private static void RefreshLootEstimates() {
            if (staged?.loot == null) { return; }
            StagedLoot lt = staged.loot;

            // Base drop chance only means anything to the style that reads it.
            bool chanceRamp = lt.style == LootFactorType.ChancePerLevel;
            foreach (GameObject row in lootChanceRampRows) { row.SetActive(chanceRamp); }
            foreach (GameObject row in lootRingRows) { row.SetActive(lt.distanceBonus); }

            if (lootStyleNote != null) { lootStyleNote.text = DescribeLootStyle(lt); }
            if (lootWarningText != null) { lootWarningText.text = LootWarning(lt); }
            RefreshLootTable(lt);
        }

        // What the selected style does, in the terms the sliders above are labelled in.
        private static string DescribeLootStyle(StagedLoot lt) {
            string scale = FormatLootBonus(lt.perLevelScale);
            switch (lt.style) {
                case LootFactorType.Exponential:
                    return $"Exponential: a creature's drops are multiplied by (1 + {scale}) once per star, so late stars are " +
                        "worth far more than early ones. Drop chances follow the same multiplier.";
                case LootFactorType.ChancePerLevel:
                    return $"ChancePerLevel: amounts are multiplied by level x {scale}, and an uncertain drop's chance by " +
                        $"{FormatLootBonus(lt.chanceBase)} + {FormatLootBonus(lt.perLevelChanceScale)} per level - so low stars " +
                        "can drop less often than vanilla, and high stars almost always.";
                default:
                    return $"PerLevel: a creature's drops are multiplied by level x {scale}, and an uncertain drop's chance by " +
                        "the same amount. Drop chance per level only reaches creatures with a table in LootSettings.yaml.";
            }
        }

        // One line above the estimate for the thing most worth knowing about these numbers right now, if anything.
        private static string LootWarning(StagedLoot lt) {
            if (lt.perLevelScale <= 0f) {
                return "Loot per star is 0, so every creature drops its base amount at any star level.";
            }
            // Exponential growth reaches this within a dozen stars at the default scale, and everything past it is cut,
            // which is worth saying out loud before the estimate is read as the intended reward.
            LootScale top = LootScaleAt(lt, Mathf.Max(0, staged.maxStars) + 1, lt.OutermostRing);
            if (top.AmountMax >= LootStyles.MaxLootScaleResult) {
                return $"At {Stars(Mathf.Max(0, staged.maxStars))} this style reaches the " +
                    $"{LootStyles.MaxLootScaleResult.ToString("#,0", CultureInfo.InvariantCulture)}x ceiling loot scaling is clamped to, " +
                    "so the top of the range is cut rather than earned.";
            }
            if (lt.style == LootFactorType.ChancePerLevel) {
                float atFirst = LootScaleAt(lt, 1, lt.InnermostRing).ChanceMin;
                if (atFirst < 1f) {
                    return $"At 0 stars this style multiplies an uncertain drop's chance by {atFirst:0.##}, so those drops are " +
                        "rarer than vanilla until a creature has a few stars.";
                }
            }
            if (lt.style != LootFactorType.ChancePerLevel && lt.perLevelScale < 1f) {
                // The chance branch shares the amount multiplier here, and DetermineLootScale floors it at 1, so a
                // scale under 1 leaves the low levels on their vanilla chances rather than lowering them.
                return "Below 1, loot per star cannot lower a drop's chance: the multiplier never falls under 1x.";
            }
            return "";
        }

        // ------------------------------------------------------------------------------------------------
        //  Estimate table
        // ------------------------------------------------------------------------------------------------

        // A label column and three value columns: no stars, Max stars, and Max stars inside the furthest ring. Built once
        // for the sample's rows; RefreshLootTable fills it in and drops the ring column while there is no ring to show,
        // handing its width to the other two. Real cells rather than one block of text, because the font is not
        // monospaced and padded text would not line up.
        private class LootTableView {
            internal GameObject Root;
            internal float Width;
            internal Text[] Header;
            // Two multiplier rows, then one row per drop in Drops. Each is the label cell and three value cells.
            internal readonly List<Text[]> Rows = new List<Text[]>();
            internal List<LootSampleDrop> Drops;
            internal Text Footer;
        }

        private const float LootTableLabelW = 140f;
        private const float LootTableCellPad = 6f;
        private const float LootTableHeaderH = 36f;
        private const float LootTableMultiplierRowH = 24f;
        private const float LootTableDropRowH = 36f;
        private const int LootTableAmountRow = 0;
        private const int LootTableChanceRow = 1;
        private const int LootTableFirstDropRow = 2;

        private static readonly Color LootTableHeaderShade = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color LootTableStripeShade = new Color(0f, 0f, 0f, 0.2f);
        private static readonly Color LootTableRuleColor = new Color(0.6f, 0.5f, 0.35f, 0.6f);

        private static LootTableView BuildLootTable(Transform parent, float x, float y, float w, float h) {
            LootTableView view = new LootTableView { Root = ConfigUI.NewRect("LootEstimate", parent, x, y, w, h), Width = w };
            Transform t = view.Root.transform;
            Color beige = GUIManager.Instance.ValheimBeige;
            float rowY = 0f;

            AddLootTableShade(t, rowY, w, LootTableHeaderH, LootTableHeaderShade);
            view.Header = AddLootTableRow(t, rowY, LootTableHeaderH, 13, GUIManager.Instance.ValheimYellow);
            rowY += LootTableHeaderH;

            string[] multiplierLabels = { "Amount multiplier", "Chance multiplier" };
            for (int i = 0; i < multiplierLabels.Length; i++) {
                if (i % 2 == 1) { AddLootTableShade(t, rowY, w, LootTableMultiplierRowH, LootTableStripeShade); }
                Text[] row = AddLootTableRow(t, rowY, LootTableMultiplierRowH, 12, beige);
                row[0].text = multiplierLabels[i];
                view.Rows.Add(row);
                rowY += LootTableMultiplierRowH;
            }

            // A rule between the multipliers and what they produce.
            AddLootTableShade(t, rowY + 3f, w, 2f, LootTableRuleColor);
            rowY += 8f;

            view.Drops = lootSample.Take(MaxEstimatedDrops).ToList();
            for (int i = 0; i < view.Drops.Count; i++) {
                if (i % 2 == 1) { AddLootTableShade(t, rowY, w, LootTableDropRowH, LootTableStripeShade); }
                Text[] row = AddLootTableRow(t, rowY, LootTableDropRowH, 13, beige);
                row[0].text = view.Drops[i].Name;
                row[0].color = GUIManager.Instance.ValheimOrange;
                view.Rows.Add(row);
                rowY += LootTableDropRowH;
            }

            rowY += 6f;
            view.Footer = ConfigUI.AddText(t, 0f, rowY, w, Mathf.Max(0f, h - rowY), "", 12, TextAnchor.UpperLeft, beige);
            return view;
        }

        // The label cell and the three value cells of one row. RefreshLootTable lays the value cells out for however many
        // columns it needs, so they start with no width.
        private static Text[] AddLootTableRow(Transform table, float y, float h, int fontSize, Color color) {
            Text[] cells = new Text[4];
            cells[0] = ConfigUI.AddText(table, LootTableCellPad, y, LootTableLabelW - LootTableCellPad, h, "", fontSize, TextAnchor.MiddleLeft, color);
            for (int c = 1; c < cells.Length; c++) {
                cells[c] = ConfigUI.AddText(table, 0f, y, 0f, h, "", fontSize, TextAnchor.MiddleCenter, color);
            }
            // A long item name, or an Exponential amount in the millions, shrinks to fit its cell rather than wrapping
            // out of the bottom of the row.
            foreach (Text cell in cells) {
                cell.resizeTextForBestFit = true;
                cell.resizeTextMinSize = 9;
                cell.resizeTextMaxSize = fontSize;
            }
            return cells;
        }

        // Row shading and the rule. Added ahead of the row's cells, so it draws behind them.
        private static void AddLootTableShade(Transform table, float y, float w, float h, Color color) {
            GameObject shade = ConfigUI.NewRect("Shade", table, 0f, y, w, h);
            Image image = shade.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void LayoutLootTableColumns(LootTableView view, int columns) {
            float cellW = (view.Width - LootTableLabelW) / columns;
            foreach (Text[] row in new[] { view.Header }.Concat(view.Rows)) {
                for (int c = 1; c < row.Length; c++) {
                    Text cell = row[c];
                    bool shown = c <= columns;
                    cell.gameObject.SetActive(shown);
                    if (shown == false) { continue; }
                    RectTransform rt = cell.rectTransform;
                    rt.anchoredPosition = new Vector2(LootTableLabelW + (c - 1) * cellW, rt.anchoredPosition.y);
                    rt.sizeDelta = new Vector2(cellW, rt.sizeDelta.y);
                }
            }
        }

        private static void RefreshLootTable(StagedLoot lt) {
            LootTableView view = lootTable;
            if (view == null || lootSample == null) { return; }
            int maxStars = Mathf.Max(0, staged.maxStars);
            int topLevel = maxStars + 1;
            // The first two columns are a kill near the world center, which is inside the first ring while distance loot
            // is on. The third is only worth a column when it is a different ring.
            StagedLootRing near = lt.InnermostRing;
            StagedLootRing far = lt.OutermostRing;
            bool farColumn = far != null && far != near;
            LayoutLootTableColumns(view, farColumn ? 3 : 2);

            view.Header[0].text = lootSampleIsLive ? $"{LootSampleCreature} drops" : "Stand-in drops";
            view.Header[1].text = "No stars";
            view.Header[2].text = Stars(maxStars);
            view.Header[3].text = farColumn ? $"{Stars(maxStars)}\n<size=11>{far.Distance}m ring</size>" : "";

            int[] levels = { 1, topLevel, topLevel };
            StagedLootRing[] rings = { near, near, far };
            for (int c = 0; c < levels.Length; c++) {
                LootScale scale = LootScaleAt(lt, levels[c], rings[c]);
                view.Rows[LootTableAmountRow][c + 1].text = MultiplierText(scale.AmountMin, scale.AmountMax);
                view.Rows[LootTableChanceRow][c + 1].text = MultiplierText(scale.ChanceMin, scale.ChanceMax);
                for (int d = 0; d < view.Drops.Count; d++) {
                    view.Rows[LootTableFirstDropRow + d][c + 1].text = LootDropCell(view.Drops[d], lt, levels[c], rings[c]);
                }
            }

            List<string> notes = new List<string>();
            if (lootSample.Count > view.Drops.Count) {
                notes.Add($"...and {lootSample.Count - view.Drops.Count} more drop(s) in its table.");
            }
            if (lootSampleIsLive == false) {
                notes.Add($"{WarningColorTag}The {LootSampleCreature} prefab is only there once a world is loaded, so these are " +
                    $"stand-in drops. Reopen this page in game to see the {LootSampleCreature}'s own table.</color>");
            }
            view.Footer.text = string.Join("\n", notes.ToArray());
        }

        // As many drop rows as fit the estimate table beside the settings column.
        private const int MaxEstimatedDrops = 6;

        private static string MultiplierText(float min, float max) {
            return Mathf.Approximately(min, max)
                ? $"x{min.ToString("0.##", CultureInfo.InvariantCulture)}"
                : $"x{min.ToString("0.##", CultureInfo.InvariantCulture)}-{max.ToString("0.##", CultureInfo.InvariantCulture)}";
        }

        // The amount multiplier every drop on a creature without its own table shares, and what its chance is scaled
        // by. Mirrors LootPatches.DetermineLootScale, including its rounding and its floor of 1.
        private struct LootScale {
            internal float AmountMin, AmountMax;
            internal float ChanceMin, ChanceMax;
        }

        private static LootScale LootScaleAt(StagedLoot lt, int level, StagedLootRing ring) {
            float minBonus = ring?.MinBonus ?? 0f;
            float maxBonus = ring?.MaxBonus ?? 0f;
            float chanceBonus = ring?.ChanceBonus ?? 0f;
            LootScale s = new LootScale { AmountMin = 1f, AmountMax = 1f, ChanceMin = 1f, ChanceMax = 1f };

            // The chance ramp is written before the level-1 shortcut, so it applies at 0 stars too.
            if (lt.style == LootFactorType.ChancePerLevel) {
                float ramp = lt.chanceBase + (lt.perLevelChanceScale + chanceBonus) * level;
                s.ChanceMin = ramp;
                s.ChanceMax = ramp;
            }
            if (level > 1) {
                if (lt.style == LootFactorType.Exponential) {
                    s.AmountMin = Mathf.Pow(1f + lt.perLevelScale + minBonus, level - 1);
                    s.AmountMax = Mathf.Pow(1f + lt.perLevelScale + maxBonus, level - 1);
                } else {
                    s.AmountMin = level * (minBonus + lt.perLevelScale);
                    s.AmountMax = level * (maxBonus + lt.perLevelScale);
                }
                s.AmountMin = RoundLootScale(s.AmountMin);
                s.AmountMax = RoundLootScale(s.AmountMax);
            }
            // Every style but ChancePerLevel keeps vanilla's behaviour of scaling the chance by the amount multiplier.
            if (lt.style != LootFactorType.ChancePerLevel) {
                s.ChanceMin = s.AmountMin;
                s.ChanceMax = s.AmountMax;
            }
            return s;
        }

        private static float RoundLootScale(float value) {
            return Mathf.Max(1f, Mathf.Round(Mathf.Min(value, LootStyles.MaxLootScaleResult)));
        }

        // One drop's cell at this level: the amount range, with its chance under it when the drop is not certain.
        private static string LootDropCell(LootSampleDrop drop, StagedLoot lt, int level, StagedLootRing ring) {
            int baseMin = Mathf.Max(0, drop.Min);
            int baseMax = Mathf.Max(baseMin, drop.Max);
            // A ScalebyMaxLevel drop walks its own range with the level instead of rolling inside it.
            if (drop.ScaleByMaxLevel && baseMax != baseMin) {
                int maxLevel = Mathf.Max(1, staged.maxStars);
                baseMin = baseMax = Mathf.RoundToInt((drop.Max - drop.Min) / (float)maxLevel * level);
            }

            float amountMin, amountMax, chance;
            if (drop.Custom) {
                CustomLootScale(drop, lt, level, ring, out amountMin, out amountMax);
                chance = CustomLootChance(drop, lt, level, ring);
            } else {
                LootScale s = LootScaleAt(lt, level, ring);
                // The amount branch also scales a drop that opts out, once ScaleAllLootByLevel is on; the chance
                // branch keeps vanilla's gate either way.
                bool scales = drop.ScalesWithLevel || lt.scaleAllLoot;
                amountMin = scales ? s.AmountMin : 1f;
                amountMax = scales ? s.AmountMax : 1f;
                // The low end of the multiplier: the chance branch reads the same single roll the amounts do, so a ring
                // with a wider Min/Max spread makes it a range too. The chance multiplier row shows that range in full.
                chance = drop.Chance * (drop.ScalesWithLevel ? s.ChanceMin : 1f);
            }

            int low = Mathf.RoundToInt(Mathf.Min(baseMin * amountMin, LootStyles.MaxLootScaleResult));
            int high = Mathf.RoundToInt(Mathf.Min(baseMax * amountMax, LootStyles.MaxLootScaleResult));
            if (drop.MaxScaledAmount > 0) {
                low = Mathf.Min(low, drop.MaxScaledAmount);
                high = Mathf.Min(high, drop.MaxScaledAmount);
            }

            string amount = low == high ? low.ToString() : $"{low}-{high}";
            chance = Mathf.Clamp01(chance);
            if (chance >= 0.999f) { return amount; }
            if (chance <= 0f) { return "never"; }
            return $"{amount}\n<size=11>{chance * 100f:0.#}%</size>";
        }

        // The multiplier a drop from a creature's own LootSettings.yaml table gets: its per-drop scale factor rather
        // than the shared one. Mirrors MultiplyLootPerLevel and ExponentLootPerLevel.
        private static void CustomLootScale(LootSampleDrop drop, StagedLoot lt, int level, StagedLootRing ring, out float min, out float max) {
            min = 1f;
            max = 1f;
            if (drop.ScalesWithLevel == false || level <= 1) { return; }
            float minBonus = ring?.MinBonus ?? 0f;
            float maxBonus = ring?.MaxBonus ?? 0f;
            float factor = drop.AmountScaleFactor * (drop.UseChanceAsMultiplier ? drop.Chance * level : 1f);
            if (factor <= 0f) { factor = 1f; }
            if (lt.style == LootFactorType.Exponential) {
                min = Mathf.Pow(lt.perLevelScale + minBonus + factor, level - 1);
                max = Mathf.Pow(lt.perLevelScale + maxBonus + factor, level - 1);
            } else {
                min = level * lt.perLevelScale * (factor + minBonus);
                max = level * lt.perLevelScale * (factor + maxBonus);
            }
            min = Mathf.Min(min, LootStyles.MaxLootScaleResult);
            max = Mathf.Min(max, LootStyles.MaxLootScaleResult);
        }

        private static float CustomLootChance(LootSampleDrop drop, StagedLoot lt, int level, StagedLootRing ring) {
            if (drop.Chance >= 1f) { return drop.Chance; }
            float scale = drop.ChanceScaleFactor + (ring?.ChanceBonus ?? 0f);
            if (lt.style == LootFactorType.PerLevel || lt.style == LootFactorType.ChancePerLevel) { scale += lt.perLevelChanceScale; }
            return scale > 0f ? drop.Chance * (1f + scale * level) : drop.Chance;
        }

        // ------------------------------------------------------------------------------------------------
        //  The sample drop table
        // ------------------------------------------------------------------------------------------------

        // One line of the table the estimate is worked on.
        private class LootSampleDrop {
            internal string Name;
            internal int Min = 1;
            internal int Max = 1;
            internal float Chance = 1f;
            internal bool ScalesWithLevel = true;
            // Set for a drop that comes from a creature's own table in LootSettings.yaml. Those scale by their own
            // per-drop factors instead of the multiplier the whole creature shares.
            internal bool Custom;
            internal float AmountScaleFactor;
            internal float ChanceScaleFactor;
            internal bool UseChanceAsMultiplier;
            internal bool ScaleByMaxLevel;
            internal int MaxScaledAmount;
        }

        // Stands in for the sample creature when its prefab is not loaded - the panel also opens on the main menu,
        // where there is no ZNetScene to read a drop table from. A certain stack and an uncertain trophy, which is
        // enough to show what the multipliers do to each kind of drop.
        private static List<LootSampleDrop> LootSampleStandIn() {
            return new List<LootSampleDrop>() {
                new LootSampleDrop { Name = "A 3-5 stack, always dropped", Min = 3, Max = 5 },
                new LootSampleDrop { Name = "A trophy, 1 at 25%", Chance = 0.25f },
            };
        }

        // The sample table: the creature's own LootSettings.yaml entry when it has one, else its live drop table, else
        // the stand-in. lootSampleIsLive says whether what is shown came from the world or from the stand-in.
        private static void ResolveLootSample() {
            lootSampleIsLive = false;
            lootSample = null;
            try {
                List<ExtendedCharacterDrop> custom = null;
                Dictionary<string, List<ExtendedCharacterDrop>> tables = LootSystemData.SLS_Drop_Settings?.CharacterSpecificLoot;
                if (tables != null) { tables.TryGetValue(LootSampleCreature, out custom); }
                if (custom != null && custom.Count > 0) {
                    lootSample = custom.Select(d => ToLootSample(d)).ToList();
                    lootSampleIsLive = true;
                    return;
                }

                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(LootSampleCreature) : null;
                CharacterDrop drops = prefab != null ? prefab.GetComponent<CharacterDrop>() : null;
                if (drops?.m_drops != null) {
                    lootSample = drops.m_drops.Where(d => d != null && d.m_prefab != null).Select(d => ToLootSample(d)).ToList();
                    lootSampleIsLive = lootSample.Count > 0;
                }
            } catch (Exception e) {
                Logger.LogDebug($"QuickConfigureTool could not read the {LootSampleCreature} drop table: {e.Message}");
                lootSample = null;
                lootSampleIsLive = false;
            }
            if (lootSampleIsLive == false) { lootSample = LootSampleStandIn(); }
        }

        private static LootSampleDrop ToLootSample(CharacterDrop.Drop drop) {
            return new LootSampleDrop {
                Name = LootDropName(drop.m_prefab, drop.m_prefab.name),
                Min = drop.m_amountMin,
                Max = drop.m_amountMax,
                Chance = drop.m_chance,
                // m_dontScale only picks the roll vanilla uses for the base amount; m_levelMultiplier is the gate
                // the level scaling itself sits behind.
                ScalesWithLevel = drop.m_levelMultiplier,
            };
        }

        private static LootSampleDrop ToLootSample(ExtendedCharacterDrop drop) {
            Drop d = drop.Drop ?? new Drop();
            return new LootSampleDrop {
                Name = LootDropName(drop.GameDrop?.m_prefab, d.Prefab),
                Min = d.Min,
                Max = d.Max,
                Chance = d.Chance,
                ScalesWithLevel = drop.DoesNotScale == false && d.DontScale == false,
                Custom = true,
                AmountScaleFactor = drop.AmountScaleFactor,
                ChanceScaleFactor = drop.ChanceScaleFactor,
                UseChanceAsMultiplier = drop.UseChanceAsMultiplier,
                ScaleByMaxLevel = drop.ScalebyMaxLevel,
                MaxScaledAmount = drop.MaxScaledAmount,
            };
        }

        // An item's own display name where the prefab is loaded, so the estimate reads like the player's inventory.
        private static string LootDropName(GameObject prefab, string fallback) {
            try {
                ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                string shared = item?.m_itemData?.m_shared?.m_name;
                if (string.IsNullOrEmpty(shared) == false) { return ConfigUI.L(shared); }
            } catch (Exception e) {
                Logger.LogDebug($"QuickConfigureTool could not name the drop {fallback}: {e.Message}");
            }
            return PrettyPrefab(fallback);
        }

        // ------------------------------------------------------------------------------------------------
        //  Save
        // ------------------------------------------------------------------------------------------------

        // Only the distance section lives in the YAML file; the per-level scales are all ConfigEntries, written in
        // SaveStaged with the rest of them.
        private static void SaveLoot(List<string> failures, List<string> warnings) {
            StagedLoot s = staged.loot;
            StagedLoot b = baseline.loot;
            LootSettings live = LootSystemData.SLS_Drop_Settings;
            if (live == null || s.YamlMatches(b)) { return; }

            LootSettings copy = CopyForEdit(YamlConfigManager.LootSettingsFile, live);
            copy.EnableDistanceLootModifier = s.distanceBonus;
            if (copy.DistanceLootModifier != null) {
                foreach (StagedLootRing ring in s.rings) {
                    if (copy.DistanceLootModifier.TryGetValue(ring.Distance, out DistanceLootModifier target) == false || target == null) { continue; }
                    target.MinAmountScaleFactorBonus = ring.MinBonus;
                    target.MaxAmountScaleFactorBonus = ring.MaxBonus;
                    target.ChanceScaleFactorBonus = ring.ChanceBonus;
                }
            }
            SaveYaml(YamlConfigManager.LootSettingsFile, copy, "Loot settings", failures, warnings);
        }
    }
}
