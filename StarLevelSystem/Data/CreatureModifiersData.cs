using MonoMod.Utils;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Data
{
    public static class CreatureModifiersData
    {
        public static CreatureModifierCollection CreatureModifiers = new CreatureModifierCollection() {
            MajorModifiers = new Dictionary<string, CreatureModifier>(),
            MinorModifiers = new Dictionary<string, CreatureModifier>(),
            BossModifiers = new Dictionary<string, CreatureModifier>()
        };

        public static Dictionary<string,List<ProbabilityEntry>> cMajorModifierProbabilityList = new Dictionary<string,List<ProbabilityEntry>>();
        public static Dictionary<string,List<ProbabilityEntry>> cMinorModifierProbabilityList = new Dictionary<string, List<ProbabilityEntry>>();

        static CreatureModifierCollection CustomModifiers = new CreatureModifierCollection();
        static CreatureModifierCollection APIAdded = new CreatureModifierCollection();
        static CreatureModifierCollection DefaultModifiers = new CreatureModifierCollection() {
            MajorModifiers = new Dictionary<string, CreatureModifier>() {
                {"Fire", new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$fire_prefix1", "$fire_prefix2", "$fire_prefix3" },
                    name_suffixes = new List<string>() { "$fire_suffix1" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    visualEffectStyle = VisualEffectStyle.bottom,
                    visualEffect = "creatureFire",
                    starVisual = "flame",
                    config = new CreatureModConfig() {
                        perlevelpower = 1f,
                        basepower = 20f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Flame"
                    }
                },
                {"Frost", new CreatureModifier() {
                    selectionWeight = 10,
                    name_prefixes = new List<string>() { "$frost_prefix1", "$frost_prefix2", "$frost_prefix3" },
                    name_suffixes = new List<string>() { "$frost_suffix1" },
                    namingConvention = NameSelectionStyle.RandomFirst,
                    visualEffect = "creatureFrost",
                    starVisual = "snowflake",
                    config = new CreatureModConfig() {
                        perlevelpower = 1f,
                        basepower = 20f
                        },
                    setupMethodClass = "StarLevelSystem.Modifiers.Frost"
                    }
                }
            },
            MinorModifiers = new Dictionary<string, CreatureModifier>() {
                {"Alert", new CreatureModifier() {
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
                {"Big", new CreatureModifier() {
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
                }
            }
        };

        public static CreatureModConfig GetConfig(string name, ModifierType type = ModifierType.Major) {
            // Check minor if requested, otherwise default to major
            if (type == ModifierType.Minor) {
                if (!CreatureModifiers.MinorModifiers.ContainsKey(name)) { return new CreatureModConfig() { }; }
                return CreatureModifiers.MinorModifiers[name].config;
            }
            if (!CreatureModifiers.MajorModifiers.ContainsKey(name)) { return new CreatureModConfig() { }; }
            return CreatureModifiers.MajorModifiers[name].config;
        }

        public static List<ProbabilityEntry> LazyCacheCreatureModifierSelect(string creature, ModifierType type = ModifierType.Major) {
            if (type == ModifierType.Major) {
                if (cMajorModifierProbabilityList.ContainsKey(creature)) { return cMajorModifierProbabilityList[creature]; }
            } else {
                if (cMinorModifierProbabilityList.ContainsKey(creature)) { return cMinorModifierProbabilityList[creature]; }
            }

            if (type == ModifierType.Minor) {
                List<ProbabilityEntry> minorProbability = BuildProbabilityEntries(creature, CreatureModifiers.MinorModifiers);
                cMinorModifierProbabilityList.Add(creature, minorProbability);
                return minorProbability;
            }

            List<ProbabilityEntry> majorProbability = BuildProbabilityEntries(creature, CreatureModifiers.MajorModifiers);
            cMajorModifierProbabilityList.Add(creature, majorProbability);
            return majorProbability;
        }

        private static List<ProbabilityEntry> BuildProbabilityEntries(string creature, Dictionary<string, CreatureModifier> modifiers) {
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
