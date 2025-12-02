using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    internal class Spawnrate
    {
        public static IEnumerator CheckSpawnRate(Character character, ZDO creatureZDO, CreatureSpecificSetting creature_settings, BiomeSpecificSetting biomeSettings, float delay = 1f) {
            yield return new WaitForSeconds(delay);
            if (CheckSetApplySpawnrate(character, creatureZDO, creature_settings, biomeSettings) == true) {
                ZNetScene.instance.Destroy(character.gameObject);
            }
        }

        // Returns a bool based on whether or not the creature should be deleted, true = delete, false = do not delete
        internal static bool CheckSetApplySpawnrate(Character character, ZDO creatureZDO,  CreatureSpecificSetting creature_settings, BiomeSpecificSetting biomeSettings) {
            if (character == null || creatureZDO == null) {
                //Logger.LogWarning($"Creature null or nview null, not running spawn multiplier.");
                return false;
            }
            if (creatureZDO.GetBool(SLS_SPAWN_MULT, false) == true) { return false; }
            if (ValConfig.BossCreaturesNeverSpawnMultiply.Value && character.IsBoss()) {
                creatureZDO.Set(SLS_SPAWN_MULT, true);
                return false;
            }

            float spawnrate = 1f;
            if (biomeSettings != null) { spawnrate = biomeSettings.SpawnRateModifier; }
            if (creature_settings != null) { spawnrate = creature_settings.SpawnRateModifier; }

            creatureZDO.Set(SLS_SPAWN_MULT, true);
            if (character.IsTamed() && ValConfig.SpawnMultiplicationAppliesToTames.Value) {
                return false;
            }
            // Chance to increase spawn, or decrease it
            //Logger.LogDebug($"Spawn multiplier {spawnrate} apply for {character.gameObject}");
            if (spawnrate > 1f) {
                spawnrate -= 1f; // Normalize spawnrate to just the bonus
                // For more than 100% spawn increases, 
                while (spawnrate > 0) {
                    float randv = UnityEngine.Random.value;
                    //Logger.LogDebug($"Spawn increase check {randv} <= {spawnrate} {randv <= spawnrate}");
                    if (randv <= spawnrate) {
                        Vector3 position = character.transform.position;
                        if (character.transform.position.y < 3000f) {
                            // Randomize position a little
                            position = DetermineOffsetPosition(position, 15f);
                        }
                        Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                        GameObject spawnedCreature = GameObject.Instantiate(PrefabManager.Instance.GetPrefab(Utils.GetPrefabName(character.gameObject)), position, rotation);
                        Character spawnedChara = spawnedCreature.GetComponent<Character>();
                        if (character.IsTamed()) {
                            spawnedChara?.SetTamed(true);
                        }
                        Logger.LogDebug($"Spawn Multiplier| Spawned {spawnedCreature.gameObject}");
                        // Spawned creatures do not count towards spawn multipliers- otherwise this is exponential
                        ModificationExtensionSystem.CreatureSetup(character);
                    }
                    spawnrate -= 1f;
                }
                //return false;
            } else if (spawnrate < 1f) {
                float randv = UnityEngine.Random.value;
                //Logger.LogDebug($"Checking for spawn rate reduction {randv} >= {spawnrate}");
                // Chance to reduce spawnrate, if triggered this creature will be queued for deletion
                if (randv >= spawnrate) {
                    Logger.LogDebug($"Spawn Reducer| Selecting {character.gameObject} for deletion.");
                    return true;
                }
            }

            return false;
        }

        internal static Vector3 DetermineOffsetPosition(Vector3 sourcePosition, float radius)
        {
            ZoneSystem.instance.FindFloor(sourcePosition, out float ysourceFloor);
            float yoffset = 0f; // This is to account for flying things
            if (ysourceFloor > sourcePosition.y + 2) {
                yoffset = sourcePosition.y - ysourceFloor;
                if (yoffset < 0f) { yoffset = 0f; } // Safety check to prevent spawning lower
            }
            int tries = 0; // If tries gets above 10 we default back to the source position
            while (tries < 10) {
                var offset = UnityEngine.Random.insideUnitCircle * (radius * 0.8f);
                Vector3 estimatedspawn = sourcePosition + new Vector3(offset.x, 0, offset.y);
                tries++;
                ZoneSystem.instance.FindFloor(new Vector3(estimatedspawn.x, estimatedspawn.y + 100f, estimatedspawn.z), out float estimatedFloor);
                estimatedspawn.y = estimatedFloor + yoffset;
                return estimatedspawn;
            }
            return sourcePosition;


        }
    }
}
