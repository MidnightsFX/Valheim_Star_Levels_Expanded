using StarLevelSystem.Data;
using StarLevelSystem.modules.Modifiers;
using System;
using System.Collections.Generic;
using System.Linq;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterModifierCommands()
        {
            _ = new SLSCommand("sls-mod-give",
                "Format: [boss/major/minor] [modifier-name] Gives nearby creatures the specified modifier.",
                ModGive, CommandArea.Modifier, ModGiveOptions,
                aliases: "SLS-give-modifier");
        }

        // The second argument's valid values depend on the first: each modifier type has its own set.
        // This is the case vanilla's argument-less options fetcher cannot express.
        private static List<string> ModGiveOptions(string[] input)
        {
            if (input.Length <= 2) { return TerminalArgs.Names<ModifierType>(); }
            ModifierType type = input.GetEnum(1, ModifierType.Major);
            return GetModifiersOfType(type).Keys.ToList();
        }

        private static void ModGive(SLSCommandArgs args)
        {
            if (args.Length < 2)
            {
                args.Output.Error("Two arguments required, modifier type and modifier name. Eg: sls-mod-give Major FireNova");
                return;
            }
            if (Enum.TryParse(args.Args[0], true, out ModifierType modtype) == false)
            {
                args.Output.Error($"Modifier type must be one of {string.Join(",", Enum.GetNames(typeof(ModifierType)))}");
                return;
            }
            if (Enum.TryParse(args.Args[1], true, out ModifierNames modname) == false)
            {
                args.Output.Error($"Modifier name must be one of {string.Join(",", Enum.GetNames(typeof(ModifierNames)))}");
                return;
            }
            CreatureModConfig cmfg = CreatureModifiersData.GetConfig(modname.ToString(), modtype);
            if (cmfg.PerlevelPower == float.NaN || cmfg.PerlevelPower == 0f && cmfg.BasePower == float.NaN || cmfg.BasePower == 0)
            {
                args.Output.Warning($"{modtype} did not contain a definition for {modname}. Types available in {modtype}: {string.Join(",", GetModifiersOfType(modtype).Keys)}");
            }
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to find nearby creatures.");
                return;
            }

            List<Character> nearbyCreatures = SLSExtensions.GetCharactersInRange(args.Center, 5f);
            int applied = 0;
            foreach (Character chara in nearbyCreatures)
            {
                if (chara.IsPlayer()) { continue; }
                CreatureModifiers.AddCreatureModifier(chara, modtype, modname.ToString());
                applied++;
            }
            args.Output.Info($"Added {modtype} {modname} to {applied} nearby creatures.");
        }
    }
}
