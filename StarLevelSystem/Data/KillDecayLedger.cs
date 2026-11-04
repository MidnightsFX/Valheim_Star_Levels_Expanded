using StarLevelSystem.common;
using StarLevelSystem.modules;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data {
    // An area of the world that levels up from kills and back down with time.
    internal interface ILeveledArea {
        int AreaLevel { get; set; }
        int AreaKills { get; set; }
        double AreaDecayStamp { get; set; }
    }

    internal static class LeveledAreas {
        // A multiplier on every level-up threshold, so it must start at the neutral 1 and only grow: a raw
        // (level - 1) * bonus drops below 1 for any bonus under 1, which makes creatures in a levelled area WEAKER, and jumps
        // from 1 at level 1 to the bare bonus at level 2.
        internal static float BonusFor(int level, float bonusPerLevel) {
            if (level <= 1) { return 1f; }
            return 1f + (level - 1) * bonusPerLevel;
        }
    }

    // The kills, decay and decay clock of one set of areas. Authority-only: clients take levels from the server.
    internal sealed class KillDecayLedger<T> where T : class, ILeveledArea {
        // How often the decay coroutine polls. Decay magnitude is time-based, so this only controls check frequency, not the
        // effective rate.
        private const float DecayTickIntervalSeconds = 900f; // 15 minutes
        // Wall-clock stamps are unix seconds (>1e9 since 2001); net time counts seconds the world has actually been played and
        // would need ~32 years of continuous play to reach that. A stamp's magnitude therefore identifies the clock that wrote
        // it on its own, which is what lets a hand-edited, missing or stale clock marker be caught instead of taken at face value.
        private const double WallClockFloor = 1000000000d;

        private readonly string label;
        private readonly Func<IList<T>> areas;
        private readonly Func<int> killsPerLevel;
        private readonly Func<float> decayPerHour;
        private readonly Action<List<T>> levelsDecayed;
        private readonly Func<bool> save;

        // The clock the in-memory stamps are expressed in. NOT the same thing as the configured clock: the two differ between
        // a config change and the re-base that follows it, and writing the configured value into the save file while the
        // stamps are still in the other clock is exactly what would make the next load trust them and floor every area.
        private ZoneDecayClockSource stampedClock = ZoneDecayClockSource.RealTime;
        // Handle for the running loop so it can be stopped on world unload (TaskRunner is DontDestroyOnLoad, so a running loop
        // would otherwise leak across worlds).
        private Coroutine loop;

        internal bool Dirty;

        internal KillDecayLedger(string label, Func<IList<T>> areas, Func<int> killsPerLevel, Func<float> decayPerHour, Action<List<T>> levelsDecayed, Func<bool> save) {
            this.label = label;
            this.areas = areas;
            this.killsPerLevel = killsPerLevel;
            this.decayPerHour = decayPerHour;
            this.levelsDecayed = levelsDecayed;
            this.save = save;
        }

        // What the stamps in a save file are, for writing beside them.
        internal string StampedClockName => stampedClock.ToString();

        // Record a file's clock only. Deliberately no re-base here: on a dedicated server this runs before ZNet.Start has
        // loaded the world, so ZNet.GetTimeSeconds() is still its 2040 initialiser. Re-basing to that would set the world's
        // decay clock days in the past and floor every area on the next tick. EnsureClockMode does it from the decay loop
        // instead, whose first pass is a full tick interval later.
        internal void AdoptSavedClock(string clockName) {
            stampedClock = clockName == ZoneDecayClockSource.GameTime.ToString() ? ZoneDecayClockSource.GameTime : ZoneDecayClockSource.RealTime;
        }

        // Fresh geometry carries no history, so it simply adopts whatever clock is configured now.
        internal void StampNew(IEnumerable<T> fresh) {
            stampedClock = ConfiguredClock();
            double now = ClockNow(stampedClock);
            foreach (T area in fresh) { area.AreaDecayStamp = now; }
        }

        // Level-ups are computed by threshold crossing, so a batch that jumps past an exact multiple still levels up.
        internal HashSet<T> ApplyKills(IEnumerable<T> killedIn) {
            int threshold = killsPerLevel();
            HashSet<T> leveled = new HashSet<T>();
            foreach (T area in killedIn) {
                Dirty = true;
                int oldKills = area.AreaKills;
                area.AreaKills = oldKills + 1;
                if (threshold <= 0) { continue; }
                int levelsGained = (area.AreaKills / threshold) - (oldKills / threshold);
                if (levelsGained > 0) {
                    area.AreaLevel += levelsGained;
                    // Restart the decay countdown: a kill-driven level-up is recent activity, so decay should measure
                    // inactivity from here (otherwise a stale build-time stamp would let the next decay pass instantly undo it).
                    area.AreaDecayStamp = ClockNow(stampedClock);
                    leveled.Add(area);
                }
            }
            return leveled;
        }

        internal void Start() {
            if (loop != null) { return; }
            // Decay and persistence are authority-only; clients receive level changes via sync.
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            loop = TaskRunner.Run().StartCoroutine(DecayLoop());
        }

        internal void Reset() {
            if (loop != null) {
                TaskRunner.Run().StopCoroutine(loop);
                loop = null;
            }
            Dirty = false;
            stampedClock = ZoneDecayClockSource.RealTime;
        }

        // The decay loop re-bases on its own next tick, so this only makes it happen now rather than up to a tick later.
        internal void OnClockSettingChanged() {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            if (EnsureClockMode()) { Persist(); }
        }

        // Writes the file and clears the dirty flag only once the write has actually happened, so a save that was skipped or
        // failed is retried on the next pass instead of being silently dropped.
        internal void Persist() {
            if (save()) { Dirty = false; }
        }

        internal void FlushIfDirty() {
            if (!Dirty) { return; }
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { return; }
            Persist();
        }

        // Configured clock, resolved defensively. AcceptableValueList constrains the local file, but this value also arrives
        // over the config sync from another peer's build, so an unrecognised string falls back to the legacy wall clock.
        private static ZoneDecayClockSource ConfiguredClock() {
            return ValConfig.ZoneDecayClock.Value == ZoneDecayClockSource.GameTime.ToString()
                ? ZoneDecayClockSource.GameTime : ZoneDecayClockSource.RealTime;
        }

        // ZNet.GetTimeSeconds is the world's net time: real seconds, but only counted while the world is being played, and
        // persisted with the world save. So an hour of it is an hour somebody was actually online for.
        //
        // Deliberately no null-ZNet fallback: every caller runs with a live ZNet, and a silent 0 here would collide with the
        // "no usable stamp" sentinel the decay loop keys on.
        private static double ClockNow(ZoneDecayClockSource clock) {
            return clock == ZoneDecayClockSource.GameTime
                ? ZNet.instance.GetTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        // What the stamps actually are, preferring observed magnitude over the persisted marker so a hand-edited, missing or
        // stale one cannot be trusted blindly. Any single stamped area is evidence: within a session every stamp shares a
        // clock, and the only window where they are mixed is between a config change and the re-base, where either answer
        // re-bases anyway.
        private ZoneDecayClockSource DetectStampedClock() {
            foreach (T area in areas()) {
                if (area.AreaDecayStamp <= 0d) { continue; } // never stamped, carries no evidence
                return area.AreaDecayStamp >= WallClockFloor ? ZoneDecayClockSource.RealTime : ZoneDecayClockSource.GameTime;
            }
            return stampedClock; // nothing stamped yet - trust the marker
        }

        // Keeps the in-memory stamps and the configured clock in the same units, re-basing every area when they diverge.
        // Returns true when it re-based, in which case the caller must skip decay for this pass.
        //
        // This is the whole safety net for the two clocks being numerically incompatible: wall stamps are ~1.7e9 and net-time
        // stamps ~1e3-1e6, so measuring across an unhandled switch either floors every area to level 1 (game -> real) or
        // freezes decay forever (real -> game). Levels are never touched here; only the countdown restarts.
        private bool EnsureClockMode() {
            ZoneDecayClockSource configured = ConfiguredClock();
            ZoneDecayClockSource actual = DetectStampedClock();
            if (actual == configured) {
                stampedClock = configured;
                return false;
            }
            stampedClock = configured;
            double now = ClockNow(configured);
            IList<T> all = areas();
            foreach (T area in all) { area.AreaDecayStamp = now; }
            Dirty = true;
            Logger.LogInfo($"{label} decay clock changed ({actual} -> {configured}). Re-based {all.Count} decay timers to {now:F0}; levels are unchanged.");
            return true;
        }

        private IEnumerator DecayLoop() {
            while (true) {
                yield return new WaitForSeconds(DecayTickIntervalSeconds);
                // TaskRunner is DontDestroyOnLoad, so this loop can outlive a teardown path that missed Reset. ClockNow
                // dereferences ZNet in GameTime mode.
                if (ZNet.instance == null) { continue; }
                DecayPass();
            }
        }

        private void DecayPass() {
            IList<T> all = areas();
            if (all.Count == 0) {
                if (Dirty) { Persist(); }
                return;
            }
            // Re-base before measuring anything: a pass that spanned a clock change would either wipe every area or stop
            // decaying entirely. Persist straight away so a crash before the next tick cannot leave the file's marker
            // disagreeing with its stamps.
            if (EnsureClockMode()) {
                Persist();
                return;
            }

            double now = ClockNow(stampedClock);
            float decayRate = decayPerHour(); // levels lost per hour of the configured clock
            if (decayRate <= 0f) {
                // Decay disabled: keep each decay clock current so toggling it back on doesn't cause a sudden catch-up drop.
                // Persisted rather than in-memory only, or a restart reinstates that drop from the stale stamps still on disk.
                foreach (T area in all) { area.AreaDecayStamp = now; }
                Persist();
                return;
            }
            List<T> changed = new List<T>();
            bool clockRewound = false;
            foreach (T area in all) {
                if (area.AreaLevel <= 1) { continue; }
                // No usable stamp (OmitDefaults drops the field when it is 0). Measuring elapsed time from the epoch would
                // floor the area on the first tick, so adopt now and persist the correction.
                if (area.AreaDecayStamp <= 0) {
                    area.AreaDecayStamp = now;
                    Dirty = true;
                    continue;
                }
                double elapsed = now - area.AreaDecayStamp;
                if (elapsed < 0d) {
                    // Clock ran backwards: a world restored from a backup (net time lives in the world save), a hand-edited
                    // stamp, or a system clock correction. Pull the stamp back to now so the area is not frozen forever; its
                    // level is untouched. Safe to clamp rather than skip because EnsureClockMode already ran, so this cannot be
                    // a units mismatch.
                    area.AreaDecayStamp = now;
                    clockRewound = true;
                    continue;
                }
                double levelsToDecay = (elapsed / 3600.0) * decayRate;
                if (levelsToDecay >= 1.0) {
                    int decayAmount = (int)levelsToDecay;
                    area.AreaLevel = Mathf.Max(1, area.AreaLevel - decayAmount);
                    // Advance the clock only by the time actually consumed so the sub-level remainder carries over.
                    area.AreaDecayStamp += (decayAmount / decayRate) * 3600.0;
                    changed.Add(area);
                }
            }
            if (clockRewound) {
                Logger.LogWarning($"Some {label} decay stamps were ahead of the current clock and were re-based to now. No levels were changed.");
                Dirty = true;
            }
            if (changed.Count > 0) {
                Logger.LogDebug($"{label} levels decayed.");
                Dirty = true;
                levelsDecayed(changed);
            }
            // Flush any kill/decay changes accumulated since the last save.
            if (Dirty) { Persist(); }
        }
    }
}
