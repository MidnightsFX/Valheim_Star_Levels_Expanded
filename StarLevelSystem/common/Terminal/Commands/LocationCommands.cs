using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.IO;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    // The Location Reset admin tools. Every one of these reads or mutates server-owned world state, so
    // they are all ServerAuthoritative: an admin client sends the request and the server runs it. That
    // is the only way these are reachable at all on a dedicated server, which has no Terminal of its own
    // and no local player to centre on.
    internal static partial class TerminalManager
    {
        private static void RegisterLocationCommands()
        {
            _ = new SLSCommand("sls-loc-status",
                "Reports Location Reset sweep throughput, how much of the world has been examined, the projected time for a full pass, and cumulative ZDO drift.",
                LocStatus, CommandArea.Loc,
                serverAuthoritative: true, requiresAdmin: true,
                aliases: "SLS-loc-reset-status");

            _ = new SLSCommand("sls-loc-reset",
                "Format: [optional: radius] Immediately resets the chunks around you, ignoring every reset timer, including the chunks currently loaded around you. Player structures are still protected. eg: sls-loc-reset 128",
                LocReset, CommandArea.Loc, TerminalArgs.RadiusPresets,
                isCheat: true, serverAuthoritative: true, requiresAdmin: true,
                aliases: "SLS-loc-reset-here");

            _ = new SLSCommand("sls-loc-reset-named",
                "Format: [required: location name] [optional: radius] [optional: safe|force] [optional: all] Resets only the named location near you, " +
                "even one no reset group covers. 'safe' waits for players to leave; 'force' (the default) works on the chunks loaded around you. " +
                "Player structures are protected either way. eg: sls-loc-reset-named Crypt2 128 force",
                LocResetNamed, CommandArea.Loc, LocResetNamedOptions,
                isCheat: true, serverAuthoritative: true, requiresAdmin: true);

            _ = new SLSCommand("sls-loc-info",
                "Format: [optional: location name] [optional: radius] Reports when the chunk you are standing in was last examined and when the " +
                "location in it was last reset, plus what is blocking it. With a name, reports on the nearest location of that name instead. " +
                "eg: sls-loc-info Crypt2 256",
                LocInfo, CommandArea.Loc, LocInfoOptions,
                serverAuthoritative: true, requiresAdmin: true);

            _ = new SLSCommand("sls-loc-api",
                "Lists the reset targets other mods have registered through the Star Level System API, with their source, schedule, and whether the " +
                "server's own config is overriding them.",
                LocApi, CommandArea.Loc,
                serverAuthoritative: true, requiresAdmin: true);

            _ = new SLSCommand("sls-loc-audit",
                "Format: [optional: radius] [optional: fix] Scans for duplicate world objects and surplus terrain compilers. Reports only unless 'fix' is passed. eg: sls-loc-audit 256 fix",
                LocAudit, CommandArea.Loc, LocAuditOptions,
                isCheat: true, serverAuthoritative: true, requiresAdmin: true,
                aliases: "SLS-loc-reset-audit");

            _ = new SLSCommand("sls-loc-dump",
                "Writes every location and vegetation entry this world knows about (including ones added by other mods) to SavedData/LocationResetCatalog.yaml, for use when configuring LocationResetSettings.yaml.",
                LocDump, CommandArea.Loc,
                isCheat: true, serverAuthoritative: true, requiresAdmin: true,
                aliases: "SLS-loc-reset-dump");

            _ = new SLSCommand("sls-loc-stamp",
                "Stamps every generated zone as reset right now and records its prefab census. Use this once after installing so an already-explored world starts its reset timers from today instead of resetting everything at once.",
                LocStamp, CommandArea.Loc,
                isCheat: true, serverAuthoritative: true, requiresAdmin: true,
                aliases: "SLS-loc-reset-stamp-all");
        }

        // Radius first, then the literal 'fix'.
        private static List<string> LocAuditOptions(string[] input)
        {
            return input.Length <= 2 ? TerminalArgs.RadiusPresets(input) : new List<string>() { "fix" };
        }

        // Location name, then radius, then the safety word, then 'all'.
        private static List<string> LocResetNamedOptions(string[] input)
        {
            if (input.Length <= 2) { return ConfiguredLocationNames(); }
            if (input.Length == 3) { return TerminalArgs.RadiusPresets(input); }
            if (input.Length == 4) { return new List<string>() { "force", "safe" }; }
            return new List<string>() { "all" };
        }

        private static List<string> LocInfoOptions(string[] input)
        {
            return input.Length <= 2 ? ConfiguredLocationNames() : TerminalArgs.RadiusPresets(input);
        }

        // Every location the reset config knows about. Not the whole world catalogue: a tab list of
        // several hundred names nobody has configured is worse than none, and sls-loc-dump already
        // exists for finding a name that is not here. A name absent from this list still works --
        // resolving an unconfigured target by name is the point of sls-loc-reset-named.
        private static List<string> ConfiguredLocationNames()
        {
            List<string> names = new List<string>();
            foreach (KeyValuePair<int, LocationResetData.ResolvedResetEntry> kvp in LocationResetData.LocationsByHash)
            {
                if (string.IsNullOrEmpty(kvp.Value.Name)) { continue; }
                names.Add(kvp.Value.Name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static void LocResetNamed(SLSCommandArgs args)
        {
            string name = args.Args.GetString(0);
            if (string.IsNullOrWhiteSpace(name))
            {
                args.Output.Error("Which location? eg: sls-loc-reset-named Crypt2 128");
                return;
            }
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to search around. Run it from a connected admin client.");
                return;
            }

            float radius = args.ReadRadius(1, 128f, 2048f);
            // Force by default here, unlike the API, which defaults to safe. An admin typing this is
            // standing in front of the thing on purpose; sls-loc-reset has always behaved this way,
            // and flipping it would surprise everyone who already uses it. The API's caller may be a
            // timer firing with nobody watching, which is the opposite situation.
            bool safe = args.Has("safe");
            bool all = args.Has("all");

            args.Output.Info($"Resetting '{name}' within {radius:0}m ({(safe ? "safe - will wait for players to leave" : "force")})" +
                $"{(all ? ", every match in range" : "")}. Player structures are protected either way.");

            bool accepted = modules.LocationReset.LocationResetControl.RequestReset(
                new modules.LocationReset.LocationResetControl.ResetRequest()
                {
                    Center = args.Center,
                    Radius = radius,
                    Safety = safe
                        ? modules.LocationReset.LocationResetControl.SafetySafe
                        : modules.LocationReset.LocationResetControl.SafetyForce,
                    LocationName = name.Trim(),
                    ResetAllMatches = all,
                    Source = $"sls-loc-reset-named '{name.Trim()}'",
                }, args.Output, null);

            if (accepted == false) { args.Output.Flush(); }
        }

        private static void LocInfo(SLSCommandArgs args)
        {
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to report on. Run it from a connected admin client.");
                return;
            }

            string name = args.Args.GetString(0);
            if (string.IsNullOrWhiteSpace(name))
            {
                args.Output.Info(FormatReport("Chunk",
                    modules.LocationReset.LocationResetQuery.GetChunkInfo(args.Center, false)));
                return;
            }

            float radius = args.ReadRadius(1, 256f, 4096f);
            Dictionary<string, object> info =
                modules.LocationReset.LocationResetQuery.GetLocationInfo(name.Trim(), args.Center, radius);
            if (info.TryGetValue("found", out object found) && found is bool ok && ok == false)
            {
                args.Output.Warning($"No location named '{name.Trim()}' within {radius:0}m.");
                return;
            }
            args.Output.Info(FormatReport($"Location '{name.Trim()}'", info));
        }

        private static void LocApi(SLSCommandArgs args)
        {
            if (LocationResetData.APIAdded.Count == 0)
            {
                args.Output.Info("No mods have registered reset targets through the Star Level System API.");
                return;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== API-registered reset targets ({LocationResetData.APIAdded.Count}) ===");
            modules.LocationReset.LocationResetControl.AppendAPIRegistrations(sb);
            args.Output.Info(sb.ToString());
        }

        // The query dictionaries are built for the API, where every value has to be a plain type.
        // Printing them straight keeps the console and the API answering with exactly the same
        // numbers rather than two formatters that can drift apart. Timestamps are rendered, because
        // "lastResetUnix: 1755..." is not something an admin should have to convert by hand.
        private static string FormatReport(string title, Dictionary<string, object> info)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== SLS Location Reset - {title} ===");
            if (info == null) { sb.AppendLine("no data"); return sb.ToString(); }

            foreach (KeyValuePair<string, object> kvp in info)
            {
                if (kvp.Value is List<Dictionary<string, object>> rows)
                {
                    sb.AppendLine($"{kvp.Key,-24}: {rows.Count} entries");
                    continue;
                }
                string value = kvp.Key.EndsWith("Unix", StringComparison.Ordinal) && kvp.Value is long stamp
                    ? DescribeStamp(stamp)
                    : Convert.ToString(kvp.Value);
                sb.AppendLine($"{kvp.Key,-24}: {value}");
            }
            return sb.ToString();
        }

        private static string DescribeStamp(long unixSeconds)
        {
            if (unixSeconds < 0) { return "unknown"; }
            if (unixSeconds == 0) { return "never"; }
            DateTimeOffset when = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
            return $"{when:yyyy-MM-dd HH:mm} ({unixSeconds})";
        }

        private static void LocStatus(SLSCommandArgs args)
        {
            args.Output.Info(modules.LocationReset.LocationResetControl.BuildStatusReport());
        }

        private static void LocReset(SLSCommandArgs args)
        {
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to centre on. Run it from a connected admin client, or use sls-loc-stamp on a headless server.");
                return;
            }
            float radius = args.ReadRadius(0, 64f, 512f);
            args.Output.Info($"Forcing a Location Reset within {radius}m. This ignores all timers but still respects player-structure protection.");
            modules.LocationReset.LocationResetControl.ForceResetAround(args.Center, radius, args.Output);
        }

        private static void LocAudit(SLSCommandArgs args)
        {
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to pick a centre point. Run it from a connected admin client.");
                return;
            }
            float radius = args.ReadRadius(0, 256f, 2048f);
            bool fix = args.Has("fix");
            modules.LocationReset.ZdoAudit.AuditReport report = modules.LocationReset.ZdoAudit.Run(args.Center, radius, fix);
            args.Output.Info(report.ToString());
            if (fix == false && (report.DuplicatesFound > 0 || report.ExtraTerrainCompilers > 0))
            {
                args.Output.Warning($"Re-run with 'fix' to remove them, e.g. sls-loc-audit {radius} fix");
            }
        }

        private static void LocDump(SLSCommandArgs args)
        {
            if (ZoneSystem.instance == null)
            {
                args.Output.Error("ZoneSystem is not ready yet.");
                return;
            }
            try
            {
                ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                LocationResetConfiguration catalog = LocationResetData.BuildPopulatedDefault();
                string header = @"#################################################
# Star Level System Expanded - Location Reset Catalog
#
# Generated by sls-loc-dump. This is a REFERENCE dump of everything this world can
# reset, not a live config file - editing it has no effect. Copy the entries you want into
# LocationResetSettings.yaml and set Enabled: true on them.
#################################################
";
                File.WriteAllText(ValConfig.locationResetCatalogPath,
                    header + System.Environment.NewLine + DataObjects.yamlSerializer.Serialize(catalog));
                args.Output.Info($"Wrote {catalog.Locations.Count} locations and {catalog.Vegetation.Count} vegetation entries to {ValConfig.locationResetCatalogPath}");
            }
            catch (Exception e)
            {
                args.Output.Error($"Failed to write the Location Reset catalog: {e.Message}");
            }
        }

        private static void LocStamp(SLSCommandArgs args)
        {
            int stamped = modules.LocationReset.LocationResetControl.StampAllGeneratedZones();
            args.Output.Info($"Stamped {stamped} generated zones. Reset timers now run from this moment.");
        }
    }
}
