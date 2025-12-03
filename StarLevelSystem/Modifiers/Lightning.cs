using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Lightning
    {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, StoredCreatureDetails ccache) {
            if (ccache == null) { return; }
            if (ccache.CreatureDamageBonus.ContainsKey(DamageType.Lightning)) {
                ccache.CreatureDamageBonus[DamageType.Lightning] += config.BasePower + (config.PerlevelPower * creature.m_level);
            } else {
                ccache.CreatureDamageBonus[DamageType.Lightning] = config.BasePower + (config.PerlevelPower * creature.m_level);
            }
        }
    }
}
