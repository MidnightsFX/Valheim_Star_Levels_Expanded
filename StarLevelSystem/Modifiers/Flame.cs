using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Flame
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CreatureDetailCache ccache) {
            if (ccache.CreatureDamageBonus.ContainsKey(DamageType.Fire)) {
                ccache.CreatureDamageBonus[DamageType.Fire] += config.BasePower + (config.PerlevelPower * ccache.Level);
            } else {
                ccache.CreatureDamageBonus[DamageType.Fire] = config.BasePower + (config.PerlevelPower * ccache.Level);
            }
        }
    }
}
