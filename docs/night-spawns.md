# Boss night spawns

[All documentation](README.md)

Defeating a boss lets new creatures roam the world at night: Greydwarfs in the Meadows after Eikthyr, Greydwarf brutes,
shamans and (in fog) Draugr after the Elder, Skeletons after Bonemass, Fulings after Yagluth, and Seekers, Seeker broods and
Ticks after the Queen. Each boss has its own switch in the `NightSpawns` section of the main config (`EikthyrNightSpawns`,
`ElderNightSpawns`, `BonemassNightSpawns`, `ModerNightSpawns`, `YagluthNightSpawns`, `QueenNightSpawns`, `FaderNightSpawns`),
all on by default. Turning one off stops that boss's night spawns from then on; any already out still leave at dawn, and
the boss's raids and other unlocks are untouched.

A night spawn belongs to a boss when its spawn entry only spawns at night and requires that boss's defeat key, so a mod's
night spawn gated the same way is covered too. Odin's night visit after the Elder is left alone. The Night Spawns page of
the quick configure panel (Mod Config button) has a checkbox per boss and, in a loaded world, lists what each one brings;
a boss with no night spawns is greyed out (on the main menu that is judged by vanilla's spawns).
These are server settings: the server's values are synced to every client, which is where spawning happens.
