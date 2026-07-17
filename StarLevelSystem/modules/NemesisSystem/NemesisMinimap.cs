using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    // Client-side registry of the shared remote-boss map pins. Pins arrive from the server (add/remove RPCs
    // and the initial full-set sync). Registers a custom pin sprite from the asset bundle when configured,
    // otherwise falls back to a vanilla pin type.
    internal static class NemesisMinimap {
        // Each boss owns a list of pins: the red circular EventArea overlay plus the boss icon.
        private static readonly Dictionary<string, List<Minimap.PinData>> pins = new Dictionary<string, List<Minimap.PinData>>();
        // Pins received before the minimap exists; flushed once the map data has loaded.
        private static readonly Dictionary<string, NemesisBossPin> pending = new Dictionary<string, NemesisBossPin>();

        private static Minimap.PinType customPinType = Minimap.PinType.None;
        private static bool spriteRegistered = false;
        private static bool triedRegister = false;

        // Hooked to MinimapManager.OnVanillaMapDataLoaded: register the sprite and flush any buffered pins.
        internal static void OnMapReady() {
            triedRegister = false;
            spriteRegistered = false;
            EnsureSpriteRegistered();
            if (pending.Count > 0) {
                foreach (NemesisBossPin pin in new List<NemesisBossPin>(pending.Values)) {
                    AddOrUpdatePin(pin);
                }
                pending.Clear();
            }
        }

        public static void AddOrUpdatePin(NemesisBossPin pin) {
            if (pin == null || string.IsNullOrEmpty(pin.Id)) { return; }
            if (NemesisSystemData.SLE_Nemesis_Settings.RemoteSpawning.ShowMapPin == false) { return; }
            if (Minimap.instance == null) { pending[pin.Id] = pin; return; }

            RemovePin(pin.Id); // avoid duplicate markers for the same boss
            RemoteNemesisSpawnSettings settings = NemesisSystemData.SLE_Nemesis_Settings.RemoteSpawning;
            List<Minimap.PinData> created = new List<Minimap.PinData>();

            // Always draw the red circular EventArea overlay under the boss icon.
            Minimap.PinData area = Minimap.instance.AddPin(pin.Position, Minimap.PinType.EventArea, "", false, false);
            area.m_worldSize = settings.MapPinAreaRadius * 2f;
            created.Add(area);

            // The boss icon pin (custom sprite or fallback pin type).
            Minimap.PinType type = ResolvePinType();
            string label = settings.PinShowsBossName && Localization.instance != null
                ? Localization.instance.Localize(pin.Name ?? "")
                : "";
            Minimap.PinData icon = Minimap.instance.AddPin(pin.Position, type, label, false, false);
            created.Add(icon);

            pins[pin.Id] = created;
        }

        public static void RemovePin(string id) {
            if (string.IsNullOrEmpty(id)) { return; }
            pending.Remove(id);
            if (pins.TryGetValue(id, out List<Minimap.PinData> pinList)) {
                if (Minimap.instance != null) {
                    foreach (Minimap.PinData pd in pinList) {
                        if (pd != null) { Minimap.instance.RemovePin(pd); }
                    }
                }
                pins.Remove(id);
            }
        }

        public static void ClearAll() {
            if (Minimap.instance != null) {
                foreach (List<Minimap.PinData> pinList in pins.Values) {
                    foreach (Minimap.PinData pd in pinList) {
                        if (pd != null) { Minimap.instance.RemovePin(pd); }
                    }
                }
            }
            pins.Clear();
            pending.Clear();
        }

        private static Minimap.PinType ResolvePinType() {
            EnsureSpriteRegistered();
            return spriteRegistered ? customPinType : NemesisSystemData.SLE_Nemesis_Settings.RemoteSpawning.FallbackPinType;
        }

        // Register a custom pin sprite as a new PinType (mirrors EpicLoot's MinimapController). Runs once.
        private static void EnsureSpriteRegistered() {
            if (triedRegister) { return; }
            if (Minimap.instance == null) { return; }
            triedRegister = true;

            string assetName = NemesisSystemData.SLE_Nemesis_Settings.RemoteSpawning.MapPinSpriteAsset;
            if (string.IsNullOrEmpty(assetName)) { return; }

            Sprite sprite = null;
            if (StarLevelSystem.EmbeddedResourceBundle != null) {
                try { sprite = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>(assetName); }
                catch (Exception ex) { Logger.LogWarning($"Failed loading Nemesis pin sprite '{assetName}': {ex.Message}"); }
            }
            if (sprite == null) {
                Logger.LogWarning($"Nemesis map pin sprite '{assetName}' not found; using the fallback pin type.");
                return;
            }

            Minimap mm = Minimap.instance;
            int idx = Enum.GetValues(typeof(Minimap.PinType)).Length + 4; // offset to avoid colliding with other mods' custom types
            customPinType = (Minimap.PinType)idx;

            if (mm.m_visibleIconTypes == null) { return; }
            if (mm.m_visibleIconTypes.Length <= idx) {
                bool[] resized = new bool[idx + 1];
                Array.Copy(mm.m_visibleIconTypes, resized, mm.m_visibleIconTypes.Length);
                for (int i = mm.m_visibleIconTypes.Length; i < resized.Length; i++) { resized[i] = true; }
                mm.m_visibleIconTypes = resized;
            }
            if (!mm.m_icons.Exists(x => x.m_name == customPinType)) {
                mm.m_icons.Add(new Minimap.SpriteData { m_name = customPinType, m_icon = sprite });
            }
            spriteRegistered = true;
        }
    }
}
