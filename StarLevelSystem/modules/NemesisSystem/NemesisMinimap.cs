using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
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
        // Pins received before the minimap has loaded its map data; flushed from OnMapReady.
        private static readonly Dictionary<string, NemesisBossPin> pending = new Dictionary<string, NemesisBossPin>();

        private static Minimap.PinType customPinType = Minimap.PinType.None;
        private static bool spriteRegistered = false;
        // The Minimap the pin type was registered on (or registration was tried and failed for). Every
        // world load builds a fresh Minimap with fresh m_visibleIconTypes/m_icons, so the type is appended
        // again there and can get a different number than last world.
        private static Minimap registeredFor;

        // Hooked to MinimapManager.OnVanillaMapDataLoaded: register the sprite and flush any buffered pins.
        internal static void OnMapReady() {
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
            // Buffer until LoadMapData, not merely until Minimap.instance exists. A joining client receives
            // the initial pin set before the server's PeerInfo, so the Minimap is alive but has not loaded
            // yet; SetMapData then runs ClearPins for any world the player has visited before, and pins
            // added here were wiped with no trace. Buffering first also means ShowMapPin is read after the
            // server's NemesisSettings sync, which Jotunn delivers after this pin sync.
            if (!MinimapOverlayFog.IsMapDataLoaded()) { pending[pin.Id] = pin; return; }
            if (NemesisSystemData.SLE_Nemesis_Settings.RemoteSpawning.ShowMapPin == false) { return; }

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
            Minimap.PinData icon = Minimap.instance.AddPin(pin.Position, type, label, save: false, isChecked: false);
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

        // Register a custom pin sprite as a new PinType (mirrors EpicLoot's MinimapController). Runs once
        // per Minimap instance.
        private static void EnsureSpriteRegistered() {
            if (Minimap.instance == null || registeredFor == Minimap.instance) { return; }
            registeredFor = Minimap.instance;
            spriteRegistered = false;
            customPinType = Minimap.PinType.None;

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
            if (mm.m_visibleIconTypes == null) { return; }
            // Take the next slot at the end, as TargetPortal, Jewelcrafting, VentureValheim and EpicLoot do.
            // A fixed number lands on another mod's type once enough are appended before this lazy
            // registration runs: the two then share one filter toggle and our sprite is never added. The
            // number can change between worlds, which is why Nemesis pins are never saved with the map.
            int idx = mm.m_visibleIconTypes.Length;
            Array.Resize(ref mm.m_visibleIconTypes, idx + 1);
            mm.m_visibleIconTypes[idx] = true;
            customPinType = (Minimap.PinType)idx;
            mm.m_icons.Add(new Minimap.SpriteData { m_name = customPinType, m_icon = sprite });
            spriteRegistered = true;
        }
    }
}
