using JetBrains.Annotations;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal static class Small
    {
        // Damage lost at the full shrink, and the least of the full effect a Small creature can roll. Overridable via
        // DamageReduction and MinimumRoll in the CreatureModConfig.Config dict in Modifiers.yaml; these are the fallbacks.
        private const float DefaultDamageReduction = 0.25f;
        private const float DefaultMinimumRoll = 0.3f;

        // Each Small creature shrinks by up to BasePower + PerlevelPower * level of its size and loses up to
        // DamageReduction of its damage. One roll scales both, so the smaller it came out the weaker it hits.
        [UsedImplicitly]
        public static void RunOnce(Character creature, CreatureModConfig config, CharacterCacheEntry ccache) {
            if (ccache == null || creature == null || config == null) { return; }
            float roll = GetRoll(creature, config);
            float shrink = (config.BasePower + (config.PerlevelPower * creature.m_level)) * roll;
            float damageReduction = ReadConfig(config, "DamageReduction", DefaultDamageReduction) * roll;
            // Additive like Big, so the two cancel the same way whichever runs first. DetermineScaleMultiplier
            // clamps the size; damage is clamped here so an oversized reduction cannot turn hits negative.
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.Size] -= shrink;
            ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseDamage] = Mathf.Max(0f, ccache.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseDamage] - damageReduction);
        }

        // The roll lives on the ZDO, written once by the owner. RunOnce runs again on every peer whenever the cache
        // entry is rebuilt (config sync, ownership handoff, restart), so a fresh roll each time would resize the
        // creature and change its damage under the player. Seeding from the ZDOID is no substitute: vanilla
        // renumbers ZDOIDs on every world load.
        private static float GetRoll(Character creature, CreatureModConfig config) {
            if (creature.m_nview == null || creature.m_nview.GetZDO() == null) { return 1f; }
            ZDO zdo = creature.m_nview.GetZDO();
            float stored = zdo.GetFloat(SLS_SMALL, -1f);
            if (stored >= 0f) { return stored; }
            // A non-owner leaves the roll to the owner, which makes it in the same setup pass that writes the
            // modifier, so the two normally arrive together. Until then show the full effect; this peer's size
            // comes from the owner's SLS_SIZE once that arrives, and only the owner reads the damage.
            if (creature.m_nview.IsOwner() == false) { return 1f; }
            float minimum = Mathf.Clamp01(ReadConfig(config, "MinimumRoll", DefaultMinimumRoll));
            float roll = UnityEngine.Random.Range(minimum, 1f);
            zdo.Set(SLS_SMALL, roll);
            return roll;
        }

        private static float ReadConfig(CreatureModConfig cfg, string key, float fallback) {
            if (cfg != null && cfg.Config != null && cfg.Config.TryGetValue(key, out float v)) { return v; }
            return fallback;
        }
    }
}
