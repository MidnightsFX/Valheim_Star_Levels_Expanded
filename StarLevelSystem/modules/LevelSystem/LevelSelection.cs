using MonoMod.Utils;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Analytics;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class LevelSelection {

        public static List<string> ForceLeveledCreatures = new List<string>();
        public static int DetermineLevel(Character character, ZDO cZDO, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings, int leveloverride = 0) {
            if (character == null || cZDO == null) {
                Logger.LogWarning($"Creature null or nview null, cannot set level.");
                return 1;
            }
            if (leveloverride > 0) {
                //character.m_level = leveloverride;
                //character.SetLevel(leveloverride);
                Logger.LogDebug($"Level override provided, setting level to {leveloverride}");
                return leveloverride;
            }

            int clevel = cZDO.GetInt(ZDOVars.s_level, 0);
            //Logger.LogDebug($"Current level from ZDO: {clevel} {clevel <= 0} || {ValConfig.OverlevedCreaturesGetRerolledOnLoad.Value} && {clevel > ValConfig.MaxLevel.Value}");
            if (clevel <= 0 || ValConfig.OverlevedCreaturesGetRerolledOnLoad.Value && clevel > ValConfig.MaxLevel.Value) {
                // Determine max level
                int max_level = ValConfig.MaxLevel.Value;
                int min_level = 0;
                if (biome_settings != null && biome_settings.BiomeMaxLevelOverride != 0) {
                    //Logger.LogDebug($"Max Level from: BiomeSpecific {biome_settings.BiomeMaxLevelOverride}");
                    max_level = biome_settings.BiomeMaxLevelOverride;
                }
                if (creature_settings != null && creature_settings.CreatureMaxLevelOverride > -1) {
                    //Logger.LogDebug($"Max Level from: CreatureSpecific:{creature_settings.CreatureMaxLevelOverride}");
                    max_level = creature_settings.CreatureMaxLevelOverride;
                }

                if (biome_settings != null && biome_settings.BiomeMinLevelOverride > 0) { min_level = biome_settings.BiomeMinLevelOverride; }
                if (creature_settings != null && creature_settings.CreatureMinLevelOverride > -1) { min_level = creature_settings.CreatureMinLevelOverride; }
                min_level += 1;
                max_level += 1;

                float levelup_roll = UnityEngine.Random.Range(0f, 100f);
                Vector3 p = character.transform.position;
                float distance_from_center = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(DistanceScaleSystem.center.x, DistanceScaleSystem.center.z));
                float distance_level_modifier = 1;
                SortedDictionary<int, float> distance_levelup_bonuses = new SortedDictionary<int, float>() { };
                SortedDictionary<int, float> levelup_chances = LevelSystemData.DefaultConfiguration.DefaultCreatureLevelUpChance;
                if (LevelSystemData.SLE_Level_Settings != null) {
                    levelup_chances = LevelSystemData.SLE_Level_Settings.DefaultCreatureLevelUpChance;
                    // If we are using distance level bonuses | Check if we are in a distance level bonus area
                    if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemData.SLE_Level_Settings.DistanceLevelBonus != null) {
                        distance_levelup_bonuses = DistanceScaleSystem.SelectDistanceFromCenterLevelBonus(distance_from_center);
                    }
                }
                if (biome_settings != null) {
                    distance_level_modifier = biome_settings.DistanceScaleModifier;
                }

                // Apply Night level scalers
                float nightScaleBonus = 1f;
                if (biome_settings != null && biome_settings.NightSettings != null && biome_settings.NightSettings.NightLevelUpChanceScaler != 1) {
                    nightScaleBonus = biome_settings.NightSettings.NightLevelUpChanceScaler;
                }
                if (creature_settings != null && creature_settings.NightSettings != null && creature_settings.NightSettings.NightLevelUpChanceScaler != 1) {
                    nightScaleBonus = creature_settings.NightSettings.NightLevelUpChanceScaler;
                }

                // Ensure we use character / biome specific levelup chances if those are set
                if (biome_settings != null && biome_settings.CustomCreatureLevelUpChance != null) {
                    levelup_chances = biome_settings.CustomCreatureLevelUpChance;
                }
                if (creature_settings != null && creature_settings.CustomCreatureLevelUpChance != null) {
                    levelup_chances = creature_settings.CustomCreatureLevelUpChance;
                }
                int level = LevelSelection.DetermineLevelRollResult(levelup_roll, max_level, levelup_chances, distance_levelup_bonuses, distance_level_modifier, nightScaleBonus);
                if (min_level > 0 && level < min_level) { level = min_level; }
                //Logger.LogDebug($"Determined level {level} min: {min_level} max {max_level}");
                //character.m_level = level;
                return level;
            }
            return clevel;
        }

        // For non-character levelups
        public static int DetermineLevel(GameObject creature, string creature_name, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings, int maxLevel) {
            if (creature == null) {
                Logger.LogWarning($"Creature is null, cannot determine level, set 1.");
                return 1;
            }

            float levelup_roll = UnityEngine.Random.Range(0f, 100f);
            // Logger.LogDebug($"levelroll: {levelup_roll}");
            // Check if the creature has an override level
            // Use the default non-biom based levelup chances
            // Logger.LogDebug($"maxlevel default: {maxLevel}");
            maxLevel += 1;
            // Determine creature location to check its biome
            // Determine creature max level from biome
            Vector3 p = creature.transform.position;
            float distance_from_center = Vector2.Distance(new Vector2(p.x, p.y), new Vector2(DistanceScaleSystem.center.x, DistanceScaleSystem.center.z));
            SortedDictionary<int, float> distance_levelup_bonuses = new SortedDictionary<int, float>() { };
            SortedDictionary<int, float> levelup_chances = LevelSystemData.SLE_Level_Settings.DefaultCreatureLevelUpChance;
            if (levelup_chances == null) { levelup_chances = LevelSystemData.DefaultConfiguration.DefaultCreatureLevelUpChance; }

            // If we are using distance level bonuses | Check if we are in a distance level bonus area
            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemData.SLE_Level_Settings.DistanceLevelBonus != null) {
                distance_levelup_bonuses = DistanceScaleSystem.SelectDistanceFromCenterLevelBonus(distance_from_center);
            }

            float distance_level_modifier = 1;
            if (biome_settings != null) { distance_level_modifier = biome_settings.DistanceScaleModifier; }
            if (creature_settings != null && creature_settings.DistanceScaleModifier != 1) { distance_level_modifier = creature_settings.DistanceScaleModifier; }
            // creature specific override
            if (LevelSystemData.SLE_Level_Settings.CreatureConfiguration != null && LevelSystemData.SLE_Level_Settings.CreatureConfiguration.ContainsKey(creature_name)) {
                //Logger.LogDebug($"Creature specific config found for {creature_name}");
                if (creature_settings.CustomCreatureLevelUpChance != null) {
                    if (creature_settings.CreatureMaxLevelOverride > -1) { maxLevel = creature_settings.CreatureMaxLevelOverride; }
                    if (creature_settings.CustomCreatureLevelUpChance != null) { levelup_chances = creature_settings.CustomCreatureLevelUpChance; }
                    return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
                }
            }

            // biome override 
            if (biome_settings != null) {
                if (biome_settings.BiomeMaxLevelOverride > 0) { maxLevel = biome_settings.BiomeMaxLevelOverride; }
                if (biome_settings.CustomCreatureLevelUpChance != null) { levelup_chances = biome_settings.CustomCreatureLevelUpChance; }
                return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
            }
            return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
        }

        public static void SetAndUpdateCharacterLevel(Character character, int level) {
            if (character == null) { return; }
            character.m_level = level;
            character.SetupMaxHealth();
            if (character.m_nview != null && character.m_nview.GetZDO() != null) {
                character.m_nview.GetZDO().Set(ZDOVars.s_level, level);
            }
        }

        // Consider decision tree for levelups to reduce iterations
        public static int DetermineLevelRollResult(float roll, int maxLevel, SortedDictionary<int, float> creature_levelup_chance, SortedDictionary<int, float> levelup_bonus, float distance_influence, float nightBonus = 1f) {
            int selected_level = 0;
            // Build new levelup definitions with bonuses applied
            SortedDictionary<int, float> LevelUpWithBonus = new SortedDictionary<int, float>() { };
            LevelUpWithBonus.AddRange<int, float>(creature_levelup_chance);
            if (levelup_bonus != null) {
                foreach (KeyValuePair<int, float> kvp in levelup_bonus) {
                    if (LevelUpWithBonus.ContainsKey(kvp.Key)) {
                        LevelUpWithBonus[kvp.Key] += (kvp.Value * distance_influence);
                    } else {
                        LevelUpWithBonus[kvp.Key] = (kvp.Value * distance_influence);
                    }
                }
            }

            int index = 0;
            foreach (KeyValuePair<int, float> kvp in LevelUpWithBonus) {
                float levelup_req = kvp.Value * nightBonus;
                index++;
                // Uncomment to debug level roll selection and values (warning verbose)
                //if (ValConfig.EnableDebugOutputLevelRolls.Value) {
                //    float bonus = 0;
                //    if (levelup_bonus != null && levelup_bonus.ContainsKey(kvp.Key)) { bonus = levelup_bonus[kvp.Key]; }
                //    float baseval = 0;
                //    if (creature_levelup_chance.ContainsKey(kvp.Key)) { baseval = creature_levelup_chance[kvp.Key]; }
                //    Logger.LogDebug($"Level Roll: {roll} >= {levelup_req} = [ {baseval}(base) + ({bonus}(bonus) * {distance_influence})] * {nightBonus} | {kvp.Key}");
                //}
                if (roll >= levelup_req || kvp.Key >= maxLevel || index == LevelUpWithBonus.Count()) {
                    selected_level = kvp.Key;
                    if (ValConfig.EnableDebugOutputLevelRolls.Value) {
                        if (index == LevelUpWithBonus.Count()) {
                            selected_level += 1; // Because we would normally select the NEXT key as our actual level (accounting for the N+1 level system)
                        }
                        float bonus = 0;
                        if (levelup_bonus != null && levelup_bonus.ContainsKey(kvp.Key)) { bonus = levelup_bonus[kvp.Key]; }
                        float baseval = 0;
                        if (creature_levelup_chance.ContainsKey(kvp.Key)) { baseval = creature_levelup_chance[kvp.Key]; }
                        Logger.LogDebug($"Level Roll: {roll} >= {levelup_req} = [ {baseval}(base) + {bonus}(distanceBonus) * {distance_influence}(DistanceInfluence)] * {nightBonus}(Night) | max-level used: {maxLevel} Selected Level: {selected_level}");
                    }
                    break;
                }
            }
            // Rolled level is always N+1 due to 1 star being level 2
            return selected_level;
        }

        public static int DeterministicDetermineTreeLevel(GameObject go) {
            if (ValConfig.EnableTreeScaling.Value == false) { return 1; }
            Vector3 p = go.transform.position;
            float distance_from_center = Vector2.Distance(new Vector2(p.x, p.y), new Vector2(DistanceScaleSystem.center.x, DistanceScaleSystem.center.z));
            int level = Mathf.RoundToInt(distance_from_center / (WorldGenerator.worldSize / ValConfig.TreeMaxLevel.Value));
            if (level < 1) { level = 1; }
            return level;
        }

        public static int DeterministicDetermineRockLevel(Vector3 pos) {
            if (ValConfig.EnableRockLevels.Value == false) { return 1; }
            float distance_from_center = Vector2.Distance(new Vector2(pos.x, pos.y), new Vector2(DistanceScaleSystem.center.x, DistanceScaleSystem.center.z));
            int level = Mathf.RoundToInt(distance_from_center / (WorldGenerator.worldSize / ValConfig.RockMaxLevel.Value));
            if (level < 1) { level = 1; }
            return level;
        }

        public static int DetermineisticDetermineObjectLevel(Vector3 pos) {
            float distance_from_center = Vector2.Distance(new Vector2(pos.x, pos.y), new Vector2(DistanceScaleSystem.center.x, DistanceScaleSystem.center.z));
            int level = Mathf.RoundToInt(distance_from_center / (WorldGenerator.worldSize / ValConfig.DestructibleMaxLevel.Value));
            if (level < 1) { level = 1; }
            return level;
        }

        internal static void SetCharacterLevelControl(Character chara, int fallbackLevel) {
            if (chara == null) { return; }
            if (ValConfig.ControlSpawnerLevels.Value) {
                CreatureSetupControl.CreatureSpawnerSetup(chara, delay: 1f);
                return;
            }
            // Fallback
            Logger.LogDebug($"Setting creature level from fallback provided {fallbackLevel}");
            chara.SetLevel(fallbackLevel);
        }

        public static void SelectCreatureBiomeSettings(GameObject creature, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome creature_biome) {
            // Determine creature max level from biome
            Vector3 p = creature.transform.position;
            creature_name = Utils.GetPrefabName(creature.gameObject);
            Heightmap.Biome biome = Heightmap.FindBiome(p);
            creature_biome = biome;
            biome_settings = null;
            creature_settings = null;
            // Guard clause for those that have empty or null configurations
            if (LevelSystemData.SLE_Level_Settings == null) { return; }

            if (LevelSystemData.SLE_Level_Settings.BiomeConfiguration != null) {
                bool biome_all_setting_check = LevelSystemData.SLE_Level_Settings.BiomeConfiguration.TryGetValue(Heightmap.Biome.All, out var allBiomeConfig);
                if (biome_all_setting_check) {
                    biome_settings = allBiomeConfig;
                }
                //Logger.LogDebug($"Biome all config checked");
                bool biome_setting_check = LevelSystemData.SLE_Level_Settings.BiomeConfiguration.TryGetValue(biome, out var biomeConfig);
                if (biome_setting_check && biome_all_setting_check) {
                    biome_settings = SLSExtensions.MutatingMergeBiomeConfigs(biomeConfig, allBiomeConfig);
                } else if (biome_setting_check) {
                    biome_settings = biomeConfig;
                }
                //Logger.LogDebug($"Merged biome configs");
            }

            if (LevelSystemData.SLE_Level_Settings.CreatureConfiguration != null) {
                if (LevelSystemData.SLE_Level_Settings.CreatureConfiguration.TryGetValue(creature_name, out var creatureConfig)) { creature_settings = creatureConfig; }
                //Logger.LogDebug($"Set character specific configs");
            }
        }

        public static IEnumerator ModifyTreeWithLevel(TreeBase tree, int level) {
            yield return new WaitForSeconds(1f);
            if (tree == null) { yield break; }
            //Logger.LogDebug($"Tree level set to: {level}");
            float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * level);
            tree.m_health += (tree.m_health * 0.1f * level);
            // Logger.LogDebug($"Setting Tree size {scale}.");
            tree.transform.localScale *= scale;
            List<DropTable.DropData> drops = new List<DropTable.DropData>();
            foreach (var drop in tree.m_dropWhenDestroyed.m_drops) {
                DropTable.DropData lvlupdrop = new DropTable.DropData();
                // Scale the amount of drops based on level
                lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (1 + ValConfig.PerLevelTreeLootScale.Value * level));
                lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (1 + ValConfig.PerLevelTreeLootScale.Value * level));
                // Logger.LogDebug($"Scaling drop {drop.m_item.name} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {storedLevel}.");
                lvlupdrop.m_item = drop.m_item;
                drops.Add(lvlupdrop);
            }
            Physics.SyncTransforms();

            yield break;
        }



        internal static void LeveledCreatureListChanged(object s, EventArgs e) {
            SetupForceLeveledCreatureList();
        }

        internal static void SetupForceLeveledCreatureList() {
            ForceLeveledCreatures.Clear();
            foreach (var item in ValConfig.SpawnsAlwaysControlled.Value.Split(',')) {
                ForceLeveledCreatures.Add(item);
            }
        }
    }
}
