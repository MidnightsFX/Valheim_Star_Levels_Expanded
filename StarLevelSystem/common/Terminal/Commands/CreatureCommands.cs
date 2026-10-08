using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

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

            _ = new SLSCommand("sls-creature-setlevel",
                "Format: [required: level] [optional: search range] Sets the closest creature to the given level. Level 1 is no stars, 2 is one star, and so on. eg: sls-creature-setlevel 5",
                CreatureSetLevel, CommandArea.Creature, SetLevelOptions);

            _ = new SLSCommand("sls-creature-setstat",
                "Format: [Base/PerLevel/DamageTaken/DamageBonus] [stat] [value, or reset] [optional: search range] Sets one stat on the closest creature, and keeps it through restarts. Modifiers still add on top, and reset goes back to the configured value. Leave out the value to see the current ones. eg: sls-creature-setstat Base Size 2",
                CreatureSetStat, CommandArea.Creature, SetStatOptions);
        }

        private static List<string> SetLevelOptions(string[] input)
        {
            if (input.Length > 2) { return TerminalArgs.RadiusPresets(input); }
            return new List<string>() { "1", "2", "3", "5", "10", (ValConfig.MaxLevel.Value + 1).ToString() };
        }

        private static void CreatureSetLevel(SLSCommandArgs args)
        {
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to find nearby creatures.");
                return;
            }
            if (args.Length < 1 || int.TryParse(args.Args[0], out int level) == false || level < 1)
            {
                args.Output.Error("A level of 1 or higher is required. Level 1 is no stars, 2 is one star. eg: sls-creature-setlevel 5");
                return;
            }
            float range = args.ReadRadius(1, 64f, 10000f);

            Character closest = FindClosestCreature(args.Center, range, out float closestDistance);
            if (closest == null)
            {
                args.Output.Error($"No creatures found within {range}m.");
                return;
            }

            // Clamp to this creature's own maximum, otherwise OverLevelCreaturesGetRerolledOnLoad
            // would reroll the level away on the next load.
            LevelSelection.SelectCreatureBiomeSettings(closest.gameObject, out string creatureName,
                out DataObjects.CreatureSpecificSetting creatureSettings, out DataObjects.BiomeSpecificSetting biomeSettings, out Heightmap.Biome biome);
            int maxLevel = LevelSelection.GetMaxCreatureLevel(closest, creatureSettings, biomeSettings, biome);
            if (level > maxLevel)
            {
                args.Output.Warning($"{level} is above {creatureName}'s maximum level of {maxLevel}; using {maxLevel}.");
                level = maxLevel;
            }

            // Same sequence the patched vanilla spawn command uses to force a level, but the target may
            // be owned by another peer, so claim it first or the ZDO write would not replicate.
            if (closest.m_nview.IsOwner() == false) { closest.m_nview.ClaimOwnership(); }
            closest.m_nview.GetZDO().Set(ZDOVars.s_level, level);
            // GetLevel() reads m_level, and the owner-side setup only assigns it to creatures still sitting
            // at their spawn level. Without this write the hud stars, the name budget and the per-level
            // health all keep scaling off the old level even though the ZDO and the cache hold the new one.
            closest.m_level = level;
            DataObjects.CharacterCacheEntry entry = CompositeLazyCache.GetAndSetLocalCache(closest, level, updateCache: true);
            if (entry != null)
            {
                // Health has to go through the forced path: the normal one skips any creature whose max
                // health was already moved off its base, which is every creature SLS has already set up.
                HealthModifications.ForceApplyHealthModifications(closest, entry);
            }
            CreatureSetupControl.CreatureSetup(closest, leveloverride: level, multiply: false, delay: 0.01f);
            // Force rebuild of the HUD showing this characters level, otherwise it will not display a change.
            UIHudControl.InvalidateCacheEntry(closest);

            args.Output.Info($"Set {creatureName} ({closestDistance:0.#}m away) to level {level} ({level - 1} stars).");
        }

        private static Character FindClosestCreature(Vector3 center, float range, out float closestDistance)
        {
            Character closest = null;
            closestDistance = float.MaxValue;
            foreach (Character chara in SLSExtensions.GetCharactersInRange(center, range))
            {
                if (chara.IsPlayer() || chara.IsDead()) { continue; }
                if (chara.m_nview == null || chara.m_nview.GetZDO() == null) { continue; }
                float distance = Vector3.Distance(center, chara.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = chara;
                }
            }
            return closest;
        }

        // Group, then the stats of that group, then a value, then a search range.
        private static List<string> SetStatOptions(string[] input)
        {
            if (input.Length <= 2) { return TerminalArgs.Names<StatGroup>(); }
            StatGroup group = input.GetEnum(1, StatGroup.Base);
            if (input.Length == 3) { return CreatureStatOverrides.StatNames(group); }
            if (input.Length == 4) { return new List<string>() { "reset", "0.5", "1", "1.5", "2", "3", "5" }; }
            return TerminalArgs.RadiusPresets(input);
        }

        private static void CreatureSetStat(SLSCommandArgs args)
        {
            if (args.HasCenter == false)
            {
                args.Output.Error("This needs a player position to find nearby creatures.");
                return;
            }
            if (args.Length < 1 || Enum.TryParse(args.Args[0], true, out StatGroup group) == false || Enum.IsDefined(typeof(StatGroup), group) == false)
            {
                args.Output.Error($"The first argument is the stat group, one of {string.Join(", ", TerminalArgs.Names<StatGroup>())}. eg: sls-creature-setstat Base Size 2");
                return;
            }
            int stat = -1;
            if (args.Length >= 2 && CreatureStatOverrides.TryParseStat(group, args.Args[1], out stat) == false)
            {
                args.Output.Error($"{group} stats are {string.Join(", ", CreatureStatOverrides.StatNames(group))}.");
                return;
            }

            // Without a value this only shows the current ones.
            bool show = args.Length < 3;
            bool reset = show == false && string.Equals(args.Args[2], "reset", StringComparison.OrdinalIgnoreCase);
            float value = 0f;
            if (show == false && reset == false)
            {
                if (TryReadStatValue(args.Args[2], out value) == false)
                {
                    args.Output.Error($"'{args.Args[2]}' is not a number. Give a value such as 1.5, or reset to go back to the configured value.");
                    return;
                }
                string problem = CreatureStatOverrides.Validate(group, stat, value);
                if (problem != null)
                {
                    args.Output.Error(problem);
                    return;
                }
            }
            float range = args.ReadRadius(3, 64f, 10000f);

            Character closest = FindClosestCreature(args.Center, range, out float distance);
            if (closest == null)
            {
                args.Output.Error($"No creatures found within {range}m.");
                return;
            }
            CharacterCacheEntry entry = CompositeLazyCache.GetAndSetLocalCache(closest);
            if (entry == null || entry.Level <= 0)
            {
                args.Output.Error("The closest creature has not been set up yet; try again in a moment.");
                return;
            }
            string creatureName = Localization.instance.Localize(entry.CreatureNameLocalizable ?? closest.m_name);

            if (show)
            {
                HashSet<int> stored = CreatureStatOverrides.StoredStats(closest, group);
                List<string> values = new List<string>();
                foreach (string name in CreatureStatOverrides.StatNames(group))
                {
                    CreatureStatOverrides.TryParseStat(group, name, out int shown);
                    if (stat >= 0 && shown != stat) { continue; }
                    values.Add($"{name} {CreatureStatOverrides.CurrentValue(entry, group, shown):0.###}{(stored.Contains(shown) ? "*" : "")}");
                }
                args.Output.Info($"{creatureName} ({distance:0.#}m away) {group}: {string.Join(", ", values)}");
                args.Output.Detail("Values include its modifiers. * marks one set on this creature rather than configured.");
                return;
            }

            string statName = CreatureStatOverrides.StatName(group, stat);
            float before = CreatureStatOverrides.CurrentValue(entry, group, stat);
            if (reset && CreatureStatOverrides.StoredStats(closest, group).Contains(stat) == false)
            {
                args.Output.Info($"{creatureName}'s {statName} is already its configured value ({before:0.###}).");
                return;
            }
            CreatureStatOverrides.Send(closest, group, stat, reset, value);
            // Send handles it on this peer before returning, so the rebuilt entry is already in place.
            entry = CompositeLazyCache.GetCacheEntry(closest) ?? entry;
            float after = CreatureStatOverrides.CurrentValue(entry, group, stat);

            if (reset)
            {
                args.Output.Info($"Reset {creatureName}'s {statName} to its configured value ({distance:0.#}m away): now {after:0.###}, was {before:0.###}.");
            }
            else if (Mathf.Approximately(after, value))
            {
                args.Output.Info($"Set {creatureName}'s {statName} to {value:0.###} ({distance:0.#}m away), was {before:0.###}.");
            }
            else
            {
                args.Output.Info($"Set {creatureName}'s {statName} to {value:0.###} ({distance:0.#}m away): now {after:0.###} with its modifiers, was {before:0.###}.");
            }
            ReportStatEffect(args, closest, entry, group, stat);
        }

        // What the change did to the creature, where a plain number would not say.
        private static void ReportStatEffect(SLSCommandArgs args, Character chara, CharacterCacheEntry entry, StatGroup group, int stat)
        {
            bool health = (group == StatGroup.Base && stat == (int)CreatureBaseAttribute.BaseHealth)
                || (group == StatGroup.PerLevel && stat == (int)CreaturePerLevelAttribute.HealthPerLevel);
            bool size = (group == StatGroup.Base && stat == (int)CreatureBaseAttribute.Size)
                || (group == StatGroup.PerLevel && stat == (int)CreaturePerLevelAttribute.SizePerLevel);

            if (health && BossPhases.IsPinned(chara))
            {
                args.Output.Warning("This is a pinned boss phase, which always keeps its vanilla health.");
            }
            else if (health && chara.m_nview.IsOwner())
            {
                // Max health lives in the ZDO, so only the owner has the new value yet.
                args.Output.Detail($"Max health is now {chara.GetMaxHealth():0}.");
            }

            if (size && group == StatGroup.PerLevel && ValConfig.EnableCreatureScalingPerLevel.Value == false)
            {
                args.Output.Warning("Size per star has no effect while EnableCreatureScalingPerLevel is off.");
            }
            else if (size)
            {
                float cap = SizeModifications.MaxScaleMultiplier(entry);
                if (SizeModifications.DetermineScaleMultiplier(entry) >= cap)
                {
                    args.Output.Warning($"It is held at the {cap:0.##}x size cap (MaximumCreatureScale, or MaxSizeScale in its LevelSettings creature entry).");
                }
            }
        }

        // Invariant, with a comma read as the decimal point: float.TryParse alone reads "1.5" as 15 under a German locale.
        private static bool TryReadStatValue(string raw, out float value)
        {
            return float.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && float.IsNaN(value) == false && float.IsInfinity(value) == false;
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
