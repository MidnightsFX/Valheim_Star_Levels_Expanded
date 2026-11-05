using BepInEx.Configuration;
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
    // The panel's first page: a handful of settings that stand for many. Max level and a difficulty between them set the
    // star chances, what each star is worth and the biome caps (see DifficultyPreset), and the other pages show and edit what
    // those wrote. A difficulty is not stored anywhere: the page works out which one the staged values amount to, so an edit
    // on another page or in a file reads as Custom rather than as a name that no longer describes the world. Custom can also be
    // picked, and shows a table of the chance of reaching each star where the boss chart otherwise is.
    internal static partial class QuickConfigureTool {
        // The slider stops here; a higher cap set elsewhere widens it rather than being clamped.
        private const float SimpleMaxLevelTop = 50f;
        private const float SimpleChartX = 420f;
        private const float SimpleChartW = 428f;

        private static Slider simpleMaxSlider;
        private static InputField simpleMaxBox;
        private static Text simpleDifficultyCaption;
        private static Text simpleDifficultyText;
        private static Toggle simpleModifiersToggle;
        private static Toggle simpleProgressiveToggle;
        private static Toggle simpleColorsToggle;
        private static Text simpleExampleText;
        private static Text simpleNotesText;
        private static LevelDistributionChart simpleChart;
        private static LevelDistributionChart simpleBossChart;
        private static GameObject simpleLevelTable;
        private static Transform simpleLevelTableContent;
        // One per star, kept while the panel is up: the Max level slider grows and shrinks the table on every tick.
        private static readonly List<LevelChanceCell> simpleLevelCells = new List<LevelChanceCell>();
        // Whether this page changed values another page has widgets for. Those are built again before they are next shown.
        private static bool simplePageChanged;
        // Custom was stepped onto, so the page stays on it even while the staged values still match a difficulty. Not stored,
        // like the difficulty itself: the next time the panel opens the values decide.
        private static bool simpleCustomPicked;
        // The table as it was when the page stepped off Custom, so stepping all the way round again brings it back.
        private static SortedDictionary<int, float> simpleCustomTable;

        private class LevelChanceCell {
            internal GameObject Root;
            internal InputField Field;
            internal int Star;
        }

        private static void ClearSimpleReferences() {
            simpleMaxSlider = null;
            simpleMaxBox = null;
            simpleDifficultyCaption = null;
            simpleDifficultyText = null;
            simpleModifiersToggle = null;
            simpleProgressiveToggle = null;
            simpleColorsToggle = null;
            simpleExampleText = null;
            simpleNotesText = null;
            simpleChart = null;
            simpleBossChart = null;
            simpleLevelTable = null;
            simpleLevelTableContent = null;
            simpleLevelCells.Clear();
        }

        // What only lasts as long as the panel, which building one page again must leave alone.
        private static void ClearSimpleChoices() {
            simplePageChanged = false;
            simpleCustomPicked = false;
            simpleCustomTable = null;
        }

        private static void BuildSimplePage(Transform parent) {
            GameObject maxRow = WithTip(BindSliderRow(parent, "MaxLevel", staged.maxStars, v => OnSimpleMaxLevelChanged((int)v), max: Mathf.Max(SimpleMaxLevelTop, staged.maxStars)),
                Tip("$sls_cfg_simple_max_level", "$sls_cfg_simple_tip_max_level"));
            simpleMaxSlider = Get<Slider>(maxRow.transform, "Slider");
            simpleMaxBox = Get<InputField>(maxRow.transform, "Value");

            Transform difficultyRow = WithTip(Find(parent, "Difficulty"), Tip("$sls_cfg_simple_difficulty", "$sls_cfg_simple_tip_difficulty_custom")).transform;
            Button cycle = Get<Button>(difficultyRow, "Cycle");
            cycle.onClick.AddListener(OnDifficultyClicked);
            simpleDifficultyCaption = cycle.GetComponentInChildren<Text>(true);
            simpleDifficultyText = Get<Text>(parent, "DifficultyText");

            simpleModifiersToggle = BindSimpleToggle(parent, "Modifiers", staged.enableModifiers, on => staged.enableModifiers = on, "$sls_cfg_simple_modifiers", "$sls_cfg_simple_tip_modifiers");
            simpleProgressiveToggle = BindSimpleToggle(parent, "Progressive", ProgressiveScalingOn(), SetProgressiveScaling, "$sls_cfg_simple_progressive", "$sls_cfg_simple_tip_progressive");
            simpleColorsToggle = BindSimpleToggle(parent, "Colors", staged.enableColorization, on => staged.enableColorization = on, "$sls_cfg_simple_colors", "$sls_cfg_simple_tip_colors");

            // Not a setting of the world, so it takes effect at once and is not part of what Save writes.
            WithTip(BindToggleRow(parent, "Advanced", ValConfig.ShowAdvancedQuickConfigurePages.Value, on => {
                ValConfig.ShowAdvancedQuickConfigurePages.Value = on;
                ShowPage(currentPage);
            }), Tip("$sls_cfg_simple_advanced", "$sls_cfg_simple_tip_advanced"));

            simpleExampleText = Get<Text>(parent, "Example");
            simpleNotesText = Get<Text>(parent, "Notes");

            simpleChart = new LevelDistributionChart(parent, SimpleChartX, 86f, SimpleChartW, 152f);
            simpleChart.SetTitle(ConfigUI.L("$sls_cfg_dist_chart_creatures"));
            simpleBossChart = new LevelDistributionChart(parent, SimpleChartX, 242f, SimpleChartW, 152f);
            simpleBossChart.SetTitle(ConfigUI.L("$sls_cfg_dist_bosses"));

            simpleLevelTable = Find(parent, "LevelTable");
            simpleLevelTableContent = ScrollContent(simpleLevelTable.transform, "Cells");
            WithTip(Find(simpleLevelTable.transform, "Header"), Tip("$sls_cfg_simple_table_header", "$sls_cfg_simple_tip_table"));

            RefreshSimplePage();
        }

        private static Toggle BindSimpleToggle(Transform parent, string name, bool value, Action<bool> onChange, string title, string tip) {
            GameObject row = WithTip(BindToggleRow(parent, name, value, on => {
                onChange(on);
                SimplePageEdited();
            }), Tip(title, tip));
            return Get<Toggle>(row.transform, "Toggle");
        }

        private static void SimplePageEdited() {
            simplePageChanged = true;
            RefreshSimplePage();
        }

        private static void RebuildPagesFollowingSimplePage() {
            if (simplePageChanged == false || pages[currentPage].Advanced == false) { return; }
            simplePageChanged = false;
            for (int i = 0; i < pages.Count; i++) {
                // One not built yet will read the new values when it is.
                if (pages[i].FollowsSimplePage && pageRoots[i] != null) { RebuildPage(i); }
            }
        }

        // Also what the page shows when it comes back into view, since the other pages can change all of this.
        private static void RefreshSimplePage() {
            if (staged == null || baseline == null || simpleChart == null) { return; }
            simpleMaxSlider.maxValue = Mathf.Max(SimpleMaxLevelTop, staged.maxStars);
            simpleMaxSlider.SetValueWithoutNotify(staged.maxStars);
            simpleMaxBox.SetTextWithoutNotify(ConfigUI.Fmt(staged.maxStars, true));
            simpleModifiersToggle.SetIsOnWithoutNotify(staged.enableModifiers);
            simpleProgressiveToggle.SetIsOnWithoutNotify(ProgressiveScalingOn());
            simpleColorsToggle.SetIsOnWithoutNotify(staged.enableColorization);

            DifficultyPreset difficulty = ShownDifficulty();
            bool custom = difficulty == null;
            simpleDifficultyCaption.text = ConfigUI.L(difficulty?.NameToken ?? "$sls_cfg_difficulty_custom");
            simpleDifficultyText.text = ConfigUI.L(difficulty?.DescriptionToken ?? "$sls_cfg_difficulty_custom_table_desc") + "\n" + L("$sls_cfg_simple_per_star",
                Percent(staged.creatureHpPerLevel), Percent(staged.creatureDmgPerLevel), Percent(staged.bossHpPerLevel), Percent(staged.bossDmgPerLevel));

            simpleChart.SetData(ToStarSeries(LevelSelection.ComputeLevelDistribution(ShownCreatureCurve(), staged.maxStars + 1)));
            simpleBossChart.SetVisible(custom == false);
            simpleLevelTable.SetActive(custom);
            if (custom) {
                RefreshLevelTable();
            } else {
                simpleBossChart.SetData(ToStarSeries(LevelSelection.ComputeLevelDistribution(ShownBossCurve(), staged.maxBossLevel + 1)));
            }
            simpleExampleText.text = FormatCreatureExample(TrollName(), TrollHp, TrollDmg * staged.allBaseDamage, staged.maxStars, staged.creatureHpPerLevel, staged.creatureDmgPerLevel);

            List<string> notes = new List<string>();
            if (custom && staged.bossCurveOn) {
                notes.Add(ConfigUI.L("$sls_cfg_simple_note_custom_boss"));
            }
            if (staged.enableConditional) {
                notes.Add(ConfigUI.L("$sls_cfg_distribution_conditional_raises_note"));
            }
            if (LevelSystemData.SLE_Level_Settings?.BiomeConfiguration?.Values.Any(biome => biome?.CustomCreatureLevelUpChance != null) == true) {
                notes.Add(ConfigUI.L("$sls_cfg_simple_note_biome_curves"));
            }
            // The menu's version of this note only says the modifier exists, which a world on the default setting never needs.
            string worldNote = ZoneSystem.instance != null ? WorldCombatModifierNote() : null;
            if (worldNote != null) { notes.Add(worldNote); }
            simpleNotesText.text = string.Join("\n", notes.ToArray());
        }

        private static string Percent(float perStar) {
            return (perStar * 100f).ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static SortedDictionary<int, float> ShownBossCurve() {
            if (staged.bossCurveOn == false) { return ShownCreatureCurve(); }
            return staged.BossCurveDiffers(baseline) ? StagedCurveTable(staged.bossGenerator, staged.BossTableSpan, staged.BossTableFor(staged.BossTableSpan)) : LiveBossTable();
        }

        // A difficulty is worked out from the top star, so it follows Max level. Anything else leaves the curve and the
        // per-star values alone and only moves the caps.
        private static void OnSimpleMaxLevelChanged(int maxLevel) {
            if (staged == null || maxLevel == staged.maxStars) { return; }
            DifficultyPreset difficulty = ShownDifficulty();
            staged.maxStars = maxLevel;
            if (difficulty != null) {
                ApplyDifficulty(difficulty);
            } else {
                staged.generator.MaxLevel = maxLevel + 1;
                if (staged.generator.MinLevel > maxLevel + 1) { staged.generator.MinLevel = maxLevel + 1; }
                SetBiomeCapsUnderMaxLevel(false);
            }
            SimplePageEdited();
        }

        // Steps through the difficulties and then Custom.
        private static void OnDifficultyClicked() {
            if (staged == null) { return; }
            DifficultyPreset shown = ShownDifficulty();
            if (shown == null) {
                simpleCustomTable = staged.CurveDiffers(baseline) ? new SortedDictionary<int, float>(ShownCreatureCurve()) : null;
                simpleCustomPicked = false;
                ApplyDifficulty(DifficultyPreset.All[0]);
            } else {
                int next = Array.IndexOf(DifficultyPreset.All, shown) + 1;
                if (next < DifficultyPreset.All.Length) {
                    ApplyDifficulty(DifficultyPreset.All[next]);
                } else {
                    ApplyCustomDifficulty();
                }
            }
            SimplePageEdited();
        }

        // Custom starts from what the world already has, not from the difficulty the button last passed on the way here, which
        // is always the hardest. With nothing typed yet that is no change at all, so the panel has nothing to save.
        private static void ApplyCustomDifficulty() {
            simpleCustomPicked = true;
            staged.maxBossLevel = baseline.maxBossLevel == baseline.maxStars ? staged.maxStars : baseline.maxBossLevel;
            staged.creatureHpPerLevel = baseline.creatureHpPerLevel;
            staged.creatureDmgPerLevel = baseline.creatureDmgPerLevel;
            staged.bossHpPerLevel = baseline.bossHpPerLevel;
            staged.bossDmgPerLevel = baseline.bossDmgPerLevel;

            staged.tableCurve = null;
            staged.generator = OverStars(baseline.generator, staged.maxStars);
            staged.bossCurveOn = baseline.bossCurveOn;
            staged.bossGenerator = OverStars(baseline.bossGenerator, staged.maxBossLevel);
            if (simpleCustomTable != null) { StageCustomCurve(new SortedDictionary<int, float>(simpleCustomTable)); }

            staged.allSpawnRate = baseline.allSpawnRate;
            staged.allNightLevelScale = baseline.allNightLevelScale;
            staged.allDistanceScale = baseline.allDistanceScale;
            staged.allBaseDamage = baseline.allBaseDamage;
            SetBiomeCapsUnderMaxLevel(false);
        }

        private static LevelGenerator OverStars(LevelGenerator generator, int maxStars) {
            LevelGenerator copy = CloneGenerator(generator);
            copy.MaxLevel = maxStars + 1;
            copy.MinLevel = Mathf.Min(copy.MinLevel, copy.MaxLevel);
            return copy;
        }

        // A curve goes in as the table it is, like a difficulty's, and the generator is shaped after it so that the snapshot
        // taken after a save seeds the same one and the save does not read as a new edit.
        private static void StageCustomCurve(SortedDictionary<int, float> table) {
            staged.tableCurve = table;
            staged.generator = StagedConfig.SeedGenerator(new CreatureLevelSettings { DefaultCreatureLevelUpChance = table }, staged.maxStars);
        }

        // --- the Custom table ---------------------------------------------------------------------------------------------

        private static void RefreshLevelTable() {
            SortedDictionary<int, float> reach = LevelReach(ShownCreatureCurve(), staged.maxStars + 1);
            for (int star = 1; star <= staged.maxStars; star++) {
                if (star > simpleLevelCells.Count) { simpleLevelCells.Add(NewLevelCell(star)); }
                LevelChanceCell cell = simpleLevelCells[star - 1];
                cell.Root.SetActive(true);
                cell.Field.SetTextWithoutNotify(reach[star].ToString("0.####", CultureInfo.InvariantCulture));
            }
            for (int i = staged.maxStars; i < simpleLevelCells.Count; i++) { simpleLevelCells[i].Root.SetActive(false); }
        }

        private static LevelChanceCell NewLevelCell(int star) {
            Transform row = Spawn("SLS_QC_Row_LevelChance", simpleLevelTableContent).transform;
            LevelChanceCell cell = new LevelChanceCell { Root = row.gameObject, Star = star, Field = Get<InputField>(row, "Field") };
            // The number alone, as on the chart above it. The words for it run to three lines in a cell this wide in some languages.
            Get<Text>(row, "Label").text = star.ToString();
            cell.Field.onEndEdit.AddListener(text => CommitLevelChance(cell, text));
            WithTip(row.gameObject, Tip("$sls_cfg_simple_table_header", "$sls_cfg_simple_tip_table"));
            return cell;
        }

        // Whatever is typed stays as typed. A higher star cannot be more likely than the one below it, since these are chances
        // of getting at least that far, so the others move out of its way where they have to.
        private static void CommitLevelChance(LevelChanceCell cell, string text) {
            // A box can still lose focus after Max level dropped below its star.
            if (staged == null || cell.Star > staged.maxStars) { return; }
            SortedDictionary<int, float> table = LevelReach(ShownCreatureCurve(), staged.maxStars + 1);
            // Invariant, with a comma as the decimal point, as in BindSliderRow.
            bool read = float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float chance);
            chance = Mathf.Clamp(Mathf.Round(chance * 10000f) / 10000f, 0f, 100f);
            // onEndEdit also fires when the box only loses focus, which is not an edit: tabbing past a row must not turn the
            // curve the world rolls from into a table. Either way the row is redrawn, so what could not be read snaps back.
            if (read == false || Mathf.Abs(chance - table[cell.Star]) < 0.00001f) {
                RefreshLevelTable();
                return;
            }
            foreach (int level in table.Keys.ToList()) {
                if (level < cell.Star) { table[level] = Mathf.Max(table[level], chance); }
                if (level > cell.Star) { table[level] = Mathf.Min(table[level], chance); }
            }
            table[cell.Star] = chance;
            StageCustomCurve(table);
            SimplePageEdited();
        }

        // The chance of rolling past each level from 1 up to this one, read the way DetermineLevelRollResult walks a table, so
        // what the rows show is what happens in play and writing them back as they are changes nothing. A level the table
        // skips has no roll of its own, so it is what the level before left: all of them below the first entry, which rolls
        // nothing below it. The last entry takes every roll that reaches it, so nothing gets past it whatever it holds.
        private static SortedDictionary<int, float> LevelReach(SortedDictionary<int, float> curve, int upToLevel) {
            SortedDictionary<int, float> reach = new SortedDictionary<int, float>();
            int lastEntry = curve.Count > 0 ? curve.Keys.Last() : 0;
            float left = 100f;
            for (int level = Mathf.Min(1, curve.Count > 0 ? curve.Keys.First() : 1); level <= Mathf.Max(upToLevel, lastEntry); level++) {
                if (level >= lastEntry) {
                    left = 0f;
                } else if (curve.TryGetValue(level, out float chance)) {
                    left = Mathf.Min(left, chance);
                }
                if (level >= 1) { reach[level] = left; }
            }
            return reach;
        }

        // Every value a difficulty sets, for the staged Max level. A curve goes in as the table it is, the way Reset page
        // puts back the shipped one, so it saves over whatever built the curve before.
        private static void ApplyDifficulty(DifficultyPreset preset) {
            staged.maxBossLevel = staged.maxStars;
            staged.creatureHpPerLevel = PerStarFor(ValConfig.EnemyHealthMultiplier, preset.HealthAtMax);
            staged.creatureDmgPerLevel = PerStarFor(ValConfig.EnemyDamageLevelMultiplier, preset.DamageAtMax);
            staged.bossHpPerLevel = PerStarFor(ValConfig.BossEnemyHealthMultiplier, preset.BossHealthAtMax);
            staged.bossDmgPerLevel = PerStarFor(ValConfig.BossEnemyDamageMultiplier, preset.BossDamageAtMax);

            staged.tableCurve = preset.CreatureCurve(staged.maxStars);
            staged.generator = StagedConfig.SeedGenerator(new CreatureLevelSettings { DefaultCreatureLevelUpChance = staged.tableCurve }, staged.maxStars);
            staged.bossCurveOn = preset.BossChance > 0f;
            staged.bossGenerator = staged.bossCurveOn ? BossGeneratorFor(preset) : StagedConfig.SeedBossGenerator(null, staged.maxBossLevel, staged.generator);

            staged.allSpawnRate = preset.SpawnRate;
            staged.allNightLevelScale = preset.NightLevelScale;
            staged.allDistanceScale = preset.DistanceScale;
            staged.allBaseDamage = preset.BaseDamage;
            SetBiomeCapsUnderMaxLevel(true);
        }

        private static LevelGenerator BossGeneratorFor(DifficultyPreset preset) {
            return new LevelGenerator {
                MinLevel = 1,
                MaxLevel = staged.maxBossLevel + 1,
                LevelUpChance = preset.BossChance,
                LevelupCalculationStyle = LevelupCalculationStyle.Exponential,
            };
        }

        // Inside the range the setting allows: at a very low Max level the whole multiplier lands on one or two stars.
        private static float PerStarFor(ConfigEntry<float> entry, float multiplierAtMax) {
            float perStar = DifficultyPreset.PerStar(multiplierAtMax, staged.maxStars);
            AcceptableValueBase range = entry.Description.AcceptableValues;
            return range != null ? (float)range.Clamp(perStar) : perStar;
        }

        // Keeps Max level true: a biome's cap replaces it there, so a cap above it would let creatures past what this page
        // says. Scaled from what the panel opened on, so dragging the slider around never compounds the rounding. Biomes
        // keep their steps under a difficulty, and without one the highest cap goes to Max level and the rest keep their
        // proportion to it.
        private static void SetBiomeCapsUnderMaxLevel(bool useDifficultySteps) {
            List<Heightmap.Biome> biomes = staged.biomeCapOriginals.Keys.Where(biome => staged.biomeCapUnset.Contains(biome) == false).ToList();
            int openedTop = 0;
            foreach (Heightmap.Biome biome in biomes) { openedTop = Mathf.Max(openedTop, baseline.CapFor(biome)); }
            bool opened = useDifficultySteps == false && staged.maxStars == baseline.maxStars;
            foreach (Heightmap.Biome biome in biomes) {
                int was = baseline.biomeCapOriginals.ContainsKey(biome) ? baseline.CapFor(biome) : staged.CapFor(biome);
                int cap = was;
                if (opened == false && (useDifficultySteps == false || DifficultyPreset.TryGetBiomeCap(biome, staged.maxStars, out cap) == false)) {
                    cap = Mathf.Clamp(Mathf.RoundToInt(was * (float)staged.maxStars / Mathf.Max(1, openedTop)), 1, staged.maxStars);
                }
                staged.biomeCapOriginals[biome] = cap;
                staged.biomeCapManual[biome] = cap;
            }
            staged.biomeCapBaseMax = staged.maxStars;
        }

        // What the page reads as: null for Custom, which it is when picked or when the values match no difficulty.
        private static DifficultyPreset ShownDifficulty() {
            return simpleCustomPicked ? null : StagedDifficulty();
        }

        // The difficulty the staged values amount to, or null if none.
        private static DifficultyPreset StagedDifficulty() {
            foreach (DifficultyPreset preset in DifficultyPreset.All) {
                if (StagedIsDifficulty(preset)) { return preset; }
            }
            return null;
        }

        private static bool StagedIsDifficulty(DifficultyPreset preset) {
            if (staged.maxBossLevel != staged.maxStars) { return false; }
            if (StagedNear(staged.creatureHpPerLevel, PerStarFor(ValConfig.EnemyHealthMultiplier, preset.HealthAtMax)) == false
                || StagedNear(staged.creatureDmgPerLevel, PerStarFor(ValConfig.EnemyDamageLevelMultiplier, preset.DamageAtMax)) == false
                || StagedNear(staged.bossHpPerLevel, PerStarFor(ValConfig.BossEnemyHealthMultiplier, preset.BossHealthAtMax)) == false
                || StagedNear(staged.bossDmgPerLevel, PerStarFor(ValConfig.BossEnemyDamageMultiplier, preset.BossDamageAtMax)) == false
                || StagedNear(staged.allSpawnRate, preset.SpawnRate) == false
                || StagedNear(staged.allNightLevelScale, preset.NightLevelScale) == false
                || StagedNear(staged.allDistanceScale, preset.DistanceScale) == false
                || StagedNear(staged.allBaseDamage, preset.BaseDamage) == false) {
                return false;
            }

            if (staged.bossCurveOn != preset.BossChance > 0f) { return false; }
            if (staged.bossCurveOn && GeneratorsEqual(staged.bossGenerator, BossGeneratorFor(preset)) == false) { return false; }

            // Up to the top star: what a table holds past it is never rolled.
            SortedDictionary<int, float> shown = ShownCreatureCurve();
            foreach (KeyValuePair<int, float> level in preset.CreatureCurve(staged.maxStars)) {
                if (level.Key > staged.maxStars) { break; }
                if (shown.TryGetValue(level.Key, out float chance) == false || Mathf.Abs(chance - level.Value) > 0.001f) { return false; }
            }

            foreach (Heightmap.Biome biome in staged.biomeCapOriginals.Keys) {
                if (DifficultyPreset.TryGetBiomeCap(biome, staged.maxStars, out int cap) && staged.CapFor(biome) != cap) { return false; }
            }
            return true;
        }

        private static bool StagedNear(float a, float b) {
            return Mathf.Abs(a - b) < 0.0002f;
        }

        // Every system on the Level Scaling page. On while any of them is.
        private static bool ProgressiveScalingOn() {
            return staged.enableDistance || staged.enableZone || staged.enableRegion || staged.enableConditional;
        }

        // Off stops them all, and a map only draws a system that is on, so the overlay settings are left as they are. On brings
        // back what the panel opened on, or distance and zones when that was all of them off: nothing ships on, and those two
        // are the ones that need no choices made first.
        private static void SetProgressiveScaling(bool on) {
            bool wasOn = baseline.enableDistance || baseline.enableZone || baseline.enableRegion || baseline.enableConditional;
            staged.enableDistance = on && (wasOn == false || baseline.enableDistance);
            staged.enableZone = on && (wasOn == false || baseline.enableZone);
            staged.enableRegion = on && wasOn && baseline.enableRegion;
            staged.enableConditional = on && wasOn && baseline.enableConditional;
        }

        private static void ResetSimplePage() {
            staged.maxStars = DefaultOf(ValConfig.MaxLevel);
            simpleCustomPicked = false;
            simpleCustomTable = null;
            ApplyDifficulty(DifficultyPreset.Normal);
            staged.enableModifiers = DefaultOf(ValConfig.EnableCreatureModifiers);
            staged.enableColorization = DefaultOf(ValConfig.EnableColorization);
            ResetScalingPage();
            simplePageChanged = true;
        }
    }
}
