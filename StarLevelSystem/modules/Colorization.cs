using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static LevelEffects;

namespace StarLevelSystem.modules
{
    public class ColorDef
    {
        public float hue {  get; set; }
        public float saturation { get; set; }
        public float value { get; set; }
    }
    public static class Colorization
    {
        public static List<ColorDef> LevelColors = new List<ColorDef>();
        public static List<LevelEffects.LevelSetup> characterLevelEffects = new List<LevelEffects.LevelSetup>();

        public static void AddColorCombo(ColorDef colordef)
        {
            LevelColors.Add(colordef);
        }

        // List of color combinations
        internal static void AddGoodColorCombos() {
            //LevelColors.Add(new ColorDef() { hue = 0.7890267f, saturation = 0.6484352f, value = 0.4952819f }); // Bright red
            LevelColors.Add(new ColorDef() { hue = 0.07130837f, saturation = 0.2f, value = 0.130073f }); // golden
        }

        // Consider if we want to use emissive colors?
        public static void SetupLevelEffects()
        {
            // Ensure known good color combos are available
            Colorization.AddGoodColorCombos();
            bool added_color_combos = false;
            for (int level = LevelColors.Count; 101 > level; level++)
            {
                // Add all of the known good color combinations first, then generate fillers for the rest
                if (added_color_combos == false)
                {
                    foreach (ColorDef col in Colorization.LevelColors)
                    {
                        float colscale = 1f;
                        if (ValConfig.EnableCreatureScalingPerLevel.Value) { colscale += (ValConfig.PerLevelScaleBonus.Value * level); }
                        characterLevelEffects.Add(new LevelEffects.LevelSetup() { m_setEmissiveColor = false, m_scale = colscale, m_hue = col.hue, m_saturation = col.saturation, m_value = col.value });
                        Logger.LogDebug($"LevelEffects: {level} - scale:{colscale}, hue:{col.hue}, sat:{col.saturation}, val:{col.value}");
                        level++;
                    }
                    added_color_combos = true;
                }
                float scale = 1f;
                if (ValConfig.EnableCreatureScalingPerLevel.Value)
                {
                    // This flattens out at the end of the level cap, everything past that up to the max of 100 will have the same size
                    scale += (ValConfig.PerLevelScaleBonus.Value * level);
                }
                //float sat = UnityEngine.Random.Range(0f, 1f);
                float sat = UnityEngine.Random.Range(-0.5f, 0.5f);
                float hue = UnityEngine.Random.Range(-0.1f, 0.1f);
                float value = UnityEngine.Random.Range(-0.25f, 0.25f);
                Logger.LogDebug($"LevelEffects: {level} - scale:{scale}, hue:{hue}, sat:{sat}, val:{value}");
                characterLevelEffects.Add(new LevelEffects.LevelSetup() { m_setEmissiveColor = false, m_scale = scale, m_hue = hue, m_saturation = sat, m_value = value });
            }
        }

        [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.Start))]
        public static class DefineLevelupModifiers
        {
            public static void Prefix(LevelEffects __instance)
            {
                // Remove the vanilla level setups so we can add level setups for every level needed.
                //__instance.m_levelSetups.Clear();
                __instance.m_levelSetups.AddRange(characterLevelEffects);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        public static class AddLevelEffectsToEverythingElse {

            public static void Postfix(Character __instance) {
                LevelEffects default_level_effects =  __instance.gameObject.GetComponentInChildren<LevelEffects>();
                if (default_level_effects == null && __instance.m_level > 1 && __instance.m_onLevelSet == null) {
                    Logger.LogInfo($"Setting level effects for character that does not have them set {__instance.m_level}");
                    LevelSetup levelSetup = characterLevelEffects[__instance.m_level];
                    __instance.transform.localScale = new Vector3(levelSetup.m_scale, levelSetup.m_scale, levelSetup.m_scale);
                }
            }
        }
    }
}
