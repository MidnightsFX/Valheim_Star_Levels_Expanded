using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterCreatureCommands()
        {
            _ = new SLSCommand("sls-creature-killall",
                "Format: [optional: range] Removes every untamed non-player creature within range. eg: sls-creature-killall 500",
                CreatureKillAll, CommandArea.Creature, TerminalArgs.RadiusPresets,
                aliases: "SLS-killall");
        }

        private static void CreatureKillAll(SLSCommandArgs args)
        {
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to find nearby creatures.");
                return;
            }
            if (args.Length > 1)
            {
                args.Output.Warning("Range is the only supported argument. eg: sls-creature-killall 500");
            }
            float range = args.ReadRadius(0, 500f, 10000f);

            List<Character> nearbyCreatures = SLSExtensions.GetCharactersInRange(args.Center, range);
            int removed = 0;
            foreach (Character chara in nearbyCreatures)
            {
                if (chara.IsPlayer() || chara.IsTamed()) { continue; }

                CharacterDrop cdrop = chara.gameObject.GetComponent<CharacterDrop>();
                if (cdrop != null)
                {
                    GameObject.Destroy(cdrop);
                }
                if (chara != null)
                {
                    ZNet.Destroy(chara.gameObject);
                    removed++;
                }
            }
            args.Output.Info($"Removed {removed} creatures within {range}m.");
        }
    }
}
