using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.LocationReset;
using StarLevelSystem.modules.Modifiers;
using StarLevelSystem.modules.Sizes;
using System;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.modules
{
    public static class APIReciever
    {
        public static bool UpdateCreatureLevel(Character chara, int level) {
            if (chara == null) { return false; }
            Logger.LogDebug($"SLS-API: Update Creature level {chara} - {level}");
            LevelSelection.SetAndUpdateCharacterLevel(chara, level);
            return true;
        }

        public static bool UpdateCreatureColorization(Character chara, float value, float hue, float sat, bool emission = false) {
            if (chara == null) { return false; }
            CharacterCacheEntry ccd = CompositeLazyCache.GetCacheEntry(chara);
            if (ccd == null) { return false; }
            // Apply the caller's colorization - this previously ignored all four arguments and just
            // recomputed the level-based default.
            ccd.Colorization = new ColorDef(hue, sat, value, emission);
            CompositeLazyCache.UpdateCharacterCacheEntry(chara, ccd);
            Colorization.ApplyColorizationWithoutLevelEffects(chara.gameObject, ccd.Colorization);
            return true;
        }

        // Base value attributes
        public static float GetBaseAttributeValue(Character chara, int attribute) {
            if (chara == null) { return -1f; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return -1f; }
            return cdc.CreatureBaseValueModifiers[(CreatureBaseAttribute)attribute];
        }

        public static bool UpdateCreatureBaseAttributes(Character chara, int attribute, float value) {
            if (chara == null) { return false; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return false; }
            cdc.CreatureBaseValueModifiers[(CreatureBaseAttribute)attribute] = value;
            if ((CreatureBaseAttribute)attribute == CreatureBaseAttribute.Size) {
                SizeModifications.SetSizeModification(chara.gameObject, chara.m_nview, cdc, true);
            }
            return true;
        }

        public static Dictionary<int, float> GetAllBaseAttributes(Character chara) {
            if (chara == null) { return null; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return null; }
            Dictionary<int, float> ret = new Dictionary<int, float>();
            foreach (var kvp in cdc.CreatureBaseValueModifiers) {
                ret[(int)kvp.Key] = kvp.Value;
            }
            return ret;
        }

        public static bool SetAllBaseAttributes(Character chara, Dictionary<int, float> attributes) {
            if (chara == null) { return false; }
            CharacterCacheEntry scd = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (scd == null) { return false; }
            foreach (var kvp in attributes) {
                scd.CreatureBaseValueModifiers[(CreatureBaseAttribute)kvp.Key] = kvp.Value;
            }
            CompositeLazyCache.UpdateCharacterCacheEntry(chara, scd);
            SpeedModifications.ApplySpeedModifications(chara, scd);
            DamageModifications.ApplyDamageModification(chara, scd);
            SizeModifications.SetSizeModification(chara.gameObject, chara.m_nview, scd, true);
            HealthModifications.ForceApplyHealthModifications(chara, scd);
            return true;
        }

        // Per level attributes
        public static float GetPerLevelAttributeValue(Character chara, int attribute) {
            if (chara == null) { return -1f; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return -1f; }
            return cdc.CreaturePerLevelValueModifiers[(CreaturePerLevelAttribute)attribute];
        }

        public static bool UpdateCreaturePerLevelAttributes(Character chara, int attribute, float value) {
            if (chara == null) { return false; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return false; }
            cdc.CreaturePerLevelValueModifiers[(CreaturePerLevelAttribute)attribute] = value;
            if ((CreaturePerLevelAttribute)attribute == CreaturePerLevelAttribute.SizePerLevel) {
                SizeModifications.SetSizeModification(chara.gameObject, chara.m_nview, cdc, true);
            }
            return true;
        }

        public static Dictionary<int, float> GetAllPerLevelAttributes(Character chara) {
            if (chara == null) { return null; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return null; }
            Dictionary<int, float> ret = new Dictionary<int, float>();
            foreach (var kvp in cdc.CreaturePerLevelValueModifiers)
            {
                ret[(int)kvp.Key] = kvp.Value;
            }
            return ret;
        }

        public static bool SetAllPerLevelAttributes(Character chara, Dictionary<int, float> attributes) {
            if (chara == null) { return false; }
            CharacterCacheEntry scd = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (scd == null) { return false; }
            foreach (var kvp in attributes)
            {
                scd.CreaturePerLevelValueModifiers[(CreaturePerLevelAttribute)kvp.Key] = kvp.Value;
            }
            CompositeLazyCache.UpdateCharacterCacheEntry(chara, scd);
            SpeedModifications.ApplySpeedModifications(chara, scd);
            DamageModifications.ApplyDamageModification(chara, scd);
            SizeModifications.SetSizeModification(chara.gameObject, chara.m_nview, scd, true);
            HealthModifications.ForceApplyHealthModifications(chara, scd);
            return true;
        }

        // Creature damage recived modifiers
        public static float GetCreatureDamageRecievedModifier(Character chara, int attribute) {
            if (chara == null) { return -1f; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return -1f; }
            return cdc.DamageRecievedModifiers[(DamageType)attribute];
        }

        public static bool UpdateCreatureDamageRecievedModifier(Character chara, int attribute, float value) {
            if (chara == null) { return false; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return false; }
            cdc.DamageRecievedModifiers[(DamageType)attribute] = value;
            return true;
        }

        public static Dictionary<int, float> GetAllDamageRecievedModifiers(Character chara) {
            if (chara == null) { return null; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return null; }
            Dictionary<int, float> ret = new Dictionary<int, float>();
            foreach (var kvp in cdc.DamageRecievedModifiers) {
                ret[(int)kvp.Key] = kvp.Value;
            }
            return ret;
        }

        public static bool SetAllDamageRecievedModifiers(Character chara, Dictionary<int, float> attributes) {
            if (chara == null) { return false; }
            CharacterCacheEntry scd = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (scd == null) { return false; }
            foreach (var kvp in attributes)
            {
                scd.DamageRecievedModifiers[(DamageType)kvp.Key] = kvp.Value;
            }
            CompositeLazyCache.UpdateCharacterCacheEntry(chara, scd);
            DamageModifications.ApplyDamageModification(chara, scd);
            return true;
        }

        // Creature bonus damage modifiers

        public static float GetCreatureDamageBonus(Character chara, int attribute) {
            if (chara == null) { return -1f; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return -1f; }
            if (cdc.CreatureDamageBonus.ContainsKey((DamageType)attribute)) {
                return cdc.CreatureDamageBonus[(DamageType)attribute];
            }
            return 0f;
        }

        public static bool UpdateCreatureDamageBonus(Character chara, int attribute, float value) {
            if (chara == null) { return false; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return false; }
            cdc.CreatureDamageBonus[(DamageType)attribute] = value;
            return true;
        }

        public static Dictionary<int, float> GetAllDamageBonus(Character chara) {
            if (chara == null) { return null; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return null; }
            Dictionary<int, float> ret = new Dictionary<int, float>();
            foreach (var kvp in cdc.CreatureDamageBonus) {
                ret[(int)kvp.Key] = kvp.Value;
            }
            return ret;
        }

        public static bool SetAllDamageBonus(Character chara, Dictionary<int, float> attributes) {
            if (chara == null) { return false; }
            CharacterCacheEntry scd = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (scd == null) { return false; }
            foreach (var kvp in attributes)
            {
                scd.CreatureDamageBonus[(DamageType)kvp.Key] = kvp.Value;
            }
            CompositeLazyCache.UpdateCharacterCacheEntry(chara, scd);
            DamageModifications.ApplyDamageModification(chara, scd);
            return true;
        }

        // Applies all changes made to attributes to the creature

        public static bool ApplyUpdatesToCreature(Character chara) {
            if (chara == null) { return false; }
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return false; }
            SpeedModifications.ApplySpeedModifications(chara, cdc);
            DamageModifications.ApplyDamageModification(chara, cdc);
            SizeModifications.SetSizeModification(chara.gameObject, chara.m_nview, cdc, true);
            HealthModifications.ForceApplyHealthModifications(chara, cdc);
            return true;
        }

        // Modifiers Management

        public static List<string> GetPossibleModifiersForType(int modifierType) {
            List<string> modifiersAndType = new List<string>();
            switch(modifierType) {
                // 0 = Major
                case 0:
                    foreach(string modName in CreatureModifiersData.ActiveCreatureModifiers.MajorModifiers.Keys) {
                        modifiersAndType.Add(modName);
                    }
                    break;

                // 1 = Minor
                case 1:
                    foreach (string modName in CreatureModifiersData.ActiveCreatureModifiers.MinorModifiers.Keys) {
                        modifiersAndType.Add(modName);
                    }
                    break;

                // 2 = Boss
                case 2:
                    foreach (string modName in CreatureModifiersData.ActiveCreatureModifiers.BossModifiers.Keys) {
                        modifiersAndType.Add(modName);
                    }
                    break;
                default:
                    Logger.LogWarning($"Invalid modifier type {modifierType} passed to GetAllPossibleModifiers. Valid types are 0 (Major), 1 (Minor), 2 (Boss).");
                    break;
            }
            return modifiersAndType;
        }

        public static Dictionary<string, int> GetAllModifiersForCreature(Character chara) {
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(chara);
            if (mods == null) { return null; }
            Dictionary<string, int> modifiersAndType = new Dictionary<string, int>();
            foreach (var mod in mods) {
                modifiersAndType.Add(mod.Key.ToString(), (int)mod.Value);
            }
            return modifiersAndType;
        }

        public static bool AddModifierToCreature(Character chara, string modifierName, int modifierType, bool update = true) {
            CharacterCacheEntry cdc = CompositeLazyCache.GetAndSetLocalCache(chara);
            if (cdc == null) { return false; }
            return CreatureModifiers.AddCreatureModifier(chara, (ModifierType)modifierType, modifierName, update);
        }

        public static bool AddNewModifierToSLS(
            int modifierID,
            string modifier_name,
            Delegate setupMethod = null,
            float selectionWeight = 10f,
            float basepower = 0f,
            float perlevelpower = 0f,
            Dictionary<Heightmap.Biome, List<string>> biomeConfig = null,
            int namingStyle = 2,
            string name_suffixes = null,
            string name_prefixes = null,
            int visualStyle = 0,
            Sprite starIcon = null,
            GameObject visualEffect = null,
            List<string> allowed_creatures = null, 
            List<string> unallowed_creatures = null, 
            List<Heightmap.Biome> allowed_biomes = null)
        {
            if (ModifierNamesLookupTable.ContainsID(modifierID)) {
                Logger.LogWarning($"Modifier ID {modifierID} already exists as {ModifierNamesLookupTable.GetValue(modifierID)}, please choose a different ID");
                return false;
            }

            CreatureModifiersData.ModifierNamesLookupTable.AddValue(modifier_name, modifierID);

            CreatureModifierDefinition newMod = new CreatureModifierDefinition();

            if (starIcon != null) {
                newMod.StarVisualAPI = starIcon;
                newMod.StarVisual = starIcon.name;
            }

            if (visualEffect != null) {
                newMod.VisualEffectAPI = visualEffect;
                newMod.VisualEffect = visualEffect.name;
            }

            if (setupMethod != null) {
                newMod.SetupEvent = setupMethod;
            }

            if (name_suffixes != null)
            {
                newMod.NameSuffix = name_suffixes;
            }

            if (name_prefixes != null)
            {
                newMod.NamePrefix = name_prefixes;
            }

            if (namingStyle > 2 || namingStyle < 0) { namingStyle = 2; }
            newMod.NamingConvention = (NameSelectionStyle)namingStyle;

            if (visualStyle > 3 || visualStyle < 0) { visualStyle = 0; }
            newMod.VisualEffectStyle = (VisualEffectStyle)visualStyle;

            CreatureModifierConfiguration clientConfig = new CreatureModifierConfiguration();

            clientConfig.Config = new CreatureModConfig()
            {
                BasePower = basepower,
                PerlevelPower = perlevelpower,
                BiomeObjects = biomeConfig
            };

            clientConfig.SelectionWeight = selectionWeight;

            if (allowed_creatures != null) {
                clientConfig.AllowedCreatures = allowed_creatures;
            }
            if (unallowed_creatures != null) {
                clientConfig.UnallowedCreatures = unallowed_creatures;
            }
            if (allowed_biomes != null) {
                clientConfig.AllowedBiomes = allowed_biomes;
            }

            // Register the definition + configuration into the active modifier set. Building them and
            // only clearing the probability caches (as this method previously did) discarded both and
            // made the API a silent no-op.
            CreatureModifiersData.RegisterAPIModifier(modifier_name, newMod, clientConfig);

            return true;
        }

        ////////////////////////////////////////
        /// Location Reset
        ////////////////////////////////////////
        //
        // Every one of these works from a client as well as from the server.
        //
        // The work itself is server-owned -- the ZDOs a reset destroys and recreates, the per-zone
        // state file, the sweep -- but the mods that want to drive it are often client-side, so each
        // public method here answers locally when it is running on the server and relays through
        // LocationResetNetwork when it is not.
        //
        // That is why they are all callback-shaped and return bool rather than returning the answer
        // directly: on a client the answer arrives over the network some frames later, and a method
        // that returned a value would have to lie about it.
        //
        // THE CALLBACK ALWAYS FIRES, EXACTLY ONCE. Success, deferral, refusal, timeout, a lost
        // connection -- every one of them ends in the callback, carrying an outcome, a human-readable
        // reason and a machine-readable refusalCode. A caller's follow-up logic therefore lives in
        // one place, rather than being split between an inline false branch and a callback that may
        // or may not arrive. The bool return is only a convenience: it says whether the work started,
        // and a false return has already delivered the reason through the callback.
        //
        // On the server, and for anything refused before it left this machine, the callback is
        // invoked before the call returns.

        // Register a location or vegetation prefab as a reset target on behalf of another mod.
        //
        // resetHours 0 with no schedule means "say nothing about timing", which falls through to the
        // config's Defaults exactly as an omitted ResetHours does in yaml.
        public static bool RegisterLocationResetTarget(string prefabName, string sourceId, float resetHours,
                string resetSchedule, int mode, bool resetTerrain, float terrainRadius, float extraTerrainRadius,
                bool resetInterior, float minDistance, float maxDistance, bool enabled, Action<bool> onResult) {

            return RelayScalar(LocationResetNetwork.Op.Register,
                new Dictionary<string, object>() {
                    { "name", prefabName ?? "" },
                    { "sourceId", sourceId ?? "" },
                    { "resetHours", resetHours },
                    { "resetSchedule", resetSchedule ?? "" },
                    { "mode", mode },
                    { "resetTerrain", resetTerrain },
                    { "terrainRadius", terrainRadius },
                    { "extraTerrainRadius", extraTerrainRadius },
                    { "resetInterior", resetInterior },
                    { "minDistance", minDistance },
                    { "maxDistance", maxDistance },
                    { "enabled", enabled },
                },
                () => LocalRegister(prefabName, sourceId, resetHours, resetSchedule, mode, resetTerrain,
                                    terrainRadius, extraTerrainRadius, resetInterior, minDistance, maxDistance, enabled),
                onResult, false, 0f);
        }

        // The registration itself. Called directly on the server, and by the RPC dispatcher for a
        // relayed one.
        internal static bool LocalRegister(string prefabName, string sourceId, float resetHours,
                string resetSchedule, int mode, bool resetTerrain, float terrainRadius, float extraTerrainRadius,
                bool resetInterior, float minDistance, float maxDistance, bool enabled) {

            // Clamped rather than rejected, matching how AddNewModifierToSLS treats its own enum-ish
            // ints. Full is the safe direction: TerrainOnly would silently stop resetting contents.
            if (mode > 1 || mode < 0) {
                Logger.LogLocationResetWarning($"'{sourceId}' registered '{prefabName}' with mode {mode}; " +
                    $"valid values are 0 (Full) and 1 (TerrainOnly). Using Full.");
                mode = 0;
            }

            LocationResetEntry entry = new LocationResetEntry() {
                Enabled = enabled,
                ResetHours = resetHours > 0f ? (float?)resetHours : null,
                ResetSchedule = string.IsNullOrWhiteSpace(resetSchedule) ? null : resetSchedule.Trim(),
                Mode = (LocationResetMode)mode,
                ResetTerrain = resetTerrain,
                TerrainRadius = terrainRadius > 0f ? (float?)terrainRadius : null,
                ExtraTerrainRadius = extraTerrainRadius > 0f ? (float?)extraTerrainRadius : null,
                ResetInterior = resetInterior,
                // Never set from the API, local or relayed. Protection policy is the server owner's
                // alone: a mod must not be able to make wards or tombstones stop blocking a reset.
                Protection = null,
            };

            return LocationResetData.RegisterAPIResetTarget(prefabName, sourceId, entry, minDistance, maxDistance);
        }

        public static bool UnregisterLocationResetTarget(string prefabName, string sourceId, Action<bool> onResult) {
            return RelayScalar(LocationResetNetwork.Op.Unregister,
                new Dictionary<string, object>() { { "name", prefabName ?? "" }, { "sourceId", sourceId ?? "" } },
                () => LocationResetData.UnregisterAPIResetTarget(prefabName, sourceId),
                onResult, false, 0f);
        }

        // sourceId null or empty returns every API-registered target, whoever owns it.
        public static bool GetRegisteredLocationResetTargets(string sourceId, Action<List<string>> onResult) {
            return RelayScalar(LocationResetNetwork.Op.RegisteredNames,
                new Dictionary<string, object>() { { "sourceId", sourceId ?? "" } },
                () => LocationResetData.GetAPIRegisteredNames(sourceId),
                onResult, new List<string>(), 0f);
        }

        // How a target actually resolved, whoever registered it. "source" is the layer that won:
        // entry, group, api, defaults or none.
        public static bool GetLocationResetTargetInfo(string prefabName, Action<Dictionary<string, object>> onResult) {
            return RelayDictionary(LocationResetNetwork.Op.TargetInfo,
                new Dictionary<string, object>() { { "name", prefabName ?? "" } },
                () => LocalTargetInfo(prefabName), onResult, 0f);
        }

        internal static Dictionary<string, object> LocalTargetInfo(string prefabName) {
            Dictionary<string, object> info = new Dictionary<string, object>() {
                { "name", prefabName ?? "" },
                { "known", false },
                { "configured", false },
                { "enabled", false },
                { "source", "none" },
            };
            if (string.IsNullOrWhiteSpace(prefabName)) { return info; }

            string name = prefabName.Trim();
            int hash = name.GetStableHashCode();
            info["name"] = name;
            info["known"] = LocationResetData.IsKnownTargetName(hash);
            info["source"] = LocationResetData.DescribeResolutionSource(name);
            info["hardBlocked"] = LocationResetData.HardBlockedLocations.Contains(name);
            info["registeredByAPI"] = LocationResetData.TryGetAPIRegistration(name, out LocationResetData.APIResetRegistration api);
            info["registeredBy"] = api != null ? api.SourceId : "";

            bool isLocation = LocationResetData.TryGetLocationEntry(hash, out LocationResetData.ResolvedResetEntry entry);
            bool isVegetation = LocationResetData.TryGetVegetationEntry(hash, out LocationResetData.ResolvedResetEntry vegEntry);
            if (entry == null) { entry = vegEntry; }
            info["isLocation"] = isLocation;
            info["isVegetation"] = isVegetation;
            info["configured"] = entry != null;
            if (entry == null) { return info; }

            info["enabled"] = entry.Enabled;
            info["groupName"] = entry.GroupName ?? "";
            info["resetSeconds"] = entry.ResetSeconds;
            info["schedule"] = entry.Schedule != null ? entry.Schedule.Expression : "";
            info["mode"] = (int)entry.Mode;
            info["resetTerrain"] = entry.ResetTerrain;
            info["terrainRadius"] = entry.TerrainRadius;
            info["extraTerrainRadius"] = entry.ExtraTerrainRadius;
            info["resetInterior"] = entry.ResetInterior;
            return info;
        }

        // Whether a reset request would currently be accepted.
        public static bool IsLocationResetReady(Action<bool> onResult) {
            return RelayScalar(LocationResetNetwork.Op.Ready, null, LocalReady, onResult, false, 0f);
        }

        internal static bool LocalReady() {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return false; }
            if (LocationResetData.BlockedByModConflict) { return false; }
            return LocationResetControl.Ready;
        }

        public static bool GetLocationResetStatus(Action<Dictionary<string, object>> onResult) {
            return RelayDictionary(LocationResetNetwork.Op.Status, null, LocalStatus, onResult, 0f);
        }

        internal static Dictionary<string, object> LocalStatus() {
            return new Dictionary<string, object>() {
                { "available", true },
                { "isServer", ZNet.instance != null && ZNet.instance.IsServer() },
                { "ready", LocationResetControl.Ready },
                { "sweepAllowed", LocationResetControl.SweepAllowed },
                // The BepInEx master switch and the yaml one are separate gates and an admin can have
                // either off, so both are reported rather than folded together.
                { "masterSwitchEnabled", ValConfig.EnableLocationReset.Value },
                { "configEnabled", LocationResetData.ConfigEnabled },
                { "blockedByModConflict", LocationResetData.BlockedByModConflict },
                { "resetRunning", LocationResetControl.ManualResetRunning },
                { "trackedZones", LocationResetState.TrackedZoneCount },
                { "generatedZones", ZoneSystem.instance != null ? ZoneSystem.instance.m_generatedZones.Count : 0 },
                { "sweepFloorSeconds", LocationResetData.MinEnabledIntervalSeconds },
                { "apiRegistrations", LocationResetData.APIAdded.Count },
                // The envelope a client's requests are held to, so a client-side mod can size its own
                // requests instead of discovering the limits by being clamped.
                { "clientMaxRadius", ValConfig.ClientLocationResetMaxRadius.Value },
                { "clientMaxDistance", ValConfig.ClientLocationResetMaxDistance.Value },
                { "clientCooldownSeconds", ValConfig.ClientLocationResetCooldownSeconds.Value },
            };
        }

        public static bool IsKnownResetTargetName(string prefabName, Action<bool> onResult) {
            return RelayScalar(LocationResetNetwork.Op.IsKnownName,
                new Dictionary<string, object>() { { "name", prefabName ?? "" } },
                () => LocalIsKnownName(prefabName), onResult, false, 0f);
        }

        internal static bool LocalIsKnownName(string prefabName) {
            if (string.IsNullOrWhiteSpace(prefabName)) { return false; }
            return LocationResetData.IsKnownTargetName(prefabName.Trim().GetStableHashCode());
        }

        // Unix seconds UTC. -1 = no such location in range (or no proxy to read), 0 = never stamped.
        public static bool GetLocationLastReset(string locationName, Vector3 center, float radius, Action<long> onResult) {
            return RelayScalar(LocationResetNetwork.Op.LastReset, LocationArgs(locationName, center, radius),
                () => LocationResetQuery.GetLocationLastReset(locationName, center, radius),
                onResult, -1L, 0f);
        }

        // -1 = unknown or not configured, 0 = due now.
        public static bool GetSecondsUntilLocationReset(string locationName, Vector3 center, float radius, Action<double> onResult) {
            return RelayScalar(LocationResetNetwork.Op.SecondsUntilDue, LocationArgs(locationName, center, radius),
                () => LocationResetQuery.GetSecondsUntilDue(locationName, center, radius),
                onResult, -1d, 0f);
        }

        public static bool GetLocationResetInfo(string locationName, Vector3 center, float radius,
                Action<Dictionary<string, object>> onResult) {
            return RelayDictionary(LocationResetNetwork.Op.LocationInfo, LocationArgs(locationName, center, radius),
                () => LocationResetQuery.GetLocationInfo(locationName, center, radius), onResult, 0f);
        }

        // includePrefabs adds the per-prefab census for the chunk, which is a full ZDO pass.
        public static bool GetChunkResetInfo(Vector3 position, bool includePrefabs,
                Action<Dictionary<string, object>> onResult) {
            Dictionary<string, object> args = PositionArgs(position);
            args["includePrefabs"] = includePrefabs;
            return RelayDictionary(LocationResetNetwork.Op.ChunkInfo, args,
                () => LocationResetQuery.GetChunkInfo(position, includePrefabs), onResult, 0f);
        }

        // Reset the named location nearest `center` within `radius`.
        //
        // safety: 0 = Safe (wait for players to leave the target chunks, then reset; give up after
        // safeWaitSeconds and touch nothing), 1 = Force (reset now, working on chunks that are
        // already loaded around a player). The player-build protection scan runs in BOTH and is never
        // bypassable, from either side of the network.
        //
        // Safe is the default the shim passes because an API caller may be firing from a timer with
        // nobody watching. The console command defaults to force instead, because an admin typing it
        // is standing there on purpose -- see LocationCommands.
        //
        // Returns whether the request was dispatched. A false return means the reset was never
        // started and onComplete will NEVER fire.
        public static bool ResetNamedLocation(string locationName, Vector3 center, float radius, int safety,
                bool resetAllMatches, float safeWaitSeconds, bool includeDetail,
                Action<Dictionary<string, object>> onComplete) {

            if (string.IsNullOrWhiteSpace(locationName)) {
                // Delivered rather than just logged, like every other refusal: a caller that only
                // handles its callback must not be left waiting because of a bad argument.
                const string reason = "ResetNamedLocation needs a location name.";
                Logger.LogLocationResetWarning($"SLS-API: {reason}");
                Deliver(onComplete, ResetSummary.RefusalDictionary(ResetSummary.CodeNoName, reason),
                        LocationResetNetwork.Op.ResetNamed);
                return false;
            }

            Dictionary<string, object> args = ResetArgs(center, radius, safety, safeWaitSeconds, includeDetail);
            args["name"] = locationName.Trim();
            args["resetAllMatches"] = resetAllMatches;

            return RelayReset(LocationResetNetwork.Op.ResetNamed, args, safeWaitSeconds, onComplete,
                () => LocationResetControl.RequestReset(new LocationResetControl.ResetRequest() {
                    Center = center,
                    Radius = radius,
                    Safety = ClampSafety(safety),
                    LocationName = locationName.Trim(),
                    ResetAllMatches = resetAllMatches,
                    SafeWaitSeconds = safeWaitSeconds,
                    IncludeDetail = includeDetail,
                    Source = $"API reset '{locationName.Trim()}'",
                }, null, onComplete));
        }

        // Reset everything the configuration covers within `radius` of `center` -- the same shape as
        // sls-loc-reset, with a safety mode and a result callback.
        public static bool ResetLocationsInRadius(Vector3 center, float radius, int safety,
                float safeWaitSeconds, bool includeDetail, Action<Dictionary<string, object>> onComplete) {

            return RelayReset(LocationResetNetwork.Op.ResetRadius,
                ResetArgs(center, radius, safety, safeWaitSeconds, includeDetail), safeWaitSeconds, onComplete,
                () => LocationResetControl.RequestReset(new LocationResetControl.ResetRequest() {
                    Center = center,
                    Radius = radius,
                    Safety = ClampSafety(safety),
                    SafeWaitSeconds = safeWaitSeconds,
                    IncludeDetail = includeDetail,
                    Source = $"API reset r={radius:0}",
                }, null, onComplete));
        }

        // Anything outside the known modes clamps to Safe. Fail-safe direction: an unrecognised value
        // must never be read as "go ahead and reset under a player".
        private static int ClampSafety(int safety) {
            return safety == LocationResetControl.SafetyForce ? LocationResetControl.SafetyForce : LocationResetControl.SafetySafe;
        }

        // -----------------------------------------------------------------------------------------
        // Local-or-relay plumbing
        // -----------------------------------------------------------------------------------------

        private static Dictionary<string, object> PositionArgs(Vector3 position) {
            // Written component-wise rather than through ZPackage.Write(Vector3), so the whole
            // payload stays one uniform tagged dictionary in both directions.
            return new Dictionary<string, object>() {
                { "x", position.x }, { "y", position.y }, { "z", position.z },
            };
        }

        private static Dictionary<string, object> LocationArgs(string locationName, Vector3 center, float radius) {
            Dictionary<string, object> args = PositionArgs(center);
            args["name"] = locationName ?? "";
            args["radius"] = radius;
            return args;
        }

        private static Dictionary<string, object> ResetArgs(Vector3 center, float radius, int safety,
                                                            float safeWaitSeconds, bool includeDetail) {
            Dictionary<string, object> args = PositionArgs(center);
            args["radius"] = radius;
            args["safety"] = safety;
            args["safeWaitSeconds"] = safeWaitSeconds;
            args["includeDetail"] = includeDetail;
            return args;
        }

        // A scalar answer: run it here on the server, otherwise ask the server and unwrap what comes
        // back. `failure` is what the caller hears when the server refuses or never answers.
        private static bool RelayScalar<T>(LocationResetNetwork.Op op, Dictionary<string, object> args,
                                           Func<T> local, Action<T> onResult, T failure, float timeout) {
            if (LocationResetNetwork.IsServer) {
                T value = local();
                Deliver(onResult, value, op);
                return true;
            }
            if (LocationResetNetwork.Send(op, args, timeout, (result) => {
                    Deliver(onResult, Unwrap(result, failure, op), op);
                })) { return true; }

            // Nothing to send it to. A scalar callback has nowhere to carry a reason, so the sentinel
            // goes through and the explanation goes to the log -- but the callback still fires, so a
            // caller waiting on it is never left hanging.
            Deliver(onResult, failure, op);
            return false;
        }

        // A dictionary answer, where the payload IS the result rather than being wrapped in a value
        // key. A refusal still arrives as an error dictionary and is handed through as-is: the caller
        // can read "error" from it, and substituting an empty result would look like a successful
        // query that happened to find nothing.
        private static bool RelayDictionary(LocationResetNetwork.Op op, Dictionary<string, object> args,
                                            Func<Dictionary<string, object>> local,
                                            Action<Dictionary<string, object>> onResult, float timeout) {
            if (LocationResetNetwork.IsServer) {
                Dictionary<string, object> value = local();
                Deliver(onResult, value, op);
                return true;
            }
            if (LocationResetNetwork.Send(op, args, timeout, (result) => {
                    LogIfRefused(result, op);
                    Deliver(onResult, result, op);
                })) { return true; }

            Deliver(onResult, NoConnection(op), op);
            return false;
        }

        // A reset is the one operation whose answer is produced long after the request: the routine
        // runs for as long as it runs, and in Safe mode spends most of that waiting. The relayed
        // timeout is therefore sized from the caller's own wait budget plus a margin, so a legitimate
        // slow reset is never reported as a lost answer.
        private static bool RelayReset(LocationResetNetwork.Op op, Dictionary<string, object> args,
                                       float safeWaitSeconds, Action<Dictionary<string, object>> onComplete,
                                       Func<bool> local) {
            // RequestReset delivers its own refusals through this same callback, so the local path
            // needs nothing extra here.
            if (LocationResetNetwork.IsServer) { return local(); }

            float timeout = Mathf.Max(safeWaitSeconds, 300f) + 60f;
            if (LocationResetNetwork.Send(op, args, timeout, (result) => {
                    // Refusals are delivered rather than swallowed. This callback is where a caller
                    // decides what happens next -- retry later, tell the player, refund the item --
                    // and "the server said no, and here is the code for why" is exactly the input
                    // that decision needs.
                    LogIfRefused(result, op);
                    Deliver(onComplete, result, op);
                })) { return true; }

            Deliver(onComplete, NoConnection(op), op);
            return false;
        }

        // A request that never left this machine. Shaped like every other refusal, so a caller reads
        // the same keys whether the server turned it down or it never reached one.
        private static Dictionary<string, object> NoConnection(LocationResetNetwork.Op op) {
            return ResetSummary.RefusalDictionary(ResetSummary.CodeNoConnection,
                $"{op} could not be sent: this client has no server connection.");
        }

        private static T Unwrap<T>(Dictionary<string, object> result, T failure, LocationResetNetwork.Op op) {
            if (LogIfRefused(result, op)) { return failure; }
            if (result == null || result.TryGetValue(LocationResetNetwork.ValueKey, out object value) == false) { return failure; }
            if (value == null) { return failure; }
            if (value is T typed) { return typed; }

            // The codec writes whichever type the value actually was, and a long that fits in an int
            // is written as an int. Convert rather than fail.
            try {
                return (T)Convert.ChangeType(value, typeof(T));
            } catch (Exception) {
                return failure;
            }
        }

        // True when this answer is a refusal rather than a result. Read off the outcome, which every
        // refusal carries whether it came from the reset machinery, the server's request guards, or
        // this client giving up on a reply.
        private static bool LogIfRefused(Dictionary<string, object> result, LocationResetNetwork.Op op) {
            if (result == null) { return false; }
            if (result.TryGetValue(LocationResetNetwork.OutcomeKey, out object outcome) == false) { return false; }
            if (Convert.ToString(outcome) != ResetSummary.OutcomeRefused) { return false; }

            result.TryGetValue(LocationResetNetwork.ReasonKey, out object reason);
            result.TryGetValue(LocationResetNetwork.RefusalCodeKey, out object code);
            Logger.LogLocationResetWarning($"SLS-API: {op} was refused [{code}] - {reason}");
            return true;
        }

        private static void Deliver<T>(Action<T> onResult, T value, LocationResetNetwork.Op op) {
            if (onResult == null) { return; }
            try {
                onResult(value);
            } catch (Exception e) {
                // On the relayed path this runs inside an RPC handler; on the local path it runs
                // inside whatever the caller was doing. Neither is a place to let a third-party
                // delegate's exception escape.
                Logger.LogLocationResetWarning($"A mod's {op} callback threw and was ignored: {e}");
            }
        }
    }
}
