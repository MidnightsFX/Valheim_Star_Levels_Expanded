using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal class Fast
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CreatureDetailCache ccache) {
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.Speed] += config.basepower + (config.perlevelpower * ccache.Level);
        }
    }
}
