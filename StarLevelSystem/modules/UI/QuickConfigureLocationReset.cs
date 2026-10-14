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
            internal float locationPlayerRadius;
            internal float defaultResetHours;
            internal bool defaultResetTerrain;
            internal float defaultExtraTerrainRadius;   // read-only here, for the descriptions
            internal float protectionRadius;
            internal float locationBuffer;
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
                    locationPlayerRadius = cfg?.LocationPlayerRadius ?? 120f,
                    defaultResetHours = defaults.ResetHours,
                    defaultResetTerrain = defaults.ResetTerrain,
                    defaultExtraTerrainRadius = defaults.ExtraTerrainRadius,
                    protectionRadius = defaults.ProtectionRadius,
                    locationBuffer = defaults.LocationBuffer,
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

            // Reset page: the two ConfigEntries and everything the page shows from the YAML go back to a freshly
            // generated file. A group the shipped file does not have keeps its settings; its members, schedule and
            // scope were never the page's to change.
            internal void ResetToShipped(LocationResetConfiguration shipped) {
                masterSwitch = DefaultOf(ValConfig.EnableLocationReset);
                sweepBudgetMs = DefaultOf(ValConfig.LocationResetSweepBudgetMs);
                if (shipped == null) { return; }
                yamlEnabled = shipped.Enabled.GetValueOrDefault(false);
                stampOnFirstSight = shipped.StampOnFirstSight;
                playerSafeRadius = shipped.PlayerSafeRadius;
                locationPlayerRadius = shipped.LocationPlayerRadius;
                LocationResetDefaults defaults = shipped.Defaults ?? new LocationResetDefaults();
                defaultResetHours = defaults.ResetHours;
                defaultResetTerrain = defaults.ResetTerrain;
                protectionRadius = defaults.ProtectionRadius;
                locationBuffer = defaults.LocationBuffer;
                containerDefaultLoot = (shipped.InPlaceRefresh ?? new LocationResetInPlace()).ContainerDefaultLoot;
                biomeRates = shipped.BiomeRates != null ? new Dictionary<Heightmap.Biome, float>(shipped.BiomeRates) : new Dictionary<Heightmap.Biome, float>();
                if (shipped.ResetGroups == null) { return; }
                foreach (KeyValuePair<string, StagedResetGroup> group in groups) {
                    if (shipped.ResetGroups.TryGetValue(group.Key, out LocationResetGroup source) == false || source == null) { continue; }
                    group.Value.Enabled = source.Enabled.GetValueOrDefault(true);
                    group.Value.ResetHours = source.ResetHours;
                    group.Value.ResetTerrain = source.ResetTerrain;
                }
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
                    || locationPlayerRadius != o.locationPlayerRadius
                    || defaultResetHours != o.defaultResetHours || defaultResetTerrain != o.defaultResetTerrain
                    || protectionRadius != o.protectionRadius || locationBuffer != o.locationBuffer
                    || containerDefaultLoot != o.containerDefaultLoot) {
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

            GameObject intro = ConfigUI.AddTextRow(parent, PageW, IntroH, "$sls_cfg_locreset_about", 13, GUIManager.Instance.ValheimBeige);
            ConfigUI.PositionRow(intro, 0f, 0f);
            locationResetWarningText = ConfigUI.AddText(parent, 0f, IntroH, PageW, 20f, "", 13, TextAnchor.MiddleCenter, GUIManager.Instance.ValheimOrange);
            float scrollY = IntroH + 24f;
            float scrollH = PageH - scrollY;

            // Left - the switch, Defaults and throughput, then biome rates.
            ConfigUI.CreateScroll(parent, 0f, scrollY, LeftW, scrollH, out Transform left, out float lw);
            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "$sls_cfg_page_location_reset"));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "$sls_cfg_locreset_enable", lr.Enabled, on => {
                lr.masterSwitch = on;
                lr.yamlEnabled = on;
                RefreshLocationResetViews();
            }), Tip(ValConfig.EnableLocationReset)));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "$sls_cfg_locreset_first_visit", lr.stampOnFirstSight, on => lr.stampOnFirstSight = on), Tip("StampOnFirstSight", "$sls_cfg_tip_locreset_first_visit")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "$sls_cfg_locreset_default_hours",
                Mathf.Min(MinResetHours, lr.defaultResetHours), Mathf.Max(MaxResetHours, lr.defaultResetHours), lr.defaultResetHours, IsWhole(lr.defaultResetHours), v => {
                    lr.defaultResetHours = v;
                    RefreshLocationResetViews();
                }), Tip("Defaults.ResetHours", "$sls_cfg_tip_locreset_default_hours")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "$sls_cfg_locreset_default_terrain", lr.defaultResetTerrain, on => {
                lr.defaultResetTerrain = on;
                RefreshLocationResetViews();
            }), Tip("Defaults.ResetTerrain", "$sls_cfg_tip_locreset_default_terrain")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "$sls_cfg_locreset_safe_radius",
                0f, Mathf.Max(1024f, lr.playerSafeRadius), lr.playerSafeRadius, true, v => lr.playerSafeRadius = v), Tip("PlayerSafeRadius", "$sls_cfg_tip_locreset_safe_radius")));
            // The protection scan reaches a chunk and its 8 neighbours, so LocationResetData clamps this to 32-96m.
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "$sls_cfg_locreset_protection_radius",
                LocationResetData.MinProtectionRadius, LocationResetData.MaxProtectionRadius, lr.protectionRadius, true, v => lr.protectionRadius = v), Tip("Defaults.ProtectionRadius", "$sls_cfg_tip_locreset_protection_radius")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "$sls_cfg_locreset_location_radius",
                0f, Mathf.Max(512f, lr.locationPlayerRadius), lr.locationPlayerRadius, true, v => lr.locationPlayerRadius = v), Tip("LocationPlayerRadius", "$sls_cfg_tip_locreset_location_radius")));
            // LocationResetData clamps this to 0-64m.
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "$sls_cfg_locreset_location_buffer",
                0f, LocationResetData.MaxLocationBuffer, lr.locationBuffer, true, v => lr.locationBuffer = v), Tip("Defaults.LocationBuffer", "$sls_cfg_tip_locreset_location_buffer")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddToggleRow(t, lw, LabelW + 80f, "$sls_cfg_locreset_refill_chests", lr.containerDefaultLoot, on => lr.containerDefaultLoot = on), Tip("InPlaceRefresh.ContainerDefaultLoot", "$sls_cfg_tip_locreset_refill_chests")));
            ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, "$sls_cfg_locreset_sweep_budget",
                0f, 33f, lr.sweepBudgetMs, false, v => lr.sweepBudgetMs = v), Tip(ValConfig.LocationResetSweepBudgetMs)));
            ScrollRow(left, lw, 84f, t => ConfigUI.AddTextRow(t, lw, 84f, "$sls_cfg_locreset_settings_notes", 12, GUIManager.Instance.ValheimBeige));

            ScrollRow(left, lw, RowHeight, t => ConfigUI.AddHeaderRow(t, lw, "$sls_cfg_locreset_header_biome_rates"));
            ScrollRow(left, lw, 50f, t => ConfigUI.AddTextRow(t, lw, 50f, "$sls_cfg_locreset_biome_help", 12, GUIManager.Instance.ValheimBeige));
            foreach (Heightmap.Biome biome in ResetRateBiomes) {
                Heightmap.Biome b = biome;   // capture per iteration
                float rate = lr.RateFor(b);
                ScrollRow(left, lw, RowHeight, t => WithTip(ConfigUI.AddSliderRow(t, lw, LabelW, SliderW, ValueW, BiomeName(b),
                    0f, Mathf.Max(5f, rate), rate, false, v => lr.biomeRates[b] = v), Tip("BiomeRates", "$sls_cfg_tip_locreset_biome_rate")));
            }

            // Right - every reset group in the file, in file order.
            ConfigUI.CreateScroll(parent, RightX, scrollY, rightW, scrollH, out Transform right, out float rw);
            ScrollRow(right, rw, RowHeight, t => ConfigUI.AddHeaderRow(t, rw, "$sls_cfg_locreset_header_groups"));
            ScrollRow(right, rw, 50f, t => ConfigUI.AddTextRow(t, rw, 50f, "$sls_cfg_locreset_groups_help", 12, GUIManager.Instance.ValheimBeige));
            foreach (KeyValuePair<string, StagedResetGroup> group in lr.groups) {
                AddResetGroupEntry(right, rw, group.Key, group.Value);
            }

            RefreshLocationResetViews();
        }

        private static void ResetLocationResetPage() {
            staged.locationReset.ResetToShipped(ShippedDefaults(YamlConfigManager.LocationResetSettings));
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
                GameObject hoursRow = WithTip(ConfigUI.AddSliderRow(entry.transform, rowW, LabelW, SliderW, ValueW, "$sls_cfg_locreset_group_hours",
                    Mathf.Min(MinResetHours, hours), Mathf.Max(MaxResetHours, hours), hours, IsWhole(hours), v => {
                        group.ResetHours = group.Source.ResetHours.HasValue == false && v == lr.defaultResetHours ? (float?)null : v;
                        RefreshLocationResetViews();
                    }), Tip("ResetHours", "$sls_cfg_tip_locreset_group_hours"));
                ConfigUI.PositionRow(hoursRow, TextX, ControlsTop);
                view.HoursSlider = hoursRow.GetComponentInChildren<Slider>();
                view.HoursField = hoursRow.GetComponentInChildren<InputField>();
            } else {
                // A schedule wins over ResetHours at the same level, so an hours slider here would do nothing. Set on the
                // text directly: the schedule is the admin's own text, and AddText would read a '$' in it as a token.
                Text scheduled = ConfigUI.AddText(entry.transform, TextX, ControlsTop, rowW, RowHeight, "", 13, TextAnchor.MiddleLeft, GUIManager.Instance.ValheimBeige);
                scheduled.text = L("$sls_cfg_locreset_group_scheduled", group.Source.ResetSchedule);
            }

            GameObject terrainRow = WithTip(ConfigUI.AddToggleRow(entry.transform, rowW, LabelW + 80f, "$sls_cfg_locreset_group_terrain", lr.TerrainFor(group), on => {
                group.ResetTerrain = group.Source.ResetTerrain.HasValue == false && on == lr.defaultResetTerrain ? (bool?)null : on;
                RefreshLocationResetViews();
            }), Tip("ResetTerrain", "$sls_cfg_tip_locreset_group_terrain"));
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
                view.Tag.text = ConfigUI.L(group.Enabled ? "$sls_cfg_tag_on" : "$sls_cfg_tag_off");
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

            // When it resets, then whether the ground goes too and how far out it reaches, as one comma-separated clause.
            List<string> parts = new List<string>();
            string hours = lr.HoursFor(group).ToString("0.##");
            parts.Add(string.IsNullOrWhiteSpace(source.ResetSchedule)
                ? L(group.ResetHours.HasValue ? "$sls_cfg_locreset_every_hours" : "$sls_cfg_locreset_every_hours_default", hours)
                : L("$sls_cfg_locreset_on_schedule", source.ResetSchedule));

            if (lr.TerrainFor(group)) {
                float extra = source.ExtraTerrainRadius ?? lr.defaultExtraTerrainRadius;
                parts.Add(extra > 0f ? L("$sls_cfg_locreset_terrain_extra", extra.ToString("0")) : ConfigUI.L("$sls_cfg_locreset_terrain"));
            }

            float min = Mathf.Max(0f, source.MinDistance ?? 0f);
            float max = Mathf.Max(0f, source.MaxDistance ?? 0f);
            if (min > 0f && max > 0f) { parts.Add(L("$sls_cfg_locreset_scope_between", min.ToString("0"), max.ToString("0"))); }
            else if (max > 0f) { parts.Add(L("$sls_cfg_locreset_scope_within", max.ToString("0"))); }
            else if (min > 0f) { parts.Add(L("$sls_cfg_locreset_scope_beyond", min.ToString("0"))); }

            return L("$sls_cfg_locreset_group_summary", members, string.Join(", ", parts.ToArray()));
        }

        // Member names are left as written: they are the prefab names an admin types into the file.
        private static string DescribeResetMembers(List<string> members) {
            List<string> names = members?.Where(m => string.IsNullOrWhiteSpace(m) == false).Select(m => m.Trim()).ToList() ?? new List<string>();
            if (names.Count == 0) { return ConfigUI.L("$sls_cfg_locreset_no_members"); }
            List<string> shown = names.Take(4).Select(DescribeResetMember).ToList();
            string list = string.Join(", ", shown.ToArray());
            return names.Count > shown.Count ? L("$sls_cfg_locreset_members_more", list, names.Count - shown.Count) : list;
        }

        private static string DescribeResetMember(string member) {
            if (string.Equals(member, "$Pickable", StringComparison.OrdinalIgnoreCase)) { return ConfigUI.L("$sls_cfg_locreset_every_pickable"); }
            if (string.Equals(member, "$Mineable", StringComparison.OrdinalIgnoreCase)) { return ConfigUI.L("$sls_cfg_locreset_every_mineable"); }
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
            if (s.locationPlayerRadius != b.locationPlayerRadius) { copy.LocationPlayerRadius = s.locationPlayerRadius; }

            if (copy.Defaults == null) { copy.Defaults = new LocationResetDefaults(); }
            if (s.defaultResetHours != b.defaultResetHours) { copy.Defaults.ResetHours = s.defaultResetHours; }
            if (s.defaultResetTerrain != b.defaultResetTerrain) { copy.Defaults.ResetTerrain = s.defaultResetTerrain; }
            if (s.protectionRadius != b.protectionRadius) { copy.Defaults.ProtectionRadius = s.protectionRadius; }
            if (s.locationBuffer != b.locationBuffer) { copy.Defaults.LocationBuffer = s.locationBuffer; }

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

            SaveYaml(file, copy, "$sls_cfg_label_location_reset_settings", failures, warnings);
        }
    }
}
