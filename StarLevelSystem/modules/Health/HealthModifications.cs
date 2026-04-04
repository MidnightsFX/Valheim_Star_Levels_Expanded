using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Health {
    internal static class HealthModifications {

        internal static void ApplyHealthModifications(Character chara, CharacterCacheEntry cDetails) {
            float chealth = chara.m_health; // base creature health not current total health
            if (!chara.IsPlayer() && Game.m_worldLevel > 0) {
                chealth *= (float)Game.m_worldLevel * Game.instance.m_worldLevelEnemyHPMultiplier;
            }

            if (cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth] != 1 || cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.HealthPerLevel] > 0) {
                float basehp = chealth * cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseHealth];
                float perlvlhp = (chealth * cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.HealthPerLevel]) * (chara.GetLevel() - 1);
                float hp = (basehp + perlvlhp);
                chara.SetMaxHealth(hp);
                //Logger.LogDebug($"Setting max HP to: {hp} = {basehp} + {perlvlhp} | base: {chara.m_health} * difficulty = {chealth}");
            } else {
                if (chara.IsBoss()) {
                    chealth *= ValConfig.BossEnemyHealthMultiplier.Value;
                    //Logger.LogDebug($"Setting max HP to: {chara.m_health} * {ValConfig.BossEnemyHealthMultiplier.Value} = {chealth}");
                } else {
                    chealth *= ValConfig.EnemyHealthMultiplier.Value;
                    //Logger.LogDebug($"Setting max HP to: {chara.m_health} * {ValConfig.EnemyHealthMultiplier.Value} = {chealth}");
                }
                chara.SetMaxHealth(chealth);
            }
        }
    }
}
