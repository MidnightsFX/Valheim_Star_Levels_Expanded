using BepInEx.Configuration;
using Jotunn.Managers;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StarLevelSystem.modules.UI {
    // Hover tooltips for the quick configure panel.
    //
    // Hand-built rather than vanilla's UITooltip: that one draws through a prefab the game wires up for its own GUI,
    // and this panel also opens on the main menu. One shared box is moved around and re-filled instead of one per row.
    //
    // The text comes from what already documents each setting: a BepInEx entry's ConfigDescription (the same line an
    // admin reads in the .cfg) or a [Description] on a YAML data class, so the panel and the files cannot drift apart.
    // Another language reads a translation of that text where its language file has one (see Translated); the heading
    // stays the setting's own name, since that is the word an admin types into the file.
    internal static class QuickConfigTooltip {
        private const float MaxWidth = 360f;
        private const float Padding = 10f;
        private const float CursorGap = 18f;

        private static GameObject root;
        private static Text label;
        private static RectTransform rect;
        private static TooltipTarget active;

        // Attaches a tooltip and returns the target, so a row can be wrapped where it is built.
        internal static GameObject Attach(GameObject target, string text) {
            if (target == null || string.IsNullOrEmpty(text)) { return target; }
            // Pointer events need something that takes raycasts, and a laid-out row is a bare RectTransform. The catcher
            // sits behind the row's own widgets, which are raycast first, so it never steals a click from them.
            if (target.GetComponent<Graphic>() == null) {
                Image catcher = target.AddComponent<Image>();
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;
            }
            TooltipTarget tip = target.GetComponent<TooltipTarget>();
            if (tip == null) { tip = target.AddComponent<TooltipTarget>(); }
            tip.Text = text;
            return target;
        }

        internal static Text Attach(Text target, string text) {
            if (target != null) { Attach(target.gameObject, text); }
            return target;
        }

        // --- text sources ---------------------------------------------------------------------------

        // A BepInEx setting: its key, and the description written for the config file.
        internal static string Of(ConfigEntryBase entry) {
            if (entry == null) { return ""; }
            string key = entry.Definition?.Key;
            return Compose(key, Translated("$sls_cfg_desc_" + key, entry.Description?.Description));
        }

        // A YAML member, described by its [Description] attribute where the data class carries one. The fallback covers
        // the many members that are documented by a comment in the class or by the file header instead; it is written
        // for the panel, so it is a token.
        internal static string OfYaml(Type type, string member, string fallback = null) {
            string described = DescriptionOf(type, member);
            if (string.IsNullOrEmpty(described)) { return Compose(member, ConfigUI.L(fallback)); }
            return Compose(member, Translated($"$sls_cfg_desc_{type.Name}_{member}", described));
        }

        // Text written for the panel: either part may be a token.
        internal static string Text(string key, string description) {
            return Compose(ConfigUI.L(key), ConfigUI.L(description));
        }

        // A setting's description as written, or the language file's translation of it. English always reads the
        // description itself: English.json carries a copy of each one only for translators, and the cached copy of that
        // file wins over the shipped one, so reading it here would keep showing a description long after it was reworded.
        private static string Translated(string token, string written) {
            if (string.IsNullOrEmpty(written) || Localization.instance == null) { return written; }
            if (Localization.instance.GetSelectedLanguage() == "English") { return written; }
            string translated = Localization.instance.Localize(token);
            // Valheim answers with "[token]" for a token it has no translation for.
            return string.IsNullOrEmpty(translated) || translated.StartsWith("[") ? written : translated;
        }

        private static readonly Dictionary<string, string> yamlDescriptions = new Dictionary<string, string>();

        private static string DescriptionOf(Type type, string member) {
            if (type == null || string.IsNullOrEmpty(member)) { return null; }
            string cacheKey = type.FullName + "." + member;
            if (yamlDescriptions.TryGetValue(cacheKey, out string cached)) { return cached; }

            string found = null;
            try {
                MemberInfo[] members = type.GetMember(member, BindingFlags.Public | BindingFlags.Instance);
                if (members.Length > 0) {
                    DescriptionAttribute attribute = members[0].GetCustomAttribute<DescriptionAttribute>();
                    found = attribute?.Description;
                }
            } catch (Exception e) {
                Logger.LogDebug($"Could not read the description of {cacheKey}: {e.Message}");
            }
            yamlDescriptions[cacheKey] = found;
            return found;
        }

        private static string Compose(string key, string description) {
            if (string.IsNullOrEmpty(description)) { return string.IsNullOrEmpty(key) ? "" : key; }
            if (string.IsNullOrEmpty(key)) { return description; }
            return $"<color=#FFCB6B>{key}</color>\n{description}";
        }

        // --- the box --------------------------------------------------------------------------------

        internal static void Show(TooltipTarget target) {
            if (target == null || string.IsNullOrEmpty(target.Text)) { return; }
            EnsureRoot();
            if (root == null) { return; }
            active = target;
            label.text = target.Text;
            Layout();
            root.SetActive(true);
            // Drawn over the panel, which shares this parent.
            root.transform.SetAsLastSibling();
            Reposition();
        }

        internal static void Hide(TooltipTarget target) {
            if (active != target) { return; }
            active = null;
            if (root != null) { root.SetActive(false); }
        }

        // Called when the panel closes: CustomGUIFront is rebuilt on every scene change, so nothing here outlives it.
        internal static void Close() {
            active = null;
            if (root != null) { UnityEngine.Object.Destroy(root); }
            root = null;
            label = null;
            rect = null;
        }

        private static void EnsureRoot() {
            if (root != null) { return; }
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) { return; }

            root = ConfigUI.NewUI("SLSQuickConfigTooltip", GUIManager.CustomGUIFront.transform, typeof(Image));
            rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);

            Image background = root.GetComponent<Image>();
            background.color = new Color(0.06f, 0.05f, 0.04f, 0.96f);
            // Never a raycast target: a box under the cursor would count as leaving the row it describes, and the two
            // would flicker against each other.
            background.raycastTarget = false;

            label = ConfigUI.AddText(root.transform, Padding, Padding, MaxWidth - 2 * Padding, 20f, "", 14,
                TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);
            label.raycastTarget = false;

            root.AddComponent<TooltipFollower>();
            root.SetActive(false);
        }

        private static void Layout() {
            float width = Mathf.Min(MaxWidth - 2 * Padding, label.preferredWidth);
            label.rectTransform.sizeDelta = new Vector2(width, 20f);
            float height = label.preferredHeight;
            label.rectTransform.sizeDelta = new Vector2(width, height);
            rect.sizeDelta = new Vector2(width + 2 * Padding, height + 2 * Padding);
        }

        // Follows the cursor, kept inside the parent canvas so a row near an edge does not push its tooltip off screen.
        private static void Reposition() {
            if (root == null || root.activeSelf == false) { return; }
            RectTransform parent = root.transform.parent as RectTransform;
            if (parent == null) { return; }

            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Input.mousePosition, camera, out Vector2 local) == false) {
                return;
            }

            Vector2 size = rect.sizeDelta;
            Rect bounds = parent.rect;
            float x = local.x + CursorGap;
            float y = local.y - CursorGap;
            // Flip to the other side of the cursor rather than sliding, so the box never sits on top of what it describes.
            if (x + size.x > bounds.xMax) { x = local.x - CursorGap - size.x; }
            if (y - size.y < bounds.yMin) { y = local.y + CursorGap + size.y; }
            rect.anchoredPosition = new Vector2(
                Mathf.Clamp(x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x)),
                Mathf.Clamp(y, Mathf.Min(bounds.yMax, bounds.yMin + size.y), bounds.yMax));
        }

        internal class TooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
            internal string Text;

            public void OnPointerEnter(PointerEventData eventData) {
                Show(this);
            }

            public void OnPointerExit(PointerEventData eventData) {
                Hide(this);
            }

            // A collapsed raid's spawn rows are switched off while hovered, and no exit event follows.
            public void OnDisable() {
                Hide(this);
            }
        }

        private class TooltipFollower : MonoBehaviour {
            public void Update() {
                Reposition();
            }
        }
    }
}
