# Raids (RaidSettings.yaml)

[All documentation](README.md)

SLS supports significantly overhauled and configurable raids. These can be disabled if you wish to revert to vanilla style raids.

By default all raids are per-player, server controlled and concurrent raids are enabled. This means with SLS it is possible that every player on a server gets an appropriate raid
during an event.

The number of players, frequency, and most all details of each raid is configurable through `RaidSettings.yaml`.

The Raids page of the quick configure panel (Mod Config button) covers the common ones without editing YAML: the global
raid settings (pausing every raid, multipliers on every raid's activation chance and cooldown, how long a raid waits
for its creatures to be finished off, and the raid creature density), which raids are enabled, and per creature in a raid how many arrive each wave (`SpawnGroupSize`), how many
may be alive at once (`MaxSpawned`, where 0 stops that creature spawning) and the seconds between its waves
(`SpawnInterval`). Open a raid's **Spawns** button to see its creatures.

New raid files ship with every raid's chance halved (`GlobalRaidChanceScalar: 0.5`), its cooldown doubled
(`GlobalRaidIntervalScalar: 2`) and a lighter creature density (2); an existing `RaidSettings.yaml` keeps its own values.
The world's vanilla **Raids** modifier applies on top, the way vanilla applies it to its own events: None starts no SLS
raids, Less and Much less lengthen cooldowns and lower chances, More and Much more do the opposite. The Raids page shows
the current world's setting.

Raid settings are **server authoritative**. On a dedicated or player hosted server the server's `UseVanillaRaidConfiguration` value and its `RaidSettings.yaml` are synced down to every client on join, so editing either of them on a client has no effect - change them on the server.

`RaidSettings.yaml` carries a `RaidVersion` stamp, like `NemesisVersion`. Do not edit it: when the shipped raids change in a way every install should pick up, the version is bumped and a file at any other version (or with none, as every file from before 1.14.0) is reset to the new defaults on load. When the versions since a file's own only added raids, as version 3 added the Deep North's `army_elakingar` and `army_jotuns`, those raids are added instead, sized to the file's raid creature density, and everything else in it is kept. The previous file is saved next to it first as `RaidSettings.yaml.v<old version>.<date>.bak`, so raids of your own can be copied back in.

## Raid cooldowns and logging out

Each player's raid cooldown, and the server's own raid check schedule, are saved per world and picked back up exactly where they left off when the world is loaded again. Logging out and back in does not hand anyone a fresh raid.

`RaidCooldownClock` chooses what a cooldown is actually measured against:

- `WorldTime` (default) - the world's own clock. It advances whenever anybody is playing the world, and jumps forward when someone sleeps through a night. On a busy server a player's cooldown keeps burning down while other people play without them.
- `PlayerTime` - each player's own time in the world. A cooldown only counts down while that player is actually online, so logging out with 20 minutes left brings them back with 20 minutes left however long they were away, and other people's sessions do not shorten it.

Neither clock runs while nobody is playing. Switching between them re-bases everyone's remaining cooldown, so nobody gains or loses raid time by the change.

## Player keys and boss kills

`RequiredGlobalKeys` and `NotRequiredGlobalKeys` check the world's keys, which are the same for everyone. `RequiredPlayerKeys`, `AnyRequiredPlayerKeys` and `NotRequiredPlayerKeys` check each player's own keys, so players can be at different points. The shipped raids only use global keys.

A kill that sets a key (every boss, plus creatures such as the Troll's `KilledTroll`) sets the world key for everyone, but the player key only for whoever gets credit. In vanilla that is the one player whose game controlled the boss; a key share mod such as ValheimCommunityPatch extends it to the players online and nearby. With `GrantWorldDefeatKeysOnJoin` (main config, on by default) SLS catches everyone else up: each time a player joins or respawns, they are given the player key for every defeat the world has already recorded, including kills made while they were offline and keys set with `setkey`. The key is saved on the character like a real kill, so it goes with that character to other worlds.

## How a raid ends

A raid spawns creatures for its `Duration`. It then stays active, with its creatures still hunting and its map pin, music and weather in place, so players can finish off what it spawned. Killing every creature it spawned ends it straight away. How long it waits otherwise depends on `RaidActiveTillDefeated`:

- `false` (default) - at most `PauseBeforeCompleted` seconds, set in `RaidSettings.yaml`'s `GlobalSettings` or on the quick configure panel's Raids page (default 30). 0 ends the raid as soon as its `Duration` elapses.
- `true` - until every creature it spawned is dead, for at most `RaidActiveTillDefeatedMaxSeconds` in the main config (default 300), so a straggler stuck somewhere cannot hold a raid open forever.

Either way the raid then winds down for `RaidWindDownSeconds`: the end message shows, the pin, music and weather go, and the creatures wander off and despawn. Anything still around at the end of the window is deleted when `RaidForceDeleteStragglers` is on.

While you are inside a raid's area the vanilla event banner at the top of the screen shows the raid's start message, just as it does for vanilla events. `sls-raid-clear-pins` removes every SLS raid pin from your map if any are ever left behind.

Raids never stack. Before a raid starts, the server checks for any raid still in the world within `RaidExclusionRange` (main config, default 500m), running or winding down, whoever it belongs to and however it was started, and skips the new one. The first raid in an area is the only raid; other players elsewhere are still considered. `sls-raid-spawn` is refused the same way.

Raids also leave boss fights alone. A player within `RaidBossExclusionRange` (main config, default 100m, the distance the boss health bar shows from) of a living boss is skipped, and considered again on the next raid check. Any creature the game treats as a boss counts, modded bosses and Nemesis minibosses included. `sls-raid-spawn` and the `event` command still start a raid there, with a warning from `sls-raid-spawn`.

## Where raid creatures come from

A raid's creatures appear at random spots within 80% of its `EventRange`, never in water or lava, and never inside a player's base: the area around a workbench, fire, bed and the like that stops monsters spawning. Mods that enlarge that area, such as AzuWorkbenchTweaks, are respected. When a base leaves no room nearby the raid looks a little further out, and if it still finds none the raid is skipped.

For servers with very large bases, turn on `RaidSpawnSearchGrowsPastBases` (main config, off by default). The search then keeps stepping outward until it finds room just past the base's edge. Creatures only move while they are within roughly 64-128m of a player, so the search stops there; a base that reaches further is raided from the furthest ground still in reach, inside the base. Either way, a raid's map area grows to cover wherever its creatures come from.

Below is an example of many of the details that can be configured for a given raid
```
- Name: foresttrolls              # Each raid has its own name, these should be unique or can be incorrectly selected
  Duration: 180                   # Duration of the raid, in seconds
  RaidCoolDownMinutes: 240        # The number of minutes between when this raid triggers and the player becomes eligble for a new raid
  ForceEnvironment: Crypt         # Environment that will be set when the raid starts available options: Clear, Misty, Darklands_dark, DeepForest_Mist, Heath_clear, InfectedMine, GDKing, Rain, LightRain, ThunderStorm, Eikthyr, Fader, 
                                  #  GoblinKing, nofogts, SwampRain, Bonemass, Snow, SnowStorm, Twilight_Clear, Twilight_Snow, Twilight_SnowStorm, Moder, Crypt, CryptHildir, Ghosts, Queen, SunkenCrypt, Mistlands_clear, Mistlands_rain, 
  Activation:                     #  Mistlands_thunder, Ashlands_ashrain, Ashlands_ashrain_clear, Ashlands_CinderRain, Ashlands_meteorshower, Ashlands_misty, Ashlands_SeaStorm, Ashlands_storm, Caves, CavesHildir
    Chance: 50                    # Chance that this raid is selected when it is a valid option, only one raid can be selected for a given player at a time
    RequiredGlobalKeys:           # The global keys required for this raid to activate
    - defeated_bonemass
    NotRequiredGlobalKeys:        # The global keys that must be missing for this raid to activate
    - defeated_dragon
    RequiredPlayerKeys:           # Player keys that must be set to activate
    - Deathlink
    AnyRequiredPlayerKeys:        # Player must have at least ONE of the listed player keys for the raid to be selected for them
    - Deathlink Deathbringer
    - Deathlink Hardcore
    NotRequiredPlayerKeys:        # Player must not have any of the following player keys for the raid to be selected for them
    - Deathlink Vanilla
  Spawns:
  - PrefabName: Troll             # Prefab name of a creature to spawn in the raid
    CreatureAI: AgitatedByBuild   # Type of creature AI setting to apply, valid values are: Alert, AgitatedByBuild, HuntPlayer
    SpawnInterval: 40             # Number of seconds between when another of this monster can spawn
    Faction: Demon                # Faction this creature will be assigned to Valid options: Players, AnimalsVeg, ForestMonsters, Undead, Demon, MountainMonsters, SeaMonsters, PlainsMonsters, Boss, MistlandsMonsters, Dverger, PlayerSpawned
    MaxSpawned: 3                 # Maximum number of this creature that can be alive (from this specific spawn group)
    SpawnGroupSize: 2             # Number of creatures to spawn at once
    LevelMin: 1                   # Min level, if using RaidLevelSystem: rolled levels are raised to at least this
    LevelMax: 3                   # Max level, if using RaidLevelSystem. 0 or left out uses MaxLevel from the main config
    UseRaidLevelSystem: true      # Wether to use the default leveling system or the custom level defintion defined here
    ModifiersNotAllowed:          # Modifiers the creature can't get
    - Poison
    RequiredModifiers:            # Modifiers that the creature must spawn with, this also requires the modifier type (Boss, Major, Minor)
      Fire: Major
    ModifierOverrides:            # This spawn's own values for modifiers its creatures have or roll, in place of the ones in Modifiers.yaml
      Fire:
        PerlevelPower: 0.05       # BasePower, PerlevelPower and Config entries, any of them; the rest stay as configured
    CustomCreatureLevelUpChance:  # Custom level chances for this creature
      1: 50
      2: 25
      3: 20
      4: 15
      5: 7
      6: 3
      7: 1
  StartMessage: $event_foresttrolls_start # Start of the raid message
  EndMessage: $event_foresttrolls_end     # End of the raid message
  ForceMusic: Zblackforest                # Music that starts when the raid happens eg: ZCombatEventL1, ZCombatEventL2, ZCombatEventL3, ZCombatEventL4, Zboss_eikthyr, Zboss_gdking, Zboss_bonemass, Zboss_moder, Zboss_goblinking, Zboss_queen, Zboss_queen_ambience, Zboss_fader, Zboss_frozenking, ZJotunInvasion,
                                          #   Zblackforest, Zmeadows, Zswamp, Zmountain, Zplains, Zplainstower, Zmistlands, Zashlands, Zdeepnorth
```
