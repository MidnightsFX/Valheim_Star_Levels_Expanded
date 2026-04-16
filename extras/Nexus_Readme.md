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
[img]https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded/blob/master/art/Header.png?raw=true[/img]Features
Levels, Levels and more Levels (LevelSettings.yaml)So you want creatures to have more levels, but you don't want to die instantly to a level 100 boar when you start the game? Well have I got the config file for you.Level settings allows configuration of creature levelup chance, creature stats, max level, increased level up chance based on distance from the center- and respectively biome based configs for all of that.Biome ConfigurationLets take a look at some of the things you can do with this, and what better spot to start out than the default All biome configuration (which applies to every creature by default).Here is a section of the default example config, lets walk through what everything does.

NOTE: until nexus supports markdown fully it is recommended to view the readme on the [url=https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded?tab=readme-ov-file#starlevelsystem]github[/url]

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

Creature Configuration

Creature specific configuration allows you to override what is set in the biome definition for a creature, which allows more fine-grained control of how a specific creature should be modified.
Lets take a look at Eikthyr

[code]
Eikthyr:                              # The prefab of the creature to modify, this can be any valid creature.
  creatureMaxLevelOverride: 4         # The maximum level that this creature can level up to, regardless of biome
  creaturePerLevelValueModifiers:     # Creature base and per level modifiers are supported here, just like the ones defined in Biome configuration
    HealthPerLevel: 0.3               # 30% (0.3) more health per level
    DamagePerLevel: 0.05              # 5% (0.05) more damage per level
    SizePerLevel: 0.07                # 7% (0.07) larger per level
[/code]

Levelup Chance

This is a definition of the chance that a creature has to level up at each point.
This works hand in hand with the distance scale modifier and distanceLevelBonus, distance level bonuses are applied if the creature falls into a distance category with bonuses. eg:

[code]
distanceLevelBonus:
  1250:
    1: 0.25
[/code]

Will give all creatures a +25% chance to reach the first star level, if they are at least 1250m from the center. This value is then modified bast on biome settings. In our example biome file we have a 1.5 value for the distance modifier so 1.5 x 0.25 = 0.375 would be the increase provided to reach level 1.
If the total bonus and base value exceeds 1.0 that level will be gaurenteed, every creature with that condition will be at a minimum that level. You can see this in some of the later distance bonuses which slowly drive of the guarnteed spawn level of creatures.

[code]
5000:
  1: 1
  2: 1      # All creatures at least 5000m from center will be level 2+
  3: 0.75
  4: 0.5
  5: 0.25
  6: 0.15
[/code]

Now, we've walked through a lot of the bonuses to level up chance but lets take a look at the base values too. Star level systems default config has a relatively large spawn range- which is limited by biome configuration.
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

Colorization (Colorization.yaml)

In vanilla there are few creatures which can be colorized when they level up. Star Level Systems changes that. Most all creatures can be colorized, it should be noted that some creatures (Yagluth eg) do not colorize well and the effect is generally not very noticable.
There are two different ways to apply colorization values to creatures.
[list]
[*]Creature specific color definitions.  These are split between default definitions (applied to any creature, if it does not have a more specific entry)  and character specific entries. These will be applied at the keyed level to the specified creature.
[code]
Greydwarf:
  1:
    hue: -0.06
    saturation: 0.1
    value: 0.05
[/code]
[/*]
[*]Color Range definitions. These are ranges of color that will be sliced up and generate gradually changing color patterns  based on the ranges between the start and end points.
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
In this example, level ranges for default colors from level 1 to 15 will form a range of colors from hue 0.07130837 -> -0.07488244 etc. If you want to see the output from a generator you can enable the debug flag to dump the generated colorization config to a file.  You can use this to hand-pick color values etc.[/*]
[/list]

Modifiers

Maybe you've tried out CLLC's modifiers, or Monster Modifiers? Both really add variety to the game that is much needed. Star Levels Systems modifiers are designed to be extremely flexible, both in configuration- but also in effect.
There are however a number of modifiers and it can be a bit unclear what each does. So to start off here is a table of all current modifiers and how they work.
Modifiers are split into three categories. Boss, Major, and Minor. Which allows customization into the random selection process for modifiers, along with seperate tuning for modifiers that appear on bosses vs minor creatures.
Boss Modifiers

These modifiers are by default only available on bosses.
[b]⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[/b]
BossSummoner - Summons minion creatures at regular intervals up to a certain limit.
SoulEater - Creature grows in strength and size when other creatures die near it. Creature heals slightly
LifeLink - Creature will redirect a portion of damage it takes to another creature in the area
ResistPierce - Reduces damage the creatures takes from all pierce sources (like arrows)
Brutal - Increases creature attack speed


Major Modifiers

These modifiers are attainable by most creatures, and are typically the more impactful modifiers.
[b]⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[/b]


Minor Modifiers

These modifiers are attainable by most creatures and are typically less directly impactful than others, but can be none the less dangerous.
[b]⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯[/b]

Localization

Localization is available for everything in the mod. I accept community translations! If you would like to contribute localizations or improve them please reach out on discord.
Otherwise localizations are available at Bepinex/config/StarLevelSystems/localizations, new languages can be made using any of [url=https://valheim-modding.github.io/Jotunn/data/localization/language-list.html]Jotunns language specific names[/url]
Some of my other mods

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
Installation (manual)

Modded Valheim requires Bepinex to load mods. If you have not modded before or are trying to simplify how easy it is for you to mod the game I recommend taking a look at a mod manager. [url=https://thunderstore.io/c/valheim/p/Kesomannen/GaleModManager/]Gale[/url] is an excellent mod manager. Download it manually, install and start it up.
If you are proceeding manually you will need to ensure that you have installed [url=https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/]Bepinex from the Thunderstore[/url], it has required configuration.
Once you are ready to install mods, they must be unzipped first and go into the Bepinex/plugins folder.
[list]
[*]Download and install [url=https://thunderstore.io/c/valheim/p/ValheimModding/YamlDotNet/]Yaml.net[/url] and [url=https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/]Jotunn[/url][/*]
[*]Download this mod and install it![/*]
[/list]

Planned Features

This mod is still in active development and is not considered complete yet.
Planned Features
[list]
[*]Comprehensive API[/*]
[*]Refinement to the existing modifers[/*]
[*]New modifiers![/*]
[*]Level scaling more things[/*]
[*]Draw rings based on distance scaling from center[/*]
[*]Generic and biome specific loot multipliers[/*]
[*]A Nemesis or mini-boss generation system[/*]
[/list]
