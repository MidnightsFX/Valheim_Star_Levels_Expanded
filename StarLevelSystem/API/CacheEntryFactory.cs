using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Reflection;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.API
{
    /// <summary>
    /// Factory for creating new CreatureCacheEntry instances
    /// </summary>
    [PublicAPI]
    public static class CacheEntryFactory
    {
        private static readonly Type CreatureDetailCacheType;
        private static readonly ConstructorInfo CreatureDetailCacheConstructor;

        static CacheEntryFactory()
        {
            // Use reflection to get the CreatureDetailCache type
            CreatureDetailCacheType = Type.GetType("StarLevelSystem.common.DataObjects+CreatureDetailCache, StarLevelSystem");
            if (CreatureDetailCacheType == null)
            {
                throw new InvalidOperationException("Could not find CreatureDetailCache type via reflection");
            }

            // Get the parameterless constructor
            CreatureDetailCacheConstructor = CreatureDetailCacheType.GetConstructor(Type.EmptyTypes);
            if (CreatureDetailCacheConstructor == null)
            {
                throw new InvalidOperationException("Could not find parameterless constructor for CreatureDetailCache");
            }
        }

        /// <summary>
        /// Creates a new cache entry with default values
        /// </summary>
        /// <returns>A new CreatureCacheEntry with default values</returns>
        public static CreatureCacheEntry CreateDefault()
        {
            try
            {
                var creatureDetailCache = CreatureDetailCacheConstructor.Invoke(null);
                return new CreatureCacheEntry(creatureDetailCache);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create default cache entry: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Creates a new cache entry with specified level
        /// </summary>
        /// <param name="level">The creature level</param>
        /// <returns>A new CreatureCacheEntry with the specified level</returns>
        public static CreatureCacheEntry CreateWithLevel(int level)
        {
            var entry = CreateDefault();
            entry.Level = level;
            return entry;
        }

        /// <summary>
        /// Creates a new cache entry with specified level and modifiers
        /// </summary>
        /// <param name="level">The creature level</param>
        /// <param name="modifiers">The creature modifiers</param>
        /// <returns>A new CreatureCacheEntry with the specified properties</returns>
        public static CreatureCacheEntry CreateWithModifiers(int level, IDictionary<ModifierNames, ModifierType> modifiers)
        {
            var entry = CreateDefault();
            entry.Level = level;
            if (modifiers != null)
            {
                entry.Modifiers = new Dictionary<ModifierNames, ModifierType>(modifiers);
            }
            return entry;
        }

        /// <summary>
        /// Creates a copy of an existing cache entry
        /// </summary>
        /// <param name="source">The source cache entry to copy</param>
        /// <returns>A new CreatureCacheEntry that is a copy of the source</returns>
        public static CreatureCacheEntry CreateCopy(CreatureCacheEntry source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var copy = CreateDefault();
            
            // Copy all properties
            copy.CreatureDisabledInBiome = source.CreatureDisabledInBiome;
            copy.CreatureCheckedSpawnMult = source.CreatureCheckedSpawnMult;
            copy.Level = source.Level;
            
            // Deep copy dictionaries
            copy.Modifiers = new Dictionary<ModifierNames, ModifierType>(source.Modifiers);
            copy.DamageRecievedModifiers = new Dictionary<DamageType, float>(source.DamageRecievedModifiers);
            copy.CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>(source.CreatureBaseValueModifiers);
            copy.CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>(source.CreaturePerLevelValueModifiers);
            copy.CreatureDamageBonus = new Dictionary<DamageType, float>(source.CreatureDamageBonus);
            
            // Deep copy modifier name dictionaries
            copy.ModifierPrefixNames = new Dictionary<ModifierNames, List<string>>();
            foreach (var kvp in source.ModifierPrefixNames)
            {
                copy.ModifierPrefixNames[kvp.Key] = new List<string>(kvp.Value);
            }
            
            copy.ModifierSuffixNames = new Dictionary<ModifierNames, List<string>>();
            foreach (var kvp in source.ModifierSuffixNames)
            {
                copy.ModifierSuffixNames[kvp.Key] = new List<string>(kvp.Value);
            }

            return copy;
        }
    }
}