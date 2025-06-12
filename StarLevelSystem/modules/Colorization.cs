using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.IO;
using UnityEngine;
using static LevelEffects;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    public class ColorDef
    {
        public float hue {  get; set; } = 0f;
        public float saturation { get; set; } = 0f;
        public float value { get; set; } = 0f;
        public bool is_emissive { get; set; } = false;

        public LevelEffects.LevelSetup toLevelEffect() {
            return new LevelEffects.LevelSetup() {
                m_scale = 1f,
                m_hue = hue,
                m_saturation = saturation,
                m_value = value,
                m_setEmissiveColor = is_emissive,
                m_emissiveColor = new Color(hue, saturation, value)
            };
        }
    }
    public static class Colorization
    {
        public static CreatureColorizationSettings creatureColorizationSettings = defaultColorizationSettings;
        private static CreatureColorizationSettings defaultColorizationSettings = new CreatureColorizationSettings()
        {
            characterSpecificColorization = ColorizationData.characterColorizationData,
            defaultLevelColorization = ColorizationData.defaultColorizationData
        };

        public static void Init() {
            creatureColorizationSettings = defaultColorizationSettings;
            try {
                UpdateYamlConfig(File.ReadAllText(ValConfig.colorsFilePath));
            } catch (Exception e) { Jotunn.Logger.LogWarning($"There was an error updating the Color Level values, defaults will be used. Exception: {e}"); }
        }

        public static string YamlDefaultConfig() {
            var yaml = DataObjects.yamlserializer.Serialize(defaultColorizationSettings);
            return yaml;
        }

        public static bool UpdateYamlConfig(string yaml) {
            try {
                creatureColorizationSettings = DataObjects.yamldeserializer.Deserialize<DataObjects.CreatureColorizationSettings>(yaml);
                // Ensure that we load the default colorization settings, maybe we consider a merge here instead?
                if (creatureColorizationSettings.defaultLevelColorization.Count +1 < 103) {
                    creatureColorizationSettings.defaultLevelColorization = defaultColorizationSettings.defaultLevelColorization;
                }
                Logger.LogInfo($"Updated ColorizationSettings.");
                // This might need to be async
                foreach (var chara in Resources.FindObjectsOfTypeAll<Character>()) {
                    if (chara.m_level <= 1) { continue; }
                    ApplyColorizationWithoutLevelEffects(chara);
                }
            } catch (System.Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse ColorizationSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }

        // Consider if we want to use emissive colors?
        // TODO make these static?
        // Make these allow levels past level 100?
        public static void SetupLevelEffects() {
            for (int level = defaultColorizationSettings.defaultLevelColorization.Count + 1; 103 > level; level++)
            {
                float sat = UnityEngine.Random.Range(-0.1f, 0.1f);
                float hue = UnityEngine.Random.Range(-0.1f, 0.1f);
                float value = UnityEngine.Random.Range(-0.3f, 0.3f);
                Logger.LogDebug($"LevelEffects: {level} - hue:{hue}, sat:{sat}, val:{value}");
                defaultColorizationSettings.defaultLevelColorization.Add(level, new ColorDef() { hue = hue, saturation = sat, value = value });
            }
        }

        // Don't run the vanilla level effects since we are managing all of that ourselves
        [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.SetupLevelVisualization))]
        public static class PreventDefaultLevelSetup
        {
            public static bool Prefix() {
                return false;
            }
        }

        public static void ApplyLevelVisual(Character charc) {
            LevelEffects charLevelEf = charc.gameObject.GetComponentInChildren<LevelEffects>();
            if (charLevelEf == null || charLevelEf.m_levelSetups == null || charLevelEf.m_levelSetups.Count <= 0) { return; }

            // Randomly select level visualization
            LevelSetup clevelset = charLevelEf.m_levelSetups[UnityEngine.Random.Range(0, charLevelEf.m_levelSetups.Count - 1)];
            if (clevelset.m_enableObject != null) { clevelset.m_enableObject.SetActive(true); }
        }


        internal static void ApplyColorizationWithoutLevelEffects(Character cgo) {
            int level = cgo.m_level - 1;

            LevelSetup genlvlup = creatureColorizationSettings.defaultLevelColorization[level].toLevelEffect();
            string cname = Utils.GetPrefabName(cgo.gameObject);
            Logger.LogDebug($"Checking for character specific colorization {cname}");
            if (creatureColorizationSettings.characterSpecificColorization.ContainsKey(cname)) {
                if (creatureColorizationSettings.characterSpecificColorization[cname].TryGetValue(level, out ColorDef charspecific_color_def)) {
                    Logger.LogDebug($"Found character specific colorization for {cname} - {level}");
                    genlvlup = charspecific_color_def.toLevelEffect();
                }
            } else { Logger.LogDebug($"No character specific colorization for {cname} - {level}"); }
            // Material assignment changes must occur in a try block- they can quietly crash the game otherwise
            try {
                foreach (var smr in cgo.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    Material[] sharedMaterials2 = smr.sharedMaterials;
                    sharedMaterials2[0] = new Material(sharedMaterials2[0]);
                    sharedMaterials2[0].SetFloat("_Hue", genlvlup.m_hue);
                    sharedMaterials2[0].SetFloat("_Saturation", genlvlup.m_saturation);
                    sharedMaterials2[0].SetFloat("_Value", genlvlup.m_value);
                    if (genlvlup.m_setEmissiveColor)
                    {
                        sharedMaterials2[0].SetColor("_EmissionColor", genlvlup.m_emissiveColor);
                    }
                    smr.sharedMaterials = sharedMaterials2;
                }
            }
            catch (Exception e) {
                Logger.LogError($"Exception while colorizing {e}");
            }
        }

        //public static void DumpDefaultColorizations()
        //{
        //    foreach(var noid in  Resources.FindObjectsOfTypeAll<Humanoid>()) {
        //        if (noid == null) { continue; }
        //        LevelEffects le = noid.GetComponentInChildren<LevelEffects>();
        //        if (le != null) {
        //            string name = noid.name.Replace("(Clone)", "");
        //            if (!defaultColorizationSettings.characterSpecificColorization.ContainsKey(name)) {
        //                defaultColorizationSettings.characterSpecificColorization.Add(name, new List<ColorDef>());
        //            }

        //            foreach(var colset in le.m_levelSetups) {
        //                defaultColorizationSettings.characterSpecificColorization[noid.name].Add(new ColorDef() { value = colset.m_value, hue = colset.m_hue, saturation = colset.m_saturation, is_emissive = colset.m_setEmissiveColor });
        //            }
        //        }
        //    }
        //    // Copy over the updated defaults
        //    Init();
        //    Logger.LogInfo(YamlDefaultConfig());
        //}


        internal static void StarLevelScaleChanged(object s, EventArgs e) {
            // This might need to be async
            foreach(var chara in Resources.FindObjectsOfTypeAll<Character>()) {
                chara.transform.localScale = Vector3.one;
                float scale = 1+ (ValConfig.PerLevelScaleBonus.Value * (chara.m_level -1 ));
                Logger.LogDebug($"Setting {chara.name} size {scale}.");
                chara.transform.localScale *= scale;
            }
            Physics.SyncTransforms();
        }
    }
}
