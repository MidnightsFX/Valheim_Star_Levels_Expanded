using JetBrains.Annotations;
using StarLevelSystem.common;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    internal class Summoner
    {
        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CharacterCacheEntry ccache) {
            if (ccache == null) { return; }
            SLSSummoner summoningScript = creature.GetComponent<SLSSummoner>();
            Logger.LogDebug($"Setting up Summoner for {creature.name} summon script {summoningScript}");
            if (summoningScript == null) {
                SLSSummoner summoner = creature.gameObject.AddComponent<SLSSummoner>();
                if (config.BiomeObjects.ContainsKey(ccache.Biome)) {
                    summoner.SetupSummoner(creature, config.BiomeObjects[ccache.Biome], Mathf.RoundToInt(config.BasePower), config.PerlevelPower);
                }
            }
        }

        public class SLSSummoner : MonoBehaviour {
            List<GameObject> summonableCreatures = new List<GameObject>();
            ZNetView creature_znet = null;
            static List<ZDOID> spawned = new List<ZDOID>();
            static int maxSummoned = 10;
            static int summonBatchSize = 2;
            static float timeBetweenSummons = 120;
            static Character bossCharacter;
            static bool setup = false;
            static bool started = false;
            static double spawntimestamp = 0;

            public void Update()
            {
                if (!setup) return;
                if (!creature_znet.IsOwner()) return;

                if (started == false) {
                    InvokeRepeating("SpawnCreaturesBatch", timeBetweenSummons, timeBetweenSummons);
                }
            }

            public void SpawnCreaturesBatch() {
                summonBatchSize.Times(() => SpawnCreatureRandomly());
            }

            public void SpawnCreatureRandomly()
            {
                if (summonableCreatures.Count == 0) return;
                if (spawned.Count >= maxSummoned) {
                    // Check if any of the spawned are still alive
                    spawned.RemoveAll(x => ZDOMan.instance.GetZDO(x) == null);
                    if (spawned.Count >= maxSummoned) return;
                }
                // Skip spawning for the requested cooldown time
                if (spawntimestamp + timeBetweenSummons < ZNet.instance.GetTimeSeconds()) { return; }
                if (summonableCreatures.Count == 0) { return; }
                if (bossCharacter  == null) { return; }

                GameObject toSummon = summonableCreatures[UnityEngine.Random.Range(0, summonableCreatures.Count)];
                Vector3 spawnPosition = bossCharacter.transform.position + new Vector3(UnityEngine.Random.Range(-10, 10), 0, UnityEngine.Random.Range(-10, 10));
                spawnPosition.y = ZoneSystem.instance.GetGroundHeight(spawnPosition) + 0.5f;
                GameObject spawnedCreature = Instantiate(toSummon, spawnPosition, Quaternion.identity);
                Character character = spawnedCreature.GetComponent<Character>();
                if (character != null) {
                    spawned.Add(character.GetZDOID());
                }
                spawntimestamp = ZNet.instance.GetTimeSeconds();
            }

            public void SetupSummoner(Character character, List<string> summonPrefabs, int max_summoned = 10, float time_between_summons = 120) {
                bossCharacter = character;
                timeBetweenSummons = time_between_summons;
                maxSummoned = max_summoned;
                creature_znet = character.m_nview;
                foreach (var prefabname in summonPrefabs) {
                    GameObject prefab = Jotunn.Managers.PrefabManager.Instance.GetPrefab(prefabname);
                    if (prefab != null) {
                        summonableCreatures.Add(prefab);
                    }
                }
                setup = true;
            }
        }

    }
}
