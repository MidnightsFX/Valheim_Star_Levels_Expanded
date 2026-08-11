using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarLevelSystem.common {
    // A parsed 5-field cron expression, used by Location Reset as an alternative to an elapsed-hours
    // interval: "0 3 * * *" is 03:00 every day, where ResetHours: 24 is "24h after the last reset"
    // and therefore drifts a little later each cycle.
    //
    // Hand-rolled rather than a NuGet dependency on purpose. The mod ships exactly one file
    // (Package/plugins holds only StarLevelSystem.dll; YamlDotNet and Jotunn come from the game and
    // Jotunn at runtime), so taking NCrontab or Cronos would mean shipping and loading a second
    // assembly for what fits in one source file.
    //
    // Fields, in order: minute hour day-of-month month day-of-week
    //
    //   *            every value
    //   5            just that value
    //   1,5,9        a list
    //   1-5          an inclusive range
    //   */15         every 15th value from the start of the range
    //   1-20/4       every 4th value across a range
    //
    // Day-of-week is 0-6 with 0 = Sunday; 7 is also accepted for Sunday, as are SUN..SAT. Months
    // accept 1-12 and JAN..DEC. Names are case-insensitive.
    //
    // Macros: @hourly @daily @midnight @weekly @monthly @yearly @annually
    //
    // Evaluated in the SERVER'S LOCAL TIME, because "0 3 * * *" from an admin means 3am where the
    // server is, not 3am UTC. Stamps stay unix seconds throughout; only occurrence generation is
    // local, which keeps the DST handling in one place (see TryGetNextOccurrence).
    internal class CronSchedule {

        // Vixie cron's day-of-month / day-of-week rule: when BOTH are restricted, a day matching
        // EITHER fires, rather than requiring both. So "0 0 1 * MON" is the 1st of the month and
        // every Monday, not "Mondays that fall on the 1st". Surprising, but it is what every other
        // cron does, and quietly disagreeing with them would be worse.
        private bool[] minutes;      // 0-59
        private bool[] hours;        // 0-23
        private bool[] daysOfMonth;  // 1-31 (index 0 unused)
        private bool[] months;       // 1-12 (index 0 unused)
        private bool[] daysOfWeek;   // 0-6, Sunday = 0
        private bool domRestricted;
        private bool dowRestricted;

        internal string Expression { get; private set; }

        // Smallest gap two consecutive fires can have. The sweep uses it as the floor below which a
        // zone is not even examined, and overlapping reset groups use it to decide which is the more
        // frequent schedule. Computed once at parse; see ComputeMinGap.
        internal long MinGapSeconds { get; private set; }

        private CronSchedule() { }

        // ---------------------------------------------------------------------------------------
        // Parsing
        // ---------------------------------------------------------------------------------------

        private static readonly string[] MonthNames = {
            "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC",
        };
        private static readonly string[] DayNames = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

        // Never throws. A bad expression is a config-load warning and the target falls back to its
        // ResetHours, so a typo makes a reset slower rather than opening one up.
        internal static bool TryParse(string expression, out CronSchedule schedule, out string error) {
            schedule = null;
            error = null;

            if (string.IsNullOrWhiteSpace(expression)) {
                error = "the expression is empty";
                return false;
            }

            string text = expression.Trim();
            string expanded = ExpandMacro(text);
            if (expanded == null) {
                error = $"'{text}' is not a recognised macro. Supported: @hourly, @daily, @midnight, @weekly, @monthly, @yearly, @annually";
                return false;
            }

            string[] fields = expanded.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 5) {
                error = $"expected 5 fields (minute hour day-of-month month day-of-week) but found {fields.Length}";
                return false;
            }

            CronSchedule parsed = new CronSchedule() { Expression = text };
            if (TryParseField(fields[0], 0, 59, null, out parsed.minutes, out bool _, out string fieldError) == false) {
                error = $"minute field '{fields[0]}': {fieldError}";
                return false;
            }
            if (TryParseField(fields[1], 0, 23, null, out parsed.hours, out bool _, out fieldError) == false) {
                error = $"hour field '{fields[1]}': {fieldError}";
                return false;
            }
            if (TryParseField(fields[2], 1, 31, null, out parsed.daysOfMonth, out parsed.domRestricted, out fieldError) == false) {
                error = $"day-of-month field '{fields[2]}': {fieldError}";
                return false;
            }
            if (TryParseField(fields[3], 1, 12, MonthNames, out parsed.months, out bool _, out fieldError) == false) {
                error = $"month field '{fields[3]}': {fieldError}";
                return false;
            }
            // 0-7 on the way in so 7 is accepted for Sunday, then folded down to 0-6.
            if (TryParseField(fields[4], 0, 7, DayNames, out bool[] dowRaw, out parsed.dowRestricted, out fieldError) == false) {
                error = $"day-of-week field '{fields[4]}': {fieldError}";
                return false;
            }
            parsed.daysOfWeek = new bool[7];
            for (int i = 0; i <= 7; i++) {
                if (dowRaw[i]) { parsed.daysOfWeek[i % 7] = true; }
            }

            parsed.MinGapSeconds = parsed.ComputeMinGap();
            if (parsed.MinGapSeconds <= 0L) {
                error = "the expression never fires (check the day-of-month and month combination)";
                return false;
            }

            schedule = parsed;
            return true;
        }

        // Returns null for an unrecognised @macro, and the input unchanged when it is not a macro.
        private static string ExpandMacro(string text) {
            if (text.Length == 0 || text[0] != '@') { return text; }
            switch (text.ToUpperInvariant()) {
                case "@HOURLY": return "0 * * * *";
                case "@DAILY":
                case "@MIDNIGHT": return "0 0 * * *";
                case "@WEEKLY": return "0 0 * * 0";
                case "@MONTHLY": return "0 0 1 * *";
                case "@YEARLY":
                case "@ANNUALLY": return "0 0 1 1 *";
                default: return null;
            }
        }

        // restricted reports whether the field was anything other than a bare '*'. Only the
        // day-of-month and day-of-week fields care, for the OR rule described above.
        private static bool TryParseField(string field, int min, int max, string[] names,
                                          out bool[] allowed, out bool restricted, out string error) {
            allowed = new bool[max + 1];
            restricted = field != "*";
            error = null;

            foreach (string part in field.Split(',')) {
                string item = part.Trim();
                if (item.Length == 0) { error = "empty list item"; return false; }

                int step = 1;
                int slash = item.IndexOf('/');
                if (slash >= 0) {
                    string stepText = item.Substring(slash + 1);
                    if (int.TryParse(stepText, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) == false || step < 1) {
                        error = $"'{stepText}' is not a step of 1 or more";
                        return false;
                    }
                    item = item.Substring(0, slash);
                    if (item.Length == 0) { error = "a step needs a range or * in front of it"; return false; }
                }

                int from;
                int to;
                if (item == "*") {
                    from = min;
                    to = max;
                } else {
                    int dash = item.IndexOf('-', 1); // from index 1: a leading '-' is a bad value, not a range
                    if (dash > 0) {
                        if (TryParseValue(item.Substring(0, dash), min, max, names, out from, out error) == false) { return false; }
                        if (TryParseValue(item.Substring(dash + 1), min, max, names, out to, out error) == false) { return false; }
                        if (to < from) { error = $"range '{item}' runs backwards"; return false; }
                    } else {
                        if (TryParseValue(item, min, max, names, out from, out error) == false) { return false; }
                        // A step with a single value means "from here to the end of the range", which
                        // is what makes */15 and 5/15 behave the same way in every cron.
                        to = slash >= 0 ? max : from;
                    }
                }

                for (int value = from; value <= to; value += step) { allowed[value] = true; }
            }

            for (int i = min; i <= max; i++) {
                if (allowed[i]) { return true; }
            }
            error = "matches no values";
            return false;
        }

        private static bool TryParseValue(string text, int min, int max, string[] names, out int value, out string error) {
            value = 0;
            error = null;
            string item = text.Trim();
            if (item.Length == 0) { error = "empty value"; return false; }

            if (int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) == false) {
                if (names != null) {
                    int index = Array.FindIndex(names, n => string.Equals(n, item, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) {
                        // Month names are 1-based, day names 0-based, which is exactly the difference
                        // between each list's min and its index.
                        value = index + min;
                        return true;
                    }
                }
                error = $"'{item}' is not a number{(names != null ? " or a recognised name" : "")}";
                return false;
            }

            if (value < min || value > max) {
                error = $"{value} is outside {min}-{max}";
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------------------------------
        // Occurrences
        // ---------------------------------------------------------------------------------------

        // How far forward TryGetNextOccurrence will look before giving up. Five years covers every
        // legal expression -- the sparsest possible is "Feb 29th", which recurs at most every 8
        // years, and that case is caught by ComputeMinGap at parse time instead.
        private const int SearchHorizonDays = 366 * 5;

        // The next fire strictly after afterLocal, at minute resolution. False if the expression has
        // no occurrence inside the search horizon.
        //
        // DST: this walks local wall-clock minutes, so a fire inside a skipped spring-forward hour
        // simply never matches and the schedule lands on its next valid minute -- for a daily 02:30
        // that means it is missed on the one day 02:30 does not exist. A repeated fall-back hour
        // matches twice in wall-clock terms, but HasElapsedSince works in unix seconds against a
        // stamp that has already moved past the first fire, so the target still resets once.
        internal bool TryGetNextOccurrence(DateTime afterLocal, out DateTime next) {
            // Truncate to the minute and step once, so "strictly after" holds even when afterLocal
            // lands exactly on a matching minute.
            DateTime candidate = new DateTime(afterLocal.Year, afterLocal.Month, afterLocal.Day,
                afterLocal.Hour, afterLocal.Minute, 0, afterLocal.Kind).AddMinutes(1);
            DateTime limit = candidate.AddDays(SearchHorizonDays);

            while (candidate < limit) {
                // Skip whole days and hours rather than 60 minutes at a time: a yearly expression
                // would otherwise be half a million iterations.
                if (months[candidate.Month] == false || DayMatches(candidate) == false) {
                    candidate = candidate.Date.AddDays(1);
                    continue;
                }
                if (hours[candidate.Hour] == false) {
                    candidate = candidate.Date.AddHours(candidate.Hour + 1);
                    continue;
                }
                if (minutes[candidate.Minute] == false) {
                    candidate = candidate.AddMinutes(1);
                    continue;
                }
                next = candidate;
                return true;
            }

            next = default(DateTime);
            return false;
        }

        // The Vixie OR rule lives here. With only one of the two fields restricted the other is '*'
        // and matches everything, so a plain AND would be wrong in exactly the cases people write
        // most often.
        private bool DayMatches(DateTime local) {
            bool domHit = daysOfMonth[local.Day];
            bool dowHit = daysOfWeek[(int)local.DayOfWeek];
            if (domRestricted && dowRestricted) { return domHit || dowHit; }
            return domHit && dowHit;
        }

        // The whole due-check: has a fire landed in (stampUnix, nowUnix]? Phrased against the
        // existing stamps so cron needs no new per-target state and no state-file change.
        internal bool HasElapsedSince(long stampUnix, long nowUnix) {
            if (nowUnix <= stampUnix) { return false; }
            DateTime afterLocal = DateTimeOffset.FromUnixTimeSeconds(stampUnix).LocalDateTime;
            if (TryGetNextOccurrence(afterLocal, out DateTime next) == false) { return false; }
            return ToUnixSeconds(next) <= nowUnix;
        }

        // The next fire after now, for status output. Null when there is none in the horizon.
        internal DateTime? NextAfter(long nowUnix) {
            DateTime local = DateTimeOffset.FromUnixTimeSeconds(nowUnix).LocalDateTime;
            if (TryGetNextOccurrence(local, out DateTime next) == false) { return null; }
            return next;
        }

        // DateTimeKind.Unspecified from the walk above is treated as local, which is what it is.
        //
        // The matching walk works in wall-clock minutes, so on a spring-forward day it can land on a
        // local time that does not exist (a daily "30 2 * * *" on the day 02:00 jumps to 03:00).
        // Resolving that here rather than in the walk keeps the matching logic pure: the fire is
        // moved to the first real minute after the gap, which is also what Vixie cron does with a
        // job it could not run during the skip. It stays ONE fire either way.
        private static long ToUnixSeconds(DateTime local) {
            DateTime asLocal = DateTime.SpecifyKind(local, DateTimeKind.Local);
            if (TimeZoneInfo.Local.IsInvalidTime(asLocal)) {
                // Gaps are an hour at most in every real timezone; the bound is a guard, not a limit.
                for (int i = 0; i < 24 * 60; i++) {
                    asLocal = asLocal.AddMinutes(1);
                    if (TimeZoneInfo.Local.IsInvalidTime(asLocal) == false) { break; }
                }
            }
            return new DateTimeOffset(asLocal).ToUnixTimeSeconds();
        }

        // Smallest gap between consecutive fires, by sampling forward from a fixed reference. The
        // reference is a Sunday 1 January so every day-of-week and day-of-month phase is reachable,
        // and it is deliberately NOT "now" -- the value feeds the sweep floor and group ordering,
        // which must not change depending on when the server happened to load its config.
        private const int MinGapSamples = 500;

        private long ComputeMinGap() {
            DateTime cursor = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Unspecified); // a Sunday
            if (TryGetNextOccurrence(cursor, out DateTime previous) == false) { return 0L; }

            long smallest = long.MaxValue;
            DateTime last = previous;
            for (int i = 0; i < MinGapSamples; i++) {
                if (TryGetNextOccurrence(last, out DateTime next) == false) { break; }
                long gap = (long)(next - last).TotalSeconds;
                if (gap > 0L && gap < smallest) { smallest = gap; }
                // A year of samples is plenty to see the tightest gap of any expression that fires
                // often enough for the floor to matter; anything sparser is bounded by the fallback.
                if ((next - previous).TotalDays > 366d) { break; }
                last = next;
            }

            if (smallest == long.MaxValue) {
                // A single fire inside the sampled window (say, one specific date). Treat it as
                // yearly: the floor only needs a safe lower bound, not an exact period.
                return 365L * 86400L;
            }
            return smallest;
        }

        // "0 3 * * * (next Wed 03:00)". Used in the chunk log and SLS-loc-reset-status, where an
        // hours figure would be meaningless.
        internal string Describe(long nowUnix) {
            DateTime? next = NextAfter(nowUnix);
            if (next.HasValue == false) { return $"cron {Expression}"; }
            return $"cron {Expression} (next {next.Value:ddd HH:mm})";
        }

        public override string ToString() { return Expression; }
    }
}
