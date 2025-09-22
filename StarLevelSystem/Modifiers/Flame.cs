﻿using JetBrains.Annotations;
using StarLevelSystem.API;
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
            if (ccache == null) { return; }
            if (ccache.CreatureDamageBonus.ContainsKey(DamageType.Fire)) {
                ccache.CreatureDamageBonus[DamageType.Fire] += config.BasePower + (config.PerlevelPower * ccache.Level);
            } else {
                ccache.CreatureDamageBonus[DamageType.Fire] = config.BasePower + (config.PerlevelPower * ccache.Level);
            }
        }
    }
}
