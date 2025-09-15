using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.common
{
    public class DataObjects
    {

        public static IDeserializer yamldeserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        public static ISerializer yamlserializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults).Build();

        public enum CreatureBaseAttribute {
            BaseHealth,
            BaseDamage,
            AttackSpeed,
            Speed,
            Size,
        }

        public static List<CreatureBaseAttribute> CreatureBaseAttributes = new List<CreatureBaseAttribute> {
            CreatureBaseAttribute.BaseHealth,
            CreatureBaseAttribute.BaseDamage,
            CreatureBaseAttribute.Speed,
            CreatureBaseAttribute.AttackSpeed,
        };

        public enum CreaturePerLevelAttribute
        {
            HealthPerLevel,
            DamagePerLevel,
            SpeedPerLevel,
            AttackSpeedPerLevel,
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
            public int BiomeMaxLevelOverride { get; set; }
            [DefaultValue(1f)]
            public float DistanceScaleModifier { get; set; } = 1f;
            [DefaultValue(1f)]
            public float SpawnRateModifier { get; set; } = 1f;
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }
            public Dictionary<DamageType, float> DamageRecievedModifiers { get; set; }
            public List<string> creatureSpawnsDisabled { get; set; }
        }

        public class CreatureSpecificSetting {
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }
            [DefaultValue(-1)]
            public int CreatureMaxLevelOverride { get; set; } = -1;
            [DefaultValue(-1)]
            public int MaxMajorModifiers { get; set; } = -1;
            [DefaultValue(-1f)]
            public float ChanceForMajorModifier { get; set; } = -1f;
            [DefaultValue(-1)]
            public int MaxMinorModifiers { get; set; } = -1;
            [DefaultValue(-1f)]
            public float ChanceForMinorModifier { get; set; } = -1f;
            [DefaultValue(-1)]
            public int MaxBossModifiers { get; set; } = -1;
            [DefaultValue(-1f)]
            public float ChanceForBossModifier { get; set; } = -1f;
            [DefaultValue(1f)]
            public float SpawnRateModifier { get; set; } = 1f;
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }
            public Dictionary<DamageType, float> DamageRecievedModifiers { get; set; }
        }

        public class CreatureColorizationSettings {
            public Dictionary<string, Dictionary<int, ColorDef>> characterSpecificColorization { get; set; }
            public Dictionary<int, ColorDef> defaultLevelColorization { get; set; }
            public Dictionary<string, List<ColorRangeDef>> CharacterColorGenerators { get; set; }
        }

        public class LootSettings {
            public Dictionary<string, List<ExtendedDrop>> characterSpecificLoot { get; set; }
            public bool EnableDistanceLootModifier { get; set; } = false;
            public SortedDictionary<int, DistanceLootModifier> DistanceLootModifier { get; set; }
        }

        public class DistanceLootModifier {
            [DefaultValue(0f)]
            public float MinAmountScaleFactorBonus { get; set; } = 0f;
            [DefaultValue(0f)]
            public float MaxAmountScaleFactorBonus { get; set; } = 0f;
            [DefaultValue(0f)]
            public float ChanceScaleFactorBonus { get; set; } = 0f;
        }

        public class ProbabilityEntry {
            public ModifierNames Name { get; set; }
            [DefaultValue(1f)]
            public float SelectionWeight { get; set; } = 1f;
        }

        public class CreatureModifier
        {
            public NameSelectionStyle namingConvention { get; set; } = NameSelectionStyle.RandomBoth;
            public List<string> NamePrefixes { get; set; }
            public List<string> NameSuffixes { get; set; }
            [DefaultValue(1f)]
            public float SelectionWeight { get; set; } = 1f;
            public CreatureModConfig Config { get; set; } = new CreatureModConfig();
            public string StarVisual { get; set; }
            public string VisualEffect { get; set; }
            public string SecondaryEffect { get; set; }
            public VisualEffectStyle VisualEffectStyle { get; set; } = VisualEffectStyle.objectCenter;
            public List<string> AllowedCreatures { get; set; }
            public List<string> UnallowedCreatures { get; set; }
            public List<Heightmap.Biome> AllowedBiomes { get; set; }
            public string SetupMethodClass { get; set; }

            // Add fallbacks to load prefabs that are not in the embedded resource bundle
            public void LoadAndSetGameObjects() {
                if (StarVisual != null && !CreatureModifiersData.LoadedModifierSprites.ContainsKey(StarVisual)) {
                    Sprite game_obj = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>($"assets/custom/starlevels/icons/{StarVisual}.png");
                    CreatureModifiersData.LoadedModifierSprites.Add(StarVisual, game_obj);
                }
                if (VisualEffect != null && !CreatureModifiersData.LoadedModifierEffects.ContainsKey(VisualEffect)) {
                    GameObject game_obj = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>(VisualEffect);
                    CustomPrefab prefab_obj = new CustomPrefab(game_obj, true);
                    PrefabManager.Instance.AddPrefab(prefab_obj);
                    GameObject mockfixedgo = PrefabManager.Instance.GetPrefab(VisualEffect);
                    CreatureModifiersData.LoadedModifierEffects.Add(VisualEffect, mockfixedgo);
                }
                if (SecondaryEffect != null && !CreatureModifiersData.LoadedSecondaryEffects.ContainsKey(SecondaryEffect)) {
                    GameObject game_obj = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>(SecondaryEffect);
                    CustomPrefab prefab_obj = new CustomPrefab(game_obj, true);
                    PrefabManager.Instance.AddPrefab(prefab_obj);
                    GameObject mockfixedgo = PrefabManager.Instance.GetPrefab(SecondaryEffect);
                    CreatureModifiersData.LoadedSecondaryEffects.Add(SecondaryEffect, mockfixedgo);
                }
            }

            public void SetupMethodCall(Character chara, CreatureModConfig cfg, CreatureDetailCache cdc) {
                if (SetupMethodClass == null || SetupMethodClass == "") { return; }
                Type methodClass = Type.GetType(SetupMethodClass);
                //Logger.LogDebug($"Setting up modifier {setupMethodClass} with signature {methodClass}");
                MethodInfo theMethod = methodClass.GetMethod("Setup");
                if (theMethod == null) {
                    Logger.LogWarning($"Could not find setup method, skipping setup.");
                    return;
                }
                theMethod.Invoke(this, new object[] { chara, cfg, cdc });
            }
        }

        public class CreatureModConfig {
            public float PerlevelPower { get; set; }
            public float BasePower { get; set; }
            public Dictionary<Heightmap.Biome, List<string>> BiomeObjects { get; set; }
        }

        public class CreatureModifierCollection
        {
            public Dictionary<ModifierNames, CreatureModifier> MajorModifiers { get; set; }
            public Dictionary<ModifierNames, CreatureModifier> MinorModifiers { get; set; }
            public Dictionary<ModifierNames, CreatureModifier> BossModifiers { get; set; }
        }

        public class CreatureDetailCache {
            public bool CreatureDisabledInBiome { get; set; } = false;
            public bool CreatureCheckedSpawnMult { get; set; } = false;
            public int Level { get; set; }
            public Dictionary<ModifierNames, ModifierType> Modifiers { get; set; }
            public Dictionary<ModifierNames, List<string>> ModifierPrefixNames { get; set; } = new Dictionary<ModifierNames, List<string>>();
            public Dictionary<ModifierNames, List<string>> ModifierSuffixNames { get; set; } = new Dictionary<ModifierNames, List<string>>();
            public ColorDef Colorization { get; set; }
            public Heightmap.Biome Biome { get; set; }
            public GameObject CreaturePrefab { get; set; }
            public Dictionary<DamageType, float> DamageRecievedModifiers { get; set; } = new Dictionary<DamageType, float>() {
                { DamageType.Blunt, 1f },
                { DamageType.Pierce, 1f },
                { DamageType.Slash, 1f },
                { DamageType.Fire, 1f },
                { DamageType.Frost, 1f },
                { DamageType.Lightning, 1f },
                { DamageType.Poison, 1f },
                { DamageType.Spirit, 1f },
            };
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; } = new Dictionary<CreatureBaseAttribute, float>() {
                { CreatureBaseAttribute.BaseDamage, 1f },
                { CreatureBaseAttribute.BaseHealth, 1f },
                { CreatureBaseAttribute.Size, 1f },
                { CreatureBaseAttribute.Speed, 1f },
                { CreatureBaseAttribute.AttackSpeed, 1f },
            };
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; } = new Dictionary<CreaturePerLevelAttribute, float>() {
                { CreaturePerLevelAttribute.DamagePerLevel, 0f },
                { CreaturePerLevelAttribute.HealthPerLevel, ValConfig.EnemyHealthMultiplier.Value },
                { CreaturePerLevelAttribute.SizePerLevel, ValConfig.PerLevelScaleBonus.Value },
                { CreaturePerLevelAttribute.SpeedPerLevel, 0f },
                { CreaturePerLevelAttribute.AttackSpeedPerLevel, 0f },
            };
            public Dictionary<DamageType, float> CreatureDamageBonus { get; set; } = new Dictionary<DamageType, float>() {};
        }

        [DataContract]
        public class ExtendedDrop
        {
            // Use fractional scaling for decaying drop increases
            public Drop Drop { get; set; }
            public CharacterDrop.Drop GameDrop { get; private set; }
            [DefaultValue(0f)]
            public float AmountScaleFactor { get; set; } = 0f;
            [DefaultValue(0f)]
            public float ChanceScaleFactor { get; set; } = 0f;
            public bool UseChanceAsMultiplier { get; set; } = false;
            // Scale amount dropped from the base amount to max, based on level
            public bool ScalebyMaxLevel { get; set; } = false;
            public bool DoesNotScale { get; set; } = false;
            [DefaultValue(0)]
            public int MaxScaledAmount { get; set; } = 0;
            // Modify drop amount based on creature stars
            public bool ScalePerNearbyPlayer { get; set; } = false;
            public bool UntamedOnlyDrop { get; set; } = false;
            public bool TamedOnlyDrop { get; set; } = false;
            public void SetupDrop() {
                GameDrop = Drop.ToCharDrop();
            }
        }

        [DataContract]
        public class Drop
        {
            public string Prefab { get; set; }
            [DefaultValue(1)]
            public int Min { get; set; } = 1;
            [DefaultValue(1)]
            public int Max { get; set; } = 1;
            [DefaultValue(1f)]
            public float Chance { get; set; } = 1f;
            public bool OnePerPlayer { get; set; } = false;
            public bool LevelMultiplier { get; set; } = true;
            public bool DontScale { get; set; } = false;

            public CharacterDrop.Drop ToCharDrop()
            {
                return new CharacterDrop.Drop
                {
                    m_prefab = PrefabManager.Instance.GetPrefab(Prefab),
                    m_amountMin = Min,
                    m_amountMax = Max,
                    m_chance = Chance,
                    m_onePerPlayer = OnePerPlayer,
                    m_levelMultiplier = LevelMultiplier,
                    m_dontScale = DontScale
                };
            }
        }

        public class ColorDef
        {
            public float hue { get; set; } = 0f;
            public float saturation { get; set; } = 0f;
            public float value { get; set; } = 0f;
            public bool is_emissive { get; set; } = false;

            public LevelEffects.LevelSetup toLevelEffect()
            {
                return new LevelEffects.LevelSetup()
                {
                    m_scale = 1f,
                    m_hue = hue,
                    m_saturation = saturation,
                    m_value = value,
                    m_setEmissiveColor = is_emissive,
                    m_emissiveColor = new Color(hue, saturation, value)
                };
            }
        }

        [DataContract]
        public class ColorRangeDef
        {
            [DefaultValue(true)]
            public bool CharacterSpecific { get; set; } = true;
            [DefaultValue(false)]
            public bool OverwriteExisting { get; set; } = false;
            public ColorDef StartColorDef { get; set; }
            public ColorDef EndColorDef { get; set; }
            public int RangeStart { get; set; }
            public int RangeEnd { get; set; }
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

        public class ListModifierZNetProperty : ZNetProperty<List<ModifierNames>>
        {
            BinaryFormatter binFormatter = new BinaryFormatter();
            public ListModifierZNetProperty(string key, ZNetView zNetView, List<ModifierNames> defaultValue) : base(key, zNetView, defaultValue)
            {
            }

            public override List<ModifierNames> Get()
            {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return new List<ModifierNames>(); }
                var mStream = new MemoryStream(stored);
                var deserializedDictionary = (List<ModifierNames>)binFormatter.Deserialize(mStream);
                return deserializedDictionary;
            }

            protected override void SetValue(List<ModifierNames> value)
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
