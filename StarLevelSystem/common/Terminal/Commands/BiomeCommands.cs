using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

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

            _ = new SLSCommand("sls-boss-scaling",
                "Shows what boss kills do to each biome's star cap and level chances in this world. Name a boss key, or 'none', to see what it would be with that boss as the latest one defeated.",
                BossScaling, CommandArea.Meta,
                options: input => new List<string>() { "none" }.Concat(ConditionalScaleSystem.BossKeyOrder(LevelSystemData.SLE_Level_Settings)).ToList(),
                isCheat: false);
        }

        private static void BossScaling(SLSCommandArgs args)
        {
            CreatureLevelSettings settings = LevelSystemData.SLE_Level_Settings;
            if (settings == null) {
                args.Output.Error("Level settings are not loaded.");
                return;
            }
            if (settings.EnableConditionalCreatureLevelupChance == false) {
                args.Output.Info("Boss scaling is off: EnableConditionalCreatureLevelupChance is false in LevelSettings.yaml.");
                return;
            }

            List<string> order = ConditionalScaleSystem.BossKeyOrder(settings);
            bool projected = args.Length > 0;
            int latest;
            if (projected == false) {
                if (ZoneSystem.instance == null) {
                    args.Output.Error("No world loaded. Name a boss key, or 'none', to see a projection.");
                    return;
                }
                latest = ConditionalScaleSystem.LatestDefeated(order);
            } else if (string.Equals(args.Args[0], "none", StringComparison.OrdinalIgnoreCase)) {
                latest = -1;
            } else {
                latest = order.FindIndex(key => string.Equals(key, args.Args[0], StringComparison.OrdinalIgnoreCase));
                if (latest < 0) {
                    args.Output.Error($"'{args.Args[0]}' is not in ConditionalBossKeyOrder: {string.Join(", ", order)}.");
                    return;
                }
            }

            ConditionalIncreaseBonus bonus = settings.ConditionalIncreaseBonus;
            string bossText = latest < 0 ? "no boss" : order[latest];
            args.Output.Info($"{(projected ? "If the latest boss defeated were" : "Latest boss defeated:")} {bossText}. Delay {settings.ConditionalBiomeDelay}, at most {settings.ConditionalDifficultyIncreases} increase(s) per biome.");
            args.Output.Info($"Each increase: {DescribeIncrease(bonus)}.");

            int top = ValConfig.MaxLevel.Value + 1;
            foreach (KeyValuePair<Heightmap.Biome, string> own in LevelSystemData.OwnBossOfBiome) {
                string name = BiomeRegistry.IdentifierOf(own.Key);
                if (projected == false && ConditionalScaleSystem.GetConditionalLevelupChance(own.Key) != null) {
                    args.Output.Info($"{name}: fixed levels from ConditionalCreatureLevelupChance.");
                    continue;
                }
                int increases = ConditionalScaleSystem.IncreasesEarned(order, own.Value, latest, settings.ConditionalBiomeDelay, settings.ConditionalDifficultyIncreases);
                BiomeSpecificSetting biome = LevelSelection.BiomeSettingsFor(own.Key);
                int cap = biome != null && biome.BiomeMaxLevelOverride != 0 ? biome.BiomeMaxLevelOverride + 1 : top;
                int raised = ConditionalScaleSystem.RaisedCap(cap, top, increases, bonus, order.Count);
                float multiplier = ConditionalScaleSystem.ChanceMultiplier(bonus, increases);
                args.Output.Info($"{name}: {increases} increase(s), up to {raised - 1} stars ({cap - 1} without), chances x{multiplier.ToString("0.##", CultureInfo.InvariantCulture)}{DescribeLevelBonus(bonus, increases)}.");
            }
            if (projected && settings.ConditionalCreatureLevelupChance != null && settings.ConditionalCreatureLevelupChance.Count > 0) {
                args.Output.Detail("Fixed levels from ConditionalCreatureLevelupChance are not part of a projection.");
            }
        }

        private static string DescribeIncrease(ConditionalIncreaseBonus bonus)
        {
            if (bonus == null) { return "nothing (no ConditionalIncreaseBonus)"; }
            string stars = bonus.ExtraStars < 0 ? "star cap an equal step toward MaxLevel" : $"+{bonus.ExtraStars} star cap";
            string chance = $"chances +{(bonus.ChanceIncrease * 100f).ToString("0.##", CultureInfo.InvariantCulture)}%";
            string levels = bonus.LevelBonus != null && bonus.LevelBonus.Count > 0 ? $", level bonus {FormatLevelBonus(bonus.LevelBonus, 1)}" : "";
            return $"{stars}, {chance}{levels}";
        }

        private static string DescribeLevelBonus(ConditionalIncreaseBonus bonus, int increases)
        {
            if (increases <= 0 || bonus?.LevelBonus == null || bonus.LevelBonus.Count == 0) { return ""; }
            return $", level bonus {FormatLevelBonus(bonus.LevelBonus, increases)}";
        }

        private static string FormatLevelBonus(SortedDictionary<int, float> bonus, int times)
        {
            return "{ " + string.Join(", ", bonus.Select(entry => $"{entry.Key}: +{(entry.Value * times).ToString("0.##", CultureInfo.InvariantCulture)}%")) + " }";
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
