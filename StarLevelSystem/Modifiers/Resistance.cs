﻿using JetBrains.Annotations;
using StarLevelSystem.Data;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Resistance
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CreatureDetailCache ccache) {
            if (ccache == null) { return; }
            if (ccache.Modifiers.ContainsKey(CreatureModifiersData.ModifierNames.ResistBlunt.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Blunt] -= config.BasePower + (config.PerlevelPower * ccache.Level);
                if (ccache.DamageRecievedModifiers[DamageType.Blunt] < 0) { ccache.DamageRecievedModifiers[DamageType.Blunt] = 0; }
            }
            if (ccache.Modifiers.ContainsKey(CreatureModifiersData.ModifierNames.ResistPierce.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Pierce] -= config.BasePower + (config.PerlevelPower * ccache.Level);
                if (ccache.DamageRecievedModifiers[DamageType.Pierce] < 0) { ccache.DamageRecievedModifiers[DamageType.Pierce] = 0; }
            }
            if (ccache.Modifiers.ContainsKey(CreatureModifiersData.ModifierNames.ResistSlash.ToString())) {
                ccache.DamageRecievedModifiers[DamageType.Slash] -= config.BasePower + (config.PerlevelPower * ccache.Level);
                if (ccache.DamageRecievedModifiers[DamageType.Slash] < 0) { ccache.DamageRecievedModifiers[DamageType.Slash] = 0; }
            }
        }
    }
}
