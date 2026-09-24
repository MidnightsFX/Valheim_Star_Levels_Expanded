using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Alert
    {
        // Each creature's hearing range from before its first Alert setup. Setup runs again for every loaded
        // creature whenever the level config loads or syncs, and multiplying the current range compounded it
        // each time.
        private static readonly ConditionalWeakTable<BaseAI, StrongBox<float>> OriginalHearRange = new ConditionalWeakTable<BaseAI, StrongBox<float>>();

        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CharacterCacheEntry ccache) {
            if (creature == null || creature.m_baseAI == null || config == null) { return; }
            BaseAI ai = creature.m_baseAI;
            float original = OriginalHearRange.GetValue(ai, a => new StrongBox<float>(a.m_hearRange)).Value;
            ai.m_hearRange = original * (config.BasePower + (config.PerlevelPower * creature.m_level));
        }

        // Wired as the Alert TeardownEvent: taking the modifier off puts the original range back.
        [UsedImplicitly]
        public static void Teardown(Character creature) {
            if (creature == null || creature.m_baseAI == null) { return; }
            if (OriginalHearRange.TryGetValue(creature.m_baseAI, out StrongBox<float> original)) {
                creature.m_baseAI.m_hearRange = original.Value;
            }
        }
    }
}
