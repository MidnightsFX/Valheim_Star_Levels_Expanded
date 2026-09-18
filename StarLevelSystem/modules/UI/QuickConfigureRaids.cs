using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    // The Raids page of the quick configure panel: the global raid settings, which raids are enabled, and per creature
    // in a raid how many arrive each wave, how many may be alive at once, and how long a wave lasts. Spawn chances,
    // levels, modifiers and activation keys stay in RaidSettings.yaml.
    internal static partial class QuickConfigureTool {

        private const int MaxSpawnGroupSize = 50;
        private const int MaxSpawnAlive = 999;
        private const float MinSpawnInterval = 1f;
        private const float MaxSpawnInterval = 3600f;

        // One creature line of one raid. Keyed by position in the raid list rather than by name, because nothing stops
        // two raids sharing a name, and PrefabName is kept so a file that changed underneath us is not written blind.
        private class StagedRaidSpawn {
            internal string PrefabName;
            internal int GroupSize;
            internal int MaxAlive;
            internal float Interval;

            internal bool SameAs(StagedRaidSpawn other) {
                return other != null && GroupSize == other.GroupSize && MaxAlive == other.MaxAlive && Interval == other.Interval;
            }
        }

        private static string RaidSpawnKey(int raidIndex, int spawnIndex) => $"{raidIndex}:{spawnIndex}";

        private static Dictionary<string, StagedRaidSpawn> SnapshotRaidSpawns(RaidConfiguration source) {
            Dictionary<string, StagedRaidSpawn> spawns = new Dictionary<string, StagedRaidSpawn>();
            if (source?.Raids == null) { return spawns; }
            for (int raidIndex = 0; raidIndex < source.Raids.Count; raidIndex++) {
                List<RaidSpawnEntry> entries = source.Raids[raidIndex]?.Spawns;
                if (entries == null) { continue; }
                for (int spawnIndex = 0; spawnIndex < entries.Count; spawnIndex++) {
                    RaidSpawnEntry entry = entries[spawnIndex];
                    if (entry == null) { continue; }
                    spawns[RaidSpawnKey(raidIndex, spawnIndex)] = new StagedRaidSpawn {
                        PrefabName = entry.PrefabName,
                        GroupSize = entry.SpawnGroupSize,
                        MaxAlive = entry.MaxSpawned,
                        Interval = entry.SpawnInterval,
                    };
                }
            }
            return spawns;
        }

        private static bool RaidSpawnsMatch(Dictionary<string, StagedRaidSpawn> a, Dictionary<string, StagedRaidSpawn> b) {
            if (a.Count != b.Count) { return false; }
            foreach (KeyValuePair<string, StagedRaidSpawn> spawn in a) {
                if (b.TryGetValue(spawn.Key, out StagedRaidSpawn other) == false || spawn.Value.SameAs(other) == false) { return false; }
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------------
        //  Page
        // ------------------------------------------------------------------------------------------------

        private static void BuildRaidsPage(Transform parent) {
            const float LeftColWidth = 430f;
            const float LabelWidth = 235f, SliderWidth = 130f, ValueWidth = 56f;
            const float ToggleLabelWidth = 300f;
            const float StartY = 4f;

            // Full-width header + intro across the top.
            GameObject header = ConfigUI.AddHeaderRow(parent, PageW, "Raids", TextAnchor.MiddleCenter);
            ConfigUI.PositionRow(header, 0f, StartY);
            GameObject intro = ConfigUI.AddTextRow(parent, PageW, 40f, "$sls_cfg_raids_intro", 13, GUIManager.Instance.ValheimBeige, TextAnchor.UpperCenter);
            ConfigUI.PositionRow(intro, 0f, StartY + RowHeight + RowGap);
            float colStartY = StartY + RowHeight + RowGap + 40f + 8f;

            // Left column - global raid settings.
            List<GameObject> left = new List<GameObject> {
                WithTip(ConfigUI.AddToggleRow(parent, LeftColWidth, ToggleLabelWidth, "Enable SLS Raids", staged.enableSlsRaids, v => staged.enableSlsRaids = v, true), Tip("UseVanillaRaidConfiguration", "On, StarLevelSystem runs its own raids. Off, Valheim's own raid events are used instead and nothing on this page applies.")),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Raid frequency (lower = more often)", 0.1f, 10f, staged.raidEventRate, false, v => staged.raidEventRate = v), Tip(ValConfig.RaidEventRate)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Minutes between checks", 1f, 120f, staged.raidCheckMinutes, true, v => staged.raidCheckMinutes = (int)v), Tip(ValConfig.ServerTimeBetweenRaidStartChecks)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Max attempts / player", 0f, 50f, staged.maxRaidAttempts, true, v => staged.maxRaidAttempts = (int)v), Tip(ValConfig.MaxRaidAttemptsPerPlayer)),
                WithTip(ConfigUI.AddSliderRow(parent, LeftColWidth, LabelWidth, SliderWidth, ValueWidth, "Max active raids", 1f, 20f, staged.maxActiveRaids, true, v => staged.maxActiveRaids = (int)v), Tip(ValConfig.MaxActiveRaids)),
            };
            ConfigUI.LayoutColumn(left, 0f, colStartY);

            // Right side - scrollable list of every configured raid, each with an enable/disable toggle and its spawns
            // behind a Spawns button. Disabled raids keep their config in RaidSettings.yaml and are marked Enabled = false.
            const float ScrollX = 446f;
            const float ScrollW = 402f;
            float scrollH = PageH - colStartY - RowHeight - 8f;
            ConfigUI.AddText(parent, ScrollX, colStartY, ScrollW, RowHeight, "Raids and their spawns", 16, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimYellow);
            ConfigUI.CreateScroll(parent, ScrollX, colStartY + RowHeight, ScrollW, scrollH, out Transform content, out float contentW);
            List<RaidDefinition> raids = staged.raidSource?.Raids;
            if (content == null || raids == null) { return; }

            // Listed by name, but each raid keeps the index it has in the file, which is what the staged spawns are
            // keyed by.
            List<int> order = Enumerable.Range(0, raids.Count).Where(i => raids[i] != null).OrderBy(i => raids[i].Name).ToList();
            foreach (int raidIndex in order) {
                AddRaidEntry(content, contentW, raidIndex, raids[raidIndex]);
            }
        }

        private static void AddRaidEntry(Transform content, float width, int raidIndex, RaidDefinition raid) {
            const float TextX = 32f;
            const float ButtonW = 62f;
            string raidName = raid.Name;   // capture for the closures

            GameObject header = ConfigUI.NewLayoutRow(content, width, 44f);
            WithTip(header, Tip(raidName, $"Runs for {raid.Duration:0}s, then waits {raid.RaidCoolDownMinutes:0} minutes before it can be picked again. " +
                "A disabled raid keeps all of its settings in RaidSettings.yaml."));
            ConfigUI.AddToggle(header.transform, 2f, 3f, 22f, staged.raidsOn.Contains(raidName), on => {
                if (on) { staged.raidsOn.Add(raidName); }
                else { staged.raidsOn.Remove(raidName); }
            });
            float textW = width - TextX - ButtonW - 8f;
            ConfigUI.AddText(header.transform, TextX, 0f, textW, 20f, PrettifyRaidName(raidName), 14, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimOrange);
            int types = raid.Spawns != null ? raid.Spawns.Select(sp => sp.PrefabName).Distinct().Count() : 0;
            string sub = $"{types} creature type{(types == 1 ? "" : "s")}  ·  {raid.Duration:0}s";
            ConfigUI.AddText(header.transform, TextX, 20f, textW, 24f, sub, 12, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);

            // The spawn rows are separate children of the scroll content, so hiding them collapses the space they took:
            // a vertical layout group skips inactive children. With 50-odd spawns across the shipped raids, listing them
            // all at once would bury the raid list itself.
            List<GameObject> spawnRows = new List<GameObject>();
            if (raid.Spawns != null && raid.Spawns.Count > 0) {
                spawnRows.Add(AddRaidSpawnHeaderRow(content, width));
                for (int spawnIndex = 0; spawnIndex < raid.Spawns.Count; spawnIndex++) {
                    if (staged.raidSpawns.TryGetValue(RaidSpawnKey(raidIndex, spawnIndex), out StagedRaidSpawn spawn) == false) { continue; }
                    spawnRows.Add(AddRaidSpawnRow(content, width, spawn));
                }
            }
            foreach (GameObject row in spawnRows) { row.SetActive(false); }

            if (spawnRows.Count == 0) { return; }
            bool expanded = false;
            GameObject button = ConfigUI.AddButton(header.transform, width - ButtonW - 4f, 8f, ButtonW, "$sls_cfg_raids_show_spawns", null, 26f);
            Text caption = button.GetComponentInChildren<Text>();
            button.GetComponent<Button>().onClick.AddListener(() => {
                expanded = !expanded;
                foreach (GameObject row in spawnRows) { row.SetActive(expanded); }
                caption.text = ConfigUI.L(expanded ? "$sls_cfg_raids_hide_spawns" : "$sls_cfg_raids_show_spawns");
            });
        }

        private const float SpawnNameX = 34f;
        private const float SpawnNameW = 150f;
        private const float SpawnGroupX = 188f;
        private const float SpawnMaxX = 240f;
        private const float SpawnIntervalX = 292f;
        private const float SpawnFieldW = 46f;

        private static GameObject AddRaidSpawnHeaderRow(Transform content, float width) {
            GameObject row = ConfigUI.NewLayoutRow(content, width, 20f);
            Color color = GUIManager.Instance.ValheimYellow;
            ConfigUI.AddText(row.transform, SpawnNameX, 0f, SpawnNameW, 20f, "Creature", 11, TextAnchor.MiddleLeft, color);
            ConfigUI.AddText(row.transform, SpawnGroupX, 0f, SpawnFieldW, 20f, "Each", 11, TextAnchor.MiddleLeft, color);
            ConfigUI.AddText(row.transform, SpawnMaxX, 0f, SpawnFieldW + 6f, 20f, "Max alive", 11, TextAnchor.MiddleLeft, color);
            ConfigUI.AddText(row.transform, SpawnIntervalX, 0f, SpawnFieldW + 20f, 20f, "Every (s)", 11, TextAnchor.MiddleLeft, color);
            return row;
        }

        private static GameObject AddRaidSpawnRow(Transform content, float width, StagedRaidSpawn spawn) {
            GameObject row = ConfigUI.NewLayoutRow(content, width, 32f);
            ConfigUI.AddText(row.transform, SpawnNameX, 0f, SpawnNameW, 28f, PrettyPrefab(spawn.PrefabName), 12, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimBeige);

            InputField group = null, max = null, interval = null;
            group = ConfigUI.AddTextField(row.transform, SpawnGroupX, 0f, SpawnFieldW, spawn.GroupSize.ToString(), text => {
                if (int.TryParse(text, out int value)) { spawn.GroupSize = Mathf.Clamp(value, 1, MaxSpawnGroupSize); }
                group.SetTextWithoutNotify(spawn.GroupSize.ToString());
            }, InputField.ContentType.IntegerNumber);
            WithTip(group.gameObject, Tip("SpawnGroupSize", "How many of this creature arrive each time the raid spawns it."));
            max = ConfigUI.AddTextField(row.transform, SpawnMaxX, 0f, SpawnFieldW, spawn.MaxAlive.ToString(), text => {
                if (int.TryParse(text, out int value)) { spawn.MaxAlive = Mathf.Clamp(value, 0, MaxSpawnAlive); }
                max.SetTextWithoutNotify(spawn.MaxAlive.ToString());
            }, InputField.ContentType.IntegerNumber);
            WithTip(max.gameObject, Tip("MaxSpawned", "How many of this creature may be alive at once from this raid. 0 stops it spawning at all."));
            interval = ConfigUI.AddTextField(row.transform, SpawnIntervalX, 0f, SpawnFieldW + 10f, FormatInterval(spawn.Interval), text => {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) {
                    spawn.Interval = Mathf.Clamp(value, MinSpawnInterval, MaxSpawnInterval);
                }
                interval.SetTextWithoutNotify(FormatInterval(spawn.Interval));
            }, InputField.ContentType.DecimalNumber);
            WithTip(interval.gameObject, Tip("SpawnInterval", "Seconds between this creature's waves. The raid keeps spawning it until it hits Max alive."));
            return row;
        }

        private static string FormatInterval(float seconds) {
            return seconds.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------------------------------------
        //  Save
        // ------------------------------------------------------------------------------------------------

        // Per-raid enable/disable and the three spawn numbers this page shows; every other per-raid and per-spawn
        // setting is preserved.
        private static void SaveRaids(List<string> failures, List<string> warnings) {
            RaidConfiguration live = RaidsData.SLE_Raid_Settings;
            if (live?.Raids == null) { return; }
            if (SetsEqual(staged.raidsOn, baseline.raidsOn) && RaidSpawnsMatch(staged.raidSpawns, baseline.raidSpawns)) { return; }

            RaidConfiguration copy = CopyForEdit(YamlConfigManager.RaidSettings, live);
            for (int raidIndex = 0; raidIndex < copy.Raids.Count; raidIndex++) {
                RaidDefinition raid = copy.Raids[raidIndex];
                if (raid == null) { continue; }
                raid.Enabled = staged.raidsOn.Contains(raid.Name);
                if (raid.Spawns == null) { continue; }
                for (int spawnIndex = 0; spawnIndex < raid.Spawns.Count; spawnIndex++) {
                    RaidSpawnEntry entry = raid.Spawns[spawnIndex];
                    if (entry == null || staged.raidSpawns.TryGetValue(RaidSpawnKey(raidIndex, spawnIndex), out StagedRaidSpawn spawn) == false) { continue; }
                    // The file can be reloaded by the watcher while the panel is open, which would shuffle these
                    // positions. Writing numbers onto the wrong creature is worse than not writing them.
                    if (string.Equals(entry.PrefabName, spawn.PrefabName, System.StringComparison.Ordinal) == false) {
                        Logger.LogWarning($"Raid '{raid.Name}' changed while the quick configure panel was open; " +
                            $"'{spawn.PrefabName}' is now '{entry.PrefabName}', so its spawn numbers were left alone.");
                        continue;
                    }
                    entry.SpawnGroupSize = spawn.GroupSize;
                    entry.MaxSpawned = spawn.MaxAlive;
                    entry.SpawnInterval = spawn.Interval;
                }
            }
            SaveYaml(YamlConfigManager.RaidSettings, copy, "Raid settings", failures, warnings);
        }
    }
}
