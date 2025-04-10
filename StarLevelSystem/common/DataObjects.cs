using System.Collections.Generic;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace StarLevelSystem.common
{
    public class DataObjects
    {

        public static IDeserializer yamldeserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        public static ISerializer yamlserializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).DisableAliases().Build();
        public class CreatureLevelSettings {
            public Dictionary<Heightmap.Biome, BiomeSpecificSetting> BiomeConfiguration { get; set; }
            public Dictionary<string, CreatureSpecificSetting> CreatureConfiguration { get; set; }
            public SortedDictionary<int, float> DefaultCreatureLevelUpChance { get; set; }
        }

        public class BiomeSpecificSetting {
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }

            public bool EnableBiomeLevelOverride { get; set; } = false;
            public int BiomeMaxLevelOverride { get; set; }
            public float CreatureSpawnHealthPerLevelBonus { get; set; }
            public float CreatureSpawnDamagePerLevelBonus { get; set; }
            public float CreatureLootMultiplierPerLevel { get; set; }
        }

        public class CreatureSpecificSetting {
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }
            public bool EnableCreatureLevelOverride { get; set; } = false;
            public int CreatureMaxLevelOverride { get; set; }
            public float CreatureSpawnHealthPerLevelBonus { get; set; }
            public float CreatureSpawnDamagePerLevelBonus { get; set; }
            public float CreatureLootMultiplierPerLevel { get; set; }
        }
    }
}
