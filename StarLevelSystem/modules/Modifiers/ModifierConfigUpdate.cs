using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.UI;
using System;
using System.Collections.Generic;
using System.IO;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Modifiers {

    // Brings the built-in modifiers a release adds into an existing Modifiers.yaml, with the player's say-so.
    //
    // A Modifiers.yaml lists each pool in full and a listed pool replaces the built-in one (CreatureModifiersData.
    // UpdateModifiers), so a modifier added in a new release never rolls on an install that already has the file. The
    // file's ModifiersVersion records which built-in set it has been checked against:
    //  - nothing missing: StampIfNothingMissing stamps the current version on load, and the framework rewrites the file;
    //  - something missing: the file is left alone and NewModifiersPrompt asks the player whether to add them. Either
    //    answer stamps the version, so a player who keeps their own list is asked once.
    // A dedicated server has nobody to ask, so it logs the missing names instead.
    internal static class ModifierConfigUpdate {
        internal struct MissingModifier {
            internal string Name;
            internal ModifierType Pool;
        }

        private static bool warnedHeadless;

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
            // A client playing under the server's file: not this machine's to change, or to ask about.
            if (ZNet.instance != null && ZNet.instance.IsServer() == false) { return; }

            List<MissingModifier> missing = Missing(loaded);
            if (missing.Count == 0) {
                // Added by hand, or answered, while the prompt was still waiting its turn.
                NewModifiersPrompt.Cancel();
                return;
            }

            if (GUIManager.IsHeadless()) {
                if (warnedHeadless) { return; }
                warnedHeadless = true;
                Logger.LogWarning($"{ValConfig.ModifiersFileName} does not list these built-in modifiers, so they never roll: " +
                    $"{string.Join(", ", Names(missing))}. Add their entries (a fresh copy of the file has them all) or use the " +
                    $"in-game config editor; setting ModifiersVersion: {CreatureModifiersData.CurrentModifiersVersion} keeps " +
                    "your list as it is and stops this warning.");
                return;
            }
            NewModifiersPrompt.Queue();
        }

        // The built-in modifiers added after this file's version that it lacks, one entry per pool. Only pools the file
        // fills in count: one it leaves out already falls back to the built-in list, and an emptied one was emptied on
        // purpose.
        internal static List<MissingModifier> Missing(CreatureModifierCollection file) {
            List<MissingModifier> missing = new List<MissingModifier>();
            if (file == null) { return missing; }
            for (int version = Math.Max(0, file.ModifiersVersion) + 1; version <= CreatureModifiersData.CurrentModifiersVersion; version++) {
                if (CreatureModifiersData.ModifiersAddedInVersion.TryGetValue(version, out List<string> names) == false) { continue; }
                foreach (string name in names) {
                    foreach (ModifierType pool in new[] { ModifierType.Major, ModifierType.Minor, ModifierType.Boss }) {
                        Dictionary<string, CreatureModifierConfiguration> builtIn = PoolOf(CreatureModifiersData.DefaultModifiers, pool);
                        Dictionary<string, CreatureModifierConfiguration> listed = PoolOf(file, pool);
                        if (builtIn == null || builtIn.ContainsKey(name) == false) { continue; }
                        if (listed == null || listed.Count == 0 || listed.ContainsKey(name)) { continue; }
                        missing.Add(new MissingModifier() { Name = name, Pool = pool });
                    }
                }
            }
            return missing;
        }

        // Each missing name once, in the order the releases added them.
        internal static List<string> Names(List<MissingModifier> missing) {
            List<string> names = new List<string>();
            foreach (MissingModifier m in missing) {
                if (names.Contains(m.Name) == false) { names.Add(m.Name); }
            }
            return names;
        }

        // What the file on disk is missing right now. The prompt asks this when it opens rather than trusting what was
        // missing when it was queued: the player may have edited the file while it waited.
        internal static List<string> MissingOnDisk() {
            CreatureModifierCollection onDisk = ReadFromDisk(out _);
            return onDisk == null ? new List<string>() : Names(Missing(onDisk));
        }

        // The player's answer. Works on the file on disk rather than the values in use, which on the main menu can still
        // be the last server's. Both answers stamp the version; only adding copies the missing entries in, each with its
        // built-in settings. A failure leaves the file unstamped, so the question comes back next launch.
        internal static void Resolve(bool add) {
            YamlConfigFile<CreatureModifierCollection> file = YamlConfigManager.ModifierSettings;
            CreatureModifierCollection onDisk = ReadFromDisk(out string problem);
            if (onDisk == null) {
                Logger.LogWarning($"Could not update {ValConfig.ModifiersFileName}, so it was left as it is: {problem}");
                return;
            }

            List<string> added = new List<string>();
            if (add) {
                List<MissingModifier> missing = Missing(onDisk);
                foreach (MissingModifier m in missing) {
                    PoolOf(onDisk, m.Pool)[m.Name] = CopyOf(file, PoolOf(CreatureModifiersData.DefaultModifiers, m.Pool)[m.Name]);
                }
                added = Names(missing);
            }
            onDisk.ModifiersVersion = CreatureModifiersData.CurrentModifiersVersion;

            if (YamlConfigManager.ApplyEdited(file, YamlConfigManager.SerializeForEdit(file, onDisk), out string message) == false) {
                Logger.LogWarning($"Could not update {file.FileName}: {message}");
                return;
            }
            if (added.Count > 0) {
                Logger.LogInfo($"Added {string.Join(", ", added)} to {file.FileName}.");
            } else {
                Logger.LogInfo($"{file.FileName} keeps its own modifier list; you will not be asked about these modifiers again.");
            }
        }

        private static CreatureModifierCollection ReadFromDisk(out string problem) {
            problem = null;
            YamlConfigFile<CreatureModifierCollection> file = YamlConfigManager.ModifierSettings;
            if (file == null || string.IsNullOrEmpty(file.Path) || File.Exists(file.Path) == false) {
                problem = "the file is not on disk";
                return null;
            }
            try {
                // Tolerant, like a normal load: one unknown key must not stop the answer from being saved.
                CreatureModifierCollection parsed = file.EffectiveFormat.TolerantDeserializer.Deserialize<CreatureModifierCollection>(File.ReadAllText(file.Path));
                if (parsed == null) { problem = "it is empty"; }
                return parsed;
            } catch (Exception e) {
                problem = e.Message;
                return null;
            }
        }

        // A deep copy through yaml, so the file never shares an object with the built-in defaults -- several entries
        // there share one list (NonCombatCreatures), and an edit to one copy must not reach the others.
        private static CreatureModifierConfiguration CopyOf(YamlConfigFile<CreatureModifierCollection> file, CreatureModifierConfiguration entry) {
            YamlFormat format = file.EffectiveFormat;
            return format.Deserializer.Deserialize<CreatureModifierConfiguration>(format.Serializer.Serialize(entry));
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
