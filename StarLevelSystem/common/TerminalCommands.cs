using BepInEx;
using Jotunn.Entities;
using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
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
                List<Character> nearbyCreatures = Extensions.GetCharactersInRange(Player.m_localPlayer.transform.position, range);
                foreach (Character chara in nearbyCreatures) {
                    if (chara.IsPlayer() || chara.IsTamed()) { continue; }

                    CharacterDrop cdrop = chara.gameObject.GetComponent<CharacterDrop>();
                    if (cdrop != null) {
                        GameObject.Destroy(cdrop);
                    }
                    if (chara != null) {
                        GameObject.Destroy(chara.gameObject);
                    }
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

                
                List<Character> nearbyCreatures = Extensions.GetCharactersInRange(Player.m_localPlayer.transform.position, 5f);
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
                lootSettings.characterSpecificLoot = characterModDrops;
                lootSettings.nonCharacterSpecificLoot = objectDrops;
                var yaml = DataObjects.yamlserializer.Serialize(lootSettings);
                Logger.LogDebug($"Writing file to disk");
                using (StreamWriter writetext = new StreamWriter(dumpfile))
                {
                    writetext.WriteLine(yaml);
                }
            }
        }
    }
}
