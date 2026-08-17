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
#################################################";

        private const string ModifiersHeader = @"#################################################
# Star Level System Expanded - Creature Modifier Configuration
#################################################";

        private const string RaidSettingsHeader = @"#################################################
# Star Level System Expanded - Raid Settings
#################################################";

        private const string NemesisSettingsHeader = @"#################################################
# Star Level System Expanded - Nemesis Settings
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
