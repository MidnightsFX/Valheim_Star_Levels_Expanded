using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.Data {
    // What each difficulty on the quick configure panel's first page sets. What depends on how many stars a world allows
    // is worked out from these numbers and that max level, so a difficulty means the same thing at any cap: the chances
    // run from the first star to the top one, and a creature at the top star has HealthAtMax times its base health wherever
    // the top star is. The shipped LevelSettings.yaml curve and biome caps are Normal at DefaultMaxLevel, so a new install
    // reads as Normal.
    internal class DifficultyPreset {
        internal const int DefaultMaxLevel = 10;

        internal string NameToken;
        internal string DescriptionToken;

        // The chance (0-100) to roll past 0 stars and past 1 star, which are what a player mostly meets, then where the
        // chances have fallen to by the top star.
        internal float ChanceFirstStar;
        internal float ChanceSecondStar;
        internal float ChanceTopStar;

        // Bosses get a curve of their own starting from this chance (0-1). 0 leaves them on the creature curve.
        internal float BossChance;

        // Multipliers on base health and damage at the top star.
        internal float HealthAtMax;
        internal float DamageAtMax;
        internal float BossHealthAtMax;
        internal float BossDamageAtMax;

        // What goes on the All biome: every creature's damage before stars, the spawn rate, the night star bonus and how
        // strongly the distance rings count.
        internal float BaseDamage = 1f;
        internal float SpawnRate = 1f;
        internal float NightLevelScale = 1f;
        internal float DistanceScale = 1f;

        // Reaches 3x health and 2x damage at the top star, which is exactly vanilla at a max level of 2, and the chances of
        // the first two stars are vanilla's.
        internal static readonly DifficultyPreset Vanilla = new DifficultyPreset {
            NameToken = "$sls_cfg_difficulty_vanilla", DescriptionToken = "$sls_cfg_difficulty_vanilla_desc",
            ChanceFirstStar = 10f, ChanceSecondStar = 1f, ChanceTopStar = 0.01f, BossChance = 0.01f,
            HealthAtMax = 3f, DamageAtMax = 2f, BossHealthAtMax = 3f, BossDamageAtMax = 1.2f,
            DistanceScale = 0.5f,
        };

        internal static readonly DifficultyPreset Easy = new DifficultyPreset {
            NameToken = "$sls_cfg_difficulty_easy", DescriptionToken = "$sls_cfg_difficulty_easy_desc",
            ChanceFirstStar = 4f, ChanceSecondStar = 0.4f, ChanceTopStar = 0.004f, BossChance = 0.005f,
            HealthAtMax = 2.5f, DamageAtMax = 1.5f, BossHealthAtMax = 2f, BossDamageAtMax = 1.1f,
            DistanceScale = 0.25f,
        };

        internal static readonly DifficultyPreset Normal = new DifficultyPreset {
            NameToken = "$sls_cfg_difficulty_normal", DescriptionToken = "$sls_cfg_difficulty_normal_desc",
            ChanceFirstStar = 20f, ChanceSecondStar = 10f, ChanceTopStar = 0.03f,
            HealthAtMax = 11f, DamageAtMax = 2f, BossHealthAtMax = 4f, BossDamageAtMax = 1.2f,
            SpawnRate = 1.1f, NightLevelScale = 1.5f, DistanceScale = 1.5f,
        };

        // Across these stars the average creature comes out near the world Combat modifier's Hard, which is 150% enemy
        // damage, but split between every creature and each star instead of landing on all of them at once.
        internal static readonly DifficultyPreset Hard = new DifficultyPreset {
            NameToken = "$sls_cfg_difficulty_hard", DescriptionToken = "$sls_cfg_difficulty_hard_desc",
            ChanceFirstStar = 35f, ChanceSecondStar = 22f, ChanceTopStar = 0.5f, BossChance = 0.4f,
            HealthAtMax = 6f, DamageAtMax = 3f, BossHealthAtMax = 6f, BossDamageAtMax = 2f,
            BaseDamage = 1.25f, SpawnRate = 1.2f, NightLevelScale = 1.5f, DistanceScale = 1.5f,
        };

        internal static readonly DifficultyPreset Brutal = new DifficultyPreset {
            NameToken = "$sls_cfg_difficulty_brutal", DescriptionToken = "$sls_cfg_difficulty_brutal_desc",
            ChanceFirstStar = 50f, ChanceSecondStar = 35f, ChanceTopStar = 2f, BossChance = 0.8f,
            HealthAtMax = 16f, DamageAtMax = 6f, BossHealthAtMax = 11f, BossDamageAtMax = 3f,
            BaseDamage = 1.25f, SpawnRate = 1.35f, NightLevelScale = 1.5f, DistanceScale = 2f,
        };

        // In the order the panel steps through them.
        internal static readonly DifficultyPreset[] All = { Vanilla, Easy, Normal, Hard, Brutal };

        // How far along the biomes are, so each one's star cap keeps its place under any max level. Biomes not listed here
        // are other mods' and keep their own proportion of the cap.
        private const int BiomeStepsTop = 26;
        private static readonly Dictionary<Heightmap.Biome, int> BiomeSteps = new Dictionary<Heightmap.Biome, int>() {
            { Heightmap.Biome.Meadows, 4 },
            { Heightmap.Biome.BlackForest, 6 },
            { Heightmap.Biome.Swamp, 10 },
            { Heightmap.Biome.Mountain, 14 },
            { Heightmap.Biome.Plains, 18 },
            { Heightmap.Biome.Mistlands, 22 },
            { Heightmap.Biome.AshLands, BiomeStepsTop },
            { Heightmap.Biome.DeepNorth, BiomeStepsTop },
        };

        internal static bool TryGetBiomeCap(Heightmap.Biome biome, int maxStars, out int cap) {
            cap = 0;
            if (BiomeSteps.TryGetValue(biome, out int step) == false) { return false; }
            cap = Mathf.Max(1, Mathf.RoundToInt(step * (float)maxStars / BiomeStepsTop));
            return true;
        }

        internal static int BiomeCap(Heightmap.Biome biome, int maxStars) {
            TryGetBiomeCap(biome, maxStars, out int cap);
            return cap;
        }

        // A level -> threshold table as DefaultCreatureLevelUpChance holds it: each entry is the chance to roll past that
        // level. The chances fall evenly from the second star to the top one, and the level at the top has nothing left
        // to roll past. Rounded, because a value this table holds has to read back the same from the yaml file.
        internal SortedDictionary<int, float> CreatureCurve(int maxStars) {
            maxStars = Mathf.Max(1, maxStars);
            SortedDictionary<int, float> table = new SortedDictionary<int, float>() { { 1, Rounded(ChanceFirstStar) } };
            for (int level = 2; level <= maxStars; level++) {
                float along = maxStars > 2 ? (level - 2f) / (maxStars - 2f) : 0f;
                table[level] = Rounded(ChanceSecondStar * Mathf.Pow(ChanceTopStar / ChanceSecondStar, along));
            }
            table[maxStars + 1] = 0f;
            return table;
        }

        // The per-star value that puts a creature at this multiplier at the top star.
        internal static float PerStar(float multiplierAtMax, int maxStars) {
            return Mathf.Round((multiplierAtMax - 1f) / Mathf.Max(1, maxStars) * 10000f) / 10000f;
        }

        private static float Rounded(float chance) {
            return Mathf.Max(0.0001f, Mathf.Round(chance * 10000f) / 10000f);
        }
    }
}
