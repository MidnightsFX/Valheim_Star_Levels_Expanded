using Jotunn.Managers;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StarLevelSystem.modules.UI {
    // The quick configure panel's prefabs (StarLevelUnity/Assets/Custom/QuickConfig): gets them out of the mod's bundle,
    // and joins what they show to the panel's code.
    //
    // A prefab holds the shape: what is where, how big, and the words as $tokens. It cannot hold Valheim's look, since the
    // game's sprites, fonts and colour blocks are not in the bundle, so Spawn puts those on through GUIManager. Code finds
    // a widget by the name it has in the prefab, so a row can be moved in Unity without touching this. A name that is not
    // there throws, and OpenPanel reports that and closes.
    internal static class QuickConfigPrefabs {
        private const string WoodpanelName = "Woodpanel";

        private static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        private static GameObject templateHolder;

        internal static GameObject Spawn(string prefab, Transform parent) {
            GameObject go = UnityEngine.Object.Instantiate(Template(prefab), parent, false);
            go.name = prefab;
            Localize(go);
            return go;
        }

        // A styled copy of the prefab for Spawn to clone, so its look is only worked out once however many rows are made of it.
        // Styling goes through GUIManager's Apply*Style, and with other mods installed a single call can take milliseconds:
        // Auga patches them and walks the stack on each one, which is what froze a heavily modded game for seconds when this
        // styled every widget it spawned.
        //
        // Kept under an inactive object in the current scene, so none of it wakes up, and so it goes when the scene does:
        // Jotunn's look differs between the main menu and in game, and the next Spawn styles a new copy for the new scene.
        private static GameObject Template(string prefab) {
            if (templates.TryGetValue(prefab, out GameObject template) && template != null) { return template; }
            GameObject asset = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>(prefab + ".prefab");
            if (asset == null) { throw new ArgumentException($"The asset bundle has no prefab {prefab}"); }
            if (templateHolder == null) {
                templateHolder = new GameObject("SLS_QC_Templates");
                templateHolder.SetActive(false);
            }
            template = UnityEngine.Object.Instantiate(asset, templateHolder.transform, false);
            Style(template);
            templates[prefab] = template;
            return template;
        }

        // Per copy, unlike the look: the player can change language between one panel and the next.
        private static void Localize(GameObject root) {
            foreach (Text text in root.GetComponentsInChildren<Text>(true)) {
                if (text.text.IndexOf('$') >= 0) { text.text = ConfigUI.L(text.text); }
            }
        }

        // --- Valheim's look -------------------------------------------------------------------------------------------

        private static void Style(GameObject root) {
            GUIManager gui = GUIManager.Instance;
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true)) {
                if (rect.name == WoodpanelName) { StylePanel(gui, rect); }
            }
            foreach (Button button in root.GetComponentsInChildren<Button>(true)) {
                Text caption = button.GetComponentInChildren<Text>(true);
                gui.ApplyButtonStyle(button, caption != null ? caption.fontSize : 16);
                ConfigUI.ClickSoundOnly(button.gameObject);
            }
            foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true)) {
                gui.ApplyToogleStyle(toggle);
            }
            foreach (InputField field in root.GetComponentsInChildren<InputField>(true)) {
                gui.ApplyInputFieldStyle(field, field.textComponent.fontSize);
            }
            foreach (Slider slider in root.GetComponentsInChildren<Slider>(true)) {
                slider.handleRect.GetComponent<Image>().sprite = gui.GetSprite("checkbox_marker");
                gui.ApplySliderStyle(slider);
            }
            foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true)) {
                if (scroll.verticalScrollbar == null) { continue; }
                scroll.verticalScrollbar.colors = gui.ValheimScrollbarHandleColorBlock;
                scroll.verticalScrollbar.handleRect.GetComponent<Image>().sprite = gui.GetSprite("UISprite");
            }
            foreach (Text text in root.GetComponentsInChildren<Text>(true)) {
                text.font = gui.AveriaSerifBold;
            }
        }

        // ApplyWoodpanelStyle centres what it is given, which every panel here already is, but it would also undo a drag:
        // this only runs before the panel has been shown.
        private static void StylePanel(GUIManager gui, RectTransform panel) {
            Vector2 size = panel.sizeDelta;
            gui.ApplyWoodpanelStyle(panel);
            panel.sizeDelta = size;
        }

        // --- finding what a prefab holds --------------------------------------------------------------------------------

        // By name, anywhere under root and whether or not it is active. Names are unique within a page.
        internal static GameObject Find(Transform root, string name) {
            Transform found = FindUnder(root, name);
            if (found == null) { throw new ArgumentException($"{root.name} has nothing named {name}"); }
            return found.gameObject;
        }

        private static Transform FindUnder(Transform root, string name) {
            foreach (Transform child in root) {
                if (child.name == name) { return child; }
                Transform found = FindUnder(child, name);
                if (found != null) { return found; }
            }
            return null;
        }

        internal static T Get<T>(Transform root, string name) where T : Component {
            T found = Find(root, name).GetComponent<T>();
            if (found == null) { throw new ArgumentException($"{name} under {root.name} has no {typeof(T).Name}"); }
            return found;
        }

        // A page's size, for what a page lays out from its data.
        internal static Vector2 PageSize(Transform page) {
            return ((RectTransform)page).sizeDelta;
        }

        // Where the rows of a scroll view named `name` go.
        internal static Transform ScrollContent(Transform root, string name) {
            return Find(root, name).transform.Find("Canvas/Scroll View/Viewport/Content");
        }

        internal static Text SetText(Transform root, string name, string words) {
            Text text = Get<Text>(root, name);
            text.text = ConfigUI.L(words);
            return text;
        }

        // --- joining widgets to the panel's state -------------------------------------------------------------------------

        internal static GameObject BindButton(Transform root, string name, UnityAction onClick) {
            GameObject go = Find(root, name);
            if (onClick != null) { go.GetComponent<Button>().onClick.AddListener(onClick); }
            return go;
        }

        internal static Toggle BindToggle(Toggle toggle, bool value, Action<bool> onChange) {
            toggle.isOn = value;
            if (onChange != null) { toggle.onValueChanged.AddListener(b => onChange(b)); }
            return toggle;
        }

        internal static Toggle BindToggle(Transform root, string name, bool value, Action<bool> onChange) {
            return BindToggle(Get<Toggle>(root, name), value, onChange);
        }

        // The row, for a row made of a Label and a Toggle.
        internal static GameObject BindToggleRow(Transform root, string name, bool value, Action<bool> onChange) {
            GameObject row = Find(root, name);
            BindToggle(row.transform, "Toggle", value, onChange);
            return row;
        }

        // A row made of a Label, a Slider and a Value box, bound both ways; SetTextWithoutNotify on the reflect path is what
        // stops the two from driving each other in a loop. The range is the prefab's unless it depends on the data.
        internal static GameObject BindSliderRow(Transform root, string name, float value, Action<float> onChange, float? min = null, float? max = null, bool? whole = null) {
            return BindSliderRow(Find(root, name), value, onChange, min, max, whole);
        }

        internal static GameObject BindSliderRow(GameObject row, float value, Action<float> onChange, float? min = null, float? max = null, bool? whole = null) {
            Slider slider = Get<Slider>(row.transform, "Slider");
            InputField box = Get<InputField>(row.transform, "Value");
            if (min.HasValue) { slider.minValue = min.Value; }
            if (max.HasValue) { slider.maxValue = max.Value; }
            if (whole.HasValue) {
                slider.wholeNumbers = whole.Value;
                box.contentType = whole.Value ? InputField.ContentType.IntegerNumber : InputField.ContentType.DecimalNumber;
            }
            bool wholeNumbers = slider.wholeNumbers;
            slider.value = Mathf.Clamp(value, slider.minValue, slider.maxValue);
            box.SetTextWithoutNotify(ConfigUI.Fmt(slider.value, wholeNumbers));

            slider.onValueChanged.AddListener(v => {
                if (wholeNumbers) { v = Mathf.Round(v); }
                box.SetTextWithoutNotify(ConfigUI.Fmt(v, wholeNumbers));
                onChange?.Invoke(v);
            });

            // Commit typed values on enter or focus loss: unparseable text snaps back to the slider, then clamp, then
            // normalise what is displayed.
            box.onEndEdit.AddListener(str => {
                // Invariant, with a comma taken as the decimal point: Unity's decimal field accepts both, and the current
                // culture read "1.5" on a German system (or "1,5" on an English one) as a grouped 15.
                if (float.TryParse(str.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) == false) { v = slider.value; }
                v = Mathf.Clamp(v, slider.minValue, slider.maxValue);
                if (wholeNumbers) { v = Mathf.Round(v); }
                box.SetTextWithoutNotify(ConfigUI.Fmt(v, wholeNumbers));
                if (slider.value != v) { slider.value = v; }   // the slider listener fires onChange
                else { onChange?.Invoke(v); }
            });
            return row;
        }

        // onEndEdit rather than onValueChanged: committing per keystroke would re-validate the whole document on every letter.
        internal static InputField BindField(InputField field, string value, Action<string> onCommit, int charLimit = 0) {
            if (charLimit > 0) { field.characterLimit = charLimit; }
            field.SetTextWithoutNotify(value ?? "");
            if (onCommit != null) { field.onEndEdit.AddListener(s => onCommit(s)); }
            return field;
        }

        internal static InputField BindField(Transform root, string name, string value, Action<string> onCommit, int charLimit = 0) {
            return BindField(Get<InputField>(root, name), value, onCommit, charLimit);
        }

        // The row, for a row made of a Label and a Field.
        internal static GameObject BindFieldRow(Transform root, string name, string value, Action<string> onCommit, int charLimit = 0) {
            GameObject row = Find(root, name);
            BindField(row.transform, "Field", value, onCommit, charLimit);
            return row;
        }

        // A row made of a Label and a Cycle button that steps through the options. The member name is not localized: it is
        // the token an admin types into the yaml file, so a translated form would teach them the wrong word.
        internal static GameObject BindEnumRow(Transform root, string name, string[] options, int currentIndex, Action<int> onChange) {
            GameObject row = Find(root, name);
            Button button = Get<Button>(row.transform, "Cycle");
            Text caption = button.GetComponentInChildren<Text>(true);
            int idx = Mathf.Clamp(currentIndex, 0, Math.Max(0, options.Length - 1));
            caption.text = options.Length > 0 ? options[idx] : "";
            button.onClick.AddListener(() => {
                if (options.Length == 0) { return; }
                idx = (idx + 1) % options.Length;
                caption.text = options[idx];
                onChange?.Invoke(idx);
            });
            return row;
        }
    }
}
