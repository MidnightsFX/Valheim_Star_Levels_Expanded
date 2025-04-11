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
            // vanilla color 1
            // vanilla color 2
            //LevelColors.Add(new ColorDef() { hue = 0.7890267f, saturation = 0.6484352f, value = 0.4952819f }); // Bright red
            LevelColors.Add(new ColorDef() { hue = 0.07130837f, saturation = 0.2f, value = 0.130073f }); // golden
            LevelColors.Add(new ColorDef() { hue = -0.07488244f, saturation = 0.4406867f, value = 0.01721987f }); // light gold
            LevelColors.Add(new ColorDef() { hue = -0.04446793f, saturation = 0.05205864f, value = -0.191282f }); // dark
            LevelColors.Add(new ColorDef() { hue = 0.08774569f, saturation = -0.3959962f, value = 0.224104f }); // pastel pink
            LevelColors.Add(new ColorDef() { hue = -0.05712423f, saturation = 0.09905273f, value = -0.1369882f }); // very light red
            LevelColors.Add(new ColorDef() { hue = 0.08566696f, saturation = -0.3398137f, value = -0.2270744f }); // light bronze
            LevelColors.Add(new ColorDef() { hue = 0.03924248f, saturation = -0.04047304f, value = -0.1966226f }); // light red?
            LevelColors.Add(new ColorDef() { hue = -0.3575756f, saturation = 0.09342755f, value = 0.3008582f }); // light red?
            LevelColors.Add(new ColorDef() { hue = 0.08566696f, saturation = -0.3398137f, value = -0.2270744f }); // green eyes, black wolf
        }

        // Consider if we want to use emissive colors?
        public static void SetupLevelEffects()
        {
            // Ensure known good color combos are available
            Colorization.AddGoodColorCombos();
            bool added_color_combos = false;
            for (int level = LevelColors.Count; ValConfig.MaxLevel.Value + 2 > level; level++)
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
                float sat = UnityEngine.Random.Range(-0.2f, 0.2f);
                float hue = UnityEngine.Random.Range(-0.4f, 0.4f);
                float value = UnityEngine.Random.Range(-0.75f, 0.75f);
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
