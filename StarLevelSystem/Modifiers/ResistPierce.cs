using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class ResistPierce
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, StoredCreatureDetails ccache)
        {
            if (ccache == null) { return; }
            ccache.DamageRecievedModifiers[DamageType.Pierce] -= config.BasePower + (config.PerlevelPower * creature.m_level);
            if (ccache.DamageRecievedModifiers[DamageType.Pierce] < 0.20f) { ccache.DamageRecievedModifiers[DamageType.Pierce] = 0.20f; }
        }
    }
}
