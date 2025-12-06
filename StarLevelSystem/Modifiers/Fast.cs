using JetBrains.Annotations;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal class Fast
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, CharacterCacheEntry ccache) {
            if (ccache == null) { return; }
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.Speed] += config.BasePower + (config.PerlevelPower * creature.m_level);
        }
    }
}
