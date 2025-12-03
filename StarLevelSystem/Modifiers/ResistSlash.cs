using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class ResistSlash
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, StoredCreatureDetails ccache)
        {
            if (ccache == null) { return; }
            ccache.DamageRecievedModifiers[DamageType.Slash] -= config.BasePower + (config.PerlevelPower * creature.m_level);
            if (ccache.DamageRecievedModifiers[DamageType.Slash] < 0.20f) { ccache.DamageRecievedModifiers[DamageType.Slash] = 0.20f; }
        }
    }
}
