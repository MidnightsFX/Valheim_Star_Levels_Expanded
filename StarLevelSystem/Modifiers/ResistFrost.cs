using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers {
    internal class ResistFrost {
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, CharacterCacheEntry ccache) {
            if (ccache == null) { return; }
            ccache.DamageRecievedModifiers[DamageType.Frost] -= config.BasePower + (config.PerlevelPower * creature.m_level);
            if (ccache.DamageRecievedModifiers[DamageType.Frost] < 0.20f) { ccache.DamageRecievedModifiers[DamageType.Frost] = 0.20f; }
        }
    }
}
