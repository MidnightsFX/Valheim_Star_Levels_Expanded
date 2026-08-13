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
