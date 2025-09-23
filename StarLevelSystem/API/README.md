# Star Level System - API

## Overview

This API provides access to the internal creature detail cache used by Star Level System.
It allows reading, modifying, and managing creature cache entries through reflection.

In order to use this API copy the API folder into your project and set a soft reference to the Star Level System assembly.
This gets added to your plugin class as an annotation:
```
[BepInDependency("MidnightsFX.StarLevelSystem", BepInDependency.DependencyFlags.SoftDependency)]
```

## Usage

To check if the API is available:
```csharp
if (StarLevelSystem.API.StarLevelSystemAPI.IsAvailable) {
	// API is available, safe to use
	}
```

To set a creatures star level:
```csharp
StarLevelSystem.API.StarLevelSystemAPI.SetCreatureLevel(Character creature, int newLevel);
```

To modify a creatures attributes:
```csharp
int attribute = 0; // 0 = Health, 1 = Stamina, 2 = Mana, 3 = CarryWeight, 4 = Damage, 5 = Armor
// Gets the current base health of the creature, this might already be modifier by other effects
float basehealth = StarLevelSystem.API.StarLevelSystemAPI.GetCreatureBaseAttribute(Character creatureId, attribute);
basehealth *= 1.5f; // Increase base health by 50%
// Sets the new base health of the creature in the cache
StarLevelSystem.API.StarLevelSystemAPI.SetCreatureBaseAttribute(Character creatureId, attribute, basehealth);
// Applies the changes to the creature
StarLevelSystem.API.StarLevelSystemAPI.ApplyCreatureUpdates(Character creatureId);
```