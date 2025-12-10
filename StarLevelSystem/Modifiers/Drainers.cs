using HarmonyLib;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Linq;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class Drainers
    {
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class ModifierDrain
        {
            private static void Postfix(HitData hit, Character __instance)
            {
                // If the attacker has a damage modification, apply it to damage done
                if (hit == null || hit.m_attacker == null || __instance == null) { return; }
                Character attacker = hit.GetAttacker();
                if (attacker == null || attacker.IsPlayer()) { return; }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(attacker);
                if ( mods == null) { return; }
                if (mods.Keys.Contains(ModifierNames.StaminaDrain.ToString())) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.StaminaDrain.ToString(), mods[ModifierNames.StaminaDrain.ToString()]);
                    __instance.UseStamina(cmcfg.BasePower + (cmcfg.PerlevelPower * attacker.m_level));
                }
                if (mods.Keys.Contains(ModifierNames.EitrDrain.ToString())) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.EitrDrain.ToString(), mods[ModifierNames.EitrDrain.ToString()]);
                    __instance.UseEitr(cmcfg.BasePower + (cmcfg.PerlevelPower * attacker.m_level));
                }
            }
        }
    }
}
