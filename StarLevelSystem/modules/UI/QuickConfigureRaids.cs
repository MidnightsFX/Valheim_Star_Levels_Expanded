using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Raids;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.modules.UI.QuickConfigPrefabs;

namespace StarLevelSystem.modules.UI {
    // The Raids page of the quick configure panel: the global raid settings, which raids are enabled, and per creature
    // in a raid how many arrive each wave, how many may be alive at once, and how long a wave lasts. Spawn chances,
    // levels, modifiers and activation keys stay in RaidSettings.yaml.
    internal static partial class QuickConfigureTool {

        private const int MaxSpawnGroupSize = 50;
        private const int MaxSpawnAlive = 999;
        private const float MinSpawnInterval = 1f;
        private const float MaxSpawnInterval = 3600f;

        private const int MinRaidDensity = 1;
        private const int MaxRaidDensity = 6;
        // The density RaidsData's raids are written at, and what a file with no density stamp sits at. New files ship
        // at the density RaidsData.DefaultConfiguration names instead.
        internal const int StandardRaidDensity = 3;

        // Ends of the two global multipliers' sliders (the chance one starts at 0). A file value outside them widens the slider.
        private const float MaxRaidChanceScalar = 2f;
        private const float MinRaidIntervalScalar = 0.1f;
        private const float MaxRaidIntervalScalar = 10f;
        // Top of the end-of-raid pause slider, in seconds. A file value above it widens the slider.
        private const int MaxRaidPauseBeforeCompleted = 300;

        // Density 1-6 to the share of each raid's configured creature counts that is actually used. 3 is the numbers the
        // raids are written with, which are already well above vanilla; 1 cuts them to roughly the size of a vanilla raid
        // and 6 triples them. Kept as a hand-written table rather than a curve so each step is a deliberate difficulty.
        private static readonly float[] RaidDensityScale = { 0.4f, 0.65f, 1f, 1.6f, 2.2f, 3f };
        private static readonly string[] RaidDensityNames = {
            "$sls_cfg_raids_density_vanilla", "$sls_cfg_raids_density_light", "$sls_cfg_raids_density_standard",
            "$sls_cfg_raids_density_heavy", "$sls_cfg_raids_density_brutal", "$sls_cfg_raids_density_impossible",
        };

        internal static int ClampRaidDensity(int density) => Mathf.Clamp(density, MinRaidDensity, MaxRaidDensity);

        private static float RaidDensityScalar(int density) => RaidDensityScale[ClampRaidDensity(density) - 1];

        // What the staged counts are multiplied by relative to the numbers in the file. 1 while the slider has not been
        // moved off the density the file was written at.
        private static float RaidDensityRatio() {
            return RaidDensityRatio(staged.raidDensityBase, staged.raidDensity);
        }

        // What counts written at one density are multiplied by to sit at another.
        private static float RaidDensityRatio(int fromDensity, int toDensity) {
            return RaidDensityScalar(toDensity) / RaidDensityScalar(fromDensity);
        }

        // Never below 1: thinning a raid out must not silently switch a creature off. An entry already at 0 is off on
        // purpose (RaidRunner skips it entirely) and is left there.
        //
        // Rounding and the clamp make this lossy across a save: a count written at a low density no longer says what it
        // was scaled from (at Light a 1 may have been a 1 or a 2), so scaling it back up in a LATER session guesses.
        // Spawns that still hold a shipped raid's numbers avoid that by scaling from RaidsData's standard counts
        // instead; only counts an admin changed, and raids the mod does not ship, scale from the file and can drift.
        // The alternative is scaling at spawn time, which is not what this setting does.
        private static int ScaleSpawnCount(int count, float ratio, int max) {
            if (count <= 0) { return count; }
            return Mathf.Clamp(Mathf.RoundToInt(count * ratio), 1, max);
        }

        // Moves one raid's counts from the density they were written at to another, the way the slider would. For the
        // shipped raids, from the standard density to the one new files ship at or a schema update adds them to.
        internal static void ScaleRaidToDensity(RaidDefinition raid, int fromDensity, int toDensity) {
            if (raid?.Spawns == null) { return; }
            float ratio = RaidDensityRatio(fromDensity, toDensity);
            if (Mathf.Approximately(ratio, 1f)) { return; }
            foreach (RaidSpawnEntry entry in raid.Spawns) {
                if (entry == null) { continue; }
                entry.SpawnGroupSize = ScaleSpawnCount(entry.SpawnGroupSize, ratio, MaxSpawnGroupSize);
                entry.MaxSpawned = ScaleSpawnCount(entry.MaxSpawned, ratio, MaxSpawnAlive);
            }
        }

        // One creature line of one raid. Keyed by position in the raid list rather than by name, because nothing stops
        // two raids sharing a name, and PrefabName is kept so a file that changed underneath us is not written blind.
        private class StagedRaidSpawn {
            internal string PrefabName;
            internal int GroupSize;
            internal int MaxAlive;
            internal float Interval;
            // The two counts as the file holds them, which is what the density slider scales from. Keeping them means
            // sliding away and back lands on the original numbers instead of compounding rounding each step.
            internal int FileGroupSize;
            internal int FileMaxAlive;
            // Set while the file's counts are a shipped raid's, scaled to the file's density: the slider scales these
            // standard-density counts instead, so a count rounded or clamped on the way down comes back exactly.
            internal bool HasStandardCounts;
            internal int StandardGroupSize;
            internal int StandardMaxAlive;

            internal bool SameAs(StagedRaidSpawn other) {
                return other != null && GroupSize == other.GroupSize && MaxAlive == other.MaxAlive && Interval == other.Interval;
            }
        }

        private static string RaidSpawnKey(int raidIndex, int spawnIndex) => $"{raidIndex}:{spawnIndex}";

        // fileDensity is the density the source's counts sit at.
        private static Dictionary<string, StagedRaidSpawn> SnapshotRaidSpawns(RaidConfiguration source, int fileDensity) {
            Dictionary<string, StagedRaidSpawn> spawns = new Dictionary<string, StagedRaidSpawn>();
            if (source?.Raids == null) { return spawns; }
            float fromStandard = RaidDensityRatio(StandardRaidDensity, fileDensity);
            for (int raidIndex = 0; raidIndex < source.Raids.Count; raidIndex++) {
                string raidName = source.Raids[raidIndex]?.Name;
                List<RaidSpawnEntry> entries = source.Raids[raidIndex]?.Spawns;
                if (entries == null) { continue; }
                for (int spawnIndex = 0; spawnIndex < entries.Count; spawnIndex++) {
                    RaidSpawnEntry entry = entries[spawnIndex];
                    if (entry == null) { continue; }
                    StagedRaidSpawn spawn = new StagedRaidSpawn {
                        PrefabName = entry.PrefabName,
                        GroupSize = entry.SpawnGroupSize,
                        MaxAlive = entry.MaxSpawned,
                        Interval = entry.SpawnInterval,
                        FileGroupSize = entry.SpawnGroupSize,
                        FileMaxAlive = entry.MaxSpawned,
                    };
                    // Only while both counts are exactly what the slider would have made of the shipped ones at this
                    // density. Anything else was edited by hand and keeps scaling from the file.
                    if (RaidsData.TryGetStandardCounts(raidName, spawnIndex, entry.PrefabName, out int standardGroup, out int standardMax)
                        && ScaleSpawnCount(standardGroup, fromStandard, MaxSpawnGroupSize) == entry.SpawnGroupSize
                        && ScaleSpawnCount(standardMax, fromStandard, MaxSpawnAlive) == entry.MaxSpawned) {
                        spawn.HasStandardCounts = true;
                        spawn.StandardGroupSize = standardGroup;
                        spawn.StandardMaxAlive = standardMax;
                    }
                    spawns[RaidSpawnKey(raidIndex, spawnIndex)] = spawn;
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

        // The GlobalSettings values the page edits, other than the density.
        private static bool RaidGlobalsMatch(StagedConfig a, StagedConfig b) {
            return a.disableAllRaids == b.disableAllRaids && a.raidChanceScalar == b.raidChanceScalar && a.raidIntervalScalar == b.raidIntervalScalar
                && a.raidPauseBeforeCompleted == b.raidPauseBeforeCompleted;
        }

        // ------------------------------------------------------------------------------------------------
        //  Page
        // ------------------------------------------------------------------------------------------------

        // Live views the density slider drives. Cleared with the rest of the page references when the panel closes.
        private class RaidSpawnFields {
            internal StagedRaidSpawn Spawn;
            internal InputField Group;
            internal InputField Max;
        }

        private static readonly List<RaidSpawnFields> raidSpawnFields = new List<RaidSpawnFields>();
        private static Text raidDensityNote;

        private static void ClearRaidPageReferences() {
            raidSpawnFields.Clear();
            raidDensityNote = null;
        }

        private static void BuildRaidsPage(Transform parent) {
            // Left column - global raid settings. The pause, the two multipliers, the end-of-raid pause and the density
            // are RaidSettings.yaml's GlobalSettings; the rest are .cfg entries.
            WithTip(BindToggleRow(parent, "EnableRaids", staged.enableSlsRaids, v => staged.enableSlsRaids = v), Tip("UseVanillaRaidConfiguration", "$sls_cfg_tip_raids_enable"));
            WithTip(BindToggleRow(parent, "PauseRaids", staged.disableAllRaids, v => staged.disableAllRaids = v),
                Tip("DisableAllRaids", "$sls_cfg_tip_raids_pause"));
            WithTip(BindSliderRow(parent, "ChanceMultiplier", staged.raidChanceScalar, v => staged.raidChanceScalar = v, max: Mathf.Max(MaxRaidChanceScalar, staged.raidChanceScalar)),
                Tip("GlobalRaidChanceScalar", "$sls_cfg_tip_raids_chance_multiplier"));
            WithTip(BindSliderRow(parent, "CooldownMultiplier", staged.raidIntervalScalar, v => staged.raidIntervalScalar = v, Mathf.Min(MinRaidIntervalScalar, staged.raidIntervalScalar), Mathf.Max(MaxRaidIntervalScalar, staged.raidIntervalScalar)),
                Tip("GlobalRaidIntervalScalar", "$sls_cfg_tip_raids_cooldown_multiplier"));
            WithTip(BindSliderRow(parent, "CheckMinutes", staged.raidCheckMinutes, v => staged.raidCheckMinutes = (int)v), Tip(ValConfig.ServerTimeBetweenRaidStartChecks));
            WithTip(BindSliderRow(parent, "MaxAttempts", staged.maxRaidAttempts, v => staged.maxRaidAttempts = (int)v), Tip(ValConfig.MaxRaidAttemptsPerPlayer));
            WithTip(BindSliderRow(parent, "MaxActive", staged.maxActiveRaids, v => staged.maxActiveRaids = (int)v, Mathf.Min(1f, staged.maxActiveRaids), Mathf.Max(20f, staged.maxActiveRaids)), Tip(ValConfig.MaxActiveRaids));
            WithTip(BindSliderRow(parent, "PauseBeforeCompleted", Mathf.Max(0, staged.raidPauseBeforeCompleted), v => staged.raidPauseBeforeCompleted = (int)v, max: Mathf.Max(MaxRaidPauseBeforeCompleted, staged.raidPauseBeforeCompleted)),
                Tip("PauseBeforeCompleted", "$sls_cfg_tip_raids_pause_before_completed"));
            WithTip(BindSliderRow(parent, "Density", staged.raidDensity, v => OnRaidDensityChanged((int)v)),
                Tip("RaidCreatureDensity", L("$sls_cfg_tip_raids_density", MinRaidDensity, MaxRaidDensity, StandardRaidDensity)));

            raidDensityNote = Get<Text>(parent, "DensityNote");
            RefreshRaidDensityNote();

            // The world's own Raids modifier scales every raid on top of everything above, and is set when the world is
            // made rather than here, so it is easy to forget it is there.
            SetText(parent, "WorldNote", WorldRaidModifierNote()).color =
                RaidControl.WorldRaidsOff && ZoneSystem.instance != null ? GUIManager.Instance.ValheimOrange : GUIManager.Instance.ValheimBeige;

            // Right side - scrollable list of every configured raid, each with an enable/disable toggle and its spawns
            // behind a Spawns button. Disabled raids keep their config in RaidSettings.yaml and are marked Enabled = false.
            Transform content = ScrollContent(parent, "RaidList");
            List<RaidDefinition> raids = staged.raidSource?.Raids;
            if (content == null || raids == null) { return; }

            // Listed by name, but each raid keeps the index it has in the file, which is what the staged spawns are
            // keyed by.
            List<int> order = Enumerable.Range(0, raids.Count).Where(i => raids[i] != null).OrderBy(i => raids[i].Name).ToList();
            foreach (int raidIndex in order) {
                AddRaidEntry(content, raidIndex, raids[raidIndex]);
            }
        }

        private static void AddRaidEntry(Transform content, int raidIndex, RaidDefinition raid) {
            string raidName = raid.Name;   // capture for the closures

            GameObject header = Spawn("SLS_QC_Row_RaidEntry", content);
            WithTip(header, Tip(raidName, L("$sls_cfg_tip_raid_entry", raid.Duration.ToString("0"), raid.RaidCoolDownMinutes.ToString("0"))));
            BindToggle(header.transform, "Toggle", staged.raidsOn.Contains(raidName), on => {
                if (on) { staged.raidsOn.Add(raidName); }
                else { staged.raidsOn.Remove(raidName); }
            });
            SetText(header.transform, "Name", PrettifyRaidName(raidName));
            int types = raid.Spawns != null ? raid.Spawns.Select(sp => sp.PrefabName).Distinct().Count() : 0;
            string sub = $"{(types == 1 ? ConfigUI.L("$sls_cfg_raids_creature_types_one") : L("$sls_cfg_raids_creature_types", types))}  ·  {raid.Duration:0}s";
            SetText(header.transform, "Details", sub);

            // The spawn rows are separate children of the scroll content, so hiding them collapses the space they took:
            // a vertical layout group skips inactive children. With 50-odd spawns across the shipped raids, listing them
            // all at once would bury the raid list itself.
            List<GameObject> spawnRows = new List<GameObject>();
            if (raid.Spawns != null && raid.Spawns.Count > 0) {
                spawnRows.Add(Spawn("SLS_QC_Row_RaidSpawnHeader", content));
                for (int spawnIndex = 0; spawnIndex < raid.Spawns.Count; spawnIndex++) {
                    if (staged.raidSpawns.TryGetValue(RaidSpawnKey(raidIndex, spawnIndex), out StagedRaidSpawn spawn) == false) { continue; }
                    spawnRows.Add(AddRaidSpawnRow(content, spawn));
                }
            }
            foreach (GameObject row in spawnRows) { row.SetActive(false); }

            GameObject button = Find(header.transform, "Spawns");
            if (spawnRows.Count == 0) {
                button.SetActive(false);
                return;
            }
            bool expanded = false;
            Text caption = button.GetComponentInChildren<Text>(true);
            button.GetComponent<Button>().onClick.AddListener(() => {
                expanded = !expanded;
                foreach (GameObject row in spawnRows) { row.SetActive(expanded); }
                caption.text = ConfigUI.L(expanded ? "$sls_cfg_raids_hide_spawns" : "$sls_cfg_raids_show_spawns");
            });
        }

        private static GameObject AddRaidSpawnRow(Transform content, StagedRaidSpawn spawn) {
            GameObject row = Spawn("SLS_QC_Row_RaidSpawn", content);
            SetText(row.transform, "Name", PrettyPrefab(spawn.PrefabName));

            InputField group = null, max = null, interval = null;
            group = BindField(row.transform, "Group", spawn.GroupSize.ToString(), text => {
                if (int.TryParse(text, out int value)) { spawn.GroupSize = Mathf.Clamp(value, 1, MaxSpawnGroupSize); }
                group.SetTextWithoutNotify(spawn.GroupSize.ToString());
            });
            WithTip(group.gameObject, Tip("SpawnGroupSize", "$sls_cfg_tip_raids_spawn_each"));
            max = BindField(row.transform, "Max", spawn.MaxAlive.ToString(), text => {
                if (int.TryParse(text, out int value)) { spawn.MaxAlive = Mathf.Clamp(value, 0, MaxSpawnAlive); }
                max.SetTextWithoutNotify(spawn.MaxAlive.ToString());
            });
            WithTip(max.gameObject, Tip("MaxSpawned", "$sls_cfg_tip_raids_spawn_max_alive"));
            interval = BindField(row.transform, "Interval", FormatInterval(spawn.Interval), text => {
                if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) {
                    spawn.Interval = Mathf.Clamp(value, MinSpawnInterval, MaxSpawnInterval);
                }
                interval.SetTextWithoutNotify(FormatInterval(spawn.Interval));
            });
            WithTip(interval.gameObject, Tip("SpawnInterval", "$sls_cfg_tip_raids_spawn_every"));
            raidSpawnFields.Add(new RaidSpawnFields { Spawn = spawn, Group = group, Max = max });
            return row;
        }

        private static string FormatInterval(float seconds) {
            return seconds.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------------------------------------
        //  Creature density
        // ------------------------------------------------------------------------------------------------

        private static void OnRaidDensityChanged(int density) {
            if (staged == null) { return; }
            density = ClampRaidDensity(density);
            if (staged.raidDensity == density) { return; }
            staged.raidDensity = density;

            // Re-derived from the file's numbers rather than from what the boxes hold, so the slider never compounds
            // its own rounding, and for a shipped raid's spawn from the numbers it was designed with. Anything typed into
            // Each or Max alive before the slider moved is re-derived with the rest -- the slider sets all of them, and
            // the boxes are for fine tuning afterwards.
            float ratio = RaidDensityRatio();
            float fromStandard = RaidDensityRatio(StandardRaidDensity, density);
            foreach (StagedRaidSpawn spawn in staged.raidSpawns.Values) {
                if (spawn.HasStandardCounts) {
                    spawn.GroupSize = ScaleSpawnCount(spawn.StandardGroupSize, fromStandard, MaxSpawnGroupSize);
                    spawn.MaxAlive = ScaleSpawnCount(spawn.StandardMaxAlive, fromStandard, MaxSpawnAlive);
                } else {
                    spawn.GroupSize = ScaleSpawnCount(spawn.FileGroupSize, ratio, MaxSpawnGroupSize);
                    spawn.MaxAlive = ScaleSpawnCount(spawn.FileMaxAlive, ratio, MaxSpawnAlive);
                }
            }

            foreach (RaidSpawnFields fields in raidSpawnFields) {
                fields.Group.SetTextWithoutNotify(fields.Spawn.GroupSize.ToString());
                fields.Max.SetTextWithoutNotify(fields.Spawn.MaxAlive.ToString());
            }
            RefreshRaidDensityNote();
        }

        // ------------------------------------------------------------------------------------------------
        //  Reset page
        // ------------------------------------------------------------------------------------------------

        // The global settings, each raid's on/off and every spawn's numbers go back to the shipped file. Raids and
        // spawns are matched to it by name, as listed; a raid the shipped file does not have keeps its settings, and its
        // counts follow the density back to the shipped one the way the slider would move them.
        private static void ResetRaidsPage() {
            staged.enableSlsRaids = !DefaultOf(ValConfig.UseVanillaRaidConfiguration);
            staged.raidCheckMinutes = DefaultOf(ValConfig.ServerTimeBetweenRaidStartChecks);
            staged.maxRaidAttempts = DefaultOf(ValConfig.MaxRaidAttemptsPerPlayer);
            staged.maxActiveRaids = DefaultOf(ValConfig.MaxActiveRaids);

            RaidConfiguration shipped = ShippedDefaults(YamlConfigManager.RaidSettings);
            GlobalRaidSettings shippedGlobal = shipped?.GlobalSettings ?? RaidsData.DefaultConfiguration.GlobalSettings;
            staged.disableAllRaids = shippedGlobal.DisableAllRaids;
            staged.raidChanceScalar = shippedGlobal.GlobalRaidChanceScalar;
            staged.raidIntervalScalar = shippedGlobal.GlobalRaidIntervalScalar;
            staged.raidPauseBeforeCompleted = shippedGlobal.PauseBeforeCompleted;
            int density = ClampRaidDensity(shippedGlobal.RaidCreatureDensity);
            // For the unmatched spawns: their file numbers sit at the old base density, and are carried to the new one.
            float carry = RaidDensityRatio(staged.raidDensityBase, density);
            float fromStandard = RaidDensityRatio(StandardRaidDensity, density);
            List<RaidDefinition> raids = staged.raidSource?.Raids;
            if (raids != null) {
                for (int raidIndex = 0; raidIndex < raids.Count; raidIndex++) {
                    RaidDefinition raid = raids[raidIndex];
                    if (raid == null) { continue; }
                    RaidDefinition shippedRaid = shipped?.Raids?.FirstOrDefault(r => r != null && r.Name == raid.Name);
                    if (shippedRaid != null) {
                        if (shippedRaid.Enabled) { staged.raidsOn.Add(raid.Name); } else { staged.raidsOn.Remove(raid.Name); }
                    }
                    if (raid.Spawns == null) { continue; }
                    for (int spawnIndex = 0; spawnIndex < raid.Spawns.Count; spawnIndex++) {
                        if (staged.raidSpawns.TryGetValue(RaidSpawnKey(raidIndex, spawnIndex), out StagedRaidSpawn spawn) == false) { continue; }
                        RaidSpawnEntry shippedSpawn = shippedRaid?.Spawns != null && spawnIndex < shippedRaid.Spawns.Count ? shippedRaid.Spawns[spawnIndex] : null;
                        if (shippedSpawn != null && string.Equals(shippedSpawn.PrefabName, spawn.PrefabName, System.StringComparison.Ordinal)) {
                            spawn.FileGroupSize = shippedSpawn.SpawnGroupSize;
                            spawn.FileMaxAlive = shippedSpawn.MaxSpawned;
                            spawn.Interval = shippedSpawn.SpawnInterval;
                            spawn.HasStandardCounts = RaidsData.TryGetStandardCounts(raid.Name, spawnIndex, spawn.PrefabName, out spawn.StandardGroupSize, out spawn.StandardMaxAlive);
                        } else if (spawn.HasStandardCounts) {
                            // Only when the shipped file could not be read: the standard counts still say where it would be.
                            spawn.FileGroupSize = ScaleSpawnCount(spawn.StandardGroupSize, fromStandard, MaxSpawnGroupSize);
                            spawn.FileMaxAlive = ScaleSpawnCount(spawn.StandardMaxAlive, fromStandard, MaxSpawnAlive);
                        } else {
                            spawn.FileGroupSize = ScaleSpawnCount(spawn.FileGroupSize, carry, MaxSpawnGroupSize);
                            spawn.FileMaxAlive = ScaleSpawnCount(spawn.FileMaxAlive, carry, MaxSpawnAlive);
                        }
                        spawn.GroupSize = spawn.FileGroupSize;
                        spawn.MaxAlive = spawn.FileMaxAlive;
                    }
                }
            }
            // Every count above now sits at the shipped density, so that is the base the slider scales from.
            staged.raidDensity = density;
            staged.raidDensityBase = density;
        }

        // Read on the client like every other global key, so a remote admin sees the server's value. Outside a world
        // there is none to show, and the last world's rate is still in Game.m_eventRate.
        private static string WorldRaidModifierNote() {
            if (ZoneSystem.instance == null) {
                return ConfigUI.L("$sls_cfg_raids_world_note_menu");
            }
            float rate = RaidControl.WorldRaidRate;
            if (RaidControl.WorldRaidsOff) {
                return L("$sls_cfg_raids_world_note_off", ConfigUI.L("$menu_none"));
            }
            if (Mathf.Approximately(rate, 1f)) {
                return L("$sls_cfg_raids_world_note_normal", ConfigUI.L("$menu_modifier_normal"));
            }
            string cooldown = rate.ToString("0.##", CultureInfo.InvariantCulture);
            string chance = (1f / rate).ToString("0.##", CultureInfo.InvariantCulture);
            return L("$sls_cfg_raids_world_note", WorldRaidModifierName(rate), cooldown, chance);
        }

        // Vanilla's names for the presets of its Raids modifier; anything else was set with setkey.
        private static string WorldRaidModifierName(float rate) {
            if (Mathf.Approximately(rate, 2f)) { return ConfigUI.L("$menu_muchless"); }
            if (Mathf.Approximately(rate, 1.5f)) { return ConfigUI.L("$menu_less"); }
            if (Mathf.Approximately(rate, 0.6f)) { return ConfigUI.L("$menu_more"); }
            if (Mathf.Approximately(rate, 0.3f)) { return ConfigUI.L("$menu_muchmore"); }
            return ConfigUI.L("$sls_cfg_raids_world_custom");
        }

        private static void RefreshRaidDensityNote() {
            if (raidDensityNote == null || staged == null) { return; }
            float ratio = RaidDensityRatio();
            string name = ConfigUI.L(RaidDensityNames[ClampRaidDensity(staged.raidDensity) - 1]);
            raidDensityNote.text = Mathf.Approximately(ratio, 1f)
                ? L("$sls_cfg_raids_density_note_file", name)
                : L("$sls_cfg_raids_density_note_scaled", name, ratio.ToString("0.##", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------------------------------------
        //  Save
        // ------------------------------------------------------------------------------------------------

        // The global switch and multipliers, per-raid enable/disable, the three spawn numbers this page shows and the
        // density they were scaled to; every other per-raid and per-spawn setting is preserved.
        private static void SaveRaids(List<string> failures, List<string> warnings) {
            RaidConfiguration live = RaidsData.SLE_Raid_Settings;
            if (live?.Raids == null) { return; }
            if (staged.raidDensity == baseline.raidDensity
                && RaidGlobalsMatch(staged, baseline)
                && SetsEqual(staged.raidsOn, baseline.raidsOn)
                && RaidSpawnsMatch(staged.raidSpawns, baseline.raidSpawns)) { return; }

            RaidConfiguration copy = CopyForEdit(YamlConfigManager.RaidSettings, live);
            if (copy.GlobalSettings == null) { copy.GlobalSettings = new GlobalRaidSettings(); }
            copy.GlobalSettings.DisableAllRaids = staged.disableAllRaids;
            copy.GlobalSettings.GlobalRaidChanceScalar = staged.raidChanceScalar;
            copy.GlobalSettings.GlobalRaidIntervalScalar = staged.raidIntervalScalar;
            copy.GlobalSettings.PauseBeforeCompleted = staged.raidPauseBeforeCompleted;
            // The counts written below are the ones this density produced, so the stamp has to go with them: the next
            // slider move reads it back as the density the file's numbers sit at.
            copy.GlobalSettings.RaidCreatureDensity = staged.raidDensity;
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
            SaveYaml(YamlConfigManager.RaidSettings, copy, "$sls_cfg_label_raid_settings", failures, warnings);
        }
    }
}
