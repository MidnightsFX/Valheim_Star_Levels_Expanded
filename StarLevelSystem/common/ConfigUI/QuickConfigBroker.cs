using HarmonyLib;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using UnityEngine;

#pragma warning disable IDE0130
namespace StarLevelSystem.common {
#pragma warning restore IDE0130

    // The shared "configure a mod" launcher: one button, bottom right, listing every loaded mod that has
    // registered a config panel.
    //
    // ============================================================================================
    // FROZEN CROSS-ASSEMBLY CONTRACT -- v1. Never change the shape of anything marked below.
    //
    // Every mod that copies Common/Config/UI/ compiles its OWN QuickConfigBroker, so the type identities
    // differ per assembly and nothing here can ever be reached by a cast. Discovery is by GameObject NAME,
    // component TYPE NAME and method SIGNATURE, all through reflection -- see ConfigUILauncher.
    //
    // Only BCL types cross the boundary. string, int, bool and System.Action live in assemblies every copy
    // already shares, so they marshal fine even though QuickConfigBroker itself does not.
    //
    // Amendment rules, and they are rules: ADDITIVE ONLY. Never rename a member, never reorder or retype a
    // parameter, never add a same-arity overload, never narrow visibility. A newer caller probes for a
    // newer method with GetMethod(...) != null and degrades silently when it is absent. If this ever truly
    // needs a breaking change, the only migration is a NEW BrokerObjectName -- i.e. two buttons on screen
    // during the transition.
    // ============================================================================================
    internal class QuickConfigBroker : MonoBehaviour {
        // --- FROZEN ---
        internal const string BrokerObjectName = "ModQuickConfigLauncher";   // GameObject.Find key
        internal const string BrokerTypeName = "QuickConfigBroker";          // Type.Name, NOT FullName
        internal const string ButtonObjectName = "ModQuickConfigButton";     // Transform.Find key, dedupe guard
        internal const int ContractVersion = 2;

        public int BrokerVersion {
            get { return ContractVersion; }
        }

        public void Register(string modName, Action openPanel) {
            if (string.IsNullOrEmpty(modName) || openPanel == null) { return; }
            if (entries.ContainsKey(modName) == false) { order.Add(modName); }
            entries[modName] = openPanel;
            RefreshVisibility();
        }

        public void Unregister(string modName) {
            if (string.IsNullOrEmpty(modName)) { return; }
            if (entries.Remove(modName)) { order.Remove(modName); }
            if (listPanel != null) { CloseList(); }
            RefreshVisibility();
        }

        public bool IsRegistered(string modName) {
            return string.IsNullOrEmpty(modName) == false && entries.ContainsKey(modName);
        }
        // --- END FROZEN ---

        private const float ButtonW = 150f;
        private const float ButtonH = 38f;
        private const float PanelW = 320f;

        private static QuickConfigBroker Instance;
        private static Harmony harmony;

        private readonly Dictionary<string, Action> entries = new Dictionary<string, Action>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();
        private GameObject mainMenuButton;
        private GameObject pauseButton;
        private GameObject listPanel;

        public void Awake() {
            Instance = this;

            GUIManager.OnCustomGUIAvailable += OnCustomGUIAvailable;
            SynchronizationManager.OnAdminStatusChanged += OnAdminStatusChanged;
            SynchronizationManager.OnConfigurationSynchronized += OnConfigurationSynchronized;

            // Patched with a private Harmony instance rather than [HarmonyPatch] attributes, so a plugin
            // that also calls Harmony.CreateAndPatchAll(assembly) cannot apply it a second time. Exactly
            // one broker ever exists, so the id is the frozen object name and not a plugin GUID -- that is
            // what keeps a second mod's copy from double-patching Menu.Start.
            try {
                if (harmony == null) {
                    harmony = new Harmony(BrokerObjectName + ".broker");
                    harmony.Patch(AccessTools.Method(typeof(Menu), nameof(Menu.Start)),
                        postfix: new HarmonyMethod(typeof(QuickConfigBroker), nameof(OnMenuStart)));
                    harmony.Patch(AccessTools.Method(typeof(Menu), nameof(Menu.Update)),
                        prefix: new HarmonyMethod(typeof(QuickConfigBroker), nameof(OnMenuUpdate)));
                }
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfig launcher could not patch Menu: {e.Message}");
            }

            // The broker may be created AFTER OnCustomGUIAvailable has already fired -- a mod registering
            // late, from OnPrefabsRegistered say -- so do not sit waiting for the next event.
            TryBuildButtons();
        }

        public void OnDestroy() {
            GUIManager.OnCustomGUIAvailable -= OnCustomGUIAvailable;
            SynchronizationManager.OnAdminStatusChanged -= OnAdminStatusChanged;
            SynchronizationManager.OnConfigurationSynchronized -= OnConfigurationSynchronized;
            if (Instance == this) { Instance = null; }
        }

        private void OnAdminStatusChanged() {
            RefreshVisibility();
        }

        private void OnConfigurationSynchronized(object sender, EventArgs e) {
            RefreshVisibility();
        }

        private static void OnMenuStart(Menu __instance) {
            // Unity fake-null: the broker survives scene loads but the static can still be stale.
            if (Instance == null) { return; }
            Instance.EnsurePauseButton(__instance);
        }

        // Escape dismisses the mod list, and is swallowed for that one frame so the pause menu it was
        // opened from stays put -- one press closes the list, a second closes the menu. Valheim reads
        // Escape inline in Menu.Update, so there is no hook finer than skipping the method for that frame;
        // all it costs is a frame of the menu's own bookkeeping.
        private static bool OnMenuUpdate() {
            if (Instance == null || Instance.listPanel == null) { return true; }
            if (ZInput.GetKeyDown(KeyCode.Escape) == false) { return true; }
            Instance.CloseList();
            return false;
        }

        public void Update() {
            // Start scene only. There is no Menu there to run OnMenuUpdate, and nothing else is listening
            // for Escape either, so nothing needs swallowing. The Menu.instance guard keeps the two paths
            // from both firing on the same press.
            if (listPanel == null || Menu.instance != null) { return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseList(); }
        }

        private void OnCustomGUIAvailable() {
            TryBuildButtons();
        }

        private void TryBuildButtons() {
            if (GUIManager.IsHeadless() || GUIManager.Instance == null) { return; }

            // Start scene only. Jotunn rebuilds CustomGUIFront on every scene change, so without this gate
            // the in-game scene grows its own copy -- one button floating over the HUD and a second one
            // beside the pause menu's. This is the guard SLS had before the launcher became shared.
            if (FejdStartup.instance != null && GUIManager.CustomGUIFront != null) {
                mainMenuButton = EnsureCornerButton(mainMenuButton, GUIManager.CustomGUIFront.transform);
            }
            if (Menu.instance != null) { EnsurePauseButton(Menu.instance); }
            RefreshVisibility();
        }

        // Parented to m_root, which Valheim already shows and hides with the pause menu, so the button
        // follows it for free.
        private void EnsurePauseButton(Menu menu) {
            if (menu == null || menu.m_root == null) { return; }
            pauseButton = EnsureCornerButton(pauseButton, menu.m_root);
            RefreshVisibility();
        }

        // The one and only place a launcher button is created. Both call sites fire repeatedly --
        // OnCustomGUIAvailable on every scene load, the Menu.Start postfix on every menu rebuild -- so this
        // adopts whatever is already under `parent` instead of stacking a second button on top of it.
        private GameObject EnsureCornerButton(GameObject existing, Transform parent) {
            if (GUIManager.IsHeadless() || GUIManager.Instance == null || parent == null) { return existing; }
            // Also the rebuild check: m_root and CustomGUIFront are both torn down with their scene, so a
            // button whose parent is no longer this one is a corpse and must not be reused.
            if (existing != null && existing.transform.parent == parent) { return existing; }

            // Transform.Find matches inactive children too, so a button hidden by RefreshVisibility is
            // found rather than duplicated.
            Transform found = parent.Find(ButtonObjectName);
            if (found != null) { return found.gameObject; }   // already ours, listener included

            GameObject go = GUIManager.Instance.CreateButton(
                text: ConfigUI.L("Mod Config"), parent: parent,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f),
                position: new Vector2(-20f, 20f), width: ButtonW, height: ButtonH);
            go.name = ButtonObjectName;
            RectTransform rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-20f, 20f);
            go.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(OpenList);
            return go;
        }

        // Host or admin only.
        //
        // A remote non-admin editing anything is pointless: the server owns every synced config and would
        // overwrite it on the next broadcast. Hiding the button is honest about that, rather than offering
        // an editor whose changes silently evaporate.
        private static bool CanConfigure() {
            if (GUIManager.IsHeadless() || GUIManager.Instance == null) { return false; }
            if (ZNet.instance == null) { return true; }        // main menu: local files, local machine
            if (ZNet.instance.IsServer()) { return true; }     // singleplayer or listen-server host
            return SynchronizationManager.Instance != null && SynchronizationManager.Instance.PlayerIsAdmin;
        }

        private void RefreshVisibility() {
            bool show = entries.Count > 0 && CanConfigure();
            // The main menu button only ever belongs to the start scene; the FejdStartup check covers the
            // frame or two where the reference outlives it.
            if (mainMenuButton != null) { mainMenuButton.SetActive(show && FejdStartup.instance != null); }
            if (pauseButton != null) { pauseButton.SetActive(show); }
            if (show == false && listPanel != null) { CloseList(); }
        }

        private void OpenList() {
            CloseList();
            if (CanConfigure() == false) { return; }

            // One registered mod means the list would be a single button in front of the thing the user
            // actually wanted. Skip it.
            if (order.Count == 1) {
                Invoke(order[0]);
                return;
            }

            // 64 clears the title, 42 per entry, 16 of bottom padding.
            float height = Mathf.Clamp(80f + 42f * order.Count, 122f, 560f);
            listPanel = ConfigUI.CreatePanel("Configure a mod", PanelW, height, out Transform body);
            ConfigUI.AddCloseX(body, PanelW, CloseList);

            float y = 64f;
            foreach (string name in order) {
                string entry = name;   // capture per iteration
                ConfigUI.AddButton(body, 20f, y, PanelW - 40f, entry, () => {
                    CloseList();
                    Invoke(entry);
                });
                y += 42f;
            }
        }

        private void CloseList() {
            if (listPanel == null) { return; }
            UnityEngine.Object.Destroy(listPanel);
            listPanel = null;
        }

        // Every foreign callback is someone else's code reached through reflection. One mod's broken panel
        // must not take the launcher, or any other mod's entry, down with it.
        private void Invoke(string modName) {
            if (entries.TryGetValue(modName, out Action open) == false || open == null) { return; }
            try {
                open();
            } catch (Exception e) {
                Logger.LogError($"The config panel for '{modName}' failed to open: {e}");
            }
        }
    }
}
