using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Poison
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, StoredCreatureDetails ccache) {
            if (ccache == null) { return; }
            if (ccache.CreatureDamageBonus.ContainsKey(DamageType.Poison)) {
                ccache.CreatureDamageBonus[DamageType.Poison] += config.BasePower + (config.PerlevelPower * creature.m_level);
            } else {
                ccache.CreatureDamageBonus[DamageType.Poison] = config.BasePower + (config.PerlevelPower * creature.m_level);
            }
        }
    }
}
