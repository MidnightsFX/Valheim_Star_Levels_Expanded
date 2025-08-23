using HarmonyLib;
using StarLevelSystem.Data;
using System.Linq;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class StaminaDrain
    {
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class StaminaModifierDrain
        {
            private static void Postfix(HitData hit, Character __instance)
            {
                // If the attacker has a damage modification, apply it to damage done
                if (hit == null || hit.m_attacker == null || __instance == null) { return; }
                
                 CreatureDetailCache cDetails = CompositeLazyCache.GetAndSetDetailCache(hit.GetAttacker());
                if (cDetails != null && cDetails.Modifiers != null && cDetails.Modifiers.Keys.Contains("StaminaDrain")) {
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig("StaminaDrain", cDetails.Modifiers["StaminaDrain"]);
                    __instance.UseStamina(cmcfg.basepower + (cmcfg.perlevelpower * cDetails.Level));
                }
            }
        }
    }
}
