# Nemesis System (NemesisSettings.yaml)

[All documentation](README.md)

The Nemesis system is off by default, turn on `EnableNemesisSystem` in the main config to use it.

It is designed to constantly tune the world around a player or group of players to ensure that their experience and challenges are appropriate.

It does this by analyzing things the player does and tracking statistics about the players combat performance. Lower combat scores result in reduced challenge,
high combat scores result in increased challenge.

This is all extremely configurable, and everyone's experience is likely to be very different due to differences in skill or playstyle and that is ok.
Ideally everyone should feel times of challenge, and that setbacks do not feel overwhelmingly punishing.

Nemesis system also contains the ability to make unique enemies that will attempt to hunt you (or a friend) down in the future (small chance to spawn).

Nemesis configuration.
```
NemesisVersion: 1
CreateMinibossFromPlayerKiller: true   # controls whether or not player deaths can result in a nemesis enemy being created
CreationRemovesSourceCreature: true    # whether or not the create that turns into a nemesis boss gets removed from its current location
NemesisBossChance: 0.1                 # chance that a nemesis boss can be created
NemesisBossMaxLevelBonus: 0.4          # percentage level bonus for the nemesis boss
NemesisBossMinLevelBonus: 0.2
NemesisMinionTemplatesByBiome: ...
ScoreSystem:                           # Nemesis score system determines player score and what actions can happen
  NeutralScore: 5000                   # score moves back towards this neutral point all of the time
  MaxScore: 10000                      # max score
  DeathScoreReduction: 1500            # how much score is reduced when the player dies
  DecayPerUpdate: 250                  # how much the score moves towards neutral constantly
  NearbyPlayerRadius: 60               # nearby radius for syncing score with friends
  MeleeDamageDealtFactor: 0.75         # score contribution of melee damage
  MagicDamageDealtFactor: 0.5          # score contribution for magic damage
  RangedDamageDealtFactor: 0.25        # score contribution for ranged damage
  DamageTakenFactor: 0.5               # score lost per point of damage taken (sign is ignored)
  BossKillBonus: 500                   # score bonus from killing a boss
GaurenteedChanges:                     # nemesis changes that always happen
  FirstBossSetLevel: true              # first world boss of each kind when enabled will use the specified level
  FirstBossLevel: 1
```

There are a few additional sections to this config that are worth covering also. Chance changes being the primary one.

```
ChanceChanges:
  CreatureOps:                              # Chance changes are random actions the nemesis system can take
    CharredAttack:                          # name of the action, required unique
      RequiredGlobalKey: defeated_fader     # if the action requires the server has a certain global key
      Chance: 0.3                           # chance the action will happen (valid values 1 <-> 0)
      LevelBonus: 5                         # level bonus applied to spawns from this
      DeniedBiomes:                         # Biomes this is not allowed to happen in
      - DeepNorth
      AllowedBiomes: []                     # Biomes this is allowed to happen in, empty allows all
      ScoreThreshold: 9000                  # Score threshold, the players score must be at least at this level (if above neutral score) or below this level if threshold is below neutral score
      Action: Spawn                         # action, valid options: ChangeLevel, AddModifier, RemoveModifier, Spawn, SpawnMiniboss
      ScoreChange: -2000                    # point change applied to score when this event happens
      SpawnConfig:                          # Spawns associated with this config
      - Prefab: Charred_Melee               # spawn creature prefab
        SpawnGroupSize: 1                   # number of creatures to spawn
        Faction: Boss                       # faction to assign to the creature
        RequiredModifiers:                  # modifiers the creature must have
          Fire: Major
        ModifierOverrides:                  # this spawn's own values for its modifiers, in place of the ones in Modifiers.yaml
          Fire:
            BasePower: 0.5                  # BasePower, PerlevelPower and Config entries, any of them; the rest stay as configured
      - Prefab: Charred_Archer
        SpawnGroupSize: 2
        Faction: Boss
        RequiredModifiers:
          Fire: Major
          ...
```

The final section in this config is NemesisMinionTemplatesByBiome. This controls the minions which can be added to a nemesis spawn.

```
NemesisMinionTemplatesByBiome:
  Meadows:
  - PrefabName: Neck
    MinAmount: 6
    MaxAmount: 9
    CreatureBaseValueModifiers:
      BaseHealth: 2
    CreaturePerLevelValueModifiers:
      DamagePerLevel: 0.01
    ModifierOverrides:                 # the minions' own values for modifiers they have or roll, as on a spawn above
      Fast:
        BasePower: 0.1
```
