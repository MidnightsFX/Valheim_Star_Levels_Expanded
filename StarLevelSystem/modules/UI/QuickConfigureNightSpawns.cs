using BepInEx.Configuration;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StarLevelSystem.modules.UI {
    // The Night Spawns page of the quick configure panel: one switch per boss for the creatures that start roaming at night
    // once that boss is defeated. The switches are ConfigEntries; the spawns are stopped in modules/BossNightSpawns.
    internal static partial class QuickConfigureTool {

        // Biomes in the order the descriptions list them, earliest progression first.
        private static readonly Heightmap.Biome[] BiomeListOrder = {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain, Heightmap.Biome.Plains,
            Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean,
        };

        private static void BuildNightSpawnsPage(Transform parent) {
            const float ColWidth = 660f;
            const float EntryH = 56f;   // the name, and room for two lines of creatures under it
            const float StartY = 4f;

            GameObject header = ConfigUI.AddHeaderRow(parent, PageW, "Night Spawns", TextAnchor.MiddleCenter);
            ConfigUI.PositionRow(header, 0f, StartY);
            GameObject intro = ConfigUI.AddTextRow(parent, PageW, 40f, "$sls_cfg_night_spawns_intro", 13, GUIManager.Instance.ValheimBeige, TextAnchor.UpperCenter);
            ConfigUI.PositionRow(intro, 0f, StartY + RowHeight + RowGap);

            List<GameObject> rows = new List<GameObject>();
            foreach (string bossKey in LevelSystemData.VanillaBossKeyOrder) {
                ConfigEntry<bool> setting = BossNightSpawns.ToggleFor(bossKey);
                if (setting == null || staged.nightSpawnsOn.TryGetValue(bossKey, out bool on) == false) { continue; }
                string key = bossKey;   // capture for the closure
                string description = DescribeNightSpawns(key, out bool hasSpawns);
                rows.Add(WithTip(AddNightSpawnRow(parent, ColWidth, EntryH, BossKeyName(key), description, on, hasSpawns, v => staged.nightSpawnsOn[key] = v), Tip(setting)));
            }
            ConfigUI.LayoutColumn(rows, (PageW - ColWidth) * 0.5f, StartY + RowHeight + RowGap + 40f + 10f);
        }

        // A boss's switch, with the creatures it lets out at night under its name. A boss with none is greyed out and its
        // switch locked: there is nothing for it to turn off, and its setting keeps whatever value it had.
        private static GameObject AddNightSpawnRow(Transform parent, float width, float height, string label, string description, bool value, bool hasSpawns, Action<bool> onChange) {
            const float ToggleSize = 26f;
            const float TextX = ToggleSize + 10f;
            const float LabelH = 24f;
            GameObject row = ConfigUI.NewRow(parent, width, height);
            ConfigUI.AddToggle(row.transform, 0f, 1f, ToggleSize, value, onChange);
            ConfigUI.AddText(row.transform, TextX, 0f, width - TextX, LabelH, label, 16, TextAnchor.MiddleLeft, hasSpawns ? GUIManager.Instance.ValheimOrange : InactiveColor);
            ConfigUI.AddText(row.transform, TextX, LabelH, width - TextX, height - LabelH, description, 13, TextAnchor.UpperLeft, hasSpawns ? GUIManager.Instance.ValheimBeige : InactiveColor);
            if (hasSpawns == false) {
                // Locks the switch and fades it with the text. Raycasts still land, so the row keeps its tooltip.
                CanvasGroup group = row.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.alpha = 0.6f;
            }
            return row;
        }

        // The creatures a boss's night spawns bring: from this world's spawn lists when one is loaded, and from vanilla's on
        // the main menu, where there are no lists to read. Creatures that share the same biomes are listed together.
        private static string DescribeNightSpawns(string bossKey, out bool hasSpawns) {
            if (BossNightSpawns.TryGetLiveSpawns(bossKey, out List<SpawnSystem.SpawnData> spawns) == false) {
                string vanilla = BossNightSpawns.VanillaSpawns.TryGetValue(bossKey, out string known) ? known : "";
                hasSpawns = vanilla.Length > 0;
                return hasSpawns ? $"In vanilla: {vanilla}." : "No night spawns in vanilla.";
            }
            hasSpawns = spawns.Count > 0;
            if (hasSpawns == false) { return "No night spawns in this world."; }

            Dictionary<string, Heightmap.Biome> biomesByCreature = new Dictionary<string, Heightmap.Biome>();
            foreach (SpawnSystem.SpawnData spawn in spawns) {
                string name = NightSpawnCreatureName(spawn.m_prefab);
                biomesByCreature.TryGetValue(name, out Heightmap.Biome biomes);
                biomesByCreature[name] = biomes | spawn.m_biome;
            }
            IEnumerable<string> groups = biomesByCreature.GroupBy(kv => kv.Value, kv => kv.Key)
                .Select(group => $"{JoinWithAnd(group.ToList())} in {JoinWithAnd(BiomeListOrder.Where(b => (group.Key & b) != 0).Select(BiomeName).ToList())}");
            return string.Join("; ", groups.ToArray()) + ".";
        }

        // The name the creature carries in game, or its prefab name when the language file has no translation for it.
        private static string NightSpawnCreatureName(GameObject prefab) {
            Character character = prefab != null ? prefab.GetComponent<Character>() : null;
            if (character == null) { return PrettyPrefab(prefab != null ? prefab.name : null); }
            string localized = ConfigUI.L(character.m_name);
            return string.IsNullOrEmpty(localized) || localized.StartsWith("[") || localized.Contains("$") ? PrettyPrefab(prefab.name) : localized;
        }

        // "A", "A and B", "A, B and C".
        private static string JoinWithAnd(List<string> parts) {
            if (parts.Count == 0) { return "nowhere"; }
            if (parts.Count == 1) { return parts[0]; }
            return string.Join(", ", parts.Take(parts.Count - 1).ToArray()) + " and " + parts[parts.Count - 1];
        }

        private static Dictionary<string, bool> SnapshotNightSpawns() {
            Dictionary<string, bool> on = new Dictionary<string, bool>();
            foreach (string bossKey in LevelSystemData.VanillaBossKeyOrder) {
                ConfigEntry<bool> setting = BossNightSpawns.ToggleFor(bossKey);
                if (setting != null) { on[bossKey] = setting.Value; }
            }
            return on;
        }

        private static bool NightSpawnsMatch(Dictionary<string, bool> a, Dictionary<string, bool> b) {
            if (a.Count != b.Count) { return false; }
            foreach (KeyValuePair<string, bool> entry in a) {
                if (b.TryGetValue(entry.Key, out bool other) == false || other != entry.Value) { return false; }
            }
            return true;
        }

        private static void ResetNightSpawnsPage() {
            foreach (string bossKey in staged.nightSpawnsOn.Keys.ToList()) {
                ConfigEntry<bool> setting = BossNightSpawns.ToggleFor(bossKey);
                if (setting != null) { staged.nightSpawnsOn[bossKey] = DefaultOf(setting); }
            }
        }

        private static void SaveNightSpawns() {
            foreach (KeyValuePair<string, bool> entry in staged.nightSpawnsOn) {
                ConfigEntry<bool> setting = BossNightSpawns.ToggleFor(entry.Key);
                if (setting != null) { setting.Value = entry.Value; }
            }
        }
    }
}
