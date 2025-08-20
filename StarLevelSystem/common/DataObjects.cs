using Jotunn.Configs;
using Jotunn.Managers;
using StarLevelSystem.modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
            Size,
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
            SizePerLevel,
        }

        public enum DamageType
        {
            Blunt,
            Slash,
            Pierce,
            Fire,
            Frost,
            Lightning,
            Poison,
            Spirit,
            Chop,
            Pickaxe,
        }

        public enum NameSelectionStyle
        {
            RandomFirst,
            RandomLast,
            RandomBoth
        }

        public enum VisualEffectStyle
        {
            objectCenter,
            top,
            bottom
        }

        public static List<CreaturePerLevelAttribute> CreaturePerLevelAttributes = new List<CreaturePerLevelAttribute> {
            CreaturePerLevelAttribute.HealthPerLevel,
            CreaturePerLevelAttribute.DamagePerLevel,
            CreaturePerLevelAttribute.SpeedPerLevel,
            CreaturePerLevelAttribute.SizePerLevel,
        };

        public enum ModifierType
        {
            Major = 0,
            Minor = 1,
            Boss = 2
        }

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
            public int MaxMajorModifiers { get; set; } = -1;
            public float ChanceForMajorModifier { get; set; } = -1f;
            public int MaxMinorModifiers { get; set; } = -1;
            public float ChanceForMinorModifier { get; set; } = -1f;
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

        public class ProbabilityEntry {
            public string Name { get; set; }
            public float selectionWeight { get; set; } = 1f;
        }

        public class CreatureModifier
        {
            public NameSelectionStyle namingConvention { get; set; } = NameSelectionStyle.RandomBoth;
            public List<string> name_prefixes { get; set; }
            public List<string> name_suffixes { get; set; }
            public float selectionWeight { get; set; } = 1f;
            public CreatureModConfig config { get; set; } = new CreatureModConfig();
            public string starVisual {  get; set; }
            public Sprite starVisualPrefab { get; set; }
            public string visualEffect { get; set; }
            public GameObject visualEffectPrefab { get; set; }
            public VisualEffectStyle visualEffectStyle { get; set; } = VisualEffectStyle.objectCenter;
            public List<string> allowedCreatures { get; set; } = new List<string>() { };
            public List<string> unallowedCreatures { get; set; } = new List<string>() { };
            public string setupMethodClass { get; set; }

            // Add fallbacks to load prefabs that are not in the embedded resource bundle
            public void LoadAndSetGameObjects() {
                if (starVisual != null) {
                    starVisualPrefab = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>(starVisual);
                }
                if (visualEffect != null) {
                    visualEffectPrefab = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>(visualEffect);
                }
            }

            public void SetupMethodCall(Character chara, CreatureModConfig cfg, CreatureDetailCache cdc) {
                Type methodClass = Type.GetType(setupMethodClass);
                Logger.LogInfo($"Setting up modifier {setupMethodClass} with signature {methodClass}");
                MethodInfo theMethod = methodClass.GetMethod("Setup");
                theMethod.Invoke(this, new object[] { chara, cfg, cdc });
            }
        }

        public class CreatureModConfig {
            public float perlevelpower { get; set; }
            public float basepower { get; set; }
        }

        public class CreatureModifierCollection
        {
            public Dictionary<string, CreatureModifier> MajorModifiers { get; set; }
            public Dictionary<string, CreatureModifier> MinorModifiers { get; set; }
            public Dictionary<string, CreatureModifier> BossModifiers { get; set; }
        }

        public class CreatureDetailCache {
            public bool creatureDisabledInBiome { get; set; } = false;
            public int Level { get; set; }
            public Dictionary<string, ModifierType> Modifiers { get; set; }
            public Dictionary<string, List<string>> ModifierPrefixNames { get; set; } = new Dictionary<string, List<string>>();
            public Dictionary<string, List<string>> ModifierSuffixNames { get; set; } = new Dictionary<string, List<string>>();
            public ColorDef Colorization { get; set; }
            public Heightmap.Biome Biome { get; set; }
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; } = new Dictionary<CreatureBaseAttribute, float>() {
                { CreatureBaseAttribute.BaseDamage, 1f },
                { CreatureBaseAttribute.BaseHealth, 1f },
                { CreatureBaseAttribute.Size, 1f },
                { CreatureBaseAttribute.Speed, 1f },
            };
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; } = new Dictionary<CreaturePerLevelAttribute, float>() {
                { CreaturePerLevelAttribute.DamagePerLevel, 0f },
                { CreaturePerLevelAttribute.HealthPerLevel, ValConfig.EnemyHealthMultiplier.Value },
                { CreaturePerLevelAttribute.SizePerLevel, ValConfig.PerLevelScaleBonus.Value },
                { CreaturePerLevelAttribute.SpeedPerLevel, 0f },
            };
            public Dictionary<DamageType, float> CreatureDamageBonus { get; set; } = new Dictionary<DamageType, float>() {};
        }

        [DataContract]
        public class ExtendedDrop
        {
            // Use fractional scaling for decaying drop increases
            public Drop Drop { get; set; }
            public CharacterDrop.Drop gameDrop { get; private set; }
            public float amountScaleFactor { get; set; } = 0f;
            public float chanceScaleFactor { get; set; } = 0f;
            public bool useChanceAsMultiplier { get; set; } = false;
            // Scale amount dropped from the base amount to max, based on level
            public bool scalebyMaxLevel { get; set; } = false;
            public bool doesNotScale { get; set; } = false;
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


        public abstract class ZNetProperty<T>
        {
            public string Key { get; private set; }
            public T DefaultValue { get; private set; }
            protected readonly ZNetView zNetView;

            protected ZNetProperty(string key, ZNetView zNetView, T defaultValue)
            {
                Key = key;
                DefaultValue = defaultValue;
                this.zNetView = zNetView;
            }

            private void ClaimOwnership()
            {
                if (!zNetView.IsOwner())
                {
                    zNetView.ClaimOwnership();
                }
            }

            public void Set(T value)
            {
                SetValue(value);
            }

            public void ForceSet(T value)
            {
                ClaimOwnership();
                Set(value);
            }

            public abstract T Get();

            protected abstract void SetValue(T value);
        }

        public class ListStringZNetProperty : ZNetProperty<List<string>>
        {
            BinaryFormatter binFormatter = new BinaryFormatter();
            public ListStringZNetProperty(string key, ZNetView zNetView, List<string> defaultValue) : base(key, zNetView, defaultValue)
            {
            }

            public override List<string> Get()
            {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return new List<string>(); }
                var mStream = new MemoryStream(stored);
                var deserializedDictionary = (List<String>)binFormatter.Deserialize(mStream);
                return deserializedDictionary;
            }

            protected override void SetValue(List<string> value)
            {
                var mStream = new MemoryStream();
                binFormatter.Serialize(mStream, value);

                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

        public class DictionaryDmgNetProperty : ZNetProperty<Dictionary<DamageType, float>>
        {
            BinaryFormatter binFormatter = new BinaryFormatter();
            public DictionaryDmgNetProperty(string key, ZNetView zNetView, Dictionary<DamageType, float> defaultValue) : base(key, zNetView, defaultValue)
            {
            }

            public override Dictionary<DamageType, float> Get()
            {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return new Dictionary<DamageType, float>(); }
                var mStream = new MemoryStream(stored);
                var deserializedDictionary = (Dictionary<DamageType, float>)binFormatter.Deserialize(mStream);
                return deserializedDictionary;
            }

            protected override void SetValue(Dictionary<DamageType, float> value)
            {
                var mStream = new MemoryStream();
                binFormatter.Serialize(mStream, value);

                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

    }
}
