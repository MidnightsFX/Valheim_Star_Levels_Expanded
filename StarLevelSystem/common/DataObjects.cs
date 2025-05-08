using System.Collections.Generic;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using StarLevelSystem.modules;

namespace StarLevelSystem.common
{
    public class DataObjects
    {

        public static IDeserializer yamldeserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        public static ISerializer yamlserializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).DisableAliases().ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults).Build();
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
            public float CreatureSpawnHealthPerLevelBonus { get; set; }
            public float CreatureSpawnDamagePerLevelBonus { get; set; }
            public float CreatureLootMultiplierPerLevel { get; set; }
            public float DistanceScaleModifier { get; set; } = 1f;
        }

        public class CreatureSpecificSetting {
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }
            public bool EnableCreatureLevelOverride { get; set; } = false;
            public int CreatureMaxLevelOverride { get; set; }
            public float CreatureSpawnHealthPerLevelBonus { get; set; }
            public float CreatureSpawnDamagePerLevelBonus { get; set; }
            public float CreatureLootMultiplierPerLevel { get; set; }
        }

        public class CreatureColorizationSettings {
            public Dictionary<string, Dictionary<int, ColorDef>> characterSpecificColorization { get; set; }
            public Dictionary<int, ColorDef> defaultLevelColorization { get; set; }
        }
    }
}
