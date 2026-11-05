# Levels (LevelSettings.yaml)

[All documentation](README.md)

So you want creatures to have more levels, but you don't want to die instantly to a level 100 boar when you start the game? 
Well have I got the config file for you.

Level settings allows configuration of creature levelup chance, creature stats, max level, increased level up chance based on
distance from the center- and respectively biome based configs for all of that.

Want some help calculating and building a level definition? Check out [this tool](https://sls-levelspreadtool.netlify.app/)

## Biome Configuration
Lets take a look at some of the things you can do with this, and what better spot to start out than the default `All` biome
configuration (which applies to every creature by default).

Here is a section of the default example config, lets walk through what everything does.
```
 All:
    distanceScaleModifier: 1.5       # The influence of distance ring based scale increases (1.5 means 150% of the bonus value will be applied)
    spawnRateModifier: 1.5           # Spawn rate of every creature is 50% higher (1.5), which means every spawn has a 50% chance of being 2 creatures.
    creatureBaseValueModifiers:      # These values modify the base stats of every creature in this biome (eg, all creatures)
      BaseHealth: 1                  # The default health of all creatures is 100% (1), numbers below 1 reduce health, above increase is (2) is 200% health for everything
      BaseDamage: 1                  # Default damage of all creatures is 100% (1)
      Speed: 1                       # Default movement speed of all creatures is 100% (1)
      Size: 1                        # Default size of all creatures is 100% (1)
    creaturePerLevelValueModifiers:  # Per level modifiers are applied per level, eg each star will give this value to the creature
      HealthPerLevel: 0.4            # Each star provides 40% more health (0.4)
      DamagePerLevel: 0.1            # Each star provides 10% more damage (0.1)
      SpeedPerLevel: 0               # Each star does not increase speed (0)
      SizePerLevel: 0.1              # Each star makes the creature 10% bigger (0.1). Negative values shrink instead (-0.1 is 10% smaller per star)
    damageRecievedModifiers:         # Damage reduction or increases
      Poison: 1.5                    # Everything recieves 50% (1.5) more damage from poison (that includes players)
```
Note: `creaturePerLevelValueModifiers` do not apply to characters. But, `damageRecievedModifiers` DOES.

Note on sizing: per-level size is applied as `Size + (SizePerLevel * stars)`, so a 0 star creature is
exactly `Size`. `SizePerLevel` may be negative to make creatures shrink with each star. The final
multiplier is floored at the `MinimumCreatureScale` config value (default `0.1`), so creatures can never
reach zero size or turn inside-out no matter how negative the value is. It is also capped at the
`MaximumCreatureScale` config value (default `5`), which holds base size, size per star and modifiers like Big
together. Both it and the global size per star (`PerLevelScaleBonus`) are at the bottom of the quick configure
panel's Level Distribution page. A creature entry can set its own cap with
`MaxSizeScale`, which replaces the global one for that creature.

Note on mounts: any creature carrying a saddle stays rideable when it is scaled, including creatures added by other
mods, with nothing to register. It rides at full speed, its saddle reach grows with it, aiming at its upper body offers
the saddle (the lower body still pets it), and getting off puts you on the ground beside it. Each mount found is named
once in the log. `EnableRidableCreatureSizeFixes` turns this off for every creature, and `RideableSizeFixes: false` in a
creature's entry turns it off for that one, which suits a mod that manages its own mount sizing.

Biome specific configurations can be used to override the default `All` configuration, in this case max level for Ashlands is being set
to 26 and the distance modifier is being reduced by 50%
```
  AshLands:
    biomeMaxLevelOverride: 26
    distanceScaleModifier: 0.5
```

## Custom biomes and larger worlds
Biomes added by other mods work anywhere a biome does, in every config file. With Expand World Data, use the biome's name
from `expand_biomes.yaml`, the `biome` field rather than its display name:
```
BiomeConfiguration:
  testgrove:
    biomeMaxLevelOverride: 12
```
Its number works too, but Expand World Data hands numbers out by the order of its file, so reordering that file changes them.

A custom biome with no entry of its own uses the `All` entry and nothing else, so raids and Nemesis rules that list biomes
skip it, and it has no summons or boss tiers. To have it use another biome's settings in every config file, add to
`LevelSettings.yaml`:
```
BiomeFallbacks:
  testgrove: BlackForest
  ashforest: None
UseTerrainAsBiomeFallback: true
```
`BiomeFallbacks` names a biome's source, and `None` keeps a biome on its own settings. `UseTerrainAsBiomeFallback` gives every
other custom biome the biome its Expand World terrain is built from.

A name that is not a biome in this world does nothing, stays in the file, and is reported once when the world starts. The
`sls-biomes` console command lists every biome and where its settings come from; `sls-biomes here` also shows the biome at your
position, both as the game reports it and as the world generated it.

Mods that make a local biome, such as Biome Generator, change what the game reports inside it, and creature levels follow that.
Turn off `DomeBiomeSetsCreatureLevels` to always use the biome the world generated.

Worlds resized by Expand World Size get zones across all of their land, object levels that reach their maximum at the edge of the
land, and remote Nemesis bosses placed where their biome really is. Zone kills and levels carry over when a world grows.

Bosses and pins already stored keep the biome number they were created with, so reordering `expand_biomes.yaml` can move those
to a different biome.

## Distance, zone and region scaling
Three systems can raise level chances on top of the base chances, all off until you turn them on. All of them are on the Level
Scaling page of the quick configure panel, which comes right after Level Distribution. Its chart shows your chances from that page
in yellow, then a line for each system that is on (distance blue, zone orange, region green) at the ring or level you pick under
it, and says in words what they come to together. They stack on a spawn:

```
chance = (base chance + ring bonus x distance scale) x night x zone x region x world combat rate
```

| | What it adds | Grows from | Map overlay |
| --- | --- | --- | --- |
| Distance | Each ring's bonus, added to the chance | Being further from the center or the starter temple | Rings |
| Zone | A multiplier: `1 + (level - 1) x ZoneLevelBonusPerLevel` | Kills in a square cell of the map, and it decays over time | Cell outlines |
| Region | A multiplier: `1 + (level - 1) x RegionLevelBonusPerLevel` | Kills in a region, and it decays over time | A low-opacity fill in the level's color, with a line around it |

An overlay and a level readout only show while their system is on. The overlay settings (`EnableMapRingsForDistanceBonus`,
`EnableZoneMapOverlay`, `EnableRegionMapOverlay`) are on by default, so turning a system on shows it unless you have turned its
overlay off. Region levels use the zone settings `ZoneOverlayColorOptions` for their colors and `ZoneDecayClock` for their decay.

Regions are built once, on the server, and stored with the world in `SavedData/RegionData.<world>.yaml`. Clients are sent the map when
they connect. Each landmass is cut up from seeds spaced along its shoreline, growing inward so its regions come out about
`RegionTargetSize` across. Land that can't be divided without a piece falling under `RegionMinSize` stays one larger region. Mountains of
at least `RegionMountainMinSize` become regions of their own, and rocks in the sea too small to matter get none.

Sizes are meters, as the side of a square of the same area. A map keeps the sizes it was cut with until it is cut again, since that
resets its levels: saving a new size on the panel while in a world does it for you, and so do `sls-zone-rebuild` and `sls-region-rebuild`.

## Creature Configuration
Creature specific configuration allows you to override what is set in the biome definition for a creature, which allows more
fine-grained control of how a specific creature should be modified.

Lets take a look at this creature definition
```
  Troll:                                # Creature prefab name, must match exactly, you can find this using VNEI, the wiki, or Jotunn prefab documentation
    customCreatureLevelUpChance:        # Sets the levelup chance for this specific creature, overrides the default values, can still be modified by distance bonuses
      1: 100                            # Gauranteed level 1
      2: 50                             # 50% chance to reach level 2
      3: 5                              # 5% chance to reach level 3
    creatureMaxLevelOverride: 11        # Overrides the max level for this creature (11 instead of biome default)
    creatureBaseValueModifiers:         # Overrides the base stat modifiers for this creature, values below 1 reduce stats, above 1 increase stats
      BaseHealth: 1.05                  # Base health is increased by 5%
      BaseDamage: 0.95                  # Base damage is reduced by 5%
      Speed: 1.05                       # Speed is increased by 5%
      Size: 1.05                        # Size is increased by 5%
      AttackSpeed: 1.05                 # Attack speed is increased by 5%
    creaturePerLevelValueModifiers:     # Per level stat modifiers for this creature, these are multiplied by the creatures level and applied to the total stat value
      HealthPerLevel: 0.3               # Each star provides 30% more health
      DamagePerLevel: 0.05              # Each star provides 5% more damage
      SizePerLevel: 0.005               # Each star provides 0.5% more size (use a negative value to shrink per star)
    requiredModifiers:                  # Modifiers that this creature will always spawn with, regardless of level, chance or modifier limit, but still count towards max modifier count
      Poison: Major                     # Trolls will always spawn with the Poison major modifier, Modifier names can be found in Modifiers.yaml, along with their categories
```

Creatures summoned on the players' side, like the nature staff's roots or the Dead Raiser's skeletons, roll stars like any
other creature by default. Turn on `PlayerSummonsKeepVanillaLevel` under `LevelSystem` in the mod's .cfg to have them keep
the level the game or their mod gives them. To change just one summon, give it a creature entry instead, for example
`CreatureMaxLevelOverride: 0` under `staff_greenroots_tentaroot`.

## Levelup Chance
This is a definition of the chance that a creature has to level up at each point.

This works hand in hand with the distance scale modifier and `distanceLevelBonus`, distance level bonuses are applied if
the creature falls into a distance category with bonuses.
eg: 
```
distanceLevelBonus:
  1250:
    1: 25
```
Will give all creatures a +25% chance to reach the first star level, if they are at least 1250m from the center. This value is then modified based on biome settings.
In our example biome file we have a `1.5` value for the distance modifier so `1.5 x 25 = 37.5` would be the increase provided to reach level 1.

If the total bonus and base value exceeds `100` that level will be guaranteed, every creature with that condition will be at a minimum that level.
You can see this in some of the later distance bonuses which slowly drive of the guaranteed spawn level of creatures up

```
  5000:
    1: 100      
    2: 100      # All creatures at least 5000m from center will be level 2+
    3: 75
    4: 50
    5: 25
    6: 15
```

You can also use the distanceLevelBonus to add additional levels, make it so that certain distances are required to spawn higher level enemies.

```
defaultCreatureLevelUpChance:
  1: 20
  2: 15
distanceLevelBonus:
  1250:
    1: 25
    2: 15
    3: 5
  5000:
    1: 100      
    2: 100
    3: 75
    4: 50
```

With the above configuration the region closest to start up to 1250m will only be able to spawn level 1 and 2 creatures. Between 1250m and 5000m creatures can spawn up to level 3, and beyond 5000m creatures can spawn up to level 4.

The Level Scaling page of the quick configure panel (Mod Config button) edits `distanceLevelBonus` without touching YAML:
add and remove rings, move them by typing a new distance, and type each ring's bonus as a list (`25, 15, 5` is levels 1, 2
and 3; a ring that skips a level is shown and typed as `1:25, 3:5`). Its chart shows the star chances past the ring you pick, on the
curve from the Level Distribution page, weighted by the `All` biome's distance scale, which is on that page too along with each
biome's own.


Now, we've walked through a lot of the bonuses to level up chance but lets take a look at the base values too. 
Star level systems default config has a relatively large spawn range- which is limited by biome configuration.

`defaultCreatureLevelUpChance` Defines the levelup chance of all creatures, this however can be limited by biome configuration and
it can be increased by other factors, such as the distance from center bonus.
```
defaultCreatureLevelUpChance:
  1: 20
  2: 15
  3: 12
  4: 10
  5: 8
  6: 6.5
  7: 5
  8: 3.5
  9: 1.5
  10: 1
  11: 0.5
  12: 0.25
```

## Level Generators
Writing a long levelup chance table by hand is tedious, so generators build one for you from a level range and a curve style.
A generator can replace the default table (`DefaultLevelupGenerators`), a biome or creature table (`LevelupGenerators`), and is
also used by raid spawns, nemesis spawns and boss-conditional chances. Name a list of generators in `CustomLevelupGenerators` to
reference it from anywhere with `LevelupGeneratorRefs` / `DefaultLevelupGeneratorRefs`.

```
CustomLevelupGenerators:
  late_game:
  - MinLevel: 1
    MaxLevel: 25
    LevelUpChance: 0.35             # 0-1 fraction, 0.35 = 35%
    LevelupCalculationStyle: Gaussian
    GaussianOffset: 0.25            # Gaussian only: -1 to 1, shifts the most likely level
    NightMultiplier: 1.5            # scales these chances at night only, 1.5 = level-ups 50% more likely (1 = no change)
DefaultLevelupGeneratorRefs: [ late_game ]
```

Curve styles:
- `Linear` - the chance to level up drops evenly from `LevelUpChance` at `MinLevel` to nothing at `MaxLevel`.
- `Exponential` - the chance drops quickly at first, so low levels are common and high levels are rare.
- `Gaussian` - a bell curve that favours levels in the middle of the range (moved with `GaussianOffset`).
- `Table` - uses a table you write yourself from `LevelupChanceTablesBySpan` instead of a formula.

Bosses roll from the same chances as everything else unless you give them `BossLevelupGenerators`, which fills
`BossCreatureLevelUpChance` on load and applies to anything the game counts as a boss, modded ones included:

```
BossLevelupGenerators:
- MinLevel: 1
  MaxLevel: 11
  LevelUpChance: 0.3
  LevelupCalculationStyle: Gaussian
```

A boss curve outranks the default, conditional and biome chances wherever that boss stands, while a `CreatureConfiguration`
entry naming the boss still wins over it. `MaxBossLevel` caps bosses either way. The Level Distribution page of the quick
configure panel turns this on and off with its **Own curve for bosses** toggle and charts both curves side by side. A boss curve
in the Table style edits its own table in the **Boss thresholds** box.

### Table style
`LevelupChanceTablesBySpan` is keyed by how many levels a generator covers (`MaxLevel - MinLevel + 1`), not by the levels themselves.
Inside a table, key `1` is the generator's `MinLevel`, key `2` is `MinLevel + 1` and so on, so one table can be reused by many generators
that cover the same number of levels at different points in the world. Like `DefaultCreatureLevelUpChance`, each value is the % chance
to roll past that level, so the values must go down. `LevelUpChance` is not used.

```
LevelupChanceTablesBySpan:
  4: { 1: 30, 2: 15, 3: 5, 4: 0.01 }
CustomLevelupGenerators:
  swamp_tier:
  - MinLevel: 3                     # levels 3-6 are 4 levels, so this uses table 4:
    MaxLevel: 6                     # 30% chance to roll past level 3, 15% past 4, 5% past 5
    LevelupCalculationStyle: Table
```

`BossLevelupChanceTablesBySpan` is the same thing for boss curves only. A `Table` generator in `BossLevelupGenerators` or
`BossLevelupGeneratorRefs` looks there first and falls back to `LevelupChanceTablesBySpan` for a span with no entry, so bosses can
have their own chances at the same length as any other curve:

```
BossLevelupChanceTablesBySpan:
  4: { 1: 60, 2: 35, 3: 10, 4: 0.01 }
BossLevelupGenerators:
- MinLevel: 1                       # levels 1-4 are 4 levels, so this uses the boss table 4:
  MaxLevel: 4
  LevelupCalculationStyle: Table
```

If there is no table for a generator's span it uses the `Exponential` curve instead, and a warning is logged once. Tables that are
missing keys or whose values do not go down are also reported when the config loads. The quick configure panel can edit the table
for its default generator directly: pick the `Table` curve style and type the values separated by commas.

## Boss-Conditional Levelup Chances
With `EnableConditionalCreatureLevelupChance: true`, older biomes get harder as world bosses are defeated, working from your own
`MaxLevel` and level chances. The Level Scaling page of the quick configure panel has the switch and the first two settings here.

```
EnableConditionalCreatureLevelupChance: true
ConditionalBiomeDelay: 1            # how many biomes back a boss raises difficulty
ConditionalDifficultyIncreases: 7   # the most increases a biome can get, at least 1
ConditionalFullChanceMultiplier: 4  # what level-up chances are multiplied by at full strength
ConditionalBossKeyOrder:            # earliest to latest; leave out to use this vanilla order
- defeated_eikthyr
- defeated_gdking
- defeated_bonemass
- defeated_dragon
- defeated_goblinking
- defeated_queen
- defeated_fader
- defeated_frozenking_p3
```

A biome earns one difficulty increase per boss from its own on, less `ConditionalBiomeDelay`. At the default of 1, defeating Eikthyr does
nothing, since the Meadows is the only biome so far. Defeating the Elder raises the Meadows but not the Black Forest, and Bonemass then
raises both of those but not the Swamp. The Deep North's boss comes last, so at 1 the Deep North never rises. At 0, a boss raises its own biome too.

`ConditionalDifficultyIncreases` is the most a biome can get. At 1, the Meadows goes up once when the Elder falls and Bonemass adds nothing
more there. At 2 it can go up a second time, and so on.

Every increase is the same size, and one per boss after the first adds up to full strength, so with the vanilla bosses each is a seventh of
it. An increase multiplies the biome's level chances a step further toward `ConditionalFullChanceMultiplier`, about 1.43 times after the first at
the default of 4, and moves its star cap a step from `BiomeMaxLevelOverride` toward `MaxLevel`, never past it. A biome with no cap of its own, or
one already at or above `MaxLevel`, keeps the cap it has. There is no minimum level. So with a `MaxLevel` of 4, the Meadows starts at 1 star and
reaches 4 once the last boss falls, while newer biomes have fewer bosses left to rise by. A creature entry with its own curve still wins, and
bosses keep `MaxBossLevel`.

Add keys from modded bosses where they belong in your progression. The latest defeated key in the list is what counts, and the tier changes as
soon as a boss key is set or removed, for creatures spawned after that. A file that still holds the vanilla list from before the Deep North update
gets `defeated_frozenking_p3` (set when the Deep North's final boss dies) added on load; a list you changed is left alone.

### Fixed levels
`ConditionalCreatureLevelupChance` sets fixed levels for a boss and biome, in place of that biome's increases. Nothing ships in it.

```
ConditionalCreatureLevelupChance:
  defeated_bonemass:
    Meadows:
      LevelupGenerators:
      - { MinLevel: 4, MaxLevel: 16, LevelUpChance: 0.25, LevelupCalculationStyle: Exponential }
```

The latest defeated key that has an entry applies, and the order of entries in the file does not matter. A key that is not in
`ConditionalBossKeyOrder` only applies while no listed boss that has an entry is defeated. An `All` entry inside a tier is the fallback for biomes the
tier does not name. These replace the biome's levelup chances and its minimum level: creatures there roll at least the generators' `MinLevel`, whatever
`BiomeMinLevelOverride` says. Their `MaxLevel` can raise the biome's cap, even past `MaxLevel`, and where it is lower than the cap the cap stays, so
beating a boss never lowers a biome's levels. Tiers used to ship this way. A file still holding those shipped levels has them removed when it
loads, once, and an entry you changed by hand is left as you wrote it.

## The Deep North's final boss
Kall Fimbulbringer is fought in three phases, each a separate creature. The second phase can only be hurt by the explosions
of the seven boss spirits it calls up, one each as it dies, and those add up to exactly its health. So SLS leaves that part
of the fight as vanilla made it:

- The second phase (`FrozenKing_p2`) always stays at 0 stars, with no modifiers and its vanilla health, whatever the config says.
- The spirits (`Aspect_*`) still roll stars and modifiers, but spawn rates and disabled spawns never clone or remove them,
  they never roll Splitter, and one that kills a player is never turned into a Nemesis.
- The last phase comes back with the stars the first phase had.
