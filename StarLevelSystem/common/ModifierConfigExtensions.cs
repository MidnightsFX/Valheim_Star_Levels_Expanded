using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    // Readers for a modifier's CreatureModConfig. A top-level class because CreatureModConfig is nested in
    // DataObjects, and C# only allows extension methods in non-nested static classes.
    internal static class ModifierConfigExtensions
    {
        // A value from the free-form Config dictionary in Modifiers.yaml, or fallback when the entry, its
        // dictionary or the key is missing. Keys an admin leaves out keep the modifier's built-in default.
        internal static float Get(this CreatureModConfig cfg, string key, float fallback) {
            if (cfg != null && cfg.Config != null && cfg.Config.TryGetValue(key, out float v)) { return v; }
            return fallback;
        }

        // The modifier's power for a creature of this level: BasePower + PerlevelPower * level, where level is
        // Character.m_level (stars + 1), the same formula every existing modifier uses.
        internal static float PowerAt(this CreatureModConfig cfg, int level) {
            if (cfg == null) { return 0f; }
            return cfg.BasePower + (cfg.PerlevelPower * level);
        }
    }
}
