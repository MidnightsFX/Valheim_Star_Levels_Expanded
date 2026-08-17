using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StarLevelSystem.common
{
    // Which family a command belongs to. Only used to group the sls-help listing.
    internal enum CommandArea { Meta, Loc, Zone, Nemesis, Modifier, Loot, Creature, Player, Raid }

    // Severity of one line of command output. Deliberately separate from the text: colour is applied
    // where the line is displayed, so markup never reaches the BepInEx log, the chunk log, or the wire.
    internal enum OutputLevel : byte { Info = 0, Detail = 1, Warning = 2, Error = 3 }

    // Completions for the token currently being typed. `input` is the whole console line split on
    // spaces, so a provider can answer differently per argument position and based on what the earlier
    // arguments already say. Vanilla's ConsoleOptionsFetcher takes no arguments and so cannot.
    internal delegate List<string> OptionProvider(string[] input);

    internal delegate void CommandAction(SLSCommandArgs args);

    // One StarLevelSystem console command.
    //
    // Subclasses the vanilla Terminal.ConsoleCommand instead of using Jotunn's wrapper. Jotunn registers
    // from a Console.Awake postfix -- which never runs on a dedicated server -- and its
    // CreateVanillaCommand hardcodes the tab-options fetcher to a single flat list. Constructing this
    // type only writes into the static Terminal.commands dictionary, so it is safe to build headless
    // where no Terminal instance will ever exist.
    internal class SLSCommand : Terminal.ConsoleCommand
    {
        // The primary name, even on an alias instance. Used for messages and for the server-side lookup
        // so an alias and its canonical name resolve to the same behaviour.
        internal readonly string Canonical;
        internal readonly CommandArea Area;
        internal readonly CommandAction Action;
        internal readonly OptionProvider Options;
        internal readonly bool HideFromHelp;

        // The action mutates world state the server owns, so a connected client must ask the server to
        // run it rather than running it locally. See TerminalManager.Execute.
        internal readonly bool ServerAuthoritative;

        // Extra hint for sls-help. The real gate is SenderIsAdmin on the server side.
        internal readonly bool RequiresAdmin;

        internal SLSCommand(
            string command,
            string description,
            CommandAction action,
            CommandArea area,
            OptionProvider options = null,
            bool isCheat = false,
            bool serverAuthoritative = false,
            bool requiresAdmin = false,
            bool hideFromHelp = false,
            string canonical = null,
            params string[] aliases)
            : base(command, description,
                  (Terminal.ConsoleEvent)(args => TerminalManager.Execute(command, args)),
                  isCheat,
                  isNetwork: false,
                  onlyServer: false,
                  // isSecret also keeps the name out of the tab-completion list, which is exactly what a
                  // back-compat alias wants: it still runs when typed in full, but never suggests itself.
                  isSecret: hideFromHelp,
                  allowInDevBuild: false,
                  optionsFetcher: null)
        {
            Canonical = canonical ?? command;
            Area = area;
            Action = action;
            Options = options;
            HideFromHelp = hideFromHelp;
            ServerAuthoritative = serverAuthoritative;
            RequiresAdmin = requiresAdmin;

            // Vanilla asks for the option list before our tabCycle/updateSearch prefixes get a chance to
            // replace it, so the fetcher has to exist and has to be safe when there is no Console (or no
            // input yet). The prefixes are what actually make per-argument completion work.
            m_tabOptionsFetcher = () => GetTabOptions(CurrentInput());
            // Options depend on what is already typed, so a cached list is always one keystroke stale.
            m_alwaysRefreshTabOptions = true;

            TerminalManager.Register(this);

            foreach (string alias in aliases)
            {
                _ = new SLSCommand(alias, description, action, area, options, isCheat, serverAuthoritative,
                    requiresAdmin, hideFromHelp: true, canonical: Canonical);
            }
        }

        internal List<string> GetTabOptions(string[] input)
        {
            if (Options == null) { return new List<string>(); }
            try { return Options(input) ?? new List<string>(); }
            catch (Exception e)
            {
                Logger.LogDebug($"Tab options for {Command} failed: {e.Message}");
                return new List<string>();
            }
        }

        // Best-effort read of the line being typed, for the vanilla fetcher path only.
        private static string[] CurrentInput()
        {
            Terminal console = global::Console.instance;
            string text = console?.m_input == null ? string.Empty : console.m_input.text;
            return (text ?? string.Empty).Split(' ');
        }
    }

    // What a command handler receives: the arguments with the command name already stripped, the point
    // the command should act around, and where its output goes.
    internal class SLSCommandArgs
    {
        internal readonly string[] Args;
        internal readonly TerminalOutput Output;

        // Position of the player who asked. On a locally-run command that is Player.m_localPlayer; on a
        // relayed one it is the requesting client's position, which is the only centre a dedicated
        // server can use since it has no local player of its own.
        internal readonly Vector3 Center;
        internal readonly bool HasCenter;

        internal SLSCommandArgs(string[] args, Vector3 center, bool hasCenter, TerminalOutput output)
        {
            Args = args ?? new string[0];
            Center = center;
            HasCenter = hasCenter;
            Output = output;
        }

        internal int Length => Args.Length;

        internal bool Has(string flag) => Args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }
}
