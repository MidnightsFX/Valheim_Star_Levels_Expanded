using StarLevelSystem.Data;
using StarLevelSystem.modules.Modifiers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common {
    // Reads and writes a creature's modifier storage.
    //
    // The modifier set lives in SLS_MODSV2 as a compact string: the marker \u0001, then one variable-length number
    // per modifier, (id << 2) | type, where the id comes from ModifierNamesLookupTable. Each character carries 6 bits
    // and a "more follows" bit, so every character stays below 0x80 and costs one byte on the wire: a modifier with an
    // id under 16 takes one byte, the rest of the built-ins two. A creature that rolled nothing is the marker alone.
    // A vanilla ZDO is resent whole on every data revision, so this is paid on every health change, not once; the
    // text it replaces ({"Fast": "Major"} and so on) took 20-60 bytes, and 18 for a creature with no modifiers.
    //
    // It stays a string under the old key, rather than moving to a byte array under a new one, because Valheim never
    // drops a removed ZDO key on a peer that already has it (ZDO.Deserialize only adds), so the old text would have
    // stayed in the server's copy, been saved with the world and resent to every client. Overwriting the same key
    // replaces it everywhere. Text written before this (the JSON shape yamlSerializerJsonCompat writes) is still read.
    //
    // Per-creature modifier config overrides live in SLS_MODCFG as a byte array, only on creatures that have one.
    internal static class StoredModifierFormat {

        private const char CompactMarker = '\u0001';
        private const byte OverrideFormat = 1;
        // An id this peer has no name for (a modifier registered through the API by a mod it does not have) is kept
        // as "#<id>", so an owner without that mod writes it back unchanged instead of dropping it.
        private const string UnknownIdPrefix = "#";

        internal static bool IsCompact(string stored) {
            return stored != null && stored.Length > 0 && stored[0] == CompactMarker;
        }

        // `dropped` collects the names that have no id and so are not written.
        internal static string EncodeSet(Dictionary<string, ModifierType> modifiers, List<string> dropped = null) {
            StringBuilder sb = new StringBuilder(1 + (modifiers != null ? modifiers.Count * 2 : 0));
            sb.Append(CompactMarker);
            if (modifiers == null) { return sb.ToString(); }
            foreach (KeyValuePair<string, ModifierType> modifier in modifiers) {
                if (string.IsNullOrEmpty(modifier.Key) || modifier.Key == CreatureModifiers.NoMods) { continue; }
                int id = ModifierId(modifier.Key);
                // 29 bits leave room for the type in a 32 bit number.
                if (id <= 0 || id >= (1 << 29) || (int)modifier.Value < 0 || (int)modifier.Value > 3) {
                    dropped?.Add(modifier.Key);
                    continue;
                }
                WriteVarint(sb, ((uint)id << 2) | (uint)modifier.Value);
            }
            return sb.ToString();
        }

        // A creature that rolled nothing comes back as the "None" entry, as it was stored before: an empty set is what
        // the owner's setup reads as never rolled.
        internal static bool TryDecodeSet(string stored, out Dictionary<string, ModifierType> modifiers) {
            modifiers = null;
            if (IsCompact(stored) == false) { return false; }
            Dictionary<string, ModifierType> parsed = new Dictionary<string, ModifierType>();
            int i = 1;
            while (i < stored.Length) {
                if (TryReadVarint(stored, ref i, out uint packed) == false) { return false; }
                ModifierType type = (ModifierType)(packed & 3);
                if (Enum.IsDefined(typeof(ModifierType), type) == false) { return false; }
                string name = ModifierName((int)(packed >> 2));
                if (parsed.ContainsKey(name)) { return false; }
                parsed.Add(name, type);
            }
            if (parsed.Count == 0) { parsed.Add(CreatureModifiers.NoMods, ModifierType.Minor); }
            modifiers = parsed;
            return true;
        }

        // [format] then per modifier: [id][flags: 1 BasePower, 2 PerlevelPower][floats][config count][key, value]...
        // Keys are written out: modifiers read them with a fallback, so one missing from Modifiers.yaml must still be
        // overridable and no table of known keys could name it. An empty dictionary still writes the format byte:
        // removing the key would not reach other peers (see above), so clearing the last override writes this instead.
        internal static byte[] EncodeOverrides(Dictionary<string, ModifierConfigOverride> overrides, List<string> dropped = null) {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8)) {
                writer.Write(OverrideFormat);
                if (overrides != null) {
                    foreach (KeyValuePair<string, ModifierConfigOverride> entry in overrides) {
                        if (entry.Value == null || entry.Value.IsEmpty()) { continue; }
                        int id = ModifierId(entry.Key);
                        if (id <= 0) {
                            dropped?.Add(entry.Key);
                            continue;
                        }
                        WriteVarint(writer, (uint)id);
                        byte flags = 0;
                        if (entry.Value.BasePower.HasValue) { flags |= 1; }
                        if (entry.Value.PerlevelPower.HasValue) { flags |= 2; }
                        writer.Write(flags);
                        if (entry.Value.BasePower.HasValue) { writer.Write(entry.Value.BasePower.Value); }
                        if (entry.Value.PerlevelPower.HasValue) { writer.Write(entry.Value.PerlevelPower.Value); }
                        int count = entry.Value.Config != null ? entry.Value.Config.Count : 0;
                        WriteVarint(writer, (uint)count);
                        if (count > 0) {
                            foreach (KeyValuePair<string, float> value in entry.Value.Config) {
                                writer.Write(value.Key ?? string.Empty);
                                writer.Write(value.Value);
                            }
                        }
                    }
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static bool TryDecodeOverrides(byte[] stored, out Dictionary<string, ModifierConfigOverride> overrides) {
            overrides = null;
            if (stored == null || stored.Length == 0 || stored[0] != OverrideFormat) { return false; }
            Dictionary<string, ModifierConfigOverride> parsed = new Dictionary<string, ModifierConfigOverride>();
            try {
                using (MemoryStream stream = new MemoryStream(stored, 1, stored.Length - 1))
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8)) {
                    while (stream.Position < stream.Length) {
                        string name = ModifierName((int)ReadVarint(reader));
                        byte flags = reader.ReadByte();
                        ModifierConfigOverride entry = new ModifierConfigOverride();
                        if ((flags & 1) != 0) { entry.BasePower = reader.ReadSingle(); }
                        if ((flags & 2) != 0) { entry.PerlevelPower = reader.ReadSingle(); }
                        uint count = ReadVarint(reader);
                        if (count > 0) {
                            entry.Config = new Dictionary<string, float>();
                            for (uint i = 0; i < count; i++) {
                                string key = reader.ReadString();
                                entry.Config[key] = reader.ReadSingle();
                            }
                        }
                        parsed[name] = entry;
                    }
                }
            } catch (Exception) {
                return false;
            }
            overrides = parsed;
            return true;
        }

        // Identifies what a creature's modifier storage holds, for ModifierPeerRefresh to tell when another peer's copy
        // of the ZDO has caught up with the owner's write. FNV-1a over both values.
        internal static int ContentHash(string set, byte[] overrides) {
            unchecked {
                uint hash = 2166136261;
                if (set != null) {
                    for (int i = 0; i < set.Length; i++) { hash = (hash ^ set[i]) * 16777619; }
                }
                hash = (hash ^ 0xFFFF) * 16777619;
                if (overrides != null) {
                    for (int i = 0; i < overrides.Length; i++) { hash = (hash ^ overrides[i]) * 16777619; }
                }
                return (int)hash;
            }
        }

        internal static bool SameBytes(byte[] a, byte[] b) {
            if (ReferenceEquals(a, b)) { return true; }
            if (a == null || b == null || a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++) {
                if (a[i] != b[i]) { return false; }
            }
            return true;
        }

        private static int ModifierId(string name) {
            if (name.StartsWith(UnknownIdPrefix, StringComparison.Ordinal) && int.TryParse(name.Substring(UnknownIdPrefix.Length), out int unknownId)) {
                return unknownId;
            }
            return CreatureModifiersData.ModifierNamesLookupTable.GetValue(name);
        }

        private static string ModifierName(int id) {
            return CreatureModifiersData.ModifierNamesLookupTable.GetValue(id) ?? UnknownIdPrefix + id;
        }

        private static void WriteVarint(StringBuilder sb, uint value) {
            while (value >= 0x40) {
                sb.Append((char)(0x40 | (value & 0x3F)));
                value >>= 6;
            }
            sb.Append((char)value);
        }

        private static bool TryReadVarint(string s, ref int i, out uint value) {
            value = 0;
            for (int shift = 0; shift < 32; shift += 6) {
                if (i >= s.Length) { return false; }
                char c = s[i++];
                if (c >= 0x80) { return false; }
                value |= (uint)(c & 0x3F) << shift;
                if ((c & 0x40) == 0) { return true; }
            }
            return false;
        }

        private static void WriteVarint(BinaryWriter writer, uint value) {
            while (value >= 0x80) {
                writer.Write((byte)(0x80 | (value & 0x7F)));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        private static uint ReadVarint(BinaryReader reader) {
            uint value = 0;
            for (int shift = 0; shift < 35; shift += 7) {
                byte b = reader.ReadByte();
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) { return value; }
            }
            throw new InvalidDataException("Variable-length number too long.");
        }

        // Text written by versions before the compact format: {"Fast": "Major", "ResistFire": "Minor"} followed by a
        // line break, character for character what yamlSerializerJsonCompat writes. Parsed here because YamlDotNet took
        // about 20 us and 11-19 KB of garbage per call. Anything else - a name that is not plain ASCII, a value written
        // by hand - returns false, and the caller hands it to YamlDotNet.
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
    }
}
