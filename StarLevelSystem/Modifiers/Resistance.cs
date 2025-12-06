using JetBrains.Annotations;
using StarLevelSystem.Data;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    // Note this is a duplicate and here for compatibility, it will be removed in the future
    internal static class Resistance
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CharacterCacheEntry ccache)
        {
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(creature);
            if (ccache == null || mods == null) { return; }
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistBlunt.ToString()))
            {
                ccache.DamageRecievedModifiers[DamageType.Blunt] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Blunt] < 0.20f) { ccache.DamageRecievedModifiers[DamageType.Blunt] = 0.20f; }
            }
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistPierce.ToString()))
            {
                ccache.DamageRecievedModifiers[DamageType.Pierce] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Pierce] < 0.20f) { ccache.DamageRecievedModifiers[DamageType.Pierce] = 0.20f; }
            }
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistSlash.ToString()))
            {
                ccache.DamageRecievedModifiers[DamageType.Slash] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Slash] < 0.20f) { ccache.DamageRecievedModifiers[DamageType.Slash] = 0.20f; }
            }
        }
    }
}