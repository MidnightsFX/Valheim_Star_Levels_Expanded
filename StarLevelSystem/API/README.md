# CreatureDetailCache Reflection API Documentation

## Overview

This API provides reflection-based access to the `CreatureDetailCache` entries stored in the `CompositeLazyCache.sessionCache`. It uses strongly typed wrappers to provide safe and convenient access to cache data without requiring direct dependencies on internal types.

## Key Components

### 1. CreatureCacheEntry

A strongly typed wrapper around the internal `CreatureDetailCache` object that provides safe property access through reflection.

**Key Properties:**
- `Level`: Creature level
- `CreatureDisabledInBiome`: Whether creature is disabled in current biome  
- `Modifiers`: Dictionary of creature modifiers and their types
- `DamageRecievedModifiers`: Damage resistance values by damage type
- `CreatureBaseValueModifiers`: Base attribute multipliers
- `CreaturePerLevelValueModifiers`: Per-level attribute multipliers
- `CreatureDamageBonus`: Damage bonus values by damage type
- `ModifierPrefixNames`: Naming prefixes for modifiers
- `ModifierSuffixNames`: Naming suffixes for modifiers

### 2. CreatureCacheAPI

Main API class providing reflection-based access to the cache.

**Key Methods:**
- `GetAllCacheEntries()`: Get all cache entries as a dictionary
- `GetCacheEntry(uint creatureId)`: Get a specific cache entry
- `UpdateCacheEntry(uint creatureId, CreatureCacheEntry entry)`: Update an entry
- `RemoveCacheEntry(uint creatureId)`: Remove an entry
- `ContainsCacheEntry(uint creatureId)`: Check if entry exists
- `GetCacheSize()`: Get total number of entries
- `ClearCache()`: Remove all entries
- `GetCachedCreatureIds()`: Get all creature IDs in cache

### 3. CacheEntryFactory

Factory for creating new cache entries.

**Key Methods:**
- `CreateDefault()`: Create entry with default values
- `CreateWithLevel(int level)`: Create entry with specified level
- `CreateWithModifiers(int level, IDictionary<ModifierNames, ModifierType> modifiers)`: Create entry with level and modifiers
- `CreateCopy(CreatureCacheEntry source)`: Create a deep copy of an existing entry

### 4. APIValidator

Utility for validating API functionality.

**Key Methods:**
- `ValidateAPIAccess()`: Validate reflection access to required types
- `TestCacheEntryFunctionality()`: Test basic cache entry operations
- `PerformFullValidation()`: Comprehensive validation

### 5. APIExamples

Example usage patterns and common operations.

## Usage Examples

### Basic Cache Access

```csharp
// Get all cache entries
var allEntries = CreatureCacheAPI.GetAllCacheEntries();

// Get a specific creature's cache entry
uint creatureId = 12345;
var cacheEntry = CreatureCacheAPI.GetCacheEntry(creatureId);

// Check if a creature exists in cache
bool exists = CreatureCacheAPI.ContainsCacheEntry(creatureId);
```

### Modifying Cache Entries

```csharp
// Create a new cache entry
var newEntry = CacheEntryFactory.CreateWithLevel(5);
newEntry.Modifiers = new Dictionary<ModifierNames, ModifierType>
{
    { ModifierNames.Fire, ModifierType.Major }
};

// Update cache
CreatureCacheAPI.UpdateCacheEntry(creatureId, newEntry);
```

### Working with Modifiers

```csharp
var entry = CreatureCacheAPI.GetCacheEntry(creatureId);
if (entry != null)
{
    // Add a new modifier
    var modifiers = new Dictionary<ModifierNames, ModifierType>(entry.Modifiers);
    modifiers[ModifierNames.Fast] = ModifierType.Minor;
    entry.Modifiers = modifiers;
    
    // Update damage resistance
    var resistances = new Dictionary<DamageType, float>(entry.DamageRecievedModifiers);
    resistances[DamageType.Fire] = 0.5f; // 50% fire resistance
    entry.DamageRecievedModifiers = resistances;
    
    CreatureCacheAPI.UpdateCacheEntry(creatureId, entry);
}
```

### Bulk Operations

```csharp
// Find all creatures with specific level
var highLevelCreatures = CreatureCacheAPI.GetAllCacheEntries()
    .Where(kvp => kvp.Value.Level >= 8)
    .ToList();

// Apply global changes
var allIds = CreatureCacheAPI.GetCachedCreatureIds().ToList();
foreach (var id in allIds)
{
    var entry = CreatureCacheAPI.GetCacheEntry(id);
    if (entry != null)
    {
        // Boost all creatures' health by 10%
        var baseModifiers = new Dictionary<CreatureBaseAttribute, float>(entry.CreatureBaseValueModifiers);
        baseModifiers[CreatureBaseAttribute.BaseHealth] *= 1.1f;
        entry.CreatureBaseValueModifiers = baseModifiers;
        CreatureCacheAPI.UpdateCacheEntry(id, entry);
    }
}
```

## Error Handling

All API methods include comprehensive error handling:

- `InvalidOperationException`: Thrown when reflection fails or required types/members aren't found
- `ArgumentNullException`: Thrown when required parameters are null
- `ArgumentException`: Thrown when parameters have invalid values

## Thread Safety

The API accesses the underlying cache through reflection and does not provide additional thread safety beyond what the original cache provides. Users should implement appropriate synchronization if accessing from multiple threads.

## Performance Considerations

- Property access uses cached `PropertyInfo` objects for better performance
- Dictionary operations are performed through reflection, which has some overhead
- For high-frequency operations, consider caching entries locally where appropriate

## Limitations

- Requires the internal types to be available at runtime
- Some properties (like `Biome`, `CreaturePrefab`, `Colorization`) are not exposed in the wrapper to avoid Unity dependencies
- Changes made through this API bypass any validation logic in the original system