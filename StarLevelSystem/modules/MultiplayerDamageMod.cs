using HarmonyLib;
using UnityEngine;

namespace StarLevelSystem.modules
{
    public static class MultiplayerDamageMod {
        internal static void Patch(Harmony harmony) {
            harmony.PatchAll(typeof(PatchPerPlayerDamageScaling));
        }
        
        [HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScalePlayer))]
        public static class PatchPerPlayerDamageScaling {
            public static bool Prefix(Game __instance, Vector3 pos, ref float __result) {
                if (ValConfig.EnableMultiplayerEnemyDamageScaling.Value == false) {
                    __result = 1f;
                    return false;
                }
                int playerDifficulty = __instance.GetPlayerDifficulty(pos);
                if (playerDifficulty >= ValConfig.MultiplayerScalingRequiredPlayersNearby.Value) {
                    float dmgscaler = (1f + playerDifficulty) * ValConfig.MultiplayerEnemyDamageModifier.Value;
                    __result = 1f + dmgscaler;
                } else {
                    __result = 1f;
                }
                Logger.LogDebug($"Multiplayer scaling Player recieves damage increase: {__result}");
                return false;
            }
        }
    }

    public static class MultiplayerHealthMod {
        internal static void Patch(Harmony harmony) {
            harmony.PatchAll(typeof(PatchPerPlayerDamageScaling));
        }

        [HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScaleEnemy))]
        public static class PatchPerPlayerDamageScaling {
            public static bool Prefix(Game __instance, Vector3 pos, ref float __result) {
                if (ValConfig.EnableMultiplayerEnemyHealthScaling.Value == false) {
                    __result = 1f;
                    return false;
                }
                int playerDifficulty = __instance.GetPlayerDifficulty(pos);
                if (playerDifficulty >= ValConfig.MultiplayerScalingRequiredPlayersNearby.Value) {
                    __result = 1f - (playerDifficulty * ValConfig.MultiplayerEnemyHealthModifier.Value);
                } else {
                    __result = 1f;
                }
                Logger.LogDebug($"Multiplayer scaling, Enemy damage taken: {__result}");
                return false;
            }
        }
    }
}
