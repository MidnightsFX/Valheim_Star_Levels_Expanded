using BepInEx;
using System;
using System.IO;
using System.Reflection;
using System.Text;

#pragma warning disable IDE0130
namespace StarLevelSystem.common {
#pragma warning restore IDE0130

    // Fails the YamlDotNet dependency loudly instead of cryptically.
    //
    // YamlDotNet is a shared Thunderstore package (ValheimModding-YamlDotNet), not something this mod
    // ships, so whatever sits in BepInEx/plugins is whatever the player's profile resolved -- and 16.0.0
    // changed IYamlTypeConverter out from under everyone:
    //
    //   <= 15.x   object ReadYaml(IParser, Type)                     void WriteYaml(IEmitter, object, Type)
    //   >= 16.0   object ReadYaml(IParser, Type, ObjectDeserializer) void WriteYaml(IEmitter, object, Type, ObjectSerializer)
    //
    // We compile against 16.3, so against an older copy Mono cannot build the interface vtable for our
    // converters and the first one the mod touches dies with
    //
    //     TypeLoadException: VTable setup of type StarLevelSystem.common.TolerantEnumConverter failed
    //
    // which names one of our types and says nothing about the real problem. A missing YamlDotNet.dll
    // reports the same way, since the interface then cannot be resolved either. Both are a player-side
    // install problem, and both are invisible from that message, so check it up front and say so.
    //
    // Nothing in here may reference a YamlDotNet type by name: naming one is exactly what triggers the
    // load this is trying to get in front of. Reflection over strings only.
    internal static class YamlDotNetCheck {
        private const string YamlAssembly = "YamlDotNet";
        private const string ConverterInterface = "YamlDotNet.Serialization.IYamlTypeConverter";
        // ReadYaml gained its rootDeserializer parameter in 16.0.0. That count IS the compatibility test,
        // and a better one than a version number: the Thunderstore package version (16.3.1) and the
        // assembly version it ships (16.0.0.0) do not match, so a version comparison invites an
        // off-by-one against whichever of the two someone had in mind. It also catches a future YamlDotNet
        // that changes the interface again, which a "is it old enough" test would wave through.
        private const int ExpectedReadYamlParameters = 3;

        private const string Needed = "the 'YamlDotNet' package by ValheimModding, 16.3.1 or newer";

        // False means: do not initialize anything. Every setting this mod has lives in a yaml file, so
        // there is no degraded mode worth running -- carrying on would just trade one clear error for a
        // stream of null references out of half-loaded config.
        internal static bool Verify() {
            Assembly yaml = Find();
            if (yaml == null) {
                Logger.LogError("StarLevelSystem did not start: YamlDotNet is not installed. Every setting " +
                    "this mod has is a yaml file, so nothing loads without it. Install " + Needed +
                    " -- your mod manager can add it as a dependency of StarLevelSystem, or you can drop " +
                    "YamlDotNet.dll into BepInEx/plugins by hand.");
                return false;
            }

            int parameters = ReadYamlParameterCount(yaml);
            if (parameters == ExpectedReadYamlParameters) { return true; }

            string what = parameters < 0
                ? $"it has no {ConverterInterface}, so it is not a YamlDotNet this mod can use"
                : $"its IYamlTypeConverter.ReadYaml takes {parameters} parameters and StarLevelSystem was " +
                  $"built against the {ExpectedReadYamlParameters}-parameter one, so its converters cannot " +
                  $"load against it (fewer parameters means a pre-16.0 copy, which is the usual cause)";
            Logger.LogError($"StarLevelSystem did not start: the YamlDotNet loaded in this profile is the " +
                $"wrong one -- {what}. Found assembly version {yaml.GetName().Version}{Origin(yaml)}; " +
                $"StarLevelSystem needs {Needed}, whose assembly reports version 16.0.0.0. Update it, and " +
                $"delete any YamlDotNet.dll another mod dropped inside its own folder -- the shared package " +
                $"is the only copy that should be installed.{Copies()}");
            return false;
        }

        private static Assembly Find() {
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loaded.Length; i++) {
                if (string.Equals(loaded[i].GetName().Name, YamlAssembly, StringComparison.OrdinalIgnoreCase)) {
                    return loaded[i];
                }
            }
            // Not loaded yet is the normal case at this point in Awake: YamlDotNet.dll carries no plugin,
            // so BepInEx only resolves it the first time something asks for it. Ask now.
            try { return Assembly.Load(YamlAssembly); }
            catch (Exception) { return null; }
        }

        private static int ReadYamlParameterCount(Assembly yaml) {
            try {
                Type iface = yaml.GetType(ConverterInterface, false);
                MethodInfo read = iface?.GetMethod("ReadYaml");
                return read == null ? -1 : read.GetParameters().Length;
            } catch (Exception) {
                return -1;
            }
        }

        private static string Origin(Assembly yaml) {
            try {
                string location = yaml.Location;
                return string.IsNullOrEmpty(location) ? "" : $" at '{location}'";
            } catch (Exception) {
                return "";
            }
        }

        // Names every folder under BepInEx/plugins holding a YamlDotNet.dll. Two copies is the usual
        // shape of this, and this line is the difference between a player reporting "which mod?" and
        // reporting the folder that shipped the stale one. Failure path only.
        private static string Copies() {
            try {
                string plugins = Paths.PluginPath;
                if (string.IsNullOrEmpty(plugins) || Directory.Exists(plugins) == false) { return ""; }

                string[] found = Directory.GetFiles(plugins, "YamlDotNet.dll", SearchOption.AllDirectories);
                if (found.Length == 0) { return ""; }

                StringBuilder sb = new StringBuilder();
                sb.Append(" YamlDotNet.dll under BepInEx/plugins:");
                for (int i = 0; i < found.Length; i++) {
                    // The containing folder is the mod package name, which is the useful part.
                    sb.Append($" [{VersionOf(found[i])}] {Path.GetFileName(Path.GetDirectoryName(found[i]))}");
                }
                return sb.ToString();
            } catch (Exception) {
                return "";
            }
        }

        // Reads the version out of the file's metadata rather than loading it -- loading a second
        // YamlDotNet into the domain is the last thing this should do while diagnosing which one is live.
        private static string VersionOf(string path) {
            try { return AssemblyName.GetAssemblyName(path).Version.ToString(); }
            catch (Exception) { return "unreadable"; }
        }
    }
}
