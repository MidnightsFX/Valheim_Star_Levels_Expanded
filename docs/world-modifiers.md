# World modifiers

[All documentation](README.md)

SLS follows the vanilla world modifiers a world was created with, or changed to later with `setworldmodifier` / `setkey`,
on top of its own settings:

- **Combat**: vanilla still applies enemy and player damage after SLS's damage scaling, and SLS's death novas follow the
  enemy damage setting too. A harder setting's higher enemy level-up rate multiplies every SLS levelup chance (creatures,
  bosses, raid spawns, fish and birds) the way night and zone bonuses do. Its larger enemies are sized on top of SLS's
  own sizing, so `MaximumCreatureScale` caps SLS's sizing and the world's size goes on top; creatures in dungeons and
  other interiors keep their size, as in vanilla.
- **Resources**: drop tables SLS hands back to vanilla keep vanilla's resource scaling, and SLS's own tables in
  `LootSettings.yaml` (`CharacterSpecificLoot`, `NonCharacterSpecificLoot`) follow it as well, after their per-level
  scaling and before `MaxScaledAmount`. Drops marked `DontScale` or `DoesNotScale`, and the item types vanilla never
  scales, keep their amounts.
- **Raids**: see [Raids](raids.md).

With `EnableDebugOutputLevelRolls` on, each level roll logs the world's rate as `(WorldLevelUpRate)` next to the night and
zone bonuses. The Level Distribution and Loot pages of the quick configure panel include the current world's modifiers in
their charts and estimates, and say which ones are active.
