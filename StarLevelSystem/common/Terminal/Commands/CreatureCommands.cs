using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.UI;
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

            _ = new SLSCommand("sls-creature-setlevel",
                "Format: [required: level] [optional: search range] Sets the closest creature to the given level. Level 1 is no stars, 2 is one star, and so on. eg: sls-creature-setlevel 5",
                CreatureSetLevel, CommandArea.Creature, SetLevelOptions);
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

            Character closest = null;
            float closestDistance = float.MaxValue;
            foreach (Character chara in SLSExtensions.GetCharactersInRange(args.Center, range))
            {
                if (chara.IsPlayer() || chara.IsDead()) { continue; }
                if (chara.m_nview == null || chara.m_nview.GetZDO() == null) { continue; }
                float distance = Vector3.Distance(args.Center, chara.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = chara;
                }
            }
            if (closest == null)
            {
                args.Output.Error($"No creatures found within {range}m.");
                return;
            }

            // Clamp to this creature's own maximum, otherwise OverLevelCreaturesGetRerolledOnLoad
            // would reroll the level away on the next load.
            LevelSelection.SelectCreatureBiomeSettings(closest.gameObject, out string creatureName,
                out DataObjects.CreatureSpecificSetting creatureSettings, out DataObjects.BiomeSpecificSetting biomeSettings, out _);
            int maxLevel = LevelSelection.GetMaxCreatureLevel(closest, creatureSettings, biomeSettings);
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
