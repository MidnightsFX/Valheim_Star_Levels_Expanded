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
    // on another page or in a file reads as Custom rather than as a name that no longer describes the world.
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
        // Whether this page changed values another page has widgets for. Those are built again before they are next shown.
        private static bool simplePageChanged;

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
        }

        private static void BuildSimplePage(Transform parent) {
            GameObject maxRow = WithTip(BindSliderRow(parent, "MaxLevel", staged.maxStars, v => OnSimpleMaxLevelChanged((int)v), max: Mathf.Max(SimpleMaxLevelTop, staged.maxStars)),
                Tip("$sls_cfg_simple_max_level", "$sls_cfg_simple_tip_max_level"));
            simpleMaxSlider = Get<Slider>(maxRow.transform, "Slider");
            simpleMaxBox = Get<InputField>(maxRow.transform, "Value");

            Transform difficultyRow = WithTip(Find(parent, "Difficulty"), Tip("$sls_cfg_simple_difficulty", "$sls_cfg_simple_tip_difficulty")).transform;
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
                if (pages[i].FollowsSimplePage) { RebuildPage(i); }
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

            DifficultyPreset difficulty = StagedDifficulty();
            simpleDifficultyCaption.text = ConfigUI.L(difficulty?.NameToken ?? "$sls_cfg_difficulty_custom");
            simpleDifficultyText.text = ConfigUI.L(difficulty?.DescriptionToken ?? "$sls_cfg_difficulty_custom_desc") + "\n" + L("$sls_cfg_simple_per_star",
                Percent(staged.creatureHpPerLevel), Percent(staged.creatureDmgPerLevel), Percent(staged.bossHpPerLevel), Percent(staged.bossDmgPerLevel));

            simpleChart.SetData(ToStarSeries(LevelSelection.ComputeLevelDistribution(ShownCreatureCurve(), staged.maxStars + 1)));
            simpleBossChart.SetData(ToStarSeries(LevelSelection.ComputeLevelDistribution(ShownBossCurve(), staged.maxBossLevel + 1)));
            simpleExampleText.text = FormatCreatureExample(TrollName(), TrollHp, TrollDmg * staged.allBaseDamage, staged.maxStars, staged.creatureHpPerLevel, staged.creatureDmgPerLevel);

            List<string> notes = new List<string>();
            if (staged.enableConditional) {
                notes.Add(ConfigUI.L("$sls_cfg_distribution_conditional_note"));
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
            DifficultyPreset difficulty = StagedDifficulty();
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

        private static void OnDifficultyClicked() {
            if (staged == null) { return; }
            int current = Array.IndexOf(DifficultyPreset.All, StagedDifficulty());
            ApplyDifficulty(current < 0 ? DifficultyPreset.Normal : DifficultyPreset.All[(current + 1) % DifficultyPreset.All.Length]);
            SimplePageEdited();
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

        // The difficulty the staged values amount to, or null for Custom.
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
            ApplyDifficulty(DifficultyPreset.Normal);
            staged.enableModifiers = DefaultOf(ValConfig.EnableCreatureModifiers);
            staged.enableColorization = DefaultOf(ValConfig.EnableColorization);
            ResetScalingPage();
            simplePageChanged = true;
        }
    }
}
