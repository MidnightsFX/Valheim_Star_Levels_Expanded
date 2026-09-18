using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    // The Location Reset page of the quick configure panel: the master switch, the Defaults most servers tune, biome
    // rates, and each reset group's on/off, timer and terrain reset. Members, schedules, distance scopes, protection
    // rules and per-prefab overrides stay in LocationResetSettings.yaml.
    internal static partial class QuickConfigureTool {

        // The biomes the page offers a rate for. Anything the file does not list falls back to its All entry, then 1.
        private static readonly Heightmap.Biome[] ResetRateBiomes = {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands,
            Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean,
        };

        // Shorter timers make the sweep re-examine the whole world that often; see LocationResetData.MinAPIResetHours.
        private const float MinResetHours = 1f;
        private const float MaxResetHours = 336f;

        // Hours snap to whole numbers unless the file already holds a fraction, which a whole-number slider would round.
        private static bool IsWhole(float value) => value == Mathf.Round(value);

        private static Text locationResetWarningText;
        private static readonly List<ResetGroupView> resetGroupViews = new List<ResetGroupView>();

        private class ResetGroupView {
            internal StagedResetGroup Group;
            internal Text Tag;
            internal Text Description;
            internal Slider HoursSlider;
            internal InputField HoursField;
            internal Toggle TerrainToggle;
        }

        private static void ClearLocationResetReferences() {
            locationResetWarningText = null;
            resetGroupViews.Clear();
        }

        private class StagedResetGroup {
            // The configured group, read-only here: members, schedule and distance scope feed the description.
            internal LocationResetGroup Source;
            internal bool Enabled;
            // Null follows Defaults, as in the file. A slider or toggle moved back onto the default by a group that
            // never set one of its own stays null, so it keeps following Defaults.
            internal float? ResetHours;
            internal bool? ResetTerrain;

            internal bool SameAs(StagedResetGroup other) {
                return other != null && Enabled == other.Enabled && ResetHours == other.ResetHours && ResetTerrain == other.ResetTerrain;
            }
        }

        private class StagedLocationReset {
            // Nothing resets until both are on, so the page shows and sets them as one switch.
            internal bool masterSwitch;    // ValConfig.EnableLocationReset
            internal bool yamlEnabled;     // LocationResetSettings.yaml Enabled
            internal float sweepBudgetMs;  // ValConfig.LocationResetSweepBudgetMs

            internal bool stampOnFirstSight;
            internal float playerSafeRadius;
            internal float defaultResetHours;
            internal bool defaultResetTerrain;
            internal float defaultExtraTerrainRadius;   // read-only here, for the descriptions
            internal float protectionRadius;
            internal bool containerDefaultLoot;
            internal Dictionary<Heightmap.Biome, float> biomeRates;
            internal Dictionary<string, StagedResetGroup> groups;

            internal bool Enabled => masterSwitch && yamlEnabled;

            internal static StagedLocationReset Snapshot() {
                LocationResetConfiguration cfg = LocationResetData.SLE_LocationReset_Settings;
                LocationResetDefaults defaults = cfg?.Defaults ?? new LocationResetDefaults();
                StagedLocationReset s = new StagedLocationReset {
                    masterSwitch = ValConfig.EnableLocationReset.Value,
                    yamlEnabled = LocationResetData.ConfigEnabled,
                    sweepBudgetMs = ValConfig.LocationResetSweepBudgetMs.Value,
                    stampOnFirstSight = cfg?.StampOnFirstSight ?? true,
                    playerSafeRadius = cfg?.PlayerSafeRadius ?? 256f,
                    defaultResetHours = defaults.ResetHours,
                    defaultResetTerrain = defaults.ResetTerrain,
                    defaultExtraTerrainRadius = defaults.ExtraTerrainRadius,
                    protectionRadius = defaults.ProtectionRadius,
                    containerDefaultLoot = LocationResetData.InPlaceRefresh.ContainerDefaultLoot,
                    biomeRates = cfg?.BiomeRates != null ? new Dictionary<Heightmap.Biome, float>(cfg.BiomeRates) : new Dictionary<Heightmap.Biome, float>(),
                    groups = new Dictionary<string, StagedResetGroup>(),
                };
                if (cfg?.ResetGroups != null) {
                    foreach (KeyValuePair<string, LocationResetGroup> group in cfg.ResetGroups) {
                        if (group.Value == null) { continue; }
                        s.groups[group.Key] = new StagedResetGroup {
                            Source = group.Value,
                            // Absent means enabled for a group.
                            Enabled = group.Value.Enabled.GetValueOrDefault(true),
                            ResetHours = group.Value.ResetHours,
                            ResetTerrain = group.Value.ResetTerrain,
                        };
                    }
                }
                return s;
            }

            // The rate the sweep uses for a biome: its own entry, else the All entry, else 1.
            internal float RateFor(Heightmap.Biome biome) {
                if (biomeRates.TryGetValue(biome, out float rate)) { return rate; }
                return biomeRates.TryGetValue(Heightmap.Biome.All, out float all) ? all : 1f;
            }

            internal float HoursFor(StagedResetGroup group) => group.ResetHours ?? defaultResetHours;
            internal bool TerrainFor(StagedResetGroup group) => group.ResetTerrain ?? defaultResetTerrain;

            // Everything that lives in the YAML file.
            internal bool YamlMatches(StagedLocationReset o) {
                if (yamlEnabled != o.yamlEnabled || stampOnFirstSight != o.stampOnFirstSight || playerSafeRadius != o.playerSafeRadius
                    || defaultResetHours != o.defaultResetHours || defaultResetTerrain != o.defaultResetTerrain
                    || protectionRadius != o.protectionRadius || containerDefaultLoot != o.containerDefaultLoot) {
                    return false;
                }
                if (ResetRateBiomes.Any(b => RateFor(b) != o.RateFor(b))) { return false; }
                if (groups.Count != o.groups.Count) { return false; }
                foreach (KeyValuePair<string, StagedResetGroup> group in groups) {
                    if (o.groups.TryGetValue(group.Key, out StagedResetGroup other) == false || group.Value.SameAs(other) == false) { return false; }
                }
                return true;
            }

            internal bool Matches(StagedLocationReset o) {
                return masterSwitch == o.masterSwitch && sweepBudgetMs == o.sweepBudgetMs && YamlMatches(o);
            }
        }

        // ------------------------------------------------------------------------------------------------
        //  Page
        // ------------------------------------------------------------------------------------------------

        private static void BuildLocationResetPage(Transform parent) {
            const float IntroH = 60f;
            const float LeftW = 392f;
            const float RightX = 408f;
            const float LabelW = 180f, SliderW = 120f, ValueW = 60f;
            float rightW = PageW - RightX;
            StagedLocationReset lr = staged.locationReset;

            GameObject intro = ConfigUI.AddTextRow(parent, PageW, IntroH, "$sls_cfg_locreset_intro", 13, GUIManager.Instance.ValheimBeige);
            ConfigUI.PositionRow(intro, 0f, 0f);
            locationResetWarningText = ConfigUI.AddText(parent, 0f, IntroH, PageW, 20f, "", 13, TextAnchor.MiddleCenter, GUIManager.Instance.ValheimOrange);
            float scrollY = IntroH + 24f;
            float scrollH = PageH - scrollY;

            // Left - the switch, Defaults and throughput, then biome rates.
            ConfigUI.CreateScroll(parent, 0f, scrollY, LeftW, scrollH, out Transform left, out float lw);
            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Location Reset"));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "Enable Location Reset", lr.Enabled, on => {
                lr.masterSwitch = on;
                lr.yamlEnabled = on;
                RefreshLocationResetViews();
            }), Tip(ValConfig.EnableLocationReset)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "Start timers on first visit", lr.stampOnFirstSight, on => lr.stampOnFirstSight = on), Tip("StampOnFirstSight", "The first time the sweep sees a zone it records what is there and starts its timer, rather than resetting it immediately.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Default reset hours",
                Mathf.Min(MinResetHours, lr.defaultResetHours), Mathf.Max(MaxResetHours, lr.defaultResetHours), lr.defaultResetHours, IsWhole(lr.defaultResetHours), v => {
                    lr.defaultResetHours = v;
                    RefreshLocationResetViews();
                }), Tip("Defaults.ResetHours", "Real-world hours between resets for anything that does not set its own timer.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "Reset terrain by default", lr.defaultResetTerrain, on => {
                lr.defaultResetTerrain = on;
                RefreshLocationResetViews();
            }), Tip("Defaults.ResetTerrain", "Restores the ground as well as the objects, which is what undoes mining craters and dug-out approaches.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Player safe radius (m)",
                0f, Mathf.Max(1024f, lr.playerSafeRadius), lr.playerSafeRadius, true, v => lr.playerSafeRadius = v), Tip("PlayerSafeRadius", "A zone with a player within this distance is left alone and tried again later, so nothing is ever reset in front of someone.")));
            // The protection scan reaches a chunk and its 8 neighbours, so LocationResetData clamps this to 32-96m.
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Protection radius (m)",
                LocationResetData.MinProtectionRadius, LocationResetData.MaxProtectionRadius, lr.protectionRadius, true, v => lr.protectionRadius = v), Tip("Defaults.ProtectionRadius", "How far from a chunk's centre a player build blocks its reset. The scan covers the chunk and its neighbours, so this is limited to 32-96m.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "Refill unowned chests", lr.containerDefaultLoot, on => lr.containerDefaultLoot = on), Tip("InPlaceRefresh.ContainerDefaultLoot", "Re-rolls the loot in chests nobody has built or placed. Off by default: it is the one refresh that grants new items.")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "Sweep budget (ms/frame)",
                0f, 33f, lr.sweepBudgetMs, false, v => lr.sweepBudgetMs = v), Tip(ValConfig.LocationResetSweepBudgetMs)));
            ScrollRow(left, lw, 84f, t => ConfigUI.AddTextRow(t, lw, 84f, "$sls_cfg_locreset_settings_help", 12, GUIManager.Instance.ValheimBeige));

            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "Biome rates"));
            ScrollRow(left, lw, 50f, t => ConfigUI.AddTextRow(t, lw, 50f, "$sls_cfg_locreset_biome_help", 12, GUIManager.Instance.ValheimBeige));
            foreach (Heightmap.Biome biome in ResetRateBiomes) {
                Heightmap.Biome b = biome;   // capture per iteration
                float rate = lr.RateFor(b);
                ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, BiomeName(b),
                    0f, Mathf.Max(5f, rate), rate, false, v => lr.biomeRates[b] = v), Tip("BiomeRates", "Multiplies every reset timer in this biome: 0.5 resets twice as fast, 2 half as fast, 0 never resets there.")));
            }

            // Right - every reset group in the file, in file order.
            ConfigUI.CreateScroll(parent, RightX, scrollY, rightW, scrollH, out Transform right, out float rw);
            ScrollRow(right, rw, RowHeight, t => ConfigUI.AddHeaderRow(t, rw, "Reset groups"));
            ScrollRow(right, rw, 50f, t => ConfigUI.AddTextRow(t, rw, 50f, "$sls_cfg_locreset_groups_help", 12, GUIManager.Instance.ValheimBeige));
            foreach (KeyValuePair<string, StagedResetGroup> group in lr.groups) {
                AddResetGroupEntry(right, rw, group.Key, group.Value);
            }

            RefreshLocationResetViews();
        }

        private static void AddResetGroupEntry(Transform content, float width, string name, StagedResetGroup group) {
            const float TextX = 30f;
            const float TagW = 60f;
            const float ControlsTop = 76f;
            const float LabelW = 120f, SliderW = 170f, ValueW = 60f;
            StagedLocationReset lr = staged.locationReset;
            GameObject entry = ConfigUI.NewLayoutRow(content, width, ControlsTop + 2 * RowHeight + 8f);
            ResetGroupView view = new ResetGroupView { Group = group };

            ConfigUI.AddToggle(entry.transform, 2f, 1f, 22f, group.Enabled, on => { group.Enabled = on; RefreshLocationResetViews(); });
            ConfigUI.AddText(entry.transform, TextX, 0f, width - TextX - TagW - 8f, 24f, Prettify(name), 15, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimOrange);
            view.Tag = ConfigUI.AddText(entry.transform, width - TagW - 4f, 0f, TagW, 24f, "", 13, TextAnchor.MiddleRight);
            view.Description = ConfigUI.AddText(entry.transform, TextX, 26f, width - TextX - 4f, ControlsTop - 28f, "", 12, TextAnchor.UpperLeft, GUIManager.Instance.ValheimBeige);

            float rowW = width - TextX;
            if (string.IsNullOrWhiteSpace(group.Source.ResetSchedule)) {
                float hours = lr.HoursFor(group);
                GameObject hoursRow = WithTip(ConfigUI.AddSliderRow(entry.transform, rowW, LabelW, SliderW, ValueW, "Reset hours",
                    Mathf.Min(MinResetHours, hours), Mathf.Max(MaxResetHours, hours), hours, IsWhole(hours), v => {
                        group.ResetHours = group.Source.ResetHours.HasValue == false && v == lr.defaultResetHours ? (float?)null : v;
                        RefreshLocationResetViews();
                    }), Tip("ResetHours", "Real-world hours between this group's resets. Left on the default until you move it."));
                ConfigUI.PositionRow(hoursRow, TextX, ControlsTop);
                view.HoursSlider = hoursRow.GetComponentInChildren<Slider>();
                view.HoursField = hoursRow.GetComponentInChildren<InputField>();
            } else {
                // A schedule wins over ResetHours at the same level, so an hours slider here would do nothing.
                ConfigUI.AddText(entry.transform, TextX, ControlsTop, rowW, RowHeight, $"Timed by the schedule '{group.Source.ResetSchedule}' in the YAML file.", 13, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimBeige);
            }

            GameObject terrainRow = WithTip(ConfigUI.AddToggleRow(entry.transform, rowW, LabelW + 80f, "Reset terrain", lr.TerrainFor(group), on => {
                group.ResetTerrain = group.Source.ResetTerrain.HasValue == false && on == lr.defaultResetTerrain ? (bool?)null : on;
                RefreshLocationResetViews();
            }), Tip("ResetTerrain", "Restores the ground around this group's targets as well as the objects themselves."));
            ConfigUI.PositionRow(terrainRow, TextX, ControlsTop + RowHeight);
            view.TerrainToggle = terrainRow.GetComponentInChildren<Toggle>();

            resetGroupViews.Add(view);
        }

        private static void RefreshLocationResetViews() {
            if (staged?.locationReset == null) { return; }
            StagedLocationReset lr = staged.locationReset;

            foreach (ResetGroupView view in resetGroupViews) {
                StagedResetGroup group = view.Group;
                // Groups that follow Defaults show the Defaults value as it changes.
                if (group.ResetHours.HasValue == false && view.HoursSlider != null) {
                    float hours = Mathf.Clamp(lr.defaultResetHours, view.HoursSlider.minValue, view.HoursSlider.maxValue);
                    view.HoursSlider.SetValueWithoutNotify(hours);
                    view.HoursField?.SetTextWithoutNotify(ConfigUI.Fmt(view.HoursSlider.value, view.HoursSlider.wholeNumbers));
                }
                if (group.ResetTerrain.HasValue == false && view.TerrainToggle != null) {
                    view.TerrainToggle.SetIsOnWithoutNotify(lr.defaultResetTerrain);
                }
                view.Description.text = DescribeResetGroup(group, lr);
                view.Tag.text = group.Enabled ? "On" : "Off";
                view.Tag.color = group.Enabled ? EasierColor : InactiveColor;
            }

            if (locationResetWarningText != null) {
                locationResetWarningText.text = LocationResetWarning(lr);
            }
        }

        // One line above the scroll views for the thing most worth knowing right now, if anything.
        private static string LocationResetWarning(StagedLocationReset lr) {
            if (LocationResetData.BlockedByModConflict) {
                return ConfigUI.L("$sls_cfg_locreset_conflict");
            }
            StagedLocationReset live = baseline?.locationReset;
            if (live != null && live.Enabled == false && live.masterSwitch != live.yamlEnabled && lr.masterSwitch == live.masterSwitch && lr.yamlEnabled == live.yamlEnabled) {
                return ConfigUI.L("$sls_cfg_locreset_switch_mismatch");
            }
            if (lr.Enabled && (live == null || live.Enabled == false)) {
                return ConfigUI.L("$sls_cfg_locreset_stamp_hint");
            }
            return "";
        }

        private static string DescribeResetGroup(StagedResetGroup group, StagedLocationReset lr) {
            LocationResetGroup source = group.Source;
            string members = DescribeResetMembers(source.Members);

            string timing = string.IsNullOrWhiteSpace(source.ResetSchedule)
                ? $"every {lr.HoursFor(group):0.##}h{(group.ResetHours.HasValue ? "" : " (default)")}"
                : $"on the schedule '{source.ResetSchedule}'";

            string terrain = "";
            if (lr.TerrainFor(group)) {
                float extra = source.ExtraTerrainRadius ?? lr.defaultExtraTerrainRadius;
                terrain = extra > 0f ? $", terrain too (+{extra:0}m)" : ", terrain too";
            }

            float min = Mathf.Max(0f, source.MinDistance ?? 0f);
            float max = Mathf.Max(0f, source.MaxDistance ?? 0f);
            string scope = "";
            if (min > 0f && max > 0f) { scope = $", only {min:0}-{max:0}m from spawn"; }
            else if (max > 0f) { scope = $", only within {max:0}m of spawn"; }
            else if (min > 0f) { scope = $", only beyond {min:0}m from spawn"; }

            return $"{members}. Resets {timing}{terrain}{scope}.";
        }

        // Member names are left as written: they are the prefab names an admin types into the file.
        private static string DescribeResetMembers(List<string> members) {
            List<string> names = members?.Where(m => string.IsNullOrWhiteSpace(m) == false).Select(m => m.Trim()).ToList() ?? new List<string>();
            if (names.Count == 0) { return "No members"; }
            List<string> shown = names.Take(4).Select(DescribeResetMember).ToList();
            string list = string.Join(", ", shown.ToArray());
            return names.Count > shown.Count ? $"{list} and {names.Count - shown.Count} more" : list;
        }

        private static string DescribeResetMember(string member) {
            if (string.Equals(member, "$Pickable", StringComparison.OrdinalIgnoreCase)) { return "every pickable"; }
            if (string.Equals(member, "$Mineable", StringComparison.OrdinalIgnoreCase)) { return "every mineable rock"; }
            return member;
        }

        // ------------------------------------------------------------------------------------------------
        //  Save
        // ------------------------------------------------------------------------------------------------

        // Only what changed is written onto the copy, so values the page does not show - and any the admin wrote in a
        // different form, like a group Enabled left absent - stay exactly as they are in the file.
        private static void SaveLocationReset(List<string> failures, List<string> warnings) {
            StagedLocationReset s = staged.locationReset;
            StagedLocationReset b = baseline.locationReset;
            YamlConfigFile<LocationResetConfiguration> file = YamlConfigManager.LocationResetSettings;
            LocationResetConfiguration live = LocationResetData.SLE_LocationReset_Settings;
            if (file == null || live == null || s.YamlMatches(b)) { return; }

            LocationResetConfiguration copy = CopyForEdit(file, live);
            if (s.yamlEnabled != b.yamlEnabled) { copy.Enabled = s.yamlEnabled; }
            if (s.stampOnFirstSight != b.stampOnFirstSight) { copy.StampOnFirstSight = s.stampOnFirstSight; }
            if (s.playerSafeRadius != b.playerSafeRadius) { copy.PlayerSafeRadius = s.playerSafeRadius; }

            if (copy.Defaults == null) { copy.Defaults = new LocationResetDefaults(); }
            if (s.defaultResetHours != b.defaultResetHours) { copy.Defaults.ResetHours = s.defaultResetHours; }
            if (s.defaultResetTerrain != b.defaultResetTerrain) { copy.Defaults.ResetTerrain = s.defaultResetTerrain; }
            if (s.protectionRadius != b.protectionRadius) { copy.Defaults.ProtectionRadius = s.protectionRadius; }

            if (s.containerDefaultLoot != b.containerDefaultLoot) {
                if (copy.InPlaceRefresh == null) { copy.InPlaceRefresh = new LocationResetInPlace(); }
                copy.InPlaceRefresh.ContainerDefaultLoot = s.containerDefaultLoot;
                // Left out of the file while it holds nothing but defaults, as the generated file does; an empty
                // section would be written as `InPlaceRefresh: {}`.
                if (copy.InPlaceRefresh.Pickables && copy.InPlaceRefresh.MineRocks && copy.InPlaceRefresh.ContainerDefaultLoot == false) {
                    copy.InPlaceRefresh = null;
                }
            }

            foreach (Heightmap.Biome biome in ResetRateBiomes) {
                if (s.RateFor(biome) == b.RateFor(biome)) { continue; }
                if (copy.BiomeRates == null) { copy.BiomeRates = new Dictionary<Heightmap.Biome, float>(); }
                copy.BiomeRates[biome] = s.RateFor(biome);
            }

            if (copy.ResetGroups != null) {
                foreach (KeyValuePair<string, StagedResetGroup> group in s.groups) {
                    if (b.groups.TryGetValue(group.Key, out StagedResetGroup before) && group.Value.SameAs(before)) { continue; }
                    if (copy.ResetGroups.TryGetValue(group.Key, out LocationResetGroup target) == false || target == null) { continue; }
                    if (target.Enabled.GetValueOrDefault(true) != group.Value.Enabled) { target.Enabled = group.Value.Enabled; }
                    target.ResetHours = group.Value.ResetHours;
                    target.ResetTerrain = group.Value.ResetTerrain;
                }
            }

            SaveYaml(file, copy, "Location reset settings", failures, warnings);
        }
    }
}
