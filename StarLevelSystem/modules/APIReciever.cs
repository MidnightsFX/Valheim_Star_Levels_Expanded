using StarLevelSystem.Data;
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
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            cdc.Level = level;
            CompositeLazyCache.UpdateCacheEntry(chara, cdc);
            LevelSystem.SetAndUpdateCharacterLevel(chara, cdc.Level);
            return true;
        }

        public static bool UpdateCreatureColorization(Character chara, float value, float hue, float sat, bool emission = false) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            cdc.Colorization = new ColorDef(hue, sat, value, emission);
            CompositeLazyCache.UpdateCacheEntry(chara, cdc);
            Colorization.ApplyColorizationWithoutLevelEffects(chara.gameObject, cdc.Colorization);
            return true;
        }

        // Base value attributes
        public static float GetBaseAttributeValue(Character chara, int attribute) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return -1f; }
            return cdc.CreatureBaseValueModifiers[(CreatureBaseAttribute)attribute];
        }

        public static bool UpdateCreatureBaseAttributes(Character chara, int attribute, float value) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            cdc.CreatureBaseValueModifiers[(CreatureBaseAttribute)attribute] = value;
            return true;
        }

        public static Dictionary<int, float> GetAllBaseAttributes(Character chara) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return null; }
            Dictionary<int, float> ret = new Dictionary<int, float>();
            foreach (var kvp in cdc.CreatureBaseValueModifiers) {
                ret[(int)kvp.Key] = kvp.Value;
            }
            return ret;
        }

        public static bool SetAllBaseAttributes(Character chara, Dictionary<int, float> attributes) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            foreach (var kvp in attributes) {
                cdc.CreatureBaseValueModifiers[(CreatureBaseAttribute)kvp.Key] = kvp.Value;
            }
            CompositeLazyCache.UpdateCacheEntry(chara, cdc);
            ModificationExtensionSystem.ApplySpeedModifications(chara, cdc);
            ModificationExtensionSystem.ApplyDamageModification(chara, cdc);
            ModificationExtensionSystem.LoadApplySizeModifications(chara.gameObject, chara.m_nview, cdc, true);
            ModificationExtensionSystem.ApplyHealthModifications(chara, cdc);
            return true;
        }

        // Per level attributes
        public static float GetPerLevelAttributeValue(Character chara, int attribute) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return -1f; }
            return cdc.CreaturePerLevelValueModifiers[(CreaturePerLevelAttribute)attribute];
        }

        public static bool UpdateCreaturePerLevelAttributes(Character chara, int attribute, float value) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            cdc.CreaturePerLevelValueModifiers[(CreaturePerLevelAttribute)attribute] = value;
            return true;
        }

        public static Dictionary<int, float> GetAllPerLevelAttributes(Character chara) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return null; }
            Dictionary<int, float> ret = new Dictionary<int, float>();
            foreach (var kvp in cdc.CreaturePerLevelValueModifiers)
            {
                ret[(int)kvp.Key] = kvp.Value;
            }
            return ret;
        }

        public static bool SetAllPerLevelAttributes(Character chara, Dictionary<int, float> attributes) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            foreach (var kvp in attributes) {
                cdc.CreaturePerLevelValueModifiers[(CreaturePerLevelAttribute)kvp.Key] = kvp.Value;
            }
            CompositeLazyCache.UpdateCacheEntry(chara, cdc);
            ModificationExtensionSystem.ApplySpeedModifications(chara, cdc);
            ModificationExtensionSystem.ApplyDamageModification(chara, cdc);
            ModificationExtensionSystem.LoadApplySizeModifications(chara.gameObject, chara.m_nview, cdc, true);
            ModificationExtensionSystem.ApplyHealthModifications(chara, cdc);
            return true;
        }

        // Creature damage recived modifiers
        public static float GetCreatureDamageRecievedModifier(Character chara, int attribute) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return -1f; }
            return cdc.DamageRecievedModifiers[(DamageType)attribute];
        }

        public static bool UpdateCreatureDamageRecievedModifier(Character chara, int attribute, float value) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            cdc.DamageRecievedModifiers[(DamageType)attribute] = value;
            return true;
        }

        public static Dictionary<int, float> GetAllDamageRecievedModifiers(Character chara) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return null; }
            Dictionary<int, float> ret = new Dictionary<int, float>();
            foreach (var kvp in cdc.DamageRecievedModifiers) {
                ret[(int)kvp.Key] = kvp.Value;
            }
            return ret;
        }

        public static bool SetAllDamageRecievedModifiers(Character chara, Dictionary<int, float> attributes) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            foreach (var kvp in attributes) {
                cdc.DamageRecievedModifiers[(DamageType)kvp.Key] = kvp.Value;
            }
            CompositeLazyCache.UpdateCacheEntry(chara, cdc);
            ModificationExtensionSystem.ApplyDamageModification(chara, cdc);
            return true;
        }

        // Creature bonus damage modifiers

        public static float GetCreatureDamageBonus(Character chara, int attribute) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return -1f; }
            if (cdc.CreatureDamageBonus.ContainsKey((DamageType)attribute)) {
                return cdc.CreatureDamageBonus[(DamageType)attribute];
            }
            return 0f;
        }

        public static bool UpdateCreatureDamageBonus(Character chara, int attribute, float value) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            if (!cdc.CreatureDamageBonus.ContainsKey((DamageType)attribute)) {
                cdc.CreatureDamageBonus[(DamageType)attribute] = value;
            } else {
                cdc.CreatureDamageBonus.Add((DamageType)attribute, value);
            }
            return true;
        }

        public static Dictionary<int, float> GetAllDamageBonus(Character chara) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return null; }
            Dictionary<int, float> ret = new Dictionary<int, float>();
            foreach (var kvp in cdc.CreatureDamageBonus) {
                ret[(int)kvp.Key] = kvp.Value;
            }
            return ret;
        }

        public static bool SetAllDamageBonus(Character chara, Dictionary<int, float> attributes) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            foreach (var kvp in attributes) {
                if (!cdc.CreatureDamageBonus.ContainsKey((DamageType)kvp.Key)) {
                    cdc.CreatureDamageBonus[(DamageType)kvp.Key] = kvp.Value;
                } else {
                    cdc.CreatureDamageBonus.Add((DamageType)kvp.Key, kvp.Value);
                }
            }
            CompositeLazyCache.UpdateCacheEntry(chara, cdc);
            ModificationExtensionSystem.ApplyDamageModification(chara, cdc);
            return true;
        }

        // Applies all changes made to attributes to the creature

        public static bool ApplyUpdatesToCreature(Character chara) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            ModificationExtensionSystem.ApplySpeedModifications(chara, cdc);
            ModificationExtensionSystem.ApplyDamageModification(chara, cdc);
            ModificationExtensionSystem.LoadApplySizeModifications(chara.gameObject, chara.m_nview, cdc, true);
            ModificationExtensionSystem.ApplyHealthModifications(chara, cdc);
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
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return null; }
            Dictionary<string, int> modifiersAndType = new Dictionary<string, int>();
            foreach (var mod in cdc.Modifiers) {
                modifiersAndType.Add(mod.Key.ToString(), (int)mod.Value);
            }
            return modifiersAndType;
        }

        public static bool AddModifierToCreature(Character chara, string modifierName, int modifierType, bool update = true) {
            CreatureDetailCache cdc = CompositeLazyCache.GetAndSetDetailCache(chara);
            if (cdc == null) { return false; }
            return CreatureModifiers.AddCreatureModifier(chara, (ModifierType)modifierType, modifierName, update);
        }

        public static bool AddNewModifierToSLS(
            int modifierID,
            string modifier_name,
            string setupMethod = null,
            float selectionWeight = 10f,
            float basepower = 0f,
            float perlevelpower = 0f,
            Dictionary<Heightmap.Biome, List<string>> biomeConfig = null,
            int namingStyle = 2,
            List<string> name_suffixes = null,
            List<string> name_prefixes = null,
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

            CreatureModifier newMod = new CreatureModifier();

            newMod.SelectionWeight = selectionWeight;

            if (starIcon != null) {
                newMod.StarVisualAPI = starIcon;
                newMod.StarVisual = starIcon.name;
            }

            if (visualEffect != null) {
                newMod.VisualEffectAPI = visualEffect;
                newMod.VisualEffect = visualEffect.name;
            }

            if (setupMethod != null) {
                newMod.SetupMethodClass = setupMethod;
            }

            newMod.Config = new CreatureModConfig() {
                BasePower = basepower,
                PerlevelPower = perlevelpower,
                BiomeObjects = biomeConfig
            };

            if (namingStyle > 2 || namingStyle < 0) { namingStyle = 2; }
            newMod.namingConvention = (NameSelectionStyle)namingStyle;

            if (name_suffixes != null) {
                newMod.NameSuffixes = name_suffixes;
            }

            if (name_prefixes != null) {
                newMod.NamePrefixes = name_prefixes;
            }

            if (visualStyle > 3 || visualStyle < 0) { visualStyle = 0; }
            newMod.VisualEffectStyle = (VisualEffectStyle)visualStyle;

            if (allowed_creatures != null) {
                newMod.AllowedCreatures = allowed_creatures;
            }
            if (unallowed_creatures != null) {
                newMod.UnallowedCreatures = unallowed_creatures;
            }
            if (allowed_biomes != null) {
                newMod.AllowedBiomes = allowed_biomes;
            }

            ClearProbabilityCaches();

            return true;
        }
    }
}
