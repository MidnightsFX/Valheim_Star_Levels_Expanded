using StarLevelSystem.Data;
using StarLevelSystem.modules;
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

            _ = new SLSCommand("sls-mod-remove",
                $"Format: [modifier-name or all] [optional: range] Removes the modifier from creatures within range (default {NearbyModifierRange}m). eg: sls-mod-remove Fire",
                ModRemove, CommandArea.Modifier, ModRemoveOptions);
        }

        private const float NearbyModifierRange = 5f;

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

            List<Character> nearbyCreatures = SLSExtensions.GetCharactersInRange(args.Center, NearbyModifierRange);
            int applied = 0;
            foreach (Character chara in nearbyCreatures)
            {
                if (chara.IsPlayer()) { continue; }
                // Through the API path so the change persists on creatures another player owns: it is replayed on
                // the owner rather than written here, where only the owner's write sticks.
                APIReciever.AddModifierToCreature(chara, modname.ToString(), (int)modtype);
                applied++;
            }
            args.Output.Info($"Added {modtype} {modname} to {applied} nearby creatures.");
        }

        // What nearby creatures carry first, then every other modifier, since a creature further off can be named by range.
        private static List<string> ModRemoveOptions(string[] input)
        {
            if (input.Length > 2) { return TerminalArgs.RadiusPresets(input); }
            List<string> options = new List<string>() { "all" };
            if (Player.m_localPlayer != null)
            {
                foreach (Character chara in SLSExtensions.GetCharactersInRange(Player.m_localPlayer.transform.position, NearbyModifierRange))
                {
                    if (chara.IsPlayer()) { continue; }
                    options.AddRange(CarriedModifiers(chara));
                }
            }
            options.AddRange(ModifierDefinitions.Keys.Where(name => name != CreatureModifiers.NoMods));
            return options.Distinct().ToList();
        }

        private static IEnumerable<string> CarriedModifiers(Character chara)
        {
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(chara);
            if (mods == null) { return Enumerable.Empty<string>(); }
            return mods.Keys.Where(name => name != CreatureModifiers.NoMods);
        }

        private static void ModRemove(SLSCommandArgs args)
        {
            if (args.Length < 1)
            {
                args.Output.Error("Name the modifier to remove, or all. eg: sls-mod-remove Fire");
                return;
            }
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to find nearby creatures.");
                return;
            }
            string requested = args.Args[0];
            bool all = string.Equals(requested, "all", StringComparison.OrdinalIgnoreCase);
            float range = args.ReadRadius(1, NearbyModifierRange, 10000f);

            int creatures = 0;
            int removed = 0;
            string removedName = requested;
            SortedSet<string> carried = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Character chara in SLSExtensions.GetCharactersInRange(args.Center, range))
            {
                if (chara.IsPlayer()) { continue; }
                List<string> has = CarriedModifiers(chara).ToList();
                carried.UnionWith(has);
                // Matched case-blind, then removed by the stored name, which is also how a modifier another mod added is named.
                List<string> matches = has.Where(name => all || string.Equals(name, requested, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0) { continue; }
                // Through the API, as sls-mod-give is, so a creature another player owns has it removed on that player.
                foreach (string modifier in matches) { APIReciever.RemoveModifierFromCreature(chara, modifier); }
                removedName = matches[0];
                removed += matches.Count;
                creatures++;
            }

            if (removed > 0)
            {
                string what = all ? $"{removed} modifiers" : removedName;
                args.Output.Info($"Removed {what} from {creatures} creatures within {range}m.");
            }
            else if (carried.Count == 0)
            {
                args.Output.Warning($"No creature within {range}m has any modifiers.");
            }
            else
            {
                args.Output.Warning($"No creature within {range}m has {requested}. They have: {string.Join(", ", carried)}.");
            }
        }
    }
}
