using StarLevelSystem.modules;
using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterBiomeCommands()
        {
            _ = new SLSCommand("sls-biomes",
                "Lists every biome Star Level System knows, with the name config files use and the biome a custom one takes its settings from. 'here' also reports the biome at your position.",
                Biomes, CommandArea.Meta,
                options: input => new List<string>() { "here" },
                isCheat: false);
        }

        private static void Biomes(SLSCommandArgs args)
        {
            if (args.Has("here"))
            {
                if (args.HasCenter == false)
                {
                    args.Output.Error("No position to report on.");
                    return;
                }
                Vector3 pos = args.Center;
                args.Output.Info($"At x {pos.x:0} z {pos.z:0}: the game reports {BiomeRegistry.IdentifierOf(BiomeLookup.AtPosition(pos))}, the world generated {BiomeRegistry.IdentifierOf(BiomeLookup.World(pos))}.");
            }

            foreach (string line in BiomeRegistry.DescribeAll())
            {
                args.Output.Info(line);
            }
        }
    }
}
