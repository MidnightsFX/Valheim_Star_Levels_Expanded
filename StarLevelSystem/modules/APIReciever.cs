using StarLevelSystem.Data;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

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
            cdc.Colorization = new ColorDef(value, hue, sat, emission);
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
    }
}
