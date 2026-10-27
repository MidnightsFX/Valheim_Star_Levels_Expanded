using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Modifiers {

    // Points out the built-in modifiers a release adds that an existing Modifiers.yaml does not list.
    //
    // A Modifiers.yaml lists each pool in full and a listed pool replaces the built-in one (CreatureModifiersData.
    // UpdateModifiers), so a modifier added in a new release never rolls on an install that already has the file. The
    // file's ModifiersVersion records which built-in set it has been checked against:
    //  - nothing missing: StampIfNothingMissing stamps the current version on load, and the framework rewrites the file;
    //  - something missing: the file is left alone and the missing names are logged, once a session. Adding them, or
    //    setting the version by hand, stops the warning.
    internal static class ModifierConfigUpdate {
        private static bool warned;

        // MigrateInPlace hook for Modifiers.yaml. Runs on every load before validation -- a dry run and a client
        // applying the server's file included -- and the framework only persists a change on the machine that owns
        // the file. Setting the version erases its own trigger, as MigrateInPlace requires.
        internal static bool StampIfNothingMissing(CreatureModifierCollection file) {
            if (file == null || file.ModifiersVersion >= CreatureModifiersData.CurrentModifiersVersion) { return false; }
            if (Missing(file).Count > 0) { return false; }
            file.ModifiersVersion = CreatureModifiersData.CurrentModifiersVersion;
            return true;
        }

        // Called from CreatureModifiersData.ApplyLoaded after every load of Modifiers.yaml.
        internal static void Check(CreatureModifierCollection loaded) {
            // A client playing under the server's file: not this machine's to change.
            if (ZNet.instance != null && ZNet.instance.IsServer() == false) { return; }
            if (warned) { return; }

            List<string> missing = Missing(loaded);
            if (missing.Count == 0) { return; }
            warned = true;
            Logger.LogWarning($"{ValConfig.ModifiersFileName} does not list these built-in modifiers, so they never roll: " +
                $"{string.Join(", ", missing)}. Add their entries (a fresh copy of the file has them all) or use the " +
                $"in-game config editor; setting ModifiersVersion: {CreatureModifiersData.CurrentModifiersVersion} keeps " +
                "your list as it is and stops this warning.");
        }

        // The built-in modifiers added after this file's version that it lacks, each name once, in the order the releases
        // added them. Only pools the file fills in count: one it leaves out already falls back to the built-in list, and
        // an emptied one was emptied on purpose.
        internal static List<string> Missing(CreatureModifierCollection file) {
            List<string> missing = new List<string>();
            if (file == null) { return missing; }
            for (int version = Math.Max(0, file.ModifiersVersion) + 1; version <= CreatureModifiersData.CurrentModifiersVersion; version++) {
                if (CreatureModifiersData.ModifiersAddedInVersion.TryGetValue(version, out List<string> names) == false) { continue; }
                foreach (string name in names) {
                    foreach (ModifierType pool in new[] { ModifierType.Major, ModifierType.Minor, ModifierType.Boss }) {
                        Dictionary<string, CreatureModifierConfiguration> builtIn = PoolOf(CreatureModifiersData.DefaultModifiers, pool);
                        Dictionary<string, CreatureModifierConfiguration> listed = PoolOf(file, pool);
                        if (builtIn == null || builtIn.ContainsKey(name) == false) { continue; }
                        if (listed == null || listed.Count == 0 || listed.ContainsKey(name)) { continue; }
                        if (missing.Contains(name) == false) { missing.Add(name); }
                    }
                }
            }
            return missing;
        }

        private static Dictionary<string, CreatureModifierConfiguration> PoolOf(CreatureModifierCollection collection, ModifierType pool) {
            switch (pool) {
                case ModifierType.Minor: return collection.MinorModifiers;
                case ModifierType.Boss: return collection.BossModifiers;
                default: return collection.MajorModifiers;
            }
        }
    }
}
