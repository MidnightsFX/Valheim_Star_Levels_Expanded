using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    // Knits itself back together once it has been left alone for a few seconds: power x its maximum health every second
    // it goes unhurt past OutOfCombatDelay.
    internal static class Regenerating
    {
        // Seconds without taking damage before it starts to heal. Overridable via OutOfCombatDelay in the
        // CreatureModConfig.Config dict in Modifiers.yaml.
        private const float DefaultOutOfCombatDelay = 4f;
        // How often vanilla's regeneration acts: BaseAI resets m_regenTimer once it passes this many seconds.
        private const float RegenTick = 2f;

        private const string RegeneratingKey = nameof(ModifierNames.Regenerating);

        // Rides vanilla's own regeneration rather than a component of its own: UpdateRegeneration only runs on the
        // creature's owner, after BaseAI's validity checks, and it follows the creature when ownership moves.
        // m_regenTimer reads exactly 0 only on the frame vanilla has just ticked, so this heals once per tick and every
        // other frame costs one comparison.
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateRegeneration))]
        public static class RegeneratingTickPatch
        {
            public static void Postfix(BaseAI __instance) {
                if (__instance.m_regenTimer != 0f) { return; }
                Character creature = __instance.m_character;
                if (creature == null || creature.IsPlayer() || creature.IsDead()) { return; }
                float maxHealth = creature.GetMaxHealth();
                if (creature.GetHealth() >= maxHealth) { return; }
                if (CompositeLazyCache.TryGetCreatureModifier(creature, RegeneratingKey, out ModifierType type) == false) { return; }
                CreatureModConfig cfg = CreatureModifiersData.GetConfig(RegeneratingKey, type);
                // Vanilla resets m_timeSinceHurt on every hit that hurts, damage-over-time ticks included.
                if (__instance.m_timeSinceHurt < cfg.Get("OutOfCombatDelay", DefaultOutOfCombatDelay)) { return; }
                float power = cfg.PowerAt(creature.m_level);
                if (power <= 0f) { return; }
                creature.Heal(maxHealth * power * RegenTick, true);
            }
        }
    }
}
