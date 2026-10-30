using StarLevelSystem.common;
using StarLevelSystem.modules.Modifiers;
using System;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    // A creature's own values for its modifiers (ModifierConfigOverride), kept in its ZDO under SLS_MODCFG so every
    // peer, the next owner and the next load apply them. Independent of the modifier set: a value can be given before
    // the creature rolls ("if it rolls Fast, this fast"), and it stays through the modifier being removed and re-added.
    //
    // Every place a modifier reads its config for one creature goes through GetEffectiveConfig. A creature with no
    // values of its own - nearly all of them - costs one ZDO lookup there and gets the shared Modifiers.yaml config.
    internal static class CreatureModifierOverrides
    {
        // The two value names that are not Config keys.
        internal const string BasePowerKey = "BasePower";
        internal const string PerlevelPowerKey = "PerlevelPower";

        private static readonly int StoredKey = SLS_MODCFG.GetStableHashCode();

        private class Decoded {
            internal byte[] Source;
            // Null when the stored value did not decode; cached too, so it is not decoded again on every hit.
            internal Dictionary<string, ModifierConfigOverride> Overrides;
            // Configs with the values merged in, by modifier type then name, for the ConfigGeneration they were built at.
            internal int Generation;
            internal Dictionary<string, CreatureModConfig>[] Merged;
        }

        // Keyed by the full ZDOID, like every per-creature cache (see CompositeLazyCache.SessionCache).
        private static readonly Dictionary<ZDOID, Decoded> Cache = new Dictionary<ZDOID, Decoded>(ZDOIDComparer.Instance);

        internal static void Forget(ZDOID id) {
            Cache.Remove(id);
        }

        internal static void FlushCache() {
            Cache.Clear();
        }

        internal static byte[] GetStored(ZDO zdo) {
            return zdo != null ? zdo.GetByteArray(StoredKey) : null;
        }

        // The creature's own values by modifier name, or null when it has none. Shared with later reads: copy to change.
        internal static Dictionary<string, ModifierConfigOverride> GetOverrides(ZDO zdo) {
            return Decode(zdo)?.Overrides;
        }

        // The config `modifier` runs with on this creature: its Modifiers.yaml entry for `type`, with the creature's own
        // values in place of the configured ones. Without any it is the shared configured object itself.
        internal static CreatureModConfig GetEffectiveConfig(Character chara, string modifier, ModifierType type) {
            CreatureModConfig configured = CreatureModifiersData.GetConfig(modifier, type);
            if (chara == null || chara.m_nview == null) { return configured; }
            Decoded decoded = Decode(chara.m_nview.GetZDO());
            if (decoded == null || decoded.Overrides == null || modifier == null) { return configured; }
            if (decoded.Overrides.TryGetValue(modifier, out ModifierConfigOverride values) == false || values == null || values.IsEmpty()) { return configured; }

            if (decoded.Merged == null || decoded.Generation != CreatureModifiersData.ConfigGeneration) {
                decoded.Merged = new Dictionary<string, CreatureModConfig>[3];
                decoded.Generation = CreatureModifiersData.ConfigGeneration;
            }
            int typeIndex = (int)type >= 0 && (int)type < decoded.Merged.Length ? (int)type : 0;
            Dictionary<string, CreatureModConfig> byName = decoded.Merged[typeIndex] ??= new Dictionary<string, CreatureModConfig>();
            if (byName.TryGetValue(modifier, out CreatureModConfig merged)) { return merged; }
            merged = Merge(configured, values);
            byName[modifier] = merged;
            return merged;
        }

        // Every value `modifier` runs with on this creature, for the API: BasePower, PerlevelPower and each Config key.
        internal static Dictionary<string, float> GetEffectiveValues(Character chara, string modifier, ModifierType type) {
            CreatureModConfig cfg = GetEffectiveConfig(chara, modifier, type);
            Dictionary<string, float> values = new Dictionary<string, float>() {
                { BasePowerKey, cfg.BasePower },
                { PerlevelPowerKey, cfg.PerlevelPower },
            };
            if (cfg.Config != null) {
                foreach (KeyValuePair<string, float> entry in cfg.Config) { values[entry.Key] = entry.Value; }
            }
            return values;
        }

        // Merges `values` into what the creature already has for `modifier`: BasePower and PerlevelPower (any case) set
        // those, every other key a Config entry. Only the owner writes, like every modifier write; it returns whether the
        // stored value changed. A name with no modifier ID or a value that is not a finite number writes nothing.
        internal static bool Set(Character chara, string modifier, Dictionary<string, float> values) {
            if (values == null || values.Count == 0 || IsKnownModifier(modifier, chara) == false) { return false; }
            ModifierConfigOverride given = new ModifierConfigOverride();
            foreach (KeyValuePair<string, float> value in values) {
                if (string.IsNullOrEmpty(value.Key)) { continue; }
                if (float.IsNaN(value.Value) || float.IsInfinity(value.Value)) {
                    Logger.LogWarning($"{modifier} value {value.Key} for {chara.name} is not a number; nothing was set.");
                    return false;
                }
                if (string.Equals(value.Key, BasePowerKey, StringComparison.OrdinalIgnoreCase)) {
                    given.BasePower = value.Value;
                } else if (string.Equals(value.Key, PerlevelPowerKey, StringComparison.OrdinalIgnoreCase)) {
                    given.PerlevelPower = value.Value;
                } else {
                    given.Config ??= new Dictionary<string, float>();
                    given.Config[value.Key] = value.Value;
                }
            }
            return Set(chara, new Dictionary<string, ModifierConfigOverride>() { { modifier, given } });
        }

        // Merges each modifier's values into what the creature already has; the ModifierOverrides of a Nemesis or raid
        // spawn come in here. Owner only.
        internal static bool Set(Character chara, Dictionary<string, ModifierConfigOverride> overrides) {
            if (overrides == null || overrides.Count == 0 || CanWrite(chara) == false) { return false; }
            ZDO zdo = chara.m_nview.GetZDO();
            Dictionary<string, ModifierConfigOverride> merged = CopyOf(GetOverrides(zdo));
            foreach (KeyValuePair<string, ModifierConfigOverride> entry in overrides) {
                if (entry.Value == null || entry.Value.IsEmpty() || IsKnownModifier(entry.Key, chara) == false) { continue; }
                if (merged.TryGetValue(entry.Key, out ModifierConfigOverride current) == false || current == null) {
                    merged[entry.Key] = entry.Value.Copy();
                    continue;
                }
                if (entry.Value.BasePower.HasValue) { current.BasePower = entry.Value.BasePower; }
                if (entry.Value.PerlevelPower.HasValue) { current.PerlevelPower = entry.Value.PerlevelPower; }
                if (entry.Value.Config != null) {
                    current.Config ??= new Dictionary<string, float>();
                    foreach (KeyValuePair<string, float> value in entry.Value.Config) { current.Config[value.Key] = value.Value; }
                }
            }
            return Write(chara, merged);
        }

        // Drops the creature's own value `key` for `modifier` (BasePower, PerlevelPower or a Config key), or all of them
        // when `key` is null, so that modifier goes back to its configured values. Owner only.
        internal static bool Clear(Character chara, string modifier, string key = null) {
            if (string.IsNullOrEmpty(modifier) || CanWrite(chara) == false) { return false; }
            ZDO zdo = chara.m_nview.GetZDO();
            Dictionary<string, ModifierConfigOverride> current = GetOverrides(zdo);
            if (current == null || current.ContainsKey(modifier) == false) { return false; }
            Dictionary<string, ModifierConfigOverride> remaining = CopyOf(current);
            if (key == null) {
                remaining.Remove(modifier);
            } else {
                ModifierConfigOverride values = remaining[modifier];
                if (string.Equals(key, BasePowerKey, StringComparison.OrdinalIgnoreCase)) {
                    values.BasePower = null;
                } else if (string.Equals(key, PerlevelPowerKey, StringComparison.OrdinalIgnoreCase)) {
                    values.PerlevelPower = null;
                } else if (values.Config != null) {
                    values.Config.Remove(key);
                }
                if (values.IsEmpty()) { remaining.Remove(modifier); }
            }
            return Write(chara, remaining);
        }

        // A creature that takes over another's modifier set (a tamed child growing up, an offspring inheriting its
        // parent's) takes its own values with it. Writes on `to`, so the caller must own it.
        internal static void CopyStored(ZDO from, ZDO to) {
            byte[] stored = GetStored(from);
            if (to == null || stored == null || StoredModifierFormat.SameBytes(stored, GetStored(to))) { return; }
            to.Set(StoredKey, stored);
        }

        // What a creature's modifier storage holds as one number: the set and its own values. ModifierPeerRefresh waits
        // for another peer's copy of the ZDO to show the owner's.
        internal static int StoredContentHash(ZDO zdo) {
            if (zdo == null) { return 0; }
            return StoredModifierFormat.ContentHash(zdo.GetString(SLS_MODSV2, null), GetStored(zdo));
        }

        private static Decoded Decode(ZDO zdo) {
            if (zdo == null) { return null; }
            byte[] stored = zdo.GetByteArray(StoredKey);
            // The format byte alone is what clearing the last value leaves.
            if (stored == null || stored.Length <= 1) { return null; }
            if (Cache.TryGetValue(zdo.m_uid, out Decoded cached) && StoredModifierFormat.SameBytes(cached.Source, stored)) {
                // A peer gets a new array with every ZDO update; keep the newest, so the next check is a reference compare.
                cached.Source = stored;
                return cached;
            }
            StoredModifierFormat.TryDecodeOverrides(stored, out Dictionary<string, ModifierConfigOverride> overrides);
            Decoded decoded = new Decoded() { Source = stored, Overrides = overrides };
            Cache[zdo.m_uid] = decoded;
            return decoded;
        }

        private static bool Write(Character chara, Dictionary<string, ModifierConfigOverride> overrides) {
            ZDO zdo = chara.m_nview.GetZDO();
            byte[] current = GetStored(zdo);
            // Nothing stored and nothing to store: a creature that never had a value carries no key at all.
            if (current == null && overrides.Count == 0) { return false; }
            List<string> dropped = new List<string>();
            byte[] stored = StoredModifierFormat.EncodeOverrides(overrides, dropped);
            if (dropped.Count > 0) {
                Logger.LogDebug($"{chara.name}: values for {string.Join(", ", dropped)} have no modifier ID here and were not stored.");
            }
            // A byte array is only ever equal to itself to the ZDO, so an unchanged value would still be resent.
            if (StoredModifierFormat.SameBytes(current, stored)) { return false; }
            zdo.Set(StoredKey, stored);
            return true;
        }

        private static bool CanWrite(Character chara) {
            return chara != null && chara.m_nview != null && chara.m_nview.GetZDO() != null && CompositeLazyCache.IsZOwner(chara);
        }

        private static bool IsKnownModifier(string modifier, Character chara) {
            if (string.IsNullOrEmpty(modifier) == false && modifier != CreatureModifiers.NoMods && CreatureModifiersData.ModifierNamesLookupTable.ContainsName(modifier)) { return true; }
            Logger.LogWarning($"{modifier} is not a modifier; its values for {(chara != null ? chara.name : "a creature")} were not set.");
            return false;
        }

        private static Dictionary<string, ModifierConfigOverride> CopyOf(Dictionary<string, ModifierConfigOverride> overrides) {
            Dictionary<string, ModifierConfigOverride> copy = new Dictionary<string, ModifierConfigOverride>();
            if (overrides == null) { return copy; }
            foreach (KeyValuePair<string, ModifierConfigOverride> entry in overrides) {
                if (entry.Value != null) { copy[entry.Key] = entry.Value.Copy(); }
            }
            return copy;
        }

        private static CreatureModConfig Merge(CreatureModConfig configured, ModifierConfigOverride values) {
            CreatureModConfig merged = new CreatureModConfig() {
                BasePower = values.BasePower ?? configured.BasePower,
                PerlevelPower = values.PerlevelPower ?? configured.PerlevelPower,
                BiomeObjects = configured.BiomeObjects,
                Config = configured.Config,
            };
            if (values.Config != null && values.Config.Count > 0) {
                merged.Config = configured.Config != null ? new Dictionary<string, float>(configured.Config) : new Dictionary<string, float>();
                foreach (KeyValuePair<string, float> value in values.Config) { merged.Config[value.Key] = value.Value; }
            }
            return merged;
        }
    }
}
