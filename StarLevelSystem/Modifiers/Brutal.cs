using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Brutal
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CreatureDetailCache ccache)
        {
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] += config.basepower;
            ccache.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.AttackSpeedPerLevel] += (config.perlevelpower * ccache.Level);
        }
    }
}
