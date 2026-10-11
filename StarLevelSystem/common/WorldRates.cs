using UnityEngine;

namespace StarLevelSystem.common
{
    // The world's vanilla world modifiers (Combat, Resources, ...) that reach things SLS does itself, so they keep
    // doing what the world was set up for. Vanilla still applies the rest on its own paths SLS leaves alone: enemy and
    // player damage in Character.RPC_Damage/ApplyDamage, enemy move speed in UpdateWalking, and the resource rate on
    // every drop table SLS hands back to vanilla.
    //
    // All of these read Game's static rates, which Game.UpdateWorldRates sets from the global keys on the server and on
    // every client (keys are synced on join and on every change), so each peer computes the same values. Outside a
    // world they are 1: the statics still hold the last world's rates there.
    internal static class WorldRates
    {
        private static bool InWorld => ZoneSystem.instance != null;

        // The EnemyLevelUpRate key over 100, which the harder Combat settings raise. Vanilla multiplies its spawners'
        // level-up chance by it; SLS multiplies every levelup threshold it rolls against.
        internal static float LevelUpRate => InWorld ? Mathf.Max(0f, Game.m_enemyLevelUpRate) : 1f;

        // The ResourceRate key over 100, set by the Resources modifier.
        internal static float ResourceRate => InWorld ? Mathf.Max(0f, Game.m_resourceRate) : 1f;

        // What vanilla's Character.Awake multiplies a non-player's scale by: the EnemySpeedSize key outside interiors,
        // then the world level's growth. SLS sets a creature's scale outright, so it applies this on top of its own.
        internal static float CreatureSizeFactor(Vector3 position) {
            if (InWorld == false) { return 1f; }
            float factor = 1f;
            if (Game.m_enemySpeedSize != 1f && Character.InInterior(position) == false) { factor *= Game.m_enemySpeedSize; }
            if (Game.m_worldLevel > 0 && Game.instance != null) { factor *= 1f + Game.m_worldLevel * Game.instance.m_worldLevelEnemyMoveSpeedMultiplier; }
            return factor;
        }

        // A drop amount SLS rolled itself, scaled by the world's resource rate the way vanilla's Game.ScaleDrops scales
        // its own: the item types vanilla never scales keep their amount. Callers leave out the drops their table marks
        // as not scaling. Unlike ScaleDrops it is not held to one stack, since SLS amounts often run past a stack and are
        // split into stacks when they are dropped.
        internal static int ScaleDropAmount(GameObject prefab, int amount) {
            if (amount <= 0 || Mathf.Approximately(ResourceRate, 1f) || ScalesWithResourceRate(prefab) == false) { return amount; }
            return ScaleDropAmount(amount);
        }

        // An amount times the resource rate, still at least one if anything dropped.
        internal static int ScaleDropAmount(int amount) {
            float rate = ResourceRate;
            if (amount <= 0 || Mathf.Approximately(rate, 1f)) { return amount; }
            return Mathf.Max(1, Mathf.RoundToInt(amount * rate));
        }

        // False for the item types the world's Game prefab lists as never scaled by the resource rate.
        internal static bool ScalesWithResourceRate(GameObject prefab) {
            if (prefab == null || Game.instance == null || prefab.TryGetComponent(out ItemDrop item) == false || item.m_itemData?.m_shared == null) { return true; }
            return Game.instance.m_nonScaledDropTypes.Contains(item.m_itemData.m_shared.m_itemType) == false;
        }
    }
}
