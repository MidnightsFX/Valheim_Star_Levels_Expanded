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
        internal const int ContractVersion = 1;

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
                }
            } catch (Exception e) {
                Logger.LogWarning($"QuickConfig launcher could not patch Menu.Start: {e.Message}");
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

        private void OnCustomGUIAvailable() {
            TryBuildButtons();
        }

        private void TryBuildButtons() {
            if (GUIManager.IsHeadless() || GUIManager.Instance == null) { return; }

            if (mainMenuButton == null && GUIManager.CustomGUIFront != null) {
                mainMenuButton = CreateCornerButton(GUIManager.CustomGUIFront.transform);
            }
            if (Menu.instance != null) { EnsurePauseButton(Menu.instance); }
            RefreshVisibility();
        }

        private void EnsurePauseButton(Menu menu) {
            if (GUIManager.IsHeadless() || GUIManager.Instance == null) { return; }
            if (menu == null || menu.m_root == null) { return; }
            // Rebuild when the menu has been recreated under us -- m_root is torn down with the scene.
            if (pauseButton != null && pauseButton.transform.parent == menu.m_root) {
                RefreshVisibility();
                return;
            }
            // Parented to m_root, which Valheim already shows and hides with the pause menu, so the button
            // follows it for free.
            pauseButton = CreateCornerButton(menu.m_root);
            RefreshVisibility();
        }

        private GameObject CreateCornerButton(Transform parent) {
            GameObject go = GUIManager.Instance.CreateButton(
                text: ConfigUI.L("Mod Config"), parent: parent,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f),
                position: new Vector2(-20f, 20f), width: ButtonW, height: ButtonH);
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
            if (mainMenuButton != null) { mainMenuButton.SetActive(show); }
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

            float height = Mathf.Clamp(96f + 42f * order.Count, 138f, 560f);
            listPanel = ConfigUI.CreatePanel("Configure a mod", 320f, height, out Transform body);

            float y = 64f;
            foreach (string name in order) {
                string entry = name;   // capture per iteration
                ConfigUI.AddButton(body, 20f, y, 280f, entry, () => {
                    CloseList();
                    Invoke(entry);
                });
                y += 42f;
            }
            ConfigUI.AddButton(body, 20f, height - 52f, 280f, "Close", CloseList, 34f);
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
