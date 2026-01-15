using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    public static class CreatureModifiers
    {
        internal static readonly string NoMods = "None";
        static readonly List<NameSelectionStyle> prefixSelectors = new List<NameSelectionStyle>() {
            NameSelectionStyle.RandomFirst,
            NameSelectionStyle.RandomBoth
        };
        static readonly List<NameSelectionStyle> suffixSelectors = new List<NameSelectionStyle>() {
            NameSelectionStyle.RandomLast,
            NameSelectionStyle.RandomBoth
        };

        public static void RunOnceModifierSetup(Character character, CharacterCacheEntry cacheEntry)
        {
            if (cacheEntry.CreatureModifiers == null || cacheEntry.RunOnceDone == true) { return; }
            int appliedMods = 0;
            foreach (KeyValuePair<string, ModifierType> kvp in cacheEntry.CreatureModifiers)
            {
                if (ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value && appliedMods >= (character.m_level -1)) { break; }
                if (kvp.Key == NoMods || kvp.Key == string.Empty) { continue; }
                //Logger.LogDebug($"Runonce setup {kvp.Value} - {kvp.Key}");
                switch (kvp.Value)
                {
                    case ModifierType.Boss:
                        RunOnceModifier(kvp.Key, character, cacheEntry, CreatureModifiersData.ActiveCreatureModifiers.BossModifiers);
                        break;
                    case ModifierType.Major:
                        RunOnceModifier(kvp.Key, character, cacheEntry, CreatureModifiersData.ActiveCreatureModifiers.MajorModifiers);
                        break;
                    case ModifierType.Minor:
                        RunOnceModifier(kvp.Key, character, cacheEntry, CreatureModifiersData.ActiveCreatureModifiers.MinorModifiers);
                        break;
                }
                appliedMods += 1;
            }
            cacheEntry.RunOnceDone = true;
        }

        public static void SetupModifiers(Character character, CharacterCacheEntry cacheEntry, Dictionary<string, ModifierType> selectedMods) {
            if (selectedMods == null) { return; }
            int appliedMods = 0;
            foreach (KeyValuePair<string, ModifierType> kvp in selectedMods)
            {
                if (ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value && appliedMods >= (character.m_level -1)) { break; }
                if (kvp.Key == NoMods || kvp.Key == string.Empty) { continue; }
                //Logger.LogDebug($"Setup running {kvp.Value} - {kvp.Key}");
                switch (kvp.Value)
                {
                    case ModifierType.Boss:
                        StartupModifier(kvp.Key, character, cacheEntry, CreatureModifiersData.ActiveCreatureModifiers.BossModifiers);
                        break;
                    case ModifierType.Major:
                        StartupModifier(kvp.Key, character, cacheEntry, CreatureModifiersData.ActiveCreatureModifiers.MajorModifiers);
                        break;
                    case ModifierType.Minor:
                        StartupModifier(kvp.Key, character, cacheEntry, CreatureModifiersData.ActiveCreatureModifiers.MinorModifiers);
                        break;
                }
                appliedMods += 1;
            }
        }

        private static void RunOnceModifier(string mod, Character character, CharacterCacheEntry cacheEntry, Dictionary<string, CreatureModifierConfiguration> availableMods)
        {
            //Logger.LogDebug($"Setting up minor modifier {mod} for character {character.name}");
            if (!availableMods.ContainsKey(mod))
            {
                if (mod == NoMods) { return; }
                Logger.LogWarning($"Modifier {mod} not found in CreatureModifiersData, skipping runonce setup for {character.name}");
                return;
            }
            var selectedMod = availableMods[mod];
            //Logger.LogDebug($"Setting up mod");
            CreatureModifiersData.ModifierDefinitions[mod].RunOnceMethodCall(character, selectedMod.Config, cacheEntry);
        }

        private static void StartupModifier(string mod, Character character, CharacterCacheEntry cacheEntry, Dictionary<string, CreatureModifierConfiguration> availableMods) {
            //Logger.LogDebug($"Setting up minor modifier {mod} for character {character.name}");
            if (!availableMods.ContainsKey(mod)) {
                if (mod == NoMods) { return; }
                Logger.LogWarning($"Modifier {mod} not found in CreatureModifiersData, skipping setup for {character.name}");
                return;
            }
            var selectedMod = availableMods[mod];
            //Logger.LogDebug($"Setting up mod");
            CreatureModifiersData.ModifierDefinitions[mod].SetupMethodCall(character, selectedMod.Config, cacheEntry);
            //Logger.LogDebug($"Setting up mod vfx");
            SetupCreatureVFX(character, CreatureModifiersData.ModifierDefinitions[mod]);
        }

        internal static void SetupCreatureVFX(Character character, CreatureModifierDefinition cmodifier) {
            if (cmodifier.VisualEffect != null) {

                GameObject effectPrefab = CreatureModifiersData.LoadedModifierEffects[cmodifier.VisualEffect];
                bool hasVFXAlready = character.transform.Find($"{effectPrefab.name}(Clone)");
                //Logger.LogDebug($"Setting up visual effect for {character.name} {character.GetZDOID().ID} - {hasVFXAlready}");
                if (hasVFXAlready == false) {
                    //Logger.LogDebug($"Adding visual effects for {character.name}");
                    GameObject vfxadd = GameObject.Instantiate(effectPrefab, character.transform);
                    float height = character.GetHeight();
                    float scale = height / 5f;
                    float rscale = character.GetRadius() / 2f;

                    switch (cmodifier.VisualEffectStyle)
                    {
                        case VisualEffectStyle.top:
                            vfxadd.transform.localPosition = new Vector3(0, height, 0);
                            break;
                        case VisualEffectStyle.bottom:
                            vfxadd.transform.localPosition = new Vector3(0, 0, 0);
                            break;
                        case VisualEffectStyle.objectCenter:
                            vfxadd.transform.localPosition = new Vector3(0, height / 2, 0);
                            break;
                    }
                    // Scale the visual effect based on the creatures height/width
                    vfxadd.transform.localScale = new Vector3(vfxadd.transform.localScale.x * scale, vfxadd.transform.localScale.y * rscale, vfxadd.transform.localScale.z * scale);
                }
            }
        }

        internal static string BuildCreatureLocalizableName(Character chara, Dictionary<string, ModifierType> modifiers) {
            modifiers ??= new Dictionary<string, ModifierType>();
            List<string> prefix_names = new List<string>();
            List<string> suffix_names = new List<string>();
            int nameEntries = 0;
            int selectedPrefixes = 1; // Starts at 1 since it only gains when one is added, and needs to be greater than 0 to skip if no prefixes are allowed
            List<string> remainingNameSegments = modifiers.Keys.ToList();
            if (ValConfig.MinorModifiersFirstInName.Value) {
                remainingNameSegments = modifiers.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).Reverse().ToList();
            }
            foreach (string modifierName in remainingNameSegments)
            {
                // Don't show modifier names for creatures that have a limit number of modifiers based on their level
                if (ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value && nameEntries >= chara.m_level - 1) { break; }
                //Logger.LogDebug($"Setting name segment for: {modifierName}");
                if (remainingNameSegments.Count <= 0 || nameEntries >= remainingNameSegments.Count) { break; }
                nameEntries++;
                if (modifierName == NoMods) { continue; }

                ModifierType modtype = ModifierType.Major;
                if (modifiers.ContainsKey(modifierName)) {
                    modtype = modifiers[modifierName];
                }
                CreatureModifierDefinition creaturemod = CreatureModifiersData.ModifierDefinitions[modifierName];
                if (creaturemod == null) { continue; }
                if (selectedPrefixes <= ValConfig.LimitCreatureModifierPrefixes.Value && prefixSelectors.Contains(creaturemod.namingConvention) && creaturemod.NamePrefix != null && creaturemod.NamePrefix.Length > 0)
                {
                    selectedPrefixes++;
                    prefix_names.Add(creaturemod.NamePrefix);
                    continue;
                }
                //Logger.LogDebug($"checking to add suffix");
                if (suffixSelectors.Contains(creaturemod.namingConvention) && creaturemod.NameSuffix != null && creaturemod.NameSuffix.Length > 0)
                {
                    suffix_names.Add(creaturemod.NameSuffix);
                }
            }

            string cname = chara.m_name;

            if (prefix_names.Count == 0 && suffix_names.Count == 0) {
                return cname;
            }
            string creatureName = $"{string.Join(" ", prefix_names)} {cname}";
            if (suffix_names.Count > 0)
            {
                creatureName += $" $suffix_moniker {string.Join(" ", suffix_names)}";
            }
            //Logger.LogDebug($"Setting creature name for {chara.name} to {setName}");
            return creatureName;

        }

        public static Dictionary<string, ModifierType> SelectModifiersForCreature(Character character, string creatureName, CreatureSpecificSetting creature_settings, Heightmap.Biome biome, int level, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null)
        {
            Dictionary<string, ModifierType> creatureModifiers = new Dictionary<string, ModifierType>();

            if (!character.IsPlayer())
            {
                if (character.IsBoss() && ValConfig.EnableBossModifiers.Value == true)
                {
                    int numBossMods = ValConfig.MaxMajorModifiersPerCreature.Value;
                    if (creature_settings != null && creature_settings.MaxBossModifiers > -1) { numBossMods = creature_settings.MaxBossModifiers; }
                    float chanceForBossMod = ValConfig.ChanceOfBossModifier.Value;
                    if (creature_settings != null && creature_settings.ChanceForBossModifier > -1f) { chanceForBossMod = creature_settings.ChanceForBossModifier; }
                    creatureModifiers = CreatureModifiers.SelectModifiers(character, creatureName, biome, level, isBoss: true, maxBossMods: numBossMods, chanceBossMods: chanceForBossMod, requiredModifiers: requiredModifiers, notAllowedModifiers: notAllowedModifiers);
                }
                else
                {
                    //Logger.LogDebug("Setting up creature modifiers");
                    int majorMods = ValConfig.MaxMajorModifiersPerCreature.Value;
                    if (creature_settings != null && creature_settings.MaxMajorModifiers > -1) { majorMods = creature_settings.MaxMajorModifiers; }
                    int minorMods = ValConfig.MaxMinorModifiersPerCreature.Value;
                    if (creature_settings != null && creature_settings.MaxMinorModifiers > -1) { minorMods = creature_settings.MaxMinorModifiers; }
                    float chanceMajorMod = ValConfig.ChanceMajorModifier.Value;
                    if (creature_settings != null && creature_settings.ChanceForMajorModifier > -1f) { chanceMajorMod = creature_settings.ChanceForMajorModifier; }
                    float chanceMinorMod = ValConfig.ChanceMinorModifier.Value;
                    if (creature_settings != null && creature_settings.ChanceForMinorModifier > -1f) { chanceMinorMod = creature_settings.ChanceForMinorModifier; }
                    // Logger.LogDebug($"Setting up to {majorMods} major at chance {chanceMajorMod} and {minorMods} minor modifiers with chances {chanceMinorMod}");
                    creatureModifiers = CreatureModifiers.SelectModifiers(character, creatureName, biome, level, maxMajorMods: majorMods, maxMinorMods: minorMods, chanceMajorMods: chanceMajorMod, chanceMinorMods: chanceMinorMod, notAllowedModifiers: notAllowedModifiers, requiredModifiers: requiredModifiers);
                }
            }

            return creatureModifiers;
        }

        public static Dictionary<string, ModifierType> SelectModifiers(Character character, string creatureName, Heightmap.Biome biome, int level, int maxMajorMods = 0, float chanceMajorMods = 1f, int maxMinorMods = 0, float chanceMinorMods = 1f, bool isBoss = false, int maxBossMods = 0, float chanceBossMods = 1f, Dictionary<string, ModifierType> requiredModifiers = null, List<string> notAllowedModifiers = null) {
            //Logger.LogDebug($"{character} - {creatureName} - {biome} - {level} - majmax: {maxMajorMods} - majchance: {chanceMajorMods} - maxminor: {maxMinorMods} - {isBoss} - maxboss: {maxBossMods} - chanceboss: {chanceBossMods} - reqmods: {requiredModifiers} - unallowedmods: {notAllowedModifiers}");

            Dictionary<string, ModifierType> selectedMods = new Dictionary<string, ModifierType>();

            //Logger.LogDebug($"Check if creature {creatureName} is in the modifier ignored list [{string.Join(",", CreatureModifiersData.ActiveCreatureModifiers.ModifierGlobalSettings.GlobalIgnorePrefabList)}].");
            if (creatureName != null && CreatureModifiersData.ActiveCreatureModifiers.ModifierGlobalSettings != null && CreatureModifiersData.ActiveCreatureModifiers.ModifierGlobalSettings.GlobalIgnorePrefabList != null && CreatureModifiersData.ActiveCreatureModifiers.ModifierGlobalSettings.GlobalIgnorePrefabList.Contains(character.name))
            {
                Logger.LogDebug($"Creature {creatureName} is in the global ignore prefab list, skipping modifier assignment.");
                if (!selectedMods.ContainsKey(NoMods)) { selectedMods.Add(NoMods, ModifierType.Minor); }
                return selectedMods;
            }

            if (requiredModifiers == null) { requiredModifiers = new Dictionary<string, ModifierType>(); }

            List<string> requiredBossMods = new List<string>();
            List<string> requiredMajorMods = new List<string>();
            List<string> requiredMinorMods = new List<string>();
            foreach(KeyValuePair<string, ModifierType> reqmods in requiredModifiers)
            {
                switch (reqmods.Value)
                {
                    case ModifierType.Minor:
                        requiredMinorMods.Add(reqmods.Key);
                        break;
                    case ModifierType.Major:
                        requiredMajorMods.Add(reqmods.Key);
                        break;
                    case ModifierType.Boss:
                        requiredBossMods.Add(reqmods.Key);
                        break;
                }
            }

            if (isBoss) {
                List<string> bossMods = SelectCreatureModifiers(creatureName, biome, chanceBossMods, maxBossMods, level, 0, ModifierType.Boss, requiredBossMods, notAllowedModifiers);
                foreach (var mod in bossMods) {
                    if (!selectedMods.ContainsKey(mod)) { selectedMods.Add(mod.ToString(), ModifierType.Boss); }
                }
                return selectedMods;
            }

            // Select a major modifiers
            List<string> majorMods = SelectCreatureModifiers(creatureName, biome, chanceMajorMods, maxMajorMods, level, 0, ModifierType.Major, requiredMajorMods, notAllowedModifiers);
            foreach (var mod in majorMods) {
                if (!selectedMods.ContainsKey(mod)) { selectedMods.Add(mod.ToString(), ModifierType.Major); }
            }

            List<string> minorMods = SelectCreatureModifiers(creatureName, biome, chanceMinorMods, maxMinorMods, level, majorMods.Count, ModifierType.Minor, requiredMinorMods, notAllowedModifiers);
            foreach (var mod in minorMods) {
                if (!selectedMods.ContainsKey(mod)) { selectedMods.Add(mod.ToString(), ModifierType.Minor); }
            }
            return selectedMods;
        }

        public static List<string> SelectCreatureModifiers(string creature, Heightmap.Biome biome, float chance, int num_mods, int level, int existingMods = 0, ModifierType type = ModifierType.Major, List<string> requiredMods = null, List<string> notAllowedModifiers = null)
        {
            List<string> selectedModifiers = new List<string>();
            List<string> avoidedModifiers = new List<string>();
            if (requiredMods != null) { selectedModifiers.AddRange(requiredMods); }

            //Logger.LogDebug("Checking probability cache for creature");
            List<ProbabilityEntry> probabilities = CreatureModifiersData.LazyCacheCreatureModifierSelect(creature, biome, type);
            //if (probabilities.Count > 0)
            //{
            //    foreach (ProbabilityEntry prob in probabilities)
            //    {
            //        Logger.LogDebug($"Found modifier {prob.Name} with weight {prob.SelectionWeight} for creature {creature} of type {type}");
            //    }
            //}

            if (probabilities.Count == 0) {
                //Logger.LogDebug($"No modifiers found for creature {creature} of type {type}");
                return selectedModifiers;
            }
            if (notAllowedModifiers != null)
            {
                avoidedModifiers.AddRange(notAllowedModifiers);
            }
            
            int mod_attemps = 0;
            if (requiredMods != null) { mod_attemps += requiredMods.Count; }
            //Logger.LogDebug($"Selecting {num_mods} modifiers, limited by level? {ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value} level:{level - 1}");
            while (num_mods > mod_attemps) {
                if (ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value == true && mod_attemps + 1 + existingMods >= level) { break; }
                if (chance < 1) {
                    float roll = UnityEngine.Random.value;
                    //Logger.LogDebug($"Rolling Chance {roll} < {chance}");
                    if (roll < chance) {
                        string mod = RandomSelect.RandomSelectFromWeightedListWithExclusions(probabilities, avoidedModifiers);
                        selectedModifiers.Add(mod);
                        avoidedModifiers.Add(mod);
                    }
                } else {
                    string mod = RandomSelect.RandomSelectFromWeightedListWithExclusions(probabilities, avoidedModifiers);
                    selectedModifiers.Add(mod);
                    avoidedModifiers.Add(mod);
                }
                mod_attemps++;
            }
            //Logger.LogDebug($"Selected {selectedModifiers.Count} modifiers {string.Join(",", selectedModifiers)} for creature {creature} of type {type} with chance {chance} limited by star level? {ValConfig.LimitCreatureModifiersToCreatureStarLevel.Value} level:{level - 1}");
            if (selectedModifiers.Count == 0) { selectedModifiers.Add(NoMods); }
            return selectedModifiers;
        }

        public static void RemoveCreatureModifier(Character character, string modifier) {
            Dictionary<string, ModifierType> characterMods = CompositeLazyCache.GetCreatureModifiers(character);
            if (characterMods.Keys.Contains(modifier))
            {
                Logger.LogDebug($"{character.name} has {modifier}, removing.");
                if (characterMods.Remove(modifier)) {
                    CompositeLazyCache.SetCreatureModifiers(character, characterMods);
                }
                LevelUI.InvalidateCacheEntry(character);
            }
        }

        public static bool AddCreatureModifier(Character character, ModifierType modType, string newModifier, bool applyChanges = true)
        {
            if (newModifier == NoMods || newModifier == string.Empty) {
                Logger.LogDebug($"No modifier specified to add to {character.name}, skipping.");
                return false;
            }
            // Select major and minor based on creature whole config
            Dictionary<string, ModifierType> savedMods = CompositeLazyCache.GetCreatureModifiers(character);

            if (savedMods.Count > 0 && savedMods.ContainsKey(newModifier)) {
                Logger.LogDebug($"{character.name} already has {newModifier}, skipping.");
                return false;
            }
            // Select a major modifiers
            savedMods.Add(newModifier, modType);
            CompositeLazyCache.SetCreatureModifiers(character, savedMods);
            //Logger.LogDebug($"Adding Modifier to ZDO.");
            CharacterCacheEntry scd = CompositeLazyCache.GetCacheEntry(character);

            CreatureModifierConfiguration selectedMod = CreatureModifiersData.GetModifierDef(newModifier, modType);
            //Logger.LogDebug($"Setting up modifier.");
            CreatureModifiersData.ModifierDefinitions[newModifier].SetupMethodCall(character, selectedMod.Config, scd);
            CreatureModifiersData.ModifierDefinitions[newModifier].RunOnceMethodCall(character, selectedMod.Config, scd);
            SetupCreatureVFX(character, CreatureModifiersData.ModifierDefinitions[newModifier]);

            // Note the character name needs to be rerolled
            LevelUI.InvalidateCacheEntry(character);

            // Not applying the update immediately
            if (applyChanges == false) {
                ModificationExtensionSystem.ApplySpeedModifications(character, scd);
                ModificationExtensionSystem.ApplyDamageModification(character, scd);
                ModificationExtensionSystem.LoadApplySizeModifications(character.gameObject, character.m_nview, scd, true);
                ModificationExtensionSystem.ApplyHealthModifications(character, scd);
                return true;
            }

            return true;
        }

    }
}
