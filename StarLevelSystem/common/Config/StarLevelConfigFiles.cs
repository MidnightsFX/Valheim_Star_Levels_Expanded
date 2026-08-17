using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common {

    // Star Level System's yaml config files. Loading, watching, validating and syncing them is handled by
    // Common/Config; this is the whole of the mod-specific half.
    //
    // RpcName is set explicitly on every file to the channel names this mod already shipped. The framework
    // would otherwise derive PluginName + "_" + file name, which is a different string on the wire -- and
    // while VersionStrictness.Patch would refuse a mismatched connection anyway, there is no reason to
    // change a working wire format during a refactor this size.
    internal static partial class YamlConfigManager {

        internal static YamlConfigFile<CreatureLevelSettings> LevelSettings;
        internal static YamlConfigFile<CreatureColorizationSettings> ColorSettings;
        internal static YamlConfigFile<LootSettings> LootSettingsFile;
        internal static YamlConfigFile<CreatureModifierCollection> ModifierSettings;
        internal static YamlConfigFile<RaidConfiguration> RaidSettings;
        internal static YamlConfigFile<NemesisConfiguration> NemesisSettings;
        internal static YamlConfigFile<LocationResetConfiguration> LocationResetSettings;

        private static void RegisterConfigFiles() {
            // The ProtectionRule shorthand converter has to be in place before anything parses. It is the
            // only reason `PlayerBuiltPiece: Block` still works alongside the expanded mapping form.
            YamlFormat.AddTypeConverter(new ProtectionRuleYamlConverter());

            LevelSettings = Register(new YamlConfigFile<CreatureLevelSettings>(ValConfig.LevelSettingsFileName) {
                RpcName = "SLS_LevelsRPC",
                Header = LevelSettingsHeader,
                Defaults = () => LevelSystemData.DefaultConfiguration,
                Apply = LevelSystemData.ApplyLoaded,
                AllowAdminEdit = true,
            });

            ColorSettings = Register(new YamlConfigFile<CreatureColorizationSettings>(ValConfig.ColorSettingsFileName) {
                RpcName = "SLS_ColorsRPC",
                Header = ColorSettingsHeader,
                Defaults = () => Colorization.defaultColorizationSettings,
                Apply = Colorization.ApplyLoaded,
                // Colours are cosmetic and the merge-in of missing default keys makes a partial file
                // workable, so a broken edit reverting to built-ins matches how this behaved before.
                OnFailure = ConfigFailurePolicy.RevertToDefaults,
                AllowAdminEdit = true,
            });

            LootSettingsFile = Register(new YamlConfigFile<LootSettings>(ValConfig.LootSettingsFileName) {
                RpcName = "SLS_CreatureLootRPC",
                Header = LootSettingsHeader,
                Defaults = () => LootSystemData.DefaultDropConfiguration,
                Apply = LootSystemData.ApplyLoaded,
                NeedsPrefabs = true,
                AllowAdminEdit = true,
            });

            ModifierSettings = Register(new YamlConfigFile<CreatureModifierCollection>(ValConfig.ModifiersFileName) {
                RpcName = "SLS_ModifiersRPC",
                Header = ModifiersHeader,
                Defaults = () => CreatureModifiersData.DefaultModifiers,
                Apply = CreatureModifiersData.ApplyLoaded,
                Validate = CreatureModifiersData.ValidateModifiers,
                OnFailure = ConfigFailurePolicy.RevertToDefaults,
                NeedsPrefabs = true,
                AllowAdminEdit = true,
            });

            RaidSettings = Register(new YamlConfigFile<RaidConfiguration>(ValConfig.RaidSettingsFileName) {
                RpcName = "SLS_RaidsRPC",
                Header = RaidSettingsHeader,
                Defaults = () => RaidsData.DefaultConfiguration,
                Apply = RaidsData.ApplyLoaded,
                OnFailure = ConfigFailurePolicy.RevertToDefaults,
                AllowAdminEdit = true,
            });

            NemesisSettings = Register(new YamlConfigFile<NemesisConfiguration>(ValConfig.NemesisSettingsFileName) {
                RpcName = "SLS_NemesisRPC",
                Header = NemesisSettingsHeader,
                Defaults = () => NemesisSystemData.DefaultConfiguration,
                Apply = NemesisSystemData.ApplyLoaded,
                // The version check used to be hand-rolled inside the load path, where it wrote with a bare
                // File.WriteAllText -- losing the header -- and, because that same method is the client's
                // receive path, made a CLIENT overwrite its own file when the server sent a mismatched
                // version. The framework only rewrites on the machine that owns the file.
                SchemaVersion = NemesisSystemData.DefaultConfiguration.NemesisVersion,
                GetSchemaVersion = NemesisSystemData.GetSchemaVersion,
                SetSchemaVersion = NemesisSystemData.SetSchemaVersion,
                Migrate = NemesisSystemData.MigrateToCurrent,
                AllowAdminEdit = true,
            });

            LocationResetSettings = Register(new YamlConfigFile<LocationResetConfiguration>(ValConfig.LocationResetSettingsFileName) {
                RpcName = "SLS_LocationResetRPC",
                Header = LocationResetHeader,
                Defaults = () => LocationResetData.BuildDefaultConfig(),
                Apply = LocationResetData.ApplyLoaded,
                NeedsPrefabs = true,
                AllowAdminEdit = true,
            });

            // Prefab names cannot be resolved during Awake, so anything whose validator or apply reaches
            // the prefab table gets a second pass once it exists.
            PrefabManager.OnPrefabsRegistered += () => RevalidateAll();

            // SavedData files -- ZoneData.yaml, ServerRaidSavedData.yaml, NemesisRemoteState.yaml,
            // LocationResetCatalog.yaml and the binary LocationResetState.dat -- deliberately stay outside
            // this framework. They are world state, written by the game rather than by a human, and giving
            // them sync channels or a watcher would be all risk and no benefit.
        }

        private const string LevelSettingsHeader = @"#################################################
# Star Level System Expanded - Level Settings
#
# Controls how creature levels roll and how stats scale with them: the base
# levelup chances, per-biome and per-creature overrides, night settings,
# distance-from-center bonuses, and reusable level generators.
#
# This file is SERVER AUTHORITATIVE: the server's copy is synced to every
# client, so edit it on the server (or hosting player). Edits apply live.
# Levels vs stars: level 1 has no stars, level 2 = 1 star, level 3 = 2 stars.
# The MaxLevel / MaxBossLevel settings in the main .cfg cap everything here.
#
# --- Levelup chances ---
# Every chance table is 'level: chance', the percent chance for a creature to
# reach that level. Values must DECREASE as the level rises. A calculator for
# building spreads lives at https://sls-levelspreadtool.netlify.app/
#
#   DefaultCreatureLevelUpChance:
#     1: 20
#     2: 10
#     3: 5
#
# --- BiomeConfiguration ---
# Per-biome overrides. The special 'All' biome is the baseline for every biome;
# a specific biome's values win where they are set. Example:
#
#   BiomeConfiguration:
#     All:
#       SpawnRateModifier: 1.1          # 1.0 = no change, 2.0 = 2x spawns
#       DistanceScaleModifier: 1.5      # how strongly distance bonuses apply here
#       DamageReceivedModifiers:        # make everything weak to poison
#         Poison: 1.5
#       NightSettings:
#         NightLevelUpChanceScaler: 1.5 # higher levels more likely at night
#     Meadows:
#       BiomeMaxLevelOverride: 4        # hard level cap inside this biome
#       CreatureSpawnsDisabled: [ Troll ]
#
# --- CreatureConfiguration ---
# Per-prefab overrides; these beat the biome values for that creature. Prefab
# names must match exactly (find them with VNEI or the Jotunn docs).
#
#   CreatureConfiguration:
#     Troll:
#       CreatureMaxLevelOverride: 11
#       CustomCreatureLevelUpChance: { 1: 100, 2: 50, 3: 5 }
#       CreatureBaseValueModifiers:     # 1.0 = vanilla for base values
#         BaseHealth: 1.05
#       CreaturePerLevelValueModifiers: # added per level above 1 (0.05 = +5%/level)
#         SizePerLevel: 0.05
#       RequiredModifiers: { Fire: Major }
#
# --- DistanceLevelBonus ---
# Extra levelup chance by distance (metres) from the world center or starter
# temple (see DistanceBonusIsFromStarterTemple in the main .cfg). Each band's
# table is ADDED to the base chances once a spawn is past that distance;
# EnableDistanceLevelBonus turns the whole system on/off.
#
#   DistanceLevelBonus:
#     1250: { 1: 25, 2: 15 }
#     5000: { 1: 45, 2: 25, 3: 10 }
#
# --- Level generators ---
# Instead of hand-writing chance tables, generators expand a Min/Max level plus
# a curve style (Linear, Exponential, Gaussian) into a table at load. Name them
# in CustomLevelupGenerators, then reference them anywhere a
# LevelupGeneratorRefs list exists (defaults, biomes, creatures, raids,
# nemesis spawns). When present, the generated curve REPLACES that section's
# chance table.
#
#   CustomLevelupGenerators:
#     late_game:
#     - MinLevel: 1
#       MaxLevel: 25
#       LevelUpChance: 0.35             # authored as a 0-1 fraction
#       LevelupCalculationStyle: Gaussian
#       GaussianOffset: 0.25
#   DefaultLevelupGeneratorRefs: [ late_game ]
#
# ConditionalCreatureLevelupChance switches biome curves as world bosses fall:
# defeated-boss global key -> biome -> generator. The highest defeated tier
# listed applies; 'All' inside an entry is that entry's fallback biome.
#################################################";

        private const string ColorSettingsHeader = @"#################################################
# Star Level System Expanded - Creature Level Color Settings
#
# Tints creatures by their star count so higher-star creatures are readable at
# a glance. Needs EnableColorization in the main .cfg. Server authoritative;
# edits apply live.
#
# Every table here is keyed by STAR COUNT: entry 1 is a 1-star creature
# (level 2), entry 2 is 2 stars, and so on. A star with no entry keeps the
# creature's normal look.
#
# A color entry is a set of SHIFTS applied to the creature's material, each
# ranging -1 to 1 with 0 meaning unchanged:
#   Hue:        rotates the color (-1/1 = full wrap, small values shift tone)
#   Saturation: negative washes color out, positive deepens it
#   Value:      negative darkens, positive brightens
#   IsEmissive: true adds a glow of the resulting color
#
# --- DefaultLevelColorization ---
# The fallback tint per star for every creature without its own entry:
#
#   DefaultLevelColorization:
#     1: { Hue: 0,     Saturation: 0,    Value: -0.05 }
#     2: { Hue: 0.05,  Saturation: 0.1,  Value: -0.1 }
#     3: { Hue: -0.2,  Saturation: 0.2,  Value: -0.1, IsEmissive: true }
#
# --- CharacterSpecificColorization ---
# Per-prefab tints that beat the defaults. Prefab names must match exactly.
#
#   CharacterSpecificColorization:
#     Boar:
#       1: { Hue: 0,    Saturation: 0, Value: -0.05 }
#       2: { Hue: 0.1,  Saturation: 0, Value: -0.05 }
#
# --- CharacterColorGenerators ---
# Builds a per-star ramp automatically by blending between two colors across a
# star range, so long star spreads do not need hand-written entries:
#
#   CharacterColorGenerators:
#     Greydwarf:
#     - RangeStart: 1
#       RangeEnd: 15
#       StartColorDef: { Hue: 0,    Saturation: 0,   Value: 0 }
#       EndColorDef:   { Hue: -0.8, Saturation: 0.3, Value: -0.1 }
#       CharacterSpecific: true     # write into this creature's own table
#       OverwriteExisting: false    # keep any hand-written entries in the range
#
# A partial file is fine: any star the file does not define falls back to the
# built-in defaults, and a file that fails to parse reverts to them entirely.
#################################################";

        private const string LootSettingsHeader = @"#################################################
# Star Level System Expanded - Creature loot configuration
#
# Custom drop tables for creatures and world objects, with level-aware scaling
# that replaces vanilla's doubling-per-star. Server authoritative; edits apply
# live. How amounts grow with level is picked by LootDropCalculationType in the
# main .cfg: PerLevel (linear), Exponential, or ChancePerLevel (base amount as
# an all-or-nothing roll whose chance grows with level).
#
# The console command sls-loot-dump writes the fully resolved loot tables for
# this world out as a reference.
#
# --- CharacterSpecificLoot ---
# Keyed by creature prefab name. An entry REPLACES that creature's vanilla
# drops entirely - list everything it should drop. Creatures without an entry
# keep their vanilla drops (still level-scaled by the setting above).
#
#   CharacterSpecificLoot:
#     BlobElite:
#     - Drop:
#         Prefab: Ooze                # item or creature prefab to drop
#         Min: 2                      # base amount range at 0 stars
#         Max: 3
#         Chance: 1                   # 0-1 chance for this drop line
#       AmountScaleFactor: 0.5        # how strongly the amount scales per level
#     - Drop:
#         Prefab: TrophyBlob
#         Chance: 0.1
#       ChanceScaleFactor: 0.01       # chance grows with level instead of amount
#       MaxScaledAmount: 1            # hard cap on the scaled amount
#
# Other per-drop options:
#   DoesNotScale: true                # drop the base range at every level
#   Drop.DontScale: true              # same, expressed on the inner drop
#   ScalebyMaxLevel: true             # amount interpolates Min->Max across the
#                                     # configured MaxLevel instead of per-level
#   UseChanceAsMultiplier: true       # use the chance value as the level scale
#   UntamedOnlyDrop / TamedOnlyDrop: true
#   Drop.OnePerPlayer: true           # one copy per nearby player
#
# --- NonCharacterSpecificLoot ---
# The same idea for trees, rocks, destructibles and pickables, keyed by prefab
# name (e.g. Pickable_Turnip, MineRock_Tin). Uses the same Drop block plus
# AmountScaleFactor / ChanceScaleFactor / MaxScaledAmount.
#
# --- Distance scaling ---
# Optional extra loot the further from the world center the drop happens:
#
#   EnableDistanceLootModifier: true
#   DistanceLootModifier:
#     2000: { MinAmountScaleFactorBonus: 0.1, MaxAmountScaleFactorBonus: 0.2 }
#     5000: { ChanceScaleFactorBonus: 0.05 }
#################################################";

        private const string ModifiersHeader = @"#################################################
# Star Level System Expanded - Creature Modifier Configuration
#
# Tunes the creature modifiers (affixes): which ones can roll, how likely each
# is, how strong it is, and which creatures/biomes it is allowed on. Server
# authoritative; edits apply live.
#
# Modifiers come in three pools: MajorModifiers, MinorModifiers and
# BossModifiers. How MANY a creature rolls (and the chance per slot) comes from
# the main .cfg (MaxMajorModifiers etc.) and can be overridden per creature in
# LevelSettings.yaml. This file configures the modifiers themselves.
#
# IMPORTANT: a NON-EMPTY pool section REPLACES the built-in list for that pool.
# If you write a MajorModifiers section, list every major you want active - a
# modifier you leave out stops rolling. An absent section keeps the defaults.
# Valid modifier names are exactly the keys in the generated default sections
# (Brutal, Fast, Big, Alert, Fire, Frost, Poison, Lightning, ElementalChaos,
# StaminaDrain, EitrDrain, Resist*, SoulEater, LifeLink, Splitter, Lootbags,
# FireNova, PoisonNova, Evolving, BossSummoner, ...). New modifiers can only be
# added through the mod API, not from yaml.
#
# Each entry:
#
#   MajorModifiers:
#     Fire:
#       Enabled: true
#       SelectionWeight: 10          # relative weight against the other entries
#       Config:
#         BasePower: 0.3             # meaning is per-modifier; for the damage
#         PerlevelPower: 0.01        # affixes these are fractions of the hit
#       UnallowedCreatures: [ Deer, Hare, Chicken, Hen ]
#       AllowedCreatures: []         # non-empty = ONLY these creatures
#       AllowedBiomes: []            # non-empty = ONLY these biomes
#
# Power semantics: effective power = BasePower + PerlevelPower * level. For
# damage affixes (Fire, Frost, ElementalChaos, ...) that is a fraction of the
# hit added as that element; for resists it is the damage reduction; for
# SoulEater/LifeLink/Splitter it drives their growth/split/link strength.
# Some modifiers read extra keys from Config (e.g. the drain modifiers accept
# BlockReduction / ParryReduction / DodgeReduction), and BossSummoner uses
# BiomeObjects to pick its summons per biome:
#
#   BossModifiers:
#     BossSummoner:
#       SelectionWeight: 10
#       Config:
#         BasePower: 10              # max concurrent summons
#         PerlevelPower: 120         # seconds between summon waves
#         BiomeObjects:
#           Meadows: [ Greyling ]
#           Plains: [ Goblin, GoblinShaman ]
#
# --- ModifierGlobalSettings ---
#   ModifierGlobalSettings:
#     GlobalIgnorePrefabList: [ piece_TrainingDummy ]   # never roll modifiers
#
# The console command sls-mod-give applies a modifier to nearby creatures for
# testing.
#################################################";

        private const string RaidSettingsHeader = @"#################################################
# Star Level System Expanded - Raid Settings
#
# Defines the SLS raid events that replace vanilla random events. Per-player:
# the server checks each player's progression keys and cooldowns and starts
# raids on eligible players. Set UseVanillaRaidConfiguration in the main .cfg
# to true to disable all of this and keep vanilla events.
#
# SERVER AUTHORITATIVE: the server's copy is synced to clients; editing this
# on a client does nothing. Edits apply live.
#
# --- GlobalSettings ---
#   GlobalSettings:
#     DisableAllRaids: false
#     PlayerBasedRaids: true         # raids target individual players
#     GlobalRaidIntervalScalar: 1    # >1 = raids less often, <1 = more often
#     GlobalRaidChanceScalar: 1      # scales every raid's activation chance
#
# --- Raids ---
# Each raid: what unlocks it, how it announces itself, and what it spawns.
#
#   Raids:
#   - Name: my_swamp_raid            # unique; also used for cooldown tracking
#     Duration: 120                  # seconds of active spawning
#     RaidActiveTillDefeated: true   # raid only ends once its creatures die
#     RaidCoolDownMinutes: 120       # per-player cooldown for THIS raid
#     EventRange: 96                 # radius of the event circle
#     StartMessage: $SLS_my_raid_start   # localization tokens or plain text
#     EndMessage: $SLS_my_raid_end
#     ForceEnvironment: SwampRain    # weather while the raid runs
#     ForceMusic: ZCombatEventL2     # combat music layer
#     Activation:
#       Chance: 25                   # 0-100, rolled each raid check
#       RequiredGlobalKeys: [ defeated_gdking ]    # world progression gates
#       RequiredPlayerKeys: [ KilledTroll ]        # per-player keys (all needed)
#       AnyRequiredPlayerKeys: []    # any one of these is enough
#       NotRequiredGlobalKeys: []    # blocks the raid when present
#       NearBaseOnly: false
#       PauseIfNoPlayerInArea: true
#     Spawns:
#     - PrefabName: Draugr
#       SpawnInterval: 10            # seconds between spawn waves
#       SpawnChance: 100             # chance per wave
#       SpawnGroupSize: 2            # creatures per wave
#       MaxSpawned: 6                # cap on this entry's living creatures
#       MaxSpawnTriggers: 0          # 0 = unlimited waves during the duration
#       InitalSpawnDelay: 0
#       CreatureAI: HuntPlayer       # HuntPlayer (default), Alerted or AgitatedByBuild
#                                    # Raid and nemesis spawns are always woken, so cave dwellers such as
#                                    # Ulv and Fenring_Cultist engage immediately instead of spawning asleep.
#       Faction: Undead
#       LevelMin: 3
#       LevelMax: 10
#       UseRaidLevelSystem: true     # roll levels from the tables below
#       CustomCreatureLevelUpChance: { 3: 100, 5: 50, 10: 5 }
#       LevelupGeneratorRefs: []     # or reference LevelSettings generators
#       RequiredModifiers: { Fire: Major }
#       ModifiersNotAllowed: [ Splitter ]
#
# Raid start/end messages support localization tokens ($...), and the shipped
# raids are a good reference for working key/environment/music values.
#################################################";

        private const string NemesisSettingsHeader = @"#################################################
# Star Level System Expanded - Nemesis Settings
#
# The Nemesis system reacts to how each player is doing: a hidden per-player
# score rises as they fight well and falls when they die, and score-gated
# events (stronger spawns, ambushes, minibosses) trigger against players who
# are doing too well. Needs EnableNemesisSystem in the main .cfg. Server
# authoritative; edits apply live.
#
# DO NOT edit NemesisVersion: a version that does not match this build resets
# the whole file to the defaults on load.
#
# NOTE: AvailableMiniBosses is written by the SERVER at runtime - minibosses
# created from player killers are added and spawned ones are removed, and the
# file is rewritten each time. Treat that section as state, not hand-config.
#
# --- ScoreSystem ---
# How the hidden score moves. Score decays toward NeutralScore over time.
#
#   ScoreSystem:
#     MinScore: 0
#     NeutralScore: 600
#     MaxScore: 1000
#     ScoreIntervalSeconds: 30       # how often the score recalculates
#     DecayPerUpdate: 30
#     MeleeDamageDealtFactor: 0.5    # score gained per point of damage dealt
#     RangedDamageDealtFactor: 0.25
#     MagicDamageDealtFactor: 0.3
#     DamageTakenFactor: 1           # score LOST per point of damage taken
#     BossKillBonus: 250
#     DeathScoreReduction: 500
#     NearbyPlayerRadius: 25         # players near each other share score drift
#     NearbyAveragingWeight: 0.05
#
# --- ChanceChanges ---
# Named, chance-based ops evaluated when a creature spawns near a player and
# the action cooldown allows. Actions: ChangeLevel, AddModifier,
# RemoveModifier, Spawn, SpawnMiniboss.
#
#   ChanceChanges:
#     CreatureOps:
#       Ocean Serpent Attack:
#         Enabled: true
#         Action: Spawn
#         Chance: 0.3                          # 0-1 roll each opportunity
#         ScoreThreshold: 7000                 # only above this score
#         RequiredGlobalKeys: [ defeated_gdking ]
#         AllowedBiomes: [ Ocean ]
#         ScoreChange: -1000                   # applied when the op fires
#         ExtraCooldownSeconds: 300
#         PlayerReqs:                          # optional player-state gates
#           PlayerCurrentBiome: Ocean
#           MinBiomeHistory: 2                 # time spent recently in that biome
#         SpawnConfig:
#         - Prefab: Serpent
#           CreatureAI: HuntPlayer
#           Faction: SeaMonsters
#           SpawnGroupSize: 1
#
# --- GaurenteedChanges ---
#   GaurenteedChanges:
#     FirstBossSetLevel: true        # the first time a player meets a boss...
#     FirstBossLevel: 0              # ...force it to this level (0 = base)
#
# --- Miniboss creation ---
# CreateMinibossFromPlayerKiller turns the creature that kills a player into a
# named miniboss added to the pool; NemesisMinionTemplatesByBiome defines the
# escorts spawned with a miniboss, per biome.
#
# --- RemoteSpawning ---
# Server-driven ambient minibosses placed around the world (also gated by the
# EnableNemesisRemoteSpawning setting in the main .cfg):
#
#   RemoteSpawning:
#     Enabled: true
#     CheckIntervalMinutes: 30       # placement cycle
#     MaxSpawnsPerInterval: 3
#     MaxConcurrentTotal: 10
#     TargetPerBiome: { Meadows: 1, Plains: 2 }
#     MaxConcurrentPerBiome: { Meadows: 2 }
#     BossCandidatesByBiome:         # archetypes used when the pool is empty
#       Plains:
#       - Prefab: GoblinBrute
#         ForcedLevel: 12
#         SelectionWeight: 1
#     ShowMapPin: true
#     PinShowsBossName: true
#
# NemesisBossLootTables adds biome-keyed bonus loot to remote bosses and their
# minions, using the same drop blocks as LootSettings.yaml. The console command
# sls-nemesis-spawn places a remote boss for testing, and sls-nemesis-score
# inspects or sets a player's score.
#################################################";

        private const string LocationResetHeader = @"#################################################
# Star Level System Expanded - Location Reset Settings
#
# Resets overworld locations, dungeons, vegetation, ores and pickables so they can be
# looted again. Sweeps run server-side, in the background, only in zones with no players
# nearby. Timers are in real-world hours. NOTHING resets until both the EnableLocationReset
# BepInEx setting and the Enabled flag below are turned on.
#
# BACK UP YOUR WORLD before enabling this.
#
# --- Reset groups: start here ---
# ResetGroups is where the work happens. A group both ENABLES a set of targets and gives them
# their settings, in one block:
#
#   ResetGroups:
#     Ores:
#       Enabled: true
#       ResetHours: 48
#       ResetTerrain: true
#       Members: [rock4_copper, MineRock_Tin, silvervein, rock3_silver, mudpile_beacon]
#
# Groups stand on their own - a member needs no entry anywhere else in this file. To turn a whole
# category off, set its Enabled: false; to drop one target, remove it from Members. If two groups
# claim the same prefab the shorter interval wins and the other is named in a warning. A member
# name this world has nothing for is warned about at load, never fatal.
#
# A member can also be a category token, $Mineable or $Pickable, which expands to everything this
# world PLACES carrying that component, and so covers modded content too. Unlike a named member a
# token stops there: one-off pickups that only exist inside dungeons are not swept up by it, so
# name those explicitly if you want them. Berry bushes are NOT Pickable_* prefabs, which is why
# they are listed separately below.
#
# MinDistance / MaxDistance limit a group to a ring around spawn (MaxDistance 0 = no outer limit).
# A scoped group applies only inside its range; outside it, its members fall back to whatever
# unscoped group covers them.
#
# --- Locations and Vegetation: overrides only ---
# Both ship EMPTY and can usually stay that way. Add a key only to override one target, or to
# enable something no group covers. Values resolve entry -> group -> Defaults, so a per-prefab
# setting always beats its group:
#
#   Locations:
#     Eikthyrnir:
#       ResetHours: 12      # BossAltars still enables it; this just retimes it
#
# For the full list of names this world can reset - including everything other mods add - run
# the console command sls-loc-dump. It writes SavedData/LocationResetCatalog.yaml as a
# reference; that file is a dump, not a config, and editing it does nothing.
#
# --- Scheduling: ResetHours or ResetSchedule ---
# ResetHours is elapsed time since a target last reset, so a 24h timer drifts a little later
# every cycle. ResetSchedule is a cron expression instead, for a fixed time of day:
#
#   ResetGroups:
#     Ores:
#       ResetSchedule: 0 3 * * *      # 03:00 every day
#     Foraging:
#       ResetSchedule: '*/30 * * * *' # every 30 minutes (quote anything starting with *)
#
# Fields are: minute hour day-of-month month day-of-week, with * , - and */n. Day-of-week is
# 0-6 (0 = Sunday) or SUN-SAT; months are 1-12 or JAN-DEC. The macros @hourly, @daily,
# @midnight, @weekly, @monthly and @yearly also work.
#
# Times are the SERVER'S LOCAL TIME, not UTC or in-game time. On the day the clocks go
# forward, a schedule inside the skipped hour runs as soon as the hour ends; on the day they
# go back it still runs once.
#
# Two gotchas worth knowing:
#   - If BOTH day-of-month and day-of-week are restricted, a day matching EITHER one fires.
#     0 0 1 * MON is the 1st of the month AND every Monday, not only Mondays that land on the
#     1st. Every cron behaves this way.
#   - BiomeRates and DistanceBands do NOT scale a cron schedule - there is no sensible way to
#     halve 'every Tuesday at 3am'. A rate of 0 still excludes the chunk entirely.
#
# ResetHours and ResetSchedule resolve as one unit, entry -> group -> Defaults: the first
# level that sets either one owns the timing, so a per-prefab ResetHours still overrides its
# group's ResetSchedule. Where a single level sets both, the schedule wins and it is logged.
# An invalid expression is logged and that target falls back to ResetHours.
#
# --- Targeting resets: BiomeRates and DistanceBands ---
# Both are multipliers applied on top of each entry's own ResetHours, so 1.0 changes
# nothing and they stack:  effective hours = ResetHours x biome rate x band rate.
# Use them to focus resets on the areas players actually strip.
#
#   BiomeRates:
#     Meadows: 0.5        # everything in the Meadows returns twice as fast
#     Mistlands: 2.0      # ...and half as fast out in the Mistlands
#   DistanceBands:
#   - Inner: 0            # metres from spawn (or from world centre - see
#     Outer: 3000         # DistanceBonusIsFromStarterTemple in the BepInEx config)
#     Multiplier: 0.5     # the hub recovers twice as fast
#
# A rate of 0 EXCLUDES that biome or band from resets entirely - it does not mean
# 'instantly'. Outer: 0 means the band has no outer limit. A chunk matching no band is
# left at 1.0, so a partial band list never disables the rest of the world.
#
# --- Protection: the three actions ---
# Every Protection category takes one of three actions:
#
#   Block     the whole chunk is left alone while the object is there. The safest, and the
#             default for everything except dropped items
#   Preserve  the object is kept, and the reset goes ahead around it
#   Ignore    the object is ordinary resettable content and IS DELETED
#
# DroppedItem ships as Preserve, so loot a player left on the ground survives a reset without
# holding the chunk back. If you ever see items piling up inside a location, set it to Ignore
# and they will be cleared with everything else.
#
# --- Ignored prefabs ---
# Each category can also list individual prefabs exempt from it. An ignored prefab neither
# blocks a chunk from resetting nor survives one - IT IS DELETED. fire_pit ships ignored
# because one abandoned campfire otherwise freezes a chunk (and its 8 neighbours) forever,
# and a campfire sitting on an ore spawn stops that ore ever coming back.
#
#   Protection:
#     PlayerBuiltPiece:
#       Action: Block
#       Ignored:
#       - fire_pit
#
# Tombstones can never be ignored, and anything in ProtectedPrefabs wins over an ignore.
# Tamed creatures are never deleted, whatever the categories or ignore lists say.
#
# --- ExtraTerrainRadius ---
# Per location: metres of terrain reset BEYOND the location's own radius, for the ramps and
# moats players dig around the outside. Clamped to 64m, which is as far as the protection
# scan actually checks for player property.
#
# --- Advanced sections, omitted while unused ---
# These four are left out of the generated file because their defaults are right for almost
# every server. Add the section by hand to use one; anything you leave out keeps its default.
#
#   ProtectedPrefabs: [portal_wood]   # always block a reset, whatever category detection says
#
#   DistanceBands: []                 # see the section above
#
#   InPlaceRefresh:                   # tier 1: refresh ZDO state without loading the zone
#     Pickables: true                 # regrow picked berries, mushrooms, flint
#     MineRocks: true                 # restore mined ore deposits
#     ContainerDefaultLoot: false     # re-roll chest loot. OFF: the one refresh that grants items
#
#   Throughput:                       # sweep pacing. Retune only if the server is struggling
#     SweepBudgetMillisecondsPerFrame: 4    # primary throttle (LocationResetSweepBudgetMs wins)
#     MaxZonesPerSecondFastLane: 200        # ZDO-only refreshes
#     MaxZonesPerSecondSlowLane: 2          # resets that must load the zone
#     AdaptiveBackoffFrameMs: 50            # over this frame time, halve the budget next tick
#     ZdoGrowthTolerance: 0                 # ZDOs a reset may leak before it is reported in the log
#################################################";
    }
}
