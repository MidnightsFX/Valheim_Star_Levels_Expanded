using HarmonyLib;
using StarLevelSystem.common;
using UnityEngine;

namespace StarLevelSystem.modules
{
    // Both patches replace vanilla's per-player difficulty outright: with SLS's scaling off a group faces solo creatures.
    // Valheim Plus's [Game] difficulty scales, when that section is on, multiply onto SLS's result (or stand alone while
    // SLS's is off) by vanilla's own formulas, since V+ only applies them by feeding the vanilla ones this skips.
    public static class MultiplayerDamageMod {
        [HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScalePlayer))]
        public static class PatchPerPlayerDamageScaling {
            public static bool Prefix(Game __instance, Vector3 pos, ref float __result) {
                __result = 1f;
                bool slsScaling = ValConfig.EnableMultiplayerEnemyDamageScaling.Value;
                bool valheimPlus = Compatibility.TryGetValheimPlusDifficulty(out float valheimPlusDamagePerPlayer, out _);
                if (slsScaling == false && valheimPlus == false) { return false; }

                int playerDifficulty = __instance.GetPlayerDifficulty(pos);
                if (slsScaling && playerDifficulty >= ValConfig.MultiplayerScalingRequiredPlayersNearby.Value) {
                    float dmgScaler = (1f + playerDifficulty) * ValConfig.MultiplayerEnemyDamageModifier.Value;
                    __result = 1f + dmgScaler;
                }
                if (valheimPlus) { __result *= ValheimPlusDamageDealt(playerDifficulty, valheimPlusDamagePerPlayer); }
                //Logger.LogDebug($"Multiplayer scaling Player receives damage increase: {__result}");
                return false;
            }
        }

        // Vanilla's GetDifficultyDamageScalePlayer with V+'s per-player damage scale.
        internal static float ValheimPlusDamageDealt(int playerDifficulty, float damagePerPlayer) {
            return Mathf.Max(0f, 1f + (playerDifficulty - 1) * damagePerPlayer);
        }
    }

    public static class MultiplayerHealthMod {
        [HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScaleEnemy))]
        public static class PatchPerPlayerDamageScaling {
            public static bool Prefix(Game __instance, Vector3 pos, ref float __result) {
                __result = 1f;
                bool slsScaling = ValConfig.EnableMultiplayerEnemyHealthScaling.Value;
                bool valheimPlus = Compatibility.TryGetValheimPlusDifficulty(out _, out float valheimPlusHealthPerPlayer);
                if (slsScaling == false && valheimPlus == false) { return false; }

                int playerDifficulty = __instance.GetPlayerDifficulty(pos);
                if (slsScaling && playerDifficulty >= ValConfig.MultiplayerScalingRequiredPlayersNearby.Value) {
                    __result = Mathf.Max(1f - (playerDifficulty * ValConfig.MultiplayerEnemyHealthModifier.Value), ValConfig.MultiplayerEnemyMinDamageTaken.Value);
                }
                // After SLS's floor: MultiplayerEnemyMinDamageTaken bounds SLS's own reduction, not V+'s.
                if (valheimPlus) { __result *= ValheimPlusDamageTaken(playerDifficulty, valheimPlusHealthPerPlayer); }
                //Logger.LogDebug($"Multiplayer scaling, Enemy damage taken: {__result}");
                return false;
            }
        }

        // Vanilla's GetDifficultyDamageScaleEnemy with V+'s per-player health scale, kept off zero and negatives.
        internal static float ValheimPlusDamageTaken(int playerDifficulty, float healthPerPlayer) {
            return 1f / Mathf.Max(0.01f, 1f + (playerDifficulty - 1) * healthPerPlayer);
        }
    }
}
