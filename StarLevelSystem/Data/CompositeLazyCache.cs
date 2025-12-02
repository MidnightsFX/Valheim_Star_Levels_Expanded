using Jotunn.Managers;
using PlayFab.EconomyModels;
using StarLevelSystem.common;
using StarLevelSystem.modules;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;
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

        public static void UpdateCachedEntry(Character chara, CreatureDetailCache cdc) {
            uint czoid = chara.GetZDOID().ID;
            if (sessionCache.ContainsKey(czoid))
            {
                sessionCache[czoid] = cdc;
            }
        }

        public static CreatureDetailCache GetCacheOrZDOOnly(Character character)
        {
            if (character == null) { return null; }
            if (character.IsPlayer()) { return null; }
            //Logger.LogDebug("Checking CreatureDetailCache");
            uint czoid = character.GetZDOID().ID;
            // Already cached
            if (sessionCache.ContainsKey(czoid))
            {
                return sessionCache[czoid];
            }
            // Check for stored Z Data
            CreatureDetailCache characterCacheEntry = CacheFromZDO(character);
            if (characterCacheEntry != null && !sessionCache.ContainsKey(czoid)) {
                sessionCache.Add(czoid, characterCacheEntry);
            }
            return characterCacheEntry;
        }

        public static CreatureDetailCache GetAndSetDetailCache(Character character, int leveloverride = 0, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null, bool spawnMultiplyCheck = true) {
            if (character == null) { return null; }
            if (character.IsPlayer()) { return null; }
            //Logger.LogDebug("Checking CreatureDetailCache");
            ZDOID czoid = character.GetZDOID();
            if (czoid == ZDOID.None) { return null; }

            // Already cached
            if (sessionCache.ContainsKey(czoid.ID)) {
                return sessionCache[czoid.ID];
            }
            // Check for stored Z Data
            CreatureDetailCache characterCacheEntry = CacheFromZDO(character);
            if (characterCacheEntry != null) {
                sessionCache.Add(czoid.ID, characterCacheEntry);
                return characterCacheEntry;
            } else {
                characterCacheEntry = new CreatureDetailCache();
            }

                ZDO creatureZDO = character.m_nview?.GetZDO();
            if (creatureZDO == null) {
                //Logger.LogWarning("ZDO null, skipping.");
                return null;
            }

            // Get character based biome and creature configuration
            //Logger.LogDebug($"Checking Creature {character.gameObject.name} biome settings");
            LevelSystem.SelectCreatureBiomeSettings(character.gameObject, out string creatureName, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);

            // Set biome | used to deletion check
            characterCacheEntry.Biome = biome;

            bool selected_for_deletion = false;
            //Logger.LogDebug("Checking Night settings.");

            // Check if night time
            if (EnvMan.IsNight()) {
                // Override spawn rate modifiers for night time
                //Logger.LogDebug($"Checking night settings for {creature_name} setup? {setupstatus}");
                if (biome_settings != null && biome_settings.NightSettings != null) {
                    //Logger.LogDebug("Checking biome settings.");
                    if (biome_settings.NightSettings.SpawnRateModifier != 1f) {
                        biome_settings.SpawnRateModifier = biome_settings.NightSettings.SpawnRateModifier;
                    }
                    //Logger.LogDebug($"Biome has {biome_settings.NightSettings.creatureSpawnsDisabled.Count} disabled creatures: {string.Join(",", biome_settings.NightSettings.creatureSpawnsDisabled)}");
                    if (biome_settings.NightSettings.creatureSpawnsDisabled != null && biome_settings.NightSettings.creatureSpawnsDisabled.Contains(creatureName)) {
                        //Logger.LogDebug("Biome has spawn disabled.");
                        selected_for_deletion = true;
                    }
                }

                if (creature_settings != null && creature_settings.NightSettings != null) {
                   // Logger.LogDebug("Checking creature settings.");
                    if (creature_settings.NightSettings.SpawnRateModifier != 1f) {
                        creature_settings.SpawnRateModifier = creature_settings.NightSettings.SpawnRateModifier;
                    }
                    if (creature_settings.NightSettings.creatureSpawnsDisabled == true) {
                        //Logger.LogDebug("Creature has spawn disabled.");
                        selected_for_deletion = true;
                    }
                }
            }

            // If the creature spawn is disabled delete it, but 
            //Logger.LogDebug("Checking Creature biome Disable spawn");
            if (selected_for_deletion == true || biome_settings != null && biome_settings.creatureSpawnsDisabled != null && biome_settings.creatureSpawnsDisabled.Contains(creatureName)) {
                if (character.m_tamed == false) {
                    ZNetScene.instance.StartCoroutine(ModificationExtensionSystem.DestroyCoroutine(character.gameObject));
                    return null;
                }
            }

            //Logger.LogDebug("Checking Spawnrate.");
            // Check creature spawn rate
            //Spawnrate.CheckSetApplySpawnrate(character, creatureZDO, creature_settings, biome_settings);
            if (spawnMultiplyCheck)
            {
                if (character.m_nview?.IsOwner() == true)
                {
                    ZNetScene.instance.StartCoroutine(Spawnrate.CheckSpawnRate(character, creatureZDO, creature_settings, biome_settings));
                }
            }

            // Set the creature
            characterCacheEntry.CreaturePrefab = PrefabManager.Instance.GetPrefab(creatureName);

            // Check for level or set it
            //Logger.LogDebug("Setting creature level");
            characterCacheEntry.Level = LevelSystem.DetermineLevel(character, creatureZDO, creature_settings, biome_settings, leveloverride);
            //Logger.LogDebug($"{creature_name} level set {characterCacheEntry.Level}");

            // Set creature Colorization pallete
            //Logger.LogDebug("Selecting creature colorization");
            characterCacheEntry.Colorization = Colorization.DetermineCharacterColorization(character, characterCacheEntry.Level);

            //Logger.LogDebug("Selecting creature Damage Recieved, Per Level and base values.");
            characterCacheEntry.DamageRecievedModifiers = ModificationExtensionSystem.DetermineCreatureDamageRecievedModifiers(biome_settings, creature_settings);
            characterCacheEntry.CreaturePerLevelValueModifiers = ModificationExtensionSystem.DetermineCharacterPerLevelStats(biome_settings, creature_settings);
            characterCacheEntry.CreatureBaseValueModifiers = ModificationExtensionSystem.DetermineCreatureBaseStats(biome_settings, creature_settings);

            // Set or load creature modifiers
            //Logger.LogDebug("Selecting creature modifiers.");
            characterCacheEntry.Modifiers = CreatureModifiers.SelectModifiersForCreature(character, creatureName, creature_settings, biome, characterCacheEntry.Level, requiredModifiers, notAllowedModifiers);
            // Run once modifier setup to modify stats on creatures
            CreatureModifiers.RunOnceModifierSetup(character, characterCacheEntry);

            // Determine creature name
            //Logger.LogDebug("Setting creature name.");
            characterCacheEntry.CreatureName = CreatureModifiers.BuildCreatureLocalizableName(character, characterCacheEntry.Modifiers);

            // Add it to the cache, and return it
            if (character == null) {return null; }
            if (!sessionCache.ContainsKey(character.GetZDOID().ID)) {
                //Logger.LogDebug("Adding creature to cache");
                sessionCache.Add(character.GetZDOID().ID, characterCacheEntry);
            }

            // Set the creatures modifiers and stored ZData to reflect
            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, character.m_nview, new StoredCreatureDetails());
            CreatureModifiersZNetProperty StoredMods = new CreatureModifiersZNetProperty(SLS_MODIFIERS, character.m_nview, new Dictionary<string, ModifierType>() { });
            StoredMods.Set(characterCacheEntry.Modifiers);
            cZDO.Set(ZStoredCreatureValuesFromCreatureDetailCache(characterCacheEntry));

            return characterCacheEntry;
        }

        public static bool CreatureDetailsCacheAvailable(Character character)
        {
            if (character == null || character.m_nview == null || character.IsPlayer()) { return false; }

            ZDO creatureZDO = character.m_nview.GetZDO();
            if (creatureZDO == null) {
                return false;
            }
            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, character.m_nview, null);
            StoredCreatureDetails storedcdetailZ = cZDO.Get();
            if (storedcdetailZ == null || storedcdetailZ.Name == null) {
                return false;
            }
            //Logger.LogDebug($"{character} has a cache available n:{storedcdetailZ.Name} c:{storedcdetailZ.Colorization} m:{storedcdetailZ.Modifiers}");
            return true;
        }

        public static CreatureDetailCache CacheFromZDO(Character character)
        {
            if (CreatureDetailsCacheAvailable(character) == false) { return null; }

            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, character.m_nview, new StoredCreatureDetails());
            StoredCreatureDetails storedcdetailZ = cZDO.Get();
            return DetailEntryFromStoredDetails(character, storedcdetailZ);
        }

        public static StoredCreatureDetails StoredFromZDO(Character character)
        {
            if (CreatureDetailsCacheAvailable(character) == false) { return null; }

            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, character.m_nview, new StoredCreatureDetails());
            return cZDO.Get();
        }

        public static void UpdateCreatureZDO(Character character, StoredCreatureDetails storedCreatureDetails)
        {
            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, character.m_nview, new StoredCreatureDetails());
            cZDO.Set(storedCreatureDetails);
            RemoveFromCache(character);
        }

        public static void UpdateCreatureZDOfromCDC(Character chara, CreatureDetailCache cdc_entry)
        {
            DictionaryDmgNetProperty DamageBonuses = new DictionaryDmgNetProperty(SLS_DAMAGE_BONUSES, chara.m_nview, new Dictionary<DamageType, float>());
            CreatureModifiersZNetProperty StoredMods = new CreatureModifiersZNetProperty(SLS_MODIFIERS, chara.m_nview, new Dictionary<string, ModifierType>() { });
            StoredMods.Set(cdc_entry.Modifiers);
            DamageBonuses.Set(cdc_entry.CreatureDamageBonus);
            UpdateCreatureZDO(chara, ZStoredCreatureValuesFromCreatureDetailCache(cdc_entry));
            RemoveFromCache(chara);
        }

        internal static StoredCreatureDetails ZStoredCreatureValuesFromCreatureDetailCache(CreatureDetailCache cdc)
        {
            return new StoredCreatureDetails()
            {
                Biome = cdc.Biome,
                Colorization = cdc.Colorization,
                CreatureBaseValueModifiers = cdc.CreatureBaseValueModifiers,
                CreatureDamageBonus = cdc.CreatureDamageBonus,
                CreaturePerLevelValueModifiers = cdc.CreaturePerLevelValueModifiers,
                DamageRecievedModifiers = cdc.DamageRecievedModifiers,
                Name = cdc.CreatureName
            };
        }

        internal static CreatureDetailCache DetailEntryFromStoredDetails(Character chara, StoredCreatureDetails storedData)
        {
            DictionaryDmgNetProperty DamageBonuses = new DictionaryDmgNetProperty(SLS_DAMAGE_BONUSES, chara.m_nview, new Dictionary<DamageType, float>());
            CreatureModifiersZNetProperty StoredMods = new CreatureModifiersZNetProperty(SLS_MODIFIERS, chara.m_nview, new Dictionary<string, ModifierType>() { });
            return new CreatureDetailCache()
            {
                Biome = storedData.Biome,
                Colorization = storedData.Colorization,
                CreatureBaseValueModifiers = storedData.CreatureBaseValueModifiers,
                CreaturePerLevelValueModifiers = storedData.CreaturePerLevelValueModifiers,
                DamageRecievedModifiers = storedData.DamageRecievedModifiers,
                CreatureDamageBonus = DamageBonuses.Get(),
                Level = chara.GetLevel(),
                CreatureName = storedData.Name,
                CreaturePrefab = PrefabManager.Instance.GetPrefab(Utils.GetPrefabName(chara.gameObject)),
                Modifiers = StoredMods.Get(),
                Size = chara.m_nview.GetZDO().GetFloat(SLS_SIZE, 0f),
                CreatureDamageModifier = chara.m_nview.GetZDO().GetFloat(SLS_DAMAGE_MODIFIER, 0f)
            };
        }

    }
}
