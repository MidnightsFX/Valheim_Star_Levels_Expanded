using Jotunn.Entities;
using System;
using YamlDotNet.Core;

#pragma warning disable IDE0130
namespace StarLevelSystem.common {
#pragma warning restore IDE0130

    // Where a set of values came from. Only ServerSync is load-bearing: it marks the one case where this
    // machine is NOT the owner of the file, so nothing may be written back to disk on its behalf.
    internal enum ConfigOrigin { Startup, LocalFile, ServerSync, Api }

    // What happens when a file cannot be loaded. KeepLastGood is the default and is almost always right:
    // an admin who breaks a file mid-session keeps the settings that were working until they fix it.
    // Only reach for RestoreFileOnDisk when a malformed file is genuinely unrecoverable.
    internal enum ConfigFailurePolicy { KeepLastGood, RevertToDefaults, RestoreFileOnDisk }

    // ServerAuthoritative registers an RPC and pushes the server's copy to every client.
    // LocalOnly is for save data and per-machine files that must never travel.
    internal enum ConfigSyncMode { ServerAuthoritative, LocalOnly }

    internal enum UnknownKeyPolicy { WarnAndContinue, Strict, Silent }

    // A single yaml file: where it lives, what it deserializes to, how it is validated, how it fails, and
    // how it syncs. Register one of these per file with YamlConfigManager and the framework owns the
    // rest -- default generation, header preservation, watching, RPC wiring, initial sync and broadcast.
    internal abstract class YamlConfigFile {
        // --- Set by the mod at registration ---

        internal string FileName { get; set; }
        // Subfolder under the mod's config directory. Null means the config root. Changing this for a
        // file that already ships is a breaking change: every existing install looks in the old place.
        internal string SubFolder { get; set; }
        // The comment block written above the content. For most files this is the ONLY documentation an
        // admin will ever read, so it is worth more than the code below it. Every write goes through
        // YamlConfigManager so this survives.
        internal string Header { get; set; }
        // Defaults to PluginName + "_" + the file name without its extension.
        internal string RpcName { get; set; }
        // Null uses YamlFormat.Default.
        internal YamlFormat Format { get; set; }
        internal ConfigFailurePolicy OnFailure { get; set; } = ConfigFailurePolicy.KeepLastGood;
        internal ConfigSyncMode Sync { get; set; } = ConfigSyncMode.ServerAuthoritative;
        internal UnknownKeyPolicy UnknownKeys { get; set; } = UnknownKeyPolicy.WarnAndContinue;
        // Whether a client persists what the server sent. False keeps the server's values in memory only
        // and leaves the client's own file untouched; true mirrors them to disk so the client can read
        // what it is playing under. Either is defensible -- pick one deliberately.
        internal bool ClientWritesToDisk { get; set; }
        internal bool Watch { get; set; } = true;
        // Set when Validate looks up prefabs. The prefab table does not exist during Awake, so the mod
        // re-runs validation from PrefabManager.OnPrefabsRegistered rather than emitting a wall of false
        // "not found" warnings at startup.
        internal bool NeedsPrefabs { get; set; }
        // 0 means unversioned. When set, GetSchemaVersion must also be supplied.
        internal int SchemaVersion { get; set; }
        // Whether a connected admin may upload a replacement for this file. Opt-in, and default false on
        // purpose: copying this folder into a mod must never silently open a write channel into its
        // config. Save-data files should always leave this off.
        internal bool AllowAdminEdit { get; set; }

        // --- Owned by the framework ---

        internal string Path { get; set; }
        internal CustomRPC Rpc { get; set; }
        // Only created when AllowAdminEdit is set. Carries admin uploads up and the accept/refuse answer
        // back down.
        internal CustomRPC EditRpc { get; set; }
        internal bool LastLoadFailed { get; set; }
        internal string LastError { get; set; }
        internal DateTime LastLoadedUtc { get; set; }
        internal ValidationReport LastReport { get; set; }

        internal YamlFormat EffectiveFormat {
            get { return Format ?? YamlFormat.Default; }
        }

        internal abstract Type ValueType { get; }
        internal abstract string SerializeDefaults();
        internal abstract string SerializeCurrent();
        internal abstract bool LoadFrom(string yaml, ConfigOrigin origin);
        internal abstract ValidationReport Revalidate();

        // Parse and validate some candidate yaml WITHOUT applying any of it: nothing is assigned, nothing
        // is published, nothing is written, and none of the Last* fields move.
        //
        // This exists because LoadFrom cannot be used to test a candidate. LoadFrom routes failure through
        // OnFailure, so on a file registered RestoreFileOnDisk a REJECTED upload would overwrite the
        // owner's perfectly good file with defaults. Anything that needs to ask "would this be accepted?"
        // -- an admin upload, an editor's Validate button -- has to come through here.
        //
        // parseError is null when the document parsed; the returned report then carries the verdict.
        internal abstract ValidationReport DryRun(string yaml, out string parseError);
    }

    internal sealed class YamlConfigFile<T> : YamlConfigFile where T : class {
        internal YamlConfigFile(string fileName) {
            FileName = fileName;
        }

        // Deferred rather than a plain value, because some defaults can only be built once the game has
        // loaded something (a prefab table, a zone system). Called on demand, never cached.
        internal Func<T> Defaults { get; set; }
        // Publish the loaded values wherever the mod reads them from. This is the one line that differs
        // between config files; everything around it is the same for all of them.
        internal Action<T> Apply { get; set; }
        // (newValue, previousValue). previousValue is null on the first load. Taking both is what lets a
        // validator diff the two and warn about things that were REMOVED -- the failure mode an admin is
        // least likely to notice on their own.
        internal Func<T, T, ValidationReport> Validate { get; set; }
        internal Func<T, T> Migrate { get; set; }
        internal Func<T, int> GetSchemaVersion { get; set; }
        internal Action<T, int> SetSchemaVersion { get; set; }

        // A one-shot format fix for a file whose value type has nowhere to put a version stamp -- a
        // bare dictionary at the root, say. Runs after a successful parse and before Validate; return
        // true if anything changed, and the framework rewrites the file so the change sticks.
        //
        // Such a migration MUST erase whatever it keys off. There is no stamp to say "already done", so
        // if the trigger survives the rewrite it fires again on every load -- and would undo an admin's
        // later edits forever.
        internal Func<T, bool> MigrateInPlace { get; set; }

        // Never null once the manager has prepared this file: a failed first load falls back to defaults.
        internal T Value { get; private set; }

        internal override Type ValueType {
            get { return typeof(T); }
        }

        internal override string SerializeDefaults() {
            T defaults = BuildDefaults();
            return defaults == null ? "" : EffectiveFormat.Serializer.Serialize(defaults);
        }

        internal override string SerializeCurrent() {
            return Value == null ? SerializeDefaults() : EffectiveFormat.Serializer.Serialize(Value);
        }

        internal override ValidationReport DryRun(string yaml, out string parseError) {
            parseError = null;

            if (YamlConfigManager.HasNoUsableConfig(yaml)) {
                parseError = "it is empty or contains only comments";
                return new ValidationReport();
            }

            T parsed = Deserialize(yaml, out string reason);
            if (parsed == null) {
                parseError = reason ?? "it could not be parsed";
                return new ValidationReport();
            }

            // Run the same in-place migration a real load would, so the candidate is judged in the shape
            // it would actually end up in. Safe: this is a throwaway object.
            if (MigrateInPlace != null) {
                try {
                    MigrateInPlace(parsed);
                } catch (Exception e) {
                    Logger.LogWarning($"{FileName} migration threw during a dry run: {e.Message}");
                }
            }

            if (Validate == null) { return new ValidationReport(); }

            try {
                return Validate(parsed, Value) ?? new ValidationReport();
            } catch (Exception e) {
                return new ValidationReport().Error($"the validator threw: {e.Message}");
            }
        }

        internal override ValidationReport Revalidate() {
            if (Validate == null || Value == null) { return new ValidationReport(); }

            ValidationReport report;
            try {
                // Current values against themselves: no diff, just the intrinsic checks. This is the pass
                // that finally resolves prefab names once the game has registered them.
                report = Validate(Value, Value) ?? new ValidationReport();
            } catch (Exception e) {
                report = new ValidationReport().Error($"the validator threw: {e.Message}");
            }

            LastReport = report;
            LogReport(report);
            return report;
        }

        internal override bool LoadFrom(string yaml, ConfigOrigin origin) {
            string reason;
            T parsed = null;

            if (YamlConfigManager.HasNoUsableConfig(yaml)) {
                reason = "it is empty or contains only comments";
            } else {
                parsed = Deserialize(yaml, out reason);
            }

            if (parsed == null) { return Fail(reason, origin); }

            bool changedByMigration = false;

            if (SchemaVersion > 0 && GetSchemaVersion != null) {
                if (ApplySchemaVersion(ref parsed, out string versionProblem, out changedByMigration) == false) {
                    return Fail(versionProblem, origin);
                }
            }

            if (MigrateInPlace != null) {
                try {
                    if (MigrateInPlace(parsed)) { changedByMigration = true; }
                } catch (Exception e) {
                    // A failed migration is not worth discarding the file over -- the values parsed fine,
                    // they are just still in the old shape.
                    Logger.LogWarning($"{FileName} migration threw, the file was left as it is: {e.Message}");
                }
            }

            ValidationReport report = new ValidationReport();
            if (Validate != null) {
                try {
                    report = Validate(parsed, Value) ?? new ValidationReport();
                } catch (Exception e) {
                    report = new ValidationReport().Error($"the validator threw: {e.Message}");
                }
            }

            LastReport = report;
            LogReport(report);
            if (report.HasErrors) {
                return Fail(string.Join(" ", report.Errors.ToArray()), origin);
            }

            Value = parsed;
            LastLoadFailed = false;
            LastError = null;
            LastLoadedUtc = DateTime.UtcNow;
            Publish();

            // Persist a migration, but only on the machine that owns the file. Without this the
            // migrated values live in memory only, the file on disk keeps its old shape, and the
            // migration runs again on every single load. A client applying what the server sent is not
            // the owner -- the server has already migrated its own copy and sent the result.
            if (changedByMigration && origin != ConfigOrigin.ServerSync
                && (ZNet.instance == null || ZNet.instance.IsServer())) {
                Logger.LogInfo($"{FileName} was migrated to the current format; rewriting it.");
                YamlConfigManager.WriteCurrentToDisk(this);
            }

            return true;
        }

        // Strict first, then tolerant. The strict pass is what produces a precise "line N, this key"
        // message; the tolerant pass is what keeps the other 99% of the file working while the admin
        // fixes it. Doing it in that order is the whole point -- a tolerant-only parse would silently
        // swallow the typo and never mention it.
        private T Deserialize(string yaml, out string reason) {
            reason = null;
            YamlFormat format = EffectiveFormat;

            try {
                T strict = format.Deserializer.Deserialize<T>(yaml);
                // YamlDotNet returns null for a document with no content WITHOUT throwing, so a caller's
                // try/catch never sees it. Handled here so no config class has to remember.
                if (strict == null) { reason = "it is empty or contains only comments"; }
                return strict;
            } catch (YamlException strictError) {
                if (UnknownKeys == UnknownKeyPolicy.Strict) {
                    reason = Describe(strictError);
                    return null;
                }

                try {
                    T tolerant = format.TolerantDeserializer.Deserialize<T>(yaml);
                    if (tolerant == null) {
                        reason = "it is empty or contains only comments";
                        return null;
                    }
                    if (UnknownKeys == UnknownKeyPolicy.WarnAndContinue) {
                        Logger.LogWarning($"{FileName} {Describe(strictError)} That setting was ignored; " +
                            "the rest of the file loaded normally.");
                    }
                    return tolerant;
                } catch (YamlException tolerantError) {
                    // Not an unknown key -- the document is genuinely malformed.
                    reason = Describe(tolerantError);
                    return null;
                }
            } catch (Exception other) {
                reason = other.Message;
                return null;
            }
        }

        private bool ApplySchemaVersion(ref T parsed, out string problem, out bool changed) {
            problem = null;
            changed = false;

            int found;
            try {
                found = GetSchemaVersion(parsed);
            } catch (Exception e) {
                problem = $"the schema version could not be read: {e.Message}";
                return false;
            }

            if (found == SchemaVersion) { return true; }

            if (Migrate == null) {
                problem = $"it is schema version {found} but this mod expects {SchemaVersion}, and there is no migration for it";
                return false;
            }

            T migrated;
            try {
                migrated = Migrate(parsed);
            } catch (Exception e) {
                problem = $"migrating from schema version {found} to {SchemaVersion} threw: {e.Message}";
                return false;
            }

            if (migrated == null) {
                problem = $"migrating from schema version {found} to {SchemaVersion} produced nothing";
                return false;
            }

            parsed = migrated;
            SetSchemaVersion?.Invoke(parsed, SchemaVersion);
            changed = true;
            Logger.LogInfo($"{FileName} migrated from schema version {found} to {SchemaVersion}.");
            return true;
        }

        private bool Fail(string reason, ConfigOrigin origin) {
            LastLoadFailed = true;
            LastError = reason;

            switch (OnFailure) {
                case ConfigFailurePolicy.RevertToDefaults:
                    PublishDefaults();
                    Logger.LogError($"{FileName} could not be loaded because {reason}. This mod's built-in " +
                        "defaults are in use; your file was left alone.");
                    break;

                case ConfigFailurePolicy.RestoreFileOnDisk:
                    PublishDefaults();
                    // Only the owner of a file may rewrite it. A client that failed to parse what the
                    // server sent must not take that out on its own copy on disk.
                    if (origin != ConfigOrigin.ServerSync && (ZNet.instance == null || ZNet.instance.IsServer())) {
                        YamlConfigManager.RestoreDefaults(this);
                        Logger.LogError($"{FileName} could not be loaded because {reason}. It has been " +
                            "overwritten with this mod's built-in defaults.");
                    } else {
                        Logger.LogError($"{FileName} could not be loaded because {reason}. This mod's " +
                            "built-in defaults are in use; the file was left alone because this machine does not own it.");
                    }
                    break;

                default:
                    if (Value == null) {
                        // Nothing has ever loaded cleanly, so there is no "last good" to keep.
                        PublishDefaults();
                        Logger.LogError($"{FileName} could not be loaded because {reason}. Nothing had " +
                            "loaded successfully yet, so this mod's built-in defaults are in use; your file was left alone.");
                    } else {
                        Logger.LogError($"{FileName} could not be loaded because {reason}. The values that " +
                            "last loaded cleanly are still in use; your file was left alone.");
                    }
                    break;
            }

            return false;
        }

        private void PublishDefaults() {
            T defaults = BuildDefaults();
            if (defaults == null) { return; }
            Value = defaults;
            Publish();
        }

        private void Publish() {
            try {
                Apply?.Invoke(Value);
            } catch (Exception e) {
                Logger.LogError($"{FileName} apply hook threw, the mod may be in a half-configured state: {e}");
            }
        }

        private T BuildDefaults() {
            if (Defaults == null) { return null; }
            try {
                T defaults = Defaults();
                if (defaults == null) { return null; }
                // Deep-clone: several Defaults factories return a shared static instance. Publishing
                // that instance as the live Value would let runtime mutations (levelup generators,
                // miniboss add/remove) corrupt the process-wide defaults for the rest of the session.
                YamlFormat format = EffectiveFormat;
                return format.Deserializer.Deserialize<T>(format.Serializer.Serialize(defaults));
            } catch (Exception e) {
                Logger.LogError($"{FileName} default factory threw: {e}");
                return null;
            }
        }

        private void LogReport(ValidationReport report) {
            if (report == null) { return; }
            for (int i = 0; i < report.Warnings.Count; i++) {
                Logger.LogWarning($"{FileName}: {report.Warnings[i]}");
            }
        }

        private static string Describe(YamlException e) {
            // The inner exception carries the useful text ("Property 'Foo' not found on type ...");
            // the outer one is usually just "Exception during deserialization".
            string message = e.InnerException != null ? e.InnerException.Message : e.Message;
            return $"line {e.Start.Line}: {message}";
        }
    }
}
