using System.Collections.Generic;

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

        public static void AddColorCombo(ColorDef colordef)
        {
            LevelColors.Add(colordef);
        }

        // List of color combinations
        internal static void AddGoodColorCombos() {
            LevelColors.Add(new ColorDef() { hue = 0.7890267f, saturation = 0.6484352f, value = 0.4952819f }); // Bright red
            LevelColors.Add(new ColorDef() { hue = 0.07130837f, saturation = 0.2f, value = 0.130073f }); // golden
        }
    }
}
