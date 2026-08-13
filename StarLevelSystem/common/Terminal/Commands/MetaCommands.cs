using System;
using System.Collections.Generic;
using System.Linq;

namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterMetaCommands()
        {
            _ = new SLSCommand("sls-help",
                "Format: [optional: area] Lists the StarLevelSystem console commands, optionally just one area. eg: sls-help loc",
                Help, CommandArea.Meta, HelpOptions);
        }

        private static List<string> HelpOptions(string[] input)
        {
            if (input.Length > 2) { return new List<string>(); }
            return Enum.GetNames(typeof(CommandArea)).Where(name => name != CommandArea.Meta.ToString()).ToList();
        }

        private static void Help(SLSCommandArgs args)
        {
            CommandArea? only = null;
            string filter = args.Args.GetString(0, null);
            if (filter != null)
            {
                if (Enum.TryParse(filter, true, out CommandArea parsed)) { only = parsed; }
                else
                {
                    args.Output.Warning($"Unknown area '{filter}'. Areas: {string.Join(", ", HelpOptions(new string[] { "sls-help", "" }))}");
                    return;
                }
            }

            // Help is for the person typing it; echoing the whole listing into the BepInEx log is noise.
            args.Output.Info("StarLevelSystem commands:", log: false);

            foreach (IGrouping<CommandArea, SLSCommand> group in Registry.Values
                .Where(command => command.HideFromHelp == false)
                .Where(command => only == null || command.Area == only.Value)
                .GroupBy(command => command.Area)
                .OrderBy(group => group.Key.ToString()))
            {
                args.Output.Info($"  [{group.Key}]", log: false);
                foreach (SLSCommand command in group.OrderBy(command => command.Command))
                {
                    args.Output.Detail($"    {command.Command}{Tags(command)} - {command.Description}", log: false);
                    string aliases = AliasesOf(command);
                    if (aliases.Length > 0)
                    {
                        args.Output.Detail($"      also accepts: {aliases}", log: false);
                    }
                }
            }
        }

        private static string Tags(SLSCommand command)
        {
            List<string> tags = new List<string>();
            if (command.IsCheat) { tags.Add("cheat"); }
            if (command.RequiresAdmin) { tags.Add("admin"); }
            if (command.ServerAuthoritative) { tags.Add("server"); }
            return tags.Count == 0 ? string.Empty : $" ({string.Join(", ", tags)})";
        }

        // The old, pre-rename names are registered as hidden aliases so existing docs and macros keep
        // working; surface them here so the rename is discoverable rather than silent.
        private static string AliasesOf(SLSCommand command)
        {
            return string.Join(", ", Registry.Values
                .Where(other => other.HideFromHelp && other.Canonical == command.Command)
                .Select(other => other.Command)
                .OrderBy(name => name));
        }
    }
}
