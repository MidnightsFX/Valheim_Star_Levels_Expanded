using BepInEx;
using HarmonyLib;
using Jotunn.Extensions;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static LevelEffects;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{

    public static class Colorization
    {
        public static CreatureColorizationSettings creatureColorizationSettings = defaultColorizationSettings;
        private static CreatureColorizationSettings defaultColorizationSettings = new CreatureColorizationSettings()
        {
            characterSpecificColorization = ColorizationData.characterColorizationData,
            defaultLevelColorization = ColorizationData.defaultColorizationData,
            characterColorGenerators = ColorizationData.defaultColorGenerators,
        };

        public static List<Color> mapRingColors = new List<Color>();
        public static readonly Dictionary<string, string> defaultColors = new Dictionary<string, string>()
        {
            { "Red",    "#ff1a1a" },
            { "Orange", "#ff9933" },
            { "Yellow", "#ffff1a" },
            { "Green",  "#50f83a" },
            { "Teal",   "#18e7a9" },
            { "Blue",   "#00abff" },
            { "Purple", "#c966ff" },
            { "Pink",   "#ff4dcf" },
            { "Gray",   "#999999" },
            { "Brown",  "#b37700" },
            { "Black",  "#333333" },
            { "White",  "#f2f2f2" },
        };

        public static ColorDef defaultColorization = new ColorDef()
        {
            hue = 0f,
            saturation = 0f,
            value = 0f,
            IsEmissive = false
        };

        public static void Init() {
            creatureColorizationSettings = defaultColorizationSettings;
            try {
                UpdateYamlConfig(File.ReadAllText(ValConfig.colorsFilePath));
            } catch (Exception e) { Jotunn.Logger.LogWarning($"There was an error updating the Color Level values, defaults will be used. Exception: {e}"); }
            
            var harmony = new Harmony($"{StarLevelSystem.PluginGUID}.Colorization");
            if (ValConfig.EnableColorization.Value) {
              Patch(harmony);
            }
            ValConfig.EnableColorization.SettingChanged += (s, e) => {
              if (ValConfig.EnableColorization.Value) Patch(harmony);
              else harmony.UnpatchSelf();
            };
        }

        private static void Patch(Harmony harmony) {
          harmony.PatchAll(typeof(PreventDefaultLevelSetup));
        }

        public static string YamlDefaultConfig() {
            var yaml = DataObjects.yamlserializer.Serialize(defaultColorizationSettings);
            return yaml;
        }

        public static bool UpdateYamlConfig(string yaml) {
            try {
                //Logger.LogInfo($"Updating ColorizationSettings from YAML:\n{yaml}");
                creatureColorizationSettings = DataObjects.yamldeserializer.Deserialize<DataObjects.CreatureColorizationSettings>(yaml);
                // Ensure that we load the default colorization settings, maybe we consider a merge here instead?
                foreach (var entry in defaultColorizationSettings.defaultLevelColorization) {
                    if (!creatureColorizationSettings.defaultLevelColorization.Keys.Contains(entry.Key)) {
                        creatureColorizationSettings.defaultLevelColorization.Add(entry.Key, entry.Value);
                    }
                }
                if (creatureColorizationSettings.characterColorGenerators != null) {
                    Logger.LogInfo("Running color generators");
                    foreach (var entry in creatureColorizationSettings.characterColorGenerators) {
                        Logger.LogInfo($"Building color range for {entry.Key}");
                        foreach (var colorRange in entry.Value) { BuildAddColorRange(entry.Key, colorRange); }
                    }
                    if (ValConfig.OutputColorizationGeneratorsData.Value) {
                        File.WriteAllText(Path.Combine(Paths.ConfigPath, "StarLevelSystem", "DebugGeneratedColorValues.yaml"), DataObjects.yamlserializer.Serialize(creatureColorizationSettings));
                    }
                }

                Logger.LogInfo($"Updated ColorizationSettings.");
                // This might need to be async
                foreach (var chara in Resources.FindObjectsOfTypeAll<Character>()) {
                    if (chara.m_level <= 1) { continue; }
                    CreatureDetailCache ccd = CompositeLazyCache.GetAndSetDetailCache(chara, true);
                    ApplyColorizationWithoutLevelEffects(chara.gameObject, ccd.Colorization);
                }
            } catch (System.Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse ColorizationSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }

        // Don't run the vanilla level effects since we are managing all of that ourselves
        [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.SetupLevelVisualization))]
        public static class PreventDefaultLevelSetup
        {
            public static bool Prefix() {
                return false;
            }
        }

        // Make level visuals deterministic?
        public static void ApplyLevelVisual(Character charc) {
            LevelEffects charLevelEf = charc.gameObject.GetComponentInChildren<LevelEffects>();
            if (charLevelEf == null || charLevelEf.m_levelSetups == null || charLevelEf.m_levelSetups.Count <= 0) { return; }

            // Randomly select level visualization
            LevelSetup clevelset = charLevelEf.m_levelSetups[UnityEngine.Random.Range(0, charLevelEf.m_levelSetups.Count - 1)];
            if (clevelset.m_enableObject != null) { clevelset.m_enableObject.SetActive(true); }
        }

        internal static ColorDef DetermineCharacterColorization(Character cgo, int level) {
            if (cgo == null) { return null; }
            string cname = Utils.GetPrefabName(cgo.gameObject);
            //Logger.LogDebug($"Checking for character specific colorization {cname}");
            if (creatureColorizationSettings.characterSpecificColorization.ContainsKey(cname) && creatureColorizationSettings.characterSpecificColorization[cname].ContainsKey(level - 1)) {
                if (creatureColorizationSettings.characterSpecificColorization[cname].TryGetValue((level-1), out ColorDef charspecific_color_def)) {
                    //Logger.LogDebug($"Found character specific colorization for {cname} - {level}");
                    return charspecific_color_def;
                }
            }
            return GetDefaultColorization(level-1);
        }


        internal static void ApplyColorizationWithoutLevelEffects(GameObject cgo, ColorDef colorization) {
            if (ValConfig.EnableColorization.Value == false) { return; }
            LevelSetup genlvlup = colorization.toLevelEffect();
            // Material assignment changes must occur in a try block- they can quietly crash the game otherwise
            try {
                foreach (var smr in cgo.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    Material[] sharedMaterials2 = smr.sharedMaterials;
                    if (sharedMaterials2.Length == 0) { continue; }
                    sharedMaterials2[0] = new Material(sharedMaterials2[0]);
                    sharedMaterials2[0].SetFloat("_Hue", genlvlup.m_hue);
                    sharedMaterials2[0].SetFloat("_Saturation", genlvlup.m_saturation);
                    sharedMaterials2[0].SetFloat("_Value", genlvlup.m_value);
                    if (genlvlup.m_setEmissiveColor) {
                        sharedMaterials2[0].SetColor("_EmissionColor", genlvlup.m_emissiveColor);
                    }
                    smr.sharedMaterials = sharedMaterials2;
                }
            }
            catch (Exception e) {
                Logger.LogError($"Exception while colorizing {e}");
            }
        }

        internal static void BuildAddColorRange(string creatureKey, ColorRangeDef colorGen) {
            float hueRange = Mathf.Abs(colorGen.EndColorDef.hue) + Mathf.Abs(colorGen.StartColorDef.hue);
            Mathf.Clamp(hueRange, 0f, 2f);

            float satRange = Mathf.Abs(colorGen.EndColorDef.saturation) + Mathf.Abs(colorGen.StartColorDef.saturation);
            Mathf.Clamp(satRange, 0f, 2f);

            float valRange = Mathf.Abs(colorGen.EndColorDef.value) + Mathf.Abs(colorGen.StartColorDef.value);
            Mathf.Clamp(valRange, 0f, 2f);

            int steps = colorGen.RangeEnd - colorGen.RangeStart;

            float hueStep = hueRange / steps;
            float satStep = satRange / steps;
            float valStep = valRange / steps;
            int hueDirection = colorGen.StartColorDef.hue > colorGen.EndColorDef.hue ? -1 : 1;
            int satDirection = colorGen.StartColorDef.saturation > colorGen.EndColorDef.saturation ? -1 : 1;
            int valueDirection = colorGen.StartColorDef.value > colorGen.EndColorDef.value ? -1 : 1;

            if (colorGen.CharacterSpecific && !creatureColorizationSettings.characterSpecificColorization.ContainsKey(creatureKey)) {
                creatureColorizationSettings.characterSpecificColorization.Add(creatureKey, new Dictionary<int, ColorDef>());
            }

            int currentLevel = colorGen.RangeStart;
            int currentSegment = 0;
            while(currentLevel < colorGen.RangeEnd + 1) {
                //Logger.LogDebug($"Generating ColorDef for {currentLevel}");
                ColorDef colorRangeDef = new ColorDef() {
                    hue = colorGen.StartColorDef.hue + (hueStep * currentSegment * hueDirection),
                    saturation = colorGen.StartColorDef.saturation + (satStep * currentSegment * satDirection),
                    value = colorGen.StartColorDef.value + (valStep * currentSegment * valueDirection),
                    IsEmissive = false
                };

                if (colorGen.CharacterSpecific == true) {
                    if (!creatureColorizationSettings.characterSpecificColorization.ContainsKey(creatureKey)) {
                        creatureColorizationSettings.characterSpecificColorization.Add(creatureKey , new Dictionary<int, ColorDef>());
                    }

                    if (creatureColorizationSettings.characterSpecificColorization[creatureKey].ContainsKey(currentLevel)) {
                        if (colorGen.OverwriteExisting == true) {
                            creatureColorizationSettings.characterSpecificColorization[creatureKey][currentLevel] = colorRangeDef;
                        }
                    } else {
                        creatureColorizationSettings.characterSpecificColorization[creatureKey].Add(currentLevel, colorRangeDef);
                    }
                } else {
                    if (creatureColorizationSettings.defaultLevelColorization.ContainsKey(currentLevel)) {
                        if (colorGen.OverwriteExisting == true) {
                            creatureColorizationSettings.defaultLevelColorization[currentLevel] = colorRangeDef;
                        }
                    } else {
                        creatureColorizationSettings.defaultLevelColorization.Add(currentLevel, colorRangeDef);
                    }
                }

                currentLevel++;
                currentSegment++;
            }
        }

        // TODO: move to a command?
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

        internal static ColorDef GetDefaultColorization(int level) {
            if (creatureColorizationSettings.defaultLevelColorization.ContainsKey(level)) {
                return creatureColorizationSettings.defaultLevelColorization[level];
            }  else {
                return defaultColorization;
            }
        }

        public static void UpdateMapColorSelection()
        {
            mapRingColors.Clear();
            foreach (string colorstring in ValConfig.DistanceRingColorOptions.Value.Split(',').ToList())
            {
                if (colorstring.StartsWith("#"))
                {
                    if (ColorUtility.TryParseHtmlString(colorstring.Trim(), out Color parsedColor))
                    {
                        mapRingColors.Add(parsedColor);
                        continue;
                    }
                    else
                    {
                        Logger.LogWarning($"Unable to parse color string {colorstring} for distance ring colors. It will be skipped");
                        continue;
                    }
                }
                string requestedColor = colorstring.Trim().CapitalizeFirstLetter();
                if (defaultColors.TryGetValue(requestedColor, out string htmlcolor))
                {
                    if (ColorUtility.TryParseHtmlString(htmlcolor, out Color parsedColor))
                    {
                        mapRingColors.Add(parsedColor);
                    }
                }
            }
        }
    }
}
