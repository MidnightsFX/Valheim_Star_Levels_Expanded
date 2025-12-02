using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Brutal
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, CreatureDetailCache ccache) {
            if (ccache == null) { return; }
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.AttackSpeed] += config.BasePower;
            ccache.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.AttackSpeedPerLevel] += (config.PerlevelPower * ccache.Level);
        }
    }
}
