using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.modules.UI.QuickConfigPrefabs;

namespace StarLevelSystem.modules.UI {
    // The level system's rings on the Level Scaling page (DistanceLevelBonus in LevelSettings.yaml), the ones drawn on the
    // map. Rings can be added, removed and moved, and each one's bonus edited. The page's chart shows what they do.
    //
    // The add/remove helpers at the bottom are shared with the Loot page's rings.
    internal static partial class QuickConfigureTool {

        // Beyond twice the vanilla world radius a ring is off the edge of even an enlarged world.
        private const int MinRingDistance = 1;
        private static int MaxRingDistance => Mathf.Max(20000, Mathf.CeilToInt(WorldSize.Radius));
        // Where the first ring goes when there are none, and how far apart the next one is when there is only one.
        private const int DefaultLevelRingStep = 1000;
        // A ring's bonus is added to a chance in %, so past 100 either way it can only pin that chance.
        private const float MaxLevelRingBonus = 100f;

        // The scroll content the ring rows live in, rebuilt whenever a ring is added, removed or moves past another.
        private static Transform levelRingContent;
        private static readonly List<GameObject> levelRingRows = new List<GameObject>();
        // The ring the chart shows. Kept by reference, so it follows the ring when a new distance re-sorts the list.
        private static StagedLevelRing shownLevelRing;

        private static void ClearDistanceRingReferences() {
            levelRingContent = null;
            levelRingRows.Clear();
            shownLevelRing = null;
        }

        // ------------------------------------------------------------------------------------------------
        //  Staged configuration
        // ------------------------------------------------------------------------------------------------

        // One ring: from Distance outwards (up to the next ring) a spawn's level-up chances get Bonus added, keyed by
        // level as the chance tables are, so level 1 is the roll from 0 stars to 1.
        private class StagedLevelRing {
            internal int Distance;
            internal SortedDictionary<int, float> Bonus;

            internal bool SameAs(StagedLevelRing other) {
                return other != null && Distance == other.Distance && ChanceTablesEqual(Bonus, other.Bonus);
            }
        }

        // Nearest first, the order the file keeps them in and the order a spawn is matched against them. A ring written
        // with no table at all comes through with an empty one: it still draws on the map, it just adds nothing.
        private static List<StagedLevelRing> LevelRingsOf(CreatureLevelSettings settings) {
            List<StagedLevelRing> rings = new List<StagedLevelRing>();
            if (settings?.DistanceLevelBonus == null) { return rings; }
            foreach (KeyValuePair<int, SortedDictionary<int, float>> ring in settings.DistanceLevelBonus) {
                rings.Add(new StagedLevelRing {
                    Distance = ring.Key,
                    Bonus = ring.Value != null ? new SortedDictionary<int, float>(ring.Value) : new SortedDictionary<int, float>(),
                });
            }
            return rings;
        }

        private static bool LevelRingsMatch(List<StagedLevelRing> a, List<StagedLevelRing> b) {
            if (a == null || b == null) { return a == b; }
            if (a.Count != b.Count) { return false; }
            for (int i = 0; i < a.Count; i++) {
                if (a[i].SameAs(b[i]) == false) { return false; }
            }
            return true;
        }

        private static SortedDictionary<int, SortedDictionary<int, float>> LevelRingsToYaml(List<StagedLevelRing> rings) {
            SortedDictionary<int, SortedDictionary<int, float>> yaml = new SortedDictionary<int, SortedDictionary<int, float>>();
            foreach (StagedLevelRing ring in rings) {
                yaml[ring.Distance] = new SortedDictionary<int, float>(ring.Bonus);
            }
            return yaml;
        }

        private static void ResetDistanceRingsPage() {
            staged.levelRings = LevelRingsOf(ShippedDefaults(YamlConfigManager.LevelSettings));
        }

        // ------------------------------------------------------------------------------------------------
        //  Page
        // ------------------------------------------------------------------------------------------------

        // The list: a row per ring under the header, or the note when there are none. The rows are built again; the header,
        // the note, the help line above them and the Add button below them are the page's own.
        private static void BuildLevelRingRows() {
            foreach (GameObject row in levelRingRows) { DiscardRow(row); }
            levelRingRows.Clear();
            if (levelRingContent == null || staged?.levelRings == null) { return; }

            Transform content = levelRingContent;
            List<StagedLevelRing> rings = staged.levelRings;
            if (shownLevelRing == null || rings.Contains(shownLevelRing) == false) { shownLevelRing = rings.FirstOrDefault(); }

            ShowRingsListState();
            foreach (StagedLevelRing ring in rings) { levelRingRows.Add(AddLevelRingRow(content, ring)); }
        }

        private static GameObject AddLevelRingRow(Transform content, StagedLevelRing ring) {
            GameObject row = SpawnRingRow("SLS_QC_Row_LevelRing", content);

            InputField distance = null;
            distance = BindField(row.transform, "Distance", ring.Distance.ToString(CultureInfo.InvariantCulture),
                text => CommitLevelRingDistance(ring, distance, text));
            WithTip(distance.gameObject, Tip("$sls_cfg_tip_distance_title", L("$sls_cfg_tip_level_ring_distance", MinRingDistance, MaxRingDistance)));

            InputField bonus = null;
            bonus = BindField(row.transform, "Bonus", FormatRingBonus(ring.Bonus),
                text => CommitLevelRingBonus(ring, bonus, text));
            WithTip(bonus.gameObject, Tip("DistanceLevelBonus", "$sls_cfg_tip_level_ring_bonus"));

            BindRingRemoveButton(row.transform, () => {
                staged.levelRings.Remove(ring);
                BuildLevelRingRows();
                RefreshLevelScaling();
            });
            return row;
        }

        private static void CommitLevelRingDistance(StagedLevelRing ring, InputField field, string text) {
            if (staged?.levelRings == null) { return; }
            int distance = ReadRingDistance(text, ring.Distance, staged.levelRings.Where(r => r != ring).Select(r => r.Distance), "$sls_cfg_rings_distance_taken");
            field.SetTextWithoutNotify(distance.ToString(CultureInfo.InvariantCulture));
            if (distance == ring.Distance) { return; }
            ring.Distance = distance;
            // Only when it passed another ring: rebuilding under a box that was just clicked into would take the click.
            if (SortByDistance(staged.levelRings, r => r.Distance)) { BuildLevelRingRows(); }
            RefreshLevelScaling();
        }

        // An unreadable entry leaves the ring as it was and says why, rather than keeping text that is not what is staged.
        private static void CommitLevelRingBonus(StagedLevelRing ring, InputField field, string text) {
            SortedDictionary<int, float> parsed = ParseRingBonus(text, out string error);
            if (parsed == null) {
                string kept = ring.Bonus.Count > 0 ? FormatRingBonus(ring.Bonus) : ConfigUI.L("$sls_cfg_rings_no_bonus");
                SetStatus(error + " " + L("$sls_cfg_rings_bonus_kept", ring.Distance, kept), false);
            } else {
                ring.Bonus = parsed;
            }
            field.SetTextWithoutNotify(FormatRingBonus(ring.Bonus));
            RefreshLevelScaling();
        }

        private static void AddLevelRing() {
            if (staged?.levelRings == null) { return; }
            List<StagedLevelRing> rings = staged.levelRings;
            if (NextRingDistance(rings.Select(r => r.Distance).ToList(), DefaultLevelRingStep, out int distance) == false) {
                SetStatus(L("$sls_cfg_rings_no_room", rings[rings.Count - 1].Distance), false);
                return;
            }
            StagedLevelRing last = rings.LastOrDefault();
            StagedLevelRing added = new StagedLevelRing {
                Distance = distance,
                Bonus = last != null ? new SortedDictionary<int, float>(last.Bonus) : new SortedDictionary<int, float> { { 1, 10f }, { 2, 5f } },
            };
            rings.Add(added);
            shownLevelRing = added;
            BuildLevelRingRows();
            RefreshLevelScaling();
        }

        // "15, 5, 1" when the bonus runs from level 1 with no gap, which is how every shipped ring is written; level:value
        // pairs otherwise, so nothing the file holds is lost by showing it here.
        private static string FormatRingBonus(SortedDictionary<int, float> bonus) {
            if (bonus == null || bonus.Count == 0) { return ""; }
            bool fromOne = bonus.Keys.Select((level, i) => level == i + 1).All(same => same);
            return string.Join(", ", bonus.Select(entry => fromOne
                ? FormatRingValue(entry.Value)
                : $"{entry.Key}:{FormatRingValue(entry.Value)}").ToArray());
        }

        private static string FormatRingValue(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        // Reads either form FormatRingBonus writes, mixed if need be: a bare value takes the level after the one before
        // it, starting from level 1. Null, with the reason, when the text cannot be read.
        private static SortedDictionary<int, float> ParseRingBonus(string text, out string error) {
            error = null;
            SortedDictionary<int, float> bonus = new SortedDictionary<int, float>();
            if (string.IsNullOrWhiteSpace(text)) { return bonus; }
            int level = 0;
            foreach (string part in text.Split(',')) {
                string token = part.Trim();
                if (token.Length == 0) { continue; }
                string valueText = token;
                int colon = token.IndexOf(':');
                if (colon >= 0) {
                    if (int.TryParse(token.Substring(0, colon).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int typedLevel) == false || typedLevel < 1) {
                        error = L("$sls_cfg_rings_bad_level", token);
                        return null;
                    }
                    level = typedLevel;
                    valueText = token.Substring(colon + 1).Trim();
                } else {
                    level++;
                }
                if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) == false) {
                    error = L("$sls_cfg_rings_unreadable", token);
                    return null;
                }
                if (bonus.ContainsKey(level)) {
                    error = L("$sls_cfg_rings_level_twice", level);
                    return null;
                }
                bonus[level] = Mathf.Clamp(value, -MaxLevelRingBonus, MaxLevelRingBonus);
            }
            return bonus;
        }

        // ------------------------------------------------------------------------------------------------
        //  Preview
        // ------------------------------------------------------------------------------------------------

        // Keys are levels, so level 1 and below is no stars.
        private static float ShareWithStars(SortedDictionary<int, float> distribution, int stars) {
            return distribution.Where(entry => entry.Key - 1 >= stars).Sum(entry => entry.Value);
        }

        private static float AverageStars(SortedDictionary<int, float> distribution) {
            return distribution.Sum(entry => Mathf.Max(0, entry.Key - 1) * entry.Value);
        }

        private static string FormatShare(float share) {
            float pct = share * 100f;
            return pct > 0f && pct < 0.1f ? "<0.1%" : pct.ToString(pct < 10f ? "0.#" : "0", CultureInfo.InvariantCulture) + "%";
        }

        // ------------------------------------------------------------------------------------------------
        //  Ring lists, shared with the Loot page
        // ------------------------------------------------------------------------------------------------

        // A typed ring distance, clamped to the range a ring can have. Anything unreadable, or a distance another ring
        // already has, keeps the current one; the second says why on the status line.
        // taken is the token for the status line, filled with the distance asked for and the one kept.
        private static int ReadRingDistance(string text, int current, IEnumerable<int> others, string taken) {
            if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int typed) == false) { return current; }
            int distance = Mathf.Clamp(typed, MinRingDistance, MaxRingDistance);
            if (distance != current && others.Contains(distance)) {
                SetStatus(L(taken, distance, current), false);
                return current;
            }
            return distance;
        }

        // Past the furthest ring by as much as the last two are apart (or by step when there are fewer than two). False
        // when the furthest ring is already at the limit.
        private static bool NextRingDistance(List<int> ascending, int step, out int distance) {
            if (ascending.Count == 0) {
                distance = step;
                return true;
            }
            int last = ascending[ascending.Count - 1];
            int gap = ascending.Count >= 2 ? last - ascending[ascending.Count - 2] : step;
            distance = Mathf.Min(last + Mathf.Max(1, gap), MaxRingDistance);
            return distance > last;
        }

        // Nearest first. True when that moved a ring, so its rows have to be laid out again.
        private static bool SortByDistance<T>(List<T> rings, Func<T, int> distance) {
            List<T> sorted = rings.OrderBy(distance).ToList();
            if (sorted.SequenceEqual(rings)) { return false; }
            rings.Clear();
            rings.AddRange(sorted);
            return true;
        }

        // A ring's row, ahead of the marker its page keeps for them among the rows that stay.
        private static GameObject SpawnRingRow(string prefab, Transform content) {
            Transform marker = Find(content, "RingRows").transform;
            GameObject row = Spawn(prefab, content);
            row.transform.SetSiblingIndex(marker.GetSiblingIndex());
            return row;
        }

        private static void BindRingRemoveButton(Transform row, UnityEngine.Events.UnityAction onClick) {
            WithTip(BindButton(row, "Remove", onClick),
                Tip("$sls_cfg_tip_remove_ring_title", "$sls_cfg_tip_remove_ring"));
        }

        // Hidden now, destroyed at the end of the frame: until then a layout group would still make room for it.
        private static void DiscardRow(GameObject row) {
            if (row == null) { return; }
            row.SetActive(false);
            UnityEngine.Object.Destroy(row);
        }
    }
}
