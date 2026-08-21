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
if (StarLevelSystem.API.IsAvailable) {
	// API is available, safe to use
	}
```

To set a creatures star level:
```csharp
StarLevelSystem.API.SetCreatureLevel(Character creature, int newLevel);
```

To modify a creatures attributes:
```csharp
int attribute = 0; // 0 = Health, 1 = Stamina, 2 = Mana, 3 = CarryWeight, 4 = Damage, 5 = Armor
// Gets the current base health of the creature, this might already be modifier by other effects
float basehealth = StarLevelSystem.API.GetCreatureBaseAttribute(Character creatureId, attribute);
basehealth *= 1.5f; // Increase base health by 50%
// Sets the new base health of the creature in the cache
StarLevelSystem.API.SetCreatureBaseAttribute(Character creatureId, attribute, basehealth);
// Applies the changes to the creature
StarLevelSystem.API.ApplyCreatureUpdates(Character creatureId);
```

To Add an existing creature modifier to a creature:
```csharp
string modifierName = "Lootbags";
int modifierType = 0; // 0 = Major, 1 = Minor, 2 = Boss
StarLevelSystem.API.AddModifierToTargetCreature(Character creatureId, modifierName, modifierType, bool update = true);
```

To add a new custom creature modifier to the modifier system.
```csharp
StarLevelSystem.API.AddNewModifier(

);
```
---

## Location Resets

Star Level System can restore looted locations, dungeons, ore, pickables and vegetation on a
schedule. This part of the API lets your mod register its own targets for that sweep, ask for a
reset directly, and find out when something was last reset.

Guard on `SupportsLocationReset`, not just `IsAvailable` — the latter only proves Star Level System
is installed, not that it is new enough to have these methods:

```csharp
if (StarLevelSystem.API.SupportsLocationReset) {
    // safe to call anything below
}
```

### Server-side only

Resetting world content means destroying and recreating ZDOs, which only the server may do. The
invoke and query calls return a refusal on a client — there is no client-to-server relay.

**Registration is the exception and is safe to call anywhere, including from your `Awake`.** It only
touches in-memory state, and it is deliberately not gated on `ZNet` existing: your plugin's `Awake`
may run before Star Level System's, so gating it would make whether your targets register at all
depend on BepInEx load order. A registration made before a world loads is applied when one does.

### Registering a target

```csharp
StarLevelSystem.API.RegisterLocationReset(
    prefabName: "MyCustomCrypt",
    sourceId: MyPlugin.PluginGUID,   // used in logs, and to scope unregistration
    resetHours: 48f,                 // or use resetSchedule instead
    resetTerrain: true,
    extraTerrainRadius: 16f);

// Cron works too, and wins over resetHours. Five fields, server local time.
StarLevelSystem.API.RegisterLocationReset("MyBossArena", MyPlugin.PluginGUID,
    resetSchedule: "0 3 * * *");     // every day at 03:00
```

The target then joins the normal background sweep. Its timer rides on the location's own world data
(the `LocationProxy` ZDO), so it survives losing the reset state file, and it is subject to the same
biome and distance-band rate multipliers, the same protection scan, and the same throughput limits
as anything the server owner configured by hand.

**The server owner always wins.** Settings resolve in the order:

| Priority | Layer | Notes |
|---|---|---|
| 1 | `Locations:` / `Vegetation:` entry in the yaml | Adding a key for your prefab name replaces your registration entirely — this is how an owner switches your target off |
| 2 | A `ResetGroups:` group listing your prefab | |
| 3 | Your registration | |
| 4 | `Defaults:` | |

Protection rules — what player-built content blocks a reset — are the owner's alone and cannot be
set through this API by design.

A registration under 0.25 hours is refused: that value becomes the sweep's *global* examination
floor, so an over-eager interval would make the server re-examine every zone in the world that often.

`sls-loc-api` on the server console lists every API-registered target, who registered it, and
whether the owner's config is overriding it.

### Resetting on demand

```csharp
StarLevelSystem.API.ResetNamedLocation("Crypt2", player.transform.position, radius: 128f,
    safety: 1,                       // 1 = Force, 0 = Safe (the default)
    onComplete: result => {
        bool ok = (bool)result["completed"];
        int rebuilt = (int)result["locationsRebuilt"];
    });
```

Unlike the background sweep, this works on a location the server has not configured for resets at
all — you named it, so it is reset. Locations that can never be reset (the starting temple) are
still refused.

`ResetLocationsInRadius` does the same for everything the owner *has* configured within a radius.

Both return `bool` — whether the request was accepted. **A `false` return means nothing was started
and `onComplete` will never fire**; the reason is written to the server's Location Reset log. Only
one manual reset runs at a time, so a second request while one is in flight is refused rather than
queued.

### Safety

| `safety` | Behaviour |
|---|---|
| `0` — Safe (default) | Waits for players to leave the affected chunks, then resets. Gives up after `safeWaitSeconds` (default 300) and touches nothing, reporting `outcome: "deferred"`. |
| `1` — Force | Resets immediately, working on chunks already loaded around a player. |

Player-built structures block a reset in **both** modes, and there is no way to override that.

Force is the mode to use when you want a location restored just before somebody walks back into it —
Valheim keeps the chunks around every player loaded, so Safe would simply never fire there.

> **Be careful with Force on a dungeon somebody is inside.** Interiors are rebuilt from scratch, and
> they sit 5000m above the surface — a player standing in one when it is reset will fall.

### Finding out when something was last reset

```csharp
long when = StarLevelSystem.API.GetLocationLastReset("Crypt2", position);
// -1 = no location of that name in range, 0 = never reset, otherwise Unix seconds UTC

double due = StarLevelSystem.API.GetSecondsUntilLocationReset("Crypt2", position);
// 0 = due now, -1 = unknown or not configured

var info  = StarLevelSystem.API.GetLocationResetInfo("Crypt2", position);
var chunk = StarLevelSystem.API.GetChunkResetInfo(position);
```

`GetLocationResetInfo` reports `found`, `lastResetUnix`, `secondsUntilDue`, `dueNow`, `enabled`,
`source`, `schedule`, and the location's position and distance. Always check `found` first.

`GetChunkResetInfo` reports on a map chunk: `tracked`, `lastExaminedUnix`, `deferredUntilUnix`,
`protectionBlocked` / `protectionReason`, and the chunk's location and its timestamp.

> **`lastExaminedUnix` is when the sweep last *looked* at the chunk, not when anything in it was
> reset.** Most chunks in a world carry a recent examination stamp and have never had a thing reset
> in them. For an actual reset time, read `locationLastResetUnix` or call `GetLocationLastReset`.
> When a chunk has been deferred — a player build is blocking it, say — `lastExaminedUnix` is `0`
> and `deferredUntilUnix` carries the time it comes back up for consideration.

### Result summary keys

`onComplete` receives a `Dictionary<string, object>` of plain values:

| Key | Type | Meaning |
|---|---|---|
| `completed` | bool | The reset ran to the end |
| `outcome` | string | `completed`, `deferred` or `failed` |
| `reason` | string | Empty on success, otherwise why it stopped |
| `target` | string | The location name, empty for a radius reset |
| `zonesConsidered` / `zonesReset` / `zonesBlocked` | int | Chunks looked at, reset, and refused by the protection scan |
| `zonesUngenerated` / `zonesAdopted` | int | Never-generated chunks, and chunks worked on while loaded |
| `locationsRebuilt` / `locationsTerrainOnly` / `locationsSkipped` | int | |
| `locationNames` | `List<string>` | What was actually rebuilt |
| `objectsCleared` / `objectsSpawned` / `vegetationObjects` | int | |
| `pickablesRefreshed` / `mineRocksRefreshed` / `containersRefreshed` | int | |
| `terrainReverted` / `doorsSealed` | int | |
| `zdoGrowth` | int | Net world-object change. A faithful restore is 0 |
| `waitedSeconds` / `elapsedSeconds` | float | Time spent waiting in Safe mode, and in total |
| `zones` | `List<Dictionary<string, object>>` | Per-chunk detail, only when `includeDetail: true` |
