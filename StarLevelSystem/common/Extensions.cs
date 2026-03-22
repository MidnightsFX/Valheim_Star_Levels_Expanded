using HarmonyLib;
using StarLevelSystem.modules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    public static class Extensions
    {
        /// <summary>
        /// Take any list of Objects and return it with Fischer-Yates shuffle
        /// </summary>
        /// <returns></returns>
        public static List<T> ShuffleList<T>(this List<T> inputList)
        {
            int i = 0;
            int t = inputList.Count;
            int r = 0;
            T p = default(T);
            List<T> tempList = new List<T>();
            tempList.AddRange(inputList);

            while (i < t)
            {
                r = UnityEngine.Random.Range(i, tempList.Count);
                p = tempList[i];
                tempList[i] = tempList[r];
                tempList[r] = p;
                i++;
            }

            return tempList;
        }

        public static bool CompareListContents<T>(this List<T> listA, List<T> listB)
        {
            if (listA == null && listB == null) return true;
            if (listA == null || listB == null) return false;
            if (listA.Count != listB.Count) return false;

            // Check for items in both lists, regardless of order, extras are left out
            // if both entries are the same count they are equal
            var firstNotSecond = listA.Except(listB).ToList();
            var secondNotFirst = listB.Except(listA).ToList();

            return !firstNotSecond.Any() && !secondNotFirst.Any();
        }

        public static int GetLabelValue(Label label) {
            MethodInfo privateMethod = typeof(Label).GetMethod("GetLabelValue", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)privateMethod.Invoke(label, null);
        }

        public static CodeMatcher CreateLabelOffset(this CodeMatcher matcher, out Label label, int offset = 0)
        {
            return matcher.CreateLabelAt(matcher.Pos + offset, out label);
        }

        public static CodeMatcher ExtractLabel( this CodeMatcher matcher, out Label elabel, int searchrange = 1, int labelToSelect = 0) {
            elabel = default;

            var list = matcher.Instructions();
            int end = Math.Min(list.Count, matcher.Pos + 1 + searchrange);
            bool found = false;
            List<Label> foundLabels = new List<Label>();
            for (int i = matcher.Pos + 1; i < end; i++) {
                var instr = list[i];
                if (instr.labels is { Count: > 0 }) {
                    Logger.LogDebug($"found labels: {instr.labels.Count}");
                    int labelindex = 0;
                    foreach(Label lb in instr.labels) {
                        foundLabels.Add(lb);
                        if (foundLabels.Count == labelToSelect + 1) {
                            Logger.LogDebug($"Selected Label {GetLabelValue(lb)}");
                            instr.labels.RemoveAt(labelindex);
                        }
                        labelindex++;
                    }
                    return matcher; // keep matcher at current position
                }
            }

            if (found == false) {
                throw new InvalidOperationException($"No label found within {searchrange} instructions ahead of position {matcher.Pos}.");
            }
            
            return matcher; // keep matcher at current position
        }

        public static CodeMatcher ExtractLabelOnNextInstructionOfType(this CodeMatcher matcher, CodeInstruction op, out Label label) {
            List<CodeInstruction> list = matcher.Instructions().GetRange(matcher.Pos - 1, matcher.Instructions().Count - matcher.Pos - 1);
            bool matched = false;
            label = default;
            foreach (CodeInstruction ci in list) {
                if (ci != op) { continue; }

                matched = true;
                if (ci.labels.Count > 0) {
                    label = ci.labels[0];
                } else {
                    Logger.LogWarning($"No Labels found on the selected CodeInstruction {ci.opcode}");
                }
            }
            if (!matched) {
                throw new InvalidOperationException($"Did not match an opcode.");
            }

            return matcher; // keep matcher at current position
        }

        public static CodeMatcher SelectLabelInRange(this CodeMatcher matcher, int key, out Label label, int keyindex = 0) {
            List<CodeInstruction> list = matcher.Instructions();
            int index = 0;
            label = default;
            Dictionary<int, List<Label>> labelLoc = new Dictionary<int, List<Label>>();
            foreach (CodeInstruction ci in list) {
                if (ci.labels.Count > 0) {
                    labelLoc.Add(index, ci.labels);
                    Logger.LogDebug($"Found Label at index {index} on {ci.opcode} label-target: {ci.labels[0]}");
                }
                index++;
            }
            if (labelLoc.ContainsKey(key)) {
                if (labelLoc[key].Count > 0) {
                    label = labelLoc[key][keyindex];
                } else {
                    label = labelLoc[key][0];
                }
            } else {
                Logger.LogWarning($"Keyed label was not found with key {key}");
            }

            Logger.LogDebug($"Label indexes {String.Join(",",labelLoc.Keys)} current position: {matcher.Pos}");

            return matcher; // keep matcher at current position
        }


        public static CodeMatcher ExtractFirstLabel(this CodeMatcher matcher, out Label label) {
            label = matcher.Labels.First();
            matcher.Labels.Clear();

            return matcher;
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
                foreach(var defweapon in noid.m_defaultItems) {
                    float wepdmg = defweapon.GetComponent<ItemDrop>().m_itemData.m_shared.m_damages.GetTotalDamage();
                    if (wepdmg > dmg) { dmg = wepdmg; }
                }
            } else {
                if (item != null) {
                    HitData.DamageTypes dmgs = item.GetDamage();
                    // Spirit and Poison get reduced weights here because they are dmg over time primarily and taking into account the whole value immediately results in a dmg spike
                    dmg = dmgs.m_fire + dmgs.m_frost + dmgs.m_lightning + (dmgs.m_spirit / 2) + (dmgs.m_poison / 6) + dmgs.m_blunt + dmgs.m_pierce + dmgs.m_slash;
                }
            }
            Logger.LogDebug($"Estimated {chara.m_name} damage as: {dmg}");
            return dmg;
        }

        public static BiomeSpecificSetting MergeBiomeConfigs(BiomeSpecificSetting prioritycfg, BiomeSpecificSetting othercfg)
        {
            BiomeSpecificSetting biomecfg = new BiomeSpecificSetting
            {
                CustomCreatureLevelUpChance = othercfg.CustomCreatureLevelUpChance != null
                    ? new SortedDictionary<int, float>(othercfg.CustomCreatureLevelUpChance) : null,
                BiomeMinLevelOverride = othercfg.BiomeMinLevelOverride,
                BiomeMaxLevelOverride = othercfg.BiomeMaxLevelOverride,
                DistanceScaleModifier = othercfg.DistanceScaleModifier,
                SpawnRateModifier = othercfg.SpawnRateModifier,
                CreatureBaseValueModifiers = othercfg.CreatureBaseValueModifiers != null
                    ? new Dictionary<CreatureBaseAttribute, float>(othercfg.CreatureBaseValueModifiers) : null,
                CreaturePerLevelValueModifiers = othercfg.CreaturePerLevelValueModifiers != null
                    ? new Dictionary<CreaturePerLevelAttribute, float>(othercfg.CreaturePerLevelValueModifiers) : null,
                DamageRecievedModifiers = othercfg.DamageRecievedModifiers != null
                    ? new Dictionary<DamageType, float>(othercfg.DamageRecievedModifiers) : null,
                creatureSpawnsDisabled = othercfg.creatureSpawnsDisabled != null
                    ? new List<string>(othercfg.creatureSpawnsDisabled) : null,
                NightSettings = othercfg.NightSettings != null
                    ? new BiomeNightSettings {
                        NightLevelUpChanceScaler = othercfg.NightSettings.NightLevelUpChanceScaler,
                        SpawnRateModifier = othercfg.NightSettings.SpawnRateModifier,
                        creatureSpawnsDisabled = othercfg.NightSettings.creatureSpawnsDisabled != null
                            ? new List<string>(othercfg.NightSettings.creatureSpawnsDisabled) : null
                    } : null
            };

            if (prioritycfg.CustomCreatureLevelUpChance != null) { biomecfg.CustomCreatureLevelUpChance = prioritycfg.CustomCreatureLevelUpChance; }
            if (prioritycfg.BiomeMinLevelOverride != 0) { biomecfg.BiomeMinLevelOverride = prioritycfg.BiomeMinLevelOverride; }
            if (prioritycfg.BiomeMaxLevelOverride != 0) { biomecfg.BiomeMaxLevelOverride = prioritycfg.BiomeMaxLevelOverride; }
            biomecfg.DistanceScaleModifier = prioritycfg.DistanceScaleModifier;
            biomecfg.SpawnRateModifier = prioritycfg.SpawnRateModifier;

            if (biomecfg.CreatureBaseValueModifiers != null && prioritycfg.CreatureBaseValueModifiers != null)
            {
                foreach (var kv in prioritycfg.CreatureBaseValueModifiers) { biomecfg.CreatureBaseValueModifiers[kv.Key] = kv.Value; }
            }
            else if (prioritycfg.CreatureBaseValueModifiers != null)
            {
                biomecfg.CreatureBaseValueModifiers = new Dictionary<CreatureBaseAttribute, float>(prioritycfg.CreatureBaseValueModifiers);
            }

            if (biomecfg.CreaturePerLevelValueModifiers != null && prioritycfg.CreaturePerLevelValueModifiers != null)
            {
                foreach (var kv in prioritycfg.CreaturePerLevelValueModifiers) { biomecfg.CreaturePerLevelValueModifiers[kv.Key] = kv.Value; }
            }
            else if (prioritycfg.CreaturePerLevelValueModifiers != null)
            {
                biomecfg.CreaturePerLevelValueModifiers = new Dictionary<CreaturePerLevelAttribute, float>(prioritycfg.CreaturePerLevelValueModifiers);
            }

            if (biomecfg.DamageRecievedModifiers != null && prioritycfg.DamageRecievedModifiers != null)
            {
                foreach (var kv in prioritycfg.DamageRecievedModifiers) { biomecfg.DamageRecievedModifiers[kv.Key] = kv.Value; }
            }
            else if (prioritycfg.DamageRecievedModifiers != null)
            {
                biomecfg.DamageRecievedModifiers = new Dictionary<DamageType, float>(prioritycfg.DamageRecievedModifiers);
            }

            if (biomecfg.creatureSpawnsDisabled != null && prioritycfg.creatureSpawnsDisabled != null)
            {
                biomecfg.creatureSpawnsDisabled = biomecfg.creatureSpawnsDisabled.Union(prioritycfg.creatureSpawnsDisabled).ToList();
            }
            else if (prioritycfg.creatureSpawnsDisabled != null)
            {
                biomecfg.creatureSpawnsDisabled = new List<string>(prioritycfg.creatureSpawnsDisabled);
            }

            if (prioritycfg.NightSettings != null) {
                if (biomecfg.NightSettings == null) { biomecfg.NightSettings = new BiomeNightSettings(); }
                biomecfg.NightSettings.SpawnRateModifier = prioritycfg.NightSettings.SpawnRateModifier;
                biomecfg.NightSettings.NightLevelUpChanceScaler = prioritycfg.NightSettings.NightLevelUpChanceScaler;
                if (prioritycfg.NightSettings.creatureSpawnsDisabled != null && biomecfg.NightSettings.creatureSpawnsDisabled != null) {
                    biomecfg.NightSettings.creatureSpawnsDisabled = biomecfg.NightSettings.creatureSpawnsDisabled.Union(prioritycfg.NightSettings.creatureSpawnsDisabled).ToList();
                } else if (prioritycfg.NightSettings.creatureSpawnsDisabled != null) {
                    biomecfg.NightSettings.creatureSpawnsDisabled = new List<string>(prioritycfg.NightSettings.creatureSpawnsDisabled);
                }
            }
            return biomecfg;
        }
    }
}
