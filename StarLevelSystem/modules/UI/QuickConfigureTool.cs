using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    // A paged editor over the settings most worlds want to change. Opened from the shared Mod Config launcher, or once
    // on the main menu as the first-time setup, which adds a welcome page in front. Every page can be saved, and the X
    // closes from anywhere. This file is the shell (opening, navigation, saving, staged state); the pages themselves are
    // built in QuickConfigurePages.cs.
    internal static partial class QuickConfigureTool {

        // --- layout constants ---
        private const float PanelW = 900f;
        private const float PanelH = 690f;
        private const float Margin = 26f;
        private const float ContentTop = 64f;
        private const float PageW = PanelW - 2 * Margin;
        private const float PageH = PanelH - ContentTop - 84f;   // leaves the status line and the nav bar below
        private const float RowHeight = ConfigUI.RowHeight;
        private const float SubRowHeight = ConfigUI.SubRowHeight;
        private const float RowGap = ConfigUI.RowGap;
        private const float NavButtonW = 170f;

        private const string LauncherEntry = "Star Level System";

        private struct PageDef {
            internal string Title;
            internal Action<Transform> Build;
            internal Action OnShow;
        }

        // --- runtime state ---
        private static GameObject overlay;
        private static GameObject panel;
        private static GameObject confirmOverlay;
        private static List<PageDef> pages;
        private static GameObject[] pageRoots;
        private static int currentPage;
        private static bool tutorialMode;
        private static Text titleText;
        private static Text statusText;
        private static GameObject backBtn;
        private static GameObject nextBtn;
        private static GameObject finishBtn;
        private static Text nextCaption;

        // What the pages edit, and what was live when the panel opened (or was last saved). Unsaved changes are the
        // difference between the two, so an edit that is put back is not an edit.
        private static StagedConfig staged;
        private static StagedConfig baseline;

        // First-time setup: shown at most once a session. The FejdStartup it was queued on is kept so a new visit to the
        // main menu can queue it again if the last one ended before the menu was ever ready.
        private static bool tutorialShownThisSession;
        private static FejdStartup tutorialQueuedOn;

        internal static void Init() {
            DistanceExample = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>("distance_rings");
            ZoneExample = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>("region_zones");

            // The corner button, the main-menu hook and the pause-menu patch all used to live here. They
            // now belong to the shared launcher in common/ConfigUI, so several mods share one button
            // instead of stacking one each in the same corner. See its README for the cross-assembly
            // contract.
            ConfigUILauncher.Init();
            ApplyRegistration();
            ConfigNetwork.EditResult += OnRemoteEditResult;
        }

        // SettingChanged handler for the client toggle.
        public static void OnShowButtonChanged(object s, EventArgs e) {
            ApplyRegistration();
        }

        private static void ApplyRegistration() {
            if (ValConfig.ShowQuickConfigureButton.Value == false) {
                ConfigUILauncher.Unregister(LauncherEntry);
                return;
            }

            // Off-host, this tool can only half work: SaveStaged writes ~25 BepInEx ConfigEntry values,
            // and Jotunn only pushes a remote admin's changed entries from SynchronizeChangedConfig, which
            // is internal and fires when the ConfigurationManager window closes -- not from here. If that
            // method cannot be reached, do not offer the button off-host at all. Better to be missing than
            // to look like it worked.
            if (IsOwner() == false && CanPushRemoteConfig() == false) {
                ConfigUILauncher.Unregister(LauncherEntry);
                return;
            }

            ConfigUILauncher.Register(LauncherEntry, OpenPanel);
        }

        private static bool IsOwner() {
            return ZNet.instance == null || ZNet.instance.IsServer();
        }

        private static MethodInfo syncChangedConfig;
        private static bool syncChangedConfigResolved;

        private static bool CanPushRemoteConfig() {
            if (syncChangedConfigResolved == false) {
                syncChangedConfigResolved = true;
                syncChangedConfig = AccessTools.Method(typeof(SynchronizationManager), "SynchronizeChangedConfig");
                if (syncChangedConfig == null) {
                    Logger.LogWarning("Jotunn's SynchronizeChangedConfig could not be found, so a remote admin " +
                        "cannot push config changes. The quick configure button will not be offered off-host.");
                }
            }
            return syncChangedConfig != null;
        }

        // Reflection into a private Jotunn method, knowingly: it is the only way a remote admin's
        // ConfigEntry edits reach the server without opening the ConfigurationManager window. Guarded, and
        // the registration above declines to offer the button at all when it is missing.
        private static void PushRemoteConfigChanges() {
            if (IsOwner() || CanPushRemoteConfig() == false) { return; }
            try {
                syncChangedConfig.Invoke(SynchronizationManager.Instance, null);
            } catch (Exception e) {
                Logger.LogWarning($"Could not push config changes to the server: {e.Message}");
            }
        }

        // ------------------------------------------------------------------------------------------------
        //  First-time setup
        // ------------------------------------------------------------------------------------------------

        // Called from the FejdStartup.Start postfix. Start runs before the intro cinematic, so this only queues: the
        // coroutine lives on the FejdStartup, and dies with it if the player leaves the start scene first.
        internal static void QueueTutorial(FejdStartup startup) {
            if (startup == null || GUIManager.IsHeadless()) { return; }
            if (ValConfig.SetupTutorialComplete.Value || tutorialShownThisSession || tutorialQueuedOn == startup) { return; }
            tutorialQueuedOn = startup;
            startup.StartCoroutine(OpenTutorialWhenMenuReady(startup));
        }

        private static IEnumerator OpenTutorialWhenMenuReady(FejdStartup startup) {
            while (MainMenuReady(startup) == false) { yield return null; }
            // The menu fades in once the cinematic ends; let it land before covering it.
            yield return new WaitForSeconds(1f);
            while (MainMenuReady(startup) == false) { yield return null; }
            if (ValConfig.SetupTutorialComplete.Value || tutorialShownThisSession || panel != null) { yield break; }
            OpenPanel(tutorial: true);
        }

        // PlayIntroCinematic keeps m_mainMenu hidden until the video stops, whether it ends, is skipped, or never plays.
        // The menu list is inactive under the character and world pickers, which are not a moment to interrupt either.
        private static bool MainMenuReady(FejdStartup startup) {
            return startup != null
                && CinematicsManager.IsStartedPlaying() == false
                && startup.m_mainMenu != null && startup.m_mainMenu.activeInHierarchy
                && startup.m_menuList != null && startup.m_menuList.activeInHierarchy
                && UnifiedPopup.IsVisible() == false
                && GUIManager.CustomGUIFront != null;
        }

        // ------------------------------------------------------------------------------------------------
        //  Panel
        // ------------------------------------------------------------------------------------------------

        // The launcher's entry point.
        internal static void OpenPanel() {
            OpenPanel(tutorial: false);
        }

        internal static void OpenPanel(bool tutorial) {
            if (GUIManager.IsHeadless() || GUIManager.Instance == null || GUIManager.CustomGUIFront == null) { return; }
            // Built fresh every time, so every widget starts from the current configuration.
            DestroyPanel();
            tutorialMode = tutorial;
            if (tutorial) { tutorialShownThisSession = true; }
            staged = StagedConfig.Snapshot();
            baseline = StagedConfig.Snapshot();
            try {
                BuildPanel();
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool failed to build panel: {e}");
                DestroyPanel();
                return;
            }
            ShowPage(0);
        }

        private static void BuildPanel() {
            // A full-screen dimmer under the panel, so clicks cannot fall through to the menu behind it. It also owns the
            // input block and, on the main menu, hides the menu itself (see MainMenuGuard); both are released when it is
            // destroyed, whatever route that takes.
            overlay = ConfigUI.NewUI("SLSQuickConfigure", GUIManager.CustomGUIFront.transform, typeof(Image));
            StretchToParent(overlay);
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            overlay.AddComponent<ConfigUI.ConfigUIInputGuard>().Hold();
            overlay.AddComponent<MainMenuGuard>();
            overlay.AddComponent<EscapeCloser>();

            panel = GUIManager.Instance.CreateWoodpanel(
                parent: overlay.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(0f, 0f),
                width: PanelW,
                height: PanelH,
                draggable: true);

            titleText = ConfigUI.AddText(
                parent: panel.transform,
                x: Margin + 40f,
                y: 18f,
                w: PageW - 80f,
                h: RowHeight,
                text: "",
                fontSize: 22,
                anchor: TextAnchor.MiddleCenter,
                color: GUIManager.Instance.ValheimYellow);
            WithTip(ConfigUI.AddCloseX(panel.transform, PanelW, RequestClose),
                Tip("Close", "Closes the panel. If anything has not been saved yet you are asked about it first."));

            pages = BuildPageList(tutorialMode);
            pageRoots = new GameObject[pages.Count];
            for (int i = 0; i < pages.Count; i++) {
                pageRoots[i] = ConfigUI.NewRect("Page" + i, panel.transform, Margin, ContentTop, PageW, PageH);
                pages[i].Build(pageRoots[i].transform);
            }

            statusText = ConfigUI.AddText(panel.transform, Margin, PanelH - 80f, PageW, 22f, "", 13, TextAnchor.MiddleCenter);

            float navY = PanelH - 56f;
            backBtn = WithTip(ConfigUI.AddButton(panel.transform, Margin, navY, 130f, "< Back", () => ShowPage(currentPage - 1)),
                Tip("Back", "The previous page. Moving between pages keeps your edits; only Save writes them."));
            WithTip(ConfigUI.AddButton(panel.transform, (PanelW - NavButtonW) * 0.5f, navY, NavButtonW, "$sls_cfg_button_save", OnSaveClicked),
                Tip("Save", "Writes every page's changes: the BepInEx settings and the YAML files behind them. The panel stays open."));
            nextBtn = WithTip(ConfigUI.AddButton(panel.transform, PanelW - Margin - NavButtonW, navY, NavButtonW, "Next >", () => ShowPage(currentPage + 1)),
                Tip("Next", "The next page. Moving between pages keeps your edits; only Save writes them."));
            nextCaption = nextBtn.GetComponentInChildren<Text>();
            finishBtn = WithTip(ConfigUI.AddButton(panel.transform, PanelW - Margin - NavButtonW, navY, NavButtonW, "$sls_cfg_button_finish", OnFinishClicked),
                Tip("Save & Finish", "Saves everything and closes the panel."));
        }

        private static List<PageDef> BuildPageList(bool tutorial) {
            List<PageDef> list = new List<PageDef>();
            if (tutorial) {
                list.Add(new PageDef { Title = "Welcome", Build = BuildWelcomePage });
            }
            list.Add(new PageDef { Title = "Level Progression", Build = BuildScalingPage });
            list.Add(new PageDef { Title = "Level Distribution", Build = BuildDistributionPage, OnShow = RefreshDistribution });
            // After the level pages: the estimate is worked at the Max stars they set, so it re-reads it on show.
            list.Add(new PageDef { Title = "Loot", Build = BuildLootPage, OnShow = RefreshLootEstimates });
            list.Add(new PageDef { Title = "Health & Damage", Build = BuildStatsPage, OnShow = UpdateExampleMath });
            list.Add(new PageDef { Title = "Modifiers", Build = BuildModifiersPage });
            list.Add(new PageDef { Title = "Raids", Build = BuildRaidsPage });
            list.Add(new PageDef { Title = "Nemesis System", Build = BuildNemesisPage, OnShow = RefreshNemesisDescriptions });
            list.Add(new PageDef { Title = "Location Reset", Build = BuildLocationResetPage, OnShow = RefreshLocationResetViews });
            return list;
        }

        private static void ShowPage(int page) {
            if (pageRoots == null) { return; }
            currentPage = Mathf.Clamp(page, 0, pageRoots.Length - 1);
            for (int i = 0; i < pageRoots.Length; i++) {
                pageRoots[i].SetActive(i == currentPage);
            }
            titleText.text = $"StarLevelSystem - {pages[currentPage].Title}  (Page {currentPage + 1} of {pageRoots.Length})";

            backBtn.SetActive(currentPage > 0);
            bool last = currentPage == pageRoots.Length - 1;
            nextBtn.SetActive(!last);
            finishBtn.SetActive(last);
            nextCaption.text = ConfigUI.L(tutorialMode && currentPage == 0 ? "$sls_cfg_button_get_started" : "Next >");
            SetStatus("", true);
            pages[currentPage].OnShow?.Invoke();
        }

        private static void SetStatus(string message, bool ok) {
            if (statusText == null) { return; }
            statusText.text = message ?? "";
            statusText.color = ok ? GUIManager.Instance.ValheimBeige : GUIManager.Instance.ValheimOrange;
        }

        // The X. Unsaved changes get a chance to be kept.
        private static void RequestClose() {
            if (staged != null && baseline != null && staged.Matches(baseline) == false) {
                ShowDiscardConfirm();
                return;
            }
            ClosePanel();
        }

        // Escape (or the gamepad's back button) closes the topmost thing: the discard prompt, else the panel, which asks
        // first when there are unsaved edits. Called every frame by the overlay and by the Menu.Update prefix in UIPatches,
        // whichever runs first; both then report the key as taken, so the pause menu under the panel does not also act on
        // it. Without that, Escape hid the pause menu (unpausing the game with the panel still up) and after that did
        // nothing at all, because the panel's input block makes the menu ignore the key.
        private static int escapeFrame = -1;
        // Whether a text box had focus at the end of the last frame. See EditingText.
        private static bool textFocusedLastFrame;

        internal static bool TakeEscape() {
            // The frame after one that was taken counts as taken too: a gamepad button reads as pressed until ZInput next
            // updates (from Game.Update), which can fall on either side of the two callers, so the next frame's first
            // caller could see the same press again.
            if (escapeFrame >= 0 && Time.frameCount - escapeFrame <= 1) { return true; }
            if (overlay == null) { return false; }
            bool escape = ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB");
            // The pause menu also hides on the gamepad's menu button. The panel does not act on it, but the menu must not
            // either: once hidden it cannot come back while the panel's input block is up.
            bool menuButton = ZInput.GetButtonDown("JoyMenu");
            if (escape == false && menuButton == false) { return false; }
            escapeFrame = Time.frameCount;
            // Keys that belong to something on top: the console closes itself on Escape, a text box cancels its edit.
            if (escape == false || global::Console.IsVisible() || UnifiedPopup.IsVisible() || EditingText()) { return true; }
            if (confirmOverlay != null) { CloseConfirm(); } else { RequestClose(); }
            return true;
        }

        // Checked against the end of the last frame as well as now: the text box handles Escape itself from the
        // EventSystem's update, which drops its focus the moment it cancels the edit, and that update may already have
        // run this frame by the time the key is checked here.
        private static bool EditingText() {
            return textFocusedLastFrame || TextFieldFocused();
        }

        private static bool TextFieldFocused() {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField field = selected != null ? selected.GetComponent<InputField>() : null;
            return field != null && field.isFocused;
        }

        private class EscapeCloser : MonoBehaviour {
            public void Update() {
                TakeEscape();
            }

            public void LateUpdate() {
                textFocusedLastFrame = TextFieldFocused();
            }
        }

        // Closing by any route finishes the first-time setup: the welcome page promises that the X is enough.
        private static void ClosePanel() {
            if (tutorialMode) {
                ValConfig.SetupTutorialComplete.Value = true;
            }
            DestroyPanel();
        }

        private static void DestroyPanel() {
            CloseConfirm();
            QuickConfigTooltip.Close();
            if (overlay != null) {
                UnityEngine.Object.Destroy(overlay);
            }
            overlay = null;
            panel = null;
            pages = null;
            pageRoots = null;
            tutorialMode = false;
            textFocusedLastFrame = false;
            ClearPageReferences();
        }

        private static void ShowDiscardConfirm() {
            CloseConfirm();
            const float W = 470f;
            const float H = 190f;
            const float ButtonW = 136f;

            confirmOverlay = ConfigUI.NewUI("SLSQuickConfigureConfirm", GUIManager.CustomGUIFront.transform, typeof(Image));
            StretchToParent(confirmOverlay);
            confirmOverlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            GameObject box = GUIManager.Instance.CreateWoodpanel(
                parent: confirmOverlay.transform,
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(0f, 0f), width: W, height: H, draggable: false);

            ConfigUI.AddText(box.transform, 0f, 16f, W, 30f, "$sls_cfg_discard_title", 20, TextAnchor.MiddleCenter, GUIManager.Instance.ValheimYellow);
            ConfigUI.AddText(box.transform, 20f, 54f, W - 40f, 50f, "$sls_cfg_discard_body", 14, TextAnchor.MiddleCenter);

            float y = H - 64f;
            float gap = (W - 3 * ButtonW) / 4f;
            ConfigUI.AddButton(box.transform, gap, y, ButtonW, "$sls_cfg_button_keep_editing", CloseConfirm, 36f);
            ConfigUI.AddButton(box.transform, 2 * gap + ButtonW, y, ButtonW, "$sls_cfg_button_discard", ClosePanel, 36f);
            ConfigUI.AddButton(box.transform, 3 * gap + 2 * ButtonW, y, ButtonW, "$sls_cfg_button_save_close", () => {
                CloseConfirm();
                OnFinishClicked();
            }, 36f);
        }

        private static void CloseConfirm() {
            if (confirmOverlay != null) {
                UnityEngine.Object.Destroy(confirmOverlay);
            }
            confirmOverlay = null;
        }

        // Tooltip helpers, kept short because nearly every row on every page carries one. See QuickConfigTooltip.
        private static GameObject WithTip(GameObject row, string tooltip) => QuickConfigTooltip.Attach(row, tooltip);

        private static string Tip(ConfigEntryBase entry) => QuickConfigTooltip.Of(entry);

        private static string Tip(string key, string description) => QuickConfigTooltip.Text(key, description);

        private static string YamlTip(Type type, string member, string fallback = null) => QuickConfigTooltip.OfYaml(type, member, fallback);

        private static void StretchToParent(GameObject go) {
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // Hides the main menu while the panel is up and restores it when the panel goes. FejdStartup keeps reading the
        // keyboard while its menu is visible: Enter in one of the panel's value boxes would start the game underneath,
        // and the arrow keys and gamepad pull focus back onto the menu buttons. Vanilla's own Settings panel does the
        // same. Tied to OnDestroy, like ConfigUIInputGuard, so no close path can leave the menu hidden.
        private class MainMenuGuard : MonoBehaviour {
            private GameObject hiddenMenu;

            public void Awake() {
                FejdStartup startup = FejdStartup.instance;
                if (startup != null && startup.m_mainMenu != null && startup.m_mainMenu.activeSelf) {
                    hiddenMenu = startup.m_mainMenu;
                    hiddenMenu.SetActive(false);
                }
            }

            public void OnDestroy() {
                if (hiddenMenu != null) {
                    hiddenMenu.SetActive(true);
                }
                hiddenMenu = null;
            }
        }

        // ------------------------------------------------------------------------------------------------
        //  Save
        // ------------------------------------------------------------------------------------------------

        private static void OnSaveClicked() {
            if (SaveStaged(out string message)) {
                SetStatus(string.IsNullOrEmpty(message) ? ConfigUI.L("$sls_cfg_status_saved") : ConfigUI.L("$sls_cfg_status_saved") + " " + message, string.IsNullOrEmpty(message));
            } else {
                SetStatus(message, false);
            }
        }

        private static void OnFinishClicked() {
            if (SaveStaged(out string message)) {
                ClosePanel();
            } else {
                SetStatus(message, false);
            }
        }

        // Applies everything staged. Returns false with the reasons when anything was refused; the panel stays open with
        // the edits intact either way, since a half-applied save closing its window is how an admin ends up believing it
        // landed. On success the message carries any validation warnings.
        private static bool SaveStaged(out string message) {
            message = "";
            if (staged == null || baseline == null) { return false; }

            string invalid = staged.ValidationError(baseline);
            if (invalid != null) {
                message = invalid;
                return false;
            }

            List<string> failures = new List<string>();
            List<string> warnings = new List<string>();
            try {
                // Every YAML document is built and dry-run first (see SaveYaml): a refusal stops the save before anything
                // has been written, instead of after the ConfigEntry writes below, which take effect as they are made.
                pendingYaml = new List<PendingYaml>();
                SaveLevelSettings(failures, warnings);
                SaveModifiers(failures, warnings);
                SaveRaids(failures, warnings);
                SaveLoot(failures, warnings);
                SaveNemesis(failures, warnings);
                SaveLocationReset(failures, warnings);
                if (failures.Count > 0) {
                    message = string.Join(" ", failures);
                    return false;
                }

                ValConfig.EnableDistanceLevelScalingBonus.Value = staged.enableDistance;
                ValConfig.EnableMapRingsForDistanceBonus.Value = staged.enableDistanceOverlay;
                ValConfig.EnableZoneScalingBonus.Value = staged.enableZone;
                ValConfig.EnableZoneMapOverlay.Value = staged.enableZoneOverlay;
                ValConfig.ShowNoMapRingLevel.Value = staged.showNoMapRing;
                ValConfig.ShowNoMapZoneLevel.Value = staged.showNoMapZone;

                ValConfig.EnemyHealthMultiplier.Value = staged.creatureHpPerLevel;
                ValConfig.EnemyDamageLevelMultiplier.Value = staged.creatureDmgPerLevel;
                ValConfig.BossEnemyHealthMultiplier.Value = staged.bossHpPerLevel;
                ValConfig.BossEnemyDamageMultiplier.Value = staged.bossDmgPerLevel;
                ValConfig.MaxLevel.Value = staged.maxStars;
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
                ValConfig.AutoTuneBiomeStarCaps.Value = staged.biomeCapAuto;
                ValConfig.ModifierIconDisplayStyle.Value = staged.displayStyle.ToString();

                // Raids - plain BepInEx ConfigEntries; "Enable SLS Raids" is the inverse of vanilla raids.
                ValConfig.UseVanillaRaidConfiguration.Value = !staged.enableSlsRaids;
                ValConfig.RaidEventRate.Value = staged.raidEventRate;
                ValConfig.ServerTimeBetweenRaidStartChecks.Value = staged.raidCheckMinutes;
                ValConfig.MaxRaidAttemptsPerPlayer.Value = staged.maxRaidAttempts;
                ValConfig.MaxActiveRaids.Value = staged.maxActiveRaids;

                // Loot - the per-level scales are ConfigEntries; the distance rings are in the LootSettings YAML.
                ValConfig.LootDropCalculationType.Value = staged.loot.style.ToString();
                ValConfig.PerLevelLootScale.Value = staged.loot.perLevelScale;
                ValConfig.PerLevelLootChanceScale.Value = staged.loot.perLevelChanceScale;
                ValConfig.ChanceBaseChancePerLevel.Value = staged.loot.chanceBase;
                ValConfig.ScaleAllLootByLevel.Value = staged.loot.scaleAllLoot;
                ValConfig.LootEggsDropIncreaseStacks.Value = staged.loot.eggStacks;
                ValConfig.PerLevelTreeLootScale.Value = staged.loot.treeScale;
                ValConfig.PerLevelMineRockLootScale.Value = staged.loot.rockScale;
                ValConfig.PerLevelDestructibleLootScale.Value = staged.loot.destructibleScale;
                ValConfig.PerLevelBirdLootScale.Value = staged.loot.birdScale;

                // Nemesis - enable flag is a ConfigEntry; the rest is in the NemesisSettings YAML.
                ValConfig.EnableNemesisSystem.Value = staged.enableNemesis;

                // Location reset - the master switch and sweep budget are ConfigEntries; the rest is in the
                // LocationResetSettings YAML.
                ValConfig.EnableLocationReset.Value = staged.locationReset.masterSwitch;
                ValConfig.LocationResetSweepBudgetMs.Value = staged.locationReset.sweepBudgetMs;

                foreach (PendingYaml doc in pendingYaml) {
                    ApplyYaml(doc.File, doc.Yaml, doc.Label, failures, warnings);
                }

                // A remote admin's ConfigEntry writes above are local-only until Jotunn is told to push
                // them. On a host this is a no-op.
                PushRemoteConfigChanges();
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool failed to apply configuration: {e}");
                message = $"Saving failed: {e.Message}";
                return false;
            } finally {
                pendingYaml = null;
            }

            if (failures.Count > 0) {
                message = string.Join(" ", failures);
                return false;
            }

            Logger.LogInfo("QuickConfigureTool applied and saved configuration.");
            // What was just written is now the live configuration, so it is the new point unsaved changes are measured from.
            baseline = StagedConfig.Snapshot();
            message = string.Join(" ", warnings);
            return true;
        }

        // Every YAML write goes through ApplyEdited: validate, apply, write with the documented header intact, broadcast.
        // It refuses and reports rather than half-writing. Off-host only the server may take that path, so a remote
        // admin's copy is sent to it instead; the verdict comes back later through OnRemoteEditResult.
        private static void ApplyYaml(YamlConfigFile file, string yaml, string label, List<string> failures, List<string> warnings) {
            if (IsOwner() == false) {
                if (ConfigNetwork.RequestEdit(file, yaml, out string refusal)) {
                    warnings.Add($"{label} sent to the server.");
                    return;
                }
                Logger.LogWarning($"{label} were not saved: {refusal}");
                failures.Add($"{label} were not saved: {refusal}");
                return;
            }
            if (YamlConfigManager.ApplyEdited(file, yaml, out string result)) {
                if (string.IsNullOrEmpty(result) == false) { warnings.Add(result); }
                return;
            }
            Logger.LogWarning($"{label} were not saved: {result}");
            failures.Add($"{label} were not saved: {result}");
        }

        // The server's answer to a YAML edit sent from here. The server broadcasts an accepted file before it answers,
        // so by now the live settings hold it and a fresh baseline stops it counting as unsaved.
        private static void OnRemoteEditResult(YamlConfigFile file, bool accepted, string message) {
            string name = file?.FileName ?? "Settings";
            if (accepted) {
                Logger.LogInfo($"The server accepted {name}. {message}");
                if (panel != null && staged != null) {
                    baseline = StagedConfig.Snapshot();
                    // The message carries the server's warnings, including a failed disk write.
                    SetStatus(string.IsNullOrEmpty(message) ? $"{name} saved on the server." : $"{name} saved on the server. {message}", string.IsNullOrEmpty(message));
                }
                return;
            }
            Logger.LogWarning($"The server refused {name}: {message}");
            if (panel != null) {
                SetStatus($"The server refused {name}: {message}", false);
            } else if (MessageHud.instance != null) {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, $"The server refused {name}: {message}");
            }
        }

        // Always through a deserialized copy, never the live object: a live settings object can BE the shared static
        // default (each Data class re-points to it whenever a parse fails), so mutating it in place would corrupt the
        // defaults for the rest of the session. Copied and written through the file's own format, so what is saved is
        // exactly what the framework would have written itself.
        private static T CopyForEdit<T>(YamlConfigFile<T> file, T live) where T : class {
            return file.EffectiveFormat.Deserializer.Deserialize<T>(YamlConfigManager.SerializeForEdit(file, live));
        }

        // A document built by a Save* step, checked and waiting for SaveStaged to apply it.
        private struct PendingYaml {
            internal YamlConfigFile File;
            internal string Yaml;
            internal string Label;
        }

        // Only set while SaveStaged runs.
        private static List<PendingYaml> pendingYaml;

        // Checked now, applied later. SaveStaged runs every Save* step before it writes any ConfigEntry, so a document
        // refused here stops the whole save while nothing has changed yet. Off-host the server still has the final say;
        // this dry run catches what it would refuse before the ConfigEntry changes are pushed.
        private static void SaveYaml<T>(YamlConfigFile<T> file, T value, string label, List<string> failures, List<string> warnings) where T : class {
            string yaml = YamlConfigManager.SerializeForEdit(file, value);
            ValidationReport report = file.DryRun(yaml, out string parseError);
            if (parseError != null) {
                failures.Add($"{label} were not saved: {file.FileName} was rejected because {parseError}.");
                return;
            }
            if (report.HasErrors) {
                failures.Add($"{label} were not saved: {string.Join(" ", report.Errors.ToArray())}");
                return;
            }
            pendingYaml.Add(new PendingYaml { File = file, Yaml = yaml, Label = label });
        }

        private static void SaveLevelSettings(List<string> failures, List<string> warnings) {
            CreatureLevelSettings live = LevelSystemData.SLE_Level_Settings;
            if (live == null) { return; }

            bool conditionalChanged = staged.enableConditional != baseline.enableConditional;
            bool curveChanged = staged.CurveDiffers(baseline);
            bool bossCurveChanged = staged.BossCurveDiffers(baseline);
            List<int> changedSpans = staged.ChangedTableSpans(baseline);
            Dictionary<Heightmap.Biome, int> changedCaps = staged.ChangedBiomeCaps(live);
            if (conditionalChanged == false && curveChanged == false && bossCurveChanged == false && changedSpans.Count == 0 && changedCaps.Count == 0) { return; }

            // From the settings as written, not the live copy: the live one has every generator already expanded into the
            // chance tables, and saving it wrote those expansions over the hand-written tables in the file.
            CreatureLevelSettings settings = CopyForEdit(YamlConfigManager.LevelSettings, LevelSystemData.AuthoredLevelSettings ?? live);
            settings.EnableConditionalCreatureLevelupChance = staged.enableConditional;
            if (curveChanged) {
                settings.DefaultLevelupGenerators = new List<LevelGenerator> { CloneGenerator(staged.generator) };
                // The page shows and saves one curve. Referenced generators would be merged into it on load and quietly
                // change every chance it showed.
                settings.DefaultLevelupGeneratorRefs = null;
            }
            if (bossCurveChanged) {
                if (staged.bossCurveOn) {
                    settings.BossLevelupGenerators = new List<LevelGenerator> { CloneGenerator(staged.bossGenerator) };
                    settings.BossLevelupGeneratorRefs = null;
                } else {
                    // With the toggle off the page shows bosses on the creature curve, so a boss table goes with the
                    // generators; leaving it behind would keep bosses on a curve the page says they no longer have.
                    settings.BossLevelupGenerators = null;
                    settings.BossLevelupGeneratorRefs = null;
                    settings.BossCreatureLevelUpChance = null;
                }
            }
            if (changedSpans.Count > 0) {
                if (settings.LevelupChanceTablesBySpan == null) { settings.LevelupChanceTablesBySpan = new Dictionary<int, SortedDictionary<int, float>>(); }
                foreach (int span in changedSpans) {
                    SortedDictionary<int, float> table = new SortedDictionary<int, float>();
                    List<float> values = staged.tables[span];
                    for (int i = 0; i < values.Count; i++) { table[i + 1] = values[i]; }
                    settings.LevelupChanceTablesBySpan[span] = table;
                }
            }
            if (settings.BiomeConfiguration != null) {
                foreach (KeyValuePair<Heightmap.Biome, int> cap in changedCaps) {
                    if (settings.BiomeConfiguration.TryGetValue(cap.Key, out BiomeSpecificSetting biome) && biome != null) {
                        biome.BiomeMaxLevelOverride = cap.Value;
                    }
                }
            }
            SaveYaml(YamlConfigManager.LevelSettings, settings, "Level settings", failures, warnings);
        }

        // Modifier enable/disable is only written when a toggle actually changed, so touching the sliders alone never
        // rewrites the modifier YAML. Disabled modifiers keep their config in the file and are marked Enabled = false.
        private static void SaveModifiers(List<string> failures, List<string> warnings) {
            CreatureModifierCollection live = CreatureModifiersData.ActiveCreatureModifiers;
            if (live == null) { return; }
            bool changed = false;
            foreach (ModifierType type in staged.modifierOn.Keys) {
                if (SetsEqual(staged.modifierOn[type], baseline.modifierOn[type]) == false) { changed = true; }
            }
            if (changed == false) { return; }

            CreatureModifierCollection copy = CopyForEdit(YamlConfigManager.ModifierSettings, live);
            ApplyEnabledFlags(copy.BossModifiers, staged.modifierOn[ModifierType.Boss]);
            ApplyEnabledFlags(copy.MajorModifiers, staged.modifierOn[ModifierType.Major]);
            ApplyEnabledFlags(copy.MinorModifiers, staged.modifierOn[ModifierType.Minor]);
            SaveYaml(YamlConfigManager.ModifierSettings, copy, "Modifier settings", failures, warnings);
        }

        // Writes the staged on/off state back onto each modifier's Enabled flag.
        private static void ApplyEnabledFlags(Dictionary<string, CreatureModifierConfiguration> dict, HashSet<string> enabledNames) {
            if (dict == null) { return; }
            foreach (KeyValuePair<string, CreatureModifierConfiguration> kv in dict) {
                kv.Value.Enabled = enabledNames.Contains(kv.Key);
            }
        }

        // The copy keeps NemesisVersion and every section this panel does not show; any other version resets the file.
        private static void SaveNemesis(List<string> failures, List<string> warnings) {
            NemesisConfiguration live = NemesisSystemData.SLE_Nemesis_Settings;
            if (live == null || staged.NemesisMatches(baseline)) { return; }

            NemesisConfiguration copy = CopyForEdit(YamlConfigManager.NemesisSettings, live);
            copy.NemesisActionCooldownSeconds = staged.nemCooldown;
            copy.NemesisInfluenceRadius = staged.nemInfluence;
            copy.NemesisMinSpawnDistance = staged.nemMinSpawn;
            if (copy.ScoreSystem == null) { copy.ScoreSystem = new NemesisScore(); }
            copy.ScoreSystem.NeutralScore = staged.neutralScore;
            copy.ScoreSystem.MinScore = staged.minScore;
            copy.ScoreSystem.MaxScore = staged.maxScore;
            copy.ScoreSystem.DecayPerUpdate = staged.decayPerUpdate;
            copy.ScoreSystem.ScoreIntervalSeconds = staged.scoreInterval;
            copy.ScoreSystem.BossKillBonus = staged.bossKillBonus;
            copy.ScoreSystem.DeathScoreReduction = staged.deathReduction;
            if (copy.ChanceChanges?.CreatureOps != null) {
                foreach (KeyValuePair<string, NemesisChanceEntry> op in copy.ChanceChanges.CreatureOps) {
                    if (op.Value == null || staged.nemesisActions.TryGetValue(op.Key, out StagedNemesisAction action) == false) { continue; }
                    op.Value.Enabled = action.Enabled;
                    op.Value.Chance = action.Chance;
                    op.Value.ScoreThreshold = action.Threshold;
                    op.Value.LevelBonus = action.LevelBonus;
                }
            }
            SaveYaml(YamlConfigManager.NemesisSettings, copy, "Nemesis settings", failures, warnings);
        }

        // ------------------------------------------------------------------------------------------------
        //  Staged configuration
        // ------------------------------------------------------------------------------------------------

        private static LevelGenerator CloneGenerator(LevelGenerator src) {
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

        private static bool GeneratorsEqual(LevelGenerator a, LevelGenerator b) {
            if (a == null || b == null) { return a == b; }
            return a.MinLevel == b.MinLevel
                && a.MaxLevel == b.MaxLevel
                && a.LevelUpChance == b.LevelUpChance
                && a.LevelupCalculationStyle == b.LevelupCalculationStyle
                && a.GaussianOffset == b.GaussianOffset
                && a.NightMultiplier == b.NightMultiplier;
        }

        // Whether the admin changed a curve, measured against the one the page opened on. A generator the file holds
        // inline is the curve, so any difference counts. Otherwise the page's generator is only a seed whose range
        // follows the star sliders, and that range moving on its own is a cap change: counting it as a curve edit wrote
        // the seed over the hand-written table (or referenced generators) the world actually rolls from, so moving Max
        // stars alone made high-star creatures several times more common.
        private static bool GeneratorEdited(LevelGenerator staged, LevelGenerator opened, bool openedInline) {
            if (openedInline || staged == null || opened == null) { return GeneratorsEqual(staged, opened) == false; }
            LevelGenerator sameRange = CloneGenerator(staged);
            sameRange.MaxLevel = opened.MaxLevel;
            // Max stars pulls Min down with it when it drops below; that is still the slider, not a chosen start.
            if (staged.MinLevel == Mathf.Min(opened.MinLevel, staged.MaxLevel)) { sameRange.MinLevel = opened.MinLevel; }
            return GeneratorsEqual(sameRange, opened) == false;
        }

        private static bool SetsEqual(HashSet<string> a, HashSet<string> b) {
            if (a == null || b == null) { return a == b; }
            return a.SetEquals(b);
        }

        private static bool TableEqual(Dictionary<int, List<float>> a, Dictionary<int, List<float>> b, int span) {
            bool inA = a.TryGetValue(span, out List<float> va);
            bool inB = b.TryGetValue(span, out List<float> vb);
            if (inA != inB) { return false; }
            return inA == false || va.SequenceEqual(vb);
        }

        private class StagedNemesisAction {
            // The configured entry, read-only here: its spawns and gates feed the generated description.
            internal NemesisChanceEntry Source;
            internal bool Enabled;
            internal float Chance;
            internal float Threshold;
            internal int LevelBonus;

            internal bool SameAs(StagedNemesisAction other) {
                return other != null && Enabled == other.Enabled && Chance == other.Chance && Threshold == other.Threshold && LevelBonus == other.LevelBonus;
            }
        }

        private class StagedConfig {
            public bool enableDistance, enableDistanceOverlay;
            public bool enableZone, enableZoneOverlay;
            // Client-side readouts: where the ring/zone level is shown when there is no map to draw it on.
            public bool showNoMapRing, showNoMapZone;
            public bool enableConditional;

            public float creatureHpPerLevel, creatureDmgPerLevel, bossHpPerLevel, bossDmgPerLevel;
            public int maxBossLevel;

            public bool mpHealth, mpDamage;
            public float mpHealthMod, mpDamageMod;
            public int mpRequiredPlayers;

            // Level distribution. The page works in stars; the generator works in levels (stars + 1). maxStars is
            // ValConfig.MaxLevel, and every edit on the page keeps the generator's range at the stars shown.
            public int maxStars;
            public LevelGenerator generator;
            // Whether generator came from an inline DefaultLevelupGenerators entry. When it did not, the world rolls a
            // hand-written table (or generators referenced by name) and generator is only a seed. See GeneratorEdited.
            public bool generatorInline;

            // Bosses roll from the creature curve unless one is configured for them. bossGenerator is kept seeded either
            // way, so turning the toggle on starts from something sensible rather than from nothing.
            public bool bossCurveOn;
            public LevelGenerator bossGenerator;
            // As generatorInline, for BossLevelupGenerators.
            public bool bossGeneratorInline;

            // Table style thresholds keyed by span, values in key order, seeded from LevelupChanceTablesBySpan.
            public Dictionary<int, List<float>> tables;

            // Biome star caps as they were when this snapshot was taken, and the MaxLevel they sat under. While caps are
            // auto-tuned they are always scaled from these, so moving Max back and forth never compounds rounding.
            public Dictionary<Heightmap.Biome, int> biomeCapOriginals;
            public int biomeCapBaseMax;
            // Auto-tuning off means the caps are whatever was typed on the page instead, and Max stars leaves them alone.
            public bool biomeCapAuto;
            public Dictionary<Heightmap.Biome, int> biomeCapManual;

            public int maxMajor, maxMinor, maxBossMods, prefixLimit;
            public float chanceMajor, chanceMinor, chanceBoss;
            public bool limitToStarLevel, enableBossMods, minorFirst;
            public ModifierDisplayStyle displayStyle;

            // Modifier enable/disable: the collection the page lists (read-only here) and the names toggled on per category.
            public CreatureModifierCollection modifierSource;
            public Dictionary<ModifierType, HashSet<string>> modifierOn;

            // Raids (BepInEx ConfigEntries). enableSlsRaids is the inverse of UseVanillaRaidConfiguration.
            public bool enableSlsRaids;
            public float raidEventRate;
            public int raidCheckMinutes, maxRaidAttempts, maxActiveRaids;

            // Per-raid enable/disable: the raids the page lists (read-only here) and the names toggled on. raidSpawns
            // holds the per-creature numbers that page edits, keyed by position in the file.
            public RaidConfiguration raidSource;
            public HashSet<string> raidsOn;
            public Dictionary<string, StagedRaidSpawn> raidSpawns;

            // Raid creature density, 1-6. raidDensityBase is the density the file's numbers were written at, so every
            // spawn count the slider derives is scaled from the snapshot rather than from the last slider position.
            public int raidDensity;
            public int raidDensityBase;

            // Nemesis system. enableNemesis is a ConfigEntry; the rest live in the NemesisSettings YAML.
            public bool enableNemesis;
            public float nemCooldown, nemInfluence, nemMinSpawn;
            public float neutralScore, minScore, maxScore, decayPerUpdate, scoreInterval, bossKillBonus, deathReduction;
            public Dictionary<string, StagedNemesisAction> nemesisActions;

            // Location reset: two ConfigEntries plus the LocationResetSettings YAML. See QuickConfigureLocationReset.cs.
            public StagedLocationReset locationReset;

            // Loot: the per-level loot scales plus the LootSettings YAML distance rings. See QuickConfigureLoot.cs.
            public StagedLoot loot;

            public int MinStars => Mathf.Max(0, Mathf.Min(generator.MinLevel, generator.MaxLevel) - 1);

            // The span of levels the generator covers, which picks its LevelupChanceTablesBySpan entry.
            public int TableSpan => Mathf.Abs(generator.MaxLevel - generator.MinLevel) + 1;

            public static StagedConfig Snapshot() {
                StagedConfig s = new StagedConfig {
                    enableDistance = ValConfig.EnableDistanceLevelScalingBonus.Value,
                    enableDistanceOverlay = ValConfig.EnableMapRingsForDistanceBonus.Value,
                    enableZone = ValConfig.EnableZoneScalingBonus.Value,
                    enableZoneOverlay = ValConfig.EnableZoneMapOverlay.Value,
                    showNoMapRing = ValConfig.ShowNoMapRingLevel.Value,
                    showNoMapZone = ValConfig.ShowNoMapZoneLevel.Value,

                    creatureHpPerLevel = ValConfig.EnemyHealthMultiplier.Value,
                    creatureDmgPerLevel = ValConfig.EnemyDamageLevelMultiplier.Value,
                    bossHpPerLevel = ValConfig.BossEnemyHealthMultiplier.Value,
                    bossDmgPerLevel = ValConfig.BossEnemyDamageMultiplier.Value,
                    maxStars = ValConfig.MaxLevel.Value,
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
                    biomeCapAuto = ValConfig.AutoTuneBiomeStarCaps.Value,

                    enableSlsRaids = !ValConfig.UseVanillaRaidConfiguration.Value,
                    raidEventRate = ValConfig.RaidEventRate.Value,
                    raidCheckMinutes = ValConfig.ServerTimeBetweenRaidStartChecks.Value,
                    maxRaidAttempts = ValConfig.MaxRaidAttemptsPerPlayer.Value,
                    maxActiveRaids = ValConfig.MaxActiveRaids.Value,

                    enableNemesis = ValConfig.EnableNemesisSystem.Value,
                };

                CreatureLevelSettings settings = LevelSystemData.SLE_Level_Settings;
                s.enableConditional = settings != null && settings.EnableConditionalCreatureLevelupChance;
                s.generator = SeedGenerator(settings, s.maxStars);
                s.generatorInline = settings?.DefaultLevelupGenerators?.Any(g => g != null) == true;
                s.bossCurveOn = (settings?.BossLevelupGenerators?.Count ?? 0) > 0 || (settings?.BossLevelupGeneratorRefs?.Count ?? 0) > 0;
                s.bossGenerator = SeedBossGenerator(settings, s.maxBossLevel, s.generator);
                s.bossGeneratorInline = settings?.BossLevelupGenerators?.Any(g => g != null) == true;
                s.tables = new Dictionary<int, List<float>>();
                if (settings?.LevelupChanceTablesBySpan != null) {
                    foreach (KeyValuePair<int, SortedDictionary<int, float>> entry in settings.LevelupChanceTablesBySpan) {
                        if (entry.Value != null) { s.tables[entry.Key] = entry.Value.Values.ToList(); }
                    }
                }
                s.biomeCapOriginals = new Dictionary<Heightmap.Biome, int>();
                s.biomeCapBaseMax = s.maxStars;
                if (settings?.BiomeConfiguration != null) {
                    foreach (KeyValuePair<Heightmap.Biome, BiomeSpecificSetting> biome in settings.BiomeConfiguration) {
                        if (biome.Value != null && biome.Value.BiomeMaxLevelOverride > 0) { s.biomeCapOriginals[biome.Key] = biome.Value.BiomeMaxLevelOverride; }
                    }
                }
                s.biomeCapManual = new Dictionary<Heightmap.Biome, int>(s.biomeCapOriginals);

                if (!Enum.TryParse(ValConfig.ModifierIconDisplayStyle.Value, out ModifierDisplayStyle ds)) {
                    ds = ModifierDisplayStyle.Stars;
                }
                s.displayStyle = ds;

                s.modifierSource = CreatureModifiersData.ActiveCreatureModifiers;
                s.modifierOn = new Dictionary<ModifierType, HashSet<string>>() {
                    { ModifierType.Boss, EnabledNamesOf(s.modifierSource?.BossModifiers) },
                    { ModifierType.Major, EnabledNamesOf(s.modifierSource?.MajorModifiers) },
                    { ModifierType.Minor, EnabledNamesOf(s.modifierSource?.MinorModifiers) },
                };

                s.nemesisActions = new Dictionary<string, StagedNemesisAction>();
                NemesisConfiguration nemesisCFG = NemesisSystemData.SLE_Nemesis_Settings;
                if (nemesisCFG != null) {
                    s.nemCooldown = nemesisCFG.NemesisActionCooldownSeconds;
                    s.nemInfluence = nemesisCFG.NemesisInfluenceRadius;
                    s.nemMinSpawn = nemesisCFG.NemesisMinSpawnDistance;
                    NemesisScore score = nemesisCFG.ScoreSystem ?? new NemesisScore();
                    s.neutralScore = score.NeutralScore;
                    s.minScore = score.MinScore;
                    s.maxScore = score.MaxScore;
                    s.decayPerUpdate = score.DecayPerUpdate;
                    s.scoreInterval = score.ScoreIntervalSeconds;
                    s.bossKillBonus = score.BossKillBonus;
                    s.deathReduction = score.DeathScoreReduction;
                    if (nemesisCFG.ChanceChanges?.CreatureOps != null) {
                        foreach (KeyValuePair<string, NemesisChanceEntry> op in nemesisCFG.ChanceChanges.CreatureOps) {
                            if (op.Value == null) { continue; }
                            s.nemesisActions[op.Key] = new StagedNemesisAction {
                                Source = op.Value,
                                Enabled = op.Value.Enabled,
                                Chance = op.Value.Chance,
                                Threshold = op.Value.ScoreThreshold,
                                LevelBonus = op.Value.LevelBonus,
                            };
                        }
                    }
                }

                s.locationReset = StagedLocationReset.Snapshot();
                s.loot = StagedLoot.Snapshot();

                s.raidSource = RaidsData.SLE_Raid_Settings;
                s.raidsOn = new HashSet<string>();
                if (s.raidSource?.Raids != null) {
                    foreach (RaidDefinition raid in s.raidSource.Raids) {
                        if (raid.Enabled) { s.raidsOn.Add(raid.Name); }
                    }
                }
                s.raidSpawns = SnapshotRaidSpawns(s.raidSource);
                s.raidDensity = ClampRaidDensity(s.raidSource?.GlobalSettings?.RaidCreatureDensity ?? DefaultRaidDensity);
                s.raidDensityBase = s.raidDensity;
                return s;
            }

            // Names of the entries that are currently enabled (entries default to enabled).
            private static HashSet<string> EnabledNamesOf(Dictionary<string, CreatureModifierConfiguration> dict) {
                HashSet<string> set = new HashSet<string>();
                if (dict == null) { return set; }
                foreach (KeyValuePair<string, CreatureModifierConfiguration> kv in dict) {
                    if (kv.Value.Enabled) { set.Add(kv.Key); }
                }
                return set;
            }

            // The generator the distribution page starts from. An existing default generator is used as is. Otherwise one
            // is shaped after the hand-written default table, starting where it starts with its first chance, so the
            // sliders open close to what the world already rolls. Two snapshots of the same config seed identically,
            // which is what lets an untouched page compare equal and never be written.
            private static LevelGenerator SeedGenerator(CreatureLevelSettings settings, int maxStars) {
                LevelGenerator src = settings?.DefaultLevelupGenerators?.FirstOrDefault(g => g != null);
                if (src != null) { return CloneGenerator(src); }

                SortedDictionary<int, float> table = settings?.DefaultCreatureLevelUpChance ?? LevelSystemData.DefaultConfiguration.DefaultCreatureLevelUpChance;
                int firstLevel = 1;
                float chance = 0.2f;
                if (table != null && table.Count > 0) {
                    KeyValuePair<int, float> first = table.First();
                    firstLevel = Mathf.Max(1, first.Key);
                    if (first.Value > 0f) { chance = Mathf.Clamp01(first.Value / 100f); }
                }
                return new LevelGenerator {
                    MinLevel = Mathf.Min(firstLevel, maxStars + 1),
                    MaxLevel = Mathf.Max(1, maxStars) + 1,
                    LevelUpChance = chance,
                    LevelupCalculationStyle = LevelupCalculationStyle.Exponential,
                    GaussianOffset = 0f,
                    NightMultiplier = 1f,
                };
            }

            // The boss curve the page starts from: the one already configured for bosses, or the creature curve reshaped
            // to the boss star range, which is the closest thing to "what bosses do today" this can offer.
            private static LevelGenerator SeedBossGenerator(CreatureLevelSettings settings, int maxBossStars, LevelGenerator creature) {
                LevelGenerator src = settings?.BossLevelupGenerators?.FirstOrDefault(g => g != null);
                if (src != null) { return CloneGenerator(src); }
                LevelGenerator seeded = CloneGenerator(creature);
                seeded.MinLevel = 1;
                seeded.MaxLevel = Mathf.Max(1, maxBossStars) + 1;
                return seeded;
            }

            public int BossMinStars => Mathf.Max(0, Mathf.Min(bossGenerator.MinLevel, bossGenerator.MaxLevel) - 1);

            public int BossTableSpan => Mathf.Abs(bossGenerator.MaxLevel - bossGenerator.MinLevel) + 1;

            // Whether what bosses roll from would change: the toggle moved, or the boss curve itself did.
            public bool BossCurveDiffers(StagedConfig other) {
                if (bossCurveOn != other.bossCurveOn) { return true; }
                if (bossCurveOn == false) { return false; }
                if (GeneratorEdited(bossGenerator, other.bossGenerator, other.bossGeneratorInline)) { return true; }
                return bossGenerator.LevelupCalculationStyle == LevelupCalculationStyle.Table
                    && TableEqual(tables, other.tables, BossTableSpan) == false;
            }

            // A biome's star cap: scaled in proportion to the staged Max stars while auto-tuning is on, the typed value
            // when it is off, and 0 for a biome that has no cap at all.
            public int CapFor(Heightmap.Biome biome) {
                if (biomeCapOriginals.TryGetValue(biome, out int original) == false) { return 0; }
                if (biomeCapAuto == false) { return biomeCapManual.TryGetValue(biome, out int manual) ? manual : original; }
                if (biomeCapBaseMax <= 0 || maxStars == biomeCapBaseMax) { return original; }
                return Mathf.Max(1, Mathf.RoundToInt(original * (float)maxStars / biomeCapBaseMax));
            }

            // Takes the caps auto-tuning is showing as the starting point for typing them by hand, so switching modes
            // never moves a cap on its own.
            public void HoldCapsForManualEditing() {
                foreach (Heightmap.Biome biome in biomeCapOriginals.Keys.ToList()) { biomeCapManual[biome] = CapFor(biome); }
            }

            // Biomes whose staged cap differs from what the live settings hold.
            public Dictionary<Heightmap.Biome, int> ChangedBiomeCaps(CreatureLevelSettings live) {
                Dictionary<Heightmap.Biome, int> changed = new Dictionary<Heightmap.Biome, int>();
                if (live?.BiomeConfiguration == null) { return changed; }
                foreach (Heightmap.Biome biome in biomeCapOriginals.Keys) {
                    if (live.BiomeConfiguration.TryGetValue(biome, out BiomeSpecificSetting setting) == false || setting == null) { continue; }
                    int cap = CapFor(biome);
                    if (setting.BiomeMaxLevelOverride != cap) { changed[biome] = cap; }
                }
                return changed;
            }

            // Whether the curve the page shows is no longer the one the world rolls from: the generator was changed, or it
            // is Table style and the table it reads was.
            public bool CurveDiffers(StagedConfig other) {
                if (GeneratorEdited(generator, other.generator, other.generatorInline)) { return true; }
                return generator.LevelupCalculationStyle == LevelupCalculationStyle.Table && TableEqual(tables, other.tables, TableSpan) == false;
            }

            public List<int> ChangedTableSpans(StagedConfig other) {
                List<int> spans = new List<int>();
                foreach (int span in tables.Keys) {
                    if (TableEqual(tables, other.tables, span) == false) { spans.Add(span); }
                }
                return spans;
            }

            public bool NemesisMatches(StagedConfig o) {
                if (nemCooldown != o.nemCooldown || nemInfluence != o.nemInfluence || nemMinSpawn != o.nemMinSpawn
                    || neutralScore != o.neutralScore || minScore != o.minScore || maxScore != o.maxScore
                    || decayPerUpdate != o.decayPerUpdate || scoreInterval != o.scoreInterval
                    || bossKillBonus != o.bossKillBonus || deathReduction != o.deathReduction) {
                    return false;
                }
                if (nemesisActions.Count != o.nemesisActions.Count) { return false; }
                foreach (KeyValuePair<string, StagedNemesisAction> action in nemesisActions) {
                    if (o.nemesisActions.TryGetValue(action.Key, out StagedNemesisAction other) == false || action.Value.SameAs(other) == false) { return false; }
                }
                return true;
            }

            // Why the staged values cannot be saved, or null. Only checks what would be written.
            public string ValidationError(StagedConfig original) {
                if (NemesisMatches(original) == false && (minScore > neutralScore || neutralScore > maxScore)) {
                    return ConfigUI.L("$sls_cfg_nemesis_score_order");
                }
                return null;
            }

            public bool Matches(StagedConfig o) {
                bool scalars =
                    enableDistance == o.enableDistance && enableDistanceOverlay == o.enableDistanceOverlay
                    && enableZone == o.enableZone && enableZoneOverlay == o.enableZoneOverlay
                    && showNoMapRing == o.showNoMapRing && showNoMapZone == o.showNoMapZone
                    && enableConditional == o.enableConditional
                    && creatureHpPerLevel == o.creatureHpPerLevel && creatureDmgPerLevel == o.creatureDmgPerLevel
                    && bossHpPerLevel == o.bossHpPerLevel && bossDmgPerLevel == o.bossDmgPerLevel
                    && maxStars == o.maxStars && maxBossLevel == o.maxBossLevel
                    && mpHealth == o.mpHealth && mpDamage == o.mpDamage
                    && mpHealthMod == o.mpHealthMod && mpDamageMod == o.mpDamageMod && mpRequiredPlayers == o.mpRequiredPlayers
                    && maxMajor == o.maxMajor && maxMinor == o.maxMinor && maxBossMods == o.maxBossMods && prefixLimit == o.prefixLimit
                    && chanceMajor == o.chanceMajor && chanceMinor == o.chanceMinor && chanceBoss == o.chanceBoss
                    && limitToStarLevel == o.limitToStarLevel && enableBossMods == o.enableBossMods && minorFirst == o.minorFirst
                    && displayStyle == o.displayStyle && biomeCapAuto == o.biomeCapAuto
                    && enableSlsRaids == o.enableSlsRaids && raidEventRate == o.raidEventRate
                    && raidCheckMinutes == o.raidCheckMinutes && maxRaidAttempts == o.maxRaidAttempts && maxActiveRaids == o.maxActiveRaids
                    && enableNemesis == o.enableNemesis;
                if (scalars == false) { return false; }

                if (GeneratorsEqual(generator, o.generator) == false) { return false; }
                if (BossCurveDiffers(o)) { return false; }
                if (tables.Count != o.tables.Count || tables.Keys.Any(span => TableEqual(tables, o.tables, span) == false)) { return false; }
                foreach (Heightmap.Biome biome in biomeCapOriginals.Keys.Union(o.biomeCapOriginals.Keys)) {
                    if (CapFor(biome) != o.CapFor(biome)) { return false; }
                }

                foreach (ModifierType type in modifierOn.Keys) {
                    if (o.modifierOn.TryGetValue(type, out HashSet<string> other) == false || SetsEqual(modifierOn[type], other) == false) { return false; }
                }
                if (raidDensity != o.raidDensity) { return false; }
                if (SetsEqual(raidsOn, o.raidsOn) == false || RaidSpawnsMatch(raidSpawns, o.raidSpawns) == false) { return false; }
                if (locationReset.Matches(o.locationReset) == false) { return false; }
                if (loot.Matches(o.loot) == false) { return false; }
                return NemesisMatches(o);
            }
        }
    }
}
