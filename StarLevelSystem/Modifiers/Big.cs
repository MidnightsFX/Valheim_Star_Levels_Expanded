using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal class Big
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CreatureDetailCache ccache) {
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.Size] += config.BasePower + (config.PerlevelPower * ccache.Level);
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth] += config.BasePower + (config.PerlevelPower * ccache.Level);
        }
    }
}
