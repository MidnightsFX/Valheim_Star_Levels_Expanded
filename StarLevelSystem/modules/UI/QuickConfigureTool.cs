using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    internal static class QuickConfigureTool {

        // --- layout constants ---
        private const float PanelW = 900f;
        private const float PanelH = 690f;
        private const float Margin = 26f;
        private const float ContentTop = 92f;
        private const float RowHeight = 34f;
        private const float SubRowHeight = 26f;
        private const float RowGap = 4f;
        private const int PageCount = 5;

        // Sample creatures data | TODO: allow selecting different creature examples?
        private const float TrollHp = 600f;
        private const float TrollDmg = 70f;
        private const float TheElderHP = 2500f;
        private const float TheElderDmg = 60f;

        private static readonly string[] CalcStyleOptions = Enum.GetNames(typeof(DataObjects.LevelupCalculationStyle));
        private static readonly string[] DisplayStyleOptions = Enum.GetNames(typeof(DataObjects.ModifierDisplayStyle));

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
            { "StaminaDrain", "Its attacks drain your stamina." },
            { "EitrDrain", "Its attacks drain your eitr." },
            { "Brutal", "Increases the creature's attack speed." },
            { "ElementalChaos", "Adds random elemental damage on each hit." },
        };

        private static Sprite DistanceExample;
        private static Sprite ZoneExample;

        // --- runtime state ---
        private static GameObject panel;
        private static GameObject[] pageRoots;
        private static int currentPage;
        private static Text titleText;
        private static GameObject backBtn;
        private static GameObject cancelBtn;
        private static GameObject nextBtn;
        private static GameObject applyBtn;
        private static Text creatureExampleText;
        private static Text bossExampleText;
        private static StagedConfig staged;

        private static GameObject mainMenuButton;
        private static GameObject pauseMenuButton;

        internal static void Init() {
            GUIManager.OnCustomGUIAvailable += OnCustomGUIAvailable;
            DistanceExample = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>("distance_rings");
            ZoneExample = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>("region_zones");
        }

        // Create the custom GUI for the start menu
        private static void OnCustomGUIAvailable() {
            try {
                if (GUIManager.IsHeadless() || GUIManager.Instance == null) { return; }
                if (FejdStartup.instance != null) { CreateMainMenuButton(); }
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool failed to set up main menu button: {e.Message}");
            }
        }

        // Create custom GUI for game pause
        internal static void CreatePauseMenuButton(Menu menu) {
            try {
                if (GUIManager.IsHeadless() || GUIManager.Instance == null) { return; }
                if (menu == null || menu.m_root == null) { return; }
                if (pauseMenuButton != null) { return; }
                pauseMenuButton = CreateCornerButton(menu.m_root);
                pauseMenuButton.SetActive(ValConfig.ShowQuickConfigureButton.Value && SynchronizationManager.Instance.PlayerIsAdmin);
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool failed to set up pause menu button: {e.Message}");
            }
        }

        // SettingChanged handler for the client toggle - shows/hides any already-created buttons live.
        public static void OnShowButtonChanged(object s, EventArgs e) {
            bool show = ValConfig.ShowQuickConfigureButton.Value;
            if (mainMenuButton != null) { mainMenuButton.SetActive(show); }
            if (pauseMenuButton != null) { pauseMenuButton.SetActive(show && SynchronizationManager.Instance.PlayerIsAdmin); }
        }

        private static void CreateMainMenuButton() {
            if (mainMenuButton != null) { return; }
            if (GUIManager.CustomGUIFront == null) { return; }
            mainMenuButton = CreateCornerButton(GUIManager.CustomGUIFront.transform);
            mainMenuButton.SetActive(ValConfig.ShowQuickConfigureButton.Value);
        }

        // A small button anchored to the bottom-right corner of the screen
        private static GameObject CreateCornerButton(Transform parent) {
            GameObject go = GUIManager.Instance.CreateButton(
                text: "SLS Config",
                parent: parent,
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 0f),
                position: new Vector2(-20f, 20f),
                width: 150f,
                height: 38f);
            RectTransform rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-20f, 20f);
            go.GetComponent<Button>().onClick.AddListener(OpenPanel);
            return go;
        }

        // ------------------------------------------------------------------------------------------------
        //  Panel Creation
        // ------------------------------------------------------------------------------------------------

        internal static void OpenPanel() {
            staged = StagedConfig.Snapshot();
            // Create fresh UI, allows setting all of the current configs to reflect current reality
            if (panel != null) {
                UnityEngine.Object.Destroy(panel);
                panel = null;
            }
            try {
                BuildPanel();
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool failed to build panel: {e}");
                if (panel != null) { UnityEngine.Object.Destroy(panel); panel = null; }
                return;
            }
            currentPage = 0;
            ShowPage(0);
        }

        private static void ClosePanel() {
            if (panel != null) {
                UnityEngine.Object.Destroy(panel);
                panel = null;
            }
        }

        private static void BuildPanel() {
            panel = GUIManager.Instance.CreateWoodpanel(
                parent: GUIManager.CustomGUIFront.transform, 
                anchorMin: new Vector2(0.5f, 0.5f), 
                anchorMax: new Vector2(0.5f, 0.5f), 
                position: new Vector2(0f, 0f),
                width: PanelW, 
                height: PanelH, 
                draggable: true);

            titleText = AddText(
                parent: panel.transform,
                x: Margin,
                y: 18f,
                w: PanelW - 2 * Margin,
                h: RowHeight,
                text: "StarLevelSystem - Quick Configure",
                fontSize: 22, 
                anchor: TextAnchor.MiddleCenter,
                color: GUIManager.Instance.ValheimYellow);

            // Build out the page skeletons
            pageRoots = new GameObject[PageCount];
            for (int i = 0; i < PageCount; i++) {
                pageRoots[i] = NewRect(
                    name: "Page" + i, 
                    parent: panel.transform,
                    x: Margin,
                    y: ContentTop,
                    w: PanelW - 2 * Margin,
                    h: PanelH - ContentTop - 70f);
            }

            BuildScalingPage(pageRoots[0].transform);
            BuildStatsPage(pageRoots[1].transform);
            BuildModifiersPage(pageRoots[2].transform);
            BuildRaidsPage(pageRoots[3].transform);
            BuildNemesisPage(pageRoots[4].transform);

            float navY = PanelH - 56f;
            backBtn = AddButton(panel.transform, Margin, navY, 130f, "< Back", () => ShowPage(currentPage - 1));
            cancelBtn = AddButton(panel.transform, Margin + 150f, navY, 130f, "Cancel", ClosePanel);
            nextBtn = AddButton(panel.transform, PanelW - Margin - 170f, navY, 170f, "Next >", () => ShowPage(currentPage + 1));
            applyBtn = AddButton(panel.transform, PanelW - Margin - 170f, navY, 170f, "Apply & Save", ApplyAndSave);
        }

        // Moves to the current page
        // Sets the Title
        private static void ShowPage(int page) {
            currentPage = Mathf.Clamp(page, 0, PageCount - 1);
            for (int i = 0; i < pageRoots.Length; i++) {
                pageRoots[i].SetActive(i == currentPage);
            }
            string[] names = { "Scaling Mechanisms", "Stats & Level Generator", "Modifiers", "Raids", "Nemesis System" };
            titleText.text = $"StarLevelSystem - {names[currentPage]}  (Page {currentPage + 1} of {PageCount})";

            backBtn.SetActive(currentPage > 0);
            bool last = currentPage == PageCount - 1;
            nextBtn.SetActive(!last);
            applyBtn.SetActive(last);
            if (currentPage == 1) { UpdateExampleMath(); }
        }

        // ------------------------------------------------------------------------------------------------
        //  Pages
        // ------------------------------------------------------------------------------------------------

        private static void BuildScalingPage(Transform parent) {
            const float ColWidth = 760f;   // left config column + gap + image(300)
            const float ImgW = 300f;
            const float ImgH = 168f;
            const float LeftColW = ColWidth - ImgW;   // configuration area to the left of the example image

            // Build each row as its own container, collect them in order, then space the column out in one pass.
            List<GameObject> column = new List<GameObject> {
                AddHeaderRow(parent, LeftColW, "$sls_cfg_scaling_selection_header", TextAnchor.MiddleCenter),
                AddTextRow(parent, ColWidth, 34f, "Here are some high level configurations from the mod, many more things can be customized within the yaml configuration.", 13, GUIManager.Instance.ValheimBeige, TextAnchor.UpperCenter),
                AddScalingFeatureRow(parent, ColWidth, ImgW, ImgH,
                    DistanceExample,
                    "$sls_cfg_distance_scale_header",
                    "$sls_cfg_distance_scale_desc",
                    staged.enableDistance,
                    v => staged.enableDistance = v,
                    "$sls_cfg_distance_overlay_toggle",
                    staged.enableDistanceOverlay,
                    v => staged.enableDistanceOverlay = v),
                AddDividerRow(parent, ColWidth),
                AddScalingFeatureRow(parent, ColWidth, ImgW, ImgH,
                    ZoneExample,
                    "$sls_cfg_zone_scale_header",
                    "$sls_cfg_zone_scale_desc",
                    staged.enableZone,
                    v => staged.enableZone = v,
                    "$sls_cfg_zone_overlay_toggle",
                    staged.enableZoneOverlay,
                    v => staged.enableZoneOverlay = v),
                AddDividerRow(parent, ColWidth),
                AddToggleRow(parent, ColWidth, 360f, "$sls_cfg_conditional_scale_header", staged.enableConditional, v => staged.enableConditional = v, true),
                AddTextRow(parent, ColWidth, 24f, "$sls_cfg_conditional_scale_desc", 13, GUIManager.Instance.ValheimBeige),
            };
            // Center the column within the page root so it isn't left-biased (page root is PanelW - 2*Margin wide).
            float colOffsetX = Mathf.Max(0f, (PanelW - 2 * Margin - ColWidth) * 0.5f);
            LayoutColumn(column, colOffsetX, 4f);
        }

        private static void BuildStatsPage(Transform parent) {
            const float RightColumnX = 450f;
            const float LeftColWidth = 430f;
            const float RightColWidth = PanelW - 2 * Margin - RightColumnX;
            const float LabelWidth = 168f;
            const float SliderWidth = 150f;
            const float ValueWidth = 60f;
            const float StartY = 2f;
            const float DividerH = 12f;

            // Left column - stat multipliers and multiplayer scaling. A full-width divider separates the
            // creature stats (above) from the boss stats (below).
            List<GameObject> left = new List<GameObject> {
                AddHeaderRow(parent, LeftColWidth, "Per-level stats"),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Creature HP / level", 0f, 5f, staged.creatureHpPerLevel, false, v => { staged.creatureHpPerLevel = v; UpdateExampleMath(); }),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Creature dmg / level", 0f, 2f, staged.creatureDmgPerLevel, false, v => { staged.creatureDmgPerLevel = v; UpdateExampleMath(); }),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Max level (stars)", 1f, 200f, staged.maxLevel, true, v => { staged.maxLevel = (int)v; UpdateExampleMath(); }),
                AddDividerRow(parent, PanelW - 2 * Margin, DividerH),   // spans both columns, between creature and boss sections
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Boss HP / level", 0f, 5f, staged.bossHpPerLevel, false, v => { staged.bossHpPerLevel = v; UpdateExampleMath(); }),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Boss dmg / level", 0f, 5f, staged.bossDmgPerLevel, false, v => { staged.bossDmgPerLevel = v; UpdateExampleMath(); }),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Max boss level", 1f, 200f, staged.maxBossLevel, true, v => { staged.maxBossLevel = (int)v; UpdateExampleMath(); }),
                AddSpacerRow(parent, LeftColWidth, 4f),
                AddHeaderRow(parent, LeftColWidth, "Multiplayer scaling"),
                AddToggleRow(parent, LeftColWidth, LabelWidth + 170f, "Enemies gain HP with more players", staged.mpHealth, v => staged.mpHealth = v),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "HP per extra player", 0f, 0.99f, staged.mpHealthMod, false, v => staged.mpHealthMod = v),
                AddToggleRow(parent, LeftColWidth, LabelWidth + 170f, "Enemies gain dmg with more players", staged.mpDamage, v => staged.mpDamage = v),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Dmg per extra player", 0f, 2f, staged.mpDamageMod, false, v => staged.mpDamageMod = v),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Players needed nearby", 1f, 20f, staged.mpRequiredPlayers, true, v => staged.mpRequiredPlayers = (int)v),
            };
            LayoutColumn(left, 0f, StartY);

            // Right column - example previews aligned to the matching left-column stat rows: the creature (Troll)
            // example sits beside the Creature HP/dmg sliders, the boss (The Elder) example beside the Boss HP/dmg
            // sliders. The default level generator is laid out below them.
            const float RowPitch = RowHeight + RowGap;
            GameObject exHeader = AddHeaderRow(parent, RightColWidth, "Example scaling preview");
            PositionRow(exHeader, RightColumnX, StartY);

            GameObject creatureEx = AddTextRow(parent, RightColWidth, 80f, "", 15, GUIManager.Instance.ValheimBeige);
            creatureExampleText = creatureEx.GetComponentInChildren<Text>();
            PositionRow(creatureEx, RightColumnX, StartY + RowPitch);          // aligns with "Creature HP / level"

            // The divider between the creature and boss sections pushes the boss rows down by its height + gap;
            // keep the boss example (and the generator below it) aligned to that shift.
            float bossShift = DividerH + RowGap;
            GameObject bossEx = AddTextRow(parent, RightColWidth, 80f, "", 15, GUIManager.Instance.ValheimBeige);
            bossExampleText = bossEx.GetComponentInChildren<Text>();
            PositionRow(bossEx, RightColumnX, StartY + 4 * RowPitch + bossShift);   // aligns with "Boss HP / level"

            // Default level generator below the previews. The Gaussian offset row is tracked so it can be shown
            // only when the Gaussian curve style is selected.
            float genStartY = StartY + 6 * RowPitch + bossShift + 8f;
            GameObject gaussianRow = null;
            List<GameObject> gen = new List<GameObject>();
            gen.Add(AddHeaderRow(parent, RightColWidth, "Default level generator"));
            gen.Add(AddSliderRow(parent, RightColWidth, LabelWidth, SliderWidth, ValueWidth, "Min level", 1f, 50f, staged.generator.MinLevel, true, v => staged.generator.MinLevel = (int)v));
            gen.Add(AddSliderRow(parent, RightColWidth, LabelWidth, SliderWidth, ValueWidth, "Max level", 1f, 200f, staged.generator.MaxLevel, true, v => staged.generator.MaxLevel = (int)v));
            gen.Add(AddSliderRow(parent, RightColWidth, LabelWidth, SliderWidth, ValueWidth, "Level-up chance", 0f, 1f, staged.generator.LevelUpChance, false, v => staged.generator.LevelUpChance = v));
            gen.Add(AddEnumCycleRow(parent, RightColWidth, LabelWidth, 150f, "Curve style", CalcStyleOptions, (int)staged.generator.LevelupCalculationStyle, i => {
                staged.generator.LevelupCalculationStyle = (LevelupCalculationStyle)i;
                if (gaussianRow != null) {
                    gaussianRow.SetActive((LevelupCalculationStyle)i == LevelupCalculationStyle.Gaussian);
                    LayoutColumn(gen, RightColumnX, genStartY);
                }
            }));
            gaussianRow = AddSliderRow(parent, RightColWidth, LabelWidth, SliderWidth, ValueWidth, "Gaussian offset", -1f, 1f, staged.generator.GaussianOffset, false, v => staged.generator.GaussianOffset = v);
            gen.Add(gaussianRow);
            gen.Add(AddSliderRow(parent, RightColWidth, LabelWidth, SliderWidth, ValueWidth, "Night multiplier", 0f, 5f, staged.generator.NightMultiplier, false, v => staged.generator.NightMultiplier = v));

            gaussianRow.SetActive(staged.generator.LevelupCalculationStyle == LevelupCalculationStyle.Gaussian);
            LayoutColumn(gen, RightColumnX, genStartY);

            UpdateExampleMath();
        }

        private static void BuildRaidsPage(Transform parent) {
            const float FullWidth = PanelW - 2 * Margin;
            const float LeftColWidth = 430f;
            const float LabelWidth = 235f, SliderWidth = 130f, ValueWidth = 56f;
            const float ToggleLabelWidth = 300f;
            const float StartY = 4f;

            // Full-width header + intro across the top.
            GameObject header = AddHeaderRow(parent, FullWidth, "Raids", TextAnchor.MiddleCenter);
            PositionRow(header, 0f, StartY);
            GameObject intro = AddTextRow(parent, FullWidth, 40f, "StarLevelSystem replaces vanilla raids with its own configurable raids. Detailed per-raid settings live in RaidSettings.yaml.", 13, GUIManager.Instance.ValheimBeige, TextAnchor.UpperCenter);
            PositionRow(intro, 0f, StartY + RowHeight + RowGap);
            float colStartY = StartY + RowHeight + RowGap + 40f + 8f;

            // Left column - global raid settings.
            List<GameObject> left = new List<GameObject> {
                AddToggleRow(parent, LeftColWidth, ToggleLabelWidth, "Enable SLS Raids", staged.enableSlsRaids, v => staged.enableSlsRaids = v, true),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Raid frequency (lower = more often)", 0.1f, 10f, staged.raidEventRate, false, v => staged.raidEventRate = v),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Minutes between checks", 1f, 120f, staged.raidCheckMinutes, true, v => staged.raidCheckMinutes = (int)v),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Max attempts / player", 0f, 50f, staged.maxRaidAttempts, true, v => staged.maxRaidAttempts = (int)v),
                AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Max active raids", 1f, 20f, staged.maxActiveRaids, true, v => staged.maxActiveRaids = (int)v),
            };
            LayoutColumn(left, 0f, colStartY);

            // Right side - scrollable list of every configured raid, each with an enable/disable toggle.
            // Disabled raids keep their config in RaidSettings.yaml and are simply marked Enabled = false.
            const float ScrollX = 446f;
            const float ScrollW = 402f;
            const float ScrollH = 398f;
            AddText(parent, ScrollX, colStartY, ScrollW, RowHeight, "Enable / disable raids", 16, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimYellow);
            GameObject scrollHolder = NewRect("RaidScrollHolder", parent, ScrollX, colStartY + RowHeight, ScrollW, ScrollH);
            GameObject scrollCanvas = GUIManager.Instance.CreateScrollView(
                scrollHolder.transform, false, true, 8f, 4f,
                GUIManager.Instance.ValheimScrollbarHandleColorBlock, new Color(0f, 0f, 0f, 0.5f),
                ScrollW, ScrollH);
            Transform content = scrollCanvas.transform.Find("Scroll View/Viewport/Content");
            if (content != null) {
                float contentW = ScrollW - 16f;   // minus the vertical scrollbar + border (handleSize + 2*border)
                List<RaidDefinition> raids = staged.raidSource?.Raids;
                if (raids != null) {
                    foreach (RaidDefinition raid in raids.OrderBy(r => r.Name)) {
                        string raidName = raid.Name;   // capture for the closure
                        AddRaidEntry(content, contentW, raid, staged.raidsOn.Contains(raidName), on => {
                            if (on) { staged.raidsOn.Add(raidName); }
                            else { staged.raidsOn.Remove(raidName); }
                        });
                    }
                }
            }
        }

        // A single raid line: enable toggle on the left, prettified name + a brief spawn summary to its right.
        private static void AddRaidEntry(Transform content, float width, RaidDefinition raid, bool enabled, Action<bool> onChange) {
            GameObject row = NewLayoutRow(content, width, 44f);
            GameObject tgo = GUIManager.Instance.CreateToggle(row.transform, 22f, 22f);
            tgo.transform.SetParent(row.transform, false);
            RectTransform trt = (RectTransform)tgo.transform;
            trt.localScale = Vector3.one;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(2f, -3f);
            Toggle tg = tgo.GetComponent<Toggle>();
            tg.isOn = enabled;
            tg.onValueChanged.AddListener(b => onChange(b));

            const float TextX = 32f;
            AddText(row.transform, TextX, 0f, width - TextX - 4f, 20f, PrettifyRaidName(raid.Name), 14, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimOrange);
            int types = raid.Spawns != null ? raid.Spawns.Select(sp => sp.PrefabName).Distinct().Count() : 0;
            string sub = $"{types} creature type{(types == 1 ? "" : "s")}  ·  {raid.Duration:0}s";
            AddText(row.transform, TextX, 20f, width - TextX - 4f, 24f, sub, 12, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);
        }

        // "army_eikthyr" -> "Army Eikthyr", "gjall_ambush" -> "Gjall Ambush".
        private static string PrettifyRaidName(string name) {
            if (string.IsNullOrEmpty(name)) { return name; }
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' '));
        }

        private static void BuildNemesisPage(Transform parent) {
            const float LeftColWidth = 400f;
            const float LeftLabelWidth = 200f, LeftSliderWidth = 130f, LeftValueWidth = 60f;
            const float RightColumnX = 440f;
            const float RightColWidth = PanelW - 2 * Margin - RightColumnX;
            const float RightLabelWidth = 210f, RightSliderWidth = 120f, RightValueWidth = 60f;
            const float StartY = 4f;

            // Full-width intro describing the system.
            GameObject intro = AddTextRow(parent, PanelW - 2 * Margin, 40f, "The Nemesis system is a personal game manager that can tune up or down the world around you or your group.", 14, GUIManager.Instance.ValheimBeige);
            PositionRow(intro, 0f, StartY);
            float colStartY = StartY + 44f;

            // Left column - core nemesis settings.
            List<GameObject> left = new List<GameObject> {
                AddHeaderRow(parent, LeftColWidth, "Nemesis settings"),
                AddToggleRow(parent, LeftColWidth, LeftLabelWidth + 60f, "Enable Nemesis system", staged.enableNemesis, v => staged.enableNemesis = v),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Action cooldown (sec)", 0f, 120f, staged.nemCooldown, false, v => staged.nemCooldown = v),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Influence radius (m)", 0f, 1000f, staged.nemInfluence, false, v => staged.nemInfluence = v),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Min spawn distance (m)", 0f, 500f, staged.nemMinSpawn, false, v => staged.nemMinSpawn = v),
            };
            LayoutColumn(left, 0f, colStartY);

            // Right column - core subset of the score system.
            List<GameObject> right = new List<GameObject> {
                AddHeaderRow(parent, RightColWidth, "Score system"),
                AddSliderRow(parent, RightColWidth, RightLabelWidth, RightSliderWidth, RightValueWidth, "Neutral score", 0f, 20000f, staged.neutralScore, true, v => staged.neutralScore = v),
                AddSliderRow(parent, RightColWidth, RightLabelWidth, RightSliderWidth, RightValueWidth, "Min score", 0f, 20000f, staged.minScore, true, v => staged.minScore = v),
                AddSliderRow(parent, RightColWidth, RightLabelWidth, RightSliderWidth, RightValueWidth, "Max score", 0f, 20000f, staged.maxScore, true, v => staged.maxScore = v),
                AddSliderRow(parent, RightColWidth, RightLabelWidth, RightSliderWidth, RightValueWidth, "Decay per update", 0f, 2000f, staged.decayPerUpdate, true, v => staged.decayPerUpdate = v),
                AddSliderRow(parent, RightColWidth, RightLabelWidth, RightSliderWidth, RightValueWidth, "Score interval (sec)", 1f, 120f, staged.scoreInterval, true, v => staged.scoreInterval = v),
                AddSliderRow(parent, RightColWidth, RightLabelWidth, RightSliderWidth, RightValueWidth, "Boss-kill bonus", 0f, 5000f, staged.bossKillBonus, true, v => staged.bossKillBonus = v),
                AddSliderRow(parent, RightColWidth, RightLabelWidth, RightSliderWidth, RightValueWidth, "Death score reduction", 0f, 5000f, staged.deathReduction, true, v => staged.deathReduction = v),
            };
            LayoutColumn(right, RightColumnX, colStartY);
        }

        private static void BuildModifiersPage(Transform parent) {
            // Left column holds all of the numeric/toggle config; sliders are kept narrow so their value
            // boxes don't run into the scroll view on the right.
            const float LeftColWidth = 430f;
            const float LeftLabelWidth = 200f, LeftSliderWidth = 120f, LeftValueWidth = 56f;
            const float ToggleLabelWidth = 300f;
            const float StartY = 4f;

            List<GameObject> left = new List<GameObject> {
                AddHeaderRow(parent, LeftColWidth, "Creature modifiers"),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Max major modifiers", 0f, 6f, staged.maxMajor, true, v => staged.maxMajor = (int)v),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Max minor modifiers", 0f, 6f, staged.maxMinor, true, v => staged.maxMinor = (int)v),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Major modifier chance", 0f, 1f, staged.chanceMajor, false, v => staged.chanceMajor = v),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Minor modifier chance", 0f, 1f, staged.chanceMinor, false, v => staged.chanceMinor = v),
                AddToggleRow(parent, LeftColWidth, ToggleLabelWidth, "Limit modifier count to star level", staged.limitToStarLevel, v => staged.limitToStarLevel = v),
                AddHeaderRow(parent, LeftColWidth, "Boss modifiers"),
                AddToggleRow(parent, LeftColWidth, ToggleLabelWidth, "Bosses can have modifiers", staged.enableBossMods, v => staged.enableBossMods = v),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Boss modifier chance", 0f, 1f, staged.chanceBoss, false, v => staged.chanceBoss = v),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Max boss modifiers", 0f, 6f, staged.maxBossMods, true, v => staged.maxBossMods = (int)v),
                AddHeaderRow(parent, LeftColWidth, "Modifier display"),
                AddSliderRow(parent, LeftColWidth, LeftLabelWidth, LeftSliderWidth, LeftValueWidth, "Max name prefixes", 0f, 6f, staged.prefixLimit, true, v => staged.prefixLimit = (int)v),
                AddToggleRow(parent, LeftColWidth, ToggleLabelWidth, "Minor modifiers first in name", staged.minorFirst, v => staged.minorFirst = v),
                AddEnumCycleRow(parent, LeftColWidth, LeftLabelWidth, 150f, "Icon display style", DisplayStyleOptions, (int)staged.displayStyle, i => staged.displayStyle = (ModifierDisplayStyle)i),
            };
            LayoutColumn(left, 0f, StartY);

            // Right side - scrollable list of every modifier defined in Modifiers.yaml, grouped by category,
            // each with an enable/disable toggle and a brief description.
            const float ScrollX = 446f;
            const float ScrollW = 400f;
            const float ScrollH = 478f;
            AddText(parent, ScrollX, StartY, ScrollW, RowHeight, "Enable / disable modifiers", 16, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimYellow);
            GameObject scrollHolder = NewRect("ModScrollHolder", parent, ScrollX, StartY + RowHeight, ScrollW, ScrollH);
            GameObject scrollCanvas = GUIManager.Instance.CreateScrollView(
                scrollHolder.transform, false, true, 8f, 4f,
                GUIManager.Instance.ValheimScrollbarHandleColorBlock, new Color(0f, 0f, 0f, 0.5f),
                ScrollW, ScrollH);
            Transform content = scrollCanvas.transform.Find("Scroll View/Viewport/Content");
            if (content != null) {
                float contentW = ScrollW - 16f;   // minus the vertical scrollbar + border (handleSize + 2*border)
                AddModifierCategory(content, contentW, "Boss modifiers", ModifierType.Boss, staged.modifierSource?.BossModifiers);
                AddModifierCategory(content, contentW, "Major modifiers", ModifierType.Major, staged.modifierSource?.MajorModifiers);
                AddModifierCategory(content, contentW, "Minor modifiers", ModifierType.Minor, staged.modifierSource?.MinorModifiers);
            }
        }

        // Adds a category header followed by one toggle row per modifier defined in that category.
        private static void AddModifierCategory(Transform content, float width, string label, ModifierType type, Dictionary<string, CreatureModifierConfiguration> dict) {
            if (dict == null || dict.Count == 0) { return; }
            AddModifierCategoryHeader(content, width, label);
            HashSet<string> enabled = staged.modifierOn[type];
            foreach (string name in dict.Keys.OrderBy(n => n)) {
                string modName = name;   // capture for the closure
                AddModifierEntry(content, width, modName, enabled.Contains(modName), on => {
                    if (on) { staged.modifierOn[type].Add(modName); }
                    else { staged.modifierOn[type].Remove(modName); }
                });
            }
        }

        private static void AddModifierCategoryHeader(Transform content, float width, string label) {
            GameObject row = NewLayoutRow(content, width, 30f);
            AddText(row.transform, 2f, 4f, width - 4f, 24f, label, 16, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimYellow);
        }

        // A single modifier line: enable toggle on the left, prettified name + brief description to its right.
        private static void AddModifierEntry(Transform content, float width, string name, bool enabled, Action<bool> onChange) {
            GameObject row = NewLayoutRow(content, width, 48f);
            GameObject tgo = GUIManager.Instance.CreateToggle(row.transform, 22f, 22f);
            tgo.transform.SetParent(row.transform, false);
            RectTransform trt = (RectTransform)tgo.transform;
            trt.localScale = Vector3.one;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(2f, -3f);
            Toggle tg = tgo.GetComponent<Toggle>();
            tg.isOn = enabled;
            tg.onValueChanged.AddListener(b => onChange(b));

            const float TextX = 32f;
            AddText(row.transform, TextX, 0f, width - TextX - 4f, 20f, Prettify(name), 14, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimOrange);
            string desc = ModifierDescriptions.TryGetValue(name, out string d) ? d : "";
            AddText(row.transform, TextX, 20f, width - TextX - 4f, 26f, desc, 12, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);
        }

        // A row sized by a LayoutElement so the scroll view's VerticalLayoutGroup stacks it correctly.
        private static GameObject NewLayoutRow(Transform content, float width, float height) {
            GameObject row = new GameObject("ModRow", typeof(RectTransform), typeof(LayoutElement));
            row.layer = GUIManager.UILayer;
            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.SetParent(content, false);
            rt.sizeDelta = new Vector2(width, height);
            LayoutElement le = row.GetComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.minWidth = width;
            le.preferredWidth = width;
            return row;
        }

        // "ResistPierce" -> "Resist Pierce", "BossSummoner" -> "Boss Summoner".
        private static string Prettify(string name) => Regex.Replace(name, "(\\B[A-Z])", " $1");

        private static void UpdateExampleMath() {
            if (staged == null) { return; }
            if (creatureExampleText != null) {
                int maxStars = Mathf.Max(0, staged.maxLevel);
                int medStars = Mathf.Max(0, Mathf.CeilToInt(maxStars / 2f));
                creatureExampleText.text = FormatCreatureExample("Troll", TrollHp, TrollDmg, medStars, maxStars, staged.creatureHpPerLevel, staged.creatureDmgPerLevel);
            }
            if (bossExampleText != null) {
                int maxStars = Mathf.Max(0, staged.maxBossLevel);
                int medStars = Mathf.Max(0, Mathf.CeilToInt(maxStars / 2f));
                bossExampleText.text = FormatCreatureExample("The Elder (boss)", TheElderHP, TheElderDmg, medStars, maxStars, staged.bossHpPerLevel, staged.bossDmgPerLevel);
            }
        }

        private static string FormatCreatureExample(string name, float baseHp, float baseDmg, int medStars, int maxStars, float hpMul, float dmgMul) {
            float Hp(int stars) => baseHp * (1f + hpMul * stars);
            float Dmg(int stars) => baseDmg * (1f + dmgMul * stars);
            return $"{name} (base {baseHp:0} HP / {baseDmg:0} dmg)\n" +
                   $"  Min (0 stars):   {Hp(0):0} HP    {Dmg(0):0} dmg\n" +
                   $"  Median ({medStars} stars):   {Hp(medStars):0} HP    {Dmg(medStars):0} dmg\n" +
                   $"  Max ({maxStars} stars):   {Hp(maxStars):0} HP    {Dmg(maxStars):0} dmg\n";
        }

        // ------------------------------------------------------------------------------------------------
        //  Apply
        // ------------------------------------------------------------------------------------------------

        private static void ApplyAndSave() {
            try {
                ValConfig.EnableDistanceLevelScalingBonus.Value = staged.enableDistance;
                ValConfig.EnableMapRingsForDistanceBonus.Value = staged.enableDistanceOverlay;
                ValConfig.EnableZoneScalingBonus.Value = staged.enableZone;
                ValConfig.EnableZoneMapOverlay.Value = staged.enableZoneOverlay;

                ValConfig.EnemyHealthMultiplier.Value = staged.creatureHpPerLevel;
                ValConfig.EnemyDamageLevelMultiplier.Value = staged.creatureDmgPerLevel;
                ValConfig.BossEnemyHealthMultiplier.Value = staged.bossHpPerLevel;
                ValConfig.BossEnemyDamageMultiplier.Value = staged.bossDmgPerLevel;
                ValConfig.MaxLevel.Value = staged.maxLevel;
                ValConfig.MaxBossLevel.Value = staged.maxBossLevel;

                ValConfig.EnableMultiplayerEnemyHealthScaling.Value = staged.mpHealth;
                ValConfig.MultiplayerEnemyHealthModifier.Value = staged.mpHealthMod;
                ValConfig.EnableMultiplayerEnemyDamageScaling.Value = staged.mpDamage;
                ValConfig.MultiplayerEnemyDamageModifier.Value = staged.mpDamageMod;
                ValConfig.MultiplayerScalingRequiredPlayersNearby.Value = staged.mpRequiredPlayers;

                ValConfig.MaxMajorModifiersPerCreature.Value = staged.maxMajor;
                ValConfig.MaxMinorModifiersPerCreature.Value = staged.maxMinor;
                ValConfig.ChanceMajorModifier.Value = staged.chanceMajor;
                ValConfig.ChanceMinorModifier.Value = staged.chanceMinor;
                ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value = staged.limitToStarLevel;
                ValConfig.EnableBossModifiers.Value = staged.enableBossMods;
                ValConfig.ChanceOfBossModifier.Value = staged.chanceBoss;
                ValConfig.MaxBossModifiersPerBoss.Value = staged.maxBossMods;
                ValConfig.LimitCreatureModifierPrefixes.Value = staged.prefixLimit;
                ValConfig.MinorModifiersFirstInName.Value = staged.minorFirst;
                ValConfig.ModifierIconDisplayStyle.Value = staged.displayStyle.ToString();

                // Write out the levelsettings
                CreatureLevelSettings settings = LevelSystemData.SLE_Level_Settings;
                if (settings != null) {
                    settings.EnableConditionalCreatureLevelupChance = staged.enableConditional;
                    settings.DefaultLevelupGenerators = new List<LevelGenerator> { staged.generator };
                    string yaml = DataObjects.yamlserializer.Serialize(settings);
                    File.WriteAllText(ValConfig.levelsFilePath, yaml);
                    LevelSystemData.UpdateYamlConfig(yaml);
                }

                // Persist modifier enable/disable changes (only if a toggle actually changed, so we don't
                // rewrite the modifier YAML when the user only touched the sliders). Disabled modifiers keep
                // their config in the file and are simply marked Enabled = false.
                if (ModifiersChanged()) {
                    CreatureModifierCollection src = staged.modifierSource;
                    ApplyEnabledFlags(src.BossModifiers, staged.modifierOn[ModifierType.Boss]);
                    ApplyEnabledFlags(src.MajorModifiers, staged.modifierOn[ModifierType.Major]);
                    ApplyEnabledFlags(src.MinorModifiers, staged.modifierOn[ModifierType.Minor]);
                    string myaml = DataObjects.yamlserializer.Serialize(src);
                    File.WriteAllText(ValConfig.creatureModifierFilePath, myaml);
                    CreatureModifiersData.UpdateModifierConfig(myaml);
                    CreatureModifiersData.ClearProbabilityCaches();
                }

                // Raids - plain BepInEx ConfigEntries; "Enable SLS Raids" is the inverse of vanilla raids.
                ValConfig.UseVanillaRaidConfiguration.Value = !staged.enableSlsRaids;
                ValConfig.RaidEventRate.Value = staged.raidEventRate;
                ValConfig.ServerTimeBetweenRaidStartChecks.Value = staged.raidCheckMinutes;
                ValConfig.MaxRaidAttemptsPerPlayer.Value = staged.maxRaidAttempts;
                ValConfig.MaxActiveRaids.Value = staged.maxActiveRaids;

                // Per-raid enable/disable lives in the RaidSettings YAML (RaidDefinition.Enabled). Only rewrite
                // when a toggle actually changed; work on a deserialized copy so the live config isn't mutated
                // in place (all other per-raid settings are preserved).
                if (RaidsChanged()) {
                    RaidConfiguration rcfg = DataObjects.yamldeserializer.Deserialize<RaidConfiguration>(
                        DataObjects.yamlserializer.Serialize(staged.raidSource));
                    foreach (RaidDefinition raid in rcfg.Raids) {
                        raid.Enabled = staged.raidsOn.Contains(raid.Name);
                    }
                    string ryaml = DataObjects.yamlserializer.Serialize(rcfg);
                    File.WriteAllText(ValConfig.raidsFilePath, ryaml);
                    RaidsData.UpdateYamlConfig(ryaml);
                }

                // Nemesis - enable flag is a ConfigEntry; the rest is in the NemesisSettings YAML.
                ValConfig.EnableNemesisSystem.Value = staged.enableNemesis;
                if (NemesisChanged()) {
                    // Work on a deserialized copy so the shared default/live instance is never mutated in place
                    // (and all other YAML sections + NemesisVersion are preserved).
                    NemesisConfiguration ncfg = DataObjects.yamldeserializer.Deserialize<NemesisConfiguration>(
                        DataObjects.yamlserializer.Serialize(staged.nemesisSource));
                    ncfg.NemesisActionCooldownSeconds = staged.nemCooldown;
                    ncfg.NemesisInfluenceRadius = staged.nemInfluence;
                    ncfg.NemesisMinSpawnDistance = staged.nemMinSpawn;
                    if (ncfg.ScoreSystem == null) { ncfg.ScoreSystem = new NemesisScore(); }
                    ncfg.ScoreSystem.NeutralScore = staged.neutralScore;
                    ncfg.ScoreSystem.MinScore = staged.minScore;
                    ncfg.ScoreSystem.MaxScore = staged.maxScore;
                    ncfg.ScoreSystem.DecayPerUpdate = staged.decayPerUpdate;
                    ncfg.ScoreSystem.ScoreIntervalSeconds = staged.scoreInterval;
                    ncfg.ScoreSystem.BossKillBonus = staged.bossKillBonus;
                    ncfg.ScoreSystem.DeathScoreReduction = staged.deathReduction;
                    string nyaml = DataObjects.yamlserializer.Serialize(ncfg);
                    File.WriteAllText(ValConfig.nemesisFilePath, nyaml);
                    NemesisSystemData.UpdateYamlConfig(nyaml);
                }

                Logger.LogInfo("QuickConfigureTool applied and saved configuration.");
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool failed to apply configuration: {e}");
            }
            ClosePanel();
        }

        // True if any modifier's staged enable state differs from its current Enabled flag.
        private static bool ModifiersChanged() {
            if (staged?.modifierSource == null) { return false; }
            return EnabledDiffers(staged.modifierSource.BossModifiers, staged.modifierOn[ModifierType.Boss])
                || EnabledDiffers(staged.modifierSource.MajorModifiers, staged.modifierOn[ModifierType.Major])
                || EnabledDiffers(staged.modifierSource.MinorModifiers, staged.modifierOn[ModifierType.Minor]);
        }

        private static bool EnabledDiffers(Dictionary<string, CreatureModifierConfiguration> dict, HashSet<string> enabledNames) {
            if (dict == null) { return false; }
            foreach (KeyValuePair<string, CreatureModifierConfiguration> kv in dict) {
                if (kv.Value.Enabled != enabledNames.Contains(kv.Key)) { return true; }
            }
            return false;
        }

        // Writes the staged on/off state back onto each modifier's Enabled flag.
        private static void ApplyEnabledFlags(Dictionary<string, CreatureModifierConfiguration> dict, HashSet<string> enabledNames) {
            if (dict == null) { return; }
            foreach (KeyValuePair<string, CreatureModifierConfiguration> kv in dict) {
                kv.Value.Enabled = enabledNames.Contains(kv.Key);
            }
        }

        // True if any staged nemesis YAML value differs from the live config (so we only rewrite when changed).
        private static bool NemesisChanged() {
            NemesisConfiguration n = staged?.nemesisSource;
            if (n == null) { return false; }
            NemesisScore sc = n.ScoreSystem ?? new NemesisScore();
            return n.NemesisActionCooldownSeconds != staged.nemCooldown
                || n.NemesisInfluenceRadius != staged.nemInfluence
                || n.NemesisMinSpawnDistance != staged.nemMinSpawn
                || sc.NeutralScore != staged.neutralScore
                || sc.MinScore != staged.minScore
                || sc.MaxScore != staged.maxScore
                || sc.DecayPerUpdate != staged.decayPerUpdate
                || sc.ScoreIntervalSeconds != staged.scoreInterval
                || sc.BossKillBonus != staged.bossKillBonus
                || sc.DeathScoreReduction != staged.deathReduction;
        }

        // True if any raid's staged enable state differs from its current Enabled flag.
        private static bool RaidsChanged() {
            if (staged?.raidSource?.Raids == null) { return false; }
            foreach (RaidDefinition raid in staged.raidSource.Raids) {
                if (raid.Enabled != staged.raidsOn.Contains(raid.Name)) { return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------
        //  UI builder helpers (top-left origin: anchor + pivot (0,1), positions measured down from the top)
        // ------------------------------------------------------------------------------------------------

        private static GameObject NewRect(string name, Transform parent, float x, float y, float w, float h) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = GUIManager.UILayer;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return go;
        }

        private static Text AddText(Transform parent, float x, float y, float w, float h, string text, int fontSize, TextAnchor anchor, Color? color = null) {
            GameObject go = GUIManager.Instance.CreateText(
                text: Localization.instance.Localize(text),
                parent: parent,
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                position: new Vector2(x, -y),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: fontSize,
                color: color ?? GUIManager.Instance.ValheimBeige,
                outline: true,
                outlineColor: Color.black,
                width: w,
                height: h,
                addContentSizeFitter: false);
            Text t = go.GetComponent<Text>();
            RectTransform rt = t.rectTransform;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // Each configuration row lives in its own container GameObject so it can be referenced (e.g. to toggle
        // visibility) and so a whole column is laid out in one pass instead of tracking a running Y per entry.
        private static GameObject NewRow(Transform parent, float width, float height) {
            GameObject go = new GameObject("Row", typeof(RectTransform));
            go.layer = GUIManager.UILayer;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, 0f);
            return go;
        }

        // Positions each active row of a column top-to-bottom, advancing by the row's own height + gap.
        // Inactive rows are skipped so a hidden row collapses
        private static void LayoutColumn(List<GameObject> rows, float x, float startY) {
            float y = startY;
            foreach (GameObject row in rows) {
                if (row == null || !row.activeSelf) { continue; }
                RectTransform rt = (RectTransform)row.transform;
                rt.anchoredPosition = new Vector2(x, -y);
                y += rt.sizeDelta.y + RowGap;
            }
        }

        // Positions a single row at an explicit (x, y) within its column, for rows placed outside a LayoutColumn pass.
        private static void PositionRow(GameObject row, float x, float y) {
            ((RectTransform)row.transform).anchoredPosition = new Vector2(x, -y);
        }

        private static GameObject AddHeaderRow(Transform parent, float colWidth, string text, TextAnchor anchor = TextAnchor.MiddleLeft) {
            GameObject row = NewRow(parent, colWidth, RowHeight);
            AddText(row.transform, 0f, 0f, colWidth, RowHeight, text, 18, anchor, GUIManager.Instance.ValheimYellow);
            return row;
        }

        // Thin horizontal rule used to separate sections within a column.
        private static GameObject AddDividerRow(Transform parent, float colWidth, float height = 12f) {
            GameObject row = NewRow(parent, colWidth, height);
            GameObject line = NewUI("Divider", row.transform, typeof(Image));
            RectTransform rt = (RectTransform)line.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(colWidth, 2f);
            rt.anchoredPosition = new Vector2(0f, -(height * 0.5f - 1f));   // vertically centered in the row
            Image img = line.GetComponent<Image>();
            img.color = new Color(0.6f, 0.5f, 0.35f, 0.6f);
            img.raycastTarget = false;
            return row;
        }

        private static GameObject AddTextRow(Transform parent, float colWidth, float height, string text, int fontSize, Color color, TextAnchor anchor = TextAnchor.UpperLeft) {
            GameObject row = NewRow(parent, colWidth, height);
            AddText(row.transform, 0f, 0f, colWidth, height, text, fontSize, anchor, color);
            return row;
        }

        private static GameObject AddSpacerRow(Transform parent, float colWidth, float height) {
            return NewRow(parent, colWidth, height);
        }

        private static GameObject AddButton(Transform parent, float x, float y, float w, string text, UnityEngine.Events.UnityAction onClick) {
            GameObject go = GUIManager.Instance.CreateButton(text, parent,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), w, 40f);
            RectTransform rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go;
        }

        private static GameObject AddToggleRow(Transform parent, float colWidth, float labelW, string label, bool value, Action<bool> onChange, bool toggleOnLeft = false) {
            const float ToggleSize = 26f;
            const float ToggleGap = 8f;
            GameObject row = NewRow(parent, colWidth, RowHeight);
            float toggleX = toggleOnLeft ? 0f : labelW + 6f;
            float labelX = toggleOnLeft ? ToggleSize + ToggleGap : 0f;
            AddText(row.transform, labelX, 0f, labelW, RowHeight, label, 15, TextAnchor.MiddleLeft);
            GameObject tgo = GUIManager.Instance.CreateToggle(row.transform, ToggleSize, ToggleSize);
            tgo.transform.SetParent(row.transform, false);
            RectTransform rt = (RectTransform)tgo.transform;
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(toggleX, -3f);
            Toggle t = tgo.GetComponent<Toggle>();
            t.isOn = value;
            t.onValueChanged.AddListener(b => onChange(b));
            return row;
        }

        // Row based dual config, for scalars
        private static GameObject AddScalingFeatureRow(Transform parent, float colWidth, float imgW, float imgH, Sprite sprite, string mainLabel, string description, bool mainValue, Action<bool> onMain, string subLabel, bool subValue, Action<bool> onSub) {
            const float SubIndent = 24f;
            const float MainToggleSize = 26f;
            const float SubToggleSize = 22f;
            const float ToggleGap = 8f;   // gap between a toggle and the title to its right
            const float DescH = 72f;      // up to ~4 wrapped lines; the row is tall so the description has room
            float leftColW = colWidth - imgW;   // configuration area to the left of the example image
            GameObject row = NewRow(parent, colWidth, imgH);

            // Example image on the right
            GameObject go = NewUI("Image", row.transform, typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(imgW, imgH);
            rt.anchoredPosition = new Vector2(colWidth - imgW, 0f);
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;   // 256x171 letterboxes inside the box
            img.raycastTarget = false;

            // Vertically center the (main toggle + description + sub toggle) block in the image box
            float blockH = RowHeight + 2f + DescH + 4f + SubRowHeight;   // 34 + 2 + 72 + 4 + 26 = 138
            float topY = Mathf.Max(0f, (imgH - blockH) * 0.5f);

            // Main toggle, directly to the left of its title
            GameObject mtgo = GUIManager.Instance.CreateToggle(row.transform, MainToggleSize, MainToggleSize);
            mtgo.transform.SetParent(row.transform, false);
            RectTransform mrt = (RectTransform)mtgo.transform;
            mrt.localScale = Vector3.one;
            mrt.anchorMin = new Vector2(0f, 1f); mrt.anchorMax = new Vector2(0f, 1f); mrt.pivot = new Vector2(0f, 1f);
            mrt.anchoredPosition = new Vector2(0f, -(topY + 3f));
            Toggle mt = mtgo.GetComponent<Toggle>();
            mt.isOn = mainValue;
            mt.onValueChanged.AddListener(b => onMain(b));

            float mainLabelX = MainToggleSize + ToggleGap;
            AddText(row.transform, mainLabelX, topY, leftColW - mainLabelX - 12f, RowHeight, mainLabel, 18, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimOrange);

            // Description directly under the main label (full left column up to the image)
            float descY = topY + RowHeight + 2f;
            AddText(row.transform, 0f, descY, leftColW - 12f, DescH, description, 14, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);

            // Sub toggle (smaller, indented, below the description), directly to the left of its title
            float subY = descY + DescH + 4f;
            GameObject stgo = GUIManager.Instance.CreateToggle(row.transform, SubToggleSize, SubToggleSize);
            stgo.transform.SetParent(row.transform, false);
            RectTransform srt = (RectTransform)stgo.transform;
            srt.localScale = Vector3.one;
            srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(0f, 1f); srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(SubIndent, -(subY + 2f));
            Toggle st = stgo.GetComponent<Toggle>();
            st.isOn = subValue;
            st.onValueChanged.AddListener(b => onSub(b));

            float subLabelX = SubIndent + SubToggleSize + ToggleGap;
            AddText(row.transform, subLabelX, subY, leftColW - subLabelX - 12f, SubRowHeight, subLabel, 14, TextAnchor.MiddleLeft);

            return row;
        }

        private static GameObject AddSliderRow(Transform parent, float colWidth, float labelW, float sliderW, float valueW, string label, float min, float max, float value, bool wholeNumbers, Action<float> onChange) {
            GameObject row = NewRow(parent, colWidth, RowHeight);
            AddText(row.transform, 0f, 0f, labelW, RowHeight, label, 15, TextAnchor.MiddleLeft);
            Slider s = BuildSlider(row.transform, labelW, 7f, sliderW, min, max, value, wholeNumbers);

            // Editable value box, kept in sync with the slider both ways.
            float boxX = labelW + sliderW + 10f;
            GameObject ifgo = GUIManager.Instance.CreateInputField(
                parent: row.transform,
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                position: new Vector2(boxX, -3f),
                contentType: wholeNumbers ? InputField.ContentType.IntegerNumber : InputField.ContentType.DecimalNumber,
                placeholderText: null,
                fontSize: 15,
                width: valueW,
                height: 28f);
            RectTransform ifrt = (RectTransform)ifgo.transform;
            ifrt.pivot = new Vector2(0f, 1f);
            ifrt.anchoredPosition = new Vector2(boxX, -3f);
            InputField vt = ifgo.GetComponent<InputField>();
            vt.SetTextWithoutNotify(Fmt(value, wholeNumbers));

            s.onValueChanged.AddListener(v => {
                if (wholeNumbers) { v = Mathf.Round(v); }
                vt.SetTextWithoutNotify(Fmt(v, wholeNumbers));   // reflect the slider without re-triggering edits
                onChange(v);
            });
            // Commit typed values on enter/focus-loss: clamp, normalize the box, then drive the slider.
            vt.onEndEdit.AddListener(str => {
                if (!float.TryParse(str, out float v)) { v = s.value; }
                v = Mathf.Clamp(v, min, max);
                if (wholeNumbers) { v = Mathf.Round(v); }
                vt.SetTextWithoutNotify(Fmt(v, wholeNumbers));
                if (s.value != v) { s.value = v; }   // slider's listener runs onChange; otherwise invoke it directly
                else { onChange(v); }
            });
            return row;
        }

        private static GameObject AddEnumCycleRow(Transform parent, float colWidth, float labelW, float ctrlW, string label, string[] options, int currentIndex, Action<int> onChange) {
            GameObject row = NewRow(parent, colWidth, RowHeight);
            AddText(row.transform, 0f, 0f, labelW, RowHeight, label, 15, TextAnchor.MiddleLeft);
            int idx = Mathf.Clamp(currentIndex, 0, options.Length - 1);
            GameObject bgo = GUIManager.Instance.CreateButton(options[idx], row.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(labelW + 6f, -2f), ctrlW, 28f);
            RectTransform rt = (RectTransform)bgo.transform;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(labelW + 6f, -2f);
            Text bt = bgo.GetComponentInChildren<Text>();
            bgo.GetComponent<Button>().onClick.AddListener(() => {
                idx = (idx + 1) % options.Length;
                bt.text = options[idx];
                onChange(idx);
            });
            return row;
        }

        private static string Fmt(float v, bool whole) => whole ? ((int)Mathf.Round(v)).ToString() : v.ToString("0.00");

        // Builds a standard Unity slider hierarchy and applies Valheim styling via Jotunn.
        private static Slider BuildSlider(Transform parent, float x, float y, float width, float min, float max, float value, bool wholeNumbers) {
            GameObject go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            go.layer = GUIManager.UILayer;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, 20f);

            GameObject bg = NewUI("Background", go.transform, typeof(Image));
            RectTransform bgrt = (RectTransform)bg.transform;
            bgrt.anchorMin = new Vector2(0f, 0.25f); bgrt.anchorMax = new Vector2(1f, 0.75f);
            bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            GameObject fillArea = NewUI("Fill Area", go.transform);
            RectTransform filla = (RectTransform)fillArea.transform;
            filla.anchorMin = new Vector2(0f, 0.25f); filla.anchorMax = new Vector2(1f, 0.75f);
            filla.offsetMin = new Vector2(5f, 0f); filla.offsetMax = new Vector2(-15f, 0f);

            GameObject fill = NewUI("Fill", fillArea.transform, typeof(Image));
            RectTransform fillrt = (RectTransform)fill.transform;
            fillrt.anchorMin = new Vector2(0f, 0f); fillrt.anchorMax = new Vector2(1f, 1f);
            fillrt.offsetMin = Vector2.zero; fillrt.offsetMax = Vector2.zero;
            fillrt.sizeDelta = new Vector2(10f, 0f);

            GameObject handleArea = NewUI("Handle Slide Area", go.transform);
            RectTransform hart = (RectTransform)handleArea.transform;
            hart.anchorMin = new Vector2(0f, 0f); hart.anchorMax = new Vector2(1f, 1f);
            hart.offsetMin = new Vector2(10f, 0f); hart.offsetMax = new Vector2(-10f, 0f);

            GameObject handle = NewUI("Handle", handleArea.transform, typeof(Image));
            ((RectTransform)handle.transform).sizeDelta = new Vector2(20f, 0f);
            Image handleImg = handle.GetComponent<Image>();
            handleImg.sprite = GUIManager.Instance.GetSprite("checkbox_marker");
            handleImg.type = Image.Type.Sliced;
            handleImg.color = Color.white;

            Slider slider = go.GetComponent<Slider>();
            slider.fillRect = fillrt;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = value;
            GUIManager.Instance.ApplySliderStyle(slider);
            return slider;
        }

        private static GameObject NewUI(string name, Transform parent, params Type[] components) {
            List<Type> comps = new List<Type> { typeof(RectTransform) };
            if (components != null) { comps.AddRange(components); }
            GameObject go = new GameObject(name, comps.ToArray());
            go.layer = GUIManager.UILayer;
            go.transform.SetParent(parent, false);
            return go;
        }

        private class StagedConfig {
            public bool enableDistance, enableDistanceOverlay;
            public bool enableZone, enableZoneOverlay;
            public bool enableConditional;

            public float creatureHpPerLevel, creatureDmgPerLevel, bossHpPerLevel, bossDmgPerLevel;
            public int maxLevel, maxBossLevel;

            public bool mpHealth, mpDamage;
            public float mpHealthMod, mpDamageMod;
            public int mpRequiredPlayers;

            public LevelGenerator generator;

            public int maxMajor, maxMinor, maxBossMods, prefixLimit;
            public float chanceMajor, chanceMinor, chanceBoss;
            public bool limitToStarLevel, enableBossMods, minorFirst;
            public ModifierDisplayStyle displayStyle;

            // Modifier enable/disable: the live active collection (read-only here) and the set of names
            // currently toggled on per category. Toggling off then removes the entry when saved.
            public CreatureModifierCollection modifierSource;
            public Dictionary<ModifierType, HashSet<string>> modifierOn;

            // Raids (BepInEx ConfigEntries). enableSlsRaids is the inverse of UseVanillaRaidConfiguration.
            public bool enableSlsRaids;
            public float raidEventRate;
            public int raidCheckMinutes, maxRaidAttempts, maxActiveRaids;

            // Per-raid enable/disable. raidSource is the live raid config (read-only here); raidsOn holds the
            // names of the raids currently toggled on. Toggling off then marks Enabled = false when saved.
            public RaidConfiguration raidSource;
            public HashSet<string> raidsOn;

            // Nemesis system. enableNemesis is a ConfigEntry; the rest live in the NemesisSettings YAML
            // (NemesisSystemData.SLE_Nemesis_Settings). nemesisSource is kept for apply + change detection.
            public bool enableNemesis;
            public float nemCooldown, nemInfluence, nemMinSpawn;
            public float neutralScore, minScore, maxScore, decayPerUpdate, scoreInterval, bossKillBonus, deathReduction;
            public NemesisConfiguration nemesisSource;

            public static StagedConfig Snapshot() {
                StagedConfig s = new StagedConfig {
                    enableDistance = ValConfig.EnableDistanceLevelScalingBonus.Value,
                    enableDistanceOverlay = ValConfig.EnableMapRingsForDistanceBonus.Value,
                    enableZone = ValConfig.EnableZoneScalingBonus.Value,
                    enableZoneOverlay = ValConfig.EnableZoneMapOverlay.Value,

                    creatureHpPerLevel = ValConfig.EnemyHealthMultiplier.Value,
                    creatureDmgPerLevel = ValConfig.EnemyDamageLevelMultiplier.Value,
                    bossHpPerLevel = ValConfig.BossEnemyHealthMultiplier.Value,
                    bossDmgPerLevel = ValConfig.BossEnemyDamageMultiplier.Value,
                    maxLevel = ValConfig.MaxLevel.Value,
                    maxBossLevel = ValConfig.MaxBossLevel.Value,

                    mpHealth = ValConfig.EnableMultiplayerEnemyHealthScaling.Value,
                    mpHealthMod = ValConfig.MultiplayerEnemyHealthModifier.Value,
                    mpDamage = ValConfig.EnableMultiplayerEnemyDamageScaling.Value,
                    mpDamageMod = ValConfig.MultiplayerEnemyDamageModifier.Value,
                    mpRequiredPlayers = ValConfig.MultiplayerScalingRequiredPlayersNearby.Value,

                    maxMajor = ValConfig.MaxMajorModifiersPerCreature.Value,
                    maxMinor = ValConfig.MaxMinorModifiersPerCreature.Value,
                    chanceMajor = ValConfig.ChanceMajorModifier.Value,
                    chanceMinor = ValConfig.ChanceMinorModifier.Value,
                    limitToStarLevel = ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value,
                    enableBossMods = ValConfig.EnableBossModifiers.Value,
                    chanceBoss = ValConfig.ChanceOfBossModifier.Value,
                    maxBossMods = ValConfig.MaxBossModifiersPerBoss.Value,
                    prefixLimit = ValConfig.LimitCreatureModifierPrefixes.Value,
                    minorFirst = ValConfig.MinorModifiersFirstInName.Value,

                    enableSlsRaids = !ValConfig.UseVanillaRaidConfiguration.Value,
                    raidEventRate = ValConfig.RaidEventRate.Value,
                    raidCheckMinutes = ValConfig.ServerTimeBetweenRaidStartChecks.Value,
                    maxRaidAttempts = ValConfig.MaxRaidAttemptsPerPlayer.Value,
                    maxActiveRaids = ValConfig.MaxActiveRaids.Value,

                    enableNemesis = ValConfig.EnableNemesisSystem.Value,
                };

                CreatureLevelSettings settings = LevelSystemData.SLE_Level_Settings;
                s.enableConditional = settings != null && settings.EnableConditionalCreatureLevelupChance;
                s.generator = CloneOrDefaultGenerator(settings, s.maxLevel);

                if (!Enum.TryParse(ValConfig.ModifierIconDisplayStyle.Value, out ModifierDisplayStyle ds)) {
                    ds = ModifierDisplayStyle.Stars;
                }
                s.displayStyle = ds;

                s.modifierSource = CreatureModifiersData.ActiveCreatureModifiers;
                s.modifierOn = new Dictionary<ModifierType, HashSet<string>>() {
                    { ModifierType.Boss, KeysOf(s.modifierSource?.BossModifiers) },
                    { ModifierType.Major, KeysOf(s.modifierSource?.MajorModifiers) },
                    { ModifierType.Minor, KeysOf(s.modifierSource?.MinorModifiers) },
                };

                NemesisConfiguration ncfg = NemesisSystemData.SLE_Nemesis_Settings;
                s.nemesisSource = ncfg;
                if (ncfg != null) {
                    s.nemCooldown = ncfg.NemesisActionCooldownSeconds;
                    s.nemInfluence = ncfg.NemesisInfluenceRadius;
                    s.nemMinSpawn = ncfg.NemesisMinSpawnDistance;
                    NemesisScore score = ncfg.ScoreSystem ?? new NemesisScore();
                    s.neutralScore = score.NeutralScore;
                    s.minScore = score.MinScore;
                    s.maxScore = score.MaxScore;
                    s.decayPerUpdate = score.DecayPerUpdate;
                    s.scoreInterval = score.ScoreIntervalSeconds;
                    s.bossKillBonus = score.BossKillBonus;
                    s.deathReduction = score.DeathScoreReduction;
                }

                s.raidSource = RaidsData.SLE_Raid_Settings;
                s.raidsOn = new HashSet<string>();
                if (s.raidSource?.Raids != null) {
                    foreach (RaidDefinition raid in s.raidSource.Raids) {
                        if (raid.Enabled) { s.raidsOn.Add(raid.Name); }
                    }
                }
                return s;
            }

            // Names of the entries that are currently enabled (entries default to enabled).
            private static HashSet<string> KeysOf(Dictionary<string, CreatureModifierConfiguration> dict) {
                HashSet<string> set = new HashSet<string>();
                if (dict == null) { return set; }
                foreach (KeyValuePair<string, CreatureModifierConfiguration> kv in dict) {
                    if (kv.Value.Enabled) { set.Add(kv.Key); }
                }
                return set;
            }

            // Prepopulate the configurable level generator from the existing default generator if one is set,
            // otherwise a sensible exponential default that approximates the built-in level-up curve.
            private static LevelGenerator CloneOrDefaultGenerator(CreatureLevelSettings settings, int maxLevel) {
                LevelGenerator src = null;
                if (settings?.DefaultLevelupGenerators != null && settings.DefaultLevelupGenerators.Count > 0) {
                    src = settings.DefaultLevelupGenerators[0];
                }
                if (src == null) {
                    return new LevelGenerator {
                        MinLevel = 1,
                        MaxLevel = Mathf.Max(1, maxLevel),
                        LevelUpChance = 0.2f,
                        LevelupCalculationStyle = LevelupCalculationStyle.Exponential,
                        GaussianOffset = 0f,
                        NightMultiplier = 1f,
                    };
                }
                return new LevelGenerator {
                    PrefabName = src.PrefabName,
                    MinLevel = src.MinLevel,
                    MaxLevel = src.MaxLevel,
                    LevelUpChance = src.LevelUpChance,
                    LevelupCalculationStyle = src.LevelupCalculationStyle,
                    GaussianOffset = src.GaussianOffset,
                    NightMultiplier = src.NightMultiplier,
                };
            }
        }
    }
}
