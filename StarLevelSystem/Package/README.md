# StarLevelSystem
Star Level Systems expands upon the Valheim star system and allows extensive customization.

Features:
- Expand Creature Star levels (as high as you want, default limit is 100)
	- Sizing configuration for all creatures and their equipment
- Fine grained control of all creature aspects
	- Health, damage, size, speed, attack speed, base and per level
	- Resistance or weakness to all elements
- Unique colorization for any creature
	- Per level colorization for any creature
	- Bulk color generators for defining whole ranges of color levels
- Unique Modifiers for all creatures, multiple different categories (Boss, Major, Minor)
	- Creatures names based on modifiers
	- Star Icons based on modifiers
	- Visual effects for modifiers
	- Per level, fine grained configuration and tuning of modifiers
- Scaling, fine-grained control and configuration of all creature drops
	- scale individual loot entries differently
	- modify chance drops based on level of the creature
	- Add, remove, change all drops
- Scaling of the world around you
	- level up the FISH!
	- level scaled BIRDS
	- level scaled TREES
- Server synchronized configs
- Active config reloading

## Features

### Levels, Levels and more Levels (LevelSettings.yaml)
So you want creatures to have more levels, but you don't want to die instantly to a level 100 boar when you start the game? 
Well have I got the config file for you.

Level settings allows configuration of creature levelup chance, creature stats, max level, increased level up chance based on
distance from the center- and respectively biome based configs for all of that.

#### Biome Configuration
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
      SizePerLevel: 0.1              # Each star makes the creature 10% bigger (0.1)
    damageRecievedModifiers:         # Damage reduction or increases
      Poison: 1.5                    # Everything recieves 50% (1.5) more damage from poison (that includes players)
```
Note: `creaturePerLevelValueModifiers` do not apply to characters. But, `damageRecievedModifiers` DOES.

Biome specific configurations can be used to override the default `All` configuration, in this case max level for Ashlands is being set
to 26 and the distance modifier is being reduced by 50%
```
  AshLands:
    biomeMaxLevelOverride: 26
    distanceScaleModifier: 0.5
```

#### Creature Configuration
Creature specific configuration allows you to override what is set in the biome definition for a creature, which allows more
fine-grained control of how a specific creature should be modified.

Lets take a look at Eikthyr
```
  Eikthyr:                            # The prefab of the creature to modify, this can be any valid creature.
    creatureMaxLevelOverride: 4       # The maximum level that this creature can level up to, regardless of biome
    creaturePerLevelValueModifiers:   # Creature base and per level modifiers are supported here, just like the ones defined in Biome configuration
      HealthPerLevel: 0.3             # 30% (0.3) more health per level
      DamagePerLevel: 0.05            # 5% (0.05) more damage per level
      SizePerLevel: 0.07              # 7% (0.07) larger per level
```

#### Levelup Chance
This is a definition of the chance that a creature has to level up at each point.

This works hand in hand with the distance scale modifier and `distanceLevelBonus`, distance level bonuses are applied if
the creature falls into a distance category with bonuses.
eg: 
```
distanceLevelBonus:
  1250:
    1: 0.25
```
Will give all creatures a +25% chance to reach the first star level, if they are at least 1250m from the center. This value is then modified bast on biome settings.
In our example biome file we have a `1.5` value for the distance modifier so `1.5 x 0.25 = 0.375` would be the increase provided to reach level 1.

If the total bonus and base value exceeds `1.0` that level will be gaurenteed, every creature with that condition will be at a minimum that level.
You can see this in some of the later distance bonuses which slowly drive of the guarnteed spawn level of creatures.

```
  5000:
    1: 1      
    2: 1      # All creatures at least 5000m from center will be level 2+
    3: 0.75
    4: 0.5
    5: 0.25
    6: 0.15
```

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


### Colorization (Colorization.yaml)
In vanilla there are few creatures which can be colorized when they level up. Star Level Systems changes that. 
Most all creatures can be colorized, it should be noted that some creatures (Yagluth eg) do not colorize well and the effect is generally not very noticable.

There are two different ways to apply colorization values to creatures.

- Creature specific color definitions. 
  These are split between default definitions (applied to any creature, if it does not have a more specific entry) 
  and character specific entries. These will be applied at the keyed level to the specified creature.
	```
	  Greydwarf:
		1:
		  hue: -0.06
		  saturation: 0.1
		  value: 0.05
	```
- Color Range definitions. These are ranges of color that will be sliced up and generate gradually changing color patterns 
  based on the ranges between the start and end points.
  ```
  DefaultGenerator:
  - characterSpecific: false
    startColorDef:
      hue: 0.07130837
      saturation: 0.05205864
      value: 0.01721987
    endColorDef:
      hue: -0.07488244
      saturation: 0.09342755
      value: -0.1008582
    rangeStart: 1
    rangeEnd: 15
  ```
  In this example, level ranges for default colors from level 1 to 15 will form a range of colors from hue 0.07130837 -> -0.07488244 etc.
  If you want to see the output from a generator you can enable the debug flag to dump the generated colorization config to a file. 
  You can use this to hand-pick color values etc.

### Modifiers
Maybe you've tried out CLLC's modifiers, or Monster Modifiers? Both really add variety to the game that is much needed.
Star Levels Systems modifiers are designed to be extremely flexible, both in configuration- but also in effect.

There are however a number of modifiers and it can be a bit unclear what each does. So to start off here is a table of all current modifiers and how they work.

Modifiers are split into three categories. `Boss`, `Major`, and `Minor`. Which allows customization into the random selection process for modifiers, 
along with seperate tuning for modifiers that appear on bosses vs minor creatures.

#### Boss Modifiers
These modifiers are by default only available on bosses.

---
|   Modifier   |                                           Description                                          |                                                    Config Adjustment                                                   | Icon |
|:------------:|:----------------------------------------------------------------------------------------------:|:----------------------------------------------------------------------------------------------------------------------:|:----:|
| BossSummoner |              Summons minion creatures at regular intervals up to a certain limit.              | BasePower = Max number summoned PerlevelPower = Time between summon BiomeObjects = Biome specific minion (prefab name) |      |
|   SoulEater  | Creature grows in strength and size when other creatures die near it. Creature heals slightly. |                                    PerlevelPower = how much health & damage to gain                                    |      |
|   LifeLink   |      Creature will redirect a portion of damage it takes to another creature in the area.      |            BasePower = Base damage redirection PerLevelPower = Additional damage redirection based on level            |      |
| ResistPierce | Reduces damage the creatures takes from all pierce sources (like arrows)                       |        BasePower = Base damage reduction PerLevelPower = Additional damage reduction granted per creature level        |      |
| Brutal       | Increases creature attack speed                                                                |      BasePower = Increases attack speed by base amount PerLevelPower = Increases attack speed by amount per level      |      |


#### Major Modifiers
These modifiers are attainable by most creatures, and are typically the more impactful modifiers.

---
|   Modifier   |                Description                |                                                                                                     Config Adjustment                                                                                                     | Icon |
|:------------:|:-----------------------------------------:|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|:----:|
|    Brutal    |      Increases creature attack speed      |                                                                   BasePower = Increases attack speed PerLevelIncrease = Increases attack speed per level                                                                  |      |
|     Fire     |              Adds Fire damage             |                                                             BasePower = Percentage of total damage added PerLevelIncrease = Additional damage added per level                                                             |      |
|     Frost    |             Adds Frost Damage             |                                                             BasePower = Percentage of total damage added PerLevelIncrease = Additional damage added per level                                                             |      |
|    Poison    |             Adds Poison Damage            |                                                             BasePower = Percentage of total damage added PerLevelIncrease = Additional damage added per level                                                             |      |
|   Lightning  |           Adds Lightning damage           |                                                             BasePower = Percentage of total damage added PerLevelIncrease = Additional damage added per level                                                             |      |
|   Splitter   | Creature spawns replacements when it dies | Config values are combined and for each whole value, an additional creature is spawned BasePower = Number of creatures to spawn on replacement PerLevelIncrease = Additional value added to give a chance for more spawns |      |
| ResistPierce |      Reduces damage taken from Pierce     |                                                                 BasePower = base damage reduction PerLevelIncrease = per level additional damage reduction                                                                |      |
|  ResistSlash |      Reduces damage taken from Slash      |                                                                 BasePower = base damage reduction PerLevelIncrease = per level additional damage reduction                                                                |      |
|  ResistBlunt |      Reduces damage taken from Blunt      |                                                                 BasePower = base damage reduction PerLevelIncrease = per level additional damage reduction                                                                |      |

#### Minor Modifiers
These modifiers are attainable by most creatures and are typically less directly impactful than others, but can be none the less dangerous.

---
|   Modifier   |                                 Description                                 |                                                     Config Adjustment                                                     | Icon |
|:------------:|:---------------------------------------------------------------------------:|:-------------------------------------------------------------------------------------------------------------------------:|:----:|
|   FireNova   |                 Explodes when it dies, damaging everything.                 | BasePower = Base damage of the explosion, based on creature damage PerLevelPower = Increase to the damage added per level |      |
|   Lootbags   | Doubles creature health and gives 25% more movement speed, drops more loot. |                      BasePower = Increases drops PerLevelPower = Increases drops by amount per level                      |      |
|     Alert    |                       Increases creature hearing range                      |                   BasePower = Increase hearing by PerLevelPower = Increase hearing by amount each level                   |      |
|      Big     |                      Increases creature size and health                     |                 BasePower = Increases health and size PerLevelPower = Increases health and size by amount                 |      |
|     Fast     |                           Increases creature speed                          |                    BasePower = Increases movement speed PerLevelPower = Increases speed based per level                   |      |
| StaminaDrain |                  Attacks by the creature drain your stamina                 |                        BasePower = Drain amount PerLevelPower = Drain increase by amount per level                        |      |
|   EitrDrain  |                   Attacks by the creature drain your Eitr                   |                        BasePower = Drain amount PerLevelPower = Drain increase by amount per level                        |      |

### Localization
Localization is available for everything in the mod. I accept community translations! If you would like to contribute localizations or improve them please reach out on discord.

Otherwise localizations are available at `Bepinex/config/StarLevelSystems/localizations`, new languages can be made using any of [Jotunns language specific names](https://valheim-modding.github.io/Jotunn/data/localization/language-list.html)

## Some of my other mods
- [Valheim Armory](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimArmory/) - Fill in vanilla weapon gaps with fitting weapons
- [Impactful Skills](https://thunderstore.io/c/valheim/p/MidnightMods/ImpactfulSkills/) - Make your skills meaningful
- [Deathlink](https://thunderstore.io/c/valheim/p/MidnightMods/Deathlink/) - Death choices for all your players with progression
- [InfiniteFire](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimInfiniteFire/) - Torches/Fires configurably dont require fuel
- [Valheim Fortress](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimFortress/) - Build a base, defend it, reap the rewards!
- [Recipe Manager](https://thunderstore.io/c/valheim/p/MidnightMods/RecipeManager/) - Configure Recipes, build pieces, and conversions
- [Epic Jewels](https://thunderstore.io/c/valheim/p/MidnightMods/EpicJewels/) - More Jewelcrafting gem options

I also directly contribute to Epic Loot! If you like new features and bugfixes always happy to hear feedback.

## Installation (manual)
Modded Valheim requires Bepinex to load mods. If you have not modded before or are trying to simplify how easy it is for you to mod the game I recommend taking a look at a mod manager.
[Gale](https://thunderstore.io/c/valheim/p/Kesomannen/GaleModManager/) is an excellent mod manager. Download it manually, install and start it up.

If you are proceeding manually you will need to ensure that you have installed [Bepinex from the Thunderstore](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/), it has required configuration.

Once you are ready to install mods, they must be unzipped first and go into the `Bepinex/plugins` folder.

- Download and install [Yaml.net](https://thunderstore.io/c/valheim/p/ValheimModding/YamlDotNet/) and [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)
- Download this mod and install it!

## Planned Features
This mod is still in active development and is not considered complete yet.

Planned Features
- Comprehensive API
- Refinement to the existing modifers
- New modifiers!
- Level scaling more things
- Draw rings based on distance scaling from center
- Generic and biome specific loot multipliers
- A Nemesis or mini-boss generation system
