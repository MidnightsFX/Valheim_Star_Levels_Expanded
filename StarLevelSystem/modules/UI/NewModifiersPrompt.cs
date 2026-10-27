using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.modules.Modifiers;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace StarLevelSystem.modules.UI {

    // The question ModifierConfigUpdate asks when Modifiers.yaml is missing built-in modifiers a release added: add them
    // with their default settings, or keep the file's own list. Opens from the startup popup queue every mod carrying
    // common/ConfigUI shares, so it waits for the main menu and never lands on the same frame as another mod's welcome.
    //
    // Either button stamps the file's version, which is what makes this a one-time question. Closing the game without
    // answering leaves the file as it was, and it is asked again next launch.
    internal static class NewModifiersPrompt {
        internal const string QueueKey = "StarLevelSystem.NewModifiers";
        private const float W = 520f;
        private const float H = 270f;
        private const float ButtonW = 180f;

        private static GameObject overlay;

        internal static void Queue() {
            ConfigUIStartupPopups.Enqueue(QueueKey, ConfigUIStartupPopups.OrderNotice, Open, () => overlay != null);
        }

        internal static void Cancel() {
            ConfigUIStartupPopups.Cancel(QueueKey);
        }

        // Declines when there is nothing left to ask, which happens when the file was edited while this waited its turn.
        private static bool Open() {
            if (overlay != null || GUIManager.Instance == null || GUIManager.CustomGUIFront == null) { return false; }
            List<string> names = ModifierConfigUpdate.MissingOnDisk();
            if (names.Count == 0) { return false; }

            overlay = ConfigUI.NewUI("SLSNewModifiersPrompt", GUIManager.CustomGUIFront.transform, typeof(Image));
            RectTransform overlayRT = (RectTransform)overlay.transform;
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            // The guard is also what the popup queue looks for to know a kit panel is open.
            overlay.AddComponent<ConfigUI.ConfigUIInputGuard>().Hold();

            GameObject box = GUIManager.Instance.CreateWoodpanel(
                parent: overlay.transform,
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(0f, 0f), width: W, height: H, draggable: false);

            ConfigUI.AddText(box.transform, 0f, 16f, W, 30f, ConfigUI.L("$sls_newmods_title", "New creature modifiers"), 20,
                TextAnchor.MiddleCenter, GUIManager.Instance.ValheimYellow);
            ConfigUI.AddText(box.transform, 24f, 56f, W - 48f, 130f, Body(names), 14, TextAnchor.UpperCenter);

            float y = H - 64f;
            float gap = (W - 2 * ButtonW) / 3f;
            ConfigUI.AddButton(box.transform, gap, y, ButtonW, ConfigUI.L("$sls_newmods_keep", "Keep my list"), () => Answer(false), 36f);
            ConfigUI.AddButton(box.transform, 2 * gap + ButtonW, y, ButtonW, ConfigUI.L("$sls_newmods_add", "Add them"), () => Answer(true), 36f);
            return overlay != null;
        }

        // No click-outside or Escape to dismiss: either answer is final, so it takes a button.
        private static void Answer(bool add) {
            Close();
            ModifierConfigUpdate.Resolve(add);
        }

        private static void Close() {
            if (overlay != null) { Object.Destroy(overlay); }
            overlay = null;
        }

        // The names are the Modifiers.yaml keys, split into words ("EarthCaller" reads "Earth Caller"), because that is
        // what an admin will find in the file afterwards.
        private static string Body(List<string> names) {
            string list = string.Join(", ", names.Select(n => Regex.Replace(n, "(?<=[a-z])(?=[A-Z])", " ")));
            string text = Localization.instance != null ? Localization.instance.Localize("$sls_newmods_body", list) : null;
            if (string.IsNullOrEmpty(text) || text.StartsWith("[")) {
                text = "This version of Star Levels adds creature modifiers your Modifiers.yaml does not list yet, so they " +
                    "never appear:\n\n" + list + "\n\nAdd them with their default settings? Either way, you will not be asked again.";
            }
            return text;
        }
    }
}
