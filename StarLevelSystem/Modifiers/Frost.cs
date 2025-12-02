using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Frost
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, CreatureDetailCache ccache) {
            if (ccache == null) { return; }
            if (ccache.CreatureDamageBonus.ContainsKey(DamageType.Frost)) {
                ccache.CreatureDamageBonus[DamageType.Frost] += config.BasePower + (config.PerlevelPower * ccache.Level);
            } else {
                ccache.CreatureDamageBonus[DamageType.Frost] = config.BasePower + (config.PerlevelPower * ccache.Level);
            }
        }
    }
}
