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
using static StarLevelSystem.modules.UI.QuickConfigPrefabs;

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

        private const float MaxObjectLootScale = 4f;
        private const float MaxRingBonus = 5f;

        private static Text lootStyleNote;
        private static LootTableView lootTable;
        private static Text lootWarningText;
        // Rows that only apply to one loot style. Hidden rather than laid out again: the column is a scroll view, so a
        // vertical layout group collapses the space an inactive child took.
        private static readonly List<GameObject> lootChanceRampRows = new List<GameObject>();
        // The ring help, header, rows and Add button, hidden together while distance loot is off.
        private static readonly List<GameObject> lootRingRows = new List<GameObject>();
        // The part of lootRingRows that is built again whenever a ring is added, removed or moves past another. It stays
        // beside the rows that are not rebuilt, so rebuilding it in place keeps the column where it was scrolled to.
        private static readonly List<GameObject> lootRingSection = new List<GameObject>();
        private static Transform lootRingContent;
        // The drop table the estimate is worked on, and what it came from, resolved once when the page is built.
        private static List<LootSampleDrop> lootSample;
        private static bool lootSampleIsLive;

        private static void ClearLootPageReferences() {
            lootStyleNote = null;
            lootTable = null;
            lootWarningText = null;
            lootChanceRampRows.Clear();
            lootRingRows.Clear();
            lootRingSection.Clear();
            lootRingContent = null;
            lootSample = null;
            lootSampleIsLive = false;
        }

        // ------------------------------------------------------------------------------------------------
        //  Staged configuration
        // ------------------------------------------------------------------------------------------------

        // One distance ring from LootSettings.yaml: a kill within Distance of the center (and past the ring before it)
        // gets these bonuses. These are the loot system's own rings, not the level system's.
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
            internal bool treeScaling, rockScaling, birdScaling;
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
                    treeScaling = ValConfig.EnableTreeScaling.Value,
                    rockScaling = ValConfig.EnableRockLevels.Value,
                    birdScaling = ValConfig.EnableScalingBirds.Value,
                    treeScale = ValConfig.PerLevelTreeLootScale.Value,
                    rockScale = ValConfig.PerLevelMineRockLootScale.Value,
                    destructibleScale = ValConfig.PerLevelDestructibleLootScale.Value,
                    birdScale = ValConfig.PerLevelBirdLootScale.Value,
                };
                LootSettings loot = LootSystemData.SLS_Drop_Settings;
                s.distanceBonus = loot != null && loot.EnableDistanceLootModifier;
                s.rings = RingsOf(loot);
                return s;
            }

            // Reset page: the ConfigEntries to their defaults and the distance section to a freshly generated file.
            internal void ResetToShipped(LootSettings shipped) {
                style = Enum.TryParse(DefaultOf(ValConfig.LootDropCalculationType), out LootFactorType parsed) ? parsed : LootFactorType.PerLevel;
                perLevelScale = DefaultOf(ValConfig.PerLevelLootScale);
                perLevelChanceScale = DefaultOf(ValConfig.PerLevelLootChanceScale);
                chanceBase = DefaultOf(ValConfig.ChanceBaseChancePerLevel);
                scaleAllLoot = DefaultOf(ValConfig.ScaleAllLootByLevel);
                eggStacks = DefaultOf(ValConfig.LootEggsDropIncreaseStacks);
                treeScaling = DefaultOf(ValConfig.EnableTreeScaling);
                rockScaling = DefaultOf(ValConfig.EnableRockLevels);
                birdScaling = DefaultOf(ValConfig.EnableScalingBirds);
                treeScale = DefaultOf(ValConfig.PerLevelTreeLootScale);
                rockScale = DefaultOf(ValConfig.PerLevelMineRockLootScale);
                destructibleScale = DefaultOf(ValConfig.PerLevelDestructibleLootScale);
                birdScale = DefaultOf(ValConfig.PerLevelBirdLootScale);
                distanceBonus = shipped != null && shipped.EnableDistanceLootModifier;
                rings = RingsOf(shipped);
            }

            // Nearest first, which is the order the file keeps them in and the order a kill is matched against them.
            private static List<StagedLootRing> RingsOf(LootSettings loot) {
                List<StagedLootRing> rings = new List<StagedLootRing>();
                if (loot?.DistanceLootModifier == null) { return rings; }
                foreach (KeyValuePair<int, DistanceLootModifier> ring in loot.DistanceLootModifier) {
                    if (ring.Value == null) { continue; }
                    rings.Add(new StagedLootRing {
                        Distance = ring.Key,
                        MinBonus = ring.Value.MinAmountScaleFactorBonus,
                        MaxBonus = ring.Value.MaxAmountScaleFactorBonus,
                        ChanceBonus = ring.Value.ChanceScaleFactorBonus,
                    });
                }
                return rings;
            }

            internal bool RingsMatch(StagedLoot o) {
                if (rings.Count != o.rings.Count) { return false; }
                for (int i = 0; i < rings.Count; i++) {
                    if (rings[i].SameAs(o.rings[i]) == false) { return false; }
                }
                return true;
            }

            // Everything that lives in LootSettings.yaml.
            internal bool YamlMatches(StagedLoot o) {
                return distanceBonus == o.distanceBonus && RingsMatch(o);
            }

            internal bool Matches(StagedLoot o) {
                return style == o.style && perLevelScale == o.perLevelScale && perLevelChanceScale == o.perLevelChanceScale
                    && chanceBase == o.chanceBase && scaleAllLoot == o.scaleAllLoot && eggStacks == o.eggStacks
                    && treeScaling == o.treeScaling && rockScaling == o.rockScaling && birdScaling == o.birdScaling
                    && treeScale == o.treeScale && rockScale == o.rockScale
                    && destructibleScale == o.destructibleScale && birdScale == o.birdScale
                    && YamlMatches(o);
            }
        }

        // ------------------------------------------------------------------------------------------------
        //  Page
        // ------------------------------------------------------------------------------------------------

        // Whether this kind of object scales at all. Its loot slider does nothing while that is off, so it goes with the switch.
        private static void BindObjectScaling(Transform parent, string switchRow, string tip, bool on, Action<bool> onChange, GameObject lootRow) {
            WithTip(BindToggleRow(parent, switchRow, on, value => {
                onChange(value);
                lootRow.SetActive(value);
            }), tip);
            lootRow.SetActive(on);
        }

        private static void BuildLootPage(Transform parent) {
            StagedLoot lt = staged.loot;

            ResolveLootSample();

            // Left - everything that scales loot with a level, then the distance rings that add to it.
            WithTip(BindEnumRow(parent, "LootStyle", LootStyleOptions, (int)lt.style, i => {
                lt.style = (LootFactorType)i;
                RefreshLootEstimates();
            }), Tip(ValConfig.LootDropCalculationType));
            WithTip(BindSliderRow(parent, "PerStar", lt.perLevelScale, v => {
                lt.perLevelScale = v;
                RefreshLootEstimates();
            }), Tip(ValConfig.PerLevelLootScale));
            WithTip(BindSliderRow(parent, "ChancePerLevel", lt.perLevelChanceScale, v => {
                lt.perLevelChanceScale = v;
                RefreshLootEstimates();
            }), Tip(ValConfig.PerLevelLootChanceScale));
            lootChanceRampRows.Add(WithTip(BindSliderRow(parent, "BaseChance", lt.chanceBase, v => {
                lt.chanceBase = v;
                RefreshLootEstimates();
            }), Tip(ValConfig.ChanceBaseChancePerLevel)));
            WithTip(BindToggleRow(parent, "ScaleAll", lt.scaleAllLoot, v => {
                lt.scaleAllLoot = v;
                RefreshLootEstimates();
            }), Tip(ValConfig.ScaleAllLootByLevel));
            WithTip(BindToggleRow(parent, "EggStacks", lt.eggStacks, v => lt.eggStacks = v), Tip(ValConfig.LootEggsDropIncreaseStacks));
            lootStyleNote = Get<Text>(parent, "StyleNote");

            // World objects carry their own per-level scale and their own shape: each is `1 + scale * level` applied to
            // the object's vanilla drop table, not the creature multiplier above.
            BindObjectScaling(parent, "TreeScaling", Tip(ValConfig.EnableTreeScaling), lt.treeScaling, on => lt.treeScaling = on,
                WithTip(BindSliderRow(parent, "TreeScale", lt.treeScale, v => lt.treeScale = v, max: Mathf.Max(MaxObjectLootScale, lt.treeScale)), Tip(ValConfig.PerLevelTreeLootScale)));
            BindObjectScaling(parent, "RockScaling", Tip(ValConfig.EnableRockLevels), lt.rockScaling, on => lt.rockScaling = on,
                WithTip(BindSliderRow(parent, "RockScale", lt.rockScale, v => lt.rockScale = v, max: Mathf.Max(MaxObjectLootScale, lt.rockScale)), Tip(ValConfig.PerLevelMineRockLootScale)));
            WithTip(BindSliderRow(parent, "DestructibleScale", lt.destructibleScale, v => lt.destructibleScale = v, max: Mathf.Max(MaxObjectLootScale, lt.destructibleScale)), Tip(ValConfig.PerLevelDestructibleLootScale));
            BindObjectScaling(parent, "BirdScaling", Tip(ValConfig.EnableScalingBirds), lt.birdScaling, on => lt.birdScaling = on,
                WithTip(BindSliderRow(parent, "BirdScale", lt.birdScale, v => lt.birdScale = v, max: Mathf.Max(MaxObjectLootScale, lt.birdScale)), Tip(ValConfig.PerLevelBirdLootScale)));

            WithTip(BindToggleRow(parent, "DistanceBonus", lt.distanceBonus, v => {
                lt.distanceBonus = v;
                RefreshLootEstimates();
            }), YamlTip(typeof(LootSettings), nameof(LootSettings.EnableDistanceLootModifier), "$sls_cfg_tip_loot_distance"));
            lootRingRows.Add(Find(parent, "RingsHelp"));
            lootRingRows.Add(Find(parent, "AddRingRow"));
            WithTip(BindButton(parent, "AddRing", AddLootRing), Tip("$sls_cfg_button_add_ring", "$sls_cfg_tip_add_loot_ring"));
            // Rebuilt in place. See lootRingSection.
            lootRingContent = ScrollContent(parent, "Settings");
            BuildLootRingSection();

            // Right - the estimate.
            lootWarningText = Get<Text>(parent, "Warning");
            lootTable = BuildLootTable(parent);
            WithTip(lootTable.Root, Tip("$sls_cfg_loot_header_estimate", "$sls_cfg_tip_loot_estimate"));

            RefreshLootEstimates();
        }

        // The rings' part of the column: a row per ring under the header, or the note when there are none. The rows are built
        // again from the staged rings whenever their number or order changes; the header, the note and the Add button are the
        // page's own.
        private static void BuildLootRingSection() {
            foreach (GameObject row in lootRingSection) {
                lootRingRows.Remove(row);
                DiscardRow(row);
            }
            lootRingSection.Clear();
            if (lootRingContent == null || staged?.loot == null) { return; }

            Transform content = lootRingContent;
            List<StagedLootRing> rings = staged.loot.rings;
            // Whichever of the note and the header applies comes and goes with lootRingRows; the other stays hidden.
            GameObject none = Find(content, "RingsNone");
            GameObject header = Find(content, "RingsHeader");
            lootRingRows.Remove(none);
            lootRingRows.Remove(header);
            none.SetActive(false);
            header.SetActive(false);
            lootRingRows.Add(rings.Count == 0 ? none : header);
            foreach (StagedLootRing ring in rings) { lootRingSection.Add(AddLootRingRow(content, ring)); }
            lootRingRows.AddRange(lootRingSection);
        }

        private static GameObject AddLootRingRow(Transform content, StagedLootRing ring) {
            GameObject row = SpawnRingRow("SLS_QC_Row_LootRing", content);
            InputField distance = null;
            distance = BindField(row.transform, "Distance", ring.Distance.ToString(CultureInfo.InvariantCulture),
                text => CommitLootRingDistance(ring, distance, text));
            WithTip(distance.gameObject, Tip("$sls_cfg_tip_distance_title", L("$sls_cfg_tip_loot_ring_distance", MinRingDistance, MaxRingDistance)));

            BindLootRingField(row.transform, "Min", ring.MinBonus, v => ring.MinBonus = v,
                YamlTip(typeof(DistanceLootModifier), nameof(DistanceLootModifier.MinAmountScaleFactorBonus), "$sls_cfg_tip_loot_ring_min"));
            BindLootRingField(row.transform, "Max", ring.MaxBonus, v => ring.MaxBonus = v,
                YamlTip(typeof(DistanceLootModifier), nameof(DistanceLootModifier.MaxAmountScaleFactorBonus), "$sls_cfg_tip_loot_ring_max"));
            BindLootRingField(row.transform, "Chance", ring.ChanceBonus, v => ring.ChanceBonus = v,
                YamlTip(typeof(DistanceLootModifier), nameof(DistanceLootModifier.ChanceScaleFactorBonus), "$sls_cfg_tip_loot_ring_chance"));
            BindRingRemoveButton(row.transform, () => {
                staged.loot.rings.Remove(ring);
                BuildLootRingSection();
                RefreshLootEstimates();
            });
            return row;
        }

        private static void CommitLootRingDistance(StagedLootRing ring, InputField field, string text) {
            if (staged?.loot == null) { return; }
            int distance = ReadRingDistance(text, ring.Distance, staged.loot.rings.Where(r => r != ring).Select(r => r.Distance), "$sls_cfg_loot_ring_distance_taken");
            field.SetTextWithoutNotify(distance.ToString(CultureInfo.InvariantCulture));
            if (distance == ring.Distance) { return; }
            ring.Distance = distance;
            // Only when it passed another ring: rebuilding under a box that was just clicked into would take the click.
            if (SortByDistance(staged.loot.rings, r => r.Distance)) { BuildLootRingSection(); }
            RefreshLootEstimates();
        }

        private static void AddLootRing() {
            if (staged?.loot == null) { return; }
            List<StagedLootRing> rings = staged.loot.rings;
            if (NextRingDistance(rings.Select(r => r.Distance).ToList(), DefaultLootRingStep, out int distance) == false) {
                SetStatus(L("$sls_cfg_loot_ring_no_room", rings[rings.Count - 1].Distance), false);
                return;
            }
            StagedLootRing last = rings.Count > 0 ? rings[rings.Count - 1] : null;
            rings.Add(new StagedLootRing {
                Distance = distance,
                MinBonus = last?.MinBonus ?? 0f,
                MaxBonus = last?.MaxBonus ?? 0f,
                ChanceBonus = last?.ChanceBonus ?? 0f,
            });
            BuildLootRingSection();
            RefreshLootEstimates();
        }

        // Where the first loot ring goes when there are none: the shipped rings are this far apart.
        private const int DefaultLootRingStep = 1250;

        private static void ResetLootPage() {
            staged.loot.ResetToShipped(ShippedDefaults(YamlConfigManager.LootSettingsFile));
        }

        private static void BindLootRingField(Transform row, string name, float value, Action<float> onCommit, string tooltip) {
            InputField field = null;
            field = BindField(row, name, FormatLootBonus(value), text => {
                float committed = value;
                if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) {
                    committed = Mathf.Clamp(parsed, 0f, MaxRingBonus);
                }
                value = committed;
                onCommit(committed);
                field.SetTextWithoutNotify(FormatLootBonus(committed));
                RefreshLootEstimates();
            });
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

        // What the selected style does, in the terms the sliders above are labelled in. The linear styles' tokens carry a
        // _base suffix since they gained the 1x base: a cached language file keeps its old text for a key it already has.
        private static string DescribeLootStyle(StagedLoot lt) {
            string scale = FormatLootBonus(lt.perLevelScale);
            switch (lt.style) {
                case LootFactorType.Exponential:
                    return L("$sls_cfg_loot_style_exponential", scale);
                case LootFactorType.ChancePerLevel:
                    return L("$sls_cfg_loot_style_chance_per_level_base", scale, FormatLootBonus(lt.chanceBase), FormatLootBonus(lt.perLevelChanceScale));
                default:
                    return L("$sls_cfg_loot_style_per_level_base", scale);
            }
        }

        // One line above the estimate for the thing most worth knowing about these numbers right now, if anything.
        private static string LootWarning(StagedLoot lt) {
            if (lt.perLevelScale <= 0f) {
                return ConfigUI.L("$sls_cfg_loot_warn_zero");
            }
            // Exponential growth reaches this within a dozen stars at the default scale, and everything past it is cut,
            // which is worth saying out loud before the estimate is read as the intended reward.
            LootScale top = LootScaleAt(lt, Mathf.Max(0, staged.maxStars) + 1, lt.OutermostRing);
            if (top.AmountMax >= LootStyles.MaxLootScaleResult) {
                return L("$sls_cfg_loot_warn_ceiling", Stars(Mathf.Max(0, staged.maxStars)),
                    LootStyles.MaxLootScaleResult.ToString("#,0", CultureInfo.InvariantCulture));
            }
            if (lt.style == LootFactorType.ChancePerLevel) {
                float atFirst = LootScaleAt(lt, 1, lt.InnermostRing).ChanceMin;
                if (atFirst < 1f) {
                    return L("$sls_cfg_loot_warn_low_chance", atFirst.ToString("0.##"));
                }
            }
            if (lt.style != LootFactorType.ChancePerLevel && lt.perLevelScale < 1f) {
                // The chance branch shares the amount multiplier here, and DetermineLootScale floors it at 1, so a
                // scale under 1 leaves the low levels on their vanilla chances rather than lowering them.
                return ConfigUI.L("$sls_cfg_loot_warn_below_one");
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

        private const int LootTableAmountRow = 0;
        private const int LootTableChanceRow = 1;
        private const int LootTableFirstDropRow = 2;

        private static LootTableView BuildLootTable(Transform page) {
            GameObject root = Find(page, "LootEstimate");
            LootTableView view = new LootTableView { Root = root, Width = ((RectTransform)root.transform).sizeDelta.x };
            view.Header = LootTableCells(Find(root.transform, "TableHeader").transform);
            view.Rows.Add(LootTableCells(Find(root.transform, "TableAmount").transform));
            view.Rows.Add(LootTableCells(Find(root.transform, "TableChance").transform));

            Transform drops = Find(root.transform, "TableDrops").transform;
            view.Drops = lootSample.Take(MaxEstimatedDrops).ToList();
            float rowY = 0f;
            for (int i = 0; i < view.Drops.Count; i++) {
                GameObject row = Spawn("SLS_QC_Row_LootDrop", drops);
                ConfigUI.PositionRow(row, 0f, rowY);
                Find(row.transform, "Shade").SetActive(i % 2 == 1);
                Text[] cells = LootTableCells(row.transform);
                cells[0].text = view.Drops[i].Name;
                view.Rows.Add(cells);
                rowY += ((RectTransform)row.transform).sizeDelta.y;
            }

            // Under the rows, in what they leave of the table.
            view.Footer = Get<Text>(drops, "Footer");
            RectTransform footer = view.Footer.rectTransform;
            footer.anchoredPosition -= new Vector2(0f, rowY);
            footer.sizeDelta = new Vector2(footer.sizeDelta.x, Mathf.Max(0f, footer.sizeDelta.y - rowY));
            return view;
        }

        // The label cell and the three value cells of one row.
        private static Text[] LootTableCells(Transform row) {
            return new[] { Get<Text>(row, "Label"), Get<Text>(row, "Value1"), Get<Text>(row, "Value2"), Get<Text>(row, "Value3") };
        }

        // The value cells share what the label column leaves, from where the first of them starts.
        private static void LayoutLootTableColumns(LootTableView view, int columns) {
            float startX = view.Header[1].rectTransform.anchoredPosition.x;
            float cellW = (view.Width - startX) / columns;
            foreach (Text[] row in new[] { view.Header }.Concat(view.Rows)) {
                for (int c = 1; c < row.Length; c++) {
                    Text cell = row[c];
                    bool shown = c <= columns;
                    cell.gameObject.SetActive(shown);
                    if (shown == false) { continue; }
                    RectTransform rt = cell.rectTransform;
                    rt.anchoredPosition = new Vector2(startX + (c - 1) * cellW, rt.anchoredPosition.y);
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

            view.Header[0].text = lootSampleIsLive ? L("$sls_cfg_loot_col_drops", TrollName()) : ConfigUI.L("$sls_cfg_loot_col_stand_in");
            view.Header[1].text = ConfigUI.L("$sls_cfg_loot_col_no_stars");
            view.Header[2].text = Stars(maxStars);
            view.Header[3].text = farColumn ? $"{Stars(maxStars)}\n<size=11>{L("$sls_cfg_loot_col_ring", far.Distance)}</size>" : "";

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
                notes.Add(L("$sls_cfg_loot_more_drops", lootSample.Count - view.Drops.Count));
            }
            string worldNote = WorldResourceModifierNote();
            if (worldNote != null) { notes.Add(worldNote); }
            if (lootSampleIsLive == false) {
                notes.Add($"{WarningColorTag}{L("$sls_cfg_loot_stand_in_note", TrollName())}</color>");
            }
            view.Footer.text = string.Join("\n", notes.ToArray());
        }

        // The world's Resources modifier scales the amounts on top of everything on this page, and is set when the world
        // is made rather than here. Read on the client like every global key, so a remote admin sees the server's value.
        // Null when there is nothing to say.
        private static string WorldResourceModifierNote() {
            if (ZoneSystem.instance == null) {
                return ConfigUI.L("$sls_cfg_loot_world_note_menu");
            }
            float rate = WorldRates.ResourceRate;
            if (Mathf.Approximately(rate, 1f)) { return null; }
            return L("$sls_cfg_loot_world_note", rate.ToString("0.##", CultureInfo.InvariantCulture));
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
                    s.AmountMin = RoundLootScale(Mathf.Pow(1f + lt.perLevelScale + minBonus, level - 1));
                    s.AmountMax = RoundLootScale(Mathf.Pow(1f + lt.perLevelScale + maxBonus, level - 1));
                } else {
                    // A 1x base plus the scale per star, rounded up with the chance of its fraction, so the multiplier
                    // can land on either whole number around it.
                    s.AmountMin = Mathf.Max(1f, RoundLootDown(1f + (level - 1) * (minBonus + lt.perLevelScale)));
                    s.AmountMax = Mathf.Max(1f, RoundLootUp(1f + (level - 1) * (maxBonus + lt.perLevelScale)));
                }
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

        // The ends of what LootStyles.RandomRound can give for a value. The slack keeps float error in a whole
        // result (1 + 5 x 0.2 is a hair over 2) from showing a step that never happens.
        private static float RoundLootDown(float value) {
            return Mathf.Floor(Mathf.Min(value, LootStyles.MaxLootScaleResult) + 0.0001f);
        }

        private static float RoundLootUp(float value) {
            return Mathf.Ceil(Mathf.Min(value, LootStyles.MaxLootScaleResult) - 0.0001f);
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
                // The amount branch also scales a drop that opts out, or that scales only its chance, once
                // ScaleAllLootByLevel is on; the chance branch keeps vanilla's gate either way.
                bool scales = (drop.ScalesWithLevel && drop.ChanceOnly == false) || lt.scaleAllLoot;
                amountMin = scales ? s.AmountMin : 1f;
                amountMax = scales ? s.AmountMax : 1f;
                // The low end of the multiplier: the chance branch reads the same single roll the amounts do, so a ring
                // with a wider Min/Max spread makes it a range too. The chance multiplier row shows that range in full.
                chance = drop.Chance * (drop.ScalesWithLevel ? s.ChanceMin : 1f);
            }

            // MultiplyLootPerLevel rounds a custom drop's scaled amount up with the chance of its fraction; the shared
            // multiplier above is already whole, which either rounding leaves as it is.
            bool randomRounded = lt.style != LootFactorType.Exponential;
            int low = randomRounded ? (int)RoundLootDown(baseMin * amountMin) : Mathf.RoundToInt(Mathf.Min(baseMin * amountMin, LootStyles.MaxLootScaleResult));
            int high = randomRounded ? (int)RoundLootUp(baseMax * amountMax) : Mathf.RoundToInt(Mathf.Min(baseMax * amountMax, LootStyles.MaxLootScaleResult));
            // The world's Resources modifier, before the cap as on both drop paths. Vanilla scales a vanilla drop's rolled
            // amount and SLS a custom drop's scaled one, which only differ here by rounding.
            if (drop.WorldScaled) {
                low = WorldRates.ScaleDropAmount(low);
                high = WorldRates.ScaleDropAmount(high);
            }
            if (drop.MaxScaledAmount > 0) {
                low = Mathf.Min(low, drop.MaxScaledAmount);
                high = Mathf.Min(high, drop.MaxScaledAmount);
            }

            string amount = low == high ? low.ToString() : $"{low}-{high}";
            chance = Mathf.Clamp01(chance);
            if (chance >= 0.999f) { return amount; }
            if (chance <= 0f) { return ConfigUI.L("$sls_cfg_loot_never"); }
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
                min = 1f + (level - 1) * lt.perLevelScale * (factor + minBonus);
                max = 1f + (level - 1) * lt.perLevelScale * (factor + maxBonus);
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
            // Vanilla's m_levelMultiplierNotAmount: the level raises this drop's chance but not its amount, unless
            // ScaleAllLootByLevel is on. Trophies are marked this way.
            internal bool ChanceOnly;
            // Set for a drop that comes from a creature's own table in LootSettings.yaml. Those scale by their own
            // per-drop factors instead of the multiplier the whole creature shares.
            internal bool Custom;
            internal float AmountScaleFactor;
            internal float ChanceScaleFactor;
            internal bool UseChanceAsMultiplier;
            internal bool ScaleByMaxLevel;
            internal int MaxScaledAmount;
            // Whether the world's Resources modifier reaches this drop: not when its table marks it as not scaling, nor
            // for the item types vanilla never scales.
            internal bool WorldScaled = true;
        }

        // Stands in for the sample creature when its prefab is not loaded - the panel also opens on the main menu,
        // where there is no ZNetScene to read a drop table from. A certain stack and an uncertain trophy, which is
        // enough to show what the multipliers do to each kind of drop.
        private static List<LootSampleDrop> LootSampleStandIn() {
            return new List<LootSampleDrop>() {
                new LootSampleDrop { Name = ConfigUI.L("$sls_cfg_loot_stand_in_stack"), Min = 3, Max = 5 },
                new LootSampleDrop { Name = ConfigUI.L("$sls_cfg_loot_stand_in_trophy"), Chance = 0.25f, ChanceOnly = true },
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
                // the level scaling itself sits behind, and m_levelMultiplierNotAmount keeps it off the amount.
                ScalesWithLevel = drop.m_levelMultiplier,
                ChanceOnly = drop.m_levelMultiplierNotAmount,
                // That roll is the one Game.ScaleDrops applies the world's resource rate in.
                WorldScaled = drop.m_dontScale == false && WorldRates.ScalesWithResourceRate(drop.m_prefab),
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
                // LootStyles.ModifyCharacterDrops applies the rate only on the path that scales with level.
                WorldScaled = drop.DoesNotScale == false && d.DontScale == false && WorldRates.ScalesWithResourceRate(drop.GameDrop?.m_prefab),
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
            if (s.RingsMatch(b) == false) {
                // The rings are the whole section: added, removed and moved ones included. Written empty rather than
                // left out when every ring was removed, so the file says so.
                copy.DistanceLootModifier = new SortedDictionary<int, DistanceLootModifier>();
                foreach (StagedLootRing ring in s.rings) {
                    copy.DistanceLootModifier[ring.Distance] = new DistanceLootModifier {
                        MinAmountScaleFactorBonus = ring.MinBonus,
                        MaxAmountScaleFactorBonus = ring.MaxBonus,
                        ChanceScaleFactorBonus = ring.ChanceBonus,
                    };
                }
            }
            SaveYaml(YamlConfigManager.LootSettingsFile, copy, "$sls_cfg_label_loot_settings", failures, warnings);
        }
    }
}
