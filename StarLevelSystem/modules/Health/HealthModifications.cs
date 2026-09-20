using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Health {
    internal static class HealthModifications {

        internal static bool ForceUpdateHealth = false;

        // For all methods that immediately update health
        internal static void ForceApplyHealthModifications(Character chara, CharacterCacheEntry cDetails) {
            ForceUpdateHealth = true;
            ApplyHealthModifications(chara, cDetails);
            ForceUpdateHealth = false;
        }

        internal static void ApplyHealthModifications(Character chara, CharacterCacheEntry cDetails) {
            float chealth = chara.m_health; // base creature health not current total health
            if (!chara.IsPlayer() && Game.m_worldLevel > 0) {
                chealth *= (float)Game.m_worldLevel * Game.instance.m_worldLevelEnemyHPMultiplier;
            }

            float currentMaxHealth = chara.GetMaxHealth();
            float maxHealthBase = chara.GetMaxHealthBase();
            // Bosses are not special-cased here: DetermineCharacterPerLevelStats already resolved
            // HealthPerLevel from the boss config entry for anything IsBoss(). Applying the per-level value
            // unconditionally is also what keeps a zero HealthPerLevel meaning "no bonus per star" rather
            // than "no health" - the old shortcut branch multiplied base health by the per-level value and
            // handed a creature configured at 0 per star a max health of 0.
            float basehp = chealth * cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth];
            float perlvlhp = (chealth * cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.HealthPerLevel]) * (chara.GetLevel() - 1);
            float targetCreatureHealth = basehp + perlvlhp;
            //Logger.LogDebug($"Setting max HP to: {targetCreatureHealth} = {basehp} + {perlvlhp} | base: {chara.m_health} * difficulty = {chealth}");

            if (ForceUpdateHealth || currentMaxHealth != targetCreatureHealth) {
                float vanillaMaxHealth = maxHealthBase * (float)chara.GetLevel();

                // Set creature health only if it is the current vanilla health and not the default for SLS | or if the override is set
                if (ForceUpdateHealth || ValConfig.OverrideCreatureModifiedHealth.Value || currentMaxHealth == maxHealthBase) {
                    // Bool check, instead of computing the localization string every time.
                    if (ValConfig.EnableDebugMode.Value) {
                        Logger.LogDebug($"Creature {Localization.instance.Localize(cDetails.CreatureNameLocalizable)} HP does not match target: current:{currentMaxHealth} (base {maxHealthBase}) != {targetCreatureHealth} | vanilla {vanillaMaxHealth} | Set to {targetCreatureHealth}");
                    }
                    // Preserve any damage taken before setup ran (spawn-window hits, or combat damage on
                    // mid-game modifier re-application). Capture before SetMaxHealth so the delta is correct.
                    float currentHealth = chara.GetHealth();
                    float damageTaken = currentMaxHealth - currentHealth;

                    chara.SetMaxHealth(targetCreatureHealth);

                    if (damageTaken > 0f) {
                        // No clamp — if pre-setup damage exceeds new max, let the creature die naturally.
                        float setHealth = targetCreatureHealth - damageTaken;
                        if (ValConfig.EnableDebugMode.Value && setHealth <= 0f) {
                            Logger.LogDebug($"{Localization.instance.Localize(cDetails.CreatureNameLocalizable)} - lvl:{chara.m_level} will die from health update. New health: {setHealth}, targetHealth: {targetCreatureHealth}, damageTaken: {damageTaken}");
                        }
                        chara.SetHealth(setHealth);
                    } else {
                        chara.Heal(targetCreatureHealth);
                    }
                }
            }
        }
    }
}
