using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.modules;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    internal static class CompositeLazyCache
    {
        public static Dictionary<uint, CreatureDetailCache> sessionCache = new Dictionary<uint, CreatureDetailCache>();

        public static readonly Vector2 center = new Vector2(0, 0);
        // Add entry on creature awake
        // Add check to delete creature from Znet that removes the creature from the cache

        public static void RemoveFromCache(Character character) {
            uint czoid = character.GetZDOID().ID;
            if (sessionCache.ContainsKey(czoid)) {
                sessionCache.Remove(czoid);
                //Logger.LogDebug($"Removed creature from cache {character.name}-{czoid}");
            }
        }

        public static CreatureDetailCache GetAndSetDetailCache(Character character, bool update = false, int leveloverride = 0) {
            if (character == null) { return null; }
            if (character.IsPlayer()) { return null; }
            //Logger.LogDebug("Checking CreatureDetailCache");
            uint czoid = character.GetZDOID().ID;
            if (sessionCache.ContainsKey(czoid) && update == false) {
                return sessionCache[czoid];
            }

            // Setup cache
            CreatureDetailCache characterCacheEntry = new CreatureDetailCache() { };

            // Get character based biome and creature configuration
            Logger.LogDebug($"Checking Creature {character.gameObject.name} biome settings");
            LevelSystem.SelectCreatureBiomeSettings(character.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);

            // Set biome | used to deletion check
            characterCacheEntry.Biome = biome;

            // Set if the creature spawn is disabled, and return the entry but do not cache it.
            Logger.LogDebug("Checking Creature biome Disable spawn");
            if (biome_settings != null && biome_settings.creatureSpawnsDisabled != null && biome_settings.creatureSpawnsDisabled.Contains(creature_name)) {
                characterCacheEntry.creatureDisabledInBiome = true;
                return characterCacheEntry;
            }

            // Set the creature
            characterCacheEntry.CreaturePrefab = PrefabManager.Instance.GetPrefab(creature_name);

            // Check for level or set it
            Logger.LogDebug("Setting creature level");
            characterCacheEntry.Level = LevelSystem.DetermineLevelSetAndRetrieve(character, creature_settings, biome_settings, leveloverride);
            Logger.LogDebug($"Creature level set {characterCacheEntry.Level}");
            if (characterCacheEntry.Level == 0) {return null; }

            // Set creature Colorization pallete
            Logger.LogDebug("Selecting creature colorization");
            characterCacheEntry.Colorization = Colorization.DetermineCharacterColorization(character, characterCacheEntry.Level);
            if (characterCacheEntry.Colorization == null) { return null; }

            // Set the creatures damage recieved modifiers
            Logger.LogDebug("Computing damage recieved modifiers");
            if (biome_settings != null && biome_settings.DamageRecievedModifiers != null) {
                foreach (var entry in biome_settings.DamageRecievedModifiers) {
                    if (characterCacheEntry.DamageRecievedModifiers.ContainsKey(entry.Key)) {
                        characterCacheEntry.DamageRecievedModifiers[entry.Key] = entry.Value;
                    } else {
                        characterCacheEntry.DamageRecievedModifiers.Add(entry.Key, entry.Value);
                    }
                }
            }
            if (creature_settings != null && creature_settings.DamageRecievedModifiers != null) {
                foreach (var entry in creature_settings.DamageRecievedModifiers) {
                    if (characterCacheEntry.DamageRecievedModifiers.ContainsKey(entry.Key)) {
                        characterCacheEntry.DamageRecievedModifiers[entry.Key] = entry.Value;
                    } else {
                        characterCacheEntry.DamageRecievedModifiers.Add(entry.Key, entry.Value);
                    }
                }
            }

            // Set creature base settings
            Logger.LogDebug("Computing base creature modifiers");
            if (biome_settings != null && biome_settings.CreatureBaseValueModifiers != null) {
                foreach (var entry in biome_settings.CreatureBaseValueModifiers) {
                    if (characterCacheEntry.CreatureBaseValueModifiers.ContainsKey(entry.Key)) {
                        characterCacheEntry.CreatureBaseValueModifiers[entry.Key] = entry.Value;
                    } else {
                        characterCacheEntry.CreatureBaseValueModifiers.Add(entry.Key, entry.Value);
                    }
                }
            }
            if (creature_settings != null && creature_settings.CreatureBaseValueModifiers != null) {
                foreach (var entry in creature_settings.CreatureBaseValueModifiers) {
                    if (characterCacheEntry.CreatureBaseValueModifiers.ContainsKey(entry.Key)) {
                        characterCacheEntry.CreatureBaseValueModifiers[entry.Key] = entry.Value;
                    } else {
                        characterCacheEntry.CreatureBaseValueModifiers.Add(entry.Key, entry.Value);
                    }
                }
            }

            // Set creature per level settings
            Logger.LogDebug("Computing perlevel creature modifiers");
            if (biome_settings != null && biome_settings.CreaturePerLevelValueModifiers != null) {
                foreach (var entry in biome_settings.CreaturePerLevelValueModifiers) {
                    if (characterCacheEntry.CreaturePerLevelValueModifiers.ContainsKey(entry.Key)) {
                        characterCacheEntry.CreaturePerLevelValueModifiers[entry.Key] = entry.Value;
                    } else {
                        characterCacheEntry.CreaturePerLevelValueModifiers.Add(entry.Key, entry.Value);
                    }
                }
            }
            if (creature_settings != null && creature_settings.CreaturePerLevelValueModifiers != null) {
                foreach (var entry in creature_settings.CreaturePerLevelValueModifiers) {
                    if (characterCacheEntry.CreaturePerLevelValueModifiers.ContainsKey(entry.Key)) {
                        characterCacheEntry.CreaturePerLevelValueModifiers[entry.Key] = entry.Value;
                    } else {
                        characterCacheEntry.CreaturePerLevelValueModifiers.Add(entry.Key, entry.Value);
                    }
                }
            }

            // Set or load creature modifiers
            if (!character.IsPlayer() && character != null) {
                if (character.IsBoss() && ValConfig.EnableBossModifiers.Value == true) {
                    int numBossMods = ValConfig.MaxMajorModifiersPerCreature.Value;
                    if (creature_settings != null && creature_settings.MaxBossModifiers > -1) { numBossMods = creature_settings.MaxBossModifiers; }
                    float chanceForBossMod = ValConfig.ChanceOfBossModifier.Value;
                    if (creature_settings != null && creature_settings.ChanceForBossModifier > -1f) { chanceForBossMod = creature_settings.ChanceForBossModifier; }
                    characterCacheEntry.Modifiers = CreatureModifiers.SetupBossModifiers(character, characterCacheEntry, numBossMods, chanceForBossMod);
                } else {
                    Logger.LogDebug("Setting up creature modifiers");
                    int majorMods = ValConfig.MaxMajorModifiersPerCreature.Value;
                    if (creature_settings != null && creature_settings.MaxMajorModifiers > -1) { majorMods = creature_settings.MaxMajorModifiers; }
                    int minorMods = ValConfig.MaxMinorModifiersPerCreature.Value;
                    if (creature_settings != null && creature_settings.MaxMinorModifiers > -1) { minorMods = creature_settings.MaxMinorModifiers; }
                    float chanceMajorMod = ValConfig.ChanceMajorModifier.Value;
                    if (creature_settings != null && creature_settings.ChanceForMajorModifier > -1f) { chanceMajorMod = creature_settings.ChanceForMajorModifier; }
                    float chanceMinorMod = ValConfig.ChanceMinorModifier.Value;
                    if (creature_settings != null && creature_settings.ChanceForMinorModifier > -1f) { chanceMinorMod = creature_settings.ChanceForMinorModifier; }
                    characterCacheEntry.Modifiers = CreatureModifiers.SetupModifiers(character, characterCacheEntry, majorMods, minorMods, chanceMajorMod, chanceMinorMod);
                }
            }

            // Add it to the cache, and return it
            if (character == null) {return null; }
            if (!sessionCache.ContainsKey(character.GetZDOID().ID)) {
                Logger.LogDebug("Adding creature to cache");
                sessionCache.Add(character.GetZDOID().ID, characterCacheEntry);
            } else if (update == true) {
                Logger.LogDebug($"Adding Updating creature in cache {creature_name}-{characterCacheEntry.Level}");
                sessionCache[character.GetZDOID().ID] = characterCacheEntry;
            }
            return characterCacheEntry;
        }

    }
}
