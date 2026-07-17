using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.NemesisSystem {
    internal static class NemesisMiniBossManager {
        internal static int NumNemesisNames = 9;
        internal static int NumNemesisPrefixes = 6;
        internal static int NumNemesisPostfixes = 4;



        // This builds a localizable boss name using prefix, postfix
        public static string RandomlySelectBossName(string playername, bool include_postfix = true, bool include_prefix = true) {
            StringBuilder sb = new StringBuilder();
            // Small chance to add a prefix to the creatures name
            if (include_prefix && UnityEngine.Random.Range(0, 1f) < 0.10f) {
                int prefix_idx = UnityEngine.Random.Range(1, NumNemesisPrefixes);
                sb.Append("$SLS_Miniboss_Prefix" + prefix_idx + " ");
            }
            
            int name_idx = UnityEngine.Random.Range(1, NumNemesisNames);
            sb.Append("$SLS_Miniboss_Name" + name_idx);

            // Chance to include the player name reference
            if (include_postfix && UnityEngine.Random.Range(0,1f) < 0.25f) {
                int postfix_idx = UnityEngine.Random.Range(1, NumNemesisPostfixes);
                sb.Append(" $SLS_Miniboss_postfix" + postfix_idx + " " + playername);
            }
            return sb.ToString();
        }

        public static DataObjects.NemesisMiniboss RandomlySelectAppropriateMiniboss(Vector3 pos) {
            Heightmap.Biome targetBiome = Heightmap.FindBiome(pos);
            return RandomlySelectAppropriateMiniboss(targetBiome);
        }

        public static DataObjects.NemesisMiniboss RandomlySelectAppropriateMiniboss(Heightmap.Biome targetBiome) {
            foreach(DataObjects.NemesisMiniboss miniboss in SLSExtensions.ShuffleList(NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses)) {
                if (miniboss.Biome != targetBiome) { continue; }

                return miniboss;
            }
            return null;
        }

        // Build a set of biome-appropriate minions from the configured NemesisMinionTemplatesByBiome.
        // Shared by the player-death nemesis creation and the remote boss generator.
        public static List<NemesisSpawn> GenerateMinionsForBiome(Heightmap.Biome biome, int minGroups = 1, int maxGroups = 4) {
            List<NemesisSpawn> minions = new List<NemesisSpawn>();
            if (NemesisSystemData.SLE_Nemesis_Settings.NemesisMinionTemplatesByBiome != null
                && NemesisSystemData.SLE_Nemesis_Settings.NemesisMinionTemplatesByBiome.TryGetValue(biome, out List<NemesisMinion> biomeMinions)
                && biomeMinions.Count > 0) {
                int numMinions = UnityEngine.Random.Range(minGroups, maxGroups);
                while (numMinions > 0) {
                    numMinions--;
                    NemesisMinion nm = biomeMinions[UnityEngine.Random.Range(0, biomeMinions.Count)];
                    GameObject minionGo = PrefabManager.Instance.GetPrefab(nm.PrefabName);
                    if (minionGo == null) { continue; }
                    Character mchara = minionGo.GetComponent<Character>();
                    if (mchara != null) {
                        minions.Add(new NemesisSpawn() {
                            Faction = Character.Faction.Boss,
                            CustomName = $"$SLS_minion {mchara.m_name}",
                            Prefab = nm.PrefabName,
                            SpawnGroupSize = UnityEngine.Random.Range(nm.MinAmount, nm.MaxAmount),
                            CreatureBaseValueModifiers = nm.CreatureBaseValueModifiers,
                            CreaturePerLevelValueModifiers = nm.CreaturePerLevelValueModifiers
                        });
                    }
                }
            }
            return minions;
        }

        // Fabricate a fresh miniboss for a biome from the RemoteSpawning.BossCandidatesByBiome templates.
        // Used by the remote spawn system when the AvailableMiniBosses pool has no biome-appropriate entry.
        public static NemesisMiniboss GenerateMinibossForBiome(Heightmap.Biome biome) {
            RemoteNemesisSpawnSettings settings = NemesisSystemData.SLE_Nemesis_Settings.RemoteSpawning;
            if (settings?.BossCandidatesByBiome == null
                || settings.BossCandidatesByBiome.TryGetValue(biome, out List<NemesisSpawn> candidates) == false
                || candidates == null || candidates.Count == 0) {
                return null;
            }

            NemesisSpawn selected = WeightedSelectCandidate(candidates);
            if (selected == null) {
                Logger.LogNemesis($"No valid remote boss candidate for biome {biome} (missing prefabs?).");
                return null;
            }

            // Deep-clone via the config serializer so we don't mutate the shared candidate when stamping
            // per-boss fields (name/flags). The level, modifiers and loot flow through SpawnNemesisSpawn.
            NemesisSpawn bossSpawn = DataObjects.yamlDeserializer.Deserialize<NemesisSpawn>(DataObjects.yamlSerializer.Serialize(selected));
            bossSpawn.IsBoss = true;
            bossSpawn.SpawnGroupSize = 1;
            if (bossSpawn.Faction == Character.Faction.TrainingDummy) { bossSpawn.Faction = Character.Faction.Boss; }
            if (string.IsNullOrEmpty(bossSpawn.CustomName)) { bossSpawn.CustomName = RandomlySelectBossName("", include_postfix: false); }

            return new NemesisMiniboss() {
                BossCreatedFromKillingPlayer = false,
                Biome = biome,
                BossSpawn = bossSpawn,
                Minions = GenerateMinionsForBiome(biome)
            };
        }

        // Weighted pick among the biome's boss archetypes, considering only those whose prefab is loaded.
        private static NemesisSpawn WeightedSelectCandidate(List<NemesisSpawn> candidates) {
            List<NemesisSpawn> valid = candidates
                .Where(c => c != null && string.IsNullOrEmpty(c.Prefab) == false && PrefabManager.Instance.GetPrefab(c.Prefab) != null)
                .ToList();
            if (valid.Count == 0) { return null; }

            float total = valid.Sum(c => Mathf.Max(0f, c.SelectionWeight));
            if (total <= 0f) { return valid[UnityEngine.Random.Range(0, valid.Count)]; }
            float roll = UnityEngine.Random.Range(0f, total);
            foreach (NemesisSpawn c in valid) {
                roll -= Mathf.Max(0f, c.SelectionWeight);
                if (roll <= 0f) { return c; }
            }
            return valid[valid.Count - 1];
        }
    }
}
