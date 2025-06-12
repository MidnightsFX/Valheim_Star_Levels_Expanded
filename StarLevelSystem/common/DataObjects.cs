using System.Collections.Generic;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using StarLevelSystem.modules;
using static StarLevelSystem.modules.LootLevelsExpanded;
using Jotunn.Managers;
using System.Runtime.Serialization;

namespace StarLevelSystem.common
{
    public class DataObjects
    {

        public static IDeserializer yamldeserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        public static ISerializer yamlserializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).DisableAliases().ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults).Build();

        public enum CreatureBaseAttribute {
            BaseHealth,
            BaseDamage,
            Speed,
        }

        public static List<CreatureBaseAttribute> CreatureBaseAttributes = new List<CreatureBaseAttribute> {
            CreatureBaseAttribute.BaseHealth,
            CreatureBaseAttribute.BaseDamage,
            CreatureBaseAttribute.Speed,
        };

        public enum CreaturePerLevelAttribute
        {
            HealthPerLevel,
            DamagePerLevel,
            SpeedPerLevel,
        }

        public static List<CreaturePerLevelAttribute> CreaturePerLevelAttributes = new List<CreaturePerLevelAttribute> {
            CreaturePerLevelAttribute.HealthPerLevel,
            CreaturePerLevelAttribute.DamagePerLevel,
            CreaturePerLevelAttribute.SpeedPerLevel,
        };

        public class CreatureLevelSettings {
            public Dictionary<Heightmap.Biome, BiomeSpecificSetting> BiomeConfiguration { get; set; }
            public Dictionary<string, CreatureSpecificSetting> CreatureConfiguration { get; set; }
            public SortedDictionary<int, float> DefaultCreatureLevelUpChance { get; set; }
            public bool EnableDistanceLevelBonus { get; set; } = false;
            public SortedDictionary<int, SortedDictionary<int, float>> DistanceLevelBonus { get; set; }
        }

        public class BiomeSpecificSetting {
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }
            public bool EnableBiomeLevelOverride { get; set; } = false;
            public int BiomeMaxLevelOverride { get; set; }
            public float DistanceScaleModifier { get; set; } = 1f;
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }
            public List<string> creatureSpawnsDisabled { get; set; }
        }

        public class CreatureSpecificSetting {
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }
            public bool EnableCreatureLevelOverride { get; set; } = false;
            public int CreatureMaxLevelOverride { get; set; }
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }
        }

        public class CreatureColorizationSettings {
            public Dictionary<string, Dictionary<int, ColorDef>> characterSpecificColorization { get; set; }
            public Dictionary<int, ColorDef> defaultLevelColorization { get; set; }
        }

        public class LootSettings {
            public Dictionary<string, List<ExtendedDrop>> characterSpecificLoot { get; set; }
            public bool EnableDistanceLootModifier { get; set; } = false;
            public SortedDictionary<int, DistanceLootModifier> DistanceLootModifier { get; set; }
        }

        public class DistanceLootModifier {
            public float minAmountScaleFactorBonus { get; set; } = 0f;
            public float maxAmountScaleFactorBonus { get; set; } = 0f;
            public float chanceScaleFactorBonus { get; set; } = 0f;
        }

        [DataContract]
        public class ExtendedDrop
        {
            // Use fractional scaling for decaying drop increases
            public Drop Drop { get; set; }
            public CharacterDrop.Drop gameDrop { get; private set; }
            public float minAmountScaleFactor { get; set; } = 0f;
            public float maxAmountScaleFactor { get; set; } = 0f;
            public float chanceScaleFactor { get; set; } = 0f;
            public bool useChanceAsMultiplier { get; set; } = false;
            // Scale amount dropped from the base amount to max, based on level
            public bool scalebyMaxLevel { get; set; } = false;
            public int maxScaledAmount { get; set; } = 0;
            // Modify drop amount based on creature stars
            public bool scalePerNearbyPlayer { get; set; } = false;
            public bool untamedOnlyDrop { get; set; } = false;
            public bool tamedOnlyDrop { get; set; } = false;
            public void SetupDrop() {
                gameDrop = Drop.ToCharDrop();
            }
        }

        [DataContract]
        public class Drop
        {
            public string prefab { get; set; }
            public int min { get; set; } = 1;
            public int max { get; set; } = 1;
            public float chance { get; set; } = 1f;
            public bool onePerPlayer { get; set; } = false;
            public bool levelMultiplier { get; set; } = true;
            public bool dontScale { get; set; } = false;

            public CharacterDrop.Drop ToCharDrop()
            {
                return new CharacterDrop.Drop
                {
                    m_prefab = PrefabManager.Instance.GetPrefab(prefab),
                    m_amountMin = min,
                    m_amountMax = max,
                    m_chance = chance,
                    m_onePerPlayer = onePerPlayer,
                    m_levelMultiplier = levelMultiplier,
                    m_dontScale = dontScale
                };
            }
        }


    }
}
