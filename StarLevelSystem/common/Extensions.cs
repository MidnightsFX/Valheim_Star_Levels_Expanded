using HarmonyLib;
using StarLevelSystem.modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    public static class Extensions
    {
        public static CodeMatcher CreateLabelOffset(this CodeMatcher matcher, out Label label, int offset = 0)
        {
            return matcher.CreateLabelAt(matcher.Pos + offset, out label);
        }

        public static void Times(this int count, Action action)
        {
            for (int i = 0; i < count; i++)
            {
                action();
            }
        }

        public static KeyValuePair<string, List<string>> RandomEntry(Dictionary<string, List<string>> dict, List<string> removedKeys = null) {
            List<string> keys = dict.Keys.ToList();
            if (removedKeys != null) {
                keys = keys.Where(k => !removedKeys.Contains(k)).ToList();
            }
            if (keys.Count == 0) {
                return new KeyValuePair<string, List<string>>(key: CreatureModifiers.NoMods, value: null);
            }
            string key = keys[UnityEngine.Random.Range(0, keys.Count - 1)];
            return new KeyValuePair<string, List<string>>(key: key, value: dict[key]);
        }

        public static List<Character> GetCharactersInRange(Vector3 position, float range)
        {
            Collider[] objs_near = Physics.OverlapSphere(position, range);
            List <Character> characters = new List<Character>();

            foreach (var col in objs_near) {
                var chara = col.GetComponentInChildren<Character>();
                if (chara != null) { characters.Add(chara); }
            }

            return characters;
        }

        public static float EstimateCharacterDamage(Character chara, bool highest = true) {
            if (chara == null || chara.IsPlayer()) return 0;
            Humanoid noid = chara as Humanoid;
            if (noid == null) return 0;
            float dmg = 0;
            ItemDrop.ItemData item = noid.GetCurrentWeapon();
            if (highest && noid.m_defaultItems != null) {
                Logger.LogDebug("Checking for highest damage");
                foreach(var defweapon in noid.m_defaultItems) {
                    float? wepdmg = defweapon.GetComponent<ItemDrop>()?.m_itemData?.GetDamage().GetTotalDamage();
                    if (wepdmg != null) { if (wepdmg > dmg) { dmg = (float)wepdmg; } }
                }
            } else {
                if (item != null) {
                    Logger.LogDebug("Checking for current weapon damage");
                    HitData.DamageTypes dmgs = item.GetDamage();
                    // Spirit and Poison get reduced weights here because they are dmg over time primarily and taking into account the whole value immediately results in a dmg spike
                    dmg = dmgs.m_fire + dmgs.m_frost + dmgs.m_lightning + (dmgs.m_spirit / 2) + (dmgs.m_poison / 6) + dmgs.m_blunt + dmgs.m_pierce + dmgs.m_slash;
                }
            }

            return dmg;
        }

        public static BiomeSpecificSetting MergeBiomeConfigs(BiomeSpecificSetting prioritycfg, BiomeSpecificSetting othercfg)
        {
            BiomeSpecificSetting biomecfg = othercfg;
            if (prioritycfg.CustomCreatureLevelUpChance != null) { biomecfg.CustomCreatureLevelUpChance = prioritycfg.CustomCreatureLevelUpChance; }
            biomecfg.BiomeMaxLevelOverride = prioritycfg.BiomeMaxLevelOverride;
            biomecfg.DistanceScaleModifier = prioritycfg.DistanceScaleModifier;
            if (biomecfg.CreatureBaseValueModifiers != null && prioritycfg.CreatureBaseValueModifiers != null)
            {
                biomecfg.CreatureBaseValueModifiers.ToList().ForEach(x => prioritycfg.CreatureBaseValueModifiers[x.Key] = x.Value);
            }
            else if (prioritycfg.CreatureBaseValueModifiers != null)
            {
                biomecfg.CreatureBaseValueModifiers = prioritycfg.CreatureBaseValueModifiers;
            }
            if (biomecfg.CreaturePerLevelValueModifiers != null && prioritycfg.CreaturePerLevelValueModifiers != null)
            {
                biomecfg.CreaturePerLevelValueModifiers.ToList().ForEach(x => prioritycfg.CreaturePerLevelValueModifiers[x.Key] = x.Value);
            }
            else if (prioritycfg.CreaturePerLevelValueModifiers != null)
            {
                biomecfg.CreaturePerLevelValueModifiers = prioritycfg.CreaturePerLevelValueModifiers;
            }
            if (biomecfg.DamageRecievedModifiers != null && prioritycfg.DamageRecievedModifiers != null)
            {
                biomecfg.DamageRecievedModifiers.ToList().ForEach(x => prioritycfg.DamageRecievedModifiers[x.Key] = x.Value);
            }
            else if (prioritycfg.DamageRecievedModifiers != null)
            {
                biomecfg.DamageRecievedModifiers = prioritycfg.DamageRecievedModifiers;
            }
            if (biomecfg.creatureSpawnsDisabled != null && prioritycfg.creatureSpawnsDisabled != null)
            {
                biomecfg.creatureSpawnsDisabled = biomecfg.creatureSpawnsDisabled.Union(prioritycfg.creatureSpawnsDisabled).ToList();
            }
            else if (prioritycfg.creatureSpawnsDisabled != null)
            {
                biomecfg.creatureSpawnsDisabled = prioritycfg.creatureSpawnsDisabled;
            }

            if (prioritycfg.NightSettings != null) {
                biomecfg.NightSettings.SpawnRateModifier = prioritycfg.NightSettings.SpawnRateModifier;
                if (prioritycfg.NightSettings.creatureSpawnsDisabled != null) {
                    biomecfg.NightSettings.creatureSpawnsDisabled = biomecfg.NightSettings.creatureSpawnsDisabled.Union(prioritycfg.NightSettings.creatureSpawnsDisabled).ToList();
                }
            }
            return biomecfg;
        }
    }
}
