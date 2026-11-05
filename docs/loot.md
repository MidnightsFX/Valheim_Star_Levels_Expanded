# Loot (LootSettings.yaml)

[All documentation](README.md)

Star level systems provides full optional configuration to control in detail all loot drops. Loot configuration can be found in the `LootSettings.yaml` file.

You may find it useful to see what existing drops look like within the SLS system. 
You can do this by dumping a debug configuration of all loot drops (as they are) with the in-game console command `sls-loot-dump`, this is output to the SLS config folder in the file `LootTablesDump.yaml`.

There are three core parts to configuring loot drops, but many options to modify if you wish to fine tune loot configurations.

- characterSpecificLoot | this governs all character based loot drops
- nonCharacterSpecificLoot | this controls all non-character based loot drops
- distanceLootModifier | this allows distance scaling of loot, similar to how distance level scaling works

The Loot page of the quick configure panel (Mod Config button) covers how much loot a level is worth without editing YAML:
the loot scaling style (`LootDropCalculationType`), loot and drop chance per star (`PerLevelLootScale`,
`PerLevelLootChanceScale`, `ChanceBaseChancePerLevel`), whether loot that never scales does
(`ScaleAllLootByLevel`), whether trees, rocks and birds scale at all (all off by default), the per-level scales for trees, rocks,
destructibles and birds, and the `distanceLootModifier` rings below (off by default), which can be added, removed and moved as well as tuned. Beside them it works those numbers through a real drop table - a Troll's own, read
from the loaded world - at no stars, at your Max stars, and inside the furthest ring, so a slider can be judged by the
loot it produces. Which creatures and objects have a custom table at all still lives in `LootSettings.yaml`.

## Character Specific Loot configuration
Lets take a look at the character specific loot drop. Here is one of the example configurations, and it is here to address a specific challenge.
Oozers create blobs when they die, this scales with the level of the creature. However, when you have a very high level oozer you can end up with 20,30 or more blobs.
Which can be impossible to deal with. This deals with that in a few ways, the final `- drop:` entry is for the Blob, spawning a minimum of 2 and a maximum of 2. But scaling with
the level of the `BlobElite` (Oozer), however that scaling is capped at a maximum of 6 drops, and only scales `.5` per level. So every other level another blob can be spawned
up to a maximum of 6.

Another useful entry to look at here is the drop for `TrophyBlob`, this has a `0.1` (10%) chance of happening, but it scales x0.01 per level, so a level five is (0.01 x 5) = 0.05 (5%) higher chance of happening.
This can be combined with a drop count to scale the drops for the entry, so a level 5 could drop more trophies. But this is disabled by setting the `maxScaledAmount: 1`
```
characterSpecificLoot:
  BlobElite:
  - drop:
      prefab: Ooze
      min: 2
      max: 3
      levelMultiplier: true
    amountScaleFactor: 0.5
  - drop:
      prefab: IronScrap
      chance: 0.33
      levelMultiplier: true
    amountScaleFactor: 0.2
  - drop:
      prefab: TrophyBlob
      chance: 0.1
      levelMultiplier: true
    chanceScaleFactor: 0.01
    maxScaledAmount: 1
  - drop:
      prefab: Blob
      min: 2
      max: 2
      levelMultiplier: true
    amountScaleFactor: 0.5
    maxScaledAmount: 6
```

## Non Character specific loot configuration

This is the section which allows you to configure all of the non-creature related loot in detail. It follows a similar structure to character loot configuration.
You can use this to modify or add loot to any existing objects which have the option to drop loot. For example, falling stalactites. This configuration gives them a chance to drop crystals.
The chance scales per level (yes inanimate objects can be leveled, tree, rock or destructible based on type). If loot configuration is not defined here then the generic configuration values from
the main config file apply here `PerLevelDestructibleLootScale`, `PerLevelMineRockLootScale`, and `PerLevelTreeLootScale` along with their max level values.
Level scaling can be ignored by setting `dontScale: true`

```
nonCharacterSpecificLoot:
  caverock_ice_stalagtite_falling:
  - drop:
      prefab: crystal
      chance: 0.3
      min: 1
      max: 3
      dontScale: true
  EvilHeart_Forest:
  - drop:
      prefab: AncientSeed
      onePerPlayer: true
      levelMultiplier: true
    maxScaledAmount: 3
```

## Creature remains
A Gammeltroll turns into a petrified gold vein when it dies, and that vein is its loot. The vein gives more gold for each of the troll's stars:
`PerLevelRemainsLootScale` in the main config sets how much, where 1 adds the vein's normal gold again for every star and 0 keeps it vanilla. Stone is not scaled.
A `nonCharacterSpecificLoot` entry for `TrollFrost_Frac` replaces this, and is scaled at the troll's level instead of the rock level.
The petrified troll and its gold vein are also the size the troll was, so a large starred troll leaves a large vein.

## Distance Loot Modifier
This is similar to the character distance modifier in that it applies a bonus to the minimum and maximum drops that an entry can have based on distance.
Loot rings are not drawn on the map, but behave exactly the same as Character distance rings (if the object is greater than x distance and less than the next once it receives that bonus).

```
distanceLootModifier:
  1250:
    minAmountScaleFactorBonus: 1.02
    maxAmountScaleFactorBonus: 1.02
```
