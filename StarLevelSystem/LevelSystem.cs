using HarmonyLib;

namespace StarLevelSystem
{
    public static class LevelSystem
    {

        [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Awake))]
        public class ModifyMaxLevel
        {
            public static void Postfix(CreatureSpawner __instance)
            {
                __instance.m_maxLevel = ValConfig.MaxLevel.Value;
            }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.Awake))]
        public static class EnableLevelDisplay
        {
            public static void Postfix(EnemyHud __instance)
            {
                // Need a patch to show the number of stars something is
                // Need to setup the 1-5 stars, and the 5-n stars
                // __instance.m_baseHud
            }
        } 

        
    }
}
