using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    // Shrugs off stagger: every point of stagger damage the creature takes is cut by its power, half to start and a
    // little more per level, never past MaxReduction. A stagger is what opens a creature up to the double-damage hit,
    // so this is as much a defence as a nuisance.
    internal static class Stalward
    {
        // The most stagger it can ever ignore, so even a very high-level creature can still be staggered eventually.
        // Overridable via MaxReduction in the CreatureModConfig.Config dict in Modifiers.yaml.
        private const float DefaultMaxReduction = 0.9f;

        private const string StalwardKey = nameof(ModifierNames.Stalward);

        // AddStaggerDamage runs on the creature's owner, from ApplyDamage and from a block. Creatures vanilla never lets
        // stagger (m_staggerDamageFactor 0) return before the damage is used, so this changes nothing for them.
        [HarmonyPatch(typeof(Character), nameof(Character.AddStaggerDamage))]
        public static class StalwardStaggerPatch
        {
            public static void Prefix(Character __instance, ref float damage) {
                if (damage <= 0f || __instance == null || __instance.IsPlayer()) { return; }
                if (CompositeLazyCache.TryGetCreatureModifier(__instance, StalwardKey, out ModifierType type) == false) { return; }
                CreatureModConfig cfg = CreatureModifiersData.GetConfig(StalwardKey, type);
                float maxReduction = Mathf.Clamp01(cfg.Get("MaxReduction", DefaultMaxReduction));
                float reduction = Mathf.Clamp(cfg.PowerAt(__instance.m_level), 0f, maxReduction);
                damage *= 1f - reduction;
            }
        }
    }
}
