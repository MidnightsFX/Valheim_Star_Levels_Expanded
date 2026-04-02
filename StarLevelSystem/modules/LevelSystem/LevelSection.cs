using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class LevelSection {

        internal static void SetCharacterLevelControl(Character chara, int fallbackLevel) {
            if (chara == null) { return; }
            if (ValConfig.ControlSpawnerLevels.Value) {
                CreatureSetupFlow.CreatureSpawnerSetup(chara, delay: 1f);
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
                    biome_settings = Extensions.MutatingMergeBiomeConfigs(biomeConfig, allBiomeConfig);
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
    }
}
