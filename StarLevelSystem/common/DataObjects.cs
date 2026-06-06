using JetBrains.Annotations;
using Jotunn.Entities;
using Jotunn.Managers;
using MonoMod.Utils;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using StarLevelSystem.modules.LevelSystem;
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

        public static IDeserializer yamldeserializer = new DeserializerBuilder().WithCaseInsensitivePropertyMatching().Build();
        public static ISerializer yamlserializer = new SerializerBuilder().WithNamingConvention(PascalCaseNamingConvention.Instance).ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults).Build();

        //public static IDeserializer yamldeserializerMinified = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        public static ISerializer yamlserializerJsonCompat = new SerializerBuilder().WithNamingConvention(PascalCaseNamingConvention.Instance).JsonCompatible().Build();

        public static BinaryFormatter binFormatter = new BinaryFormatter();

        public static readonly string SLS_NAME = "SLS_NAME";
        public static readonly string SLS_DAMAGE_MODIFIER = "SLS_DMOD";
        public static readonly string SLS_DAMAGE_BONUSES = "SLS_DBON";
        public static readonly string SLS_SPAWN_MULT = "SLS_MULT";
        public static readonly string SLS_MODIFIERS = "SLS_MODS";
        public static readonly string SLS_MODSV2 = "SLS_MODV2";
        public static readonly string SLS_CHARNAME = "SLS_CHARNAME";
        public static readonly string SLS_TREE = "SLE_Tree";
        public static readonly string SLS_FISH = "SLE_Fish";
        public static readonly string SLS_BIRD = "SLE_Bird";
        public static readonly string SLS_INFERTILE = "SLS_Infertile";
        public static readonly string SLS_SOULEATER = "SLS_SoulEater";
        public static readonly string SLS_EVOLVE = "SLS_Evolve";
        public static readonly string SLS_SIZE = "SLS_SIZE";
        public static readonly string SLS_NEMESIS_SCORE = "SLS_NEM_SCORE";
        public static readonly string SLS_NEMESIS_SCOREDATA = "SLS_NEM_SCOREDATA";
        public static readonly string SLS_RAIDS_ACTIVE = "SLS_RAIDS_ACTIVE";

        public static readonly string SLS_MOD_CAP = "EffectCap";

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
            Destructible,
            None,
            Item
        }

        public enum LootFactorType {
            PerLevel,
            Exponential,
            ChancePerLevel
        }

        public enum DamageEstimateType {
            Average,
            Highest,
            Lowest
        }

        public enum AI {
            HuntPlayer,
            Alerted,
            AgitatedByBuild
        }

        public enum Music {
            Zrespawn,
            Zintro,
            Zmenu,
            Zcombat,
            ZCombatEventL1,
            ZCombatEventL2,
            ZCombatEventL3,
            ZCombatEventL4,
            Zboss_eikthyr,
            Zboss_gdking,
            Zboss_bonemass,
            Zboss_moder,
            Zboss_goblinking,
            Zboss_queen,
            Zboss_queen_ambience,
            Zboss_fader,
            Zmorning,
            Zevening,
            Zsailing,
            Zsailing_ashlands,
            Zblackforest,
            Zmeadows,
            Zswamp,
            Zmountain,
            Zplains,
            Zplainstower,
            Zmistlands,
            Zashlands,
            Zforestcrypt,
            Zforestcrypthildir,
            Zfrostcaves,
            Zfrostcaveshildir,
            Zhome,
            Zlocation_forest,
            Zlocation_haldor,
            Zlocation_dvergrtower,
            Zlocation_dvergrexc,
            Zlocation_ashlands_ruins
        }

        public enum Environment {
            Clear,
            Misty,
            Darklands_dark,
            DeepForest_Mist,
            Heath_clear,
            InfectedMine,
            GDKing,
            Rain,
            LightRain,
            ThunderStorm,
            Eikthyr,
            Fader,
            GoblinKing,
            nofogts,
            SwampRain,
            Bonemass,
            Snow,
            SnowStorm,
            Twilight_Clear,
            Twilight_Snow,
            Twilight_SnowStorm,
            Moder,
            Crypt,
            CryptHildir,
            Ghosts,
            Queen,
            SunkenCrypt,
            Mistlands_clear,
            Mistlands_rain,
            Mistlands_thunder,
            Ashlands_ashrain,
            Ashlands_ashrain_clear,
            Ashlands_CinderRain,
            Ashlands_meteorshower,
            Ashlands_misty,
            Ashlands_SeaStorm,
            Ashlands_storm,
            Caves,
            CavesHildir,
        }

        public enum NemesisAction {
            ChangeLevel,
            AddModifier,
            RemoveModifier,
            Spawn,
            SpawnMiniboss
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

        public class LevelGenerator {
            public string PrefabName { get; set; }
            [DefaultValue(1)]
            public int MaxLevel { get; set; }
            [DefaultValue(1)]
            public int MinLevel { get; set; }
            [DefaultValue(0f)]
            public float LevelUpChance { get; set; }
            [DefaultValue(1f)]
            public float NightMultiplier { get; set; }
            [DefaultValue(false)]

            public SortedDictionary<int, float> GetLevelUpDefinition() {
                int cLevel = MaxLevel - MinLevel;
                SortedDictionary<int, float> LevelupChances = new SortedDictionary<int, float>();
                // Fallthrough for invalid or Min=Max Level
                if (cLevel >= 0) {
                    LevelupChances.Add(MinLevel, LevelUpChance);
                    return LevelupChances;
                }

                cLevel += MinLevel;
                float levelupChance = LevelUpChance;
                while (cLevel <= MaxLevel) {
                    LevelupChances.Add(cLevel, levelupChance);
                    levelupChance *= LevelUpChance;
                }
                return LevelupChances;
            }

            public int RollAndDetermineLevel() {
                float levelup_roll = UnityEngine.Random.Range(0f, 100f);
                return LevelSelection.DetermineLevelRollResult(levelup_roll, MaxLevel, GetLevelUpDefinition(), new SortedDictionary<int, float>(), 1f, NightMultiplier);
            }
        }

        [Description("Controls overhaul creature levels")]
        public class CreatureLevelSettings {
            [Description("Controls biome specific configuration, the 'All' biome can be used to set the default for everything.")]
            public Dictionary<Heightmap.Biome, BiomeSpecificSetting> BiomeConfiguration { get; set; }

            [Description("Creature specific configuration.")]
            public Dictionary<string, CreatureSpecificSetting> CreatureConfiguration { get; set; }

            [Description("Levelup chance for all creatures, this is modified by distance level bonuses and can be overridden by biome-specific settings or creature specific settings.")]
            public SortedDictionary<int, float> DefaultCreatureLevelUpChance { get; set; }

            [Description("Globally disables the distance scaling system.")]
            public bool EnableDistanceLevelBonus { get; set; } = false;

            [Description("Distance scaling system, each entry is a distance threshold and its corresponding level bonus. These are added to DefaultCreatureLevelUpChance, when a distance bucket is selected.")]
            public SortedDictionary<int, SortedDictionary<int, float>> DistanceLevelBonus { get; set; }
        }

        [Description("Controls Night-time specific settings")]
        public class NightSettings {
            [Description("Modifies the spawn rate of creatures during the night time 1.0 = no change, 2.0 = 2x spawns, 0.5 = 50% reduced spawns.")]
            [DefaultValue(1f)]
            public float SpawnRateModifier { get; set; } = 1f;

            [Description("A level up chance scalar that is only applied at night. 1.0 = no change. 2.0 = all levels are 2x more likely (typically mostly impacts higher levels)")]
            [DefaultValue(1f)]
            public float NightLevelUpChanceScaler { get; set; } = 1f;

            [Description("Disables this creatures spawn during the night time.")]
            [DefaultValue(false)]
            public bool CreatureSpawnsDisabled { get; set; } = false;
        }

        [Description("Controls biome-specific Night-time specific settings")]
        public class BiomeNightSettings {
            [Description("Modifies the spawn rate of creatures during the night time 1.0 = no change, 2.0 = 2x spawns, 0.5 = 50% reduced spawns.")]
            [DefaultValue(1f)]
            public float SpawnRateModifier { get; set; } = 1f;

            [Description("A level up chance scalar that is only applied at night. 1.0 = no change. 2.0 = all levels are 2x more likely (typically mostly impacts higher levels)")]
            [DefaultValue(1f)]
            public float NightLevelUpChanceScaler { get; set; } = 1f;

            [Description("Disables this creatures spawn during the night time.")]
            public List<string> CreatureSpawnsDisabled { get; set; } = new List<string>();
        }

        [Description("Biome specific settings.")]
        public class BiomeSpecificSetting {
            [Description("Custom creature levelup chances that will replace default chances for any creature spawned in this biome.")]
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }

            [Description("Minimum level override for creatures in this biome.")]
            [DefaultValue(0)]
            public int BiomeMinLevelOverride { get; set; }

            [Description("Maximum level override for creatures in this biome.")]
            public int BiomeMaxLevelOverride { get; set; }

            [Description("How strong distance effects are in this biome. 1.0 = no change, 2.0 = 2x stronger, 0.5 = 50% weaker.")]
            [DefaultValue(1f)]
            public float DistanceScaleModifier { get; set; } = 1f;

            [Description("Spawn rate modifier for creatures in this biome. 1.0 = no change, 2.0 = 2x spawns, 0.5 = 50% reduced spawns.")]
            [DefaultValue(1f)]
            public float SpawnRateModifier { get; set; } = 1f;

            [Description("Creature base value modifiers for all creatures spawned in this biome.")]
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }

            [Description("Creature per-level value modifiers for all creatures spawned in this biome.")]
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }

            [Description("Damage type and modifiers for all creatures spawned in this biome. This can be used to make creatures weak to, or immune to, certain damage types.")]
            public Dictionary<DamageType, float> DamageRecievedModifiers { get; set; }

            [Description("List of creature spawns which are disabled in this biome.")]
            public List<string> CreatureSpawnsDisabled { get; set; }

            [Description("Night-time specific settings for this biome.")]
            public BiomeNightSettings NightSettings { get; set; }
        }

        [Description("Creature-specific settings.")]
        public class CreatureSpecificSetting {
            [Description("How strong distance effects are for this creature. 1.0 = no change, 2.0 = 2x stronger, 0.5 = 50% weaker.")]
            [DefaultValue(1f)]
            public float DistanceScaleModifier { get; set; } = 1f;

            [Description("Custom creature levelup chances that will replace default chances for this creature.")]
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; }

            [Description("Creature specific minimum level.")]
            [DefaultValue(-1)]
            public int CreatureMinLevelOverride { get; set; } = -1;

            [Description("Creature specific maximum level.")]
            [DefaultValue(-1)]
            public int CreatureMaxLevelOverride { get; set; } = -1;

            [Description("Creature specific limit to the number of major modifiers.")]
            [DefaultValue(-1)]
            public int MaxMajorModifiers { get; set; } = -1;

            [Description("Creature specific chance for major modifiers.")]
            [DefaultValue(-1f)]
            public float ChanceForMajorModifier { get; set; } = -1f;

            [Description("Creature specific limit to the number of minor modifiers.")]
            [DefaultValue(-1)]
            public int MaxMinorModifiers { get; set; } = -1;

            [Description("Creature specific chance for minor modifiers.")]
            [DefaultValue(-1f)]
            public float ChanceForMinorModifier { get; set; } = -1f;

            [Description("Creature specific limit to the number of boss modifiers.")]
            [DefaultValue(-1)]
            public int MaxBossModifiers { get; set; } = -1;

            [Description("Creature specific chance for boss modifiers.")]
            [DefaultValue(-1f)]
            public float ChanceForBossModifier { get; set; } = -1f;

            [Description("Modifiers that this creature will always spawn with.")]
            public Dictionary<string, ModifierType> RequiredModifiers { get; set; }

            [Description("Spawn rate modifier for this creature. 1.0 = no change, 2.0 = 2x spawns, 0.5 = 50% reduced spawns.")]
            public float SpawnRateModifier { get; set; } = 1f;

            [Description("Night-time specific settings for this creature.")]
            public NightSettings NightSettings { get; set; }

            [Description("Base value modifiers for this creature.")]
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }

            [Description("Per-level value modifiers for this creature.")]
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }

            [Description("Damage received modifiers for this creature.")]
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

        public class LootEntry {
            public int Amount { get; set; }
            public GameObject Prefab { get; set; }
            public int MaxAmountPerDrop { get; set; } = 1;
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

            // TODO: Add fallbacks to load prefabs that are not in the embedded resource bundle
            public void LoadAndSetGameObjects() {
                if (FromAPI) {
                    LoadAPIGameObjects();
                    return;
                }
                if (StarVisual != null && !CreatureModifiersData.LoadedModifierSprites.ContainsKey(StarVisual)) {
                    string path = $"assets/custom/starlevels/icons/{StarVisual}.png";
                    if (ValConfig.UseStarShapedModifierIcons.Value) { path = $"assets/custom/starlevels/icons2/{StarVisual}.png"; }
                    Sprite game_obj = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Sprite>(path);
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
            public Dictionary<string, float> Config { get; set; }
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

        public class PlayerRaidData {
            public RaidDefinition ActiveRaid { get; set; } = null;
            public List<string> PlayerPrivatekeys { get; set; } = new List<string>();
            public List<RaidDefinition> PlayerAvailableRaids { get; set; } = new List<RaidDefinition>();
            public double NextRaidableTime { get; set; } = 0f;
            public Vector3 CurrentRaidPosition { get; set; }
            public Dictionary<string, double> LastRaidByName { get; set; } = new Dictionary<string, double>();
        }

        public class PlayerPrivatekeys {
            public List<string> PrivateKeys { get; set; } = new List<string>();
            public double LastUpdatedAt { get; set; } = 0f;
        }

        public class PlayerRaidHistory {
            public double NextRaidableTime { get; set; }
            public Dictionary<string, double> LastRaidByName { get; set; }
        }

        public class ActiveRaid {
            public Vector3 Position { get; set; }
            public RaidDefinition Definition { get; set; }
            public float StartTime { get; set; }
            public ZNetPeer TargetPlayer { get; set; }
        }

        public class RaidConfiguration {
            public GlobalRaidSettings GlobalSettings { get; set; } = new GlobalRaidSettings();
            public List<RaidDefinition> Raids { get; set; } = new List<RaidDefinition>();
        }

        public class GlobalRaidSettings {
            [DefaultValue(false)]
            public bool DisableAllRaids { get; set; } = false;
            [DefaultValue(true)]
            public bool PlayerBasedRaids { get; set; } = true;
            [DefaultValue(1f)]
            public float GlobalRaidIntervalScalar { get; set; } = 1f;
            [DefaultValue(1f)]
            public float GlobalRaidChanceScalar { get; set; } = 1f;
        }

        public class NetworkRaidRequest {
            public Vector3 RaidPostion { get; set; } = Vector3.zero;
            public RaidDefinition Raid { get; set; }
        }

        [Serializable]
        public class RaidDefinition {
            public string Name { get; set; }
            [DefaultValue(true)]
            public bool Enabled { get; set; } = true;
            [DefaultValue(60f)]
            public float Duration { get; set; } = 60f;
            [DefaultValue(true)]
            public bool RaidActiveTillDefeated { get; set; } = true;
            [DefaultValue(12)]
            public int SpawnPoints { get; set; } = 12;
            public float RaidCoolDownMinutes { get; set; } = 120f;
            public RaidActivation Activation { get; set; } = new RaidActivation();
            public List<RaidSpawnEntry> Spawns { get; set; } = new List<RaidSpawnEntry>();
            [DefaultValue(96f)]
            public float EventRange { get; set; } = 96f;
            [DefaultValue("")]
            public string StartMessage { get; set; } = "";
            [DefaultValue("")]
            public string EndMessage { get; set; } = "";
            [DefaultValue(Environment.Clear)]
            public Environment ForceEnvironment { get; set; } = Environment.Clear;
            [DefaultValue(Music.Zcombat)]
            public Music ForceMusic { get; set; } = Music.Zcombat;

            public RandomEvent ToRaid(Vector3 position) {
               RandomEvent raid = new RandomEvent();
                raid.m_name = Name;
                raid.m_duration = Duration;
                if (Activation != null) {
                    if (Activation.RequiredGlobalKeys != null) {
                        raid.m_requiredGlobalKeys = Activation.RequiredGlobalKeys;
                    }
                    if (Activation.NotRequiredGlobalKeys != null) {
                        raid.m_notRequiredGlobalKeys = Activation.NotRequiredGlobalKeys;
                    }
                    if (Activation.RequiredPlayerKeys != null) {
                        raid.m_altRequiredPlayerKeysAll = Activation.RequiredPlayerKeys;
                    }
                    if (Activation.NotRequiredPlayerKeys != null) {
                        raid.m_altNotRequiredPlayerKeys = Activation.NotRequiredPlayerKeys;
                    }
                    if (Activation.AnyRequiredPlayerKeys != null) {
                        raid.m_altRequiredPlayerKeysAny = Activation.AnyRequiredPlayerKeys;
                    }

                    raid.m_standaloneChance = Activation.Chance;
                    raid.m_standaloneInterval = 100f;
                    raid.m_pauseIfNoPlayerInArea = Activation.PauseIfNoPlayerInArea;
                    raid.m_nearBaseOnly = Activation.NearBaseOnly;
                }
                raid.m_spawnerDelay = 0f;
                raid.m_eventRange = EventRange;
                raid.m_startMessage = Localization.instance.Localize(StartMessage);
                raid.m_endMessage = Localization.instance.Localize(EndMessage);
                raid.m_forceEnvironment = ForceEnvironment.ToString();
                raid.m_biome = Heightmap.FindBiome(position);
                raid.m_forceMusic = ForceMusic.ToString();
                raid.m_random = true;
                raid.m_time = 0; // This is used to track event times
                raid.m_pos = position;

                return raid;
            }
        }

        [Serializable]
        public class RaidActivation {
            public List<Heightmap.Biome> Biomes { get; set; }
            [DefaultValue(true)]
            public bool NearBaseOnly { get; set; } = false;
            [DefaultValue(true)]
            public bool PauseIfNoPlayerInArea { get; set; } = true;
            [DefaultValue(100f)]
            public float Chance { get; set; } = 100f;
            public List<string> RequiredGlobalKeys { get; set; }
            public List<string> NotRequiredGlobalKeys { get; set; }
            public List<string> RequiredPlayerKeys { get; set; }
            public List<string> NotRequiredPlayerKeys { get; set; }
            public List<string> AnyRequiredPlayerKeys { get; set; }
        }

        [Serializable]
        public class RaidSpawnEntry {
            public string PrefabName { get; set; }
            public AI CreatureAI { get; set; } = AI.Alerted;
            [DefaultValue(10f)]
            public float SpawnInterval { get; set; } = 10f;
            [DefaultValue(100f)]
            public float SpawnChance { get; set; } = 100f;
            [DefaultValue(0f)]
            public float InitalSpawnDelay { get; set; } = 0f;
            [DefaultValue(0)]
            public int MaxSpawned { get; set; } = 0;
            [DefaultValue(0)]
            public int MaxSpawnTriggers { get; set; } = 0;
            [DefaultValue(1)]
            public int SpawnGroupSize { get; set; } = 1;
            [DefaultValue(Character.Faction.TrainingDummy)]
            public Character.Faction Faction { get; set; } = Character.Faction.TrainingDummy;
            [DefaultValue(1)]
            public int LevelMin { get; set; } = 1;
            public int LevelMax { get; set; } = ValConfig.MaxLevel.Value;
            public bool UseRaidLevelSystem { get; set; } = true;
            public Dictionary<string, ModifierType> RequiredModifiers { get; set; } = null;
            public List<string> ModifiersNotAllowed { get; set; } = null;
            [DefaultValue(null)]
            public SortedDictionary<int, float> CustomCreatureLevelUpChance { get; set; } = null;
        }

        [Serializable]
        public class RaidMonitor {
            public RaidSpawnEntry RaidSpawnDef { get; set; }
            public double NextSpawn { get; set; } = 0;
            [DefaultValue(0)]
            public int TriggerCount { get; set; } = 0;
            public List<string> SpawnedCreatures { get; set; } = new List<string>();

            public List<ZDOID> GetSpawnedZDOIDs() {
                List<ZDOID> connected = new List<ZDOID>();
                foreach(var creature in SpawnedCreatures) {
                    var parts = creature.Split(':');
                    connected.Add(new ZDOID(long.Parse(parts[0]), uint.Parse(parts[1])));
                }
                return connected;
            }

            public void StoreZDOIDS(List<ZDOID> connected) {
                SpawnedCreatures.Clear();
                foreach (var creature in connected) {
                    SpawnedCreatures.Add(creature.ToString());
                }
            }
        }

        public class NemesisConfiguration {
            public int NemesisVersion { get; set; }
            [DefaultValue(10f)]
            public float NemesisActionCooldownSeconds { get; set; } = 10f;
            [DefaultValue(300f)]
            public float NemesisInfluenceRadius { get; set; } = 300f;
            public float NemesisMinSpawnDistance { get; set; } = 20f;
            public bool CreateMinibossFromPlayerKiller { get; set; } = true;
            public bool CreationRemovesSourceCreature { get; set; } = true;
            public float NemesisBossChance { get; set; } = 0.1f;
            public float NemesisBossMaxLevelBonus { get; set; } = 0.40f;
            public float NemesisBossMinLevelBonus { get; set; } = 0.20f;

            public NemesisScore ScoreSystem { get; set; } = new NemesisScore();
            public NemesisGaurenteedChanges GaurenteedChanges { get; set; } = new NemesisGaurenteedChanges();
            public NemesisChanceChanges ChanceChanges { get; set; } = new NemesisChanceChanges();
            public List<NemesisMiniboss> AvailableMiniBosses { get; set; } = new List<NemesisMiniboss>();
            public Dictionary<Heightmap.Biome, List<NemesisMinion>> NemesisMinionTemplatesByBiome = new Dictionary<Heightmap.Biome, List<NemesisMinion>>();
        }

        public class NemesisMinion {
            public string PrefabName { get; set; }
            public int MinAmount { get; set; }
            public int MaxAmount { get; set; }
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }
        }

        public class NemesisChanceChanges {
           public Dictionary<string, NemesisChanceEntry> CreatureOps { get; set; } = new Dictionary<string, NemesisChanceEntry>();
        }

        public class NemesisChanceEntry {
            public List<string> RequiredGlobalKeys { get; set; }
            public List<string> NotRequiredGlobalKeys { get; set; }
            public List<string> RequiredPrivateKeys { get; set; }
            [DefaultValue(true)]
            public bool Enabled { get; set; } = true;
            [DefaultValue(0.5f)]
            public float Chance { get; set; } = 0.5f;
            [DefaultValue(0)]
            public int LevelBonus { get; set; } = 0;
            public List<Heightmap.Biome> DeniedBiomes { get; set; } = new List<Heightmap.Biome>() { Heightmap.Biome.None };
            public List<Heightmap.Biome> AllowedBiomes { get; set; } = new List<Heightmap.Biome>() { };
            [DefaultValue(0f)]
            public float ScoreThreshold { get; set; } = 0f;
            public NemesisAction Action { get; set; } = NemesisAction.ChangeLevel;
            [DefaultValue(0f)]
            public float ScoreChange { get; set; } = 0f;
            public float ExtraCooldownSeconds { get; set; } = 0f;
            public List<NemesisSpawn> SpawnConfig { get; set; }
            public NemesisPlayerStateRequirements PlayerReqs { get; set; }
        }

        public class NemesisPlayerStateRequirements {
            public Heightmap.Biome PlayerCurrentBiome { get; set; }
        }

        public class NemesisSpawn {
            public string Prefab { get; set; }
            public AI CreatureAI { get; set; } = AI.HuntPlayer;
            [DefaultValue(0)]
            public int ForcedLevel { get; set; } = 0;
            [DefaultValue(false)]
            public bool IsBoss { get; set; } = false;
            public int SpawnGroupSize { get; set; } = 1;
            [DefaultValue("")]
            public string CustomName { get; set; } = "";
            [DefaultValue(Character.Faction.TrainingDummy)]
            public Character.Faction Faction { get; set; } = Character.Faction.TrainingDummy;
            [DefaultValue(null)]
            public Dictionary<string, ModifierType> RequiredModifiers { get; set; } = null;
            public Dictionary<CreatureBaseAttribute, float> CreatureBaseValueModifiers { get; set; }
            public Dictionary<CreaturePerLevelAttribute, float> CreaturePerLevelValueModifiers { get; set; }
        }

        public class NemesisMiniboss {
            public bool BossCreatedFromKillingPlayer { get; set; }
            public string KilledPlayerName { get; set; }
            public NemesisSpawn BossSpawn { get; set; }
            public List<NemesisSpawn> Minions { get; set; }
            public Heightmap.Biome Biome { get; set; }
        }

        public class NemesisGaurenteedChanges {
            public bool FirstBossSetLevel { get; set; } = true;
            public int FirstBossLevel { get; set; } = 0;
        }

        public class NemesisScore {
            [DefaultValue(0f)]
            public float NeutralScore { get; set; } = 600f;
            [DefaultValue(0f)]
            public float MinScore { get; set; } = 0f;
            [DefaultValue(0f)]
            public float MaxScore { get; set; } = 1000f;
            [DefaultValue(500f)]
            public float DeathScoreReduction { get; set; } = 500f;
            [DefaultValue(30f)]
            public float DecayPerUpdate { get; set; } = 30f;
            [DefaultValue(30f)]
            public float ScoreIntervalSeconds { get; set; } = 30f;
            [DefaultValue(25f)]
            public float NearbyPlayerRadius { get; set; } = 25f;
            [DefaultValue(0.05f)]
            public float NearbyAveragingWeight { get; set; } = 0.05f;
            [DefaultValue(0.5f)]
            public float MeleeDamageDealtFactor { get; set; } = 0.5f;
            [DefaultValue(0.25f)]
            public float RangedDamageDealtFactor { get; set; } = 0.25f;
            [DefaultValue(0.3f)]
            public float MagicDamageDealtFactor { get; set; } = 0.3f;
            [DefaultValue(1f)]
            public float DamageTakenFactor { get; set; } = 1f;
            [DefaultValue(250f)]
            public float BossKillBonus { get; set; } = 250f;
            [DefaultValue(100f)]
            public float BossKillRadius { get; set; } = 100f;
        }

        [Serializable]
        public class ScoreData {
            public float DamageDealtMelee { get; set; } = 0f;
            public float DamageDealtRanged { get; set; } = 0f;
            public float DamageDealtMagic { get; set; } = 0f;
            public float DamageTaken { get; set; } = 0f;
            public int BossKills { get; set; } = 0;
            public Dictionary<string, int> BossKillsHistory { get; set; } = new Dictionary<string, int>();
            public double LastDeath { get; set; } = 0f;
            public List<DamageScoreData> DamageScoreHistory { get; set; } = new List<DamageScoreData>();
        }

        [Serializable]
        public class DamageScoreData {
            public float DamageDealtMelee { get; set; } = 0f;
            public float DamageDealtRanged { get; set; } = 0f;
            public float DamageDealtMagic { get; set; } = 0f;
            public float DamageTaken { get; set; } = 0f;
            public int BossKills { get; set; } = 0;
        }

        [Serializable]
        public class CharacterCacheEntry
        {
            public int Level { get; set; }
            public ZDO ZDO { get; set; } = null;
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

        public class RaidZNetProperty : ZNetProperty<RaidDefinition> {
            public RaidZNetProperty(string key, ZNetView zNetView, RaidDefinition defaultValue) : base(key, zNetView, defaultValue) {
            }

            public override RaidDefinition Get() {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return null; }
                MemoryStream mStream = new MemoryStream(stored);
                return (RaidDefinition)binFormatter.Deserialize(mStream);
            }

            protected override void SetValue(RaidDefinition value) {
                MemoryStream mStream = new MemoryStream();
                binFormatter.Serialize(mStream, value);

                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

        public class IntZNetProperty : ZNetProperty<int> {
            public IntZNetProperty(string key, ZNetView zNetView, int defaultValue) : base(key, zNetView, defaultValue) {
            }

            public override int Get() {
                return zNetView.GetZDO().GetInt(Key, DefaultValue);
            }

            protected override void SetValue(int value) {
                zNetView.GetZDO().Set(Key, value);
            }
        }

        public class DoubleZNetProperty : ZNetProperty<double> {
            public DoubleZNetProperty(string key, ZNetView zNetView, double defaultValue) : base(key, zNetView, defaultValue) {
            }

            public override double Get() {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return 0; }
                MemoryStream mStream = new MemoryStream(stored);
                return (double)binFormatter.Deserialize(mStream);
            }

            protected override void SetValue(double value) {
                MemoryStream mStream = new MemoryStream();
                binFormatter.Serialize(mStream, value);

                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

        public class RaidMonitorListZNetProperty : ZNetProperty<List<RaidMonitor>> {
            public RaidMonitorListZNetProperty(string key, ZNetView zNetView, List<RaidMonitor> defaultValue) : base(key, zNetView, defaultValue) {
            }

            public override List<RaidMonitor> Get() {
                var stored = zNetView.GetZDO().GetByteArray(Key);
                // we can't deserialize a null buffer
                if (stored == null) { return new List<RaidMonitor>(); }
                MemoryStream mStream = new MemoryStream(stored);
                return (List<RaidMonitor>)binFormatter.Deserialize(mStream);
            }

            protected override void SetValue(List<RaidMonitor> value) {
                MemoryStream mStream = new MemoryStream();
                binFormatter.Serialize(mStream, value);

                zNetView.GetZDO().Set(Key, mStream.ToArray());
            }
        }

        public class ListVectorZNetProperty : ZNetProperty<List<Vector3>> {
            public ListVectorZNetProperty(string key, ZNetView zNetView, List<Vector3> defaultValue)
                : base(key, zNetView, defaultValue) {
            }

            public override List<Vector3> Get() {
                byte[] bytes = zNetView.GetZDO().GetByteArray(Key);
                if (bytes is null) { return null; }
                
                List<Vector3> result = new List<Vector3>();

                int length = bytes.Length / 12;

                for (int i = 0; i < length; ++i) {
                    result.Add(new Vector3(
                        BitConverter.ToSingle(bytes, i * 12 + 0), 
                        BitConverter.ToSingle(bytes, i * 12 + 4), 
                        BitConverter.ToSingle(bytes, i * 12 + 8)
                        ));
                }
                return result;
            }

            protected override void SetValue(List<Vector3> value) {
                byte[] bytes = new byte[value.Count * 12];

                for (int i = 0; i < value.Count; ++i) {
                    BitConverter.GetBytes(value[i].x).CopyTo(bytes, i * 12 + 0);
                    BitConverter.GetBytes(value[i].y).CopyTo(bytes, i * 12 + 4);
                    BitConverter.GetBytes(value[i].z).CopyTo(bytes, i * 12 + 8);
                }

                zNetView.GetZDO().Set(Key, bytes);
            }
        }

        public class BoolZNetProperty : ZNetProperty<bool> {
            public BoolZNetProperty(string key, ZNetView zNetView, bool defaultValue) : base(key, zNetView, defaultValue) {
            }

            public override bool Get() {
                return zNetView.GetZDO().GetBool(Key, DefaultValue);
            }

            protected override void SetValue(bool value) {
                zNetView.GetZDO().Set(Key, value);
            }
        }

    }
}
