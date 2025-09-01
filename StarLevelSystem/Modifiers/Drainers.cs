using HarmonyLib;
using StarLevelSystem.Data;
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
                CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(attacker);
                if (cDetails == null || cDetails.Modifiers == null) { return; }
                if (cDetails.Modifiers.Keys.Contains(ModifierNames.StaminaDrain)) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.StaminaDrain, cDetails.Modifiers[ModifierNames.StaminaDrain]);
                    __instance.UseStamina(cmcfg.basepower + (cmcfg.perlevelpower * cDetails.Level));
                }
                if (cDetails.Modifiers.Keys.Contains(ModifierNames.EitrDrain)) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.EitrDrain, cDetails.Modifiers[ModifierNames.EitrDrain]);
                    __instance.UseEitr(cmcfg.basepower + (cmcfg.perlevelpower * cDetails.Level));
                }
            }
        }
    }
}
