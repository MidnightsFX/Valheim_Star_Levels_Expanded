using Jotunn.Managers;
using PlayFab.EconomyModels;
using StarLevelSystem.common;
using StarLevelSystem.modules;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;
using static Heightmap;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    internal static class CompositeLazyCache
    {
        public static readonly Vector2 center = new Vector2(0, 0);
        // Add entry on creature awake
        // Add check to delete creature from Znet that removes the creature from the cache

        public static StoredCreatureDetails GetZDONoCreate(Character character)
        {
            if (character == null) { return null; }
            if (character.IsPlayer()) { return null; }
            // Check for stored Z Data
            StoredCreatureDetails characterCacheEntry = CacheFromZDO(character);
            return characterCacheEntry;
        }

        public static StoredCreatureDetails GetAndSetZDO(Character character, int leveloverride = 0, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null, bool spawnMultiplyCheck = true, bool setLevel = true) {
            if (character == null || character.IsPlayer() || character.m_nview == null) { return null; }

            ZDO creatureZDO = character.m_nview.GetZDO();
            if (creatureZDO == null) {
                return null;
            }
            // Check for stored Z Data
            //Logger.LogDebug("Checking for already setZDO");
            StoredCreatureDetails characterEntry = CacheFromZDO(character);
            if (characterEntry != null) { return characterEntry; }

            if (characterEntry == null) { characterEntry = new StoredCreatureDetails(); }

            // Get character based biome and creature configuration
            //Logger.LogDebug($"Checking Creature {character.gameObject.name} biome settings");
            LevelSystem.SelectCreatureBiomeSettings(character.gameObject, out string creatureName, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);

            // Set biome | used to deletion check
            characterEntry.Biome = biome;

            characterEntry.RefCreatureName = creatureName;

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
            Spawnrate.CheckSetApplySpawnrate(character, creatureZDO, creature_settings, biome_settings);
            if (spawnMultiplyCheck) {
                if (character.m_nview.IsOwner() == true) {
                    ZNetScene.instance.StartCoroutine(Spawnrate.CheckSpawnRate(character, creatureZDO, creature_settings, biome_settings));
                }
            }

            // Check for level or set it
            //Logger.LogDebug("Setting creature level");
            int level = LevelSystem.DetermineLevel(character, creatureZDO, creature_settings, biome_settings, leveloverride);
            if (setLevel) {
                Logger.LogDebug($"Setting {creatureName} level {level}");
                character.SetLevel(level);
            }
            //Logger.LogDebug($"{creature_name} level set {characterCacheEntry.Level}");

            // Set creature Colorization pallete
            //Logger.LogDebug("Selecting creature colorization");
            characterEntry.Colorization = Colorization.DetermineCharacterColorization(character, character.m_level);

            //Logger.LogDebug("Selecting creature Damage Recieved, Per Level and base values.");
            characterEntry.DamageRecievedModifiers = ModificationExtensionSystem.DetermineCreatureDamageRecievedModifiers(biome_settings, creature_settings);
            characterEntry.CreaturePerLevelValueModifiers = ModificationExtensionSystem.DetermineCharacterPerLevelStats(biome_settings, creature_settings);
            characterEntry.CreatureBaseValueModifiers = ModificationExtensionSystem.DetermineCreatureBaseStats(biome_settings, creature_settings);

            // Set or load creature modifiers
            //Logger.LogDebug("Selecting creature modifiers.");
            CreatureModifiersZNetProperty StoredMods = new CreatureModifiersZNetProperty(SLS_MODIFIERS, character.m_nview, new Dictionary<string, ModifierType>() { });
            Dictionary<string, ModifierType> selectedMods = CreatureModifiers.SelectModifiersForCreature(character, creatureName, creature_settings, biome, character.m_level, requiredModifiers, notAllowedModifiers);
            StoredMods.Set(selectedMods);
            // Run once modifier setup to modify stats on creatures
            CreatureModifiers.RunOnceModifierSetup(character, characterEntry, selectedMods);

            // Determine creature name
            //Logger.LogDebug("Setting creature name.");
            creatureZDO.Set(SLS_CHARNAME, CreatureModifiers.BuildCreatureLocalizableName(character, selectedMods));

            // Set the creatures modifiers and stored ZData to reflect
            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, character.m_nview, new StoredCreatureDetails());
            cZDO.Set(characterEntry);

            return characterEntry;
        }

        public static StoredCreatureDetails CacheFromZDO(Character character)
        {
            if (character == null || character.m_nview) { return null; }
            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, character.m_nview, null);
            StoredCreatureDetails scd = cZDO.Get();
            return scd;
        }

        public static void OverwriteZDOForCreature(Character character, StoredCreatureDetails scd)
        {
            if (character == null || character.m_nview == null) { return; }
            CreatureDetailsZNetProperty cZDO = new CreatureDetailsZNetProperty(SLS_CREATURE, character.m_nview, null);
            cZDO.Set(scd);
        }

        public static void RebuildCreatureName(Character character)
        {
            CreatureModifiersZNetProperty storedModifiers = new CreatureModifiersZNetProperty(SLS_MODIFIERS, character.m_nview, null);
            character.m_nview.GetZDO().Set(SLS_CHARNAME, CreatureModifiers.BuildCreatureLocalizableName(character, storedModifiers.Get()));
        }

        public static string GetCreatureName(Character character)
        {
            if (character == null || character.m_nview == null || character.m_nview.GetZDO() == null) return character.m_name;
            return character.m_nview.GetZDO().GetString(SLS_CHARNAME, character.m_name);
        }

        public static Dictionary<string, ModifierType> GetCreatureModifiers(Character character)
        {
            if (character == null || character.m_nview == null) { return null; }
            CreatureModifiersZNetProperty StoredMods = new CreatureModifiersZNetProperty(SLS_MODIFIERS, character.m_nview, null);
            return StoredMods.Get();
        }

        public static void SetCreatureModifiers(Character character, Dictionary<string, ModifierType> modifiers)
        {
            CreatureModifiersZNetProperty StoredMods = new CreatureModifiersZNetProperty(SLS_MODIFIERS, character.m_nview, null);
            StoredMods.Set(modifiers);
        }
    }
}
