using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.modules
{
    public static class CreatureModifiers
    {
        static readonly List<NameSelectionStyle> prefixSelectors = new List<NameSelectionStyle>() {
            NameSelectionStyle.RandomFirst,
            NameSelectionStyle.RandomBoth
        };
        static readonly List<NameSelectionStyle> suffixSelectors = new List<NameSelectionStyle>() {
            NameSelectionStyle.RandomLast,
            NameSelectionStyle.RandomBoth
        };

        public static Dictionary<ModifierNames, ModifierType> SetupBossModifiers(Character character, CreatureDetailCache cacheEntry, int max_mods, float chance) {
            Dictionary<ModifierNames, ModifierType> mods = new Dictionary<ModifierNames, ModifierType>();
            foreach (ModifierNames mod in SelectOrLoadModifiers(character, cacheEntry, max_mods, chance, ModifierType.Boss)) {
                if (mod == ModifierNames.None) { continue; }
                if (!CreatureModifiersData.ActiveCreatureModifiers.BossModifiers.ContainsKey(mod)) {
                    Logger.LogWarning($"Major modifier {mod} not found in CreatureModifiersData, skipping setup for {character.name}");
                    continue;
                }
                mods.Add(mod, ModifierType.Boss);
                cacheEntry.Modifiers = mods;
                var selectedMod = CreatureModifiersData.ActiveCreatureModifiers.BossModifiers[mod];
                selectedMod.LoadAndSetGameObjects();
                selectedMod.SetupMethodCall(character, selectedMod.Config, cacheEntry);
                SetupCreatureVFX(character, selectedMod);
                if (selectedMod.NamePrefixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                    cacheEntry.ModifierPrefixNames.Add(mod, selectedMod.NamePrefixes);
                }
                if (selectedMod.NameSuffixes != null && suffixSelectors.Contains(selectedMod.namingConvention)) {
                    cacheEntry.ModifierSuffixNames.Add(mod, selectedMod.NameSuffixes);
                }
            }
            return mods;
        }

        public static Dictionary<ModifierNames, ModifierType> SetupModifiers(Character character, CreatureDetailCache cacheEntry, int num_major_mods, int num_minor_mods, float chanceMajor, float chanceMinor) {
            Dictionary<ModifierNames, ModifierType> mods = new Dictionary<ModifierNames, ModifierType>();
            if (num_major_mods > 0) {
                foreach (ModifierNames mod in SelectOrLoadModifiers(character, cacheEntry, num_major_mods, chanceMajor, ModifierType.Major)) {
                    if (mod == ModifierNames.None) { continue; }
                    // Logger.LogDebug($"Setting up major modifier {mod} for character {character.name}");
                    if (!CreatureModifiersData.ActiveCreatureModifiers.MajorModifiers.ContainsKey(mod)) {
                        Logger.LogWarning($"Major modifier {mod} not found in CreatureModifiersData, skipping setup for {character.name}");
                        continue;
                    }
                    mods.Add(mod, ModifierType.Major);
                    cacheEntry.Modifiers = mods;
                    var selectedMod = CreatureModifiersData.ActiveCreatureModifiers.MajorModifiers[mod];
                    selectedMod.SetupMethodCall(character, selectedMod.Config, cacheEntry);
                    SetupCreatureVFX(character, selectedMod);
                    if (selectedMod.NamePrefixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                        cacheEntry.ModifierPrefixNames.Add(mod, selectedMod.NamePrefixes);
                    }
                    if (selectedMod.NameSuffixes != null && suffixSelectors.Contains(selectedMod.namingConvention)) {
                        cacheEntry.ModifierSuffixNames.Add(mod, selectedMod.NameSuffixes);
                    }
                }
            }
            if (num_minor_mods > 0) {
                foreach (ModifierNames mod in SelectOrLoadModifiers(character, cacheEntry, num_minor_mods, chanceMinor, ModifierType.Minor)) {
                    //Logger.LogDebug($"Setting up minor modifier {mod} for character {character.name}");
                    if (!CreatureModifiersData.ActiveCreatureModifiers.MinorModifiers.ContainsKey(mod)) {
                        if (mod == ModifierNames.None) { continue; }
                        Logger.LogWarning($"Minor modifier {mod} not found in CreatureModifiersData, skipping setup for {character.name}");
                        continue;
                    }
                    mods.Add(mod, ModifierType.Minor);
                    cacheEntry.Modifiers = mods;
                    //Logger.LogDebug($"Checking {CreatureModifiersData.CreatureModifiers.MinorModifiers.Count} for {mod}");
                    var selectedMod = CreatureModifiersData.ActiveCreatureModifiers.MinorModifiers[mod];
                    //Logger.LogDebug($"Setting up mod");
                    selectedMod.SetupMethodCall(character, selectedMod.Config, cacheEntry);
                    //Logger.LogDebug($"Setting up mod vfx");
                    SetupCreatureVFX(character, selectedMod);
                    //Logger.LogDebug($"Setting updating name prefixes");
                    if (selectedMod.NamePrefixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                        cacheEntry.ModifierPrefixNames.Add(mod, selectedMod.NamePrefixes);
                    }
                    //Logger.LogDebug($"Setting updating name postfixes");
                    if (selectedMod.NameSuffixes != null && suffixSelectors.Contains(selectedMod.namingConvention)) {
                        cacheEntry.ModifierSuffixNames.Add(mod, selectedMod.NameSuffixes);
                    }
                }
            }
            return mods;
        }

        internal static void SetupCreatureVFX(Character character, CreatureModifier cmodifier) {
            if (cmodifier.VisualEffect != null) {

                GameObject effectPrefab = CreatureModifiersData.LoadedModifierEffects[cmodifier.VisualEffect];
                bool hasVFXAlready = character.transform.Find($"{effectPrefab.name}(Clone)");
                Logger.LogDebug($"Setting up visual effect for {character.name} {character.GetZDOID().ID} - {hasVFXAlready}");
                if (hasVFXAlready == false) {
                    Logger.LogDebug($"Adding visual effects for {character.name}");
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

        internal static string CheckOrBuildCreatureName(Character chara, CreatureDetailCache cacheEntry, bool useChache = true) {
            // Skip if the creature is getting deleted
            if (cacheEntry.CreatureDisabledInBiome == true || cacheEntry == null) { return Localization.instance.Localize(chara.m_name); }
            string setName = chara.m_nview.GetZDO().GetString("SLE_Name");
            if (setName == "" || useChache == false) {
                List<string> prefix_names = new List<string>();
                List<string> suffix_names = new List<string>();
                int nameEntries = 0;
                List<ModifierNames> remainingNameSegments = cacheEntry.Modifiers.Keys.ToList();
                while (nameEntries < cacheEntry.Modifiers.Count) {
                    if (remainingNameSegments.Count == 0) { break; }
                    // Try selecting a prefix name
                    if (cacheEntry.ModifierPrefixNames != null && cacheEntry.ModifierPrefixNames.Count > 0) {
                        KeyValuePair<ModifierNames, List<string>> selected = Extensions.RandomEntry(cacheEntry.ModifierPrefixNames);
                        // Remove this modifier from future selection lists
                        remainingNameSegments.Remove(selected.Key);
                        cacheEntry.ModifierPrefixNames.Remove(selected.Key);
                        cacheEntry.ModifierSuffixNames.Remove(selected.Key);
                        // Randomly select one of the prefix entries for this modifier
                        prefix_names.Add(selected.Value[Random.Range(0, selected.Value.Count - 1)]);
                    }
                    if (cacheEntry.ModifierSuffixNames != null && cacheEntry.ModifierSuffixNames.Count > 0) {
                        KeyValuePair<ModifierNames, List<string>> selected = Extensions.RandomEntry(cacheEntry.ModifierSuffixNames);
                        remainingNameSegments.Remove(selected.Key);
                        cacheEntry.ModifierPrefixNames.Remove(selected.Key);
                        cacheEntry.ModifierSuffixNames.Remove(selected.Key);
                        suffix_names.Add(selected.Value[Random.Range(0, selected.Value.Count - 1)]);
                    }
                    nameEntries++;
                }

                

                Tameable component = chara.GetComponent<Tameable>();
                string cname = chara.m_name;
                if ((bool)component) {
                    cname = component.GetHoverName();
                }
                setName = $"{string.Join(" ", prefix_names)} {cname} {string.Join(" ", suffix_names)}";
                chara.m_nview.GetZDO().Set("SLE_Name", setName);
                Logger.LogDebug($"Setting creature name for {chara.name} to {setName}");
                return Localization.instance.Localize(setName.Trim());
            }
            //Logger.LogDebug($"Loaded creature name for {chara.name} to {setName}");
            return Localization.instance.Localize(setName);
        }

        public static List<ModifierNames> SelectOrLoadModifiers(Character character, CreatureDetailCache cdc, int num_mods, float chanceForMod, ModifierType modType = ModifierType.Major) {
            // Select major and minor based on creature whole config
            ListModifierZNetProperty characterMods = new ListModifierZNetProperty($"SLS_{modType}_MODS", character.m_nview, new List<ModifierNames>() { });
            List<ModifierNames> savedMods = characterMods.Get();
            if (savedMods.Count > 0) {
                // Logger.LogDebug($"Loaded {savedMods.Count} {modType} for {character.name}");
                return savedMods;
            }
            // Select a major modifiers
            List<ModifierNames> modifiers = SelectCreatureModifiers(Utils.GetPrefabName(character.gameObject), cdc.Biome, chanceForMod, num_mods, cdc.Level, modType);
            characterMods.Set(modifiers);
            return modifiers;
        }

        public static List<ModifierNames> SelectCreatureModifiers(string creature, Heightmap.Biome biome, float chance, int num_mods, int level, ModifierType type = ModifierType.Major)
        {
            List<ModifierNames> selectedModifiers = new List<ModifierNames>();
            List<ProbabilityEntry> probabilities = CreatureModifiersData.LazyCacheCreatureModifierSelect(creature, biome, type);
            if (probabilities.Count == 0) {
                // Logger.LogDebug($"No modifiers found for creature {creature} of type {type}");
                return selectedModifiers;
            }
            int mod_attemps = 0;
            while (num_mods > mod_attemps) {
                if (mod_attemps + 1 >= level) { break; }
                if (chance < 1) {
                    float roll = UnityEngine.Random.value;
                    // Logger.LogDebug($"Rolling Chance {roll} < {chance}");
                    if (roll < chance) {
                        selectedModifiers.Add(RandomSelect.RandomSelectFromWeightedList(probabilities));
                    }
                } else {
                    selectedModifiers.Add(RandomSelect.RandomSelectFromWeightedList(probabilities));
                }
                mod_attemps++;
            }
            // Logger.LogDebug($"Selected {selectedModifiers.Count} modifiers for creature {creature} of type {type} with chance {chance}");
            if (selectedModifiers.Count == 0) { selectedModifiers.Add(ModifierNames.None); }
            return selectedModifiers;
        }

        public static void AddCreatureModifier(Character character, ModifierType modType, ModifierNames newModifier)
        {
            // Select major and minor based on creature whole config
            ListModifierZNetProperty characterMods = new ListModifierZNetProperty($"SLS_{modType}_MODS", character.m_nview, new List<ModifierNames>() { });
            List<ModifierNames> savedMods = characterMods.Get();
            if (savedMods.Count > 0 && savedMods.Contains(newModifier)) {
                Logger.LogDebug($"{character.name} already has {newModifier}, skipping.");
                return;
            }
            // Select a major modifiers
            savedMods.Add(newModifier);
            characterMods.Set(savedMods);
            Logger.LogDebug($"Adding Modifier to ZDO.");
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(character);

            var selectedMod = CreatureModifiersData.GetModifierDef(newModifier, modType);
            Logger.LogDebug($"Setting up modifier.");
            selectedMod.SetupMethodCall(character, selectedMod.Config, cdc);
            SetupCreatureVFX(character, selectedMod);

            // Name monikers
            Logger.LogDebug($"Updating naming monikers.");
            if (selectedMod.NamePrefixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                Logger.LogDebug($"Adding prefix names.");
                if (!cdc.ModifierPrefixNames.ContainsKey(newModifier)) {
                    cdc.ModifierPrefixNames.Add(newModifier, selectedMod.NamePrefixes);
                }
            }
            if (selectedMod.NameSuffixes != null && suffixSelectors.Contains(selectedMod.namingConvention)) {
                Logger.LogDebug($"Adding suffix names.");
                if (!cdc.ModifierSuffixNames.ContainsKey(newModifier)) {
                    cdc.ModifierSuffixNames.Add(newModifier, selectedMod.NameSuffixes);
                }
            }
            Logger.LogDebug($"Updating character cache entry.");
            cdc.Modifiers.Add(newModifier, modType);
            // Update the existing cache entry with our new modifier for the creature
            CompositeLazyCache.UpdateCacheEntry(character, cdc);
            // Forces a rebuild of this characters UI to include possible new star icons or name changes
            Logger.LogDebug($"Rebuilding Character UI");
            CheckOrBuildCreatureName(character, cdc, false);
            LevelUI.InvalidateCacheEntry(character.GetZDOID());
        }

    }
}
