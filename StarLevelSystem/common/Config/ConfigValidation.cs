using System;
using System.Collections.Generic;

#pragma warning disable IDE0130
namespace StarLevelSystem.common {
#pragma warning restore IDE0130

    // What a config file's Validate hook hands back.
    //
    // Warnings and errors are two lists rather than one list with a severity field, because the split is
    // what decides whether the file is used at all. "This prefab name does not resolve" is a warning --
    // the rest of the file is perfectly usable and the admin needs to know about the one bad line.
    // "This file has no entries" is an error -- nothing downstream can work, so the file's
    // ConfigFailurePolicy takes over. Only errors route to the policy; warnings are logged and dropped.
    internal class ValidationReport {
        internal readonly List<string> Warnings = new List<string>();
        internal readonly List<string> Errors = new List<string>();

        internal bool HasErrors {
            get { return Errors.Count > 0; }
        }

        internal ValidationReport Warn(string message) {
            Warnings.Add(message);
            return this;
        }

        internal ValidationReport Error(string message) {
            Errors.Add(message);
            return this;
        }

        // Merge another report in, so a validator can delegate per-section checks and still return one
        // report to the framework.
        internal ValidationReport Absorb(ValidationReport other) {
            if (other == null) { return this; }
            Warnings.AddRange(other.Warnings);
            Errors.AddRange(other.Errors);
            return this;
        }
    }

    internal static class ConfigValidation {
        // Precedence helper for the documented "the BepInEx entry wins unless it is set to the sentinel"
        // pattern -- see the BepInEx/yaml table in this folder's README. Validators and cold paths only;
        // a per-frame read should compare the two values inline rather than pay for a call.
        internal static float Prefer(float bepInExValue, float yamlValue, float sentinel = 0f) {
            return bepInExValue == sentinel ? yamlValue : bepInExValue;
        }

        // "Did you mean ...?" for an unrecognised key. Returns "" when nothing is close enough, so the
        // caller can always concatenate it onto a warning without a null check.
        //
        // The threshold scales with the key length: a 4-character key allows 1 edit, a 20-character key
        // allows 5. A fixed distance either misses obvious typos in long names or starts suggesting
        // unrelated short ones.
        internal static string SuggestKey(string unknownKey, IEnumerable<string> knownKeys) {
            if (string.IsNullOrEmpty(unknownKey) || knownKeys == null) { return ""; }

            int allowed = Math.Max(1, unknownKey.Length / 4);
            string best = null;
            int bestDistance = int.MaxValue;

            foreach (string candidate in knownKeys) {
                if (string.IsNullOrEmpty(candidate)) { continue; }
                int distance = Distance(unknownKey, candidate);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (best == null || bestDistance > allowed) { return ""; }
            return $" Did you mean '{best}'?";
        }

        // Case-insensitive Levenshtein distance. Two rows rather than a full matrix -- these run over
        // every key of a failed parse, and the keys are short.
        private static int Distance(string a, string b) {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            if (a == b) { return 0; }
            if (a.Length == 0) { return b.Length; }
            if (b.Length == 0) { return a.Length; }

            int[] previous = new int[b.Length + 1];
            int[] current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) { previous[j] = j; }

            for (int i = 1; i <= a.Length; i++) {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++) {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                int[] swap = previous;
                previous = current;
                current = swap;
            }

            return previous[b.Length];
        }
    }
}
