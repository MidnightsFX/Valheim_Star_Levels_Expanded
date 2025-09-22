using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.API
{
    /// <summary>
    /// Strongly typed wrapper for CreatureDetailCache providing safe access to cache properties
    /// </summary>
    [PublicAPI]
    public class CreatureCacheEntry
    {
        private readonly object _creatureDetailCache;
        private static readonly Type CreatureDetailCacheType;
        private static readonly Dictionary<string, PropertyInfo> PropertyCache;

        static CreatureCacheEntry()
        {
            // Use reflection to get the CreatureDetailCache type from the common namespace
            CreatureDetailCacheType = Type.GetType("StarLevelSystem.common.DataObjects+CreatureDetailCache, StarLevelSystem");
            if (CreatureDetailCacheType == null)
            {
                throw new InvalidOperationException("Could not find CreatureDetailCache type via reflection");
            }

            // Cache property info for performance
            PropertyCache = CreatureDetailCacheType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(p => p.Name, p => p);
        }

        internal CreatureCacheEntry(object creatureDetailCache)
        {
            if (creatureDetailCache == null)
                throw new ArgumentNullException(nameof(creatureDetailCache));
            
            if (!CreatureDetailCacheType.IsInstanceOfType(creatureDetailCache))
                throw new ArgumentException($"Expected {CreatureDetailCacheType.Name}, got {creatureDetailCache.GetType().Name}");

            _creatureDetailCache = creatureDetailCache;
        }

        /// <summary>
        /// Gets whether the creature is disabled in its current biome
        /// </summary>
        public bool CreatureDisabledInBiome 
        { 
            get => GetProperty<bool>(nameof(CreatureDisabledInBiome));
            set => SetProperty(nameof(CreatureDisabledInBiome), value);
        }

        /// <summary>
        /// Gets whether the creature has been checked for spawn multiplier
        /// </summary>
        public bool CreatureCheckedSpawnMult 
        { 
            get => GetProperty<bool>(nameof(CreatureCheckedSpawnMult));
            set => SetProperty(nameof(CreatureCheckedSpawnMult), value);
        }

        /// <summary>
        /// Gets or sets the creature's level
        /// </summary>
        public int Level 
        { 
            get => GetProperty<int>(nameof(Level));
            set => SetProperty(nameof(Level), value);
        }

        /// <summary>
        /// Gets the creature's modifiers and their types
        /// </summary>
        public IDictionary<ModifierNames, ModifierType> Modifiers 
        { 
            get => GetProperty<Dictionary<ModifierNames, ModifierType>>(nameof(Modifiers)) ?? 
                   new Dictionary<ModifierNames, ModifierType>();
            set => SetProperty(nameof(Modifiers), value);
        }

        /// <summary>
        /// Gets the damage resistance modifiers for different damage types
        /// </summary>
        public IDictionary<DamageType, float> DamageRecievedModifiers 
        { 
            get => GetProperty<Dictionary<DamageType, float>>(nameof(DamageRecievedModifiers)) ?? 
                   new Dictionary<DamageType, float>();
            set => SetProperty(nameof(DamageRecievedModifiers), value);
        }

        /// <summary>
        /// Gets the base attribute modifiers for the creature
        /// </summary>
        public IDictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers 
        { 
            get => GetProperty<Dictionary<CreatureBaseAttribute, float>>(nameof(CreatureBaseValueModifiers)) ?? 
                   new Dictionary<CreatureBaseAttribute, float>();
            set => SetProperty(nameof(CreatureBaseValueModifiers), value);
        }

        /// <summary>
        /// Gets the per-level attribute modifiers for the creature
        /// </summary>
        public IDictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers 
        { 
            get => GetProperty<Dictionary<CreaturePerLevelAttribute, float>>(nameof(CreaturePerLevelValueModifiers)) ?? 
                   new Dictionary<CreaturePerLevelAttribute, float>();
            set => SetProperty(nameof(CreaturePerLevelValueModifiers), value);
        }

        /// <summary>
        /// Gets the damage bonus modifiers for different damage types
        /// </summary>
        public IDictionary<DamageType, float> CreatureDamageBonus 
        { 
            get => GetProperty<Dictionary<DamageType, float>>(nameof(CreatureDamageBonus)) ?? 
                   new Dictionary<DamageType, float>();
            set => SetProperty(nameof(CreatureDamageBonus), value);
        }

        /// <summary>
        /// Gets the modifier prefix names for creature naming
        /// </summary>
        public IDictionary<ModifierNames, List<string>> ModifierPrefixNames 
        { 
            get => GetProperty<Dictionary<ModifierNames, List<string>>>(nameof(ModifierPrefixNames)) ?? 
                   new Dictionary<ModifierNames, List<string>>();
            set => SetProperty(nameof(ModifierPrefixNames), value);
        }

        /// <summary>
        /// Gets the modifier suffix names for creature naming
        /// </summary>
        public IDictionary<ModifierNames, List<string>> ModifierSuffixNames 
        { 
            get => GetProperty<Dictionary<ModifierNames, List<string>>>(nameof(ModifierSuffixNames)) ?? 
                   new Dictionary<ModifierNames, List<string>>();
            set => SetProperty(nameof(ModifierSuffixNames), value);
        }

        private T GetProperty<T>(string propertyName)
        {
            if (!PropertyCache.TryGetValue(propertyName, out var property))
                throw new InvalidOperationException($"Property {propertyName} not found on {CreatureDetailCacheType.Name}");

            try
            {
                var value = property.GetValue(_creatureDetailCache);
                return value == null ? default(T) : (T)value;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get property {propertyName}: {ex.Message}", ex);
            }
        }

        private void SetProperty<T>(string propertyName, T value)
        {
            if (!PropertyCache.TryGetValue(propertyName, out var property))
                throw new InvalidOperationException($"Property {propertyName} not found on {CreatureDetailCacheType.Name}");

            if (!property.CanWrite)
                throw new InvalidOperationException($"Property {propertyName} is read-only");

            try
            {
                property.SetValue(_creatureDetailCache, value);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set property {propertyName}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets the underlying CreatureDetailCache object for advanced scenarios
        /// </summary>
        internal object GetUnderlyingCache()
        {
            return _creatureDetailCache;
        }
    }
}
