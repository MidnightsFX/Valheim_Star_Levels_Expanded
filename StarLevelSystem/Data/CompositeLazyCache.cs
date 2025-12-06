using StarLevelSystem.common;
using StarLevelSystem.modules;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    internal static class CompositeLazyCache
    {
        public static readonly Vector2 center = new Vector2(0, 0);
        // Add entry on creature awake
        // Add check to delete creature from Znet that removes the creature from the cache
        public static Dictionary<uint, CharacterCacheEntry> SessionCache = new Dictionary<uint, CharacterCacheEntry>();


        // Check for cached creature data
        public static CharacterCacheEntry GetCacheEntry(Character character)
        {
            CharacterCacheEntry characterCacheEntry = RetrieveStoredCreatureFromCache(character);
            return characterCacheEntry;
        }

        public static CharacterCacheEntry GetAndSetLocalCache(Character character, int leveloverride = 0, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null, bool spawnMultiplyCheck = true, bool updateCache = false) {

            // Check for cached creature data
            CharacterCacheEntry characterEntry = RetrieveStoredCreatureFromCache(character);
            if (characterEntry != null) {
                Logger.LogDebug($"{character.name} Creature Data Object Cache available.");
                return characterEntry;
            }

            ZDO creatureZDO = character.m_nview.GetZDO();

            characterEntry ??= new CharacterCacheEntry();

            characterEntry.ControlledCharacter = character;
            // Get character based biome and creature configuration
            //Logger.LogDebug($"Checking Creature {character.gameObject.name} biome settings");
            LevelSystem.SelectCreatureBiomeSettings(character.gameObject, out string creatureName, out DataObjects.CreatureSpecificSetting creatureSettings, out BiomeSpecificSetting biomeSettings, out Heightmap.Biome biome);

            // Set biome | used to deletion check
            characterEntry.Biome = biome;
            characterEntry.RefCreatureName = creatureName;
            characterEntry.BiomeSettings = biomeSettings;
            characterEntry.CreatureSettings = creatureSettings;
            // Biome settings say to delete this
            if (biomeSettings != null && biomeSettings.creatureSpawnsDisabled != null && biomeSettings.creatureSpawnsDisabled.Contains(creatureName)) {
                characterEntry.ShouldDelete = true;
            }
            // biome or creature spawn settings 
            if (biomeSettings != null) { characterEntry.SpawnRateModifier = biomeSettings.SpawnRateModifier; }
            if (creatureSettings != null) { characterEntry.SpawnRateModifier = creatureSettings.SpawnRateModifier; }

            //Logger.LogDebug("Checking Night settings.");
            // Check if night time
            if (EnvMan.IsNight()) {
                // Override spawn rate modifiers for night time
                //Logger.LogDebug($"Checking night settings for {creature_name} setup? {setupstatus}");
                if (biomeSettings != null && biomeSettings.NightSettings != null) {
                    //Logger.LogDebug("Checking biome settings.");
                    if (biomeSettings.NightSettings.SpawnRateModifier != 1f) {
                        biomeSettings.SpawnRateModifier = biomeSettings.NightSettings.SpawnRateModifier;
                    }
                    //Logger.LogDebug($"Biome has {biome_settings.NightSettings.creatureSpawnsDisabled.Count} disabled creatures: {string.Join(",", biome_settings.NightSettings.creatureSpawnsDisabled)}");
                    if (biomeSettings.NightSettings.creatureSpawnsDisabled != null && biomeSettings.NightSettings.creatureSpawnsDisabled.Contains(creatureName)) {
                        //Logger.LogDebug("Biome has spawn disabled.");
                        characterEntry.ShouldDelete = true;
                    }
                }

                if (creatureSettings != null && creatureSettings.NightSettings != null) {
                   // Logger.LogDebug("Checking creature settings.");
                    if (creatureSettings.NightSettings.SpawnRateModifier != 1f) {
                        creatureSettings.SpawnRateModifier = creatureSettings.NightSettings.SpawnRateModifier;
                    }
                    if (creatureSettings.NightSettings.creatureSpawnsDisabled == true) {
                        //Logger.LogDebug("Creature has spawn disabled.");
                        characterEntry.ShouldDelete = true;
                    }
                }
            }

            // Set modifier requirements
            characterEntry.ModifiersNotAllowed = notAllowedModifiers;
            characterEntry.ModifiersRequired = requiredModifiers;

            // Check for level or set it
            //Logger.LogDebug("Setting creature level");
            int level = LevelSystem.DetermineLevel(character, creatureZDO, creatureSettings, biomeSettings, leveloverride);
            characterEntry.Level = level;
            Logger.LogDebug($"Determined {creatureName} level {level}");

            // Set creature Colorization pallete
            //Logger.LogDebug("Selecting creature colorization");
            characterEntry.Colorization = Colorization.DetermineCharacterColorization(character, level);

            //Logger.LogDebug("Selecting creature Damage Recieved, Per Level and base values.");
            characterEntry.DamageRecievedModifiers = ModificationExtensionSystem.DetermineCreatureDamageRecievedModifiers(biomeSettings, creatureSettings);
            characterEntry.CreaturePerLevelValueModifiers = ModificationExtensionSystem.DetermineCharacterPerLevelStats(biomeSettings, creatureSettings);
            characterEntry.CreatureBaseValueModifiers = ModificationExtensionSystem.DetermineCreatureBaseStats(biomeSettings, creatureSettings);

            // skip setting cache if the creature is gone already
            if (character == null) { return null; }
            uint uid = character.GetZDOID().ID;
            if (SessionCache.ContainsKey(uid)) {
                if (updateCache) {
                    SessionCache[uid] = characterEntry;
                }
            } else {
                SessionCache.Add(uid, characterEntry);
            }

            // Return the entry to the caller
            return characterEntry;
        }

        // SAFE to re-run
        public static void StartZOwnerCreatureRoutines(CharacterCacheEntry characterEntry) {
            if (characterEntry == null) { return; }

            // Destroy character if its selected for deletion
            if (characterEntry.ShouldDelete && characterEntry.ControlledCharacter.m_tamed == false) {
                ZNetScene.instance.StartCoroutine(ModificationExtensionSystem.DestroyCoroutine(characterEntry.ControlledCharacter.gameObject));
                return;
            }

            // Set level ZDO, only if its not been set, and only if its not what the cache is expecting
            if (characterEntry.ControlledCharacter.GetLevel() <= 1 && characterEntry.Level != characterEntry.ControlledCharacter.GetLevel()) {
                characterEntry.ControlledCharacter.SetLevel(characterEntry.Level);
            }

            // Set or load creature modifiers
            //Logger.LogDebug("Selecting creature modifiers.");
            CreatureModifiersZNetProperty StoredMods = new CreatureModifiersZNetProperty(SLS_MODIFIERS, characterEntry.ControlledCharacter.m_nview, null);
            characterEntry.CreatureModifiers = StoredMods.Get();
            if (StoredMods == null) {
                characterEntry.CreatureModifiers = CreatureModifiers.SelectModifiersForCreature(characterEntry.ControlledCharacter, characterEntry.RefCreatureName, characterEntry.CreatureSettings, characterEntry.Biome, characterEntry.Level, characterEntry.ModifiersRequired, characterEntry.ModifiersNotAllowed);
            }

            // Determine creature name
            //Logger.LogDebug("Setting creature name.");
            characterEntry.CreatureNameLocalizable = CreatureModifiers.BuildCreatureLocalizableName(characterEntry.ControlledCharacter, characterEntry.CreatureModifiers);

            // Get/set check SLS_SPAWN_MULT - applies spawn if not already applied
            Spawnrate.CheckSetApplySpawnrate(characterEntry);
        }

        public static void PostZDOSetup(CharacterCacheEntry characterEntry)
        {
            // Run once modifier setup to modify stats on creatures
            CreatureModifiers.RunOnceModifierSetup(characterEntry.ControlledCharacter, characterEntry, characterEntry.CreatureModifiers);
        }

        public static CharacterCacheEntry RetrieveStoredCreatureFromCache(Character character)
        {
            if (character == null || character.m_nview == null || character.IsPlayer() || character.m_nview.GetZDO() == null) { return null; }
            uint cid = character.GetZDOID().ID;
            if (SessionCache.ContainsKey(cid)) { return SessionCache[cid]; }
            return null;
        }

        public static void UpdateCharacterCacheEntry(Character character, CharacterCacheEntry scd)
        {
            uint cid = character.GetZDOID().ID;
            if (SessionCache.ContainsKey(cid))
            {
                SessionCache[cid] = scd; 
            } else {
                SessionCache.Add(cid, scd);
            }
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
