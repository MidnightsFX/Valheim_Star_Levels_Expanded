using BepInEx;
using Jotunn.Entities;
using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Modifiers;
using StarLevelSystem.modules.NemesisSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using UnityEngine;
using UnityEngine.SocialPlatforms.Impl;
using static CharacterDrop;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.common
{
    internal static class TerminalCommands
    {
        internal static void AddCommands()
        {
            CommandManager.Instance.AddConsoleCommand(new ResetZOIDModifiers());
            CommandManager.Instance.AddConsoleCommand(new GiveCreatureModifier());
            CommandManager.Instance.AddConsoleCommand(new DumpLootTablesCommand());
            CommandManager.Instance.AddConsoleCommand(new KillAllCreaturesNearby());
            CommandManager.Instance.AddConsoleCommand(new SetNemesisScore());
            CommandManager.Instance.AddConsoleCommand(new SpawnNemesisRemote());
            CommandManager.Instance.AddConsoleCommand(new RebuildZones());
            CommandManager.Instance.AddConsoleCommand(new LocationResetDump());
            CommandManager.Instance.AddConsoleCommand(new LocationResetStampAll());
            CommandManager.Instance.AddConsoleCommand(new LocationResetStatus());
            CommandManager.Instance.AddConsoleCommand(new LocationResetHere());
            CommandManager.Instance.AddConsoleCommand(new LocationResetAudit());
        }

        // Shared radius parser for the Location Reset commands.
        private static float ParseRadiusArg(string[] args, float fallback, float max, Terminal context) {
            if (args.Length == 0) { return fallback; }
            if (float.TryParse(args[0], out float parsed) == false) {
                Echo(context, $"Radius must be a number; using {fallback}.");
                return fallback;
            }
            return Mathf.Clamp(parsed, 0f, max);
        }

        // The Location Reset commands used to write only to the BepInEx log, so an admin running them
        // from the in-game console saw nothing at all. Everything they say now goes to both.
        private static void Echo(Terminal context, string message) {
            Logger.LogInfo(message);
            context?.AddString(message);
        }

        internal class LocationResetStatus : ConsoleCommand {
            public override string Name => "SLS-loc-reset-status";
            public override string Help => "Reports Location Reset sweep throughput, how much of the world has been examined, the projected time for a full pass, and cumulative ZDO drift.";

            public override void Run(string[] args, Terminal context) {
                if (RequireLocationResetServer(context) == false) { return; }
                Echo(context, modules.LocationReset.LocationResetControl.BuildStatusReport());
            }
        }

        internal class LocationResetHere : ConsoleCommand {
            public override string Name => "SLS-loc-reset-here";
            public override string Help => "Format: [optional: radius] Immediately resets the chunks around you, ignoring every reset timer, including the chunks currently loaded around you. Player structures are still protected. eg: SLS-loc-reset-here 128";
            public override bool IsCheat => true;

            public override void Run(string[] args, Terminal context) {
                if (RequireLocationResetServer(context) == false) { return; }
                if (Player.m_localPlayer == null) {
                    Echo(context, "This command needs a local player; use SLS-loc-reset-stamp-all on a headless server instead.");
                    return;
                }
                float radius = ParseRadiusArg(args, 64f, 512f, context);
                Echo(context, $"Forcing a Location Reset within {radius}m. This ignores all timers but still respects player-structure protection.");
                modules.LocationReset.LocationResetControl.ForceResetAround(Player.m_localPlayer.transform.position, radius, context);
            }
        }

        internal class LocationResetAudit : ConsoleCommand {
            public override string Name => "SLS-loc-reset-audit";
            public override string Help => "Format: [optional: radius] [optional: fix] Scans for duplicate world objects and surplus terrain compilers. Reports only unless 'fix' is passed. eg: SLS-loc-reset-audit 256 fix";
            public override bool IsCheat => true;

            public override void Run(string[] args, Terminal context) {
                if (RequireLocationResetServer(context) == false) { return; }
                if (Player.m_localPlayer == null) {
                    Echo(context, "This command needs a local player to pick a centre point.");
                    return;
                }
                float radius = ParseRadiusArg(args, 256f, 2048f, context);
                bool fix = args.Any(a => string.Equals(a, "fix", StringComparison.OrdinalIgnoreCase));
                var report = modules.LocationReset.ZdoAudit.Run(Player.m_localPlayer.transform.position, radius, fix);
                Echo(context, report.ToString());
                if (fix == false && (report.DuplicatesFound > 0 || report.ExtraTerrainCompilers > 0)) {
                    Echo(context, "Re-run with 'fix' to remove them, e.g. SLS-loc-reset-audit " + radius + " fix");
                }
            }
        }

        // Server-side gate shared by the Location Reset commands. They all mutate world state or read
        // server-only data, so a client can never run them locally.
        private static bool RequireLocationResetServer(Terminal context) {
            if (ZNet.instance == null) {
                Echo(context, "You must be in a world to use the Location Reset commands.");
                return false;
            }
            if (ZNet.instance.IsServer() == false) {
                Echo(context, "Location Reset is server-authoritative; run this on the server console.");
                return false;
            }
            return true;
        }

        internal class LocationResetDump : ConsoleCommand {
            public override string Name => "SLS-loc-reset-dump";
            public override string Help => "Writes every location and vegetation entry this world knows about (including ones added by other mods) to SavedData/LocationResetCatalog.yaml, for use when configuring LocationResetSettings.yaml.";
            public override bool IsCheat => true;

            public override void Run(string[] args, Terminal context) {
                if (RequireLocationResetServer(context) == false) { return; }
                if (ZoneSystem.instance == null) {
                    Echo(context, "ZoneSystem is not ready yet.");
                    return;
                }
                try {
                    ValConfig.GetSavedDataSecondaryConfigDirectoryPath();
                    LocationResetConfiguration catalog = LocationResetData.BuildPopulatedDefault();
                    string header = @"#################################################
# Star Level System Expanded - Location Reset Catalog
#
# Generated by SLS-loc-reset-dump. This is a REFERENCE dump of everything this world can
# reset, not a live config file - editing it has no effect. Copy the entries you want into
# LocationResetSettings.yaml and set Enabled: true on them.
#################################################
";
                    File.WriteAllText(ValConfig.locationResetCatalogPath,
                        header + System.Environment.NewLine + DataObjects.yamlSerializer.Serialize(catalog));
                    Echo(context, $"Wrote {catalog.Locations.Count} locations and {catalog.Vegetation.Count} vegetation entries to {ValConfig.locationResetCatalogPath}");
                } catch (Exception e) {
                    Echo(context, $"Failed to write the Location Reset catalog: {e.Message}");
                }
            }
        }

        internal class LocationResetStampAll : ConsoleCommand {
            public override string Name => "SLS-loc-reset-stamp-all";
            public override string Help => "Stamps every generated zone as reset right now and records its prefab census. Use this once after installing so an already-explored world starts its reset timers from today instead of resetting everything at once.";
            public override bool IsCheat => true;

            public override void Run(string[] args, Terminal context) {
                if (RequireLocationResetServer(context) == false) { return; }
                int stamped = modules.LocationReset.LocationResetControl.StampAllGeneratedZones();
                Echo(context, $"Stamped {stamped} generated zones. Reset timers now run from this moment.");
            }
        }

        internal class RebuildZones : ConsoleCommand {
            public override string Name => "SLS-rebuild-zones";
            public override string Help => "Clears existing zones and regenerates the zone map from the world, then redraws the minimap overlay. Resets zone kill counts and levels.";
            public override bool IsCheat => true;

            public override void Run(string[] args) {
                modules.LevelSystem.ZoneScaleSystem.RebuildZones();
            }
        }

        internal class KillAllCreaturesNearby : ConsoleCommand {
            public override string Name => "SLS-killall";
            public override string Help => "Format: [optional: range] eg: sls-killall 500";

            public override void Run(string[] args) {
                float range = 500f;
                if (args.Length > 1) {
                    Logger.LogInfo("Optional argument of range is the only supported argument. Ensure your command follows the format: sls-killall 500");
                }
                if (args.Length > 0) {
                    try {
                        range = float.Parse(args[0]);
                    } catch (Exception e) {
                        Logger.LogInfo("Optional argument of range must be a valid number. Ensure your command follows the format: sls-killall 500");
                        Logger.LogWarning(e.Message);
                    }
                }
                List<Character> nearbyCreatures = SLSExtensions.GetCharactersInRange(Player.m_localPlayer.transform.position, range);
                foreach (Character chara in nearbyCreatures) {
                    if (chara.IsPlayer() || chara.IsTamed()) { continue; }

                    CharacterDrop cdrop = chara.gameObject.GetComponent<CharacterDrop>();
                    if (cdrop != null) {
                        GameObject.Destroy(cdrop);
                    }
                    if (chara != null) {
                        ZNet.Destroy(chara.gameObject);
                    }
                }
            }
        }

        internal class SetNemesisScore : ConsoleCommand {
            public override string Name => "SLS-SetNem-Score";
            public override string Help => "Format: [required: value] eg: sls-setnem-score 500";

            public override void Run(string[] args) {
                // Set the updated score
                float.TryParse(args[0], out float scoreres);
                float score = Mathf.Clamp(scoreres, NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.MinScore, NemesisSystemData.SLE_Nemesis_Settings.ScoreSystem.MaxScore);
                NemesisScoreSystem.SetScore(Player.m_localPlayer, score);
                NemesisSystem.CachedPlayerScore = score;
                Logger.LogInfo($"Set Local player Nemesis score to {score}");
            }
        }

        internal class SpawnNemesisRemote : ConsoleCommand {
            public override string Name => "SLS-spawn-nemesis-remote";
            public override string Help => "Format: [optional: biome] Force-scouts and places one remote Nemesis boss. On a dedicated server, admins can run it from a connected client.";

            public override void Run(string[] args) {
                if (ZNet.instance == null) {
                    Logger.LogInfo("You must be in a world to spawn a remote Nemesis boss.");
                    return;
                }
                // Remote spawning is server-authoritative. A connected client routes the request to the server,
                // which honors it only for admins, so reject non-admins here for a clearer message.
                if (ZNet.instance.IsServer() == false && SynchronizationManager.Instance.PlayerIsAdmin == false) {
                    Logger.LogInfo("Only server admins can force-spawn a remote Nemesis boss.");
                    return;
                }
                Heightmap.Biome biome = Heightmap.Biome.Meadows;
                if (args.Length >= 1 && Enum.TryParse(args[0], true, out Heightmap.Biome parsed) && parsed != Heightmap.Biome.None) {
                    biome = parsed;
                } else if (Player.m_localPlayer != null) {
                    biome = Heightmap.FindBiome(Player.m_localPlayer.transform.position);
                }
                if (ZNet.instance.IsServer()) {
                    Logger.LogInfo($"Force-spawning a remote Nemesis boss for biome {biome}...");
                } else {
                    Logger.LogInfo($"Requesting the server force-spawn a remote Nemesis boss for biome {biome}...");
                }
                if (NemesisRemoteSpawnControl.RequestForceSpawn(biome) == false) {
                    Logger.LogInfo("Unable to request a remote Nemesis spawn (no server connection or remote spawning is unavailable).");
                }
            }
        }

        internal class GiveCreatureModifier : ConsoleCommand
        {
            public override string Name => "SLS-give-modifier";
            public override string Help => "Format: [boss/major/minor] [modifier-name] Gives nearby creatures the specified modifier";

            public override void Run(string[] args)
            {
                if (args.Length < 2) {
                    Logger.LogInfo("Two arguments required, modifier type and modifier name. Eg: Major FireNova");
                }
                if (!Enum.TryParse(args[0], true, out ModifierType modtype)) {
                    Logger.LogInfo($"Modifier type must be one of {string.Join(",", Enum.GetValues(typeof(ModifierType)))}");
                }
                if (!Enum.TryParse(args[1], true, out ModifierNames modname))
                {
                    Logger.LogInfo($"Modifier Name must be one of {string.Join(",", Enum.GetValues(typeof(ModifierNames)))}");
                }
                CreatureModConfig cmfg = CreatureModifiersData.GetConfig(modname.ToString(), modtype);
                if (cmfg.PerlevelPower == float.NaN || cmfg.PerlevelPower == 0f && cmfg.BasePower == float.NaN || cmfg.BasePower == 0) {
                    Logger.LogInfo($"{modtype} did not contain a definition for {modname}. Types availabe in {modtype}: {string.Join(",", GetModifiersOfType(modtype).Keys)}");
                }

                
                List<Character> nearbyCreatures = SLSExtensions.GetCharactersInRange(Player.m_localPlayer.transform.position, 5f);
                Logger.LogInfo($"Adding {modtype} {modname} to {nearbyCreatures.Count}");
                foreach (Character chara in nearbyCreatures) {
                    if (chara.IsPlayer()) { continue; }
                    // modify the modifers the creature has, and re-init modifiers for the creature
                    CreatureModifiers.AddCreatureModifier(chara, modtype, modname.ToString());
                }
            }
        }

        internal class ResetZOIDModifiers : ConsoleCommand
        {
            public override string Name => "SLS-reset-player-modifiers";

            public override string Help => "Resets all of the modified damage, movementspeed, scale, health values that are assigned to the player.";

            public override void Run(string[] args)
            {
                var id = Player.m_localPlayer.GetZDOID().ID;
                // Set damage modifier to 1
                Player.m_localPlayer.m_nview.GetZDO().Set(SLS_DAMAGE_MODIFIER, 1f);
                // Set base attribute modifers to 1
                DictionaryDmgNetProperty existingDmgMods = new DictionaryDmgNetProperty(SLS_DAMAGE_BONUSES, Player.m_localPlayer.m_nview, new Dictionary<DamageType, float>());
                Dictionary<DamageType, float> dmgBonuses = new Dictionary<DamageType, float>() {
                    { DamageType.Blunt, 0f },
                    { DamageType.Slash, 0f },
                    { DamageType.Pierce, 0f },
                    { DamageType.Frost, 0f },
                    { DamageType.Lightning, 0f },
                    { DamageType.Poison, 0f },
                    { DamageType.Spirit, 0f },
                    { DamageType.Fire, 0f },
                    { DamageType.Chop, 0f },
                    { DamageType.Pickaxe, 0f }
                };
                existingDmgMods.Set(dmgBonuses);
                Logger.LogInfo($"Reset Player {id}");
            }
        }

        internal class DumpLootTablesCommand : ConsoleCommand
        {
            public override string Name => "SLS-Dump-LootTables";

            public override string Help => "Writes all creature loot-tables to a debug file.";

            public override bool IsCheat => true;

            public override void Run(string[] args)
            {
                string dumpfile = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "LootTablesDump.yaml");
                Dictionary<string, List<ExtendedCharacterDrop>> characterModDrops = new Dictionary<string, List<ExtendedCharacterDrop>>();
                foreach (var chardrop in Resources.FindObjectsOfTypeAll<CharacterDrop>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList<CharacterDrop>())
                {
                    Logger.LogDebug($"Checking {chardrop.name} for loot tables");
                    string name = chardrop.name;
                    if (characterModDrops.ContainsKey(name)) { continue; }
                    Logger.LogDebug($"checking {name}");
                    var extendedDrops = new List<ExtendedCharacterDrop>();
                    Logger.LogDebug($"drops {chardrop.m_drops.Count}");
                    foreach (var drop in chardrop.m_drops)
                    {
                        var extendedDrop = new ExtendedCharacterDrop
                        {
                            Drop = new DataObjects.Drop
                            {
                                Min = drop.m_amountMin,
                                Max = drop.m_amountMax,
                                Chance = drop.m_chance,
                                OnePerPlayer = drop.m_onePerPlayer,
                                LevelMultiplier = drop.m_levelMultiplier,
                                DontScale = drop.m_dontScale
                            }
                        };
                        if (drop.m_prefab != null) {
                            extendedDrop.Drop.Prefab = drop.m_prefab.name;
                        }
                        extendedDrops.Add(extendedDrop);
                    }
                    characterModDrops.Add(name, extendedDrops);
                    Logger.LogDebug($"Adding {name} loot-table");
                }

                Dictionary<string, List<ExtendedObjectDrop>> objectDrops = new Dictionary<string, List<ExtendedObjectDrop>>();
                Logger.LogDebug($"Checking TreeLogs for loot tables");
                foreach (TreeLog tdrop in Resources.FindObjectsOfTypeAll<TreeLog>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList()) {
                    string name = Utils.GetPrefabName(tdrop.gameObject);
                    Logger.LogDebug($"Checking {name} for loot tables");
                    if (objectDrops.ContainsKey(name)) { continue; }
                    List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
                    Logger.LogDebug($"drops {tdrop.m_dropWhenDestroyed.m_drops.Count}");
                    foreach(DropTable.DropData drop in tdrop.m_dropWhenDestroyed.m_drops) {
                        ExtendedObjectDrop eodrop = new ExtendedObjectDrop() {
                            Drop = new DataObjects.Drop {
                                Prefab = drop.m_item.name,
                                Min = drop.m_stackMin,
                                Max = drop.m_stackMax,
                                Chance = tdrop.m_dropWhenDestroyed.m_dropChance,
                                LevelMultiplier = false, // No static drop types use levels by default so this field does not exist
                                DontScale = drop.m_dontScale,
                            }
                        };
                        if (drop.m_item != null) {
                            eodrop.Drop.Prefab = drop.m_item.name;
                        }
                        extendedDrops.Add(eodrop);
                    }
                    objectDrops.Add(name, extendedDrops);
                }

                Logger.LogDebug($"Checking Minerock5 for loot tables");
                foreach (MineRock5 tdrop in Resources.FindObjectsOfTypeAll<MineRock5>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList()) {
                    string name = Utils.GetPrefabName(tdrop.gameObject);
                    Logger.LogDebug($"Checking {name} for loot tables");
                    if (objectDrops.ContainsKey(name)) { continue; }
                    List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
                    Logger.LogDebug($"drops {tdrop.m_dropItems.m_drops.Count}");
                    foreach (DropTable.DropData drop in tdrop.m_dropItems.m_drops) {
                        ExtendedObjectDrop eodrop = new ExtendedObjectDrop() {
                            Drop = new DataObjects.Drop {
                                Prefab = drop.m_item.name,
                                Min = drop.m_stackMin,
                                Max = drop.m_stackMax,
                                Chance = tdrop.m_dropItems.m_dropChance,
                                LevelMultiplier = false, // No static drop types use levels by default so this field does not exist
                                DontScale = drop.m_dontScale,
                            }
                        };
                        if (drop.m_item != null) {
                            eodrop.Drop.Prefab = drop.m_item.name;
                        }
                        extendedDrops.Add(eodrop);
                    }
                    objectDrops.Add(name, extendedDrops);
                }

                Logger.LogDebug($"Checking Minerock for loot tables");
                foreach (MineRock tdrop in Resources.FindObjectsOfTypeAll<MineRock>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList()) {
                    string name = Utils.GetPrefabName(tdrop.gameObject);
                    Logger.LogDebug($"Checking {name} for loot tables");
                    if (objectDrops.ContainsKey(name)) { continue; }
                    List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
                    Logger.LogDebug($"drops {tdrop.m_dropItems.m_drops.Count}");
                    foreach (DropTable.DropData drop in tdrop.m_dropItems.m_drops) {
                        ExtendedObjectDrop eodrop = new ExtendedObjectDrop() {
                            Drop = new DataObjects.Drop {
                                Min = drop.m_stackMin,
                                Max = drop.m_stackMax,
                                Chance = tdrop.m_dropItems.m_dropChance,
                                LevelMultiplier = false, // No static drop types use levels by default so this field does not exist
                                DontScale = drop.m_dontScale,
                            }
                        };
                        if (drop.m_item != null) {
                            eodrop.Drop.Prefab = drop.m_item.name;
                        }
                        extendedDrops.Add(eodrop);
                    }
                    objectDrops.Add(name, extendedDrops);
                }

                Logger.LogDebug($"Checking DropOnDestroyed for loot tables");
                foreach (DropOnDestroyed tdrop in Resources.FindObjectsOfTypeAll<DropOnDestroyed>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true && !cdrop.name.Contains(" ")).ToList()) {
                    string name = Utils.GetPrefabName(tdrop.gameObject);
                    Logger.LogDebug($"Checking {name} for loot tables");
                    if (objectDrops.ContainsKey(name)) { continue; }
                    List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
                    Logger.LogDebug($"drops {tdrop.m_dropWhenDestroyed.m_drops.Count}");
                    foreach (DropTable.DropData drop in tdrop.m_dropWhenDestroyed.m_drops) {
                        ExtendedObjectDrop eodrop = new ExtendedObjectDrop() {
                            Drop = new DataObjects.Drop {
                                Min = drop.m_stackMin,
                                Max = drop.m_stackMax,
                                Chance = tdrop.m_dropWhenDestroyed.m_dropChance,
                                LevelMultiplier = false, // No static drop types use levels by default so this field does not exist
                                DontScale = drop.m_dontScale,
                            }
                        };
                        if (drop.m_item != null) {
                            eodrop.Drop.Prefab = drop.m_item.name;
                        }
                        extendedDrops.Add(eodrop);
                    }
                    objectDrops.Add(name, extendedDrops);
                }

                Logger.LogDebug($"Checking TreeBase for loot tables");
                foreach (TreeBase tdrop in Resources.FindObjectsOfTypeAll<TreeBase>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true && !cdrop.name.Contains(" ")).ToList()) {
                    string name = Utils.GetPrefabName(tdrop.gameObject);
                    Logger.LogDebug($"Checking {name} for loot tables");
                    if (objectDrops.ContainsKey(name)) { continue; }
                    List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
                    Logger.LogDebug($"drops {tdrop.m_dropWhenDestroyed.m_drops.Count}");
                    foreach (DropTable.DropData drop in tdrop.m_dropWhenDestroyed.m_drops) {
                        ExtendedObjectDrop eodrop = new ExtendedObjectDrop() {
                            Drop = new DataObjects.Drop {
                                Min = drop.m_stackMin,
                                Max = drop.m_stackMax,
                                Chance = tdrop.m_dropWhenDestroyed.m_dropChance,
                                LevelMultiplier = false, // No static drop types use levels by default so this field does not exist
                                DontScale = drop.m_dontScale,
                            }
                        };
                        if (drop.m_item != null) {
                            eodrop.Drop.Prefab = drop.m_item.name;
                        }
                        extendedDrops.Add(eodrop);
                    }
                    objectDrops.Add(name, extendedDrops);
                }

                Logger.LogDebug($"Serializing data");
                LootSettings lootSettings = new LootSettings();
                lootSettings.CharacterSpecificLoot = characterModDrops;
                lootSettings.NonCharacterSpecificLoot = objectDrops;
                var yaml = DataObjects.yamlSerializer.Serialize(lootSettings);
                Logger.LogDebug($"Writing file to disk");
                using (StreamWriter writetext = new StreamWriter(dumpfile))
                {
                    writetext.WriteLine(yaml);
                }
            }
        }
    }
}
