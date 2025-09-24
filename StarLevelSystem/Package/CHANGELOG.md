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