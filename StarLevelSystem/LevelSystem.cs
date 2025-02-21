using HarmonyLib;
using UnityEngine;

namespace StarLevelSystem
{
    public static class LevelSystem
    {

        private static GameObject star;

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
                star = __instance.m_baseHud.transform.Find("level_2/star").gameObject;
                GameObject level_4 = new GameObject();
                level_4.transform.SetParent(__instance.m_baseHud.transform);
                GameObject star1 = Object.Instantiate(star, level_4.transform);
                
                // Loop through creating more stars adjusted orientation correctly
                GameObject star2 = Object.Instantiate(star, level_4.transform);
                Vector3 s1p = star2.transform.position;
                star2.transform.position = new Vector3(x: s1p.x + 5f, y: s1p.y, z: s1p.z);
            }
        }

        
    }
}
