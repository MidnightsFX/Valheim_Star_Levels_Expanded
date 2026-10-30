using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    // Drinks the life it takes: each landed hit heals it by power x the health its target actually lost, measured after
    // armour, resistances, a block, a shield and every other modifier have had their say.
    internal static class Vampiric
    {
        private const string VampiricKey = nameof(ModifierNames.Vampiric);

        internal struct Feeding
        {
            internal Character Attacker;
            internal float HealthBefore;
            internal float Power;
        }

        // ApplyDamage is the one place the target's health drops, and it runs on the target's owner. The attacker may be
        // owned by another peer; Heal routes to whoever owns it. Hits without an attacker never feed it: the death novas,
        // and fire, poison and spirit damage, which vanilla turns into status effects whose ticks carry no attacker.
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        public static class VampiricFeedPatch
        {
            public static void Prefix(Character __instance, HitData hit, out Feeding __state) {
                __state = default;
                if (__instance == null || hit == null || hit.HaveAttacker() == false) { return; }
                Character attacker = hit.GetAttacker();
                if (attacker == null || attacker == __instance || attacker.IsPlayer() || attacker.IsDead()) { return; }
                if (CompositeLazyCache.TryGetCreatureModifier(attacker, VampiricKey, out ModifierType type) == false) { return; }
                float power = Mathf.Clamp01(CreatureModifierOverrides.GetEffectiveConfig(attacker, VampiricKey, type).PowerAt(attacker.m_level));
                if (power <= 0f) { return; }
                __state = new Feeding() { Attacker = attacker, HealthBefore = __instance.GetHealth(), Power = power };
            }

            public static void Postfix(Character __instance, Feeding __state) {
                if (__state.Attacker == null || __state.Attacker.IsDead()) { return; }
                float lost = __state.HealthBefore - Mathf.Max(0f, __instance.GetHealth());
                if (lost <= 0f) { return; }
                __state.Attacker.Heal(lost * __state.Power, true);
            }
        }
    }
}
