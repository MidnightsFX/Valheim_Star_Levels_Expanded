using StarLevelSystem.Data;
using StarLevelSystem.modules.NemesisSystem;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterNemesisCommands()
        {
            // Remote spawning is driven by a manager that only exists server-side, so this routes through
            // the relay rather than doing anything locally.
            _ = new SLSCommand("sls-nemesis-spawn",
                "Format: [optional: biome] Force-scouts and places one remote Nemesis boss. Admins can run it from a connected client.",
                NemesisSpawn, CommandArea.Nemesis, TerminalArgs.Biomes,
                serverAuthoritative: true, requiresAdmin: true,
                aliases: "SLS-spawn-nemesis-remote");

            // Server authoritative for the same reason: only the server holds every ZDO in the world.
            _ = new SLSCommand("sls-nemesis-cleanup",
                "Format: [required: all|radius] Removes Nemesis spawns: remote bosses still waiting to appear, Nemesis bosses, their minions " +
                "and other Nemesis creatures, and their map pins. 'all' clears the whole world; a radius clears around you. Tamed creatures " +
                "are left alone and nothing drops loot. Admins can run it from a connected client. eg: sls-nemesis-cleanup 300",
                NemesisCleanup, CommandArea.Nemesis, NemesisCleanupOptions,
                serverAuthoritative: true, requiresAdmin: true);

            _ = new SLSCommand("sls-nemesis-score",
                "Format: [required: value] Sets your local Nemesis score. eg: sls-nemesis-score 500",
                NemesisScore, CommandArea.Nemesis, NemesisScoreOptions,
                aliases: "SLS-SetNem-Score");
        }

        private static List<string> NemesisScoreOptions(string[] input)
        {
            if (input.Length > 2) { return new List<string>(); }
            DataObjects.NemesisScore scores = NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem;
            return new List<string>() {
                scores.MinScore.ToString("0"), scores.NeutralScore.ToString("0"), scores.MaxScore.ToString("0")
            };
        }

        private static List<string> NemesisCleanupOptions(string[] input)
        {
            if (input.Length > 2) { return new List<string>(); }
            List<string> options = new List<string>() { "all" };
            options.AddRange(TerminalArgs.RadiusPresets(input));
            return options;
        }

        // No default scope: this deletes things, so a missing or mistyped radius is refused rather than guessed at.
        private static void NemesisCleanup(SLSCommandArgs args)
        {
            string scope = args.Args.GetString(0, null);
            bool all = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
            float radius = 0f;
            if (all == false)
            {
                if (scope == null || float.TryParse(scope, out radius) == false || radius <= 0f)
                {
                    args.Output.Error("Give 'all' to clear the whole world, or a radius to clear around you. eg: sls-nemesis-cleanup 300");
                    return;
                }
                if (args.HasCenter == false)
                {
                    args.Output.Error("A radius needs a player position to centre on. Run it from a connected admin client, or use 'all'.");
                    return;
                }
            }
            if (ZNet.instance == null || ZNet.instance.IsServer() == false)
            {
                args.Output.Error("Nemesis cleanup only runs on the server.");
                return;
            }
            if (NemesisRemoteSpawnControl.IsCleanupRunning())
            {
                args.Output.Error("A Nemesis cleanup is already running.");
                return;
            }

            string where = all ? "in the world" : $"within {radius:0}m of you";
            TerminalOutput output = args.Output;
            output.Info($"Removing every Nemesis spawn {where}...");
            output.Flush();
            ZNet.instance.StartCoroutine(NemesisRemoteSpawnControl.CleanupNemesisSpawns(args.Center, all ? 0f : radius, report =>
            {
                if (report == null)
                {
                    output.Warning("The Nemesis cleanup stopped before it finished.");
                    output.Flush();
                    return;
                }
                if (report.Bosses + report.Creatures + report.Spawners + report.Pins == 0)
                {
                    output.Info($"Found no Nemesis spawns {where}.");
                }
                else
                {
                    output.Info($"Removed {report.Bosses} Nemesis bosses, {report.Creatures} other Nemesis creatures and {report.Spawners} remote bosses " +
                        $"that had not appeared yet, and cleared {report.Pins} map pins.");
                }
                if (report.TamedSkipped > 0)
                {
                    output.Detail($"Left {report.TamedSkipped} tamed Nemesis creatures alone.");
                }
                if (all && ValConfig.EnableNemesisRemoteSpawning.Value && NemesisSystemData.SLE_Nemesis_Settings?.RemoteSpawning?.Enabled == true)
                {
                    output.Detail("Remote spawning is still on, so new remote bosses will be placed on its next cycle.");
                }
                output.Flush();
            }));
        }

        private static void NemesisSpawn(SLSCommandArgs args)
        {
            Heightmap.Biome biome = args.Args.GetEnum(0, Heightmap.Biome.None);
            if (biome == Heightmap.Biome.None)
            {
                // No biome given: use the one the requesting player is standing in.
                biome = args.HasCenter ? Heightmap.FindBiome(args.Center) : Heightmap.Biome.Meadows;
            }
            if (NemesisRemoteSpawnControl.Manager == null)
            {
                args.Output.Error("Remote Nemesis spawning is unavailable on this server.");
                return;
            }
            args.Output.Info($"Force-spawning a remote Nemesis boss for biome {biome}...");
            NemesisRemoteSpawnControl.Manager.ForceSpawnForBiome(biome);
        }

        private static void NemesisScore(SLSCommandArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                args.Output.Error("This needs a local player.");
                return;
            }
            if (args.Length < 1)
            {
                args.Output.Error("A score value is required. eg: sls-nemesis-score 500");
                return;
            }
            DataObjects.NemesisScore scores = NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem;
            float score = Mathf.Clamp(args.Args.GetFloat(0, scores.NeutralScore), scores.MinScore, scores.MaxScore);
            NemesisScoreSystem.SetScore(Player.m_localPlayer, score);
            NemesisSystem.CachedPlayerScore = score;
            args.Output.Info($"Set local player Nemesis score to {score}.");
        }
    }
}
