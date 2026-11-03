using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.LocationReset;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules {

    // What biomes exist, and how a biome named in a config file finds the settings written for it.
    //
    // A custom biome's real value is not usable as its identity: Expand World Data hands values out by the order of its
    // file, and its names only arrive in its Start (after every Awake, so after SLS's first parse), or from the server on
    // a client's join. So config files never resolve a custom name at all. A name that is not vanilla's becomes a token,
    // a Heightmap.Biome value derived from the name that the game can never return, and lookups resolve it here against
    // whatever the table holds at that moment. Parsing never depends on load order or join timing, and an edit to Expand
    // World's file takes effect without reloading any SLS config.
    //
    // Nothing here runs for a vanilla biome beyond a bit test, and with no biome mod the custom tables stay empty.
    internal static class BiomeRegistry {
        // Heightmap.Biome.All is every vanilla biome's bit. Bit 0x80 is not one of them; Expand World Data uses it last.
        private const uint VanillaBits = (uint)Heightmap.Biome.All;

        // Top two bits set: multiple bits, so no game call returns it, and no biome mod allocates it.
        private const uint TokenMarker = 0xC0000000;
        private const uint TokenHashBits = 0x00FFFFFF;

        // From the enum's fields: Expand World Data patches Enum.GetNames and the like to return its own table, which has
        // no All or Land.
        private static readonly Dictionary<string, Heightmap.Biome> VanillaNames = typeof(Heightmap.Biome)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => field.Name, field => (Heightmap.Biome)field.GetValue(null), StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, Heightmap.Biome> customByName = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<Heightmap.Biome, string> customNames = new Dictionary<Heightmap.Biome, string>();
        private static Dictionary<Heightmap.Biome, Heightmap.Biome> terrainOf = new Dictionary<Heightmap.Biome, Heightmap.Biome>();
        private static Heightmap.Biome lavaBiomes = Heightmap.Biome.AshLands;
        private static string tableFingerprint = "";

        // Biomes and settings sources other mods registered through the API, for biome mods that do not patch Enum.
        private static readonly Dictionary<string, Heightmap.Biome> registeredBiomes = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Heightmap.Biome> registeredFallbacks = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<Heightmap.Biome, string> tokenNames = new Dictionary<Heightmap.Biome, string>();
        private static readonly Dictionary<string, Heightmap.Biome> tokensByName = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);

        // The keys a custom biome's settings can sit under, most specific first. Rebuilt from the table and the fallbacks.
        private static readonly Dictionary<Heightmap.Biome, Heightmap.Biome[]> lookupKeys = new Dictionary<Heightmap.Biome, Heightmap.Biome[]>();

        private static readonly HashSet<string> warnedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool warnImmediately;

        internal static IEnumerable<Heightmap.Biome> CustomBiomes => customNames.Keys;
        internal static IEnumerable<string> CustomBiomeNames => customNames.Values;

        internal static void Init() {
            // A client's Expand World table comes with the join, and this is the point its synced data is in.
            SynchronizationManager.OnConfigurationSynchronized += (sender, args) => Refresh();
        }

        // Called from the Expand World patches and at world start. Reads the table fresh, and only acts when it changed.
        internal static void Refresh() {
            Dictionary<Heightmap.Biome, string> names = new Dictionary<Heightmap.Biome, string>();
            Dictionary<string, Heightmap.Biome> byName = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
            try {
                // Zipped rather than Enum.GetName, which Expand World Data answers with "None" for anything unnamed.
                Array values = Enum.GetValues(typeof(Heightmap.Biome));
                string[] enumNames = Enum.GetNames(typeof(Heightmap.Biome));
                for (int i = 0; i < values.Length && i < enumNames.Length; i++) {
                    AddCustom(names, byName, enumNames[i], (Heightmap.Biome)values.GetValue(i));
                }
            } catch (Exception e) {
                Logger.LogWarning($"Could not read the biome table: {e.Message}");
            }
            foreach (KeyValuePair<string, Heightmap.Biome> registered in registeredBiomes) {
                AddCustom(names, byName, registered.Key, registered.Value);
            }

            Dictionary<Heightmap.Biome, Heightmap.Biome> terrain = new Dictionary<Heightmap.Biome, Heightmap.Biome>();
            foreach (Heightmap.Biome biome in names.Keys) {
                terrain[biome] = ExpandWorldDataBiomes.TerrainOf(biome);
            }
            Heightmap.Biome lava = ExpandWorldDataBiomes.LavaBiomes();

            string fingerprint = lava == Heightmap.Biome.AshLands && names.Count == 0
                ? ""
                : (int)lava + "|" + string.Join(",", names.Select(pair => $"{(int)pair.Key}={pair.Value}={(int)terrain[pair.Key]}"));
            if (fingerprint == tableFingerprint) { return; }

            tableFingerprint = fingerprint;
            customNames = names;
            customByName = byName;
            terrainOf = terrain;
            lavaBiomes = lava;
            lookupKeys.Clear();
            FlushBiomeCaches();
            Logger.LogInfo(names.Count == 0
                ? "No custom biomes in this world."
                : $"Custom biomes known to Star Level System: {string.Join(", ", names.Values)}.");
        }

        private static void AddCustom(Dictionary<Heightmap.Biome, string> names, Dictionary<string, Heightmap.Biome> byName, string name, Heightmap.Biome biome) {
            uint bits = (uint)biome;
            if (string.IsNullOrEmpty(name) || bits == 0 || (bits & VanillaBits) != 0 || (bits & (bits - 1)) != 0) { return; }
            names[biome] = name;
            byName[name] = biome;
        }

        // The caches keyed by real biome, or filled from a lookup through the tables above.
        private static void FlushBiomeCaches() {
            LevelSelection.ClearMergedBiomeSettings();
            ConditionalScaleSystem.ResetCache();
            CompositeLazyCache.FlushCache();
            CreatureModifiersData.ClearBiomeProbabilityCaches();
            ZoneRates.ResetCache();
        }

        // New settings can change a custom biome's fallback. Only a lookup for a custom biome fills this, so with none yet
        // there is nothing to do, which is every load of LevelSettings.yaml in a vanilla world.
        internal static void OnLevelSettingsApplied() {
            if (lookupKeys.Count == 0) { return; }
            lookupKeys.Clear();
            CreatureModifiersData.ClearBiomeProbabilityCaches();
            ZoneRates.ResetCache();
        }

        // Config is the server's, so only the machine that owns it is told a name means nothing. Warned here rather than as
        // it is parsed, because the first parse comes before any mod's biome names exist.
        internal static void OnWorldStart() {
            Refresh();
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            warnImmediately = true;
            foreach (string name in tokenNames.Values.ToList()) { WarnIfUnresolved(name); }
        }

        private static void WarnIfUnresolved(string name) {
            if (customByName.ContainsKey(name) || warnedNames.Add(name) == false) { return; }
            string biomes = string.Join(", ", VanillaNames.Where(pair => IsPlace(pair.Value)).Select(pair => pair.Key).Concat(customNames.Values));
            Logger.LogWarning($"'{name}' is named in a Star Level System config file but is not a biome in this world, so settings for it do nothing. Biomes here: {biomes}.");
        }

        // A single biome rather than None or one of the combined values.
        private static bool IsPlace(Heightmap.Biome biome) {
            uint bits = (uint)biome;
            return bits != 0 && (bits & (bits - 1)) == 0;
        }

        // ---- Names in config text --------------------------------------------------------------------------------------

        internal static bool TryParseVanilla(string name, out Heightmap.Biome biome) {
            return VanillaNames.TryGetValue(name, out biome);
        }

        // The stable value standing in for a name. The same on every machine and in every session, so it survives the
        // decimal-writing serializers in DataObjects.
        internal static Heightmap.Biome TokenFor(string name) {
            if (tokensByName.TryGetValue(name, out Heightmap.Biome token)) { return token; }
            uint hash = (uint)name.ToLowerInvariant().GetStableHashCode() & TokenHashBits;
            token = (Heightmap.Biome)(TokenMarker | hash);
            while (tokenNames.ContainsKey(token)) {
                hash = (hash + 1) & TokenHashBits;
                token = (Heightmap.Biome)(TokenMarker | hash);
            }
            tokenNames[token] = name;
            tokensByName[name] = token;
            if (warnImmediately) { WarnIfUnresolved(name); }
            return token;
        }

        // What a config file says for this value, the inverse of BiomeYamlConverter's read. Vanilla's text is untouched, so
        // files keep the exact form they have today.
        internal static string ConfigText(Heightmap.Biome biome) {
            if (tokenNames.TryGetValue(biome, out string token)) { return token; }
            return customNames.TryGetValue(biome, out string name) ? name : biome.ToString();
        }

        // ---- Questions about a biome -----------------------------------------------------------------------------------

        internal static bool IsVanilla(Heightmap.Biome biome) {
            return ((uint)biome & ~VanillaBits) == 0;
        }

        // A value that is not vanilla's and not a name standing in for one. Includes a bare number nothing has named.
        internal static bool IsCustom(Heightmap.Biome biome) {
            return IsVanilla(biome) == false && tokenNames.ContainsKey(biome) == false;
        }

        // A configured biome as the value the game uses. False for a name that is not a biome in this world.
        internal static bool Resolve(Heightmap.Biome configured, out Heightmap.Biome real) {
            if (tokenNames.TryGetValue(configured, out string name)) {
                return customByName.TryGetValue(name, out real);
            }
            real = configured;
            return true;
        }

        internal static bool TryGetByName(string name, out Heightmap.Biome biome) {
            if (string.IsNullOrWhiteSpace(name)) {
                biome = Heightmap.Biome.None;
                return false;
            }
            if (int.TryParse(name, out int number)) {
                biome = (Heightmap.Biome)number;
                return true;
            }
            return VanillaNames.TryGetValue(name, out biome) || customByName.TryGetValue(name, out biome);
        }

        internal static string IdentifierOf(Heightmap.Biome biome) {
            return ConfigText(biome);
        }

        // The name a player reads. Expand World Data registers biome_<value> for its display names; a value with nothing
        // registered falls back on its identifier, which is what happens to its last slot, 0x80000000, because the minus
        // sign ends a localization token.
        internal static string DisplayName(Heightmap.Biome biome) {
            if (tokenNames.TryGetValue(biome, out string token)) {
                if (customByName.TryGetValue(token, out Heightmap.Biome real) == false) { return token; }
                biome = real;
            }
            string identifier = ConfigText(biome);
            if (Localization.instance == null) { return identifier; }
            string localized = Localization.instance.Localize("$biome_" + biome.ToString().ToLowerInvariant());
            return string.IsNullOrEmpty(localized) || localized.StartsWith("[") ? identifier : localized;
        }

        internal static Heightmap.Biome TerrainOf(Heightmap.Biome biome) {
            return IsVanilla(biome) == false && terrainOf.TryGetValue(biome, out Heightmap.Biome terrain) ? terrain : biome;
        }

        internal static bool HasLava(Heightmap.Biome biome) {
            return (lavaBiomes & biome) != Heightmap.Biome.None;
        }

        // The key an editor writes a biome's settings under, so its row and the file's entry are the same entry.
        internal static Heightmap.Biome ConfigKey(Heightmap.Biome biome) {
            return customNames.TryGetValue(biome, out string name) ? TokenFor(name) : biome;
        }

        // The same, but keeping to an entry a table already has under the biome's number, so an edit lands where it is read.
        internal static Heightmap.Biome ConfigKeyFor<T>(Dictionary<Heightmap.Biome, T> table, Heightmap.Biome biome) {
            return table != null && table.ContainsKey(biome) ? biome : ConfigKey(biome);
        }

        // One line per biome for sls-biomes.
        internal static IEnumerable<string> DescribeAll() {
            foreach (KeyValuePair<string, Heightmap.Biome> vanilla in VanillaNames.Where(pair => IsPlace(pair.Value))) {
                yield return $"{vanilla.Key} ({(int)vanilla.Value}): {DisplayName(vanilla.Value)}";
            }
            foreach (KeyValuePair<Heightmap.Biome, string> custom in customNames) {
                string source = TryGetSettingsSource(custom.Key, custom.Value, out Heightmap.Biome from) ? ConfigText(from) : "its own and All only";
                yield return $"{custom.Value} ({(int)custom.Key}): {DisplayName(custom.Key)}, custom, settings from {source}, terrain {ConfigText(TerrainOf(custom.Key))}, lava {HasLava(custom.Key)}";
            }
            foreach (string name in tokenNames.Values.Where(name => customByName.ContainsKey(name) == false)) {
                yield return $"{name}: named in config but not a biome in this world";
            }
        }

        // ---- Lookups ---------------------------------------------------------------------------------------------------
        //
        // None of these consider Biome.All, which each caller handles its own way: LevelSelection merges a biome over it,
        // and a list naming it is not a wildcard.

        private static Heightmap.Biome[] KeysFor(Heightmap.Biome biome) {
            if (lookupKeys.TryGetValue(biome, out Heightmap.Biome[] keys)) { return keys; }

            List<Heightmap.Biome> found = new List<Heightmap.Biome>() { biome };
            bool named = customNames.TryGetValue(biome, out string name);
            if (named) { found.Add(TokenFor(name)); }
            if (TryGetSettingsSource(biome, named ? name : ((int)biome).ToString(), out Heightmap.Biome source)) {
                found.Add(source);
                if (Resolve(source, out Heightmap.Biome real) && real != source) {
                    found.Add(real);
                } else if (customNames.TryGetValue(source, out string sourceName)) {
                    found.Add(TokenFor(sourceName));
                }
            }
            keys = found.ToArray();
            lookupKeys[biome] = keys;
            return keys;
        }

        // Which biome's settings a custom biome borrows, if any. One level only, so a loop cannot form. The admin's word
        // comes first, and None from them means no fallback at all. All would only repeat what callers merge anyway.
        private static bool TryGetSettingsSource(Heightmap.Biome biome, string name, out Heightmap.Biome source) {
            source = Heightmap.Biome.None;
            CreatureLevelSettings settings = LevelSystemData.SLE_Level_Settings;
            bool chosen = false;
            if (settings?.BiomeFallbacks != null) {
                foreach (KeyValuePair<string, Heightmap.Biome> fallback in settings.BiomeFallbacks) {
                    if (string.Equals(fallback.Key, name, StringComparison.OrdinalIgnoreCase)) {
                        source = fallback.Value;
                        chosen = true;
                        break;
                    }
                }
            }
            if (chosen == false && registeredFallbacks.TryGetValue(name, out Heightmap.Biome registered)) {
                source = registered;
                chosen = true;
            }
            if (chosen == false && settings != null && settings.UseTerrainAsBiomeFallback && terrainOf.TryGetValue(biome, out Heightmap.Biome terrain) && terrain != biome) {
                source = terrain;
            }
            return source != Heightmap.Biome.None && source != Heightmap.Biome.All;
        }

        internal static bool TryGetKey<T>(Dictionary<Heightmap.Biome, T> table, Heightmap.Biome biome, out Heightmap.Biome key) {
            key = biome;
            if (table == null) { return false; }
            if (IsVanilla(biome)) { return table.ContainsKey(biome); }
            foreach (Heightmap.Biome candidate in KeysFor(biome)) {
                if (table.ContainsKey(candidate)) {
                    key = candidate;
                    return true;
                }
            }
            return false;
        }

        internal static bool TryGetEntry<T>(Dictionary<Heightmap.Biome, T> table, Heightmap.Biome biome, out T value) {
            value = default;
            if (table == null) { return false; }
            if (IsVanilla(biome)) { return table.TryGetValue(biome, out value); }
            foreach (Heightmap.Biome candidate in KeysFor(biome)) {
                if (table.TryGetValue(candidate, out value)) { return true; }
            }
            return false;
        }

        internal static bool ListHasBiome(List<Heightmap.Biome> list, Heightmap.Biome biome) {
            if (list == null) { return false; }
            if (IsVanilla(biome)) { return list.Contains(biome); }
            foreach (Heightmap.Biome candidate in KeysFor(biome)) {
                if (list.Contains(candidate)) { return true; }
            }
            return false;
        }

        // Whether a biome named in config covers the biome something is actually in.
        internal static bool Matches(Heightmap.Biome configured, Heightmap.Biome actual) {
            if (configured == actual) { return true; }
            if (IsVanilla(actual)) { return false; }
            return Array.IndexOf(KeysFor(actual), configured) >= 0;
        }

        // ---- API -------------------------------------------------------------------------------------------------------

        internal static bool RegisterBiome(string name, Heightmap.Biome biome, Heightmap.Biome settingsFrom) {
            uint bits = (uint)biome;
            if (string.IsNullOrWhiteSpace(name) || bits == 0 || (bits & VanillaBits) != 0 || (bits & (bits - 1)) != 0 || VanillaNames.ContainsKey(name)) {
                return false;
            }
            registeredBiomes[name] = biome;
            if (settingsFrom != Heightmap.Biome.None) { registeredFallbacks[name] = settingsFrom; }
            Refresh();
            lookupKeys.Clear();
            return true;
        }
    }
}
