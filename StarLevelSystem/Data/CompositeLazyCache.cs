using StarLevelSystem.common;
using StarLevelSystem.modules;
using System;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    internal static class CompositeLazyCache
    {
        public static readonly Vector2 Center = new Vector2(0, 0);


        // Add check to delete creature from Znet that removes the creature from the cache
        private static Dictionary<uint, CharacterCacheEntry> sessionCache = new Dictionary<uint, CharacterCacheEntry>();

        // Session tree cache, to avoid recalculating tree levels multiple times
        private static Dictionary<uint, int> treeSessionCache = new Dictionary<uint, int>();

        public static int GetOrAddCachedTreeEntry(ZNetView zgo) {
            if ( zgo == null || zgo.IsValid() == false || zgo.GetZDO() == null) { return 1; }
            uint cid = zgo.GetZDO().m_uid.ID;
            if (treeSessionCache.ContainsKey(cid)) { return treeSessionCache[cid]; }
            int level = LevelSystem.DeterministicDetermineTreeLevel(zgo.gameObject);
            treeSessionCache.Add(cid, level);
            return level;
        }

        public static void RemoveTreeCacheEntry(uint id) {
            if (treeSessionCache.ContainsKey(id)) {
                treeSessionCache.Remove(id);
            }
        }

        public static CharacterCacheEntry GetCacheEntry(uint cid)
        {
            if (sessionCache.ContainsKey(cid))
            {
                return sessionCache[cid];
            }
            return null;
        }

        // Check for cached creature data
        public static CharacterCacheEntry GetCacheEntry(Character character)
        {
            CharacterCacheEntry characterCacheEntry = RetrieveStoredCreatureFromCache(character);
            return characterCacheEntry;
        }

        public static void ClearCachedCreature(Character character)
        {
            if (character == null || character.m_nview == null || character.IsPlayer() || character.m_nview.GetZDO() == null) { return; }
            uint cid = character.GetZDOID().ID;
            sessionCache.Remove(cid);
        }

        public static CharacterCacheEntry GetAndSetLocalCache(Character character, int leveloverride = 0, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null, bool updateCache = false) {

            // Check for cached creature data
            CharacterCacheEntry cacheEntry = RetrieveStoredCreatureFromCache(character);
            if (cacheEntry != null && updateCache == false) {
                return cacheEntry;
            }

            ZDO creatureZDO = character.m_nview.GetZDO();

            CharacterCacheEntry characterEntry = new CharacterCacheEntry() { };

            // Get character based biome and creature configuration
            //Logger.LogDebug($"Checking Creature {character.gameObject.name} biome settings");
            LevelSystem.SelectCreatureBiomeSettings(character.gameObject, out string creatureName, out DataObjects.CreatureSpecificSetting creatureSettings, out BiomeSpecificSetting biomeSettings, out Heightmap.Biome biome);

            characterEntry.creatureSettings = creatureSettings;

            // Set biome | used to deletion check
            characterEntry.Biome = biome;
            characterEntry.RefCreatureName = creatureName;
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

            if (requiredModifiers == null) {
                if (creatureSettings != null && creatureSettings.RequiredModifiers != null) {
                    requiredModifiers = creatureSettings.RequiredModifiers;
                }
            } else {
                if (creatureSettings != null && creatureSettings.RequiredModifiers != null) {
                    // Merge required modifiers
                    foreach (var reqmod in creatureSettings.RequiredModifiers) {
                        if (requiredModifiers.ContainsKey(reqmod.Key) == false) {
                            requiredModifiers.Add(reqmod.Key, reqmod.Value);
                        }
                    }
                }
            }
            // Set modifier requirements
            characterEntry.ModifiersNotAllowed = notAllowedModifiers;
            characterEntry.ModifiersRequired = requiredModifiers;

            // Check for level or set it
            //Logger.LogDebug("Setting creature level");
            characterEntry.Level = LevelSystem.DetermineLevel(character, creatureZDO, creatureSettings, biomeSettings, leveloverride);


            // Set creature Colorization pallete
            //Logger.LogDebug("Selecting creature colorization");
            characterEntry.Colorization = Colorization.DetermineCharacterColorization(character, characterEntry.Level);

            //Logger.LogDebug("Selecting creature Damage Recieved, Per Level and base values.");
            characterEntry.DamageRecievedModifiers = ModificationExtensionSystem.DetermineCreatureDamageRecievedModifiers(biomeSettings, creatureSettings);
            characterEntry.CreaturePerLevelValueModifiers = ModificationExtensionSystem.DetermineCharacterPerLevelStats(biomeSettings, creatureSettings);
            characterEntry.CreatureBaseValueModifiers = ModificationExtensionSystem.DetermineCreatureBaseStats(biomeSettings, creatureSettings);

            // skip setting cache if the creature is gone already
            uint uid = character.GetZDOID().ID;
            //Logger.LogDebug($"Determined {creatureName} level {characterEntry.Level} Setting cache {uid}");
            if (sessionCache.ContainsKey(uid)) {
                if (updateCache) {
                    sessionCache[uid] = characterEntry;
                }
            } else {
                sessionCache.Add(uid, characterEntry);
            }

            // Return the entry to the caller
            return characterEntry;
        }

        // SAFE to re-run
        public static void StartZOwnerCreatureRoutines(Character chara, CharacterCacheEntry characterEntry, bool spawnratecheck = true) {
            if (characterEntry == null || chara == null || characterEntry.Level == 0) { return; }

            // Destroy character if its selected for deletion
            if (characterEntry.ShouldDelete && chara.m_tamed == false) {
                ZNetScene.instance.StartCoroutine(ModificationExtensionSystem.DestroyCoroutine(chara.gameObject));
                return;
            }

            int clevel = chara.GetLevel();
            // Set level ZDO, only if its not been set, and only if its not what the cache is expecting
            if (clevel <= 1 && characterEntry.Level != clevel && characterEntry.Level != 0) {
                chara.SetLevel(characterEntry.Level);
                chara.m_level = characterEntry.Level;
                LevelUI.InvalidateCacheEntry(chara);
                //Logger.LogDebug($"{characterEntry.RefCreatureName} setting level to {characterEntry.Level} from {clevel}");
            }

            //Logger.LogDebug($"Activating ZOwner setup routines {chara.GetZDOID().ID} {characterEntry.RefCreatureName} - level cache: {characterEntry.Level} character: {clevel}");


            // Reset character level if its overleveled
            if (ValConfig.OverlevedCreaturesGetRerolledOnLoad.Value && clevel > ValConfig.MaxLevel.Value + 1)
            {
                // Rebuild level?
                characterEntry = CompositeLazyCache.GetAndSetLocalCache(chara, updateCache: true);
                int maxlevel = ValConfig.MaxLevel.Value + 1;
                Logger.LogDebug($"{characterEntry.RefCreatureName} level {clevel} over max {maxlevel}, resetting to {maxlevel}");
                chara.SetLevel(maxlevel);
                chara.m_level = maxlevel;
                ModificationExtensionSystem.LoadApplySizeModifications(chara.gameObject, chara.m_nview, characterEntry, force_update: true);
                Colorization.ApplyColorizationWithoutLevelEffects(chara.gameObject, characterEntry.Colorization);
                LevelUI.InvalidateCacheEntry(chara);
            }

            // Logger.LogDebug($"{characterEntry.RefCreatureName} Level check {chara.GetLevel()} - {characterEntry.Level}");
            // Ensure force leveled characters and bosses get their level set even if they are not being directly setup
            if (chara.IsBoss() && ValConfig.ControlBossSpawns.Value || ModificationExtensionSystem.ForceLeveledCreatures.Contains(characterEntry.RefCreatureName))
            {
                chara.SetLevel(characterEntry.Level);
            }

            // Set or load creature modifiers
            characterEntry.CreatureModifiers = GetCreatureModifiers(chara);
            //Logger.LogDebug($"Checking stored mods {characterEntry.CreatureModifiers.Count}");
            //if (characterEntry.CreatureModifiers.Count > 0) {
            //    Logger.LogDebug($"  Stored mods {characterEntry.CreatureModifiers.Keys}");
            //}
            // Setup the creatures modifiers if it does not have any- ideally this only gets calculated on the zowners first setup
            // If network calls are significantly delayed this could be updated by a client and overwritten
            if (characterEntry.CreatureModifiers == null || characterEntry.CreatureModifiers.Count == 0) {
                characterEntry.CreatureModifiers = CreatureModifiers.SelectModifiersForCreature(
                    chara,
                    creatureName: characterEntry.RefCreatureName,
                    creature_settings: characterEntry.creatureSettings,
                    biome: characterEntry.Biome,
                    level: characterEntry.Level,
                    requiredModifiers: characterEntry.ModifiersRequired,
                    notAllowedModifiers: characterEntry.ModifiersNotAllowed
                    );
                SetCreatureModifiers(chara, characterEntry.CreatureModifiers);
            }
            // 
            //if (characterEntry.CreatureModifiers != null) {
            //    StringBuilder sb = new StringBuilder();
            //    sb.AppendLine($"Selecting {characterEntry.RefCreatureName} modifiers:");
            //    foreach (var modifier in characterEntry.CreatureModifiers) {
            //        sb.AppendLine($"{modifier.Key}-{modifier.Value}");
            //    }
            //    Logger.LogDebug(sb.ToString());
            //}

            // Get/set check SLS_SPAWN_MULT - applies spawn if not already applied
            if (spawnratecheck == false) {
                chara.m_nview.GetZDO().Set(SLS_SPAWN_MULT, true);
            } else {
                Spawnrate.CheckSetApplySpawnrate(chara, characterEntry);
            }
        }

        public static CharacterCacheEntry RetrieveStoredCreatureFromCache(Character character)
        {
            //Logger.LogDebug($"Retrieving cached creature data for ( {character == null} || {character.m_nview == null} || {character.IsPlayer()} || {character.m_nview.GetZDO() == null})");
            if (character == null || character.GetZDOID() == ZDOID.None || character.IsPlayer()) { return null; }
            uint cid = character.GetZDOID().ID;
            if (sessionCache.ContainsKey(cid)) { return sessionCache[cid]; }
            return null;
        }

        public static void UpdateCharacterCacheEntry(Character character, CharacterCacheEntry scd)
        {
            uint cid = character.GetZDOID().ID;
            if (sessionCache.ContainsKey(cid))
            {
                sessionCache[cid] = scd; 
            } else {
                sessionCache.Add(cid, scd);
            }
        }

        public static Dictionary<string, ModifierType> GetCreatureModifiers(Character character)
        {
            if (character == null || character.m_nview == null) { return null; }
            string mods = character.m_nview.GetZDO().GetString(SLS_MODSV2, null);
            // Priority storage of V2 Mod format
            if (mods != null) {
                try {
                    return DataObjects.yamldeserializer.Deserialize<Dictionary<string, ModifierType>>(mods);
                }
                catch {  return null; }
            }

            CreatureModifiersZNetProperty StoredMods = new CreatureModifiersZNetProperty(SLS_MODIFIERS, character.m_nview, null);
            return StoredMods.Get();
        }

        public static void SetCreatureModifiers(Character chara, Dictionary<string, ModifierType> modifiers)
        {
            chara.m_nview.GetZDO().Set(SLS_MODSV2, DataObjects.yamlserializerJsonCompat.Serialize(modifiers));
            CharacterCacheEntry cce = GetCacheEntry(chara);
            if (cce != null) {
                cce.CreatureModifiers = modifiers;
                UpdateCharacterCacheEntry(chara, cce);
            }
        }
    }
}
