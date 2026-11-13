# Location Reset (LocationResetSettings.yaml)

[All documentation](README.md)

> **Back up your world before enabling this.** Location Reset destroys and recreates world objects.

On a busy server the world is a finite pool of loot. Crypts, camps, ore and pickables get consumed
once and never come back, so late joiners and less-active players are permanently locked out of
content the early players already stripped. Location Reset brings that content back on a timer.

**It is disabled by default, and every location and vegetation entry is opt-in.** Turn on
`EnableLocationReset`, then set `Enabled: true` on the specific things you want to come back.

The Location Reset page of the quick configure panel (Mod Config button) covers the common settings
without editing YAML: one switch for both master switches, the default timer and terrain reset, the
safe and protection radii, chest refills, the sweep budget, biome rates, and each reset group's on/off,
timer and terrain reset. Group members, schedules, distance limits and protection rules stay in the file.

How it works:
- **Resets happen in the background, away from players.** Ore, pickables and vegetation wait until
  nobody is within `PlayerSafeRadius` (256m) of their chunk; a location's surface waits until nobody
  is within `LocationPlayerRadius` (120m) of it. A chunk somebody happened to be standing in is
  re-tried a couple of times a few minutes later rather than losing its whole cycle.
- **Locations are restored, not re-rolled.** The original position and rotation are kept, so
  buildings never rotate, shift or clip into the terrain after a reset.
- **Ore, pickables and vegetation only come back when something is actually missing**, and never
  past the amount the chunk had. Placement is replayed using the world's own generation seed, which
  re-places every node in the chunk: the ones still standing are matched by position and discarded
  rather than stacked on top of the survivor, and of the rest only as many are kept as went missing.
  A regrown node lands where world generation would put one, though not always on the exact spot the
  lost one stood — `sls-loc-audit` will tell you if any duplicates ever slip through.
- **Dungeon interiors reset with their entrance** — crypts, caves, mines and citadels. Only a player
  inside a dungeon holds its interior back. If players have built around the entrance, the interior
  is still rebuilt and the entrance is left exactly as it is; tombstones, dropped items and player
  builds inside are kept where they lie.
- **Terrain can be reset** around ore to undo mining craters. Locations also support
  `Mode: TerrainOnly`, which flattens the ground around one without touching the structure itself.
- **Your stuff is safe.** Player-built structures, tombstones, wards, portals, beds, player-placed
  chests, tamed creatures and player bases all block a reset. For ore and vegetation that means
  anything in or near their chunk; for a location, only what stands within `LocationBuffer` (10m)
  of the ground it resets, so a build elsewhere in the same chunk no longer holds it back.
  Protection is configurable per entry, so you can decide (for example) that a stray dropped item
  is preserved rather than blocking the reset.
- `StartTemple` can never be reset. Boss altars are covered by the `BossAltars` group, which ships
  enabled with terrain reset on to undo the crater players dig around a summoning circle — set
  `Enabled: false` on that group to leave them alone.

Timers are in **real-world hours** (`ResetHours`), which stays predictable on a server that runs
24/7. Throughput is controlled by `LocationResetSweepBudgetMs` — the milliseconds of server frame
time the sweep may use per frame. Raise it to restore the world faster; it automatically backs off
when the server is under load. Use `sls-loc-status` to see the projected time for a full pass.

**An already-explored world baselines itself.** The first time resets are switched on in a world
with no reset timers of its own, every generated zone is stamped as reset right then, so timers
start from that moment instead of everything becoming due at once. The same happens if the state
file is ever lost. Set `StampOnFirstSight: false` to skip it and have the sweep reset an explored
world instead; `sls-loc-stamp` re-baselines a world by hand at any time.

**Reset groups.** Groups are how this feature is configured. A group configures and **enables** a
whole set of targets in one block, and stands on its own — a member needs no entry anywhere else in
the file:

```yaml
ResetGroups:
  Ores:
    Enabled: true
    ResetHours: 48
    ResetTerrain: true
    Members: [rock4_copper, MineRock_Tin, silvervein, rock3_silver, mudpile_beacon]
```

To turn a whole category off, set its `Enabled: false`; to drop one target, remove it from
`Members`. If two groups claim the same prefab the shorter interval wins and the other is named in a
warning, and a member name your game version has nothing for is warned about at load.

A member can also be a category token — `$Mineable` or `$Pickable` — which expands to everything the
world *places* carrying that component, and so picks up modded ore and pickables automatically.
Unlike a named member a token stops there: one-off pickups that only exist inside dungeons are not
swept up by it, so name those explicitly if you want them. `$Pickable` includes berry bushes. Named
members need care, because prefab names are irregular: copper is `rock4_copper` while
`rock4_forest` is worthless scenery, and silver is both `rock3_silver` and `silvervein`.

A group can be limited to a ring around spawn with `MinDistance` / `MaxDistance` (`MaxDistance: 0`
means no outer limit). A scoped group applies only inside its range; outside it, its members fall
back to whatever unscoped group covers them — so `FlintNearSpawn` at 6h within 3000m leaves flint on
the normal foraging timer everywhere else.

The mod ships working groups (`BossAltars`, `Ores`, `Foraging`, `FlintNearSpawn`,
`Leviathans`, `QuestSites`, `CharredSpawners`, `AshlandsForts`) **already enabled**, so the feature
is usable without editing anything. Nothing resets until `EnableLocationReset` and the YAML
`Enabled` are both turned on. `sls-loc-status` lists every group with a `matched/total` member
count — a shortfall means a prefab name no longer exists in your game version.

**Locations and Vegetation are overrides.** Both ship empty and can usually stay that way. Add a key
only to retune one target or to enable something no group covers; values resolve as
**entry → group → Defaults**, so a per-prefab setting always beats its group:

```yaml
Locations:
  Eikthyrnir:
    ResetHours: 12        # BossAltars still enables it; this just retimes it
```

For the full list of names your world can reset — including everything other mods add — run
`sls-loc-dump`. It writes `SavedData/LocationResetCatalog.yaml` as a reference; that file is a
dump, not a config, and editing it does nothing.

**Advanced sections are omitted while unused.** `Throughput`, `InPlaceRefresh`, `ProtectedPrefabs`
and `DistanceBands` are left out of the generated file because their defaults suit almost every
server. Add a section by hand to use one — anything you leave out keeps its default. The config
file's own header comment documents each with its default value.

**Scheduling by clock time.** `ResetHours` measures elapsed time since a target last reset, so a
24h timer drifts a little later every cycle and you cannot say "restock overnight". `ResetSchedule`
takes a cron expression instead, usable anywhere `ResetHours` is:

```yaml
ResetGroups:
  Ores:
    ResetSchedule: 0 3 * * *        # 03:00 every day
  Foraging:
    ResetSchedule: '*/30 * * * *'   # every 30 minutes
  AshlandsForts:
    ResetSchedule: 0 4 * * MON      # 04:00 on Mondays
```

Fields are `minute hour day-of-month month day-of-week`, supporting `*`, `,`, `-` and `*/n`.
Day-of-week is `0-6` (0 = Sunday) or `SUN`–`SAT`, months are `1-12` or `JAN`–`DEC`, and the macros
`@hourly`, `@daily`, `@midnight`, `@weekly`, `@monthly` and `@yearly` all work. Quote any expression
starting with `*`, or YAML will read it as an alias.

Times are the **server's local time**, not UTC and not in-game time. Two things to know:

- If **both** day-of-month and day-of-week are restricted, a day matching *either* fires —
  `0 0 1 * MON` is the 1st of the month **and** every Monday. Surprising, but it is what every cron
  does.
- `BiomeRates` and `DistanceBands` do **not** scale a cron schedule; there is no sensible way to
  halve "every Tuesday at 3am". A rate of `0` still excludes the chunk entirely.

The two fields resolve as one unit, entry → group → Defaults: the first level that sets *either* one
owns the timing, so a per-prefab `ResetHours` still overrides its group's `ResetSchedule`. An invalid
expression is logged and that target quietly falls back to `ResetHours` rather than taking the config
file down.

**Focusing resets where they are needed.** Depletion is not uniform — it concentrates in the
biomes and near the spawn areas your players actually work. Two multipliers stack on top of each
entry's own `ResetHours`, so `effective hours = ResetHours × biome rate × band rate`, and both
default to `1.0`:

```yaml
BiomeRates:
  Meadows: 0.5          # everything in the Meadows returns twice as fast
  Mistlands: 2.0        # ...and half as fast out in the Mistlands
DistanceBands:
- Inner: 0              # metres from spawn
  Outer: 3000
  Multiplier: 0.5       # the hub recovers twice as fast
```

A rate of `0` **excludes** that biome or band from resets entirely — it does not mean "instantly" —
which is also the cheap way to say "only reset near spawn". `Outer: 0` means no outer limit, and a
chunk matching no band is left at `1.0`, so a partial band list never disables the rest of the
world. Distance is measured from the same point as the distance level-scaling rings; see
`DistanceBonusIsFromStarterTemple`.

**Ignoring trivial player clutter.** One abandoned campfire otherwise freezes a chunk's ore and
vegetation forever: any player-built piece blocks them, and their protection scan covers a chunk
*and its 8 neighbours*.
Worse, a campfire sitting on an ore spawn stops that node coming back even when the chunk does
reset, because vanilla will not place vegetation into a collider. Each protection category can list
prefabs exempt from it:

```yaml
Protection:
  PlayerBuiltPiece:
    Action: Block
    Ignored:
    - fire_pit
```

> An ignored prefab neither blocks a reset **nor survives one — it is deleted.** `fire_pit` ships
> ignored for the reasons above; add to the list sparingly. Tombstones can never be ignored, and
> anything in `ProtectedPrefabs` wins over an ignore. Every deletion is recorded in the chunk log.

**Player bases.** A chunk is also left alone while any part of it lies inside a player's base — the
area around a workbench and similar pieces that stops monsters spawning in vanilla — and so is a
location's surface while that area reaches its ground. This is the `PlayerBaseEffect` category,
and it reaches exactly as far as that area does rather than `ProtectionRadius`. A prefab ignored under `PlayerBuiltPiece` does not count as a base either, so the
shipped `fire_pit` ignore covers both. A reset group can relax it like any other category:

```yaml
ResetGroups:
  Ores:
    Protection:
      PlayerBuiltPiece: Ignore   # stray builds no longer hold ore chunks...
      PlayerBaseEffect: Ignore   # ...and neither does a workbench abandoned on an ore spawn
```

**Resetting terrain around a location.** Players dig approach ramps and moats just *outside* a
dungeon's footprint, where the normal terrain reset does not reach. `ExtraTerrainRadius` on a
location entry adds metres beyond the location's own radius, clamped to `ProtectionRadius` minus
32m — 16m at the default, 64m at most. The extra ground is checked for player property like the
rest of the location, plus `LocationBuffer`:

```yaml
Locations:
  Crypt2:
    Enabled: true
    ResetTerrain: true
    ExtraTerrainRadius: 16
```

**Seeing what it did.** Every chunk the system works on gets a record in
`SavedData/LocationResetLog.log` — its zone coordinates, world position and biome, and what was and
was not reset inside it, including the reason anything was skipped:

```
Zone -12,34 @ x=-768 z=2176 (BlackForest) reset: refreshed pickables 14, minerock 3 | location 'Crypt2' rebuilt (cleared 214, spawned 218) | ZDO 402->405, 4 restored
Zone -11,34 @ x=-704 z=2176 (Meadows) reset: vegetation 6 objects across 1 entries (68 duplicates rejected, 9 not missing) | ZDO 1531->1537, 6 restored
Zone -12,35 @ x=-768 z=2240 (Meadows) skipped: protected by PlayerBuiltPiece 'wood_floor' at x=-742 z=2251
Zone -12,36 @ x=-768 z=2304 (Meadows) nothing reset: location 'FireHole' not due, 3 vegetation entries not due
Zone -9,40 @ x=-576 z=2560 (BlackForest) reset: location 'Crypt3' interior rebuilt, surface left as it was (protected by Portal 'portal_wood' (built by 8FA31C02) at x=-561 z=2577, within 30m) (cleared 188, spawned 190, 12 surface copies dropped)
```

The `ZDO` figure is the object count of the chunk and its neighbours before and after the reset, and
`restored` is how much of that was missing content coming back. A reset that grows the count past
what it restored is warned about in the BepInEx log.

Turn this off with `EnableLocationResetLog`. `EnableDebugLocationResetDetails` additionally copies
the background sweep's chunk lines into the BepInEx log and expands every record with a per-entry
breakdown of what was skipped and why. Both are client-side settings, so on a dedicated server they
are configured on the server itself.

Not compatible with VentureValheim's LocationReset — SLS disables its own Location Reset
automatically if that mod is present, since both would fight over the same objects.
