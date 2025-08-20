using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

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

        public static Dictionary<string, ModifierType> SetupModifiers(Character character, CreatureDetailCache cacheEntry, int num_major_mods, int num_minor_mods, float chanceMajor, float chanceMinor) {
            Dictionary<string, ModifierType> mods = new Dictionary<string, ModifierType>();
            if (num_major_mods > 0) {
                foreach (string mod in SelectOrLoadModifiers(character, num_major_mods, chanceMajor, ModifierType.Major)) {
                    if (mod == "none") { continue; }
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
                foreach (string mod in SelectOrLoadModifiers(character, num_minor_mods, chanceMinor, ModifierType.Minor)) {
                    //Logger.LogDebug($"Setting up minor modifier {mod} for character {character.name}");
                    if (!CreatureModifiersData.CreatureModifiers.MinorModifiers.ContainsKey(mod)) {
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
                bool hasVFXAlready = character.transform.Find(cmodifier.visualEffectPrefab.name);
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

        internal static string CheckOrBuildCreatureName(Character chara, CreatureDetailCache cacheEntry)
        {
            string setName = chara.m_nview.GetZDO().GetString("SLE_Name");
            if (setName == "") {
                string prefix = "";
                string prefixFromMod = "";
                if (cacheEntry.ModifierPrefixNames.Count > 0) {
                    KeyValuePair<string, List<string>> selected = Extensions.RandomEntry(cacheEntry.ModifierPrefixNames);
                    prefixFromMod = selected.Key;
                    prefix = selected.Value[UnityEngine.Random.Range(0, selected.Value.Count - 1)];
                }
                string suffix = "";
                if (cacheEntry.ModifierSuffixNames.Count > 0) {
                    KeyValuePair<string, List<string>> selected;
                    if (prefixFromMod != "") {
                        selected = Extensions.RandomEntry(cacheEntry.ModifierSuffixNames, new List<string>() { prefixFromMod });
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

        public static List<string> SelectOrLoadModifiers(Character character, int num_mods, float chanceForMod, ModifierType modType = ModifierType.Major) {
            // Select major and minor based on creature whole config
            ListStringZNetProperty characterMods = new ListStringZNetProperty($"SLS_{modType}_Mods", character.m_nview, new List<string>() { });
            List<string> savedMods = characterMods.Get();
            if (savedMods.Count > 0) {
                Logger.LogDebug($"Loaded {savedMods.Count} {modType} for {character.name}");
                return savedMods;
            }
            // Select a major modifiers
            List<string> modifiers = SelectCreatureModifiers(Utils.GetPrefabName(character.gameObject), chanceForMod, num_mods, modType);
            characterMods.Set(modifiers);
            return modifiers;
        }

        public static List<string> SelectCreatureModifiers(string creature, float chance, int num_mods, ModifierType type = ModifierType.Major)
        {
            List<string> selectedModifiers = new List<string>();
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
            if (selectedModifiers.Count == 0) { selectedModifiers.Add("none"); }
            return selectedModifiers;
        }

    }
}
