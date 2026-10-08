using System;
using System.Collections.Generic;
using System.Text;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common {
    // Reads and writes SLS_MODSV2, a creature's modifier set as yamlSerializerJsonCompat writes it:
    // {"Fast": "Major", "ResistFire": "Minor"} followed by a line break. Every creature that loads parses one and every
    // roll writes one, and YamlDotNet took about 20 us and 11-19 KB of garbage per call even on desktop .NET, more under
    // the game's Mono. That shape is read and written here, character for character what YamlDotNet writes. Anything
    // else - a name that is not plain ASCII, a value written by hand or by an older version - returns false, and the
    // caller hands it to YamlDotNet as before.
    internal static class StoredModifierFormat {

        internal static bool TryParse(string stored, out Dictionary<string, ModifierType> modifiers) {
            modifiers = null;
            if (stored == null) { return false; }
            int i = SkipSpace(stored, 0);
            if (i >= stored.Length || stored[i] != '{') { return false; }
            i = SkipSpace(stored, i + 1);
            Dictionary<string, ModifierType> parsed = new Dictionary<string, ModifierType>();
            if (i < stored.Length && stored[i] == '}') {
                i++;
            } else {
                while (true) {
                    if (ReadName(stored, ref i, out string name) == false) { return false; }
                    i = SkipSpace(stored, i);
                    if (i >= stored.Length || stored[i] != ':') { return false; }
                    i = SkipSpace(stored, i + 1);
                    if (ReadName(stored, ref i, out string typeName) == false || TryParseType(typeName, out ModifierType type) == false) { return false; }
                    // YamlDotNet keeps the last of a repeated name; that case is left to it.
                    if (parsed.ContainsKey(name)) { return false; }
                    parsed.Add(name, type);
                    i = SkipSpace(stored, i);
                    if (i >= stored.Length) { return false; }
                    if (stored[i] == '}') { i++; break; }
                    if (stored[i] != ',') { return false; }
                    i = SkipSpace(stored, i + 1);
                }
            }
            if (SkipSpace(stored, i) != stored.Length) { return false; }
            modifiers = parsed;
            return true;
        }

        internal static bool TrySerialize(Dictionary<string, ModifierType> modifiers, out string stored) {
            stored = null;
            if (modifiers == null) { return false; }
            StringBuilder sb = new StringBuilder(4 + modifiers.Count * 24);
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, ModifierType> modifier in modifiers) {
                string typeName = TypeName(modifier.Value);
                if (typeName == null || IsPlainName(modifier.Key) == false) { return false; }
                if (first == false) { sb.Append(", "); }
                first = false;
                sb.Append('"').Append(modifier.Key).Append("\": \"").Append(typeName).Append('"');
            }
            sb.Append('}').Append(System.Environment.NewLine);
            stored = sb.ToString();
            return true;
        }

        private static int SkipSpace(string s, int i) {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) { i++; }
            return i;
        }

        // A double-quoted name with no escapes in it.
        private static bool ReadName(string s, ref int i, out string name) {
            name = null;
            if (i >= s.Length || s[i] != '"') { return false; }
            int start = i + 1;
            int end = start;
            while (end < s.Length && s[end] != '"') {
                if (IsPlainChar(s[end]) == false) { return false; }
                end++;
            }
            if (end >= s.Length || end == start) { return false; }
            name = s.Substring(start, end - start);
            i = end + 1;
            return true;
        }

        private static bool IsPlainName(string name) {
            if (string.IsNullOrEmpty(name)) { return false; }
            for (int i = 0; i < name.Length; i++) {
                if (IsPlainChar(name[i]) == false) { return false; }
            }
            return true;
        }

        // Characters YamlDotNet writes inside a quoted name as they are.
        private static bool IsPlainChar(char c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.' || c == ' ';
        }

        // Exact names only. YamlDotNet also takes other casings and numbers, so those fall back to it.
        private static bool TryParseType(string name, out ModifierType type) {
            switch (name) {
                case "Major": type = ModifierType.Major; return true;
                case "Minor": type = ModifierType.Minor; return true;
                case "Boss": type = ModifierType.Boss; return true;
            }
            type = default;
            return false;
        }

        private static string TypeName(ModifierType type) {
            switch (type) {
                case ModifierType.Major: return "Major";
                case ModifierType.Minor: return "Minor";
                case ModifierType.Boss: return "Boss";
            }
            return null;
        }
    }
}
