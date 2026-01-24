using JetBrains.Annotations;
using Jotunn.Entities;
using Jotunn.Managers;
using MonoMod.Utils;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
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

        //public static IDeserializer yamldeserializerMinified = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        public static ISerializer yamlserializerJsonCompat = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).JsonCompatible().Build();

        public static readonly string SLS_CREATURE = "SLS_CREATURE";
        public static readonly string SLS_SIZE = "SLS_SIZE";
        public static readonly string SLS_DAMAGE_MODIFIER = "SLS_DMOD";
        public static readonly string SLS_DAMAGE_BONUSES = "SLS_DBON";
        public static readonly string SLS_SPAWN_MULT = "SLS_MULT";
        public static readonly string SLS_MODIFIERS = "SLS_MODS";
        public static readonly string SLS_MODSV2 = "SLS_MODV2";
        public static readonly string SLS_CHARNAME = "SLS_CHARNAME";
        public static readonly string SLS_TREE = "SLE_Tree";
        public static readonly string SLS_FISH = "SLE_Fish";
        public static readonly string SLS_BIRD = "SLE_Bird";

        public enum CreatureBaseAttribute {
            BaseHealth = 0,
            BaseDamage = 1,
            AttackSpeed = 2,
            Speed = 3,
            Size = 4,
        }

        public static List<CreatureBaseAttribute> CreatureBaseAttributes = new List<CreatureBaseAttribute> {
            CreatureBaseAttribute.BaseHealth,
            CreatureBaseAttribute.BaseDamage,
            CreatureBaseAttribute.Speed,
            CreatureBaseAttribute.AttackSpeed,
            CreatureBaseAttribute.Size
        };

        public enum CreaturePerLevelAttribute
        {
            HealthPerLevel = 0,
            DamagePerLevel = 1,
            SpeedPerLevel = 2,
            AttackSpeedPerLevel = 3,
            SizePerLevel = 4,
        }

        public enum DamageType
        {
            Blunt = 0,
            Slash = 1,
            Pierce = 2,
            Fire = 3,
            Frost = 4,
            Lightning = 5,
            Poison = 6,
            Spirit = 7,
            Chop = 8,
            Pickaxe = 9,
        }

        public enum NameSelectionStyle
        {
            RandomFirst = 0,
            RandomLast = 1,
            RandomBoth = 2,
        }

        public enum VisualEffectStyle
        {
            objectCenter = 0,
            top = 1,
            bottom = 2,
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

        public enum DropType {
            Tree,
            Rock,
            Destructible
        }

        public class DNum {
            private static Dictionary<int, string> _enumReverseLookup = new Dictionary<int, string>();
            private static Dictionary<string, int> _enumData = new Dictionary<string, int>();

            public DNum() { }

            public DNum(Array modnames)
            {
                foreach(int enumvalue in modnames)
                {
                    string name = Enum.GetName(typeof(ModifierNames), enumvalue);
                    _enumData[name] = enumvalue;
                    _enumReverseLookup[enumvalue] = name;
                }
            }
            public DNum(Dictionary<string, int> initialValues) {
                _enumData.AddRange(initialValues);
                foreach (var pair in initialValues) {
                    _enumReverseLookup[pair.Value] = pair.Key;
                }
            }

            public void AddValue(string name, int id)
            {
                if (_enumData.ContainsKey(name)) {
                    Logger.LogWarning($"Tried to add duplicate enum name {name} to DNum, skipping.");
                }
                _enumData[name] = id;
                _enumReverseLookup[id] = name;
            }

            [CanBeNull]
            public string GetValue(int id) {
                return _enumReverseLookup.TryGetValue(id, out string value) ? value : null;
            }

            public int GetValue(string name) {
                return _enumData.TryGetValue(name, out int id) ? id : -1;
            }
            public bool ContainsName(string name) {
                return _enumData.ContainsKey(name);
            }
            public bool ContainsID(int id) {
                return _enumReverseLookup.ContainsKey(id);
            }
        }


        public class CreatureLevelSettings {
            public Dictionary<Heightmap.Biome, BiomeSpecificSetting> BiomeConfiguration { get; set; }
            public Dictionary<string, CreatureSpecificSetting> CreatureConfiguration { get; set; }
            public SortedDictionary<int, float> DefaultCreatureLevelUpChance { get; set; }
            public bool EnableDistanceLevelBonus { get; set; } = false;
            public SortedDictionary<int, SortedDictionary<int, float>> DistanceLevelBonus { get; set; }
        }

        public class NightSettings
        {
            [DefaultValue(1f)]
            public float SpawnRateModifier { get; set; } = 1f;
            [DefaultValue(1f)]
            public float NightLevelUpChanceScaler { get; set; } = 1f;
            [DefaultValue(false)]
            public bool creatureSpawnsDisabled { get; set; } = false;
        }

        public class BiomeNightSettings
        {
            [DefaultValue(1f)]
            public float SpawnRateModifier { get; set; } = 1f;
            [DefaultValue(1f)]
            public float NightLevelUpChanceScaler { get; set; } = 1f;
            public List<string> creatureSpawnsDisabled { get; set; }
        }

        public class BiomeSpecificSetting {
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }
            [DefaultValue(0)]
            public int BiomeMinLevelOverride { get; set; }
            public int BiomeMaxLevelOverride { get; set; }
            [DefaultValue(1f)]
            public float DistanceScaleModifier { get; set; } = 1f;
            [DefaultValue(1f)]
            public float SpawnRateModifier { get; set; } = 1f;
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }
            public Dictionary<DamageType, float> DamageRecievedModifiers { get; set; }
            public List<string> creatureSpawnsDisabled { get; set; }
            public BiomeNightSettings NightSettings { get; set; }
        }

        public class CreatureSpecificSetting {
            [DefaultValue(1f)]
            public float DistanceScaleModifier { get; set; } = 1f;
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }
            [DefaultValue(-1)]
            public int CreatureMinLevelOverride { get; set; } = -1;
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
            public Dictionary<string, ModifierType> RequiredModifiers { get; set; }
            public float SpawnRateModifier { get; set; } = 1f;
            public NightSettings NightSettings { get; set; }
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }
            public Dictionary<DamageType, float> DamageRecievedModifiers { get; set; }
        }

        [DataContract]
        public class CreatureColorizationSettings {
            public Dictionary<string, Dictionary<int, ColorDef>> characterSpecificColorization { get; set; }
            public Dictionary<int, ColorDef> defaultLevelColorization { get; set; }
            public Dictionary<string, List<ColorRangeDef>> characterColorGenerators { get; set; }
        }

        public class LootSettings {
            public Dictionary<string, List<ExtendedCharacterDrop>> characterSpecificLoot { get; set; }
            public Dictionary<string, List<ExtendedObjectDrop>> nonCharacterSpecificLoot { get; set; }
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
            public string Name { get; set; }
            [DefaultValue(1f)]
            public float SelectionWeight { get; set; } = 1f;
        }

        public class CreatureModifierConfiguration
        {
            [DefaultValue(1f)]
            public float SelectionWeight { get; set; } = 1f;
            public CreatureModConfig Config { get; set; } = new CreatureModConfig();
            public List<string> AllowedCreatures { get; set; }
            public List<string> UnallowedCreatures { get; set; }
            public List<Heightmap.Biome> AllowedBiomes { get; set; }
        }

        public class CreatureModifierDefinition
        {
            public NameSelectionStyle namingConvention { get; set; } = NameSelectionStyle.RandomBoth;
            public string NamePrefix { get; set; }
            public string NameSuffix { get; set; }
            public string StarVisual { get; set; }
            public string VisualEffect { get; set; }
            public string SecondaryEffect { get; set; }
            public VisualEffectStyle VisualEffectStyle { get; set; } = VisualEffectStyle.objectCenter;
            public Delegate SetupEvent { get; set; } = null;
            public Delegate RunOnceEvent { get; set; } = null;
            public bool FromAPI { get; set; } = false;
            public Sprite StarVisualAPI { get; set; }
            public GameObject VisualEffectAPI { get; set; }
            public GameObject SecondaryEffectAPI { get; set; }

            // Add fallbacks to load prefabs that are not in the embedded resource bundle
            public void LoadAndSetGameObjects() {
                if (FromAPI) {
                    LoadAPIGameObjects();
                    return;
                }
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

            public void LoadAPIGameObjects() {
                if (StarVisualAPI != null && !CreatureModifiersData.LoadedModifierSprites.ContainsKey(StarVisual)) {
                    CreatureModifiersData.LoadedModifierSprites.Add(StarVisual, StarVisualAPI);
                }
                if (VisualEffectAPI != null && !CreatureModifiersData.LoadedModifierEffects.ContainsKey(VisualEffect)) {
                    CreatureModifiersData.LoadedModifierEffects.Add(VisualEffect, VisualEffectAPI);
                }
                if (SecondaryEffectAPI != null && !CreatureModifiersData.LoadedSecondaryEffects.ContainsKey(SecondaryEffect)) {
                    CreatureModifiersData.LoadedSecondaryEffects.Add(SecondaryEffect, SecondaryEffectAPI);
                }
            }

            public void RunOnceMethodCall(Character chara, CreatureModConfig cfg, CharacterCacheEntry scd)
            {
                if (RunOnceEvent == null) { return; }
                RunOnceEvent.DynamicInvoke(chara, cfg, scd);
            }

            public void SetupMethodCall(Character chara, CreatureModConfig cfg, CharacterCacheEntry scd) {
                if (SetupEvent == null) { return; }
                SetupEvent.DynamicInvoke(chara, cfg, scd);
            }
        }

        public class CreatureModConfig {
            public float PerlevelPower { get; set; }
            public float BasePower { get; set; }
            public Dictionary<Heightmap.Biome, List<string>> BiomeObjects { get; set; }
        }

        public class CreatureModifierCollection
        {
            public GlobalModifierSettings ModifierGlobalSettings { get; set; } = new GlobalModifierSettings();
            public Dictionary<string, CreatureModifierConfiguration> MajorModifiers { get; set; }
            public Dictionary<string, CreatureModifierConfiguration> MinorModifiers { get; set; }
            public Dictionary<string, CreatureModifierConfiguration> BossModifiers { get; set; }
        }

        public class GlobalModifierSettings {
            public List<string> GlobalIgnorePrefabList = new List<string>();
        }

        [Serializable]
        public class CharacterCacheEntry
        {
            public int Level { get; set; }
            public bool ShouldDelete { get; set; } = false;
            public string CreatureNameLocalizable { get; set; } = null;
            public string RefCreatureName { get; set; } = null;
            public ColorDef Colorization { get; set; } = null;
            public Heightmap.Biome Biome { get; set; }
            public float SpawnRateModifier { get; set; } = 1f;
            public bool RunOnceDone { get; set; } = false;
            public Dictionary<string, ModifierType> CreatureModifiers { get; set; } = new Dictionary<string, ModifierType>();
            public Dictionary<string, ModifierType> ModifiersRequired { get; set; } = null;
            public List<string> ModifiersNotAllowed { get; set; } = null;
            public Dictionary<DamageType, float> DamageRecievedModifiers { get; set; } = new Dictionary<DamageType, float>() {
                { DamageType.Blunt, 1f },
                { DamageType.Pierce, 1f },
                { DamageType.Slash, 1f },
                { DamageType.Fire, 1f },
                { DamageType.Frost, 1f },
                { DamageType.Lightning, 1f },
                { DamageType.Poison, 1f },
                { DamageType.Spirit, 1f },
                { DamageType.Chop, 1f },
                { DamageType.Pickaxe, 1f },
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
                { CreaturePerLevelAttribute.HealthPerLevel, 0f },
                { CreaturePerLevelAttribute.SizePerLevel, 0f },
                { CreaturePerLevelAttribute.SpeedPerLevel, 0f },
                { CreaturePerLevelAttribute.AttackSpeedPerLevel, 0f },
            };
            public CreatureSpecificSetting creatureSettings { get; set; } = null;
            public Dictionary<DamageType, float> CreatureDamageBonus { get; set; } = new Dictionary<DamageType, float>() { };

            public string GetDamageBonusDescription()
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<DamageType, float> bonusD in CreatureDamageBonus)
                {
                    if (bonusD.Value > 0f) { sb.Append($"|{bonusD.Key}-{bonusD.Value}"); }
                }
                return sb.ToString();
            }
        }

        [DataContract]
        public class ExtendedCharacterDrop
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
            public bool UntamedOnlyDrop { get; set; } = false;
            public bool TamedOnlyDrop { get; set; } = false;
            public void ToCharacterDrop() {
                GameDrop = Drop.ToCharDrop();
            }
        }

        [DataContract]
        public class ExtendedObjectDrop {
            public Drop Drop { get; set; }
            public GameObject DropGo { get; private set; }
            public float AmountScaleFactor { get; set; } = 0f;
            [DefaultValue(0f)]
            public float ChanceScaleFactor { get; set; } = 0f;
            public bool UseChanceAsMultiplier { get; set; } = false;
            [DefaultValue(0)]
            public int MaxScaledAmount { get; set; } = 0;

            public void ResolveDropPrefab() {
                DropGo = PrefabManager.Instance.GetPrefab(Drop.Prefab);
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
            [DefaultValue(false)]
            public bool OnePerPlayer { get; set; } = false;
            [DefaultValue(true)]
            public bool LevelMultiplier { get; set; } = true;
            [DefaultValue(false)]
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

        [DataContract]
        [Serializable]
        public class ColorDef
        {
            public float hue { get; set; } = 0f;
            public float saturation { get; set; } = 0f;
            public float value { get; set; } = 0f;
            public bool IsEmissive { get; set; } = false;

            public ColorDef() { }
            public ColorDef(float hue = 0f, float saturation = 0f, float value = 0f, bool is_emissive = false)
            {
                this.hue = hue;
                this.saturation = saturation;
                this.value = value;
                this.IsEmissive = is_emissive;
            }

            public LevelEffects.LevelSetup toLevelEffect()
            {
                return new LevelEffects.LevelSetup()
                {
                    m_scale = 1f,
                    m_hue = hue,
                    m_saturation = saturation,
                    m_value = value,
                    m_setEmissiveColor = IsEmissive,
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
                return (List<String>)binFormatter.Deserialize(mStream);
            }

            protected override void SetValue(List<string> value)
            {
                var mStream = new MemoryStream();
                binFormatter.Serialize(mStream, value);
                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

        public class CreatureModifiersZNetProperty : ZNetProperty<Dictionary<string, ModifierType>>
        {
            BinaryFormatter binFormatter = new BinaryFormatter();
            public CreatureModifiersZNetProperty(string key, ZNetView zNetView, Dictionary<string, ModifierType> defaultValue) : base(key, zNetView, defaultValue)
            {
            }
            public override Dictionary<string, ModifierType> Get()
            {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return new Dictionary<string, ModifierType>(); }
                var mStream = new MemoryStream(stored);
                var deserializedDictionary = (Dictionary<int, ModifierType>)binFormatter.Deserialize(mStream);
                Dictionary<string, ModifierType> modifierNamesToTypes =  new Dictionary<string, ModifierType>();
                foreach (var kvp in deserializedDictionary) {
                    string key = CreatureModifiersData.ModifierNamesLookupTable.GetValue(kvp.Key);
                    if (key == null) { continue; }
                    modifierNamesToTypes.Add(key, kvp.Value);
                }
                return modifierNamesToTypes;
            }
            protected override void SetValue(Dictionary<string, ModifierType> value)
            {
                Dictionary<int, ModifierType> serializableModifiers = new Dictionary<int, ModifierType>();
                foreach (var kvp in value) {
                    int key = CreatureModifiersData.ModifierNamesLookupTable.GetValue(kvp.Key);
                    if (key == -1) { continue; }
                    serializableModifiers.Add(key, kvp.Value);
                }
                var mStream = new MemoryStream();
                binFormatter.Serialize(mStream, serializableModifiers);
                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

        public class ListIntZNetProperty : ZNetProperty<List<int>>
        {
            BinaryFormatter binFormatter = new BinaryFormatter();
            public ListIntZNetProperty(string key, ZNetView zNetView, List<int> defaultValue) : base(key, zNetView, defaultValue)
            {
            }
            public override List<int> Get()
            {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return new List<int>(); }
                MemoryStream mStream = new MemoryStream(stored);
                return (List<int>)binFormatter.Deserialize(mStream);
            }
            protected override void SetValue(List<int> value)
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
                return (List<ModifierNames>)binFormatter.Deserialize(mStream);
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
                return (Dictionary<DamageType, float>)binFormatter.Deserialize(mStream);
            }

            protected override void SetValue(Dictionary<DamageType, float> value)
            {
                var mStream = new MemoryStream();
                binFormatter.Serialize(mStream, value);
                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

        public class CreatureDetailsZNetProperty : ZNetProperty<CharacterCacheEntry>
        {
            BinaryFormatter binFormatter = new BinaryFormatter();
            public CreatureDetailsZNetProperty(string key, ZNetView zNetView, CharacterCacheEntry defaultValue) : base(key, zNetView, defaultValue)
            {
            }

            public override CharacterCacheEntry Get()
            {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return new CharacterCacheEntry(); }
                MemoryStream mStream = new MemoryStream(stored);
                return (CharacterCacheEntry)binFormatter.Deserialize(mStream);
            }

            protected override void SetValue(CharacterCacheEntry value)
            {
                MemoryStream mStream = new MemoryStream();
                binFormatter.Serialize(mStream, value);

                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

    }
}
