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
            CharacterSpecificColorization = ColorizationData.characterColorizationData,
            DefaultLevelColorization = ColorizationData.defaultColorizationData,
            CharacterColorGenerators = ColorizationData.defaultColorGenerators,
        };

        public static List<Color> mapRingColors = new List<Color>();
        public static List<Color> zoneOverlayColors = new List<Color>();
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
            // Light/dark variants used by the zone overlay heat-map gradient.
            { "LightYellow", "#ffff99" },
            { "LightOrange", "#ffc266" },
            { "DarkOrange",  "#cc6600" },
            { "LightRed",    "#ff6666" },
            { "DarkRed",     "#b30000" },
            { "LightPurple", "#e0b3ff" },
            { "DarkPurple",  "#8000b3" },
        };

        public static ColorDef defaultColorization = new ColorDef()
        {
            Hue = 0f,
            Saturation = 0f,
            Value = 0f,
            IsEmissive = false
        };

        public static void Init() {
            creatureColorizationSettings = defaultColorizationSettings;
            try {
                UpdateYamlConfig(File.ReadAllText(ValConfig.colorsFilePath));
            } catch (Exception e) { Jotunn.Logger.LogWarning($"There was an error updating the Color Level values, defaults will be used. Exception: {e}"); }
        }

        public static string YamlDefaultConfig() {
            var yaml = DataObjects.yamlSerializer.Serialize(defaultColorizationSettings);
            return yaml;
        }

        public static bool UpdateYamlConfig(string yaml) {
            try {
                //Logger.LogInfo($"Updating ColorizationSettings from YAML:\n{yaml}");
                creatureColorizationSettings = DataObjects.yamlDeserializer.Deserialize<DataObjects.CreatureColorizationSettings>(yaml);
                // Ensure that we load the default colorization settings, maybe we consider a merge here instead?
                foreach (var entry in defaultColorizationSettings.DefaultLevelColorization) {
                    if (!creatureColorizationSettings.DefaultLevelColorization.Keys.Contains(entry.Key)) {
                        creatureColorizationSettings.DefaultLevelColorization.Add(entry.Key, entry.Value);
                    }
                }
                if (creatureColorizationSettings.CharacterColorGenerators != null) {
                    Logger.LogInfo("Running color generators");
                    creatureColorizationSettings.CharacterSpecificColorization ??= new Dictionary<string, Dictionary<int, ColorDef>>();
                    foreach (var entry in creatureColorizationSettings.CharacterColorGenerators) {
                        Logger.LogInfo($"Building color range for {entry.Key}");
                        foreach (var colorRange in entry.Value) { BuildAddColorRange(entry.Key, colorRange); }
                    }
                    if (ValConfig.OutputColorizationGeneratorsData.Value) {
                        File.WriteAllText(Path.Combine(Paths.ConfigPath, "StarLevelSystem", "DebugGeneratedColorValues.yaml"), DataObjects.yamlSerializer.Serialize(creatureColorizationSettings));
                    }
                }

                Logger.LogInfo($"Updated ColorizationSettings.");
                // This might need to be async
                foreach (var chara in Resources.FindObjectsOfTypeAll<Character>()) {
                    if (chara.m_level <= 1) { continue; }


                    CharacterCacheEntry ccd = CompositeLazyCache.GetCacheEntry(chara);
                    if (ccd == null) { continue; }
                    ccd.Colorization = Colorization.DetermineCharacterColorization(chara, chara.m_level);
                    CompositeLazyCache.UpdateCharacterCacheEntry(chara, ccd);
                    ApplyColorizationWithoutLevelEffects(chara.gameObject, ccd.Colorization);
                }
            } catch (System.Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse ColorizationSettings YAML: {ex.Message}");
                StarLevelSystem.Log.LogError($"Colorization will use internal defaults.");
                creatureColorizationSettings = defaultColorizationSettings;
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
            if (creatureColorizationSettings.CharacterSpecificColorization != null && creatureColorizationSettings.CharacterSpecificColorization.ContainsKey(cname) && creatureColorizationSettings.CharacterSpecificColorization[cname].ContainsKey(level - 1)) {
                if (creatureColorizationSettings.CharacterSpecificColorization[cname].TryGetValue((level-1), out ColorDef charspecific_color_def)) {
                    //Logger.LogDebug($"Found character specific colorization for {cname} - {level}");
                    return charspecific_color_def;
                }
            }
            return GetDefaultColorization(level-1);
        }


        internal static void ApplyColorizationWithoutLevelEffects(GameObject cgo, ColorDef colorization) {
            if (ValConfig.EnableColorization.Value == false) { return; }
            if (colorization == null) { return; }
            LevelSetup genlvlup = colorization.ToLevelEffect();
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
            float hueRange = Mathf.Abs(colorGen.EndColorDef.Hue) + Mathf.Abs(colorGen.StartColorDef.Hue);
            Mathf.Clamp(hueRange, 0f, 2f);

            float satRange = Mathf.Abs(colorGen.EndColorDef.Saturation) + Mathf.Abs(colorGen.StartColorDef.Saturation);
            Mathf.Clamp(satRange, 0f, 2f);

            float valRange = Mathf.Abs(colorGen.EndColorDef.Value) + Mathf.Abs(colorGen.StartColorDef.Value);
            Mathf.Clamp(valRange, 0f, 2f);

            int steps = colorGen.RangeEnd - colorGen.RangeStart;

            float hueStep = hueRange / steps;
            float satStep = satRange / steps;
            float valStep = valRange / steps;
            int hueDirection = colorGen.StartColorDef.Hue > colorGen.EndColorDef.Hue ? -1 : 1;
            int satDirection = colorGen.StartColorDef.Saturation > colorGen.EndColorDef.Saturation ? -1 : 1;
            int valueDirection = colorGen.StartColorDef.Value > colorGen.EndColorDef.Value ? -1 : 1;

            if (colorGen.CharacterSpecific && !creatureColorizationSettings.CharacterSpecificColorization.ContainsKey(creatureKey)) {
                creatureColorizationSettings.CharacterSpecificColorization.Add(creatureKey, new Dictionary<int, ColorDef>());
            }

            int currentLevel = colorGen.RangeStart;
            int currentSegment = 0;
            while(currentLevel < colorGen.RangeEnd + 1) {
                //Logger.LogDebug($"Generating ColorDef for {currentLevel}");
                ColorDef colorRangeDef = new ColorDef() {
                    Hue = colorGen.StartColorDef.Hue + (hueStep * currentSegment * hueDirection),
                    Saturation = colorGen.StartColorDef.Saturation + (satStep * currentSegment * satDirection),
                    Value = colorGen.StartColorDef.Value + (valStep * currentSegment * valueDirection),
                    IsEmissive = false
                };

                if (colorGen.CharacterSpecific == true) {
                    if (!creatureColorizationSettings.CharacterSpecificColorization.ContainsKey(creatureKey)) {
                        creatureColorizationSettings.CharacterSpecificColorization.Add(creatureKey , new Dictionary<int, ColorDef>());
                    }

                    if (creatureColorizationSettings.CharacterSpecificColorization[creatureKey].ContainsKey(currentLevel)) {
                        if (colorGen.OverwriteExisting == true) {
                            creatureColorizationSettings.CharacterSpecificColorization[creatureKey][currentLevel] = colorRangeDef;
                        }
                    } else {
                        creatureColorizationSettings.CharacterSpecificColorization[creatureKey].Add(currentLevel, colorRangeDef);
                    }
                } else {
                    if (creatureColorizationSettings.DefaultLevelColorization.ContainsKey(currentLevel)) {
                        if (colorGen.OverwriteExisting == true) {
                            creatureColorizationSettings.DefaultLevelColorization[currentLevel] = colorRangeDef;
                        }
                    } else {
                        creatureColorizationSettings.DefaultLevelColorization.Add(currentLevel, colorRangeDef);
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

        internal static ColorDef GetDefaultColorization(int level) {
            if (creatureColorizationSettings.DefaultLevelColorization.ContainsKey(level)) {
                return creatureColorizationSettings.DefaultLevelColorization[level];
            }  else {
                return defaultColorization;
            }
        }

        public static void UpdateMapColorSelection()
        {
            ParseColorOptions(ValConfig.DistanceRingColorOptions.Value, mapRingColors, "distance ring");
        }

        public static void UpdateZoneOverlayColorSelection()
        {
            ParseColorOptions(ValConfig.ZoneOverlayColorOptions.Value, zoneOverlayColors, "zone overlay");
        }

        // Parses a comma-separated list of named colors (from defaultColors) and/or raw #hex strings
        // into the supplied target list. Shared by the distance-ring and zone-overlay palettes.
        private static void ParseColorOptions(string optionsCsv, List<Color> target, string contextLabel)
        {
            target.Clear();
            foreach (string colorstring in optionsCsv.Split(',').ToList())
            {
                if (colorstring.StartsWith("#"))
                {
                    if (ColorUtility.TryParseHtmlString(colorstring.Trim(), out Color parsedColor))
                    {
                        target.Add(parsedColor);
                        continue;
                    }
                    else
                    {
                        Logger.LogWarning($"Unable to parse color string {colorstring} for {contextLabel} colors. It will be skipped");
                        continue;
                    }
                }
                string requestedColor = colorstring.Trim().CapitalizeFirstLetter();
                if (defaultColors.TryGetValue(requestedColor, out string htmlcolor))
                {
                    if (ColorUtility.TryParseHtmlString(htmlcolor, out Color parsedColor))
                    {
                        target.Add(parsedColor);
                    }
                }
            }
        }
    }
}
