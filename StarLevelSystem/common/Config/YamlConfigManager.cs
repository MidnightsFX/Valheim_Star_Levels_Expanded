using System;
using System.Collections.Generic;
using System.IO;

#pragma warning disable IDE0130
namespace StarLevelSystem.common {
#pragma warning restore IDE0130

    // The registry every yaml config file goes through.
    //
    // Owns the parts that are identical for every file -- resolving the path, generating a default file,
    // keeping the documented header in front of the content on every write, watching for edits, wiring
    // the sync RPC -- so a config file is a declaration rather than a implementation. Add yours in
    // RegisterConfigFiles(); see Examples/ExampleYamlConfig.cs.
    internal static partial class YamlConfigManager {
        private static bool initialized;
        private static readonly List<YamlConfigFile> Files = new List<YamlConfigFile>();
        private static readonly Dictionary<string, YamlConfigFile> ByPath =
            new Dictionary<string, YamlConfigFile>(StringComparer.OrdinalIgnoreCase);

        internal static IEnumerable<YamlConfigFile> All {
            get { return Files; }
        }

        // Call once from Awake, after the mod's config class is constructed (this reads cfgFolder,
        // ConfigPollIntervalSeconds and ConfigApplyDelay from it).
        internal static void Init() {
            if (initialized) { return; }
            initialized = true;

            // Before any file registers: RegisterFile needs NetworkManager, and the sync-state teardown
            // patch has to be in place before a world is ever loaded.
            ConfigNetwork.Init();

            // On by default rather than left to the mod. An unrecognised enum VALUE is the most likely
            // mistake in a hand-edited config and, without this, the only one that still takes the whole
            // file down -- IgnoreUnmatchedProperties covers keys, not values. Add your own converters
            // through YamlFormat.AddTypeConverter before calling Init.
            YamlFormat.AddTypeConverter(new TolerantEnumConverter());

            RegisterConfigFiles();
            ConfigFileWatcher.Initialize();

            Logger.LogDebug($"Registered {Files.Count} yaml config files.");
        }

        // The only way to add a file. Registering after Init has run is supported and is how a config
        // whose defaults need game state gets in -- hook PrefabManager.OnPrefabsRegistered (or whatever
        // your defaults depend on) and call this from there.
        internal static TFile Register<TFile>(TFile file) where TFile : YamlConfigFile {
            if (file == null) { return null; }
            Files.Add(file);
            if (initialized) { Prepare(file); }
            return file;
        }

        internal static YamlConfigFile Find(string fileNameOrPath) {
            if (string.IsNullOrEmpty(fileNameOrPath)) { return null; }
            if (ByPath.TryGetValue(fileNameOrPath, out YamlConfigFile found)) { return found; }
            for (int i = 0; i < Files.Count; i++) {
                if (string.Equals(Files[i].FileName, fileNameOrPath, StringComparison.OrdinalIgnoreCase)) {
                    return Files[i];
                }
            }
            return null;
        }

        // Creates the directory as a side effect, so callers can treat the result as ready to write into.
        internal static string ConfigDirectory(string subFolder = null) {
            string path = Path.Combine(BepInEx.Paths.ConfigPath, ValConfig.cfgFolder);
            if (string.IsNullOrEmpty(subFolder) == false) { path = Path.Combine(path, subFolder); }
            return Directory.CreateDirectory(path).FullName;
        }

        internal static void ReloadFromDisk(YamlConfigFile file, bool broadcast = true) {
            if (file == null) { return; }

            try {
                if (File.Exists(file.Path) == false) {
                    Logger.LogWarning($"{file.FileName} is no longer on disk; rewriting it with this mod's built-in defaults.");
                    RestoreDefaults(file);
                }

                bool loaded = file.LoadFrom(File.ReadAllText(file.Path), ConfigOrigin.LocalFile);
                // Only a clean load goes out to the peers. Pushing a file that just failed to parse would
                // hand every client the same breakage the admin is still fixing.
                if (loaded && broadcast) { ConfigNetwork.Broadcast(file); }
            } catch (Exception e) {
                Logger.LogError($"Could not reload {file.FileName}: {e.Message}");
            }
        }

        // The single apply path for every editor-driven change, whether it came from a panel on this
        // machine or from an admin's upload. Returns false with a human-readable reason on refusal.
        //
        // Yaml text is the unit of exchange rather than a live object, deliberately. Text is the one
        // representation that is identical on disk, on the wire and after validation; an object overload
        // would need a second validation path and would hand Value an object the editor still holds a
        // reference to, which is exactly how a mod ends up serving edited-but-stale cached values.
        internal static bool ApplyEdited(YamlConfigFile file, string yaml, out string message) {
            message = "";
            if (file == null) { message = "no config file was named"; return false; }

            // Only the machine that owns the file may take this path. A client's editor goes through
            // ConfigNetwork.RequestEdit and the SERVER ends up here instead.
            if (ZNet.instance != null && ZNet.instance.IsServer() == false) {
                message = $"{file.FileName} belongs to the server; changes have to be sent to it.";
                return false;
            }

            ValidationReport report = file.DryRun(yaml, out string parseError);
            if (parseError != null) {
                message = $"{file.FileName} was rejected because {parseError}.";
                return false;
            }
            if (report.HasErrors) {
                message = string.Join(" ", report.Errors.ToArray());
                return false;
            }

            if (file.LoadFrom(yaml, ConfigOrigin.Api) == false) {
                message = file.LastError ?? $"{file.FileName} could not be applied.";
                return false;
            }

            // The exact bytes that were validated, so what is on disk is what was judged -- and the
            // documented header survives, because this goes through WriteRawToDisk.
            WriteRawToDisk(file, yaml);

            // Explicit, and required: WriteRawToDisk re-stamps the watcher so it will not see our own
            // write, which means the watcher-driven broadcast never fires for this path.
            ConfigNetwork.Broadcast(file);

            message = report.Warnings.Count == 0 ? "" : string.Join(" ", report.Warnings.ToArray());
            return true;
        }

        // So an editor cannot pick its own serializer and hand back a document the framework would never
        // have written itself.
        internal static string SerializeForEdit<T>(YamlConfigFile<T> file, T value) where T : class {
            if (file == null || value == null) { return ""; }
            return file.EffectiveFormat.Serializer.Serialize(value);
        }

        internal static void RestoreDefaults(YamlConfigFile file) {
            WriteRawToDisk(file, file?.SerializeDefaults());
        }

        internal static void WriteCurrentToDisk(YamlConfigFile file) {
            WriteRawToDisk(file, file?.SerializeCurrent());
        }

        // Every write to a registered config file goes through here rather than File.WriteAllText.
        //
        // The header is the only documentation of the schema an admin ever sees -- it is what tells them
        // which fields exist, what the enums accept and what the numbers mean. A bare write silently
        // deletes it and nobody notices until someone needs it.
        internal static void WriteRawToDisk(YamlConfigFile file, string serializedYaml) {
            if (file == null || string.IsNullOrEmpty(file.Path)) { return; }

            try {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Path));
                // Scoped so the handle is closed before RefreshStamp reads the file's length and write
                // time -- a using declaration would dispose after that and re-seed a stale stamp.
                using (StreamWriter writer = new StreamWriter(file.Path)) {
                    if (string.IsNullOrEmpty(file.Header) == false) { writer.WriteLine(file.Header); }
                    writer.WriteLine(serializedYaml);
                }
                ConfigFileWatcher.RefreshStamp(file.Path);
            } catch (Exception e) {
                Logger.LogError($"Could not write {file.FileName}: {e.Message}");
            }
        }

        // Re-run every file's validator against the values already loaded. Call this once the game state a
        // validator depends on exists -- most often PrefabManager.OnPrefabsRegistered -- and whenever a
        // BepInEx entry a validator cross-checks has changed.
        internal static void RevalidateAll() {
            for (int i = 0; i < Files.Count; i++) {
                try {
                    Files[i].Revalidate();
                } catch (Exception e) {
                    Logger.LogError($"Revalidating {Files[i].FileName} threw: {e.Message}");
                }
            }
        }

        // True when the text is null/whitespace or consists only of comments, blank lines and bare YAML
        // document markers. Such a file deserializes to null rather than throwing, so without this check
        // an admin who empties a file to "start over" gets a silent fallback to built-in defaults with no
        // idea why their edits do nothing.
        internal static bool HasNoUsableConfig(string yamlText) {
            if (string.IsNullOrWhiteSpace(yamlText)) { return true; }
            foreach (string line in yamlText.Split('\n')) {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed == "---" || trimmed == "...") { continue; }
                return false;
            }
            return true;
        }

        private static void Prepare(YamlConfigFile file) {
            try {
                file.Path = Path.Combine(ConfigDirectory(file.SubFolder), file.FileName);
                ByPath[file.Path] = file;

                if (File.Exists(file.Path) == false) {
                    Logger.LogDebug($"{file.FileName} missing, writing this mod's built-in defaults.");
                    RestoreDefaults(file);
                } else if (HasNoUsableConfig(File.ReadAllText(file.Path))) {
                    Logger.LogWarning($"{file.FileName} was empty and has been overwritten with this mod's " +
                        $"built-in defaults. File: {file.Path}");
                    RestoreDefaults(file);
                }

                file.LoadFrom(File.Exists(file.Path) ? File.ReadAllText(file.Path) : "", ConfigOrigin.Startup);

                ConfigNetwork.RegisterFile(file);
                if (file.Watch) { ConfigFileWatcher.Register(file.Path, OnWatchedFileChanged); }
            } catch (Exception e) {
                // One unusable file must not take Awake down with it -- every other config, and the rest
                // of the mod, still loads.
                Logger.LogError($"Could not prepare {file.FileName}: {e}");
            }
        }

        private static void OnWatchedFileChanged(string path) {
            if (ByPath.TryGetValue(path, out YamlConfigFile file) == false) { return; }

            // Debounced rather than reloaded straight off the poll: most editors save by truncating and
            // then writing, which the watcher sees as two separate changes, and a reload is not cheap
            // (parse, validate, apply, broadcast to every peer).
            ConfigChangeDebouncer.Schedule(file, () => ReloadFromDisk(file, broadcast: true));
        }
    }
}
