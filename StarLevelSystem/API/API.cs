using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StarLevelSystem.API
{
    /// <summary>
    /// Reflection-based API for accessing and modifying CreatureDetailCache entries in CompositeLazyCache
    /// </summary>
    [PublicAPI]
    public static class CreatureCacheAPI
    {
        private static readonly Type CompositeLazyCacheType;
        private static readonly FieldInfo SessionCacheField;
        private static readonly MethodInfo GetAndSetDetailCacheMethod;
        private static readonly MethodInfo UpdateCacheEntryMethod;
        private static readonly MethodInfo RemoveFromCacheMethod;

        static CreatureCacheAPI()
        {
            // Use reflection to get the CompositeLazyCache type
            CompositeLazyCacheType = Type.GetType("StarLevelSystem.Data.CompositeLazyCache, StarLevelSystem");
            if (CompositeLazyCacheType == null)
            {
                throw new InvalidOperationException("Could not find CompositeLazyCache type via reflection");
            }

            // Get the sessionCache field
            SessionCacheField = CompositeLazyCacheType.GetField("sessionCache", 
                BindingFlags.Public | BindingFlags.Static);
            if (SessionCacheField == null)
            {
                throw new InvalidOperationException("Could not find sessionCache field via reflection");
            }

            // Get the methods we need
            GetAndSetDetailCacheMethod = CompositeLazyCacheType.GetMethod("GetAndSetDetailCache", 
                BindingFlags.Public | BindingFlags.Static);
            UpdateCacheEntryMethod = CompositeLazyCacheType.GetMethod("UpdateCacheEntry", 
                BindingFlags.Public | BindingFlags.Static);
            RemoveFromCacheMethod = CompositeLazyCacheType.GetMethod("RemoveFromCache", 
                BindingFlags.Public | BindingFlags.Static);

            if (GetAndSetDetailCacheMethod == null || UpdateCacheEntryMethod == null || RemoveFromCacheMethod == null)
            {
                throw new InvalidOperationException("Could not find required methods on CompositeLazyCache via reflection");
            }
        }

        /// <summary>
        /// Gets all creature cache entries currently in the session cache
        /// </summary>
        /// <returns>Dictionary of creature IDs to cache entries</returns>
        public static IDictionary<uint, CreatureCacheEntry> GetAllCacheEntries()
        {
            try
            {
                var sessionCache = SessionCacheField.GetValue(null);
                if (sessionCache == null)
                    return new Dictionary<uint, CreatureCacheEntry>();

                var result = new Dictionary<uint, CreatureCacheEntry>();
                
                // The sessionCache is Dictionary<uint, CreatureDetailCache>
                var dictionary = sessionCache as System.Collections.IDictionary;
                if (dictionary != null)
                {
                    foreach (System.Collections.DictionaryEntry entry in dictionary)
                    {
                        if (entry.Key is uint key && entry.Value != null)
                        {
                            result[key] = new CreatureCacheEntry(entry.Value);
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get all cache entries: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets a creature cache entry by its ID
        /// </summary>
        /// <param name="creatureId">The creature's ZDOID</param>
        /// <returns>The cache entry or null if not found</returns>
        public static CreatureCacheEntry GetCacheEntry(uint creatureId)
        {
            try
            {
                var sessionCache = SessionCacheField.GetValue(null);
                if (sessionCache == null)
                    return null;

                // Access the dictionary using reflection
                var dictionaryType = sessionCache.GetType();
                var containsKeyMethod = dictionaryType.GetMethod("ContainsKey");
                var getItemMethod = dictionaryType.GetProperty("Item");

                if (containsKeyMethod == null || getItemMethod == null)
                    return null;

                var containsKey = (bool)containsKeyMethod.Invoke(sessionCache, new object[] { creatureId });
                if (!containsKey)
                    return null;

                var cacheEntry = getItemMethod.GetValue(sessionCache, new object[] { creatureId });
                return cacheEntry != null ? new CreatureCacheEntry(cacheEntry) : null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get cache entry for creature {creatureId}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Updates a creature cache entry in the session cache
        /// </summary>
        /// <param name="creatureId">The creature's ZDOID</param>
        /// <param name="cacheEntry">The updated cache entry</param>
        public static void UpdateCacheEntry(uint creatureId, CreatureCacheEntry cacheEntry)
        {
            if (cacheEntry == null)
                throw new ArgumentNullException(nameof(cacheEntry));

            try
            {
                var sessionCache = SessionCacheField.GetValue(null);
                if (sessionCache == null)
                    throw new InvalidOperationException("Session cache is null");

                // Access the dictionary using reflection to update the entry
                var dictionaryType = sessionCache.GetType();
                var setItemMethod = dictionaryType.GetProperty("Item");

                if (setItemMethod == null)
                    throw new InvalidOperationException("Could not find Item property on session cache");

                setItemMethod.SetValue(sessionCache, cacheEntry.GetUnderlyingCache(), new object[] { creatureId });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update cache entry for creature {creatureId}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Removes a creature cache entry from the session cache
        /// </summary>
        /// <param name="creatureId">The creature's ZDOID</param>
        /// <returns>True if the entry was removed, false if it wasn't found</returns>
        public static bool RemoveCacheEntry(uint creatureId)
        {
            try
            {
                var sessionCache = SessionCacheField.GetValue(null);
                if (sessionCache == null)
                    return false;

                // Access the dictionary using reflection
                var dictionaryType = sessionCache.GetType();
                var removeMethod = dictionaryType.GetMethod("Remove", new[] { typeof(uint) });

                if (removeMethod == null)
                    return false;

                return (bool)removeMethod.Invoke(sessionCache, new object[] { creatureId });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove cache entry for creature {creatureId}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Checks if a creature cache entry exists in the session cache
        /// </summary>
        /// <param name="creatureId">The creature's ZDOID</param>
        /// <returns>True if the entry exists, false otherwise</returns>
        public static bool ContainsCacheEntry(uint creatureId)
        {
            try
            {
                var sessionCache = SessionCacheField.GetValue(null);
                if (sessionCache == null)
                    return false;

                // Access the dictionary using reflection
                var dictionaryType = sessionCache.GetType();
                var containsKeyMethod = dictionaryType.GetMethod("ContainsKey");

                if (containsKeyMethod == null)
                    return false;

                return (bool)containsKeyMethod.Invoke(sessionCache, new object[] { creatureId });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to check cache entry for creature {creatureId}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets the total number of entries in the session cache
        /// </summary>
        /// <returns>The number of cache entries</returns>
        public static int GetCacheSize()
        {
            try
            {
                var sessionCache = SessionCacheField.GetValue(null);
                if (sessionCache == null)
                    return 0;

                // Access the dictionary count using reflection
                var dictionaryType = sessionCache.GetType();
                var countProperty = dictionaryType.GetProperty("Count");

                if (countProperty == null)
                    return 0;

                return (int)countProperty.GetValue(sessionCache);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get cache size: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Clears all entries from the session cache
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                var sessionCache = SessionCacheField.GetValue(null);
                if (sessionCache == null)
                    return;

                // Access the dictionary clear method using reflection
                var dictionaryType = sessionCache.GetType();
                var clearMethod = dictionaryType.GetMethod("Clear");

                clearMethod?.Invoke(sessionCache, null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to clear cache: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets all creature IDs currently in the cache
        /// </summary>
        /// <returns>Collection of creature IDs</returns>
        public static IEnumerable<uint> GetCachedCreatureIds()
        {
            try
            {
                var sessionCache = SessionCacheField.GetValue(null);
                if (sessionCache == null)
                    return Enumerable.Empty<uint>();

                // Access the dictionary keys using reflection
                var dictionaryType = sessionCache.GetType();
                var keysProperty = dictionaryType.GetProperty("Keys");

                if (keysProperty == null)
                    return Enumerable.Empty<uint>();

                var keys = keysProperty.GetValue(sessionCache);
                return keys as IEnumerable<uint> ?? Enumerable.Empty<uint>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get cached creature IDs: {ex.Message}", ex);
            }
        }
    }
}
