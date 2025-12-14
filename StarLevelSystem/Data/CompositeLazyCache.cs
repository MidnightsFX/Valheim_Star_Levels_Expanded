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
        public static readonly Vector2 center = new Vector2(0, 0);


        // Add check to delete creature from Znet that removes the creature from the cache
        public static Dictionary<uint, CharacterCacheEntry> SessionCache = new Dictionary<uint, CharacterCacheEntry>();

        // Session tree cache, to avoid recalculating tree levels multiple times
        public static Dictionary<uint, int> TreeSessionCache = new Dictionary<uint, int>();

        public static int GetOrAddCachedTreeEntry(ZNetView zgo) {
            if ( zgo == null || zgo.IsValid() == false || zgo.GetZDO() == null) { return 1; }
            uint cid = zgo.GetZDO().m_uid.ID;
            if (TreeSessionCache.ContainsKey(cid)) { return TreeSessionCache[cid]; }
            TreeSessionCache.Add(cid, LevelSystem.DeterministicDetermineTreeLevel(zgo.gameObject));
            return TreeSessionCache[cid];
        }

        public static void RemoveTreeCacheEntry(uint id) {
            if (TreeSessionCache.ContainsKey(id)) {
                TreeSessionCache.Remove(id);
            }
        }

        public static CharacterCacheEntry GetCacheEntry(uint cid)
        {
            if (SessionCache.ContainsKey(cid))
            {
                return SessionCache[cid];
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
            SessionCache.Remove(cid);
        }

        public static CharacterCacheEntry GetAndSetLocalCache(Character character, int leveloverride = 0, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null, bool updateCache = false) {

            // Check for cached creature data
            CharacterCacheEntry characterEntry = RetrieveStoredCreatureFromCache(character);
            if (characterEntry != null && updateCache == false) {
                //Logger.LogDebug($"{character.name} Creature Data Object Cache available.");
                return characterEntry;
            }

            ZDO creatureZDO = character.m_nview.GetZDO();

            characterEntry ??= new CharacterCacheEntry();

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
            //Logger.LogDebug($"Determined {creatureName} level {level}");

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
        public static void StartZOwnerCreatureRoutines(Character chara, CharacterCacheEntry characterEntry, bool spawnratecheck = true) {
            if (characterEntry == null || chara == null) { return; }

            // Logger.LogDebug($"Activating ZOwner setup routines {characterEntry.RefCreatureName}");

            // Destroy character if its selected for deletion
            if (characterEntry.ShouldDelete && chara.m_tamed == false) {
                ZNetScene.instance.StartCoroutine(ModificationExtensionSystem.DestroyCoroutine(chara.gameObject));
                return;
            }

            int clevel = chara.GetLevel();
            // Set level ZDO, only if its not been set, and only if its not what the cache is expecting
            if (clevel <= 1 && characterEntry.Level != clevel) {
                chara.SetLevel(characterEntry.Level);
                chara.m_level = characterEntry.Level;
                LevelUI.InvalidateCacheEntry(chara);
            }

            // Running this causes inconsistent results due to how long it takes to set a creatures level
            // Verify that the cache and character variables match
            //if (clevel != characterEntry.Level)
            //{
            //    characterEntry.Level = clevel;
            //    chara.m_level = characterEntry.Level;
            //}


            // Reset character level if its overleveled
            if (ValConfig.OverlevedCreaturesGetRerolledOnLoad.Value && clevel > ValConfig.MaxLevel.Value + 1)
            {
                // Rebuild level?
                int level = LevelSystem.DetermineLevel(chara, chara.m_nview.GetZDO(), characterEntry.CreatureSettings, characterEntry.BiomeSettings);
                characterEntry.Level = level;
                Logger.LogDebug($"{characterEntry.RefCreatureName} level {clevel} over max {ValConfig.MaxLevel.Value + 1}, resetting to {characterEntry.Level}");
                chara.SetLevel(characterEntry.Level);
                chara.m_level = characterEntry.Level;
                ModificationExtensionSystem.LoadApplySizeModifications(chara.gameObject, chara.m_nview, characterEntry, force_update: true);
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
            if (characterEntry.CreatureModifiers == null || characterEntry.CreatureModifiers.Count == 0) {
                characterEntry.CreatureModifiers = CreatureModifiers.SelectModifiersForCreature(chara, characterEntry.RefCreatureName, characterEntry.CreatureSettings, characterEntry.Biome, characterEntry.Level, characterEntry.ModifiersRequired, characterEntry.ModifiersNotAllowed);
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
