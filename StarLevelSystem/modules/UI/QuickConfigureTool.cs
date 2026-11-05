using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.modules.UI.QuickConfigPrefabs;

namespace StarLevelSystem.modules.UI {
    // A paged editor over the settings most worlds want to change. Opened from the shared Mod Config launcher, or once
    // on the main menu as the first-time setup, which adds a welcome page in front. Every page can be saved, and the X
    // closes from anywhere. This file is the shell (opening, navigation, saving, staged state). The layout is in the prefabs
    // under StarLevelUnity/Assets/Custom/QuickConfig; the code that joins each page to the staged values is in
    // QuickConfigurePages.cs and the files beside it, and QuickConfigurePrefabs.cs is what they use to do it.
    internal static partial class QuickConfigureTool {

        private const string LauncherEntry = "Star Level System";

        private struct PageDef {
            internal string Title;
            // The page's layout. Build joins what is in it to the staged values, and adds what depends on the data.
            internal string Prefab;
            internal Action<Transform> Build;
            internal Action OnShow;
            // Puts the page's staged values back to the shipped defaults. Null for a page with nothing to reset.
            internal Action Reset;
            // Drops the references Build keeps to the page's widgets, so the page can be built again on its own.
            internal Action ClearRefs;
            // Only among the pages navigation goes through with advanced options on. See VisiblePages.
            internal bool Advanced;
            // Has widgets for values the simple page sets, so it is built again before it is next shown after they change.
            internal bool FollowsSimplePage;
        }

        // --- runtime state ---
        private static GameObject overlay;
        private static GameObject panel;
        private static GameObject confirmOverlay;
        private static List<PageDef> pages;
        private static GameObject[] pageRoots;
        private static Transform pageHost;
        private static int currentPage;
        private static bool tutorialMode;
        private static Text titleText;
        private static Text statusText;
        private static GameObject backBtn;
        private static GameObject resetBtn;
        private static GameObject nextBtn;
        private static GameObject finishBtn;
        private static Text nextCaption;

        // What the pages edit, and what was live when the panel opened (or was last saved). Unsaved changes are the
        // difference between the two, so an edit that is put back is not an edit.
        private static StagedConfig staged;
        private static StagedConfig baseline;

        internal static void Init() {
            DistanceExample = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>("distance_rings");
            ZoneExample = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>("region_zones");
            RegionExample = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>("region_fill");

            // The corner button, the main-menu hook and the pause-menu patch all used to live here. They
            // now belong to the shared launcher in common/ConfigUI, so several mods share one button
            // instead of stacking one each in the same corner. See its README for the cross-assembly
            // contract.
            ConfigUILauncher.Init();
            ApplyRegistration();
            ConfigNetwork.EditResult += OnRemoteEditResult;
            QueueFirstTimeSetup();
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

            // Off-host, this tool can only half work: SaveStaged writes ~55 BepInEx ConfigEntry values,
            // and Jotunn only pushes changed entries from SynchronizeChangedConfig, which is internal and
            // fires when the ConfigurationManager window closes or the .cfg is reloaded -- not from here. If
            // that method cannot be reached, do not offer the button off-host at all. Better to be missing
            // than to look like it worked. A host still gets there without it, late: the file watcher sees
            // the .cfg change on its next poll and the reload makes Jotunn push.
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

        // Reflection into a private Jotunn method, knowingly: it is the only way ConfigEntry edits leave this
        // machine without opening the ConfigurationManager window. A remote admin's go to the server, which
        // forwards them; a host's go straight to every client. Jotunn does not watch SettingChanged, so
        // without this a host's edits waited for the file watcher's next poll to reload the .cfg. Guarded,
        // and the registration above declines to offer the button off-host when it is missing.
        private static void PushConfigChanges() {
            // The main menu has nobody to send to. In single player m_peers is empty and Jotunn sends nothing.
            if (ZNet.instance == null || CanPushRemoteConfig() == false) { return; }
            try {
                syncChangedConfig.Invoke(SynchronizationManager.Instance, null);
            } catch (Exception e) {
                Logger.LogWarning($"Could not push config changes to the server: {e.Message}");
            }
        }

        // ------------------------------------------------------------------------------------------------
        //  First-time setup
        // ------------------------------------------------------------------------------------------------

        // Queued once, from Init, on the startup popup queue every mod carrying common/ConfigUI shares: it waits for the
        // main menu to settle and opens the popups one at a time, so this and another mod's welcome never land on the
        // same frame. Whether this user has seen it is kept per user rather than per profile; see ConfigUIFirstRun.
        private static void QueueFirstTimeSetup() {
            ConfigUIFirstRun.QueueFirstRunPopup(ValConfig.FirstTimeSetupKey, ValConfig.FirstTimeSetupRevision,
                ValConfig.FirstTimeSetup, ConfigUIStartupPopups.OrderWelcome, OpenFirstTimeSetup, () => overlay != null);
        }

        // Declines when the panel is already up: someone reached it through Mod Config while this waited its turn.
        private static bool OpenFirstTimeSetup() {
            if (overlay != null) { return false; }
            OpenPanel(tutorial: true);
            return overlay != null;
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
            overlay = Spawn("SLS_QC_Panel", GUIManager.CustomGUIFront.transform);
            overlay.AddComponent<ConfigUI.ConfigUIInputGuard>().Hold();
            overlay.AddComponent<MainMenuGuard>();
            overlay.AddComponent<EscapeCloser>();
            panel = Find(overlay.transform, "Woodpanel");

            // Found before the pages go in: a page can use the same names, and Find looks through everything under the panel.
            Transform shell = panel.transform;
            titleText = Get<Text>(shell, "Title");
            statusText = Get<Text>(shell, "Status");
            pageHost = Find(shell, "Pages").transform;
            WithTip(BindButton(shell, "Close", RequestClose), Tip("$sls_cfg_tip_close_title", "$sls_cfg_tip_close"));
            backBtn = WithTip(BindButton(shell, "Back", () => ShowPage(VisiblePageFrom(-1))), Tip("$sls_cfg_tip_back_title", "$sls_cfg_tip_back"));
            resetBtn = WithTip(BindButton(shell, "ResetPage", ShowResetConfirm), Tip("$sls_cfg_button_reset_page", "$sls_cfg_tip_reset_page"));
            WithTip(BindButton(shell, "Save", OnSaveClicked), Tip("$sls_cfg_button_save", "$sls_cfg_tip_save"));
            nextBtn = WithTip(BindButton(shell, "Next", () => ShowPage(VisiblePageFrom(1))), Tip("$sls_cfg_tip_next_title", "$sls_cfg_tip_next"));
            nextCaption = nextBtn.GetComponentInChildren<Text>();
            finishBtn = WithTip(BindButton(shell, "Finish", OnFinishClicked), Tip("$sls_cfg_button_finish", "$sls_cfg_tip_finish"));

            pages = BuildPageList(tutorialMode);
            pageRoots = new GameObject[pages.Count];
        }

        // A page is built the first time it is shown, since most visits to the panel stop at the first one. Its widgets are
        // only a view of the staged values, so a page that was never built saves the same as one that was. A prefab that no
        // longer matches the code throws, and that closes the panel the way a failed open does.
        private static bool EnsurePageBuilt(int index) {
            if (pageRoots[index] != null) { return true; }
            try {
                pageRoots[index] = Spawn(pages[index].Prefab, pageHost);
                pages[index].Build?.Invoke(pageRoots[index].transform);
                return true;
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool failed to build the {pages[index].Title} page: {e}");
                DestroyPanel();
                return false;
            }
        }

        // Titles are localized once, here, because they are read by the title bar and by status messages alike.
        private static List<PageDef> BuildPageList(bool tutorial) {
            List<PageDef> list = new List<PageDef>();
            if (tutorial) {
                list.Add(new PageDef { Title = L("$sls_cfg_page_welcome"), Prefab = "SLS_QC_Page_Welcome" });
            }
            list.Add(new PageDef { Title = L("$sls_cfg_page_simple"), Prefab = "SLS_QC_Page_Simple", Build = BuildSimplePage, OnShow = RefreshSimplePage, Reset = ResetSimplePage, ClearRefs = ClearSimpleReferences });
            list.Add(new PageDef { Title = L("$sls_cfg_page_level_distribution"), Prefab = "SLS_QC_Page_Distribution", Build = BuildDistributionPage, OnShow = RefreshDistribution, Reset = ResetDistributionPage, ClearRefs = ClearDistributionReferences, Advanced = true, FollowsSimplePage = true });
            // After the distribution: the chart works everything through the curve set there, so it re-reads it on show.
            list.Add(new PageDef { Title = L("$sls_cfg_page_level_scaling"), Prefab = "SLS_QC_Page_LevelScaling", Build = BuildLevelScalingPage, OnShow = RefreshLevelScaling, Reset = ResetLevelScalingPage, ClearRefs = ClearLevelScalingReferences, Advanced = true, FollowsSimplePage = true });
            // After the level pages: the estimate is worked at the Max stars they set, so it re-reads it on show.
            list.Add(new PageDef { Title = L("$sls_cfg_page_loot"), Prefab = "SLS_QC_Page_Loot", Build = BuildLootPage, OnShow = RefreshLootEstimates, Reset = ResetLootPage, ClearRefs = ClearLootPageReferences, Advanced = true });
            list.Add(new PageDef { Title = L("$sls_cfg_page_health_damage"), Prefab = "SLS_QC_Page_Stats", Build = BuildStatsPage, OnShow = UpdateExampleMath, Reset = ResetStatsPage, ClearRefs = ClearStatsReferences, Advanced = true, FollowsSimplePage = true });
            list.Add(new PageDef { Title = L("$sls_cfg_page_modifiers"), Prefab = "SLS_QC_Page_Modifiers", Build = BuildModifiersPage, Reset = ResetModifiersPage, Advanced = true });
            list.Add(new PageDef { Title = L("$sls_cfg_page_raids"), Prefab = "SLS_QC_Page_Raids", Build = BuildRaidsPage, Reset = ResetRaidsPage, ClearRefs = ClearRaidPageReferences, Advanced = true });
            list.Add(new PageDef { Title = L("$sls_cfg_page_night_spawns"), Prefab = "SLS_QC_Page_NightSpawns", Build = BuildNightSpawnsPage, Reset = ResetNightSpawnsPage, Advanced = true });
            list.Add(new PageDef { Title = L("$sls_cfg_page_nemesis"), Prefab = "SLS_QC_Page_Nemesis", Build = BuildNemesisPage, OnShow = RefreshNemesisDescriptions, Reset = ResetNemesisPage, ClearRefs = ClearNemesisReferences, Advanced = true });
            list.Add(new PageDef { Title = L("$sls_cfg_page_location_reset"), Prefab = "SLS_QC_Page_LocationReset", Build = BuildLocationResetPage, OnShow = RefreshLocationResetViews, Reset = ResetLocationResetPage, ClearRefs = ClearLocationResetReferences, Advanced = true });
            return list;
        }

        // Builds one page again from the staged values, for when they changed under its widgets (Reset page). The page
        // keeps its place in the panel; everything else, including the other pages, is left as it is.
        private static void RebuildPage(int index) {
            if (pageRoots == null || panel == null || index < 0 || index >= pageRoots.Length) { return; }
            // A tooltip can be open over a widget that is about to go.
            QuickConfigTooltip.Close();
            GameObject old = pageRoots[index];
            int sibling = old.transform.GetSiblingIndex();
            // Hidden now, destroyed at the end of the frame: until then it still counts to layout and raycasts.
            old.SetActive(false);
            UnityEngine.Object.Destroy(old);

            pages[index].ClearRefs?.Invoke();
            GameObject root = Spawn(pages[index].Prefab, pageHost);
            root.transform.SetSiblingIndex(sibling);
            pageRoots[index] = root;
            pages[index].Build?.Invoke(root.transform);
            root.SetActive(index == currentPage);
            if (index == currentPage) { pages[index].OnShow?.Invoke(); }
        }

        private static void ShowResetConfirm() {
            if (pages == null || pages[currentPage].Reset == null) { return; }
            ShowConfirm("$sls_cfg_reset_title", "$sls_cfg_reset_body", "$sls_cfg_button_reset_page", ResetCurrentPage);
        }

        private static void ResetCurrentPage() {
            CloseConfirm();
            if (staged == null || pages == null || pages[currentPage].Reset == null) { return; }
            int page = currentPage;
            try {
                pages[page].Reset();
                RebuildPage(page);
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool could not reset the {pages[page].Title} page: {e}");
                SetStatus(L("$sls_cfg_status_reset_failed", pages[page].Title, e.Message), false);
                return;
            }
            SetStatus(L("$sls_cfg_status_reset_done", pages[page].Title), true);
        }

        // --- defaults, for Reset page ---

        private static T DefaultOf<T>(ConfigEntry<T> entry) => (T)entry.DefaultValue;

        // A YAML file's shipped defaults, as a private copy. Several files hand back a shared static instance from
        // Defaults, which must never be edited, and going through the file's own format means what a reset stages is
        // exactly what a freshly generated file would hold.
        private static T ShippedDefaults<T>(YamlConfigFile<T> file) where T : class {
            if (file == null) { return null; }
            try {
                string yaml = file.SerializeDefaults();
                return string.IsNullOrEmpty(yaml) ? null : file.EffectiveFormat.Deserializer.Deserialize<T>(yaml);
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool could not read the defaults for {file.FileName}: {e.Message}");
                return null;
            }
        }

        private static void ShowPage(int page) {
            if (pageRoots == null) { return; }
            currentPage = Mathf.Clamp(page, 0, pageRoots.Length - 1);
            RebuildPagesFollowingSimplePage();
            if (EnsurePageBuilt(currentPage) == false) { return; }
            for (int i = 0; i < pageRoots.Length; i++) {
                if (pageRoots[i] != null) { pageRoots[i].SetActive(i == currentPage); }
            }
            List<int> visible = VisiblePages();
            int position = visible.IndexOf(currentPage);
            titleText.text = L("$sls_cfg_panel_title", pages[currentPage].Title, position + 1, visible.Count);

            backBtn.SetActive(position > 0);
            resetBtn.SetActive(pages[currentPage].Reset != null);
            bool last = position == visible.Count - 1;
            nextBtn.SetActive(!last);
            finishBtn.SetActive(last);
            nextCaption.text = ConfigUI.L(tutorialMode && currentPage == 0 ? "$sls_cfg_button_get_started" : "$sls_cfg_button_next");
            SetStatus("", true);
            pages[currentPage].OnShow?.Invoke();
        }

        // The pages navigation goes through, which is all of them only with advanced options on. What was edited on the rest
        // before they were hidden stays staged, and a save still writes it.
        private static List<int> VisiblePages() {
            List<int> visible = new List<int>();
            for (int i = 0; i < pages.Count; i++) {
                if (pages[i].Advanced == false || ValConfig.ShowAdvancedQuickConfigurePages.Value) { visible.Add(i); }
            }
            return visible;
        }

        private static int VisiblePageFrom(int step) {
            List<int> visible = VisiblePages();
            return visible[Mathf.Clamp(visible.IndexOf(currentPage) + step, 0, visible.Count - 1)];
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
                ConfigUIFirstRun.MarkSeen(ValConfig.FirstTimeSetupKey, ValConfig.FirstTimeSetupRevision);
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
            pageHost = null;
            tutorialMode = false;
            textFocusedLastFrame = false;
            ClearSimpleChoices();
            ClearPageReferences();
        }

        private static void ShowDiscardConfirm() {
            CloseConfirm();
            confirmOverlay = Spawn("SLS_QC_ConfirmDiscard", GUIManager.CustomGUIFront.transform);
            Transform box = confirmOverlay.transform;
            BindButton(box, "KeepEditing", CloseConfirm);
            BindButton(box, "Discard", ClosePanel);
            BindButton(box, "SaveClose", () => {
                CloseConfirm();
                OnFinishClicked();
            });
        }

        // A two-button prompt in the same place as the discard one: back out, or go ahead. Escape backs out.
        private static void ShowConfirm(string title, string body, string confirmLabel, UnityEngine.Events.UnityAction onConfirm) {
            CloseConfirm();
            confirmOverlay = Spawn("SLS_QC_Confirm", GUIManager.CustomGUIFront.transform);
            Transform box = confirmOverlay.transform;
            SetText(box, "Title", title);
            SetText(box, "Body", body);
            BindButton(box, "KeepEditing", CloseConfirm);
            BindButton(box, "Confirm", onConfirm).GetComponentInChildren<Text>().text = ConfigUI.L(confirmLabel);
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

        // A token with its $1, $2... filled in, the way vanilla fills its own messages. The words are inserted after the
        // token is translated, so they are never read as tokens themselves.
        private static string L(string token, params object[] words) {
            if (string.IsNullOrEmpty(token)) { return ""; }
            if (Localization.instance == null) { return token; }
            return Localization.instance.Localize(token, words.Select(w => Convert.ToString(w)).ToArray());
        }

        // A vanilla token's translation, or the fallback when the language files have none: Valheim answers "[token]".
        private static string LocalizedOr(string token, string fallback) {
            string localized = ConfigUI.L(token);
            return string.IsNullOrEmpty(localized) || localized.StartsWith("[") ? fallback : localized;
        }

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

                // Only when flipped: it reads off whenever the YAML switch is off, and an unrelated save would otherwise turn this
                // entry off underneath an admin who left it on.
                if (staged.enableDistance != baseline.enableDistance) {
                    ValConfig.EnableDistanceLevelScalingBonus.Value = staged.enableDistance;
                }
                ValConfig.EnableMapRingsForDistanceBonus.Value = staged.enableDistanceOverlay;
                ValConfig.EnableZoneScalingBonus.Value = staged.enableZone;
                ValConfig.EnableZoneMapOverlay.Value = staged.enableZoneOverlay;
                ValConfig.ShowNoMapRingLevel.Value = staged.showNoMapRing;
                ValConfig.ShowNoMapZoneLevel.Value = staged.showNoMapZone;
                ValConfig.MapRingsAboveFog.Value = staged.ringsAboveFog;
                ValConfig.DistanceBonusIsFromStarterTemple.Value = staged.ringsFromTemple;
                ValConfig.DistanceRingColorOptions.Value = staged.ringColors;
                ValConfig.ZoneOverlayAboveFog.Value = staged.zonesAboveFog;
                ValConfig.ZoneLevelBonusPerLevel.Value = staged.zoneBonusPerLevel;
                ValConfig.ZoneKillsPerLevelUp.Value = staged.zoneKillsPerLevel;
                ValConfig.ZoneDecayLevelsPerHour.Value = staged.zoneDecayPerHour;
                ValConfig.ZoneDecayClock.Value = staged.zoneDecayClock;
                ValConfig.MaxZoneSize.Value = staged.zoneMaxSize;
                ValConfig.MinZoneSize.Value = staged.zoneMinSize;
                ValConfig.ZoneOverlayColorOptions.Value = staged.zoneColors;
                ValConfig.ZoneOverlayColorTransparency.Value = staged.zoneOpacity;
                ValConfig.KillReportFlushIntervalSeconds.Value = staged.killReportInterval;
                ValConfig.EnableRegionScalingBonus.Value = staged.enableRegion;
                ValConfig.EnableRegionMapOverlay.Value = staged.enableRegionOverlay;
                ValConfig.RegionOverlayAboveFog.Value = staged.regionsAboveFog;
                ValConfig.ShowNoMapRegionLevel.Value = staged.showNoMapRegion;
                ValConfig.RegionLevelBonusPerLevel.Value = staged.regionBonusPerLevel;
                ValConfig.RegionKillsPerLevelUp.Value = staged.regionKillsPerLevel;
                ValConfig.RegionDecayLevelsPerHour.Value = staged.regionDecayPerHour;
                ValConfig.RegionTargetSize.Value = staged.regionTargetSize;
                ValConfig.RegionMinSize.Value = staged.regionMinSize;
                ValConfig.RegionMountainMinSize.Value = staged.regionMountainMinSize;
                ValConfig.RegionOverlayOpacity.Value = staged.regionOpacity;

                ValConfig.EnemyHealthMultiplier.Value = staged.creatureHpPerLevel;
                ValConfig.EnemyDamageLevelMultiplier.Value = staged.creatureDmgPerLevel;
                ValConfig.BossEnemyHealthMultiplier.Value = staged.bossHpPerLevel;
                ValConfig.BossEnemyDamageMultiplier.Value = staged.bossDmgPerLevel;
                ValConfig.EnableCreatureScalingPerLevel.Value = staged.scaleSizePerStar;
                ValConfig.PerLevelScaleBonus.Value = staged.sizePerStar;
                ValConfig.MaximumCreatureScale.Value = staged.maxCreatureScale;
                ValConfig.MaxLevel.Value = staged.maxStars;
                ValConfig.MaxBossLevel.Value = staged.maxBossLevel;

                ValConfig.EnableMultiplayerEnemyHealthScaling.Value = staged.mpHealth;
                ValConfig.MultiplayerEnemyHealthModifier.Value = staged.mpHealthMod;
                ValConfig.EnableMultiplayerEnemyDamageScaling.Value = staged.mpDamage;
                ValConfig.MultiplayerEnemyDamageModifier.Value = staged.mpDamageMod;
                ValConfig.MultiplayerScalingRequiredPlayersNearby.Value = staged.mpRequiredPlayers;

                ValConfig.EnableColorization.Value = staged.enableColorization;
                ValConfig.EnableCreatureModifiers.Value = staged.enableModifiers;
                ValConfig.MaxMajorModifiersPerCreature.Value = staged.maxMajor;
                ValConfig.MaxMinorModifiersPerCreature.Value = staged.maxMinor;
                ValConfig.ChanceMajorModifier.Value = staged.chanceMajor;
                ValConfig.ChanceMinorModifier.Value = staged.chanceMinor;
                ValConfig.MaxBossModifiersPerCreature.Value = staged.maxBossOnCreature;
                ValConfig.ChanceOfBossModifierOnCreature.Value = staged.chanceBossOnCreature;
                ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value = staged.limitToStarLevel;
                ValConfig.EnableBossModifiers.Value = staged.enableBossMods;
                ValConfig.MaxBossModifiersPerBoss.Value = staged.maxBossMods;
                ValConfig.ChanceOfBossModifier.Value = staged.chanceBoss;
                ValConfig.MaxMajorModifiersPerBoss.Value = staged.maxMajorOnBoss;
                ValConfig.ChanceOfMajorModifierOnBoss.Value = staged.chanceMajorOnBoss;
                ValConfig.MaxMinorModifiersPerBoss.Value = staged.maxMinorOnBoss;
                ValConfig.ChanceOfMinorModifierOnBoss.Value = staged.chanceMinorOnBoss;
                ValConfig.LimitCreatureModifierPrefixes.Value = staged.prefixLimit;
                ValConfig.MinorModifiersFirstInName.Value = staged.minorFirst;
                ValConfig.AutoTuneBiomeStarCaps.Value = staged.biomeCapAuto;
                ValConfig.ModifierIconDisplayStyle.Value = staged.displayStyle.ToString();

                // Raids - plain BepInEx ConfigEntries; "Enable SLS Raids" is the inverse of vanilla raids.
                ValConfig.UseVanillaRaidConfiguration.Value = !staged.enableSlsRaids;
                ValConfig.ServerTimeBetweenRaidStartChecks.Value = staged.raidCheckMinutes;
                ValConfig.MaxRaidAttemptsPerPlayer.Value = staged.maxRaidAttempts;
                ValConfig.MaxActiveRaids.Value = staged.maxActiveRaids;

                // Night spawns - one ConfigEntry per boss.
                SaveNightSpawns();

                // Loot - the per-level scales are ConfigEntries; the distance rings are in the LootSettings YAML.
                ValConfig.LootDropCalculationType.Value = staged.loot.style.ToString();
                ValConfig.PerLevelLootScale.Value = staged.loot.perLevelScale;
                ValConfig.PerLevelLootChanceScale.Value = staged.loot.perLevelChanceScale;
                ValConfig.ChanceBaseChancePerLevel.Value = staged.loot.chanceBase;
                ValConfig.ScaleAllLootByLevel.Value = staged.loot.scaleAllLoot;
                ValConfig.LootEggsDropIncreaseStacks.Value = staged.loot.eggStacks;
                ValConfig.EnableTreeScaling.Value = staged.loot.treeScaling;
                ValConfig.EnableRockLevels.Value = staged.loot.rockScaling;
                ValConfig.EnableScalingBirds.Value = staged.loot.birdScaling;
                ValConfig.PerLevelTreeLootScale.Value = staged.loot.treeScale;
                ValConfig.PerLevelMineRockLootScale.Value = staged.loot.rockScale;
                ValConfig.PerLevelDestructibleLootScale.Value = staged.loot.destructibleScale;
                ValConfig.PerLevelBirdLootScale.Value = staged.loot.birdScale;

                // Nemesis - enable flag is a ConfigEntry; the rest is in the NemesisSettings YAML.
                ValConfig.EnableNemesisSystem.Value = staged.enableNemesis;
                // Only when flipped: it reads off whenever the YAML switch is off, and an unrelated save would otherwise
                // turn this entry off underneath an admin who left it on.
                if (staged.enableNemesisRemote != baseline.enableNemesisRemote) {
                    ValConfig.EnableNemesisRemoteSpawning.Value = staged.enableNemesisRemote;
                }

                // Location reset - the master switch and sweep budget are ConfigEntries; the rest is in the
                // LocationResetSettings YAML.
                ValConfig.EnableLocationReset.Value = staged.locationReset.masterSwitch;
                ValConfig.LocationResetSweepBudgetMs.Value = staged.locationReset.sweepBudgetMs;

                foreach (PendingYaml doc in pendingYaml) {
                    ApplyYaml(doc.File, doc.Yaml, doc.Label, failures, warnings);
                }

                // The ConfigEntry writes above are local-only until Jotunn is told to push them, on a host
                // as much as on a remote admin. After the YAML, so peers get both halves of the save together.
                PushConfigChanges();
                RebuildMapsWithNewSizes();
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfigureTool failed to apply configuration: {e}");
                message = L("$sls_cfg_status_save_failed", e.Message);
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
        // label is a token naming what the document holds; the log names the file instead, in English.
        private static void ApplyYaml(YamlConfigFile file, string yaml, string label, List<string> failures, List<string> warnings) {
            if (IsOwner() == false) {
                if (ConfigNetwork.RequestEdit(file, yaml, out string refusal)) {
                    warnings.Add(L("$sls_cfg_status_sent_to_server", L(label)));
                    return;
                }
                Logger.LogWarning($"{file.FileName} was not saved: {refusal}");
                failures.Add(L("$sls_cfg_status_not_saved", L(label), refusal));
                return;
            }
            if (YamlConfigManager.ApplyEdited(file, yaml, out string result)) {
                if (string.IsNullOrEmpty(result) == false) { warnings.Add(result); }
                return;
            }
            Logger.LogWarning($"{file.FileName} was not saved: {result}");
            failures.Add(L("$sls_cfg_status_not_saved", L(label), result));
        }

        // The server's answer to a YAML edit sent from here. The server broadcasts an accepted file before it answers,
        // so by now the live settings hold it and a fresh baseline stops it counting as unsaved.
        private static void OnRemoteEditResult(YamlConfigFile file, bool accepted, string message) {
            string name = file?.FileName ?? L("$sls_cfg_label_settings");
            if (accepted) {
                Logger.LogInfo($"The server accepted {name}. {message}");
                if (panel != null && staged != null) {
                    baseline = StagedConfig.Snapshot();
                    // The message carries the server's warnings, including a failed disk write.
                    string saved = L("$sls_cfg_status_saved_on_server", name);
                    SetStatus(string.IsNullOrEmpty(message) ? saved : saved + " " + message, string.IsNullOrEmpty(message));
                }
                return;
            }
            Logger.LogWarning($"The server refused {name}: {message}");
            string refused = L("$sls_cfg_status_server_refused", name, message);
            if (panel != null) {
                SetStatus(refused, false);
            } else if (MessageHud.instance != null) {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, refused);
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
                failures.Add(L("$sls_cfg_status_not_saved", L(label), L("$sls_cfg_status_parse_error", file.FileName, parseError)));
                return;
            }
            if (report.HasErrors) {
                failures.Add(L("$sls_cfg_status_not_saved", L(label), string.Join(" ", report.Errors.ToArray())));
                return;
            }
            pendingYaml.Add(new PendingYaml { File = file, Yaml = yaml, Label = label });
        }

        // After the push, so the server holds the new sizes by the time it is asked to cut with them. Both reset their levels,
        // which the rows say. From the main menu there is no map to cut: the sizes go to the next one built.
        private static void RebuildMapsWithNewSizes() {
            if (ZNet.instance == null) { return; }
            if (staged.enableZone && (staged.zoneMaxSize != baseline.zoneMaxSize || staged.zoneMinSize != baseline.zoneMinSize)) {
                if (ZNet.instance.IsServer()) { ZoneScaleSystem.RebuildZones(); } else { TerminalManager.RunOnServer("sls-zone-rebuild"); }
            }
            if (staged.enableRegion && (staged.regionTargetSize != baseline.regionTargetSize || staged.regionMinSize != baseline.regionMinSize || staged.regionMountainMinSize != baseline.regionMountainMinSize)) {
                if (ZNet.instance.IsServer()) { RegionScaleSystem.RebuildRegions(out _); } else { TerminalManager.RunOnServer("sls-region-rebuild"); }
            }
        }

        private static void SaveLevelSettings(List<string> failures, List<string> warnings) {
            CreatureLevelSettings live = LevelSystemData.SLE_Level_Settings;
            if (live == null) { return; }

            bool conditionalChanged = staged.enableConditional != baseline.enableConditional || staged.conditionalDelay != baseline.conditionalDelay || staged.conditionalIncreases != baseline.conditionalIncreases;
            bool curveChanged = staged.CurveDiffers(baseline);
            bool bossCurveChanged = staged.BossCurveDiffers(baseline);
            List<int> changedSpans = staged.ChangedTableSpans(baseline);
            bool bossTablesChanged = TablesEqual(staged.bossTables, baseline.bossTables) == false;
            Dictionary<Heightmap.Biome, int> changedCaps = staged.ChangedBiomeCaps(live);
            bool ringsChanged = LevelRingsMatch(staged.levelRings, baseline.levelRings) == false;
            bool allBiomeChanged = staged.AllBiomeMatches(baseline) == false;
            bool biomeStrengthsChanged = BiomeStrengthsMatch(staged.biomeStrengths, baseline.biomeStrengths) == false;
            bool distanceYamlTurnedOn = staged.enableDistance && baseline.distanceYamlOn == false;
            if (biomeStrengthsChanged == false && distanceYamlTurnedOn == false && conditionalChanged == false && curveChanged == false && bossCurveChanged == false && changedSpans.Count == 0 && bossTablesChanged == false && changedCaps.Count == 0 && ringsChanged == false && allBiomeChanged == false) { return; }

            // From the settings as written, not the live copy: the live one has every generator already expanded into the
            // chance tables, and saving it wrote those expansions over the hand-written tables in the file.
            CreatureLevelSettings settings = CopyForEdit(YamlConfigManager.LevelSettings, LevelSystemData.AuthoredLevelSettings ?? live);
            settings.EnableConditionalCreatureLevelupChance = staged.enableConditional;
            settings.ConditionalBiomeDelay = staged.conditionalDelay;
            settings.ConditionalDifficultyIncreases = staged.conditionalIncreases;
            if (distanceYamlTurnedOn) { settings.EnableDistanceLevelBonus = true; }
            if (biomeStrengthsChanged) { WriteBiomeStrengths(settings); }
            if (curveChanged && staged.tableCurve != null) {
                // Reset page or a difficulty set the curve as a table, which no generator describes.
                settings.DefaultCreatureLevelUpChance = new SortedDictionary<int, float>(staged.tableCurve);
                settings.DefaultLevelupGenerators = null;
                settings.DefaultLevelupGeneratorRefs = null;
            } else if (curveChanged) {
                settings.DefaultLevelupGenerators = new List<LevelGenerator> { CloneGenerator(staged.generator) };
                // The page shows and saves one curve. Referenced generators would be merged into it on load and quietly
                // change every chance it showed.
                settings.DefaultLevelupGeneratorRefs = null;
            }
            if (ringsChanged) {
                // Written empty rather than left out when every ring was removed, so the file says so.
                settings.DistanceLevelBonus = LevelRingsToYaml(staged.levelRings);
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
                    settings.LevelupChanceTablesBySpan[span] = KeyedTable(staged.tables[span]);
                }
            }
            if (bossTablesChanged) {
                // All of them rather than the changed spans, since Reset page removes them. No tables, no key in the file.
                settings.BossLevelupChanceTablesBySpan = staged.bossTables.Count == 0 ? null
                    : staged.bossTables.ToDictionary(entry => entry.Key, entry => KeyedTable(entry.Value));
            }
            if (settings.BiomeConfiguration != null) {
                foreach (KeyValuePair<Heightmap.Biome, int> cap in changedCaps) {
                    if (settings.BiomeConfiguration.TryGetValue(cap.Key, out BiomeSpecificSetting biome) && biome != null) {
                        biome.BiomeMaxLevelOverride = cap.Value;
                    } else if (BiomeRegistry.IsVanilla(cap.Key) == false) {
                        settings.BiomeConfiguration[cap.Key] = new BiomeSpecificSetting() { BiomeMaxLevelOverride = cap.Value };
                    }
                }
            }
            if (allBiomeChanged) { WriteAllBiomeSettings(settings); }
            SaveYaml(YamlConfigManager.LevelSettings, settings, "$sls_cfg_label_level_settings", failures, warnings);
        }

        // Only the ones that differ from when the panel opened, so an entry an admin never touched stays out of the file.
        private static void WriteBiomeStrengths(CreatureLevelSettings settings) {
            if (settings.BiomeConfiguration == null) { settings.BiomeConfiguration = new Dictionary<Heightmap.Biome, BiomeSpecificSetting>(); }
            foreach (KeyValuePair<Heightmap.Biome, float> strength in staged.biomeStrengths) {
                baseline.biomeStrengths.TryGetValue(strength.Key, out float was);
                if (strength.Value == was) { continue; }
                Heightmap.Biome key = BiomeRegistry.ConfigKeyFor(settings.BiomeConfiguration, strength.Key);
                if (settings.BiomeConfiguration.TryGetValue(key, out BiomeSpecificSetting biome) == false || biome == null) {
                    biome = new BiomeSpecificSetting();
                    settings.BiomeConfiguration[key] = biome;
                }
                biome.DistanceScaleModifier = strength.Value;
            }
        }

        // Every biome a world has, with what it stores. The All biome has its own row on the page.
        private static Dictionary<Heightmap.Biome, float> BiomeStrengthsOf(CreatureLevelSettings settings) {
            Dictionary<Heightmap.Biome, float> strengths = new Dictionary<Heightmap.Biome, float>();
            Heightmap.Biome[] vanilla = { Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth };
            foreach (Heightmap.Biome biome in vanilla.Concat(BiomeRegistry.CustomBiomes)) { strengths[biome] = 1f; }
            if (settings?.BiomeConfiguration != null) {
                foreach (KeyValuePair<Heightmap.Biome, BiomeSpecificSetting> biome in settings.BiomeConfiguration) {
                    if (biome.Key == Heightmap.Biome.All || biome.Key == Heightmap.Biome.None || biome.Value == null) { continue; }
                    strengths[biome.Key] = biome.Value.DistanceScaleModifier;
                }
            }
            return strengths;
        }

        private static bool BiomeStrengthsMatch(Dictionary<Heightmap.Biome, float> a, Dictionary<Heightmap.Biome, float> b) {
            if (a == null || b == null) { return a == b; }
            return a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out float other) && other == entry.Value);
        }

        // What a difficulty keeps on the All biome. Every other biome inherits these unless it sets its own.
        private static void WriteAllBiomeSettings(CreatureLevelSettings settings) {
            if (settings.BiomeConfiguration == null) { settings.BiomeConfiguration = new Dictionary<Heightmap.Biome, BiomeSpecificSetting>(); }
            if (settings.BiomeConfiguration.TryGetValue(Heightmap.Biome.All, out BiomeSpecificSetting all) == false || all == null) {
                all = new BiomeSpecificSetting();
                settings.BiomeConfiguration[Heightmap.Biome.All] = all;
            }
            all.SpawnRateModifier = staged.allSpawnRate;
            all.DistanceScaleModifier = staged.allDistanceScale;
            if (all.NightSettings != null || staged.allNightLevelScale != 1f) {
                if (all.NightSettings == null) { all.NightSettings = new BiomeNightSettings(); }
                all.NightSettings.NightLevelUpChanceScaler = staged.allNightLevelScale;
            }
            if (all.CreatureBaseValueModifiers != null || staged.allBaseDamage != 1f) {
                if (all.CreatureBaseValueModifiers == null) { all.CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>(); }
                all.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseDamage] = staged.allBaseDamage;
            }
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
            SaveYaml(YamlConfigManager.ModifierSettings, copy, "$sls_cfg_label_modifier_settings", failures, warnings);
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
            if (live == null) { return; }
            // Turning remote bosses on needs the YAML's own switch on too. Turning them off leaves it alone: the
            // ConfigEntry is enough to stop them.
            bool enableRemoteInFile = staged.enableNemesisRemote && baseline.enableNemesisRemote == false && live.RemoteSpawning?.Enabled != true;
            if (staged.NemesisMatches(baseline) && enableRemoteInFile == false) { return; }

            NemesisConfiguration copy = CopyForEdit(YamlConfigManager.NemesisSettings, live);
            if (enableRemoteInFile) {
                if (copy.RemoteSpawning == null) { copy.RemoteSpawning = new RemoteNemesisSpawnSettings(); }
                copy.RemoteSpawning.Enabled = true;
            }
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
            SaveYaml(YamlConfigManager.NemesisSettings, copy, "$sls_cfg_label_nemesis_settings", failures, warnings);
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
        private static bool GeneratorEdited(LevelGenerator current, LevelGenerator opened, bool openedInline) {
            if (openedInline || current == null || opened == null) { return GeneratorsEqual(current, opened) == false; }
            LevelGenerator sameRange = CloneGenerator(current);
            sameRange.MaxLevel = opened.MaxLevel;
            // Max stars pulls Min down with it when it drops below; that is still the slider, not a chosen start.
            if (current.MinLevel == Mathf.Min(opened.MinLevel, current.MaxLevel)) { sameRange.MinLevel = opened.MinLevel; }
            return GeneratorsEqual(sameRange, opened) == false;
        }

        // Whether LevelSettings.yaml's default curve is exactly this hand-written table, with no generator over it. A file
        // with no table rolls the built-in one, so that counts as it.
        private static bool FileCurveIs(SortedDictionary<int, float> table) {
            CreatureLevelSettings authored = LevelSystemData.AuthoredLevelSettings ?? LevelSystemData.SLE_Level_Settings;
            if (authored == null) { return false; }
            if (authored.DefaultLevelupGenerators?.Any(g => g != null) == true || (authored.DefaultLevelupGeneratorRefs?.Count ?? 0) > 0) { return false; }
            return ChanceTablesEqual(authored.DefaultCreatureLevelUpChance ?? LevelSystemData.DefaultConfiguration.DefaultCreatureLevelUpChance, table);
        }

        // Keys and values, with room for the float round trip through YAML.
        private static bool ChanceTablesEqual(SortedDictionary<int, float> a, SortedDictionary<int, float> b) {
            if (a == null || b == null) { return a == b; }
            if (a.Count != b.Count) { return false; }
            foreach (KeyValuePair<int, float> entry in a) {
                if (b.TryGetValue(entry.Key, out float other) == false || Mathf.Abs(entry.Value - other) > 0.00001f) { return false; }
            }
            return true;
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

        private static bool TablesEqual(Dictionary<int, List<float>> a, Dictionary<int, List<float>> b) {
            return a.Count == b.Count && a.Keys.All(span => TableEqual(a, b, span));
        }

        private static SortedDictionary<int, float> KeyedTable(List<float> values) {
            SortedDictionary<int, float> table = new SortedDictionary<int, float>();
            for (int i = 0; i < values.Count; i++) { table[i + 1] = values[i]; }
            return table;
        }

        private static Dictionary<int, List<float>> ValuesBySpan(Dictionary<int, SortedDictionary<int, float>> written) {
            Dictionary<int, List<float>> tables = new Dictionary<int, List<float>>();
            if (written != null) {
                foreach (KeyValuePair<int, SortedDictionary<int, float>> entry in written) {
                    if (entry.Value != null) { tables[entry.Key] = entry.Value.Values.ToList(); }
                }
            }
            return tables;
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
            public int conditionalDelay, conditionalIncreases;

            // The Level Scaling page's settings past those switches.
            public bool distanceYamlOn;
            public bool ringsAboveFog, ringsFromTemple;
            public string ringColors;
            // What each biome stores, keyed as the file does. 1 is a biome with no strength of its own, which takes the All biome's.
            public Dictionary<Heightmap.Biome, float> biomeStrengths;
            public bool zonesAboveFog;
            public float zoneBonusPerLevel, zoneDecayPerHour, zoneMaxSize, zoneMinSize, zoneOpacity, killReportInterval;
            public int zoneKillsPerLevel;
            public string zoneDecayClock, zoneColors;
            public bool enableRegion, enableRegionOverlay, regionsAboveFog, showNoMapRegion;
            public float regionBonusPerLevel, regionDecayPerHour, regionTargetSize, regionMinSize, regionMountainMinSize, regionOpacity;
            public int regionKillsPerLevel;

            public float creatureHpPerLevel, creatureDmgPerLevel, bossHpPerLevel, bossDmgPerLevel;
            public int maxBossLevel;

            // Creature size, at the bottom of the distribution page.
            public bool scaleSizePerStar;
            public float sizePerStar, maxCreatureScale;

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
            // Set by a difficulty, and by Reset page when the shipped curve is a hand-written table: the pages then show and
            // save that table, and generator is only the seed shaped after it. Any edit to the curve clears it, handing the
            // curve back to generator. Never set by a snapshot.
            public SortedDictionary<int, float> tableCurve;

            // The distance rings, nearest first. See QuickConfigureDistanceRings.cs.
            public List<StagedLevelRing> levelRings;

            // Bosses roll from the creature curve unless one is configured for them. bossGenerator is kept seeded either
            // way, so turning the toggle on starts from something sensible rather than from nothing.
            public bool bossCurveOn;
            public LevelGenerator bossGenerator;
            // As generatorInline, for BossLevelupGenerators.
            public bool bossGeneratorInline;

            // Table style thresholds keyed by span, values in key order, seeded from LevelupChanceTablesBySpan.
            public Dictionary<int, List<float>> tables;
            // The boss curve's own, seeded from BossLevelupChanceTablesBySpan. See BossTableFor.
            public Dictionary<int, List<float>> bossTables;

            // Biome star caps as they were when this snapshot was taken (or as shipped, after Reset page), and the MaxLevel
            // they sat under. While caps are auto-tuned they are always scaled from these, so moving Max back and forth
            // never compounds rounding.
            public Dictionary<Heightmap.Biome, int> biomeCapOriginals;
            public int biomeCapBaseMax;
            // Auto-tuning off means the caps are whatever was typed on the page instead, and Max stars leaves them alone.
            public bool biomeCapAuto;
            public Dictionary<Heightmap.Biome, int> biomeCapManual;
            // Custom biomes with no cap of their own, whose row shows the cap they are under now and writes nothing.
            public HashSet<Heightmap.Biome> biomeCapUnset = new HashSet<Heightmap.Biome>();
            public int biomeCapForUnset;

            // What a difficulty keeps on the All biome. See DifficultyPreset.
            public float allSpawnRate, allNightLevelScale, allDistanceScale, allBaseDamage;

            public bool enableColorization, enableModifiers;
            public int maxMajor, maxMinor, maxBossOnCreature, maxBossMods, maxMajorOnBoss, maxMinorOnBoss, prefixLimit;
            public float chanceMajor, chanceMinor, chanceBossOnCreature, chanceBoss, chanceMinorOnBoss, chanceMajorOnBoss;
            public bool limitToStarLevel, enableBossMods, minorFirst;
            public ModifierDisplayStyle displayStyle;

            // Modifier enable/disable: the collection the page lists (read-only here) and the names toggled on per category.
            public CreatureModifierCollection modifierSource;
            public Dictionary<ModifierType, HashSet<string>> modifierOn;

            // Raids (BepInEx ConfigEntries). enableSlsRaids is the inverse of UseVanillaRaidConfiguration.
            public bool enableSlsRaids;
            public int raidCheckMinutes, maxRaidAttempts, maxActiveRaids;

            // RaidSettings.yaml GlobalSettings: DisableAllRaids, GlobalRaidChanceScalar, GlobalRaidIntervalScalar and
            // PauseBeforeCompleted.
            public bool disableAllRaids;
            public float raidChanceScalar, raidIntervalScalar;
            public int raidPauseBeforeCompleted;

            // Per-raid enable/disable: the raids the page lists (read-only here) and the names toggled on. raidSpawns
            // holds the per-creature numbers that page edits, keyed by position in the file.
            public RaidConfiguration raidSource;
            public HashSet<string> raidsOn;
            public Dictionary<string, StagedRaidSpawn> raidSpawns;

            // Raid creature density, 1-6. raidDensityBase is the density the file's numbers were written at, so every
            // spawn count the slider derives is scaled from the snapshot rather than from the last slider position.
            public int raidDensity;
            public int raidDensityBase;

            // Whether each boss's night spawns are allowed, keyed by its defeat key. See QuickConfigureNightSpawns.cs.
            public Dictionary<string, bool> nightSpawnsOn;

            // Nemesis system. enableNemesis is a ConfigEntry; the rest live in the NemesisSettings YAML. enableNemesisRemote is
            // both: remote bosses only spawn with the ConfigEntry and the YAML's RemoteSpawning.Enabled on, so it reads on
            // only when both are.
            public bool enableNemesis, enableNemesisRemote;
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
                    ringsAboveFog = ValConfig.MapRingsAboveFog.Value,
                    ringsFromTemple = ValConfig.DistanceBonusIsFromStarterTemple.Value,
                    ringColors = ValConfig.DistanceRingColorOptions.Value,
                    zonesAboveFog = ValConfig.ZoneOverlayAboveFog.Value,
                    zoneBonusPerLevel = ValConfig.ZoneLevelBonusPerLevel.Value,
                    zoneKillsPerLevel = ValConfig.ZoneKillsPerLevelUp.Value,
                    zoneDecayPerHour = ValConfig.ZoneDecayLevelsPerHour.Value,
                    zoneDecayClock = ValConfig.ZoneDecayClock.Value,
                    zoneMaxSize = ValConfig.MaxZoneSize.Value,
                    zoneMinSize = ValConfig.MinZoneSize.Value,
                    zoneColors = ValConfig.ZoneOverlayColorOptions.Value,
                    zoneOpacity = ValConfig.ZoneOverlayColorTransparency.Value,
                    killReportInterval = ValConfig.KillReportFlushIntervalSeconds.Value,
                    enableRegion = ValConfig.EnableRegionScalingBonus.Value,
                    enableRegionOverlay = ValConfig.EnableRegionMapOverlay.Value,
                    regionsAboveFog = ValConfig.RegionOverlayAboveFog.Value,
                    showNoMapRegion = ValConfig.ShowNoMapRegionLevel.Value,
                    regionBonusPerLevel = ValConfig.RegionLevelBonusPerLevel.Value,
                    regionKillsPerLevel = ValConfig.RegionKillsPerLevelUp.Value,
                    regionDecayPerHour = ValConfig.RegionDecayLevelsPerHour.Value,
                    regionTargetSize = ValConfig.RegionTargetSize.Value,
                    regionMinSize = ValConfig.RegionMinSize.Value,
                    regionMountainMinSize = ValConfig.RegionMountainMinSize.Value,
                    regionOpacity = ValConfig.RegionOverlayOpacity.Value,

                    creatureHpPerLevel = ValConfig.EnemyHealthMultiplier.Value,
                    creatureDmgPerLevel = ValConfig.EnemyDamageLevelMultiplier.Value,
                    bossHpPerLevel = ValConfig.BossEnemyHealthMultiplier.Value,
                    bossDmgPerLevel = ValConfig.BossEnemyDamageMultiplier.Value,
                    scaleSizePerStar = ValConfig.EnableCreatureScalingPerLevel.Value,
                    sizePerStar = ValConfig.PerLevelScaleBonus.Value,
                    maxCreatureScale = ValConfig.MaximumCreatureScale.Value,
                    maxStars = ValConfig.MaxLevel.Value,
                    maxBossLevel = ValConfig.MaxBossLevel.Value,

                    mpHealth = ValConfig.EnableMultiplayerEnemyHealthScaling.Value,
                    mpHealthMod = ValConfig.MultiplayerEnemyHealthModifier.Value,
                    mpDamage = ValConfig.EnableMultiplayerEnemyDamageScaling.Value,
                    mpDamageMod = ValConfig.MultiplayerEnemyDamageModifier.Value,
                    mpRequiredPlayers = ValConfig.MultiplayerScalingRequiredPlayersNearby.Value,

                    enableColorization = ValConfig.EnableColorization.Value,
                    enableModifiers = ValConfig.EnableCreatureModifiers.Value,
                    maxMajor = ValConfig.MaxMajorModifiersPerCreature.Value,
                    maxMinor = ValConfig.MaxMinorModifiersPerCreature.Value,
                    chanceMajor = ValConfig.ChanceMajorModifier.Value,
                    chanceMinor = ValConfig.ChanceMinorModifier.Value,
                    maxBossOnCreature = ValConfig.MaxBossModifiersPerCreature.Value,
                    chanceBossOnCreature = ValConfig.ChanceOfBossModifierOnCreature.Value,
                    limitToStarLevel = ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value,
                    enableBossMods = ValConfig.EnableBossModifiers.Value,
                    chanceBoss = ValConfig.ChanceOfBossModifier.Value,
                    maxBossMods = ValConfig.MaxBossModifiersPerBoss.Value,
                    chanceMajorOnBoss = ValConfig.ChanceOfMajorModifierOnBoss.Value,
                    maxMajorOnBoss = ValConfig.MaxMajorModifiersPerBoss.Value,
                    chanceMinorOnBoss = ValConfig.ChanceOfMinorModifierOnBoss.Value,
                    maxMinorOnBoss = ValConfig.MaxMinorModifiersPerBoss.Value,
                    prefixLimit = ValConfig.LimitCreatureModifierPrefixes.Value,
                    minorFirst = ValConfig.MinorModifiersFirstInName.Value,
                    biomeCapAuto = ValConfig.AutoTuneBiomeStarCaps.Value,

                    enableSlsRaids = !ValConfig.UseVanillaRaidConfiguration.Value,
                    raidCheckMinutes = ValConfig.ServerTimeBetweenRaidStartChecks.Value,
                    maxRaidAttempts = ValConfig.MaxRaidAttemptsPerPlayer.Value,
                    maxActiveRaids = ValConfig.MaxActiveRaids.Value,

                    enableNemesis = ValConfig.EnableNemesisSystem.Value,
                };

                CreatureLevelSettings settings = LevelSystemData.SLE_Level_Settings;
                s.enableConditional = settings != null && settings.EnableConditionalCreatureLevelupChance;
                s.conditionalDelay = settings != null ? settings.ConditionalBiomeDelay : 1;
                s.conditionalIncreases = settings != null ? settings.ConditionalDifficultyIncreases : 7;
                // The roll needs both switches. The page offers one, which reads on only when both are.
                s.distanceYamlOn = settings == null || settings.EnableDistanceLevelBonus;
                s.enableDistance = s.enableDistance && s.distanceYamlOn;
                s.generator = SeedGenerator(settings, s.maxStars);
                s.generatorInline = settings?.DefaultLevelupGenerators?.Any(g => g != null) == true;
                s.bossCurveOn = (settings?.BossLevelupGenerators?.Count ?? 0) > 0 || (settings?.BossLevelupGeneratorRefs?.Count ?? 0) > 0;
                s.bossGenerator = SeedBossGenerator(settings, s.maxBossLevel, s.generator);
                s.bossGeneratorInline = settings?.BossLevelupGenerators?.Any(g => g != null) == true;
                s.tables = ValuesBySpan(settings?.LevelupChanceTablesBySpan);
                s.bossTables = ValuesBySpan(settings?.BossLevelupChanceTablesBySpan);
                s.biomeCapOriginals = new Dictionary<Heightmap.Biome, int>();
                s.biomeCapBaseMax = s.maxStars;
                if (settings?.BiomeConfiguration != null) {
                    foreach (KeyValuePair<Heightmap.Biome, BiomeSpecificSetting> biome in settings.BiomeConfiguration) {
                        if (biome.Value != null && biome.Value.BiomeMaxLevelOverride > 0) { s.biomeCapOriginals[biome.Key] = biome.Value.BiomeMaxLevelOverride; }
                    }
                }
                s.AddUncappedCustomBiomes(settings);
                s.biomeCapManual = new Dictionary<Heightmap.Biome, int>(s.biomeCapOriginals);
                BiomeSpecificSetting allBiome = null;
                settings?.BiomeConfiguration?.TryGetValue(Heightmap.Biome.All, out allBiome);
                s.allSpawnRate = allBiome?.SpawnRateModifier ?? 1f;
                s.allNightLevelScale = allBiome?.NightSettings?.NightLevelUpChanceScaler ?? 1f;
                s.allDistanceScale = allBiome?.DistanceScaleModifier ?? 1f;
                float allBaseDamage = 1f;
                allBiome?.CreatureBaseValueModifiers?.TryGetValue(CreatureBaseAttribute.BaseDamage, out allBaseDamage);
                s.allBaseDamage = allBaseDamage;
                // As written: the save starts from the authored settings too.
                s.levelRings = LevelRingsOf(LevelSystemData.AuthoredLevelSettings ?? settings);
                // As written too: in the live copy every biome with no strength of its own already holds the All biome's.
                s.biomeStrengths = BiomeStrengthsOf(LevelSystemData.AuthoredLevelSettings ?? settings);

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
                s.enableNemesisRemote = ValConfig.EnableNemesisRemoteSpawning.Value && nemesisCFG?.RemoteSpawning?.Enabled == true;
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
                // A file whose GlobalSettings section is missing reads as the class defaults, which is what a save would
                // start from too.
                GlobalRaidSettings raidGlobals = s.raidSource?.GlobalSettings ?? new GlobalRaidSettings();
                s.disableAllRaids = raidGlobals.DisableAllRaids;
                s.raidChanceScalar = raidGlobals.GlobalRaidChanceScalar;
                s.raidIntervalScalar = raidGlobals.GlobalRaidIntervalScalar;
                s.raidPauseBeforeCompleted = raidGlobals.PauseBeforeCompleted;
                s.raidDensity = ClampRaidDensity(raidGlobals.RaidCreatureDensity);
                s.raidDensityBase = s.raidDensity;
                s.raidSpawns = SnapshotRaidSpawns(s.raidSource, s.raidDensityBase);
                s.nightSpawnsOn = SnapshotNightSpawns();
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
            public static LevelGenerator SeedGenerator(CreatureLevelSettings settings, int maxStars) {
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
            public static LevelGenerator SeedBossGenerator(CreatureLevelSettings settings, int maxBossStars, LevelGenerator creature) {
                LevelGenerator src = settings?.BossLevelupGenerators?.FirstOrDefault(g => g != null);
                if (src != null) { return CloneGenerator(src); }
                LevelGenerator seeded = CloneGenerator(creature);
                seeded.MinLevel = 1;
                seeded.MaxLevel = Mathf.Max(1, maxBossStars) + 1;
                return seeded;
            }

            public int BossMinStars => Mathf.Max(0, Mathf.Min(bossGenerator.MinLevel, bossGenerator.MaxLevel) - 1);

            public int BossTableSpan => Mathf.Abs(bossGenerator.MaxLevel - bossGenerator.MinLevel) + 1;

            // The table a Table style boss curve of this span reads, or null. As in LevelGenerator, a span with no table of
            // its own reads the general one.
            public List<float> BossTableFor(int span) {
                if (bossTables.TryGetValue(span, out List<float> own) && own.Count > 0) { return own; }
                return tables.TryGetValue(span, out List<float> general) ? general : null;
            }

            // Whether what bosses roll from would change: the toggle moved, or the boss curve itself did.
            public bool BossCurveDiffers(StagedConfig other) {
                if (bossCurveOn != other.bossCurveOn) { return true; }
                if (bossCurveOn == false) { return false; }
                if (GeneratorEdited(bossGenerator, other.bossGenerator, other.bossGeneratorInline)) { return true; }
                if (bossGenerator.LevelupCalculationStyle != LevelupCalculationStyle.Table) { return false; }
                List<float> mine = BossTableFor(BossTableSpan), theirs = other.BossTableFor(BossTableSpan);
                return mine == null || theirs == null ? mine != theirs : mine.SequenceEqual(theirs) == false;
            }

            // A biome's star cap: scaled in proportion to the staged Max stars while auto-tuning is on, the typed value
            // when it is off, and 0 for a biome that has no cap at all.
            public int CapFor(Heightmap.Biome biome) {
                if (biomeCapUnset.Contains(biome)) { return biomeCapForUnset > 0 ? biomeCapForUnset : maxStars; }
                if (biomeCapOriginals.TryGetValue(biome, out int original) == false) { return 0; }
                if (biomeCapAuto == false) { return biomeCapManual.TryGetValue(biome, out int manual) ? manual : original; }
                if (biomeCapBaseMax <= 0 || maxStars == biomeCapBaseMax) { return original; }
                return Mathf.Max(1, Mathf.RoundToInt(original * (float)maxStars / biomeCapBaseMax));
            }

            // A custom biome gets a row whether or not it has a cap yet, so an admin can give it one without knowing how it
            // is keyed in the file. Without one it is under the All entry's cap, or MaxLevel.
            public void AddUncappedCustomBiomes(CreatureLevelSettings settings) {
                biomeCapUnset.Clear();
                biomeCapForUnset = 0;
                Dictionary<Heightmap.Biome, BiomeSpecificSetting> biomes = settings?.BiomeConfiguration;
                if (biomes != null && biomes.TryGetValue(Heightmap.Biome.All, out BiomeSpecificSetting all) && all != null) { biomeCapForUnset = all.BiomeMaxLevelOverride; }
                foreach (Heightmap.Biome custom in BiomeRegistry.CustomBiomes) {
                    Heightmap.Biome key = BiomeRegistry.ConfigKeyFor(biomes, custom);
                    if (biomes != null && biomes.TryGetValue(key, out BiomeSpecificSetting own) && own != null && own.BiomeMaxLevelOverride > 0) { continue; }
                    biomeCapOriginals[key] = biomeCapForUnset > 0 ? biomeCapForUnset : maxStars;
                    biomeCapUnset.Add(key);
                }
            }

            // Takes the caps auto-tuning is showing as the starting point for typing them by hand, so switching modes
            // never moves a cap on its own.
            public void HoldCapsForManualEditing() {
                foreach (Heightmap.Biome biome in biomeCapOriginals.Keys.ToList()) { biomeCapManual[biome] = CapFor(biome); }
            }

            // Reset page on the distribution page: everything it shows goes back to what a freshly generated .cfg and
            // LevelSettings.yaml hold, seeded the way Snapshot seeds from a file.
            public void ResetDistribution(CreatureLevelSettings shipped) {
                maxStars = DefaultOf(ValConfig.MaxLevel);
                maxBossLevel = DefaultOf(ValConfig.MaxBossLevel);
                biomeCapAuto = DefaultOf(ValConfig.AutoTuneBiomeStarCaps);

                generator = SeedGenerator(shipped, maxStars);
                // A shipped inline generator is an ordinary curve; a shipped table has to be saved as that table.
                bool shippedInline = shipped?.DefaultLevelupGenerators?.Any(g => g != null) == true;
                tableCurve = shippedInline ? null
                    : new SortedDictionary<int, float>(shipped?.DefaultCreatureLevelUpChance ?? LevelSystemData.DefaultConfiguration.DefaultCreatureLevelUpChance);
                bossCurveOn = (shipped?.BossLevelupGenerators?.Count ?? 0) > 0 || (shipped?.BossLevelupGeneratorRefs?.Count ?? 0) > 0;
                bossGenerator = SeedBossGenerator(shipped, maxBossLevel, generator);
                // Unlike the general tables below, these only exist for the curve this page is resetting.
                bossTables = ValuesBySpan(shipped?.BossLevelupChanceTablesBySpan);
                // Spans the shipped file has no table for keep theirs: the save only ever writes a span, never removes one.
                if (shipped?.LevelupChanceTablesBySpan != null) {
                    foreach (KeyValuePair<int, SortedDictionary<int, float>> entry in shipped.LevelupChanceTablesBySpan) {
                        if (entry.Value != null) { tables[entry.Key] = entry.Value.Values.ToList(); }
                    }
                }

                // Shipped caps for the biomes the file has an entry for, since the save only writes onto an existing one.
                // A cap on a biome the shipped file does not cap is left where it is.
                CreatureLevelSettings live = LevelSystemData.SLE_Level_Settings;
                if (shipped?.BiomeConfiguration != null && live?.BiomeConfiguration != null) {
                    foreach (KeyValuePair<Heightmap.Biome, BiomeSpecificSetting> biome in shipped.BiomeConfiguration) {
                        if (biome.Value == null || biome.Value.BiomeMaxLevelOverride <= 0) { continue; }
                        if (live.BiomeConfiguration.TryGetValue(biome.Key, out BiomeSpecificSetting target) && target != null) {
                            biomeCapOriginals[biome.Key] = biome.Value.BiomeMaxLevelOverride;
                        }
                    }
                }
                biomeCapBaseMax = maxStars;
                AddUncappedCustomBiomes(live);
                biomeCapManual = new Dictionary<Heightmap.Biome, int>(biomeCapOriginals);
            }

            // Biomes whose staged cap differs from what the live settings hold.
            public Dictionary<Heightmap.Biome, int> ChangedBiomeCaps(CreatureLevelSettings live) {
                Dictionary<Heightmap.Biome, int> changed = new Dictionary<Heightmap.Biome, int>();
                if (live?.BiomeConfiguration == null) { return changed; }
                foreach (Heightmap.Biome biome in biomeCapOriginals.Keys) {
                    if (biomeCapUnset.Contains(biome)) { continue; }
                    bool hasEntry = live.BiomeConfiguration.TryGetValue(biome, out BiomeSpecificSetting setting) && setting != null;
                    // Only a cap typed for a custom biome makes a new entry. Auto-tuning scales caps that exist.
                    if (hasEntry == false && (BiomeRegistry.IsVanilla(biome) || biomeCapAuto)) { continue; }
                    int cap = CapFor(biome);
                    if ((hasEntry ? setting.BiomeMaxLevelOverride : 0) != cap) { changed[biome] = cap; }
                }
                return changed;
            }

            // Whether the curve the page shows is no longer the one the world rolls from: the generator was changed, or it
            // is Table style and the table it reads was. A table set by Reset page or a difficulty is measured against the
            // file itself rather than the snapshot, so a save that wrote it stops counting it as a change.
            public bool CurveDiffers(StagedConfig other) {
                if (tableCurve != null) { return FileCurveIs(tableCurve) == false; }
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

            public bool AllBiomeMatches(StagedConfig o) {
                return allSpawnRate == o.allSpawnRate && allNightLevelScale == o.allNightLevelScale
                    && allDistanceScale == o.allDistanceScale && allBaseDamage == o.allBaseDamage;
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
                    && enableConditional == o.enableConditional && conditionalDelay == o.conditionalDelay && conditionalIncreases == o.conditionalIncreases
                    && ringsAboveFog == o.ringsAboveFog && ringsFromTemple == o.ringsFromTemple && ringColors == o.ringColors
                    && zonesAboveFog == o.zonesAboveFog && zoneBonusPerLevel == o.zoneBonusPerLevel && zoneKillsPerLevel == o.zoneKillsPerLevel
                    && zoneDecayPerHour == o.zoneDecayPerHour && zoneDecayClock == o.zoneDecayClock && zoneMaxSize == o.zoneMaxSize
                    && zoneMinSize == o.zoneMinSize && zoneColors == o.zoneColors && zoneOpacity == o.zoneOpacity && killReportInterval == o.killReportInterval
                    && enableRegion == o.enableRegion && enableRegionOverlay == o.enableRegionOverlay && regionsAboveFog == o.regionsAboveFog
                    && showNoMapRegion == o.showNoMapRegion && regionBonusPerLevel == o.regionBonusPerLevel && regionKillsPerLevel == o.regionKillsPerLevel
                    && regionDecayPerHour == o.regionDecayPerHour && regionTargetSize == o.regionTargetSize && regionMinSize == o.regionMinSize
                    && regionMountainMinSize == o.regionMountainMinSize && regionOpacity == o.regionOpacity
                    && BiomeStrengthsMatch(biomeStrengths, o.biomeStrengths)
                    && creatureHpPerLevel == o.creatureHpPerLevel && creatureDmgPerLevel == o.creatureDmgPerLevel
                    && bossHpPerLevel == o.bossHpPerLevel && bossDmgPerLevel == o.bossDmgPerLevel
                    && scaleSizePerStar == o.scaleSizePerStar && sizePerStar == o.sizePerStar && maxCreatureScale == o.maxCreatureScale
                    && maxStars == o.maxStars && maxBossLevel == o.maxBossLevel
                    && mpHealth == o.mpHealth && mpDamage == o.mpDamage
                    && mpHealthMod == o.mpHealthMod && mpDamageMod == o.mpDamageMod && mpRequiredPlayers == o.mpRequiredPlayers
                    && enableColorization == o.enableColorization && enableModifiers == o.enableModifiers
                    && maxMajor == o.maxMajor && maxMinor == o.maxMinor && maxBossMods == o.maxBossMods && maxMajorOnBoss == o.maxMajorOnBoss 
                    && maxMinorOnBoss == o.maxMinorOnBoss && maxBossOnCreature == o.maxBossOnCreature && prefixLimit == o.prefixLimit
                    && chanceMajor == o.chanceMajor && chanceMinor == o.chanceMinor && chanceBoss == o.chanceBoss
                    && chanceBossOnCreature == o.chanceBossOnCreature
                    && chanceMajorOnBoss == o.chanceMajorOnBoss && chanceMinorOnBoss == o.chanceMinorOnBoss
                    && limitToStarLevel == o.limitToStarLevel && enableBossMods == o.enableBossMods && minorFirst == o.minorFirst
                    && displayStyle == o.displayStyle && biomeCapAuto == o.biomeCapAuto
                    && enableSlsRaids == o.enableSlsRaids
                    && raidCheckMinutes == o.raidCheckMinutes && maxRaidAttempts == o.maxRaidAttempts && maxActiveRaids == o.maxActiveRaids
                    && enableNemesis == o.enableNemesis && enableNemesisRemote == o.enableNemesisRemote;
                if (scalars == false) { return false; }

                if (GeneratorsEqual(generator, o.generator) == false) { return false; }
                if (tableCurve != null && CurveDiffers(o)) { return false; }
                if (AllBiomeMatches(o) == false) { return false; }
                if (BossCurveDiffers(o)) { return false; }
                if (TablesEqual(tables, o.tables) == false || TablesEqual(bossTables, o.bossTables) == false) { return false; }
                foreach (Heightmap.Biome biome in biomeCapOriginals.Keys.Union(o.biomeCapOriginals.Keys)) {
                    if (CapFor(biome) != o.CapFor(biome)) { return false; }
                }
                if (LevelRingsMatch(levelRings, o.levelRings) == false) { return false; }

                foreach (ModifierType type in modifierOn.Keys) {
                    if (o.modifierOn.TryGetValue(type, out HashSet<string> other) == false || SetsEqual(modifierOn[type], other) == false) { return false; }
                }
                if (raidDensity != o.raidDensity || RaidGlobalsMatch(this, o) == false) { return false; }
                if (SetsEqual(raidsOn, o.raidsOn) == false || RaidSpawnsMatch(raidSpawns, o.raidSpawns) == false) { return false; }
                if (NightSpawnsMatch(nightSpawnsOn, o.nightSpawnsOn) == false) { return false; }
                if (locationReset.Matches(o.locationReset) == false) { return false; }
                if (loot.Matches(o.loot) == false) { return false; }
                return NemesisMatches(o);
            }
        }
    }
}
