using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.modules.UI.QuickConfigPrefabs;

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

        // The biomes above and any custom ones, keyed as the rates hold them.
        private static IEnumerable<Heightmap.Biome> ResetRateRows(Dictionary<Heightmap.Biome, float> rates) {
            return ResetRateBiomes.Concat(BiomeRegistry.CustomBiomes.Select(custom => BiomeRegistry.ConfigKeyFor(rates, custom)));
        }

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
            StagedLocationReset lr = staged.locationReset;

            locationResetWarningText = Get<Text>(parent, "Warning");

            // Left - the switch, Defaults and throughput, then biome rates.
            WithTip(BindToggleRow(parent, "Enable", lr.Enabled, on => {
                lr.masterSwitch = on;
                lr.yamlEnabled = on;
                RefreshLocationResetViews();
            }), Tip(ValConfig.EnableLocationReset));
            WithTip(BindToggleRow(parent, "FirstVisit", lr.stampOnFirstSight, on => lr.stampOnFirstSight = on), Tip("StampOnFirstSight", "$sls_cfg_tip_locreset_first_visit"));
            WithTip(BindSliderRow(parent, "DefaultHours", lr.defaultResetHours, v => {
                lr.defaultResetHours = v;
                RefreshLocationResetViews();
            }, Mathf.Min(MinResetHours, lr.defaultResetHours), Mathf.Max(MaxResetHours, lr.defaultResetHours), IsWhole(lr.defaultResetHours)), Tip("Defaults.ResetHours", "$sls_cfg_tip_locreset_default_hours"));
            WithTip(BindToggleRow(parent, "DefaultTerrain", lr.defaultResetTerrain, on => {
                lr.defaultResetTerrain = on;
                RefreshLocationResetViews();
            }), Tip("Defaults.ResetTerrain", "$sls_cfg_tip_locreset_default_terrain"));
            WithTip(BindSliderRow(parent, "SafeRadius", lr.playerSafeRadius, v => lr.playerSafeRadius = v, max: Mathf.Max(1024f, lr.playerSafeRadius)), Tip("PlayerSafeRadius", "$sls_cfg_tip_locreset_safe_radius"));
            // The protection scan reaches a chunk and its 8 neighbours, so LocationResetData clamps this to 32-96m.
            WithTip(BindSliderRow(parent, "ProtectionRadius", lr.protectionRadius, v => lr.protectionRadius = v), Tip("Defaults.ProtectionRadius", "$sls_cfg_tip_locreset_protection_radius"));
            WithTip(BindSliderRow(parent, "LocationRadius", lr.locationPlayerRadius, v => lr.locationPlayerRadius = v, max: Mathf.Max(512f, lr.locationPlayerRadius)), Tip("LocationPlayerRadius", "$sls_cfg_tip_locreset_location_radius"));
            // LocationResetData clamps this to 0-64m.
            WithTip(BindSliderRow(parent, "LocationBuffer", lr.locationBuffer, v => lr.locationBuffer = v), Tip("Defaults.LocationBuffer", "$sls_cfg_tip_locreset_location_buffer"));
            WithTip(BindToggleRow(parent, "RefillChests", lr.containerDefaultLoot, on => lr.containerDefaultLoot = on), Tip("InPlaceRefresh.ContainerDefaultLoot", "$sls_cfg_tip_locreset_refill_chests"));
            WithTip(BindSliderRow(parent, "SweepBudget", lr.sweepBudgetMs, v => lr.sweepBudgetMs = v), Tip(ValConfig.LocationResetSweepBudgetMs));

            Transform left = ScrollContent(parent, "LeftScroll");
            foreach (Heightmap.Biome biome in ResetRateRows(lr.biomeRates)) {
                Heightmap.Biome b = biome;   // capture per iteration
                float rate = lr.RateFor(b);
                GameObject row = Spawn("SLS_QC_Row_ResetBiomeRate", left);
                SetText(row.transform, "Label", BiomeName(b));
                WithTip(BindSliderRow(row, rate, v => lr.biomeRates[b] = v, max: Mathf.Max(5f, rate)), Tip("BiomeRates", "$sls_cfg_tip_locreset_biome_rate"));
            }

            // Right - every reset group in the file, in file order.
            Transform right = ScrollContent(parent, "RightScroll");
            foreach (KeyValuePair<string, StagedResetGroup> group in lr.groups) {
                AddResetGroupEntry(right, group.Key, group.Value);
            }

            RefreshLocationResetViews();
        }

        private static void ResetLocationResetPage() {
            staged.locationReset.ResetToShipped(ShippedDefaults(YamlConfigManager.LocationResetSettings));
        }

        private static void AddResetGroupEntry(Transform content, string name, StagedResetGroup group) {
            StagedLocationReset lr = staged.locationReset;
            GameObject entry = Spawn("SLS_QC_Row_ResetGroup", content);
            ResetGroupView view = new ResetGroupView { Group = group };

            BindToggle(entry.transform, "Enabled", group.Enabled, on => { group.Enabled = on; RefreshLocationResetViews(); });
            SetText(entry.transform, "Name", Prettify(name));
            view.Tag = Get<Text>(entry.transform, "Tag");
            view.Description = Get<Text>(entry.transform, "Description");

            if (string.IsNullOrWhiteSpace(group.Source.ResetSchedule)) {
                Find(entry.transform, "Scheduled").SetActive(false);
                float hours = lr.HoursFor(group);
                GameObject hoursRow = WithTip(BindSliderRow(Find(entry.transform, "Hours"), hours, v => {
                    group.ResetHours = group.Source.ResetHours.HasValue == false && v == lr.defaultResetHours ? (float?)null : v;
                    RefreshLocationResetViews();
                }, Mathf.Min(MinResetHours, hours), Mathf.Max(MaxResetHours, hours), IsWhole(hours)), Tip("ResetHours", "$sls_cfg_tip_locreset_group_hours"));
                view.HoursSlider = Get<Slider>(hoursRow.transform, "Slider");
                view.HoursField = Get<InputField>(hoursRow.transform, "Value");
            } else {
                // A schedule wins over ResetHours at the same level, so an hours slider here would do nothing. Set on the
                // text directly: the schedule is the admin's own text, and SetText would read a '$' in it as a token.
                Find(entry.transform, "Hours").SetActive(false);
                Get<Text>(entry.transform, "Scheduled").text = L("$sls_cfg_locreset_group_scheduled", group.Source.ResetSchedule);
            }

            GameObject terrainRow = WithTip(BindToggleRow(entry.transform, "Terrain", lr.TerrainFor(group), on => {
                group.ResetTerrain = group.Source.ResetTerrain.HasValue == false && on == lr.defaultResetTerrain ? (bool?)null : on;
                RefreshLocationResetViews();
            }), Tip("ResetTerrain", "$sls_cfg_tip_locreset_group_terrain"));
            view.TerrainToggle = Get<Toggle>(terrainRow.transform, "Toggle");

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

            foreach (Heightmap.Biome biome in ResetRateRows(s.biomeRates)) {
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
