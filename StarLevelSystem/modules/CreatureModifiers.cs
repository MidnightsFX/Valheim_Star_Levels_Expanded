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
            foreach (ModifierNames mod in SelectOrLoadModifiers(character, max_mods, chance, ModifierType.Boss)) {
                if (mod == ModifierNames.None) { continue; }
                if (!CreatureModifiersData.CreatureModifiers.BossModifiers.ContainsKey(mod)) {
                    Logger.LogWarning($"Major modifier {mod} not found in CreatureModifiersData, skipping setup for {character.name}");
                    continue;
                }
                mods.Add(mod, ModifierType.Boss);
                var selectedMod = CreatureModifiersData.CreatureModifiers.BossModifiers[mod];
                selectedMod.SetupMethodCall(character, selectedMod.config, cacheEntry);
                SetupCreatureVFX(character, selectedMod);
                if (selectedMod.name_prefixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                    cacheEntry.ModifierPrefixNames.Add(mod, selectedMod.name_prefixes);
                }
                if (selectedMod.name_suffixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                    cacheEntry.ModifierSuffixNames.Add(mod, selectedMod.name_suffixes);
                }
            }
            return mods;
        }

        public static Dictionary<ModifierNames, ModifierType> SetupModifiers(Character character, CreatureDetailCache cacheEntry, int num_major_mods, int num_minor_mods, float chanceMajor, float chanceMinor) {
            Dictionary<ModifierNames, ModifierType> mods = new Dictionary<ModifierNames, ModifierType>();
            if (num_major_mods > 0) {
                foreach (ModifierNames mod in SelectOrLoadModifiers(character, num_major_mods, chanceMajor, ModifierType.Major)) {
                    if (mod == ModifierNames.None) { continue; }
                    Logger.LogDebug($"Setting up major modifier {mod} for character {character.name}");
                    if (!CreatureModifiersData.CreatureModifiers.MajorModifiers.ContainsKey(mod)) {
                        Logger.LogWarning($"Major modifier {mod} not found in CreatureModifiersData, skipping setup for {character.name}");
                        continue;
                    }
                    mods.Add(mod, ModifierType.Major);
                    var selectedMod = CreatureModifiersData.CreatureModifiers.MajorModifiers[mod];
                    selectedMod.SetupMethodCall(character, selectedMod.config, cacheEntry);
                    SetupCreatureVFX(character, selectedMod);
                    if (selectedMod.name_prefixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                        cacheEntry.ModifierPrefixNames.Add(mod, selectedMod.name_prefixes);
                    }
                    if (selectedMod.name_suffixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                        cacheEntry.ModifierSuffixNames.Add(mod, selectedMod.name_suffixes);
                    }
                }
            }
            if (num_minor_mods > 0) {
                foreach (ModifierNames mod in SelectOrLoadModifiers(character, num_minor_mods, chanceMinor, ModifierType.Minor)) {
                    //Logger.LogDebug($"Setting up minor modifier {mod} for character {character.name}");
                    if (!CreatureModifiersData.CreatureModifiers.MinorModifiers.ContainsKey(mod)) {
                        if (mod == ModifierNames.None) { continue; }
                        Logger.LogWarning($"Minor modifier {mod} not found in CreatureModifiersData, skipping setup for {character.name}");
                        continue;
                    }
                    mods.Add(mod, ModifierType.Minor);
                    //Logger.LogDebug($"Checking {CreatureModifiersData.CreatureModifiers.MinorModifiers.Count} for {mod}");
                    var selectedMod = CreatureModifiersData.CreatureModifiers.MinorModifiers[mod];
                    //Logger.LogDebug($"Setting up mod");
                    selectedMod.SetupMethodCall(character, selectedMod.config, cacheEntry);
                    //Logger.LogDebug($"Setting up mod vfx");
                    SetupCreatureVFX(character, selectedMod);
                    //Logger.LogDebug($"Setting updating name prefixes");
                    if (selectedMod.name_prefixes != null && prefixSelectors.Contains(selectedMod.namingConvention)) {
                        cacheEntry.ModifierPrefixNames.Add(mod, selectedMod.name_prefixes);
                    }
                    //Logger.LogDebug($"Setting updating name postfixes");
                    if (selectedMod.name_suffixes != null && suffixSelectors.Contains(selectedMod.namingConvention)) {
                        cacheEntry.ModifierSuffixNames.Add(mod, selectedMod.name_suffixes);
                    }
                }
            }
            return mods;
        }

        internal static void SetupCreatureVFX(Character character, CreatureModifier cmodifier) {
            if (cmodifier.visualEffect != null) {
                if (cmodifier.visualEffectPrefab == null) { cmodifier.LoadAndSetGameObjects(); }
                bool hasVFXAlready = character.transform.Find($"{cmodifier.visualEffectPrefab.name}(Clone)");
                Logger.LogDebug($"Setting up visual effect for {character.name} {character.GetZDOID().ID} - {hasVFXAlready}");
                if (hasVFXAlready == false) {
                    Logger.LogDebug($"Adding visual effects for {character.name}");
                    GameObject vfxadd = GameObject.Instantiate(cmodifier.visualEffectPrefab, character.transform);
                    float height = character.GetHeight();
                    float scale = height / 5f;
                    float rscale = character.GetRadius() / 2f;

                    switch (cmodifier.visualEffectStyle)
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

        internal static string CheckOrBuildCreatureName(Character chara, CreatureDetailCache cacheEntry) {
            // Skip if the creature is getting deleted
            if (cacheEntry.creatureDisabledInBiome == true) { return Localization.instance.Localize(chara.m_name); }
            string setName = chara.m_nview.GetZDO().GetString("SLE_Name");
            if (setName == "") {
                string prefix = "";
                ModifierNames prefixFromMod = ModifierNames.None;
                if (cacheEntry.ModifierPrefixNames.Count > 0) {
                    KeyValuePair<ModifierNames, List<string>> selected = Extensions.RandomEntry(cacheEntry.ModifierPrefixNames);
                    prefixFromMod = selected.Key;
                    prefix = selected.Value[UnityEngine.Random.Range(0, selected.Value.Count - 1)];
                }
                string suffix = "";
                if (cacheEntry.ModifierSuffixNames.Count > 0) {
                    KeyValuePair<ModifierNames, List<string>> selected;
                    if (prefixFromMod != ModifierNames.None) {
                        selected = Extensions.RandomEntry(cacheEntry.ModifierSuffixNames, new List<ModifierNames>() { prefixFromMod });
                    } else {
                        selected = Extensions.RandomEntry(cacheEntry.ModifierSuffixNames);
                    }
                    if (selected.Value != null) {
                        suffix = selected.Value[UnityEngine.Random.Range(0, selected.Value.Count - 1)];
                    }
                }
                Tameable component = chara.GetComponent<Tameable>();
                string cname = chara.m_name;
                if ((bool)component) {
                    cname = component.GetHoverName();
                }
                setName = $"{prefix} {cname} {suffix}";
                chara.m_nview.GetZDO().Set("SLE_Name", setName);
                //Logger.LogDebug($"Setting creature name for {chara.name} to {setName}");
                return Localization.instance.Localize(setName.Trim());
            }
            //Logger.LogDebug($"Loaded creature name for {chara.name} to {setName}");
            return Localization.instance.Localize(setName);
        }

        public static List<ModifierNames> SelectOrLoadModifiers(Character character, int num_mods, float chanceForMod, ModifierType modType = ModifierType.Major) {
            // Select major and minor based on creature whole config
            ListModifierZNetProperty characterMods = new ListModifierZNetProperty($"SLS_{modType}_MODS", character.m_nview, new List<ModifierNames>() { });
            List<ModifierNames> savedMods = characterMods.Get();
            if (savedMods.Count > 0) {
                Logger.LogDebug($"Loaded {savedMods.Count} {modType} for {character.name}");
                return savedMods;
            }
            // Select a major modifiers
            List<ModifierNames> modifiers = SelectCreatureModifiers(Utils.GetPrefabName(character.gameObject), chanceForMod, num_mods, modType);
            characterMods.Set(modifiers);
            return modifiers;
        }

        public static List<ModifierNames> SelectCreatureModifiers(string creature, float chance, int num_mods, ModifierType type = ModifierType.Major)
        {
            List<ModifierNames> selectedModifiers = new List<ModifierNames>();
            List<ProbabilityEntry> probabilities = CreatureModifiersData.LazyCacheCreatureModifierSelect(creature, type);
            if (probabilities.Count == 0) {
                Logger.LogDebug($"No modifiers found for creature {creature} of type {type}");
                return selectedModifiers;
            }
            num_mods.Times(() => {
                if (chance < 1) {
                    float roll = UnityEngine.Random.value;
                    Logger.LogDebug($"Rolling Chance {roll} < {chance}");
                    if (roll < chance) {
                        selectedModifiers.Add(RandomSelect.RandomSelectFromWeightedList(probabilities));
                    }
                } 
                else {
                    selectedModifiers.Add(RandomSelect.RandomSelectFromWeightedList(probabilities));
                }
            });
            Logger.LogDebug($"Selected {selectedModifiers.Count} modifiers for creature {creature} of type {type} with chance {chance}");
            if (selectedModifiers.Count == 0) { selectedModifiers.Add(ModifierNames.None); }
            return selectedModifiers;
        }

    }
}
