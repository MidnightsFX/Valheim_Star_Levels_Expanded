using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class Drainers
    {
        // How the incoming hit resolved for the player - drives how much of the drain still applies.
        private enum DrainOutcome { Clean, Dodge, Block, Parry }

        private class DrainContext
        {
            public float BaseDamage;
            public Character Attacker;
            public Dictionary<string, ModifierType> Mods;
            public DrainOutcome Outcome = DrainOutcome.Clean;
            public float PreBlockDamage;
        }

        // Keyed on the HitData reference: the SAME hit object flows through RPC_Damage Prefix ->
        // Humanoid.BlockAttack -> RPC_Damage Postfix, so we can carry per-hit state without any
        // shared/static scalar that could collide across peers or reentrant deflection hits.
        private static readonly ConditionalWeakTable<HitData, DrainContext> _pending = new ConditionalWeakTable<HitData, DrainContext>();

        // Fraction of the drain removed for each outcome. Overridable per-drainer via the
        // CreatureModConfig.Config dict in Modifiers.yaml; these are the fallbacks.
        private const float DefaultBlockReduction = 0.25f;
        private const float DefaultParryReduction = 0.75f;
        private const float DefaultDodgeReduction = 1.0f;

        private const string StaminaDrainName = nameof(ModifierNames.StaminaDrain);
        private const string EitrDrainName = nameof(ModifierNames.EitrDrain);

        private static bool HasDrainMod(Dictionary<string, ModifierType> mods)
        {
            return mods != null && (mods.ContainsKey(StaminaDrainName) || mods.ContainsKey(EitrDrainName));
        }

        private static float ReadConfig(CreatureModConfig cfg, string key, float fallback)
        {
            if (cfg != null && cfg.Config != null && cfg.Config.TryGetValue(key, out float v)) { return v; }
            return fallback;
        }

        private static float ReductionFor(CreatureModConfig cfg, DrainOutcome outcome)
        {
            switch (outcome)
            {
                case DrainOutcome.Parry: return ReadConfig(cfg, "ParryReduction", DefaultParryReduction);
                case DrainOutcome.Block: return ReadConfig(cfg, "BlockReduction", DefaultBlockReduction);
                case DrainOutcome.Dodge: return ReadConfig(cfg, "DodgeReduction", DefaultDodgeReduction);
                default: return 0f;
            }
        }

        private static void ApplyDrain(Character target, DrainContext ctx, string modName, bool stamina)
        {
            if (!ctx.Mods.ContainsKey(modName)) { return; }
            CreatureModConfig cfg = CreatureModifiersData.GetConfig(modName, ctx.Mods[modName]);
            float power = cfg.BasePower + (cfg.PerlevelPower * ctx.Attacker.m_level);
            float reduction = ReductionFor(cfg, ctx.Outcome);
            float drain = ctx.BaseDamage * power * (1f - reduction);
            if (drain <= 0f)
            {
                if (Logger.IsDebugEnabled) { Logger.LogDebug($"{modName}: outcome {ctx.Outcome} -> no drain"); }
                return;
            }
            if (stamina)
            {
                if (Logger.IsDebugEnabled) { Logger.LogDebug($"Draining Stamina from target {drain} (outcome {ctx.Outcome})"); }
                target.UseStamina(drain);
            }
            else
            {
                if (Logger.IsDebugEnabled) { Logger.LogDebug($"Draining Eitr from target {drain} (outcome {ctx.Outcome})"); }
                target.UseEitr(drain);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        public static class ModifierDrain
        {
            // Snapshot the attack BEFORE Valheim mutates the hit (block/resistance/armor) so the drain
            // is based on the attack's own power, and classify dodge here (RPC_Damage early-returns on it).
            private static void Prefix(HitData hit, Character __instance)
            {
                if (hit == null || hit.m_attacker == null || __instance == null) { return; }
                // Only the ZDO owner actually applies damage (Character.RPC_Damage bails for non-owners).
                if (__instance.m_nview == null || !__instance.m_nview.IsOwner()) { return; }
                Character attacker = hit.GetAttacker();
                if (attacker == null || attacker.IsPlayer()) { return; }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(attacker);
                if (!HasDrainMod(mods)) { return; }

                // States where RPC_Damage returns before touching damage at all -> no drain whatsoever.
                if (__instance.GetHealth() <= 0f || __instance.IsDead() || __instance.IsTeleporting() || __instance.InCutscene()) { return; }

                DrainContext ctx = new DrainContext
                {
                    Attacker = attacker,
                    Mods = mods,
                    BaseDamage = hit.m_damage.GetTotalDamageOptions(include_poison: true, include_spirit: true),
                    Outcome = (hit.m_dodgeable && __instance.IsDodgeInvincible()) ? DrainOutcome.Dodge : DrainOutcome.Clean,
                };
                _pending.Remove(hit);
                _pending.Add(hit, ctx);
            }

            private static void Postfix(HitData hit, Character __instance)
            {
                if (hit == null || !_pending.TryGetValue(hit, out DrainContext ctx)) { return; }
                _pending.Remove(hit);
                if (ctx.Attacker == null || __instance == null) { return; }

                ApplyDrain(__instance, ctx, StaminaDrainName, stamina: true);
                ApplyDrain(__instance, ctx, EitrDrainName, stamina: false);
            }
        }

        [HarmonyPatch(typeof(Humanoid), "BlockAttack", new Type[] { typeof(HitData), typeof(Character) })]
        public static class ModifierDrainBlock
        {
            public static void Prefix(HitData hit, Humanoid __instance)
            {
                if (hit == null || !_pending.TryGetValue(hit, out DrainContext ctx)) { return; }
                if (ctx.Outcome != DrainOutcome.Clean) { return; }
                ctx.PreBlockDamage = hit.GetTotalBlockableDamage();
            }

            public static void Postfix(HitData hit, Humanoid __instance)
            {
                if (hit == null || __instance == null || !_pending.TryGetValue(hit, out DrainContext ctx)) { return; }
                if (ctx.Outcome != DrainOutcome.Clean) { return; }
                // Only a block that actually mitigated damage counts; classify perfect (parry) vs normal
                // exactly as Humanoid.BlockAttack does (m_timedBlockBonus > 1 && m_blockTimer in [0, 0.25)).
                bool held = hit.GetTotalBlockableDamage() < ctx.PreBlockDamage - 0.01f;
                if (!held) { return; }
                ItemDrop.ItemData blocker = __instance.GetCurrentBlocker();
                bool perfect = blocker != null
                    && blocker.m_shared.m_timedBlockBonus > 1f
                    && __instance.m_blockTimer != -1f
                    && __instance.m_blockTimer < 0.25f;
                ctx.Outcome = perfect ? DrainOutcome.Parry : DrainOutcome.Block;
            }
        }
    }
}
