using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

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

        public static Dictionary<string, ModifierType> SetupModifiers(Character character, CreatureDetailCache cacheEntry, int num_major_mods = 0, int num_minor_mods = 0, int num_boss_mods = 0, float chanceMajor = 0f, float chanceMinor = 0f, float chanceBoss = 0f, bool isboss = false) {
            Dictionary<string, ModifierType> creatureMods = new Dictionary<string, ModifierType>();
            
            foreach (KeyValuePair<string, ModifierType> kvp in SelectOrLoadModifiers(character, cacheEntry, num_major_mods, chanceMajor, num_minor_mods, chanceMinor, isboss, num_boss_mods, chanceBoss))
            {
                switch(kvp.Value)
                {
                    case ModifierType.Boss:
                        StartupModifier(kvp.Key, kvp.Value, character, cacheEntry, creatureMods, CreatureModifiersData.ActiveCreatureModifiers.BossModifiers);
                        break;
                    case ModifierType.Major:
                        StartupModifier(kvp.Key, kvp.Value, character, cacheEntry, creatureMods, CreatureModifiersData.ActiveCreatureModifiers.MajorModifiers);
                        break;
                    case ModifierType.Minor:
                        StartupModifier(kvp.Key, kvp.Value, character, cacheEntry, creatureMods, CreatureModifiersData.ActiveCreatureModifiers.MinorModifiers);
                        break;
                }
            }
            return creatureMods;
        }

        private static void StartupModifier(string mod, ModifierType modtype, Character character, CreatureDetailCache cacheEntry, Dictionary<string, ModifierType> creaturesMods, Dictionary<string, CreatureModifier> availableMods) {
            //Logger.LogDebug($"Setting up minor modifier {mod} for character {character.name}");
            if (!availableMods.ContainsKey(mod)) {
                if (mod == NoMods) { return; }
                Logger.LogWarning($"Modifier {mod} not found in CreatureModifiersData, skipping setup for {character.name}");
                return;
            }
            creaturesMods.Add(mod, modtype);
            cacheEntry.Modifiers = creaturesMods;
            //Logger.LogDebug($"Checking {CreatureModifiersData.CreatureModifiers.MinorModifiers.Count} for {mod}");
            var selectedMod = availableMods[mod];
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
                List<string> remainingNameSegments = cacheEntry.Modifiers.Keys.ToList();
                while (nameEntries < cacheEntry.Modifiers.Count) {
                    if (remainingNameSegments.Count == 0) { break; }
                    // Try selecting a prefix name
                    if (cacheEntry.ModifierPrefixNames != null && cacheEntry.ModifierPrefixNames.Count > 0) {
                        KeyValuePair<string, List<string>> selected = Extensions.RandomEntry(cacheEntry.ModifierPrefixNames);
                        // Remove this modifier from future selection lists
                        remainingNameSegments.Remove(selected.Key);
                        cacheEntry.ModifierPrefixNames.Remove(selected.Key);
                        cacheEntry.ModifierSuffixNames.Remove(selected.Key);
                        // Randomly select one of the prefix entries for this modifier
                        prefix_names.Add(selected.Value[UnityEngine.Random.Range(0, selected.Value.Count - 1)]);
                    }
                    if (cacheEntry.ModifierSuffixNames != null && cacheEntry.ModifierSuffixNames.Count > 0) {
                        KeyValuePair<string, List<string>> selected = Extensions.RandomEntry(cacheEntry.ModifierSuffixNames);
                        remainingNameSegments.Remove(selected.Key);
                        cacheEntry.ModifierPrefixNames.Remove(selected.Key);
                        cacheEntry.ModifierSuffixNames.Remove(selected.Key);
                        suffix_names.Add(selected.Value[UnityEngine.Random.Range(0, selected.Value.Count - 1)]);
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


        public static Dictionary<string, ModifierType> SelectOrLoadModifiers(Character character, CreatureDetailCache cdc, int maxMajorMods = 0, float chanceMajorMods = 1f, int maxMinorMods = 0, float chanceMinorMods = 1f, bool isBoss = false, int maxBossMods = 0, float chanceBossMods = 1f) {
            CreatureModifiersZNetProperty updatedMods = new CreatureModifiersZNetProperty($"SLS_MODS", character.m_nview, new Dictionary<string, ModifierType>() { });
            Dictionary<string, ModifierType> savedMods = updatedMods.Get();
            if (savedMods.Count > 0) { return savedMods; }

            // Select major and minor based on creature whole config
            ListModifierZNetProperty old_majorMods = new ListModifierZNetProperty($"SLS_{ModifierType.Major}_MODS", character.m_nview, new List<ModifierNames>() { });
            ListModifierZNetProperty old_minorMods = new ListModifierZNetProperty($"SLS_{ModifierType.Minor}_MODS", character.m_nview, new List<ModifierNames>() { });
            ListModifierZNetProperty old_bossMods = new ListModifierZNetProperty($"SLS_{ModifierType.Boss}_MODS", character.m_nview, new List<ModifierNames>() { });
            List<ModifierNames> oldMajorMods = old_majorMods.Get();
            List<ModifierNames> oldMinorMods = old_minorMods.Get();
            List<ModifierNames> oldBossMods = old_bossMods.Get();
            if (oldMajorMods.Count > 0 || oldMinorMods.Count > 0 || oldBossMods.Count > 0) {
                foreach (var mod in oldMajorMods) {
                    if (!savedMods.ContainsKey(mod.ToString())) { savedMods.Add(mod.ToString(), ModifierType.Major); }
                }
                foreach (var mod in oldMinorMods) {
                    if (!savedMods.ContainsKey(mod.ToString())) { savedMods.Add(mod.ToString(), ModifierType.Minor); }
                }
                foreach (var mod in oldBossMods) {
                    if (!savedMods.ContainsKey(mod.ToString())) { savedMods.Add(mod.ToString(), ModifierType.Boss); }
                }

                old_majorMods.Set(new List<ModifierNames>() { });
                old_minorMods.Set(new List<ModifierNames>() { });
                old_bossMods.Set(new List<ModifierNames>() { });

                updatedMods.Set(savedMods);

                Logger.LogDebug($"Upgraded {savedMods.Count} for {character.name}");
                return savedMods;
            }

            if (isBoss) {
                List<string> bossMods = SelectCreatureModifiers(Utils.GetPrefabName(character.gameObject), cdc.Biome, chanceBossMods, maxBossMods, cdc.Level, ModifierType.Boss);
                foreach (var mod in bossMods) {
                    if (!savedMods.ContainsKey(mod)) { savedMods.Add(mod.ToString(), ModifierType.Boss); }
                }
                updatedMods.Set(savedMods);
                return savedMods;
            }

            // Select a major modifiers
            List<string> majorMods = SelectCreatureModifiers(Utils.GetPrefabName(character.gameObject), cdc.Biome, chanceMajorMods, maxMajorMods, cdc.Level, ModifierType.Major);
            foreach (var mod in majorMods) {
                if (!savedMods.ContainsKey(mod)) { savedMods.Add(mod.ToString(), ModifierType.Major); }
            }
            List<string> minorMods = SelectCreatureModifiers(Utils.GetPrefabName(character.gameObject), cdc.Biome, chanceMinorMods, maxMinorMods, cdc.Level, ModifierType.Minor);
            foreach (var mod in minorMods)
            {
                if (!savedMods.ContainsKey(mod)) { savedMods.Add(mod.ToString(), ModifierType.Minor); }
            }
            updatedMods.Set(savedMods);
            return savedMods;
        }

        public static List<string> SelectCreatureModifiers(string creature, Heightmap.Biome biome, float chance, int num_mods, int level, ModifierType type = ModifierType.Major)
        {
            List<string> selectedModifiers = new List<string>();
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
                        selectedModifiers.Add(RandomSelect.RandomSelectFromWeightedListWithExclusions(probabilities, selectedModifiers));
                    }
                } else {
                    selectedModifiers.Add(RandomSelect.RandomSelectFromWeightedListWithExclusions(probabilities, selectedModifiers));
                }
                mod_attemps++;
            }
            // Logger.LogDebug($"Selected {selectedModifiers.Count} modifiers for creature {creature} of type {type} with chance {chance}");
            if (selectedModifiers.Count == 0) { selectedModifiers.Add(NoMods); }
            return selectedModifiers;
        }

        public static bool AddCreatureModifier(Character character, ModifierType modType, string newModifier, bool applyChanges = true)
        {
            // Select major and minor based on creature whole config
            CreatureModifiersZNetProperty updatedMods = new CreatureModifiersZNetProperty($"SLS_MODS", character.m_nview, new Dictionary<string, ModifierType>() { });
            Dictionary<string, ModifierType> savedMods = updatedMods.Get();

            if (savedMods.Count > 0 && savedMods.ContainsKey(newModifier)) {
                Logger.LogDebug($"{character.name} already has {newModifier}, skipping.");
                return false;
            }
            // Select a major modifiers
            savedMods.Add(newModifier, modType);
            updatedMods.Set(savedMods);
            //Logger.LogDebug($"Adding Modifier to ZDO.");
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(character);

            var selectedMod = CreatureModifiersData.GetModifierDef(newModifier, modType);
            //Logger.LogDebug($"Setting up modifier.");
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

            // Not applying the update immediately
            if (applyChanges == false) {
                ModificationExtensionSystem.ApplySpeedModifications(character, cdc);
                ModificationExtensionSystem.ApplyDamageModification(character, cdc);
                ModificationExtensionSystem.LoadApplySizeModifications(character.gameObject, character.m_nview, cdc, true);
                ModificationExtensionSystem.ApplyHealthModifications(character, cdc);
                return true;
            }

            return true;
        }

    }
}
