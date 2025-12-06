using JetBrains.Annotations;
using StarLevelSystem.Data;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class ResistBlunt
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, CharacterCacheEntry ccache)
        {
            if (ccache == null) { return; }
            ccache.DamageRecievedModifiers[DamageType.Blunt] -= config.BasePower + (config.PerlevelPower * creature.m_level);
            if (ccache.DamageRecievedModifiers[DamageType.Blunt] < 0.20f) { ccache.DamageRecievedModifiers[DamageType.Blunt] = 0.20f; }
        }
    }
}
