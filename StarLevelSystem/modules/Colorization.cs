using HarmonyLib;
using StarLevelSystem.common;
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

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        public static class AddLevelEffectsWhenSpawned {
            public static void Postfix(Character __instance) {
                // Apply Character specific modifiers

                if (__instance.m_level <= 1) { return; }

                // Color
                ApplyColorizationWithoutLevelEffects(__instance);
                // Visual modifiers
                ApplyHighLevelVisual(__instance);

                // Scaling
                if (__instance.transform.position.y < 3000f && ValConfig.EnableScalingInDungeons.Value == false) {
                    // Don't scale in dungeons
                    float scale = 1 + (ValConfig.PerLevelScaleBonus.Value * __instance.m_level);
                    Logger.LogInfo($"Setting character size {scale} and color.");
                    __instance.transform.localScale *= scale;
                    Physics.SyncTransforms();
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class AddLevelEffectsWhenLoaded
        {
            // Characters that are spawned are spawned at level 1- so this won't trigger
            // This will only trigger for characters that already have their level set
            public static void Postfix(Character __instance) {
                if (__instance.m_level <= 1) { return; }
                //LevelEffects le = __instance.GetComponentInChildren<LevelEffects>();
                //if (le == null) { ApplyColorizationWithoutLevelEffects(__instance); }
                ApplyColorizationWithoutLevelEffects(__instance);
                ApplyHighLevelVisual(__instance);
                float scale = 1 + (ValConfig.PerLevelScaleBonus.Value * __instance.m_level);
                Logger.LogInfo($"Setting character size {scale} and color.");
                __instance.transform.localScale *= scale;
                Physics.SyncTransforms();
            }
        }

        public static void ApplyHighLevelVisual(Character charc) {
            LevelEffects charLevelEf = charc.gameObject.GetComponentInChildren<LevelEffects>();
            if (charLevelEf == null) { return; }
            // Enables the highest level visual effect for this high level creature (maybe we want to randomize this based on level?)
            int selected_setup = charc.m_level;
            if (selected_setup > charLevelEf.m_levelSetups.Count) { selected_setup = charLevelEf.m_levelSetups.Count; }

            LevelSetup clevelset = charLevelEf.m_levelSetups[(charLevelEf.m_levelSetups.Count - 1)];
            if (clevelset.m_enableObject != null) { clevelset.m_enableObject.SetActive(true); }
        }


        private static void ApplyColorizationWithoutLevelEffects(Character cgo) {
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
                Logger.LogInfo($"Setting {chara.name} size {scale}.");
                chara.transform.localScale *= scale;
            }
            Physics.SyncTransforms();
        }
    }
}
