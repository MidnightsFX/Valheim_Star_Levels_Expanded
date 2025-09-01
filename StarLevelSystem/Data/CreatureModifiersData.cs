using MonoMod.Utils;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class CreatureModifiersData
    {
        public static CreatureModifierCollection CreatureModifiers = new CreatureModifierCollection() {
            MajorModifiers = new Dictionary<ModifierNames, CreatureModifier>(),
            MinorModifiers = new Dictionary<ModifierNames, CreatureModifier>(),
            BossModifiers = new Dictionary<ModifierNames, CreatureModifier>()
        };

        public static Dictionary<string,List<ProbabilityEntry>> cMajorModifierProbabilityList = new Dictionary<string,List<ProbabilityEntry>>();
        public static Dictionary<string,List<ProbabilityEntry>> cMinorModifierProbabilityList = new Dictionary<string, List<ProbabilityEntry>>();
        public static Dictionary<string, List<ProbabilityEntry>> cBossModifierProbabilityList = new Dictionary<string, List<ProbabilityEntry>>();

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
            Fire = 11,
            Frost = 12,
            Poison = 13,
            Lightning = 14,
            ResistSlash = 21,
            ResistBlunt = 22,
            ResistPierce = 23,
            Alert = 51,
            Big = 52,
            Fast = 53,
            StaminaDrain = 54,
            EitrDrain = 55
        }

        static CreatureModifierCollection CustomModifiers = new CreatureModifierCollection();
        static CreatureModifierCollection APIAdded = new CreatureModifierCollection();
        static CreatureModifierCollection DefaultModifiers = new CreatureModifierCollection() {
            BossModifiers = new Dictionary<ModifierNames, CreatureModifier>() {
                {ModifierNames.BossSummoner, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$bossSummoner_prefix1", "$bossSummoner_prefix2" },
                    name_suffixes = new List<string>() { "$bossSummoner_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffectStyle = VisualEffectStyle.bottom,
                    //visualEffect = "creatureFire",
                    starVisual = "summoner",
                    config = new CreatureModConfig() {
                        basepower = 2.0f,
                        perlevelpower = 1.0f,
                        biomeObjects = new Dictionary<Heightmap.Biome, List<string>>() {
                            { Heightmap.Biome.Meadows, new List<string>() { "Greyling" } },
                            { Heightmap.Biome.BlackForest, new List<string>() { "Greydwarf_Shaman" } },
                            { Heightmap.Biome.Swamp, new List<string>() { "BlobElite" } },
                            { Heightmap.Biome.Mountain, new List<string>() { "Hatchling" } },
                            { Heightmap.Biome.Plains, new List<string>() { "Goblin", "GoblinShaman", "GoblinBrute" } },
                            { Heightmap.Biome.Mistlands, new List<string>() { "SeekerBrute", "Seeker" } },
                            { Heightmap.Biome.AshLands, new List<string>() { "Charred_Archer", "Charred_Melee" } }
                            },
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Summoner",
                    }
                },
                {ModifierNames.SoulEater, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$SoulEater_prefix1" },
                    name_suffixes = new List<string>() { "$SoulEater_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    starVisual = "vortex",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.01f,
                        },
                    }
                },
                {ModifierNames.ResistPierce, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$ResistPierce_prefix1" },
                    name_suffixes = new List<string>() { "$ResistPierce_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    starVisual = "pierceResist",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.02f,
                        basepower = 0.5f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Resistance"
                    }
                }
            },
            MajorModifiers = new Dictionary<ModifierNames, CreatureModifier>() {
                {ModifierNames.Fire, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$fire_prefix1", "$fire_prefix2", "$fire_prefix3" },
                    name_suffixes = new List<string>() { "$fire_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    visualEffectStyle = VisualEffectStyle.bottom,
                    visualEffect = "creatureFire",
                    starVisual = "flame",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.01f,
                        basepower = 1.3f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Flame",
                    unallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Frost, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$frost_prefix1", "$frost_prefix2", "$frost_prefix3" },
                    name_suffixes = new List<string>() { "$frost_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    visualEffect = "creatureFrost",
                    starVisual = "snowflake",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.01f,
                        basepower = 1.3f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Frost",
                    unallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Poison, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$poison_prefix1", "$poison_prefix2", "$poison_prefix3" },
                    name_suffixes = new List<string>() { "$poison_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    visualEffect = "creaturePoison",
                    starVisual = "poison",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.05f,
                        basepower = 2f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Poison",
                    unallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.Lightning, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$lightning_prefix1", "$lightning_prefix2", "$lightning_prefix3" },
                    name_suffixes = new List<string>() { "$lightning_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    visualEffectStyle = VisualEffectStyle.objectCenter,
                    visualEffect = "creatureLightning",
                    starVisual = "lightning",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.05f,
                        basepower = 2f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Lightning",
                    unallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.SoulEater, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$SoulEater_prefix1" },
                    name_suffixes = new List<string>() { "$SoulEater_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    starVisual = "vortex",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.05f,
                        },
                    unallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.ResistPierce, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$ResistPierce_prefix1" },
                    name_suffixes = new List<string>() { "$ResistPierce_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    starVisual = "pierceResist",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.02f,
                        basepower = 0.5f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Resistance"
                    }
                },
                {ModifierNames.ResistSlash, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$ResistSlash_prefix1" },
                    name_suffixes = new List<string>() { "$ResistSlash_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    starVisual = "slashResist",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.02f,
                        basepower = 0.5f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Resistance"
                    }
                },
                {ModifierNames.ResistBlunt, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$ResistBlunt_prefix1" },
                    name_suffixes = new List<string>() { "$ResistBlunt_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect = "creatureLightning",
                    starVisual = "bluntResist",
                    config = new CreatureModConfig() {
                        perlevelpower = 0.02f,
                        basepower = 0.5f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Resistance"
                    }
                }
            },
            MinorModifiers = new Dictionary<ModifierNames, CreatureModifier>() {
                {ModifierNames.Alert, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$alert_prefix1" },
                    name_suffixes = new List<string>() { "$alert_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect
                    config = new CreatureModConfig() {
                        perlevelpower = 0.05f,
                        basepower = 2f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Alert"
                    }
                },
                {ModifierNames.Big, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$big_prefix1", "$big_prefix2" },
                    name_suffixes = new List<string>() { "$big_suffix1" },
                    namingConvention = NameSelectionStyle.RandomBoth,
                    //visualEffect
                    config = new CreatureModConfig() {
                        perlevelpower = 0.00f,
                        basepower = 0.3f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Big"
                    }
                },
                {ModifierNames.Fast, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$fast_prefix1", "$fast_prefix2", "$fast_prefix3" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    //visualEffect
                    config = new CreatureModConfig() {
                        perlevelpower = 0.00f,
                        basepower = 0.2f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Fast"
                    }
                },
                {ModifierNames.StaminaDrain, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$staminaDrain_prefix1", "$staminaDrain_prefix2" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    starVisual = "staminaDrain",
                    config = new CreatureModConfig() {
                        perlevelpower = 2.0f,
                        basepower = 3.0f
                        },
                    unallowedCreatures = NonCombatCreatures
                    }
                },
                {ModifierNames.EitrDrain, new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$EitrDrain_prefix1", "$EitrDrain_prefix2" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    starVisual = "EitrDrain",
                    config = new CreatureModConfig() {
                        perlevelpower = 4.0f,
                        basepower = 10.0f
                        },
                    unallowedCreatures = NonCombatCreatures
                    }
                }
            }
        };

        public static CreatureModConfig GetConfig(ModifierNames name, ModifierType type = ModifierType.Major) {
            // Check minor if requested, otherwise default to major
            if (type == ModifierType.Minor) {
                if (!CreatureModifiers.MinorModifiers.ContainsKey(name)) { return new CreatureModConfig() { }; }
                return CreatureModifiers.MinorModifiers[name].config;
            }
            if (!CreatureModifiers.MajorModifiers.ContainsKey(name)) { return new CreatureModConfig() { }; }
            return CreatureModifiers.MajorModifiers[name].config;
        }

        public static List<ProbabilityEntry> LazyCacheCreatureModifierSelect(string creature, ModifierType type = ModifierType.Major) {
            Logger.LogDebug($"Getting modifier probability list for {creature} of type {type}");
            // Check type cache first
            switch (type) {
                case ModifierType.Major:
                    if (cMajorModifierProbabilityList.ContainsKey(creature)) { return cMajorModifierProbabilityList[creature]; }
                    break;
                case ModifierType.Minor:
                    if (cMinorModifierProbabilityList.ContainsKey(creature)) { return cMinorModifierProbabilityList[creature]; }
                    break;
                case ModifierType.Boss:
                    if (cBossModifierProbabilityList.ContainsKey(creature)) { return cBossModifierProbabilityList[creature]; }
                    break;
            }

            if (type == ModifierType.Boss) {
                List<ProbabilityEntry> bossProbability = BuildProbabilityEntries(creature, CreatureModifiers.BossModifiers);
                if (!cBossModifierProbabilityList.ContainsKey(creature)) {
                    cBossModifierProbabilityList.Add(creature, bossProbability);
                }
                return bossProbability;
            }

            if (type == ModifierType.Major) {
                List<ProbabilityEntry> majorProbability = BuildProbabilityEntries(creature, CreatureModifiers.MajorModifiers);
                if (!cMajorModifierProbabilityList.ContainsKey(creature)) {
                    cMajorModifierProbabilityList.Add(creature, majorProbability);
                }
                return majorProbability;
            }

            List<ProbabilityEntry> minorProbability = BuildProbabilityEntries(creature, CreatureModifiers.MinorModifiers);
            if (!cMinorModifierProbabilityList.ContainsKey(creature)) {
                cMinorModifierProbabilityList.Add(creature, minorProbability);
            }
            
            return minorProbability;
        }

        private static List<ProbabilityEntry> BuildProbabilityEntries(string creature, Dictionary<ModifierNames, CreatureModifier> modifiers) {
            List<ProbabilityEntry> creatureModifierProbability = new List<ProbabilityEntry>();
            Logger.LogDebug($"Building probability entries for creature {creature} with {modifiers.Count} modifiers");
            foreach (var entry in modifiers) {
                Logger.LogDebug($"Checking modifier {entry.Key}");
                // Skip if in the deny list
                if (entry.Value.unallowedCreatures.Contains(creature)) { continue; }

                // Add if in the allow list, skip if allow list defined and not in there
                Logger.LogDebug($"Checking Allowed creatures {entry.Key}");
                if (entry.Value.allowedCreatures.Count > 0) {
                    if (entry.Value.allowedCreatures.Contains(creature)) {
                        creatureModifierProbability.Add(new ProbabilityEntry() { Name = entry.Key, selectionWeight = entry.Value.selectionWeight });
                    } else {
                        continue;
                    }
                }


                // Add if allow and deny list are not defined, default
                creatureModifierProbability.Add(new ProbabilityEntry() { Name = entry.Key, selectionWeight = entry.Value.selectionWeight });
            }
            Logger.LogDebug($"Built {creatureModifierProbability.Count} probability entries for creature {creature}");
            return creatureModifierProbability;
        }

        private static void UpdateModifiers(CreatureModifierCollection creatureMods = null, CreatureModifierCollection APIcreatureMods = null)
        {
            Logger.LogDebug("Updating Creature Modifiers");
            CreatureModifiers.MajorModifiers.Clear();
            CreatureModifiers.MinorModifiers.Clear();
            CreatureModifiers.BossModifiers.Clear();
            // Set new modifiers, if provided
            Logger.LogDebug("Setting config definitions");
            if (creatureMods != null) { CustomModifiers = creatureMods; }
            if (APIcreatureMods != null) { APIAdded = APIcreatureMods; }

            // Update major modifiers
            Logger.LogDebug("Merging config Major mod definitions");
            if (CustomModifiers.MajorModifiers != null &&  CustomModifiers.MajorModifiers.Count > 0) { CreatureModifiers.MajorModifiers.AddRange(CustomModifiers.MajorModifiers); }
            if (APIAdded.MajorModifiers != null && APIAdded.MajorModifiers.Count > 0) { CreatureModifiers.MajorModifiers.AddRange(APIAdded.MajorModifiers); }
            if (APIAdded.MajorModifiers == null && CustomModifiers.MajorModifiers == null) { CreatureModifiers.MajorModifiers.AddRange(DefaultModifiers.MajorModifiers); }

            // Update minor modifiers
            Logger.LogDebug("Merging config Minor mod definitions");
            if (CustomModifiers.MinorModifiers != null && CustomModifiers.MinorModifiers.Count > 0) { CreatureModifiers.MinorModifiers.AddRange(CustomModifiers.MinorModifiers); }
            if (APIAdded.MinorModifiers != null && APIAdded.MinorModifiers.Count > 0) { CreatureModifiers.MinorModifiers.AddRange(APIAdded.MinorModifiers); }
            if (APIAdded.MinorModifiers == null && CustomModifiers.MinorModifiers == null) { CreatureModifiers.MinorModifiers.AddRange(DefaultModifiers.MinorModifiers); }

            // Update major modifiers
            Logger.LogDebug("Merging config Boss mod definitions");
            if (CustomModifiers.BossModifiers != null && CustomModifiers.BossModifiers.Count > 0) { CreatureModifiers.BossModifiers.AddRange(CustomModifiers.BossModifiers); }
            if (APIAdded.BossModifiers != null && APIAdded.BossModifiers.Count > 0) { CreatureModifiers.BossModifiers.AddRange(APIAdded.BossModifiers); }
            if (APIAdded.BossModifiers == null && CustomModifiers.BossModifiers == null) { CreatureModifiers.BossModifiers.AddRange(DefaultModifiers.BossModifiers); }
        }

        internal static string GetModifierDefaultConfig() {
            var yaml = DataObjects.yamlserializer.Serialize(DefaultModifiers);
            return yaml;
        }

        internal static bool UpdateModifierConfig(string yaml)
        {
            try {
                CreatureModifierCollection modcollection = DataObjects.yamldeserializer.Deserialize<CreatureModifierCollection>(yaml);
                UpdateModifiers(modcollection);
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
            if (CreatureModifiers.MinorModifiers != null) {
                foreach (var mod in CreatureModifiers.MinorModifiers) {
                    mod.Value.LoadAndSetGameObjects();
                }
            }
            if (CreatureModifiers.MajorModifiers != null) {
                foreach (var mod in CreatureModifiers.MajorModifiers) {
                    mod.Value.LoadAndSetGameObjects();
                }
            }
            if (CreatureModifiers.BossModifiers != null) {
                foreach (var mod in CreatureModifiers.BossModifiers) {
                    mod.Value.LoadAndSetGameObjects();
                }
            }
        }


        internal static void Init()
        {
            // Read config file?
            UpdateModifiers();
        }
    }
}
