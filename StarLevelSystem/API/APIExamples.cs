using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.API
{
    /// <summary>
    /// Example usage patterns for the CreatureDetailCache API
    /// </summary>
    [PublicAPI]
    public static class APIExamples
    {
        /// <summary>
        /// Example: Get all creatures with fire modifiers
        /// </summary>
        /// <returns>List of creature IDs that have fire modifiers</returns>
        public static List<uint> GetCreaturesWithFireModifier()
        {
            var fireCreatures = new List<uint>();
            
            var allEntries = CreatureCacheAPI.GetAllCacheEntries();
            foreach (var kvp in allEntries)
            {
                if (kvp.Value.Modifiers.ContainsKey(ModifierNames.Fire))
                {
                    fireCreatures.Add(kvp.Key);
                }
            }
            
            return fireCreatures;
        }

        /// <summary>
        /// Example: Boost a creature's health by 50%
        /// </summary>
        /// <param name="creatureId">The creature to boost</param>
        /// <returns>True if the creature was found and boosted</returns>
        public static bool BoostCreatureHealth(uint creatureId)
        {
            var cacheEntry = CreatureCacheAPI.GetCacheEntry(creatureId);
            if (cacheEntry == null)
                return false;

            // Modify the base health modifier
            var baseModifiers = new Dictionary<CreatureBaseAttribute, float>(cacheEntry.CreatureBaseValueModifiers);
            baseModifiers[CreatureBaseAttribute.BaseHealth] *= 1.5f;
            cacheEntry.CreatureBaseValueModifiers = baseModifiers;

            // Update the cache
            CreatureCacheAPI.UpdateCacheEntry(creatureId, cacheEntry);
            return true;
        }

        /// <summary>
        /// Example: Create a powerful boss creature
        /// </summary>
        /// <param name="creatureId">The creature ID to make into a boss</param>
        /// <returns>The created boss cache entry</returns>
        public static CreatureCacheEntry CreateBossCreature(uint creatureId)
        {
            var bossEntry = CacheEntryFactory.CreateWithLevel(10);
            
            // Add boss modifiers
            bossEntry.Modifiers = new Dictionary<ModifierNames, ModifierType>
            {
                { ModifierNames.Brutal, ModifierType.Boss },
                { ModifierNames.Fire, ModifierType.Major },
                { ModifierNames.ResistSlash, ModifierType.Major }
            };

            // Boost base attributes
            bossEntry.CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>
            {
                { CreatureBaseAttribute.BaseHealth, 3.0f },
                { CreatureBaseAttribute.BaseDamage, 2.0f },
                { CreatureBaseAttribute.Size, 1.5f },
                { CreatureBaseAttribute.Speed, 1.2f },
                { CreatureBaseAttribute.AttackSpeed, 1.3f }
            };

            // Add damage resistances
            bossEntry.DamageRecievedModifiers = new Dictionary<DamageType, float>
            {
                { DamageType.Blunt, 0.7f },
                { DamageType.Pierce, 0.8f },
                { DamageType.Slash, 0.6f },
                { DamageType.Fire, 0.5f },
                { DamageType.Frost, 1.2f },
                { DamageType.Lightning, 1.0f },
                { DamageType.Poison, 0.3f },
                { DamageType.Spirit, 0.9f }
            };

            // Add to cache
            CreatureCacheAPI.UpdateCacheEntry(creatureId, bossEntry);
            
            return bossEntry;
        }

        /// <summary>
        /// Example: Find all high-level creatures (level 5+)
        /// </summary>
        /// <returns>Dictionary of creature IDs to their levels</returns>
        public static Dictionary<uint, int> GetHighLevelCreatures()
        {
            var highLevelCreatures = new Dictionary<uint, int>();
            
            var allEntries = CreatureCacheAPI.GetAllCacheEntries();
            foreach (var kvp in allEntries)
            {
                if (kvp.Value.Level >= 5)
                {
                    highLevelCreatures[kvp.Key] = kvp.Value.Level;
                }
            }
            
            return highLevelCreatures;
        }

        /// <summary>
        /// Example: Apply damage resistance to all creatures in cache
        /// </summary>
        /// <param name="damageType">The damage type to add resistance to</param>
        /// <param name="resistanceMultiplier">The resistance multiplier (0.5 = 50% damage reduction)</param>
        /// <returns>Number of creatures modified</returns>
        public static int ApplyGlobalDamageResistance(DamageType damageType, float resistanceMultiplier)
        {
            int modifiedCount = 0;
            var creatureIds = CreatureCacheAPI.GetCachedCreatureIds().ToList();
            
            foreach (var creatureId in creatureIds)
            {
                var cacheEntry = CreatureCacheAPI.GetCacheEntry(creatureId);
                if (cacheEntry != null)
                {
                    var resistances = new Dictionary<DamageType, float>(cacheEntry.DamageRecievedModifiers);
                    resistances[damageType] = resistanceMultiplier;
                    cacheEntry.DamageRecievedModifiers = resistances;
                    
                    CreatureCacheAPI.UpdateCacheEntry(creatureId, cacheEntry);
                    modifiedCount++;
                }
            }
            
            return modifiedCount;
        }

        /// <summary>
        /// Example: Remove all disabled creatures from cache
        /// </summary>
        /// <returns>Number of creatures removed</returns>
        public static int CleanupDisabledCreatures()
        {
            int removedCount = 0;
            var creatureIds = CreatureCacheAPI.GetCachedCreatureIds().ToList();
            
            foreach (var creatureId in creatureIds)
            {
                var cacheEntry = CreatureCacheAPI.GetCacheEntry(creatureId);
                if (cacheEntry != null && cacheEntry.CreatureDisabledInBiome)
                {
                    if (CreatureCacheAPI.RemoveCacheEntry(creatureId))
                    {
                        removedCount++;
                    }
                }
            }
            
            return removedCount;
        }

        /// <summary>
        /// Example: Get cache statistics
        /// </summary>
        /// <returns>A summary of cache contents</returns>
        public static string GetCacheStatistics()
        {
            var totalCreatures = CreatureCacheAPI.GetCacheSize();
            var allEntries = CreatureCacheAPI.GetAllCacheEntries();
            
            var levelDistribution = new Dictionary<int, int>();
            var modifierCount = new Dictionary<ModifierNames, int>();
            int disabledCreatures = 0;
            
            foreach (var entry in allEntries.Values)
            {
                // Level distribution
                if (!levelDistribution.ContainsKey(entry.Level))
                    levelDistribution[entry.Level] = 0;
                levelDistribution[entry.Level]++;
                
                // Modifier count
                foreach (var modifier in entry.Modifiers.Keys)
                {
                    if (!modifierCount.ContainsKey(modifier))
                        modifierCount[modifier] = 0;
                    modifierCount[modifier]++;
                }
                
                // Disabled creatures
                if (entry.CreatureDisabledInBiome)
                    disabledCreatures++;
            }
            
            var stats = $"Cache Statistics:\n";
            stats += $"Total Creatures: {totalCreatures}\n";
            stats += $"Disabled Creatures: {disabledCreatures}\n";
            stats += $"Level Distribution: {string.Join(", ", levelDistribution.Select(kvp => $"L{kvp.Key}:{kvp.Value}"))}\n";
            stats += $"Top Modifiers: {string.Join(", ", modifierCount.OrderByDescending(kvp => kvp.Value).Take(5).Select(kvp => $"{kvp.Key}:{kvp.Value}"))}";
            
            return stats;
        }
    }
}