using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Frost
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CreatureDetailCache ccache) {
            if (ccache.CreatureDamageBonus.ContainsKey(DamageType.Frost)) {
                ccache.CreatureDamageBonus[DamageType.Frost] += config.basepower + (config.perlevelpower * ccache.Level);
            } else {
                ccache.CreatureDamageBonus[DamageType.Frost] = config.basepower + (config.perlevelpower * ccache.Level);
            }
        }
    }
}
