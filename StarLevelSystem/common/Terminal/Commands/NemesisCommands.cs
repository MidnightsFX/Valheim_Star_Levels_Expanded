using StarLevelSystem.Data;
using StarLevelSystem.modules.NemesisSystem;
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
