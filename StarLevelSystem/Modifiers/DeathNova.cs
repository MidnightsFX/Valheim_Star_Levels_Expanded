using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Shared;
using StarLevelSystem.modules.Modifiers;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers
{
    internal static class DeathNova
    {
        private enum NovaElement { Fire, Poison, Frost, Lightning }

        // Each nova's ground circle colour (NovaIndicator), keyed by its SecondaryEffect.
        private static readonly Dictionary<string, Color> IndicatorTints = new Dictionary<string, Color>() {
            { "DeathFireNova", new Color(1f, 0.55f, 0.15f) },
            { "DeathPoisonNova", new Color(0.45f, 0.9f, 0.3f) },
            { "DeathFrostNova", new Color(0.6f, 0.85f, 1f) },
            { "DeathLightningNova", new Color(0.7f, 0.5f, 1f) },
        };

        // Puts the optional ground circle on every loaded nova prefab. Called after each modifier prefab load; the novas
        // are networked, so every instance any peer gets is a copy of this prefab and carries the circle with it.
        internal static void AttachIndicators() {
            foreach (KeyValuePair<string, Color> nova in IndicatorTints) {
                if (CreatureModifiersData.LoadedSecondaryEffects.TryGetValue(nova.Key, out GameObject prefab) == false || prefab == null) { continue; }
                NovaIndicator indicator = prefab.GetComponent<NovaIndicator>();
                if (indicator == null) { indicator = prefab.AddComponent<NovaIndicator>(); }
                indicator.Tint = nova.Value;
            }
            // A nova prefab loaded in-world (a config reload or a server's config) has just had its mocks resolved back
            // to the vanilla bursts; point it at NovaBurstFx's copies again.
            UseNovaBursts();
        }

        // Points the frost and lightning novas' bursts at NovaBurstFx's copies -- the single-wave fenring nova and the
        // shrunk Eikthyr stomp -- in place of the vanilla prefabs their mocks resolve to. Before the first world load
        // there are no copies yet, and NovaBurstFx.Register calls this once they are built.
        internal static void UseNovaBursts() {
            SwapBurst("DeathFrostNova", "fx_fenring_icenova", NovaBurstFx.Registered(NovaBurstFx.FrostTemplate));
            SwapBurst("DeathLightningNova", "fx_lightningstaffprojectile_hit", NovaBurstFx.Registered(NovaBurstFx.LightningTemplate));
        }

        private static void SwapBurst(string nova, string vanillaBurst, GameObject replacement) {
            if (replacement == null || CreatureModifiersData.LoadedSecondaryEffects.TryGetValue(nova, out GameObject prefab) == false || prefab == null) { return; }
            Aoe aoe = prefab.GetComponent<Aoe>();
            if (aoe == null || aoe.m_initiateEffect == null || aoe.m_initiateEffect.m_effectPrefabs == null) { return; }
            foreach (EffectList.EffectData effect in aoe.m_initiateEffect.m_effectPrefabs) {
                if (effect.m_prefab != null && effect.m_prefab.name.EndsWith(vanillaBurst)) { effect.m_prefab = replacement; }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class DeathNovaOnDeathPatch
        {
            private static void Prefix(Character __instance) {
                if (__instance == null || __instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.IsOwner() == false) {
                    return;
                }
                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance);
                if (mods == null) { return; }
                // Each nova resolves independently: an unconfigured FireNova must not stop a PoisonNova
                // on the same creature (the old early-returns did exactly that).
                TriggerNova(__instance, mods, ModifierNames.FireNova.ToString(), bluntFraction: 0.25f, NovaElement.Fire, elementFraction: 0.5f);
                // Fire is applied multiple times so its fraction is a diminished return
                TriggerNova(__instance, mods, ModifierNames.PoisonNova.ToString(), bluntFraction: 0.16f, NovaElement.Poison, elementFraction: 1f);
                TriggerNova(__instance, mods, ModifierNames.FrostNova.ToString(), bluntFraction: 0.25f, NovaElement.Frost, elementFraction: 0.5f);
                TriggerNova(__instance, mods, ModifierNames.LightningNova.ToString(), bluntFraction: 0.16f, NovaElement.Lightning, elementFraction: 0.5f);
            }

            private static void TriggerNova(Character chara, Dictionary<string, ModifierType> mods, string novaName, float bluntFraction, NovaElement element, float elementFraction) {
                if (mods.TryGetValue(novaName, out ModifierType modType) == false) { return; }
                CreatureModConfig cmcfg = CreatureModifierOverrides.GetEffectiveConfig(chara, novaName, modType);
                if (cmcfg == null) { return; }
                if (ModifierDefinitions.TryGetValue(novaName, out var novaDef) == false) { return; }
                GameObject go = CreatureModifiers.ApplySecondaryVFX(novaDef.SecondaryEffect, chara.transform.position, chara.transform.rotation);
                if (go == null) { return; }
                go.SetActive(false);
                Aoe aoe = go.GetComponent<Aoe>();
                // Configure damage. The nova's Aoe has no owner, so its hits carry no attacker, and vanilla only applies
                // the world's enemy damage rate to hits from a creature, so it is applied here.
                float dmgmod = (cmcfg.BasePower + (cmcfg.PerlevelPower * chara.m_level)) * Game.m_enemyDamageRate;
                if (aoe) {
                    float characterdmg = SLSExtensions.EstimateCharacterDamage(chara, DamageEstimateType.Average);
                    if (characterdmg <= 0) {
                        characterdmg = 10 * chara.m_level; // fallback to a base damage if for some reason we can't get an estimate
                    }
                    float total = characterdmg * dmgmod;
                    if (bluntFraction > 0) { aoe.m_damage.m_blunt = Mathf.Clamp(total * bluntFraction, 0f, 5000f); }
                    float elemental = Mathf.Clamp(total * elementFraction, 0f, 5000f);
                    switch (element) {
                        case NovaElement.Fire: aoe.m_damage.m_fire = elemental; break;
                        case NovaElement.Poison: aoe.m_damage.m_poison = elemental; break;
                        case NovaElement.Frost: aoe.m_damage.m_frost = elemental; break;
                        case NovaElement.Lightning: aoe.m_damage.m_lightning = elemental; break;
                    }
                    Logger.LogDebug($"Activating {novaName} m:{dmgmod} x c:{characterdmg} = {total}");
                }

                go.SetActive(true);
            }
        }
    }
}
