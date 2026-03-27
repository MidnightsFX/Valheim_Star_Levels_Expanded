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
        public static void Setup(Character creature, CreatureModConfig config, CharacterCacheEntry ccache) {
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(creature);
            if (ccache == null || mods == null) { return; }
            float cap = 0.2f;
            if (config.Config != null && config.Config.ContainsKey(SLS_MOD_CAP)) {
                cap = config.Config[SLS_MOD_CAP];
            }
            // Physicals
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistBlunt.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Blunt] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Blunt] < cap) { ccache.DamageRecievedModifiers[DamageType.Blunt] = cap; }
            }
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistPierce.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Pierce] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Pierce] < cap) { ccache.DamageRecievedModifiers[DamageType.Pierce] = cap; }
            }
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistSlash.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Slash] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Slash] < cap) { ccache.DamageRecievedModifiers[DamageType.Slash] = cap; }
            }

            // Elementals
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistFire.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Fire] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Fire] < cap) { ccache.DamageRecievedModifiers[DamageType.Fire] = cap; }
            }
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistFrost.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Frost] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Frost] < cap) { ccache.DamageRecievedModifiers[DamageType.Frost] = cap; }
            }
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistPoison.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Poison] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Poison] < cap) { ccache.DamageRecievedModifiers[DamageType.Poison] = cap; }
            }
            if (mods.ContainsKey(CreatureModifiersData.ModifierNames.ResistSpirit.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Spirit] -= config.BasePower + (config.PerlevelPower * creature.m_level);
                if (ccache.DamageRecievedModifiers[DamageType.Spirit] < cap) { ccache.DamageRecievedModifiers[DamageType.Spirit] = cap; }
            }
        }
    }
}