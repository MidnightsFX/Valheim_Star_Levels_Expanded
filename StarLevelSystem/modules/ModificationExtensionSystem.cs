using HarmonyLib;
using UnityEngine;

namespace StarLevelSystem.modules
{
    internal class ModificationExtensionSystem
    {
        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class CreatureCharacterExtension
        {
            public static void Postfix(Character __instance) {
                Logger.LogDebug($"Character Awake called for {__instance.name} with level {__instance.m_level}");
                SetupLevelColorSizeAndStats(__instance);
            }
        }

        //[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Awake))]
        //public static class BossCharacterExtension
        //{
        //    public static void Postfix(Humanoid __instance) {
        //        Logger.LogDebug($"Humanoid Awake called for {__instance.name} with level {__instance.m_level}");
        //        SetupLevelColorSizeAndStats(__instance);
        //    }
        //}

        private static void SetupLevelColorSizeAndStats(Character __instance)
        {
            if (__instance.IsPlayer()) { return; }
            ZNetView zview = __instance.gameObject.GetComponent<ZNetView>();
            bool setupdone = zview.GetZDO().GetBool("SLE_SDone", false);
            if (setupdone != true) {
                // Setup the level and colorization if it wasn't already
                if (__instance.m_level <= 1) {
                    LevelSystem.DetermineApplyLevelGeneric(__instance.gameObject);
                }
                zview.GetZDO().Set("SLE_SDone", true);
            }
            // No need to do colorization or size scaling if the creature is level 1 or lower
            if (__instance.m_level <= 1) { return; }
            // Colorization and visual adjustments
            Colorization.ApplyColorizationWithoutLevelEffects(__instance);
            Colorization.ApplyHighLevelVisual(__instance);

            // Scaling
            if (__instance.transform.position.y < 3000f && ValConfig.EnableScalingInDungeons.Value == false) {
                // Don't scale in dungeons
                float scale = 1 + (ValConfig.PerLevelScaleBonus.Value * __instance.m_level);
                Logger.LogDebug($"Setting character size {scale} and color.");
                __instance.transform.localScale *= scale;
                Physics.SyncTransforms();
            }
        }

    }
}
