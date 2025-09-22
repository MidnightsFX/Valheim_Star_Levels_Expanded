using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.API
{
    /// <summary>
    /// Utility class for validating and testing the API functionality
    /// </summary>
    [PublicAPI]
    public static class APIValidator
    {
        /// <summary>
        /// Validates that the reflection-based API can access all necessary types and members
        /// </summary>
        /// <returns>True if all validations pass, false otherwise</returns>
        public static bool ValidateAPIAccess()
        {
            try
            {
                // Test that we can access the types via reflection
                var cacheType = Type.GetType("StarLevelSystem.Data.CompositeLazyCache, StarLevelSystem");
                if (cacheType == null)
                {
                    Console.WriteLine("Failed to find CompositeLazyCache type");
                    return false;
                }

                var detailCacheType = Type.GetType("StarLevelSystem.common.DataObjects+CreatureDetailCache, StarLevelSystem");
                if (detailCacheType == null)
                {
                    Console.WriteLine("Failed to find CreatureDetailCache type");
                    return false;
                }

                // Test that we can access the sessionCache field
                var sessionCacheField = cacheType.GetField("sessionCache", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (sessionCacheField == null)
                {
                    Console.WriteLine("Failed to find sessionCache field");
                    return false;
                }

                // Test that we can create a cache entry
                var constructor = detailCacheType.GetConstructor(Type.EmptyTypes);
                if (constructor == null)
                {
                    Console.WriteLine("Failed to find CreatureDetailCache constructor");
                    return false;
                }

                Console.WriteLine("All API access validations passed");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"API validation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tests the basic functionality of cache entry creation and property access
        /// </summary>
        /// <returns>True if all tests pass, false otherwise</returns>
        public static bool TestCacheEntryFunctionality()
        {
            try
            {
                // Test factory creation
                var entry = CacheEntryFactory.CreateDefault();
                if (entry == null)
                {
                    Console.WriteLine("Failed to create default cache entry");
                    return false;
                }

                // Test property access
                entry.Level = 5;
                if (entry.Level != 5)
                {
                    Console.WriteLine("Failed to set/get Level property");
                    return false;
                }

                entry.CreatureDisabledInBiome = true;
                if (!entry.CreatureDisabledInBiome)
                {
                    Console.WriteLine("Failed to set/get CreatureDisabledInBiome property");
                    return false;
                }

                // Test dictionary properties
                entry.Modifiers = new Dictionary<ModifierNames, ModifierType>
                {
                    { ModifierNames.Fire, ModifierType.Major },
                    { ModifierNames.Fast, ModifierType.Minor }
                };

                if (entry.Modifiers.Count != 2)
                {
                    Console.WriteLine("Failed to set/get Modifiers dictionary");
                    return false;
                }

                // Test copy functionality
                var copy = CacheEntryFactory.CreateCopy(entry);
                if (copy == null || copy.Level != 5 || !copy.CreatureDisabledInBiome)
                {
                    Console.WriteLine("Failed to create copy of cache entry");
                    return false;
                }

                Console.WriteLine("All cache entry functionality tests passed");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cache entry functionality test failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Performs a comprehensive validation of the API
        /// </summary>
        /// <returns>True if all validations pass, false otherwise</returns>
        public static bool PerformFullValidation()
        {
            Console.WriteLine("Starting API validation...");
            
            bool accessValid = ValidateAPIAccess();
            bool functionalityValid = TestCacheEntryFunctionality();
            
            bool allValid = accessValid && functionalityValid;
            
            Console.WriteLine($"Validation result: {(allValid ? "PASSED" : "FAILED")}");
            return allValid;
        }
    }
}