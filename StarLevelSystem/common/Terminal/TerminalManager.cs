using HarmonyLib;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StarLevelSystem.common
{
    // Registration and dispatch for every StarLevelSystem console command.
    //
    // Commands are built once from the plugin's Awake, not from a Terminal or Console hook, because a
    // dedicated server has neither: Console.Awake and Terminal.InitTerminal only ever run on a client.
    // The server still needs the command table so it can dispatch a relayed request, and constructing a
    // Terminal.ConsoleCommand only touches a static dictionary, so building them headless is harmless.
    internal static partial class TerminalManager
    {
        internal static readonly Dictionary<string, SLSCommand> Registry = new Dictionary<string, SLSCommand>();

        // Whichever terminal the admin last used to send a server-authoritative command, so the relayed
        // reply lands where they typed rather than always in the console.
        private static Terminal responseTerminal;

        private static bool initialized;

        internal static void Init()
        {
            if (initialized) { return; }
            initialized = true;

            RegisterMetaCommands();
            RegisterLocationCommands();
            RegisterZoneCommands();
            RegisterNemesisCommands();
            RegisterRaidCommands();
            RegisterModifierCommands();
            RegisterLootCommands();
            RegisterCreatureCommands();
            RegisterPlayerCommands();

            Logger.LogDebug($"Registered {Registry.Count} StarLevelSystem console commands.");
        }

        internal static void Register(SLSCommand command)
        {
            Registry[Key(command.Command)] = command;
        }

        private static string Key(string name) => (name ?? string.Empty).ToLowerInvariant();

        private static SLSCommand Lookup(string name)
        {
            return Registry.TryGetValue(Key(name), out SLSCommand command) ? command : null;
        }

        // Every command's vanilla action funnels through here.
        internal static void Execute(string name, Terminal.ConsoleEventArgs consoleArgs)
        {
            SLSCommand command = Lookup(name);
            if (command == null) { return; }

            string[] args = consoleArgs.Args.Skip(1).ToArray();
            TerminalOutput output = TerminalOutput.Local(consoleArgs.Context);

            if (command.ServerAuthoritative == false)
            {
                Invoke(command, args, output);
                return;
            }

            if (ZNet.instance == null)
            {
                output.Error($"You must be in a world to use {command.Canonical}.");
                return;
            }
            // An integrated host is the server, so it just runs the thing.
            if (ZNet.instance.IsServer())
            {
                Invoke(command, args, output);
                return;
            }
            // Client-side check is for a clear message only; the server re-checks the sender either way.
            if (SynchronizationManager.Instance.PlayerIsAdmin == false)
            {
                output.Error($"Only server admins can run {command.Canonical} from a client.");
                return;
            }
            ZNetPeer server = ZNet.instance.GetServerPeer();
            if (server == null)
            {
                output.Error($"No server connection, so {command.Canonical} cannot be sent.");
                return;
            }

            responseTerminal = consoleArgs.Context;
            output.Info($"Asked the server to run {command.Canonical}; its output follows.");
            ValConfig.ClientCommandRequestRPC.SendPackage(server.m_uid, BuildRequest(command.Canonical, args));
        }

        // Server side of the relay. The caller has already established that the sender is an admin.
        internal static void ExecuteFromNetwork(string name, string[] args, Vector3 center, bool hasCenter, TerminalOutput output)
        {
            SLSCommand command = Lookup(name);
            // Never dispatch a name the client picked that is not one of ours, and never let the relay
            // reach a command that was not built to run server-side.
            if (command == null || command.ServerAuthoritative == false)
            {
                output.Error($"'{name}' is not a server-runnable StarLevelSystem command.");
                output.Flush();
                return;
            }
            Invoke(command, args, center, hasCenter, output);
        }

        private static void Invoke(SLSCommand command, string[] args, TerminalOutput output)
        {
            Vector3 center = Vector3.zero;
            bool hasCenter = false;
            if (Player.m_localPlayer != null)
            {
                center = Player.m_localPlayer.transform.position;
                hasCenter = true;
            }
            Invoke(command, args, center, hasCenter, output);
        }

        private static void Invoke(SLSCommand command, string[] args, Vector3 center, bool hasCenter, TerminalOutput output)
        {
            try
            {
                command.Action(new SLSCommandArgs(args, center, hasCenter, output));
            }
            catch (Exception e)
            {
                // A command that throws must not take the console or the RPC handler with it.
                output.Error($"{command.Canonical} failed: {e.Message}");
                Logger.LogError($"{command.Canonical} threw: {e}");
            }
            finally
            {
                // Commands that finish synchronously are fully flushed here. A command that started a
                // coroutine keeps using the same sink and flushes again as it goes.
                output.Flush();
            }
        }

        private static ZPackage BuildRequest(string name, string[] args)
        {
            ZPackage package = new ZPackage();
            package.Write(name);
            package.Write(args.Length);
            foreach (string arg in args) { package.Write(arg); }

            bool hasCenter = Player.m_localPlayer != null;
            package.Write(hasCenter);
            package.Write(hasCenter ? Player.m_localPlayer.transform.position : Vector3.zero);
            return package;
        }

        // A relayed line arriving back on the requesting client.
        internal static void PrintResponse(OutputLevel level, string line)
        {
            TerminalOutput.LogLine(level, line);
            // ?? is not enough for a UnityEngine.Object: a destroyed terminal is a non-null reference
            // that only compares equal to null through Unity's own operator.
            Terminal target = responseTerminal != null ? responseTerminal : null;
            if (target == null && global::Console.instance != null) { target = global::Console.instance; }
            TerminalOutput.PrintTo(target, level, line);
        }

        // ---------------------------------------------------------------------------------------------
        // Tab completion
        //
        // Vanilla only ever completes the first argument: Terminal.Update hands tabCycle/updateSearch
        // strArray[1] and a single flat list from ConsoleCommand.GetTabOptions(). These two prefixes swap
        // in a list that depends on everything typed so far and point `word` at the token actually being
        // edited, so completion keeps working at the second argument and beyond.
        // ---------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.tabCycle))]
        private static class Terminal_tabCycle_Patch
        {
            private static void Prefix(Terminal __instance, ref string word, ref List<string> options, bool usePrefix)
            {
                ApplyContextOptions(__instance, usePrefix, ref word, ref options);
            }
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.updateSearch))]
        private static class Terminal_updateSearch_Patch
        {
            private static void Prefix(Terminal __instance, ref string word, ref List<string> options, bool usePrefix)
            {
                ApplyContextOptions(__instance, usePrefix, ref word, ref options);
            }
        }

        private static void ApplyContextOptions(Terminal terminal, bool usePrefix, ref string word, ref List<string> options)
        {
            // usePrefix means the command name itself is being completed; vanilla already handles that.
            if (usePrefix || terminal == null || terminal.m_input == null) { return; }

            string[] tokens = (terminal.m_input.text ?? string.Empty).Split(' ');
            if (tokens.Length < 2) { return; }

            // Chat prefixes commands with m_tabPrefix ('/'), the console does not.
            string name = terminal.m_tabPrefix == char.MinValue
                ? tokens[0]
                : (tokens[0].Length == 0 ? string.Empty : tokens[0].Substring(1));

            SLSCommand command = Lookup(name);
            if (command == null) { return; }

            List<string> resolved = command.GetTabOptions(tokens);
            if (resolved == null || resolved.Count == 0) { return; }

            options = resolved;
            word = tokens[tokens.Length - 1];
        }
    }
}
