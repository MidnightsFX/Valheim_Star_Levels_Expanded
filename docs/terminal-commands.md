# Terminal commands

[All documentation](README.md)

Star Level Systems provides a number of terminal commands for debugging, testing and cleanup. Type
`sls-help` in the console for the full list, or `sls-help loc` for a single group. Tab-completes each
argument as you type it, and colours output by severity (turn that off with the `EnableTerminalColors`
client setting if you prefer plain text).

- `sls-help [area]` - lists the StarLevelSystem commands, optionally just one area
- `sls-creature-killall [range:500]` - kills creatures within the specified range (default 500m), skips players and tamed creatures
- `sls-creature-setlevel [level] [range:64]` - sets the closest creature (within the search range) to the given level. Level 1 is no stars, 2 is one star, and so on; clamped to that creature's configured maximum
- `sls-creature-setstat [group] [stat] [value|reset] [range:64]` - sets one stat on the closest creature, the same value its `LevelSettings.yaml` creature entry would set, and keeps it on that creature through restarts. Groups are `Base` (`BaseHealth`, `BaseDamage`, `AttackSpeed`, `Speed`, `Size`), `PerLevel` (`HealthPerLevel`, `DamagePerLevel`, `SpeedPerLevel`, `AttackSpeedPerLevel`, `SizePerLevel`), `DamageTaken` and `DamageBonus` (one stat per damage type). Modifiers like Big still add on top, size is still held to the size cap, and `reset` puts the configured value back. Leave out the value to see the current ones. eg: `sls-creature-setstat Base Size 2`, `sls-creature-setstat Base BaseHealth 5`, `sls-creature-setstat DamageTaken Fire 0.5`
- `sls-mod-give [modifier_type:major] [modifier_name:fire]` - gives nearby creatures the specified modifier (must be very close)
- `sls-loot-dump` - dumps all loot table configurations in the game, in SLS format, to `BepInEx/config/StarLevelSystem/LootTablesDump.yaml`
- `sls-zone-rebuild` - regenerates the zone map from the world with the current size settings, for everyone. Resets zone kill counts and levels
- `sls-region-rebuild` - cuts the region map again from the world with the current size settings, and sends it to everyone. Resets region kill counts and levels
- `sls-nemesis-spawn [biome]` - force-scouts and places one remote Nemesis boss
- `sls-nemesis-cleanup <all|radius>` - removes Nemesis spawns: remote bosses still waiting to appear, Nemesis bosses with their minions, other Nemesis creatures, and their map pins. `all` clears the whole world; a radius clears around you. Tamed creatures are left alone and nothing drops loot. Server authoritative, admin only
- `sls-nemesis-score [value]` - sets your local Nemesis score
- `sls-raid-spawn [raid_name] [x] [z]` - force-starts a raid, ignoring every cooldown and activation requirement (biome, keys, player base), but not a raid already running within `RaidExclusionRange`. Tab-completes the raid names from your `RaidSettings.yaml`. Defaults to your own position; pass an `x` and `z` to start it somewhere else. The raid does not consume the target player's raid cooldown, so it will not delay their next natural raid. Works on raids that are disabled or while `DisableAllRaids` is set, so you can test one before turning it on
- `sls-raid-clear-pins` - removes every SLS raid map pin (the red event circle and the `!` marker) from your own map. Raids still running near you redraw theirs; pins left behind by a raid that has ended are gone for good. Pins are local to each client, so no admin is needed

Location Reset commands (server authoritative):
- `sls-loc-status` - reports sweep throughput, how much of the world has been examined, the projected time for a full pass, and cumulative ZDO drift
- `sls-loc-dump` - writes every location and vegetation entry this world knows about (including ones other mods add) to `SavedData/LocationResetCatalog.yaml`, for use when configuring `LocationResetSettings.yaml`
- `sls-loc-stamp` - stamps every generated zone as reset right now, restarting every timer from today. A world with no timers at all does this automatically when resets are switched on, so this is for re-baselining one that already has them
- `sls-loc-reset [range:64]` - immediately resets the chunks around you, ignoring every timer, including the chunks currently loaded around you. Reports each chunk it touched to the console. Player structures are still protected
- `sls-loc-audit [range:256] [fix]` - scans for duplicate world objects and surplus terrain compilers. Reports only unless `fix` is passed

## Running the server commands from a client

The Location Reset commands, `sls-zone-rebuild`, `sls-region-rebuild`, `sls-nemesis-spawn`, `sls-nemesis-cleanup` and `sls-raid-spawn` act on world state only the server owns. A
dedicated server has no console of its own, so an **admin** can now run them from a connected client:
the request goes to the server, the server runs it, and its output is streamed back into your console
as well as being written to the server's log. `sls-loc-reset` and `sls-loc-audit` centre on **your**
position, which is what makes them usable on a headless server at all.

Non-admins are refused. Several of these are flagged as cheat commands, which vanilla only allows once
`devcommands` is active — on a server that requires
[Server devcommands](https://github.com/JereKuusela/valheim-dev) on the client. `sls-loc-status` is not
a cheat command and works without it.

## Renamed commands

Commands were regrouped as `sls-<area>-<verb>` in 1.5.2. Every old name still works and is accepted
silently, so existing macros and guides are unaffected; `sls-help` lists the old name alongside each
command.

| Old name | New name |
| --- | --- |
| `SLS-killall` | `sls-creature-killall` |
| `SLS-give-modifier` | `sls-mod-give` |
| `SLS-Dump-LootTables` | `sls-loot-dump` |
| `SLS-rebuild-zones` | `sls-zone-rebuild` |
| `SLS-spawn-nemesis-remote` | `sls-nemesis-spawn` |
| `SLS-SetNem-Score` | `sls-nemesis-score` |
| `SLS-reset-player-modifiers` | `sls-player-reset` |
| `sls-loc-status` | `sls-loc-status` |
| `sls-loc-reset` | `sls-loc-reset` |
| `sls-loc-audit` | `sls-loc-audit` |
| `sls-loc-dump` | `sls-loc-dump` |
| `sls-loc-stamp` | `sls-loc-stamp` |
