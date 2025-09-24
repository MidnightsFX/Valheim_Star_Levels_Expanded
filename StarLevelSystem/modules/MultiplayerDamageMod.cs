using HarmonyLib;
using UnityEngine;

namespace StarLevelSystem.modules
{
    public static class MultiplayerDamageMod {
        [HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScalePlayer))]
        public static class PatchPerPlayerDamageScaling {
            public static bool Prefix(Game __instance, Vector3 pos, ref float __result) {
                if (ValConfig.EnableMultiplayerEnemyDamageScaling.Value == false) {
                    __result = 1f;
                    return false;
                }
                int playerDifficulty = __instance.GetPlayerDifficulty(pos);
                __result = 1f + (playerDifficulty - 1) * ValConfig.MultiplayerEnemyDamageModifier.Value;
                Logger.LogDebug($"Player difficulty Damage scale: {__result}");
                return false;
            }
        }
    }

    public static class MultiplayerHealthMod {
        [HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScaleEnemy))]
        public static class PatchPerPlayerDamageScaling {
            public static bool Prefix(Game __instance, Vector3 pos, ref float __result) {
                if (ValConfig.EnableMultiplayerEnemyHealthScaling.Value == false) {
                    __result = 1f;
                    return false;
                }
                int playerDifficulty = __instance.GetPlayerDifficulty(pos);
                float healthscaler = 1f + ((playerDifficulty - 1) * ValConfig.MultiplayerEnemyHealthModifier.Value);
                __result = 1f / healthscaler;
                // Logger.LogDebug($"Player difficulty Enemy Health scale: {__result}");
                return false;
            }
        }
    }
}
