StarLevelSystem

[img]https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded/blob/master/art/TitleHeader.png?raw=true[/img]

Star Level Systems expands upon the Valheim star system and allows extensive customization.

Features:
[list]
[*]Expand Creature Star levels (as high as you want)
[list]
[*]Sizing configuration for all creatures and their equipment[/*]
[/list]
[/*]
[*]Fine grained control of all creature aspects
[list]
[*]Health, damage, size, speed, attack speed, base and per level[/*]
[*]Resistance or weakness to all elements[/*]
[/list]
[/*]
[*]Unique colorization for any creature
[list]
[*]Per level colorization for any creature[/*]
[*]Bulk color generators for defining whole ranges of color levels[/*]
[/list]
[/*]
[*]Unique Modifiers for all creatures, multiple different categories (Boss, Major, Minor)
[list]
[*]Creatures names based on modifiers[/*]
[*]Star Icons based on modifiers[/*]
[*]Visual effects for modifiers[/*]
[*]Per level, fine grained configuration and tuning of modifiers[/*]
[/list]
[/*]
[*]Scaling, fine-grained control and configuration of all creature drops
[list]
[*]scale individual loot entries differently[/*]
[*]modify chance drops based on level of the creature[/*]
[*]Add, remove, change all drops[/*]
[/list]
[/*]
[*]Scaling of the world around you
[list]
[*]level up the FISH![/*]
[*]level scaled BIRDS[/*]
[*]level scaled TREES[/*]
[/list]
[/*]
[/list]

Got a bug to report or just want to chat about the mod? Drop by the discord or github.

[url=https://discord.gg/Dmr9PQTy9m][img]https://i.imgur.com/uE6umQE.png[/img][/url] [url=https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded][img]https://i.imgur.com/lvbP5OF.png[/img][/url]

Below are a few examples of what you might see, and what the mod can do.

[img]https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded/blob/master/art/Header.png?raw=true[/img]

[size=5][b]Features[/b][/size]

NOTE: until nexus supports markdown fully it is recommended to view the readme on the [url=https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded?tab=readme-ov-file#starlevelsystem]github[/url]

[size=4][b]Levels, Levels and more Levels (LevelSettings.yaml)[/b][/size]
So you want creatures to have more levels, but you don't want to die instantly to a level 100 boar when you start the game?
Well have I got the config file for you.

Level settings allows configuration of creature levelup chance, creature stats, max level, increased level up chance based on distance from the center- and respectively biome based configs for all of that.

Want some help calculating and building a level definition? Check out [url=https://sls-levelspreadtool.netlify.app/]this tool[/url]

[b]Biome Configuration[/b]
Lets take a look at some of the things you can do with this, and what better spot to start out than the default All biome configuration (which applies to every creature by default).

Here is a section of the default example config, lets walk through what everything does.
[code]
All:
  distanceScaleModifier: 1.5         # The influence of distance ring based scale increases (1.5 means 150% of the bonus value will be applied)
  spawnRateModifier: 1.5             # Spawn rate of every creature is 50% higher (1.5), which means every spawn has a 50% chance of being 2 creatures.
  creatureBaseValueModifiers:        # These values modify the base stats of every creature in this biome (eg, all creatures)
    BaseHealth: 1                    # The default health of all creatures is 100% (1), numbers below 1 reduce health, above increase is (2) is 200% health for everything
    BaseDamage: 1                    # Default damage of all creatures is 100% (1)
    Speed: 1                         # Default movement speed of all creatures is 100% (1)
    Size: 1                          # Default size of all creatures is 100% (1)
  creaturePerLevelValueModifiers:    # Per level modifiers are applied per level, eg each star will give this value to the creature
    HealthPerLevel: 0.4              # Each star provides 40% more health (0.4)
    DamagePerLevel: 0.1              # Each star provides 10% more damage (0.1)
    SpeedPerLevel: 0                 # Each star does not increase speed (0)
    SizePerLevel: 0.1                # Each star makes the creature 10% bigger (0.1)
  damageRecievedModifiers:           # Damage reduction or increases
    Poison: 1.5                      # Everything recieves 50% (1.5) more damage from poison (that includes players)
[/code]
Note: creaturePerLevelValueModifiers do not apply to characters. But, damageRecievedModifiers DOES.

Biome specific configurations can be used to override the default All configuration, in this case max level for Ashlands is being set to 26 and the distance modifier is being reduced by 50%
[code]
AshLands:
  biomeMaxLevelOverride: 26
  distanceScaleModifier: 0.5
[/code]

[b]Creature Configuration[/b]
Creature specific configuration allows you to override what is set in the biome definition for a creature, which allows more fine-grained control of how a specific creature should be modified.

Lets take a look at this creature definition
[code]
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
    SizePerLevel: 0.005               # Each star provides 0.5% more size
  requiredModifiers:                  # Modifiers that this creature will always spawn with, regardless of level, chance or modifier limit, but still count towards max modifier count
    Poison: Major                     # Trolls will always spawn with the Poison major modifier, Modifier names can be found in Modifiers.yaml, along with their categories
[/code]

[b]Levelup Chance[/b]
This is a definition of the chance that a creature has to level up at each point.

This works hand in hand with the distance scale modifier and distanceLevelBonus, distance level bonuses are applied if the creature falls into a distance category with bonuses.
eg:
[code]
distanceLevelBonus:
  1250:
    1: 25
[/code]
Will give all creatures a +25% chance to reach the first star level, if they are at least 1250m from the center. This value is then modified based on biome settings.
In our example biome file we have a 1.5 value for the distance modifier so 1.5 x 25 = 37.5 would be the increase provided to reach level 1.

If the total bonus and base value exceeds 100 that level will be gaurenteed, every creature with that condition will be at a minimum that level.
You can see this in some of the later distance bonuses which slowly drive of the guaranteed spawn level of creatures up

[code]
5000:
  1: 100
  2: 100      # All creatures at least 5000m from center will be level 2+
  3: 75
  4: 50
  5: 25
  6: 15
[/code]

You can also use the distanceLevelBonus to add additional levels, make it so that certain distances are required to spawn higher level enemies.

[code]
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
[/code]

With the above configuation the region closest to start up to 1250m will only be able to spawn level 1 and 2 creatures. Between 1250m and 5000m creatures can spawn up to level 3, and beyond 5000m creatures can spawn up to level 4.


Now, we've walked through a lot of the bonuses to level up chance but lets take a look at the base values too.
Star level systems default config has a relatively large spawn range- which is limited by biome configuration.

defaultCreatureLevelUpChance Defines the levelup chance of all creatures, this however can be limited by biome configuration and it can be increased by other factors, such as the distance from center bonus.
[code]
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
[/code]


[size=4][b]Colorization (Colorization.yaml)[/b][/size]
In vanilla there are few creatures which can be colorized when they level up. Star Level Systems changes that.
Most all creatures can be colorized, it should be noted that some creatures (Yagluth eg) do not colorize well and the effect is generally not very noticable.

There are two different ways to apply colorization values to creatures.

[list]
[*]Creature specific color definitions. These are split between default definitions (applied to any creature, if it does not have a more specific entry) and character specific entries. These will be applied at the keyed level to the specified creature.
[code]
Greydwarf:
  1:
    hue: -0.06
    saturation: 0.1
    value: 0.05
[/code]
[/*]
[*]Color Range definitions. These are ranges of color that will be sliced up and generate gradually changing color patterns based on the ranges between the start and end points.
[code]
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
[/code]
In this example, level ranges for default colors from level 1 to 15 will form a range of colors from hue 0.07130837 -> -0.07488244 etc. If you want to see the output from a generator you can enable the debug flag to dump the generated colorization config to a file. You can use this to hand-pick color values etc.[/*]
[/list]


[size=4][b]Loot Configuration[/b][/size]
Star level systems provides full optional configuration to control in detail all loot drops. Loot configuration can be found in the LootSettings.yaml file.

You may find it useful to see what existing drops look like within the SLS system.
You can do this by dumping a debug configuration of all loot drops (as they are) with the in-game console command sls-dump-loottables, this is output to the SLS config folder in the file LootTablesDump.yaml.

There are three core parts to configuring loot drops, but many options to modify if you wish to fine tune loot configurations.

[list]
[*]characterSpecificLoot | this governs all character based loot drops[/*]
[*]nonCharacterSpecificLoot | this controls all non-character based loot drops[/*]
[*]distanceLootModifier | this allows distance scaling of loot, similar to how distance level scaling works[/*]
[/list]

[b]Character Specific Loot configuration[/b]
Lets take a look at the character specific loot drop. Here is one of the example configurations, and it is here to address a specific challenge.
Oozers create blobs when they die, this scales with the level of the creature. However, when you have a very high level oozer you can end up with 20,30 or more blobs.
Which can be impossible to deal with. This deals with that in a few ways, the final - drop: entry is for the Blob, spawning a minimum of 2 and a maximum of 2. But scaling with the level of the BlobElite (Oozer), however that scaling is capped at a maximum of 6 drops, and only scales .5 per level. So every other level another blob can be spawned up to a maximum of 6.

Another useful entry to look at here is the drop for TrophyBlob, this has a 0.1 (10%) chance of happening, but it scales x0.01 per level, so a level five is (0.01 x 5) = 0.05 (5%) higher chance of happening.
This can be combined with a drop count to scale the drops for the entry, so a level 5 could drop more trophies. But this is disabled by setting the maxScaledAmount: 1
[code]
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
[/code]

[b]Non Character specific loot configuration[/b]

This is the section which allows you to configure all of the non-creature related loot in detail. It follows a similar structure to character loot configuration.
You can use this to modify or add loot to any existing objects which have the option to drop loot. For example, falling stalagtites. This configuration gives them a chance to drop crystals.
The chance scales per level (yes inanimate objects can be leveled, tree, rock or destructible based on type). If loot configuration is not defined here then the generic configuration values from the main config file apply here PerLevelDestructibleLootScale, PerLevelMineRockLootScale, and PerLevelTreeLootScale along with their max level values.
Level scaling can be ignored by setting dontScale: true

[code]
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
[/code]

[b]Distance Loot Modifier[/b]
This is similar to the character distance modifier in that it applies a bonus to the minimum and maximum drops that an entry can have based on distance.
Loot rings are not drawn on the map, but behave exactly the same as Character distance rings (if the object is greater than x distance and less than the next once it recieves that bonus).

[code]
distanceLootModifier:
  1250:
    minAmountScaleFactorBonus: 1.02
    maxAmountScaleFactorBonus: 1.02
[/code]


[size=4][b]Modifiers[/b][/size]
Maybe you've tried out CLLC's modifiers, or Monster Modifiers? Both really add variety to the game that is much needed.
Star Levels Systems modifiers are designed to be extremely flexible, both in configuration- but also in effect.

There are however a number of modifiers and it can be a bit unclear what each does. So to start off here is a table of all current modifiers and how they work.

Modifiers are split into three categories. Boss, Major, and Minor. Which allows customization into the random selection process for modifiers, along with separate tuning for modifiers that appear on bosses vs minor creatures.

[b]Boss Modifiers[/b]
These modifiers are by default only available on bosses.
[b]⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[/b]
[list]
[*][b]BossSummoner[/b] - Summons minion creatures at regular intervals up to a certain limit.
Config: BasePower = Max number summoned, PerlevelPower = Time between summon, BiomeObjects = Biome specific minion (prefab name)[/*]
[*][b]SoulEater[/b] - Creature grows in strength and size when other creatures die near it. Creature heals slightly.
Config: PerlevelPower = how much health & damage to gain[/*]
[*][b]LifeLink[/b] - Creature will redirect a portion of damage it takes to another creature in the area.
Config: BasePower = Base damage redirection, PerLevelPower = Additional damage redirection based on level[/*]
[*][b]ResistPierce[/b] - Reduces damage the creatures takes from all pierce sources (like arrows).
Config: BasePower = Base damage reduction, PerLevelPower = Additional damage reduction granted per creature level[/*]
[*][b]Brutal[/b] - Increases creature attack speed.
Config: BasePower = Increases attack speed by base amount, PerLevelPower = Increases attack speed by amount per level[/*]
[/list]


[b]Major Modifiers[/b]
These modifiers are attainable by most creatures, and are typically the more impactful modifiers.
[b]⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[/b]
[list]
[*][b]Brutal[/b] - Increases creature attack speed.
Config: BasePower = Increases attack speed, PerLevelIncrease = Increases attack speed per level[/*]
[*][b]Fire[/b] - Adds Fire damage.
Config: BasePower = Percentage of total damage added, PerLevelIncrease = Additional damage added per level[/*]
[*][b]Frost[/b] - Adds Frost Damage.
Config: BasePower = Percentage of total damage added, PerLevelIncrease = Additional damage added per level[/*]
[*][b]Poison[/b] - Adds Poison Damage.
Config: BasePower = Percentage of total damage added, PerLevelIncrease = Additional damage added per level[/*]
[*][b]Lightning[/b] - Adds Lightning damage.
Config: BasePower = Percentage of total damage added, PerLevelIncrease = Additional damage added per level[/*]
[*][b]Elemental Chaos[/b] - Random typed elemental damage on each hit.
Config: BasePower = Percentage of total damage added, PerLevelIncrease = Additional damage added per level[/*]
[*][b]Splitter[/b] - Creature spawns replacements when it dies. Config values are combined and for each whole value, an additional creature is spawned.
Config: BasePower = Number of creatures to spawn on replacement, PerLevelIncrease = Additional value added to give a chance for more spawns[/*]
[*][b]ResistPierce[/b] - Reduces damage taken from Pierce.
Config: BasePower = base damage reduction, PerLevelIncrease = per level additional damage reduction[/*]
[*][b]ResistSlash[/b] - Reduces damage taken from Slash.
Config: BasePower = base damage reduction, PerLevelIncrease = per level additional damage reduction[/*]
[*][b]ResistBlunt[/b] - Reduces damage taken from Blunt.
Config: BasePower = base damage reduction, PerLevelIncrease = per level additional damage reduction[/*]
[/list]


[b]Minor Modifiers[/b]
These modifiers are attainable by most creatures and are typically less directly impactful than others, but can be none the less dangerous.
[b]⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[/b]
[list]
[*][b]FireNova[/b] - Explodes when it dies, damaging everything.
Config: BasePower = Base damage of the explosion, based on creature damage, PerLevelPower = Increase to the damage added per level[/*]
[*][b]PoisonNova[/b] - Explodes when it dies, damaging everything.
Config: BasePower = Base damage of the explosion, based on creature damage, PerLevelPower = Increase to the damage added per level[/*]
[*][b]Lootbags[/b] - Doubles creature health and gives 25% more movement speed, drops more loot.
Config: BasePower = Increases drops, PerLevelPower = Increases drops by amount per level[/*]
[*][b]Alert[/b] - Increases creature hearing range.
Config: BasePower = Increase hearing by, PerLevelPower = Increase hearing by amount each level[/*]
[*][b]Big[/b] - Increases creature size and health.
Config: BasePower = Increases health and size, PerLevelPower = Increases health and size by amount[/*]
[*][b]Fast[/b] - Increases creature speed.
Config: BasePower = Increases movement speed, PerLevelPower = Increases speed based per level[/*]
[*][b]Evolving[/b] - After X kills the creature gains a level.
Config: BasePower = Base number of kills required, PerLevelIncrease = Additional kills required per level[/*]
[*][b]StaminaDrain[/b] - Attacks by the creature drain your stamina.
Config: BasePower = Drain amount, PerLevelPower = Drain increase by amount per level[/*]
[*][b]EitrDrain[/b] - Attacks by the creature drain your Eitr.
Config: BasePower = Drain amount, PerLevelPower = Drain increase by amount per level[/*]
[*][b]ResistFire[/b] - Reduces damage taken from Fire.
Config: BasePower = base damage reduction, PerLevelIncrease = per level additional damage reduction[/*]
[*][b]ResistFrost[/b] - Reduces damage taken from Frost.
Config: BasePower = base damage reduction, PerLevelIncrease = per level additional damage reduction[/*]
[*][b]ResistPoison[/b] - Reduces damage taken from Poison.
Config: BasePower = base damage reduction, PerLevelIncrease = per level additional damage reduction[/*]
[*][b]ResistSpirit[/b] - Reduces damage taken from Spirit.
Config: BasePower = base damage reduction, PerLevelIncrease = per level additional damage reduction[/*]
[/list]


[size=4][b]Localization[/b][/size]
Localization is available for everything in the mod. I accept community translations! If you would like to contribute localizations or improve them please reach out on discord.

Otherwise localizations are available at Bepinex/config/StarLevelSystems/localizations, new languages can be made using any of [url=https://valheim-modding.github.io/Jotunn/data/localization/language-list.html]Jotunns language specific names[/url]


[size=4][b]Terminal Commands[/b][/size]
Star Level Systems provides a number of terminal commands for debugging and testing and cleanup.
[list]
[*][b]sls-killall [range:500][/b] - kills creatures within the specfiied range (default 500m), skips players and tamed creatures.[/*]
[*][b]sls-give-modifier [modifier_type:major] [modifier_name:fire][/b] - gives nearby creatures the specified modifier (must be very close)[/*]
[*][b]sls-dump-loottables[/b] - provides dumps all loot table configurations in the game, in SLS format, to Bepinex/config/StarLevelSystems/LootTablesDump.yaml[/*]
[/list]


[size=4][b]API Usage (WIP)[/b][/size]
Star Level Systems provides a public API for other mods to interact with.
The API currently allows reading, modifying and managing creature stat modifiers, color, and level.
Check out the API Documentation [url=https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded/tree/master/StarLevelSystem/API]here[/url]


[size=5][b]Some of my other mods[/b][/size]
[list]
[*][url=https://thunderstore.io/c/valheim/p/MidnightMods/ValheimArmory/]Valheim Armory[/url] - Fill in vanilla weapon gaps with fitting weapons[/*]
[*][url=https://thunderstore.io/c/valheim/p/MidnightMods/ImpactfulSkills/]Impactful Skills[/url] - Make your skills meaningful[/*]
[*][url=https://thunderstore.io/c/valheim/p/MidnightMods/Deathlink/]Deathlink[/url] - Death choices for all your players with progression[/*]
[*][url=https://thunderstore.io/c/valheim/p/MidnightMods/ValheimInfiniteFire/]InfiniteFire[/url] - Torches/Fires configurably dont require fuel[/*]
[*][url=https://thunderstore.io/c/valheim/p/MidnightMods/ValheimFortress/]Valheim Fortress[/url] - Build a base, defend it, reap the rewards![/*]
[*][url=https://thunderstore.io/c/valheim/p/MidnightMods/RecipeManager/]Recipe Manager[/url] - Configure Recipes, build pieces, and conversions[/*]
[*][url=https://thunderstore.io/c/valheim/p/MidnightMods/EpicJewels/]Epic Jewels[/url] - More Jewelcrafting gem options[/*]
[/list]

I also directly contribute to Epic Loot! If you like new features and bugfixes always happy to hear feedback.


[size=5][b]Installation (manual)[/b][/size]
Modded Valheim requires Bepinex to load mods. If you have not modded before or are trying to simplify how easy it is for you to mod the game I recommend taking a look at a mod manager.
[url=https://thunderstore.io/c/valheim/p/Kesomannen/GaleModManager/]Gale[/url] is an excellent mod manager. Download it manually, install and start it up.

If you are proceeding manually you will need to ensure that you have installed [url=https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/]Bepinex from the Thunderstore[/url], it has required configuration.

Once you are ready to install mods, they must be unzipped first and go into the Bepinex/plugins folder.

[list]
[*]Download and install [url=https://thunderstore.io/c/valheim/p/ValheimModding/YamlDotNet/]Yaml.net[/url] and [url=https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/]Jotunn[/url][/*]
[*]Download this mod and install it![/*]
[/list]


[size=5][b]Compatibility[/b][/size]
[list]
[*]This mod is incompatible with Creature Level and Loot Control (they do the same things)[/*]
[/list]

Compatibility is being worked on for the following mods:
[list]
[*]CarryMeMaster[/*]
[*]PortablePals[/*]
[/list]


[size=5][b]Planned Features[/b][/size]
This mod is still in active development and is not considered complete yet.

Planned Features
[list]
[*]Refinement to the existing modifers[/*]
[*]New modifiers![/*]
[*]Level scaling more things[/*]
[*]Generic and biome specific loot multipliers[/*]
[*]A Nemesis or mini-boss generation system[/*]
[*]World leveling system based on boss kills[/*]
[/list]
