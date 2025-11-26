using MonoMod.Utils;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static Heightmap;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class CreatureModifiersData
    {
        public static CreatureModifierCollection ActiveCreatureModifiers = new CreatureModifierCollection() {
            MajorModifiers = new Dictionary<string, CreatureModifier>(),
            MinorModifiers = new Dictionary<string, CreatureModifier>(),
            BossModifiers = new Dictionary<string, CreatureModifier>(),
            ModifierGlobalSettings = new GlobalModifierSettings() {
            }
        };

        public static Dictionary<Heightmap.Biome, Dictionary<string, List<ProbabilityEntry>>> biomeMajorProbabilityList = new();
        public static Dictionary<Heightmap.Biome, Dictionary<string, List<ProbabilityEntry>>> biomeMinorProbabilityList = new();
        public static Dictionary<Heightmap.Biome, Dictionary<string, List<ProbabilityEntry>>> biomeBossProbabilityList = new();

        public static Dictionary<string, GameObject> LoadedModifierEffects = new Dictionary<string, GameObject>();
        public static Dictionary<string, GameObject> LoadedSecondaryEffects = new Dictionary<string, GameObject>();
        public static Dictionary<string, Sprite> LoadedModifierSprites = new Dictionary<string, Sprite>();

        public static List<string> NonCombatCreatures = new List<string>() {
            "Deer",
            "Hare",
            "Chicken",
            "Hen"
        };

        public enum ModifierNames
        {
            None = 0,
            BossSummoner = 1,
            SoulEater = 2,
            LifeLink = 3,
            Splitter = 9,
            Lootbags = 10,
            Fire = 11,
            Frost = 12,
            Poison = 13,
            Lightning = 14,
            FireNova = 15,
            FrostNova = 16,
            PoisonNova = 17,
            LightningNova = 18,
            ResistSlash = 21,
            ResistBlunt = 22,
            ResistPierce = 23,
            Alert = 51,
            Big = 52,
            Fast = 53,
            StaminaDrain = 54,
            EitrDrain = 55,
            Brutal = 56
        }

        public static DNum ModifierNamesLookupTable = new DNum(Enum.GetValues(typeof(ModifierNames)));

        static CreatureModifierCollection CustomModifiers = new CreatureModifierCollection();
        static CreatureModifierCollection APIAdded = new CreatureModifierCollection();
        static CreatureModifierCollection DefaultModifiers = new CreatureModifierCollection() {
            BossModifiers = new Dictionary<string, CreatureModifier>() {
                {ModifierNames.BossSummoner.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$bossSummoner_prefix1", "$bossSummoner_prefix2" },
                    NameSuffixes = new List<string>() { "$bossSummoner_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffectStyle = VisualEffectStyle.bottom,
                    //visualEffect = "creatureFire",
                    StarVisual = "summoner",
                    Config = new CreatureModConfig() {
                        BasePower = 10.0f,
                        PerlevelPower = 120.0f,
                        BiomeObjects = new Dictionary<Heightmap.Biome, List<string>>() {
                            { Heightmap.Biome.Meadows, new List<string>() { "Greyling" } },
                            { Heightmap.Biome.BlackForest, new List<string>() { "Greydwarf_Shaman" } },
                            { Heightmap.Biome.Swamp, new List<string>() { "BlobElite" } },
                            { Heightmap.Biome.Mountain, new List<string>() { "Hatchling" } },
                            { Heightmap.Biome.Plains, new List<string>() { "Goblin", "GoblinShaman", "GoblinBrute" } },
                            { Heightmap.Biome.Mistlands, new List<string>() { "SeekerBrute", "Seeker" } },
                            { Heightmap.Biome.AshLands, new List<string>() { "Charred_Archer", "Charred_Melee" } }
                            },
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Summoner",
                    }
                },
                {ModifierNames.SoulEater.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$SoulEater_prefix1" },
                    NameSuffixes = new List<string>() { "$SoulEater_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "vortex",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.01f,
                        },
                    }
                },
                {ModifierNames.LifeLink.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$LifeLink_prefix1" },
                    NameSuffixes = new List<string>() { "$LifeLink_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "LifeLink2",
                    SecondaryEffect = "LifelinkEffect",
                    Config = new CreatureModConfig() {
                        BasePower = 0.7f,
                        PerlevelPower = 0.02f,
                        },
                    }
                },
                {ModifierNames.ResistPierce.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$ResistPierce_prefix1" },
                    NameSuffixes = new List<string>() { "$ResistPierce_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "pierceresist",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.02f,
                        BasePower = 0.5f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Resistance"
                    }
                },
                {ModifierNames.Brutal.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$Brutal_prefix1", "$Brutal_prefix2", "$Brutal_prefix3" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    VisualEffectStyle = VisualEffectStyle.bottom,
                    //visualEffect = "creatureFire",
                    StarVisual = "brutal",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.0f,
                        BasePower = 1.02f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Brutal",
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
            },
            MajorModifiers = new Dictionary<string, CreatureModifier>() {
                {ModifierNames.Brutal.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$Brutal_prefix1", "$Brutal_prefix2", "$Brutal_prefix3" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    VisualEffectStyle = VisualEffectStyle.bottom,
                    //visualEffect = "creatureFire",
                    StarVisual = "brutal",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.0f,
                        BasePower = 1.07f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Brutal",
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Fire.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$fire_prefix1", "$fire_prefix2", "$fire_prefix3" },
                    NameSuffixes = new List<string>() { "$fire_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    VisualEffectStyle = VisualEffectStyle.bottom,
                    VisualEffect = "creatureFire",
                    StarVisual = "flame",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.01f,
                        BasePower = 0.3f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Flame",
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Frost.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$frost_prefix1", "$frost_prefix2", "$frost_prefix3" },
                    NameSuffixes = new List<string>() { "$frost_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    VisualEffect = "creatureFrost",
                    StarVisual = "snowflake",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.01f,
                        BasePower = 0.3f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Frost",
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Poison.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$poison_prefix1", "$poison_prefix2", "$poison_prefix3" },
                    NameSuffixes = new List<string>() { "$poison_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    VisualEffect = "creaturePoison",
                    StarVisual = "poison",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.05f,
                        BasePower = 1f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Poison",
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Lightning.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$lightning_prefix1", "$lightning_prefix2", "$lightning_prefix3" },
                    NameSuffixes = new List<string>() { "$lightning_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    VisualEffectStyle = VisualEffectStyle.objectCenter,
                    VisualEffect = "creatureLightning",
                    StarVisual = "lightning",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.05f,
                        BasePower = 0.3f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Lightning",
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Splitter.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$Splitter_prefix1" },
                    NameSuffixes = new List<string>() { "$Splitter_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "splitting",
                    Config = new CreatureModConfig() {
                        BasePower = 2.0f,
                        PerlevelPower = 0.1f,
                        },
                    }
                },
                {ModifierNames.SoulEater.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$SoulEater_prefix1" },
                    NameSuffixes = new List<string>() { "$SoulEater_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "vortex",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.05f,
                        },
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.ResistPierce.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$ResistPierce_prefix1" },
                    NameSuffixes = new List<string>() { "$ResistPierce_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "pierceresist",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.02f,
                        BasePower = 0.5f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Resistance"
                    }
                },
                {ModifierNames.ResistSlash.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$ResistSlash_prefix1" },
                    NameSuffixes = new List<string>() { "$ResistSlash_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "slashresist",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.02f,
                        BasePower = 0.5f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Resistance"
                    }
                },
                {ModifierNames.ResistBlunt.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$ResistBlunt_prefix1" },
                    NameSuffixes = new List<string>() { "$ResistBlunt_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "bluntresist",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.02f,
                        BasePower = 0.5f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Resistance"
                    }
                },
            },
            MinorModifiers = new Dictionary<string, CreatureModifier>() {
                {ModifierNames.FireNova.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$FireNova_prefix1", "$FireNova_prefix2" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    //visualEffect = "creatureLightning",
                    StarVisual = "firenova",
                    SecondaryEffect = "DeathFireNova",
                    Config = new CreatureModConfig() {
                        BasePower = 2.0f,
                        PerlevelPower = 0.1f,
                        },
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Lootbags.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$Lootbags_prefix1" },
                    NameSuffixes = new List<string>() { "$Lootbags_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    StarVisual = "lootbag",
                    Config = new CreatureModConfig() {
                        BasePower = 2.0f,
                        PerlevelPower = 0.1f,
                        },
                    }
                },
                {ModifierNames.Alert.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$alert_prefix1" },
                    NameSuffixes = new List<string>() { "$alert_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.05f,
                        BasePower = 2f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Alert"
                    }
                },
                {ModifierNames.Big.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$big_prefix1", "$big_prefix2" },
                    NameSuffixes = new List<string>() { "$big_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.00f,
                        BasePower = 0.3f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Big"
                    }
                },
                {ModifierNames.Fast.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$fast_prefix1", "$fast_prefix2", "$fast_prefix3" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    //visualEffect
                    Config = new CreatureModConfig() {
                        PerlevelPower = 0.00f,
                        BasePower = 0.2f
                        },
                    SetupMethodClass = "StarLevelSystem.Modifiers.Fast"
                    }
                },
                {ModifierNames.StaminaDrain.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$staminaDrain_prefix1", "$staminaDrain_prefix2" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    StarVisual = "staminadrain",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 2.0f,
                        BasePower = 3.0f
                        },
                    UnallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.EitrDrain.ToString(), new CreatureModifier() {
                    SelectionWeight = 10,
                    NamePrefixes = new List<string>() { "$EitrDrain_prefix1", "$EitrDrain_prefix2" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    StarVisual = "EitrEater",
                    Config = new CreatureModConfig() {
                        PerlevelPower = 4.0f,
                        BasePower = 10.0f
                        },
                    UnallowedCreatures = NonCombatCreatures,
                    AllowedBiomes = new List<Biome>() {
                            Biome.Meadows,
                            Biome.BlackForest,
                            Biome.Swamp,
                        }
                    }
                }
            },
            ModifierGlobalSettings = new GlobalModifierSettings() { GlobalIgnorePrefabList = new List<string>() { "piece_TrainingDummy" } }
        };

        public static Dictionary<string, CreatureModifier> GetModifiersOfType(ModifierType type)
        {
            if (type == ModifierType.Minor) { return ActiveCreatureModifiers.MinorModifiers; }
            if (type == ModifierType.Boss) { return ActiveCreatureModifiers.BossModifiers; }
            return ActiveCreatureModifiers.MajorModifiers;
        }

        public static CreatureModConfig GetConfig(string name, ModifierType type = ModifierType.Major) {
            // Check minor if requested, otherwise default to major
            if (type == ModifierType.Minor) {
                if (!ActiveCreatureModifiers.MinorModifiers.ContainsKey(name)) { return new CreatureModConfig() { }; }
                return ActiveCreatureModifiers.MinorModifiers[name].Config;
            }
            if (type == ModifierType.Boss) {
                if (!ActiveCreatureModifiers.BossModifiers.ContainsKey(name)) { return new CreatureModConfig() { }; }
                return ActiveCreatureModifiers.BossModifiers[name].Config;
            }
            if (!ActiveCreatureModifiers.MajorModifiers.ContainsKey(name)) { return new CreatureModConfig() { }; }
            return ActiveCreatureModifiers.MajorModifiers[name].Config;
        }

        public static CreatureModifier GetModifierDef(string name, ModifierType type = ModifierType.Major)
        {
            // Check minor if requested, otherwise default to major
            if (type == ModifierType.Minor)
            {
                if (!ActiveCreatureModifiers.MinorModifiers.ContainsKey(name)) { return new CreatureModifier() { }; }
                return ActiveCreatureModifiers.MinorModifiers[name];
            }
            if (type == ModifierType.Boss)
            {
                if (!ActiveCreatureModifiers.BossModifiers.ContainsKey(name)) { return new CreatureModifier() { }; }
                return ActiveCreatureModifiers.BossModifiers[name];
            }
            if (!ActiveCreatureModifiers.MajorModifiers.ContainsKey(name)) { return new CreatureModifier() { }; }
            return ActiveCreatureModifiers.MajorModifiers[name];
        }

        public static List<ProbabilityEntry> LazyCacheCreatureModifierSelect(string creature, Heightmap.Biome biome, ModifierType type = ModifierType.Major) {
            //Logger.LogDebug($"Getting modifier probability list for {creature} of type {type}");
            // Check type cache first
            switch (type) {
                case ModifierType.Major:
                    if (biomeMajorProbabilityList.ContainsKey(biome) && biomeMajorProbabilityList[biome].ContainsKey(creature)) {
                        return biomeMajorProbabilityList[biome][creature];
                    }
                    break;
                case ModifierType.Minor:
                    if (biomeMinorProbabilityList.ContainsKey(biome) && biomeMinorProbabilityList[biome].ContainsKey(creature)) {
                        return biomeMinorProbabilityList[biome][creature];
                    }
                    break;
                case ModifierType.Boss:
                    if (biomeBossProbabilityList.ContainsKey(biome) && biomeBossProbabilityList[biome].ContainsKey(creature)) {
                        return biomeBossProbabilityList[biome][creature];
                    }
                    break;
            }

            if (type == ModifierType.Boss) {
                return BuildCacheProbabilities(creature, biome, ActiveCreatureModifiers.BossModifiers, biomeBossProbabilityList);
            }

            if (type == ModifierType.Major) {
                return BuildCacheProbabilities(creature, biome, ActiveCreatureModifiers.MajorModifiers, biomeMajorProbabilityList);
            }

            return BuildCacheProbabilities(creature, biome, ActiveCreatureModifiers.MinorModifiers, biomeMinorProbabilityList);
        }

        private static List<ProbabilityEntry> BuildCacheProbabilities(string creature, Heightmap.Biome biome, Dictionary<string, CreatureModifier> activeMods, Dictionary<Heightmap.Biome, Dictionary<string, List<ProbabilityEntry>>> probabilitiesCache) {
            if (probabilitiesCache.ContainsKey(biome) && probabilitiesCache[biome].ContainsKey(creature)) {
                return probabilitiesCache[biome][creature];
            }
            
            List<ProbabilityEntry> probabilities = BuildProbabilityEntries(creature, biome, activeMods);
            if (!probabilitiesCache.ContainsKey(biome)) {
                probabilitiesCache.Add(biome, new Dictionary<string, List<ProbabilityEntry>>() { { creature, probabilities } });
            } else {
                probabilitiesCache[biome].Add(creature, probabilities);
            }
            return probabilities;
        }

        private static List<ProbabilityEntry> BuildProbabilityEntries(string creature, Heightmap.Biome biome, Dictionary<string, CreatureModifier> modifiers) {
            List<ProbabilityEntry> creatureModifierProbability = new List<ProbabilityEntry>();
            // Logger.LogDebug($"Building probability entries for creature {creature} with {modifiers.Count} modifiers");
            foreach (var entry in modifiers) {
                // Logger.LogDebug($"Checking modifier {entry.Key}");
                // Skip if in the deny list
                if (entry.Value.UnallowedCreatures != null && entry.Value.UnallowedCreatures.Contains(creature)) { continue; }
                if (entry.Value.AllowedBiomes != null && !entry.Value.AllowedBiomes.Contains(biome)) { continue; }

                // Add if in the allow list, skip if allow list defined and not in there
                // Logger.LogDebug($"Checking Allowed creatures {entry.Key}");
                if (entry.Value.AllowedCreatures != null && entry.Value.AllowedCreatures.Count > 0) {
                    if (entry.Value.AllowedCreatures.Contains(creature)) {
                        creatureModifierProbability.Add(new ProbabilityEntry() { Name = entry.Key, SelectionWeight = entry.Value.SelectionWeight });
                    } else {
                        continue;
                    }
                }


                // Add if allow and deny list are not defined, default
                creatureModifierProbability.Add(new ProbabilityEntry() { Name = entry.Key, SelectionWeight = entry.Value.SelectionWeight });
            }
            // Logger.LogDebug($"Built {creatureModifierProbability.Count} probability entries for creature {creature}");
            return creatureModifierProbability;
        }

        private static void UpdateModifiers(CreatureModifierCollection creatureMods = null, CreatureModifierCollection APIcreatureMods = null)
        {
            // Logger.LogDebug("Updating Creature Modifiers");
            ActiveCreatureModifiers.MajorModifiers.Clear();
            ActiveCreatureModifiers.MinorModifiers.Clear();
            ActiveCreatureModifiers.BossModifiers.Clear();
            // Set new modifiers, if provided
            Logger.LogDebug("Setting config definitions");
            if (creatureMods != null) { CustomModifiers = creatureMods; }
            if (APIcreatureMods != null) { APIAdded = APIcreatureMods; }

            // Applying global settings
            if (CustomModifiers.ModifierGlobalSettings != null) {
                ActiveCreatureModifiers.ModifierGlobalSettings = CustomModifiers.ModifierGlobalSettings;
            } else {
                ActiveCreatureModifiers.ModifierGlobalSettings = DefaultModifiers.ModifierGlobalSettings;
            }
            // Update major modifiers
                Logger.LogDebug("Merging config Major mod definitions");
            if (CustomModifiers.MajorModifiers != null &&  CustomModifiers.MajorModifiers.Count > 0) { ActiveCreatureModifiers.MajorModifiers.AddRange(CustomModifiers.MajorModifiers); }
            if (APIAdded.MajorModifiers != null && APIAdded.MajorModifiers.Count > 0) { ActiveCreatureModifiers.MajorModifiers.AddRange(APIAdded.MajorModifiers); }
            if (APIAdded.MajorModifiers == null && CustomModifiers.MajorModifiers == null) { ActiveCreatureModifiers.MajorModifiers.AddRange(DefaultModifiers.MajorModifiers); }

            // Update minor modifiers
            Logger.LogDebug("Merging config Minor mod definitions");
            if (CustomModifiers.MinorModifiers != null && CustomModifiers.MinorModifiers.Count > 0) { ActiveCreatureModifiers.MinorModifiers.AddRange(CustomModifiers.MinorModifiers); }
            if (APIAdded.MinorModifiers != null && APIAdded.MinorModifiers.Count > 0) { ActiveCreatureModifiers.MinorModifiers.AddRange(APIAdded.MinorModifiers); }
            if (APIAdded.MinorModifiers == null && CustomModifiers.MinorModifiers == null) { ActiveCreatureModifiers.MinorModifiers.AddRange(DefaultModifiers.MinorModifiers); }

            // Update major modifiers
            Logger.LogDebug("Merging config Boss mod definitions");
            if (CustomModifiers.BossModifiers != null && CustomModifiers.BossModifiers.Count > 0) { ActiveCreatureModifiers.BossModifiers.AddRange(CustomModifiers.BossModifiers); }
            if (APIAdded.BossModifiers != null && APIAdded.BossModifiers.Count > 0) { ActiveCreatureModifiers.BossModifiers.AddRange(APIAdded.BossModifiers); }
            if (APIAdded.BossModifiers == null && CustomModifiers.BossModifiers == null) { ActiveCreatureModifiers.BossModifiers.AddRange(DefaultModifiers.BossModifiers); }
        }

        internal static string GetModifierDefaultConfig() {
            var yaml = DataObjects.yamlserializer.Serialize(DefaultModifiers);
            return yaml;
        }

        internal static void ClearProbabilityCaches(object s, EventArgs e)
        {
            ClearProbabilityCaches();
        }

        internal static void ClearProbabilityCaches() {
            biomeMajorProbabilityList.Clear();
            biomeMinorProbabilityList.Clear();
            biomeBossProbabilityList.Clear();
        }

        internal static bool UpdateModifierConfig(string yaml)
        {
            try {
                CreatureModifierCollection modcollection = DataObjects.yamldeserializer.Deserialize<CreatureModifierCollection>(yaml);
                UpdateModifiers(creatureMods: modcollection);
                LoadPrefabs();
                // Resolve all of the prefab references
                Logger.LogDebug("Loading Modifier Configuration.");
            }
            catch (Exception ex)
            {
                StarLevelSystem.Log.LogError($"Failed to parse Modifier settings YAML: {ex.Message}");
                return false;
            }
            return true;
        }

        internal static void LoadPrefabs() {
            if (ActiveCreatureModifiers.MinorModifiers != null) {
                foreach (var mod in ActiveCreatureModifiers.MinorModifiers) {
                    // Logger.LogDebug($"Loading assets for: {mod}");
                    mod.Value.LoadAndSetGameObjects();
                }
            }
            if (ActiveCreatureModifiers.MajorModifiers != null) {
                foreach (var mod in ActiveCreatureModifiers.MajorModifiers) {
                    // Logger.LogDebug($"Loading assets for: {mod}");
                    mod.Value.LoadAndSetGameObjects();
                }
            }
            if (ActiveCreatureModifiers.BossModifiers != null) {
                foreach (var mod in ActiveCreatureModifiers.BossModifiers) {
                    // Logger.LogDebug($"Loading assets for: {mod}");
                    mod.Value.LoadAndSetGameObjects();
                }
            }
        }


        internal static void Init() {
            UpdateModifierConfig(File.ReadAllText(ValConfig.creatureModifierFilePath));
        }
    }
}
