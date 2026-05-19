using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Linq;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class Drainers
    {
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        public static class ModifierDrain
        {
            private static void Postfix(HitData hit, Character __instance)
            {
                // If the attacker has a damage modification, apply it to damage done
                if (hit == null || hit.m_attacker == null || __instance == null) { return; }
                Character attacker = hit.GetAttacker();
                if (attacker == null || attacker.IsPlayer()) { return; }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(attacker);
                if (mods == null) { return; }
                if (mods.Keys.Contains(ModifierNames.StaminaDrain.ToString())) {
                    float dmgtotal = hit.m_damage.GetTotalDamageOptions(include_poison: true, include_spirit: true);
                    
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.StaminaDrain.ToString(), mods[ModifierNames.StaminaDrain.ToString()]);
                    float drain = dmgtotal * (cmcfg.BasePower + (cmcfg.PerlevelPower * attacker.m_level));
                    Logger.LogDebug($"Draining Stamina from target {drain}");
                    __instance.UseStamina(drain);
                }
                if (mods.Keys.Contains(ModifierNames.EitrDrain.ToString())) {
                    float dmgtotal = hit.m_damage.GetTotalDamageOptions(include_poison: true, include_spirit: true);
                    CreatureModConfig cmcfg = CreatureModifiersData.GetConfig(ModifierNames.EitrDrain.ToString(), mods[ModifierNames.EitrDrain.ToString()]);
                    float drain = dmgtotal * (cmcfg.BasePower + (cmcfg.PerlevelPower * attacker.m_level));
                    Logger.LogDebug($"Draining Eitr from target {drain}");
                    __instance.UseEitr(drain);
                }
            }
        }
    }
}
