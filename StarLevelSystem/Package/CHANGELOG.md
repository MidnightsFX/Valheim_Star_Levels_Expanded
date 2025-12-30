**0.14.6**
 ---
 ```
 - Consistency improvements for delayed growth setup for bred creatures
 - Recoloring of overleveled creatures that get rerolled
 ```

**0.14.5**
 ---
 ```
 - Simplifies Modifier method setup calls
 - Ensure creature modifier prefixes are limited properly
 - Ensure creature modifiers are rolled by zowner or secondary
 ```

**0.14.4**
 ---
 ```
 - Fixes cache reset before znet has updated character level
 - Fixes level on-change running during loading
 - Fixes children exploding instead of growing up
 - Fixes Fish and Bird onchange settings running too early when connecting to a server
 ```

**0.14.3**
 ---
 ```
 - Fixes drops being left behind by creatures selected for deletion
 - Fixes spawned creatures not having their levels set correctly in some cases
 ```

**0.14.2**
 ---
 ```
 - Significantly improves cache updates for networked changes to creature modifiers and levels
 - Force rerolling of creatures above the maximum level will now also resize them to the correct size
 - Fixes a race condition where tamed breeding creature would not inherit the correct level
 - Fixes and issue where splitters would not always inherit the correct level from the parent creature
 ```

**0.14.1**
 ---
 ```
 - Fixes an issue where deterministic tree scaling would result in no wood if the tree rolled level 0
 - Added in-game size-rescaling for trees, fish, and birds, changing size configurations will now automatically rescale
 ```

**0.14.0**
 ---
 ```
 - Improves UI synchronziation for creature modifier names
 - Fixes an error when Lifelink triggers
 - Changes configuration for Trees, Birds and Fish to have their own config section
   - Trees now level up primarily based on distance to spawn/center of the world.
   - Fish size is now reduced
 ```

**0.13.0**
 ---
 ```
 - Added a configuration option to force-reroll creatures that are over the specified max level when loaded
 - Enables Map Ring redraw/removal when setting is changed
 - Added more safety checks to BossSummoner
 - Fixes an issue where characters would not get size increases
 - Re-implements character client side cache
 - Modifiers Updated (please delete your Modifiers.yaml)
	- Changes Modifier name generation to be deterministic, removes multiple prefix and postfix options
	- Updated Modifier configuration with user important details being centric
 ```

**0.12.1**
 ---
 ```
 - Logged detailed scaling changes for damage per level
 - Fix splitter not splitting when using fallbacks
 - More cache invalidation for UI related and setup changes
 - Fix size scale setting weapon sizes to zero before a creature has scale data
 - Fixed Character specific level tables not being used if a biome table was available
 ```

**0.12.0**
 ---
 ```
 - Fixes an issue where resistant creatures would be immune to damage (does not apply to creatures that have already rolled this modifier)
 - Improves modifier and level consistency across players with variable connection speeds and latencies
 - Provides more information for damage recieved and dealt modifiers, can be enabled/disabled seperately in the config (per client)
 - Changed a number of base values in the modifiers configuration, it is recommended you delete your configuration
 - Some modifiers no longer run regularly and instead are setup once, again required that you regenerate your configuration (delete Modifiers.yaml)
 - Tuning
	- Nerfed the boss modifier for resist pierce to be 25% resistance plus 2% per level
	- Buffed Lootbags to provide more loot, also makes the creature slightly higher health and move faster
	- Capped resistance modifiers at 80% resistance, nerfed default resistance values
	- Increased the delay for the boss affix summoner
	- Significantly reduced brutal speed modifiers (some old configs had these at 100%+ increases)
	- Improved fallback logic for Splitter
 ```

**0.11.8**
 ---
 ```
 - Improves cache consistency between clients
 - Provides configuration to allow tames to pass modifiers to child creatures
 - Allows configuring tame modifier inheritence
 - Ensures children maintain their modifiers when growing up
 ```

**0.11.7**
 ---
 ```
 - Asynchronous creature checks and fallback for creature setup
 - Fixes server error when attempting to build minimap rings
 ```

**0.11.6**
 ---
 ```
 - Fixes baby creatures not inheriting level when not using randomized baby levels
 ```

**0.11.5**
 ---
 ```
 - Adds safety checks when removing all level definitions
 ```

**0.11.4**
 ---
 ```
 - Expands DistanceScaleModifier to also work when applied to specific creatures
 ```

**0.11.3**
 ---
 ```
 - Fixes character specific level settings not always overriding default level settings
 - Provides a way to set force level control for specific creatures
	- Training dummy is default included in this
 - Expanded caching of short term character entries to prevent constant recalculation
 - Fixed tame level settings not being under the correct category
 ```

**0.11.2**
 ---
 ```
 - Fixes spawn levels not being set for creatures created from loot table drops
 - Fixed level not always being accounted for in loot table drop calculations
 - Set default custom loot drops for Oozers (blobElite)
	- No longer spawns 2 blobs per level, now spawns up to 6 blobs, moderately scaling by level
	- Delete your CreatureLootSettings.yaml if you want the new default
 ```

**0.11.1**
 ---
 ```
 - Fixes distance calculations for dungeons incorrectly accounting for height
 ```

**0.11.0**
 ---
 ```
 - Adds Ring drawing for spawn distance modifiers for visualization
  - This is toggleable via config, and can be shown/hidden per player from the map itself
  - Color configuration for all rings
  - Rings are automatically redrawn when configuration changes
  - Retuned all of the default distance modifiers to allow slightly more regular difficulty increases, along with much higher star levels
	- Delete your config (LevelSettings.yaml) if you want the new default
 - Fixes an error when trying to spawn null creatures
 - Improves spawning not leveling up creatures from certain spawners
 ```

**0.10.2**
 ---
 ```
 - Compatibility improvements for spawner level control when the cache can't be built
 - Improves compatibility for mods that break or remove spawner effects
 ```

**0.10.1**
 ---
 ```
 - Fixed a bug (from 0.10.0) which prevented many of the random world spawns from happening
 - Tuned level up table to have more staggered steps towards the higher levels
 - Disabled a few more optional debug logs
 - Added back in default loot table modifications for greydwarves to drop greydwarf eyes
 - "Fixed" a feature where extremely high level creatures with many modifiers would always spawn with splitter, and multiply on kills
 ```

**0.10.0**
 ---
 ```
 - Adds night-time specific configuration
	- Disable certain spawns at night, per creature/biome (disable night spawns caused by boss kills etc)
	- Modify spawn rates at night, per creature/biome
	- Modify level scales at night, per creature/biome (higher or lower chances of high level creatures)
 - Fixes a bug where creature items would be twice as big as intended
 - Adds configuration options to controll which spawners SLS controls (now defaults manual spawns to not be controlled)
	- This improves support for mods which manually spawn creatures
 - Updates default level scales to be more aggressive and increase weight further from center
 - Disabled some optional debug logging
 - Rebalances default level settings configuration
 ```

**0.9.6**
 ---
 ```
 - Fixes lifelink always applying a damage reduction, now only applies damage reduction if there is a target to redirect damage to
 - Adds: BiomeMinLevelOverride and CreatureMinLevelOverride, which will ensure creatures spawn at least at the specified level
 ```

**0.9.5**
 ---
 ```
 - Colorization configuration (allow skipping colorization)
 - API fix for colorization not applying correctly
 ```

**0.9.4**
 ---
 ```
 - Improves configurability of local player damage and health scaling
 - Fixes frost modifier effect for Linux
 - Reduced default spawn rate config
 ```

**0.9.3**
 ---
 ```
 - Limits the number of modifiers that can be applied due to star level for both modifier types, instead of individually
 - Prevents global and per creature configuration from stacking health modifiers
 - Per level damage modifications take the highest priority modifier only
 - Retuned many of the damage modifers to be less aggressive in their damage increases
 ```

**0.9.2**
 ---
 ```
- Ensures manually spawned creatures get a fair chance of modifiers
- Fixes CreatureLootSettings.yaml not being live reloaded after edits
- Added a global exclusion list for modifiers that will apply to all creatures, defaults to just TWIG
	- Delete your config (Modifiers.yaml) if you want the new default
- Fixes modifier configuration not being reloaded on startup
- Removed the immediate explosion from FireNova, it now only has the 1 second delayed explosion
 ```

**0.9.1**
 ---
 ```
 - Fixes NPE when no loot configuration is defined
 - Fixes NPE when trying to add a modifier that does not exist
 - Improves support for huge numbers of modifiers on creatures
 - Adds API functions to add modifiers to creatures
 ```

**0.9.0**
 ---
 ```
 - Partial API support, manipulation of creature levels, color, attributes, damage, damage recived
 - Reduces spawn multiplier checks during race conditions
 - Prevents UI errors when creatures have duplicated modifiers
 ```

**0.8.4**
 ---
 ```
 - Compatibility improvements for mods with weaponless characters
 - Adds a config option to limit the number of modifiers that a creature gets to its star level in addition to the max number of modifiers
 - Updated name generation to always add available modifiers
 - Added a config option to avoid spawn multiplying boss creatures
 ```

**0.8.3**
 ---
 ```
 - Compatibility improvement with mods that manipulate or add star entries
 ```

**0.8.2**
 ---
 ```
 - Fix for splitting tames not spawned tamed minions
 - NPE fix for creatures death before setup
 - Config spelling fix for LootDropCalculationType
 ```

**0.8.1**
 ---
 ```
 - Adding incompatibility with CLLC
 - Improving compatibility for spawned child creatures multiplied by biome multipliers
 - Improving multiplier compatibility for spawn command and boss spawns
 - Adding initial translations for 26 languages
 ```

**0.8.0**
 ---
 ```
 - Public release
 ```