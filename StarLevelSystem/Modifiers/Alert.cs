using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Alert
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, StoredCreatureDetails ccache) {
            creature.m_baseAI.m_hearRange *= config.BasePower + (config.PerlevelPower * creature.m_level);
        }
    }
}
