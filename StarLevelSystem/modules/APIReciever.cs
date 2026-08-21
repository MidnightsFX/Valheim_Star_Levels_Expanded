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
        // Everything here is server-side. Resetting world content means destroying and recreating
        // ZDOs, which only the server may do -- LocationResetControl refuses off-server through its
        // own gates, and the invoke methods below refuse early so a client-side caller gets a clean
        // false instead of a silent no-op.
        //
        // Registration and the read-only queries deliberately do NOT check IsServer. A dependent
        // mod's Awake can run long before ZNet exists, so gating registration on it would make
        // whether a mod's targets register at all depend on BepInEx plugin load order. Registration
        // only touches in-memory lookups and is inert on a client, where SweepAllowed is false
        // anyway.

        // Register a location or vegetation prefab as a reset target on behalf of another mod.
        //
        // resetHours 0 with no schedule means "say nothing about timing", which falls through to the
        // config's Defaults exactly as an omitted ResetHours does in yaml.
        public static bool RegisterLocationResetTarget(string prefabName, string sourceId, float resetHours,
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
                // Never set from the API. Protection policy is the server owner's alone: a mod must
                // not be able to make wards or tombstones stop blocking a reset.
                Protection = null,
            };

            return LocationResetData.RegisterAPIResetTarget(prefabName, sourceId, entry, minDistance, maxDistance);
        }

        public static bool UnregisterLocationResetTarget(string prefabName, string sourceId) {
            return LocationResetData.UnregisterAPIResetTarget(prefabName, sourceId);
        }

        // sourceId null or empty returns every API-registered target, whoever owns it.
        public static List<string> GetRegisteredLocationResetTargets(string sourceId) {
            return LocationResetData.GetAPIRegisteredNames(sourceId);
        }

        // How a target actually resolved, whoever registered it. "source" is the layer that won:
        // entry, group, api, defaults or none.
        public static Dictionary<string, object> GetLocationResetTargetInfo(string prefabName) {
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

        // Cheap pre-flight for a caller that wants to know whether an invoke would be accepted.
        public static bool IsLocationResetReady() {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return false; }
            if (LocationResetData.BlockedByModConflict) { return false; }
            return LocationResetControl.Ready;
        }

        public static Dictionary<string, object> GetLocationResetStatus() {
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
            };
        }

        public static bool IsKnownResetTargetName(string prefabName) {
            if (string.IsNullOrWhiteSpace(prefabName)) { return false; }
            return LocationResetData.IsKnownTargetName(prefabName.Trim().GetStableHashCode());
        }

        // Unix seconds UTC. -1 = no such location in range (or no proxy to read), 0 = never stamped.
        public static long GetLocationLastReset(string locationName, Vector3 center, float radius) {
            return LocationResetQuery.GetLocationLastReset(locationName, center, radius);
        }

        // -1 = unknown or not configured, 0 = due now.
        public static double GetSecondsUntilLocationReset(string locationName, Vector3 center, float radius) {
            return LocationResetQuery.GetSecondsUntilDue(locationName, center, radius);
        }

        public static Dictionary<string, object> GetLocationResetInfo(string locationName, Vector3 center, float radius) {
            return LocationResetQuery.GetLocationInfo(locationName, center, radius);
        }

        // includePrefabs adds the per-prefab census for the chunk, which is a full ZDO pass.
        public static Dictionary<string, object> GetChunkResetInfo(Vector3 position, bool includePrefabs) {
            return LocationResetQuery.GetChunkInfo(position, includePrefabs);
        }

        // Reset the named location nearest `center` within `radius`.
        //
        // safety: 0 = Safe (wait for players to leave the target chunks, then reset; give up after
        // safeWaitSeconds and touch nothing), 1 = Force (reset now, working on chunks that are
        // already loaded around a player). The player-build protection scan runs in BOTH and is never
        // bypassable.
        //
        // Safe is the default the shim passes because an API caller may be firing from a timer with
        // nobody watching. The console command defaults to force instead, because an admin typing it
        // is standing there on purpose -- see LocationCommands.
        //
        // Returns whether the request was accepted. A false return means nothing was started and
        // onComplete will NEVER fire; the reason is written to the Location Reset log.
        public static bool ResetNamedLocation(string locationName, Vector3 center, float radius, int safety,
                bool resetAllMatches, float safeWaitSeconds, bool includeDetail,
                Action<Dictionary<string, object>> onComplete) {

            if (RefuseInvoke("ResetNamedLocation")) { return false; }
            if (string.IsNullOrWhiteSpace(locationName)) {
                Logger.LogLocationResetWarning("SLS-API: ResetNamedLocation was called with no location name.");
                return false;
            }

            return LocationResetControl.RequestReset(new LocationResetControl.ResetRequest() {
                Center = center,
                Radius = radius,
                Safety = ClampSafety(safety),
                LocationName = locationName.Trim(),
                ResetAllMatches = resetAllMatches,
                SafeWaitSeconds = safeWaitSeconds,
                IncludeDetail = includeDetail,
                Source = $"API reset '{locationName.Trim()}'",
            }, null, onComplete);
        }

        // Reset everything the configuration covers within `radius` of `center` -- the same shape as
        // sls-loc-reset, with a safety mode and a result callback.
        public static bool ResetLocationsInRadius(Vector3 center, float radius, int safety,
                float safeWaitSeconds, bool includeDetail, Action<Dictionary<string, object>> onComplete) {

            if (RefuseInvoke("ResetLocationsInRadius")) { return false; }

            return LocationResetControl.RequestReset(new LocationResetControl.ResetRequest() {
                Center = center,
                Radius = radius,
                Safety = ClampSafety(safety),
                SafeWaitSeconds = safeWaitSeconds,
                IncludeDetail = includeDetail,
                Source = $"API reset r={radius:0}",
            }, null, onComplete);
        }

        // Anything outside the known modes clamps to Safe. Fail-safe direction: an unrecognised value
        // must never be read as "go ahead and reset under a player".
        private static int ClampSafety(int safety) {
            return safety == LocationResetControl.SafetyForce ? LocationResetControl.SafetyForce : LocationResetControl.SafetySafe;
        }

        // The one gate the invoke methods share. LocationResetControl refuses again on its own -- it
        // has to, since the console commands reach it by another route -- but refusing here means a
        // client-side caller gets an immediate, explained false rather than a coroutine that quietly
        // does nothing.
        private static bool RefuseInvoke(string method) {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) {
                Logger.LogLocationResetWarning($"SLS-API: {method} is server-side only and was called on a client. Ignored.");
                return true;
            }
            return false;
        }
    }
}
