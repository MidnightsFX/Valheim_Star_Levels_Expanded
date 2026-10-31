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
int attribute = 0; // 0 = BaseHealth, 1 = BaseDamage, 2 = AttackSpeed, 3 = Speed, 4 = Size
// Gives the creature a base health of 1.5x its vanilla health, in place of what its LevelSettings
// entry gives it. Its modifiers (Big, ...) still add their share on top. Applies at once.
StarLevelSystem.API.SetCreatureBaseAttribute(Character creatureId, attribute, 1.5f);
```
See [Set values are the creature's base](#set-values-are-the-creatures-base) before reading a value,
changing it and setting it back.

To Add an existing creature modifier to a creature:
```csharp
string modifierName = "Lootbags";
int modifierType = 0; // 0 = Major, 1 = Minor, 2 = Boss
StarLevelSystem.API.AddModifierToTargetCreature(Character creatureId, modifierName, modifierType, bool update = true);
```

To give one creature its own values for a modifier:
```csharp
// This creature's Shielded holds a bigger shield that comes back sooner; every other Shielded creature is unchanged.
StarLevelSystem.API.SetCreatureModifierConfig(creature, "Shielded", new Dictionary<string, float>() {
	{ "BasePower", 0.6f },
	{ "RegenDelay", 2f },
});
```
See [Per-creature modifier values](#per-creature-modifier-values).

To add a new custom creature modifier to the modifier system.
```csharp
StarLevelSystem.API.AddNewModifier(

);
```
Give it a modifier ID of 100 or more. IDs 0-99 are reserved for Star Level System's own modifiers, and
an ID that is already in use is refused.

### Attribute changes are saved on the creature

The attribute setters (`SetCreatureBaseAttribute`, `SetCreaturePerLevelAttribute`,
`SetCreatureDamageReceivedModifier`, `SetCreatureFlatDamageBonus` and their `SetAll...` forms) are
saved on the creature's ZDO whichever machine calls them. Every later rebuild of its stats reapplies
them: another player's game, the next owner after a handoff, a reload, a Star Level System config
reload. Set a value once, when you spawn or first adjust the creature, and it sticks.

Only the machine that **owns** the creature writes its ZDO. A call from any other machine applies
there at once and is forwarded to the owner, which saves it a moment later. A creature nobody owns
is taken by the calling machine first. The same holds for `SetCreatureLevel`,
`AddModifierToTargetCreature`, `RemoveModifierFromTargetCreature`, `SetCreatureModifierConfig`,
`ClearCreatureModifierConfig`, `ApplyCreatureUpdates` and `SetCreatureSpawnManaged`.

Once the owner has saved a modifier from `AddModifierToTargetCreature`, every machine with the
creature loaded, the owner included, rebuilds its stats, size and speed with the modifier, whatever
`update` was. `update: false` only holds the change back on a calling machine that does not own the
creature, until the owner's save reaches it.

### Set values are the creature's base

A value you set is the creature's own base for that stat, the value its `LevelSettings` entry would
give it. Its modifiers add their share on top, the same way they do to a configured value: Big still
adds to a set size and health, a Resist modifier still lowers a set resistance, and Flame still adds
to a set fire bonus. The change applies at once (stats, size, speed and health), so the setters
need no `ApplyCreatureUpdates` after them. A creature Star Level System has not set up yet, such as
one you spawned this frame, gets the value when its setup runs.

The getters (`GetCreatureBaseAttribute` and the rest) return what the creature has now, its
modifiers' share included. So don't read a value, scale it and set it back: the share you read ends
up in the base, and the modifiers add it again. A Big creature with a configured base health of 1.0
that Big raises to 1.5 reads as 1.5; scaled by 1.5 and set back, it gets a base of 2.25 and Big's 0.5
on top, 2.75. Set the base you want instead (1.5 here, which Big then raises to 2.0).

Before 1.23.2 a setter wrote its value over the modified one, so the modifiers' share was missing
until the creature's stats were next rebuilt (a reload, an ownership handoff, a config reload), and
was added on top of the value from then on.

### Per-creature modifier values

Every modifier runs with the values its entry in `Modifiers.yaml` gives it: `BasePower`,
`PerlevelPower` and the keys of its `Config` (for example `RegenDelay` and `RegenDuration` for
Shielded, `MaxReduction` for Stalward). `SetCreatureModifierConfig` gives one creature its own values
in place of those. The keys are `"BasePower"`, `"PerlevelPower"` (in any case) or a `Config` key
(exactly as `Modifiers.yaml` writes it). Keys you leave out keep the configured value, and a second
call merges into the first.

- The values belong to the creature, not to the modifier it carries now. You can set them before
  Star Level System rolls the creature's modifiers ("if it rolls Fast, use this"), and they stay
  when the modifier is removed and given again. They are saved on the creature like the attribute
  setters above.
- They apply whatever type (Major, Minor, Boss) the creature carries the modifier as.
- `GetCreatureModifierConfig` returns what the modifier runs with on that creature, the creature's
  own values included. `ClearCreatureModifierConfig` drops one value, or all of them for that
  modifier.
- A machine that does not own the creature forwards these calls to the owner and returns `true`
  once the modifier name and values are valid; it cannot tell whether the owner had anything to
  remove or clear. Its own view, and `GetCreatureModifierConfig` there, change once the owner's save
  reaches it, a moment later. Every machine with the creature loaded then rebuilds its stats, size
  and speed.
- Earth Caller's `LaunchSpeed` always uses the configured value: the knock-up it gives a player does
  not know which boss cast it.
- Nemesis and raid spawns take the same values from their config, under `ModifierOverrides`.

`RemoveModifierFromTargetCreature` takes a modifier off a creature. It stops on every machine with
the creature loaded, and its share of the creature's stats goes.

Guard all of these on `SupportsModifierConfig`: an older Star Level System returns `false` or `null`.

A creature's modifiers are saved in a compact form from 1.30.0 onward. Which are not backwards-compatible.

### Creatures your mod spawns

If your mod spawns a creature on purpose and its existence and level matter (a quest or bounty
target, say), mark it spawn-managed:

```csharp
StarLevelSystem.API.SetCreatureSpawnManaged(creature, true);
```

Star Level System keeps scaling a spawn-managed creature (stats, modifiers, colour, HUD), but it
never:
- deletes it or spawns copies of it for a biome or creature spawn-rate modifier;
- deletes it because its spawns are disabled in that biome (by day or at night);
- rerolls or clamps its level when it is above the configured maximum.

Call it in the frame you spawn the creature, since Star Level System's own setup waits
`InitialDelayBeforeSetup` first. Call it again when the creature loads if it may have been spawned
before your mod made this call. Guard on `SupportsSpawnManaged`: an older Star Level System returns
`false`.

---

## Enemy huds and UI mods

Star Level System adds its own things to every enemy hud: the creature's name with its modifiers, a
star per level up to five stars (a star carries a modifier's icon in its place), one numbered star
followed by up to five modifier icons above five stars, a thin evolve bar under the health bar of an
Evolving creature, and optionally health numbers on the bar. It also scales the health bar
(`EnemyHealthbarScalarX/Y`) and sizes and stacks the boss bars.

A UI mod that lays out or draws the enemy hud itself can turn any of that off, read what Star Level
System would show and draw it in its own style, or both. Everything in this section needs
`SupportsEnemyHud`: an older Star Level System returns `false`, `0`, `null` and `-1`.

### Turning off Star Level System's hud changes

```csharp
// In your plugin's Awake, with the soft BepInDependency on Star Level System from the top of this page.
if (StarLevelSystem.API.SupportsEnemyHud) {
	// We lay out the bars ourselves and draw the stars from GetCreatureHudInfo.
	StarLevelSystem.API.SetEnemyHudChangesDisabled(MyPlugin.PluginGUID,
		StarLevelSystem.API.EnemyHudBarLayout | StarLevelSystem.API.EnemyHudStars);
}
```

| Value | Constant | What Star Level System stops doing |
|---|---|---|
| 1 | `EnemyHudBarLayout` | Scaling the normal health bar, and sizing, stacking and reparenting the boss bars and moving their name and `bkg`/`darken` images. The settings for those (`EnemyHealthbarScalarX/Y`, `StackMultipleBossHealthbars`, `BossHealthbarSpacing`, `BossHudTopBuffer`, `BossHealthbarWidthPercent`) then do nothing. Its stars follow your hud's own `level_2` row, see below. |
| 2 | `EnemyHudStars` | Its stars, numbered star and modifier icons, and hiding the hud's own `level_2` and `level_3`, which then show for levels 2 and 3 as in vanilla. |
| 4 | `EnemyHudEvolveBar` | The evolve bar. |
| 8 | `EnemyHudHealthNumbers` | The health numbers, whatever `EnableEnemyHealthbarNumberDisplay` says. |
| 15 | `EnemyHudAll` | All of the above. |

- Each call replaces what your earlier calls turned off, so `0` turns everything back on. A change
  stays off while any mod has it off, and `GetDisabledEnemyHudChanges` returns what is off now, every
  mod's set together.
- Call it from your `Awake`. Everything except the health numbers decides which objects Star Level
  System builds into the hud, so it applies the next time the enemy hud is built, which every world
  load does. The log names every mod that turned something off when the hud is built.
- The creature's name keeps its modifiers: it comes from `Character.GetHoverName`, which Star Level
  System patches everywhere, not from a change to the hud.
- **Project Auga** (`randyknapp.mods.auga`) gets `EnemyHudBarLayout` without asking. Once Auga calls
  `SetEnemyHudChangesDisabled` itself, its own set replaces that default, `0` included.

### Names Star Level System needs on a replaced hud

A UI mod that swaps vanilla's `EnemyHud` prefab for its own has to keep these names, because Star
Level System finds them, unless the change in the last column is turned off:

| Hud | Path | Used for | Not needed with |
|---|---|---|---|
| `m_baseHud` | `level_2/star`, with an `Image` and a child `star (1)` with an `Image` | cloned for every star and modifier icon; `star` is the dark back star, `star (1)` the lit one | `EnemyHudStars` |
| `m_baseHud` | `level_3/star` | hidden; vanilla's level 3 has a second star | `EnemyHudStars` |
| both | `Name` (`TextMeshProUGUI`), `Health` | the name, and the parent of the evolve bar and health numbers | |
| both | `Health/health_slow/bar`, `Health/health_fast/bar`, a `GuiBar` on `health_slow` and `health_fast` | scaling the bar (normal huds) and sizing the boss bar | `EnemyHudBarLayout` |
| `m_baseHudBoss` | `Health/bkg`, `Health/darken` | moved into `Health/Background` and sized with the boss bar | `EnemyHudBarLayout` |

Vanilla's own `ShowHud` and `UpdateHuds` also need `level_2`, `level_3`, `Alerted` and `Aware`.

### Where Star Level System's stars go on your hud

With `EnemyHudBarLayout` off and the rest on, Star Level System builds its stars, evolve bar and
health numbers on your hud and keeps them up to date. It adds those objects and changes nothing of
yours: no size, position or parent of `Health`, its bars, `bkg`, `darken`, `Name` or anything you
added to them.

- **The stars go where your hud's own stars go.** The star holders (`SLS_level_2` to `SLS_level_6`
  and `SLS_level_n`) hang under one `SLS_stars` object, which takes the anchors, pivot, size and
  position of your hud's own `level_2` row when the hud is built, and keeps its position every frame
  after that. So place your `level_2` row (and your other level rows) where stars belong, before
  Star Level System's `EnemyHud.Awake` postfix or at any time later, and its stars follow. On a
  normal hud it keeps your own `level_2` and `level_3` hidden while it draws stars.
- Inside `SLS_stars` the stars are centred, 16 apart on a normal hud and 20 on a boss hud, and drop
  a few units while the evolve bar shows. Above five stars the numbered star and its modifier icons
  are centred as one group; its number is a legacy `Text` at
  `SLS_level_n/level_n_name(Clone)/Text`, drawn to the left of the star.
- A hud without a `level_2` gets `SLS_stars` where Star Level System's first star goes on vanilla's
  hud, and you can move it yourself. Never move its children: Star Level System rewrites their
  positions.
- The evolve bar is `Health/SLS_evolve_bar`, 3 units tall, 1 below `Health`, stretched across its
  full width. Star Level System only shows, hides and fills it, so you can inset or move it, once, on
  the `m_baseHud` and `m_baseHudBoss` templates in your own `EnemyHud.Awake` postfix with
  `[HarmonyAfter("MidnightsFX.StarLevelSystem")]`.
- The health numbers are `Health/HealthText(Clone)`, made on each hud the first time it updates.

### Drawing it yourself

```csharp
Dictionary<string, object> info = StarLevelSystem.API.GetCreatureHudInfo(creature);
if (info != null) {
	int stars = (int)info["stars"];
	bool highLevel = (bool)info["highLevel"];       // one numbered star instead of a star per level
	List<Sprite> starIcons = (List<Sprite>)info["starIcons"];       // per star, null = plain star
	List<Sprite> highLevelIcons = (List<Sprite>)info["highLevelIcons"];
}
float evolve = StarLevelSystem.API.GetCreatureEvolveProgress(creature);   // -1 = no evolve bar
```

`GetCreatureHudInfo` returns a dictionary with `ready`, `level`, `stars`, `highLevel`, `name`,
`modifiers`, `modifierTypes`, `modifierIcons`, `starIcons`, `highLevelIcons`, `evolveProgress` and
`healthNumbers`; the comment on the method describes each. It only reads, and is safe to call before
Star Level System has set the creature up: `ready` is `false` until it has, and the modifiers may
still be missing then. It returns `null` for a player or a creature that is not networked. It builds
new lists on every call, so ask when a hud is shown and again when the creature's level changes or
`ready` was `false`, not every frame.

Turn off `EnemyHudStars`, `EnemyHudEvolveBar` or `EnemyHudHealthNumbers` for whatever you draw
yourself, or Star Level System draws it as well. `healthNumbers` stays the server's setting, so you
can follow it with your own numbers.

A mod that does not copy this API can call the same methods on
`StarLevelSystem.modules.APIReciever` by reflection.

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

### Everything here works from a client

The work itself happens on the server, which owns the world objects. But calling from a client
relays the request and brings the answer back, so an item that resets a dungeon can run its logic on
whoever used it.

That is why **every method takes a callback and returns `bool`**: on a client the answer arrives over
the network a moment later, so a method that returned it directly would have to lie.

```csharp
// bool = "the work started", NOT the answer.
bool started = StarLevelSystem.API.GetLocationLastReset("Crypt2", pos, 256f,
    when => { /* the answer, later on a client, immediately on the server */ });
```

> **The callback always fires, exactly once** — on success, on a deferral, on a refusal, on a
> timeout, and when there is no server to ask. Put your follow-up logic there and nowhere else; a
> `false` return has already delivered the reason through it. On the server, and for anything
> refused before it leaves the client, the callback runs before the call returns.

### Registering a target

```csharp
StarLevelSystem.API.RegisterLocationReset(
    prefabName: "MyCustomCrypt",
    sourceId: MyPlugin.PluginGUID,   // used in logs, and to scope unregistration
    resetHours: 48f,                 // or use resetSchedule instead
    resetTerrain: true,
    extraTerrainRadius: 16f,
    onResult: ok => { /* did the server accept it */ });

// Cron works too, and wins over resetHours. Five fields, server local time.
StarLevelSystem.API.RegisterLocationReset("MyBossArena", MyPlugin.PluginGUID,
    resetSchedule: "0 3 * * *");     // every day at 03:00
```

The target then joins the normal background sweep. Its timer rides on the location's own world data
(the `LocationProxy` ZDO), so it survives losing the reset state file, and it is subject to the same
biome and distance-band rate multipliers, the same protection scan, and the same throughput limits
as anything the server owner configured by hand.

If your mod is installed on the server, register whenever you like — including before a world loads.
If it is client-side only, the registration has to relay, so **register once you are connected**
rather than in your `Awake`.

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
whether the owner's config is overriding it. A registration that arrived from a client is tagged with
the peer it came from.

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

Only one manual reset runs at a time, so a second request while one is in flight is refused rather
than queued.

### Safety

| `safety` | Behaviour |
|---|---|
| `0` — Safe (default) | Waits for players to leave the affected chunks, then resets. Gives up after `safeWaitSeconds` (default 300) and touches nothing, reporting `outcome: "deferred"`. |
| `1` — Force | Resets immediately, working on chunks already loaded around a player. |

Player-built structures block a reset in **both** modes, and there is no way to override that. For
ore and vegetation they block their whole chunk; for a location, only structures within
`LocationBuffer` of the ground it resets count. A dungeon whose entrance is held back by one still has
its interior rebuilt, and is reported in `locationsInteriorOnly`.

Force is the mode to use when you want a location restored just before somebody walks back into it —
Valheim keeps the chunks around every player loaded, so Safe would simply never fire there.

A dungeon is never rebuilt around a player inside it, in either mode: its interior is left alone and
the zone's `zones` entry carries the reason in `locationHeldReason`. The same happens while a dungeon
holding a tombstone, dropped item or player build is loaded by a nearby player, so nothing kept inside
falls out while the rooms are rebuilt.

### Limits on requests from a client

A client's request is not admin-gated, so the server bounds it instead:

| Limit | Server setting | Default |
|---|---|---|
| Largest radius it will accept (clamped, not refused) | `ClientLocationResetMaxRadius` | 256m |
| How far from the requester the target may be | `ClientLocationResetMaxDistance` | 256m |
| Minimum gap between resets or registrations from one client | `ClientLocationResetCooldownSeconds` | 30s |

Read `clientMaxRadius`, `clientMaxDistance` and `clientCooldownSeconds` from `GetLocationResetStatus`
to size your requests rather than discovering the limits by being clamped. Read-only queries are not
rate-limited.

A refused request still reaches your callback, with `outcome: "refused"` and a `refusalCode` — see
below.

### Finding out when something was last reset

```csharp
StarLevelSystem.API.GetLocationLastReset("Crypt2", position, 256f, when => {
    // -1 = no location of that name in range, 0 = never reset, else Unix seconds UTC
});

StarLevelSystem.API.GetSecondsUntilLocationReset("Crypt2", position, 256f, due => {
    // 0 = due now, -1 = unknown or not configured
});

StarLevelSystem.API.GetLocationResetInfo("Crypt2", position, 256f, info => { });
StarLevelSystem.API.GetChunkResetInfo(position, false, chunk => { });
```

`GetLocationResetInfo` reports `found`, `lastResetUnix`, `secondsUntilDue`, `dueNow`, `enabled`,
`source`, `schedule`, and the location's position and distance. Always check `found` first.

`GetChunkResetInfo` reports on a map chunk: `tracked`, `lastExaminedUnix`, `deferredUntilUnix`,
`protectionBlocked` / `protectionReason`, and the chunk's location and its timestamp.
`protectionBlocked` is the chunk's own protection, which holds back its ore, pickables and vegetation;
a location in the chunk is judged separately.

Both also report `locationScope` and `locationHeldReason`: what a reset of the location would do if
it came due right now. `locationScope` is `full`, `interior` (a dungeon whose surface is held back, so
only its interior would be rebuilt), `held` (nothing would be touched), or `none` (not a reset target).
`locationHeldReason` says what is holding it, and is empty when nothing is.

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
| `outcome` | string | `completed`, `deferred`, `refused` or `failed` |
| `refusalCode` | string | Empty unless `outcome` is `refused`; see the table below |
| `reason` | string | Empty on success, otherwise why it stopped, in prose |
| `target` | string | The location name, empty for a radius reset |
| `zonesConsidered` / `zonesReset` / `zonesBlocked` | int | Chunks looked at, reset, and refused outright — protection held back both the chunk and its location |
| `zonesUngenerated` / `zonesAdopted` | int | Never-generated chunks, and chunks worked on while loaded |
| `locationsRebuilt` / `locationsTerrainOnly` / `locationsSkipped` | int | |
| `locationsInteriorOnly` | int | Of `locationsRebuilt`, dungeons whose interior alone was rebuilt because their surface was held back |
| `locationsWithSpawnErrors` | int | Of `locationsRebuilt`, locations whose respawn logged errors. The location came back, but part of it may not have initialised — usually a fault in the mod that adds it |
| `locationNames` | `List<string>` | What was actually rebuilt |
| `objectsCleared` / `objectsSpawned` / `vegetationObjects` | int | |
| `pickablesRefreshed` / `mineRocksRefreshed` / `containersRefreshed` | int | |
| `terrainReverted` / `doorsSealed` | int | |
| `zdoGrowth` | int | Net world-object change |
| `zdoRestored` | int | Of that, objects that were missing and came back: regrown vegetation, and location content beyond what was still standing |
| `zdoDrift` | int | Growth past `zdoRestored`, never negative. A faithful restore is 0 |
| `waitedSeconds` / `elapsedSeconds` | float | Time spent waiting in Safe mode, and in total |
| `zones` | `List<Dictionary<string, object>>` | Per-chunk detail, only when `includeDetail: true`. Left empty on a call from a client when the detail is too large to send back; the counters still arrive |

Numeric values arrive as the type listed here whether the call was local or relayed.

A refused request delivers the same shape with every counter at zero, so `result["completed"]` and
`result["refusalCode"]` can be read without checking which kind of answer arrived first.

### Refusal codes

Branch on `refusalCode`, never on `reason` — the prose is written for humans and will be reworded.

| Code | Meaning | Worth retrying? |
|---|---|---|
| `no_such_location` | Nothing of that name within the radius | Not without moving or widening |
| `hard_blocked` | This location can never be reset (the starting temple) | No |
| `already_running` | Another manual reset is in flight | Yes, shortly |
| `cooldown` | This client asked too recently | Yes, after `clientCooldownSeconds` |
| `too_far` | The position is beyond `clientMaxDistance` from you | Not from here |
| `not_ready` | No world loaded on the server yet | Yes, shortly |
| `mod_conflict` | A conflicting reset mod is installed | No |
| `no_connection` | This client has no server to ask | Yes, once connected |
| `no_name` | The call was made without a location name | No — fix the call |
| `timeout` | The server did not answer in time | Yes |
| `disconnected` | The world unloaded before the answer arrived | No |
| `server_error` | The server threw handling the request | No — check the server log |

An `outcome` of `deferred` is not a refusal and carries no code: Safe mode waited for players to
leave, gave up, and changed nothing. That one is always worth retrying later.

### Worked example: a reset-on-use item

```csharp
// Runs wherever the item was used - client or server, no branching needed.
void OnRuneUsed(Player player) {
    if (!StarLevelSystem.API.SupportsLocationReset) { return; }

    StarLevelSystem.API.GetSecondsUntilLocationReset("Crypt2", player.transform.position, 64f, due => {
        if (due != 0d) {
            player.Message(MessageHud.MessageType.Center, "This place is not ready to renew.");
            return;
        }
        StarLevelSystem.API.ResetNamedLocation("Crypt2", player.transform.position, 64f,
            safety: 1,   // they are standing next to it, so the chunk is loaded
            onComplete: result => {
                // Fires whatever happened, so the item is consumed or refunded in one place.
                if ((bool)result["completed"] && (int)result["locationsRebuilt"] > 0) {
                    player.Message(MessageHud.MessageType.Center, "The crypt has been renewed.");
                    return;
                }
                RefundRune(player);
                switch ((string)result["refusalCode"]) {
                    case "cooldown":
                    case "already_running":
                        player.Message(MessageHud.MessageType.Center, "The magic is still settling. Try again shortly.");
                        break;
                    case "":   // deferred, or a reset that ran but rebuilt nothing
                        player.Message(MessageHud.MessageType.Center, (string)result["reason"]);
                        break;
                    default:
                        player.Message(MessageHud.MessageType.Center, "This place resists renewal.");
                        break;
                }
            });
    });
}
```
