using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Poison
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CreatureDetailCache ccache)
        {
            if (ccache.CreatureDamageBonus.ContainsKey(DamageType.Poison)) {
                ccache.CreatureDamageBonus[DamageType.Poison] += config.basepower + (config.perlevelpower * ccache.Level);
            } else {
                ccache.CreatureDamageBonus[DamageType.Poison] = config.basepower + (config.perlevelpower * ccache.Level);
            }
        }
    }
}
