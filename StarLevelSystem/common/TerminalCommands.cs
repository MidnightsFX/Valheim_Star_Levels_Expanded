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
                Dictionary<string, List<ExtendedDrop>> characterModDrops = new Dictionary<string, List<ExtendedDrop>>();
                foreach (var chardrop in Resources.FindObjectsOfTypeAll<CharacterDrop>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList<CharacterDrop>())
                {
                    Logger.LogDebug($"Checking {chardrop.name} for loot tables");
                    string name = chardrop.name;
                    if (characterModDrops.ContainsKey(name)) { continue; }
                    Logger.LogDebug($"checking {name}");
                    var extendedDrops = new List<ExtendedDrop>();
                    Logger.LogDebug($"drops {chardrop.m_drops.Count}");
                    foreach (var drop in chardrop.m_drops)
                    {
                        var extendedDrop = new ExtendedDrop
                        {
                            Drop = new DataObjects.Drop
                            {
                                Prefab = drop.m_prefab.name,
                                Min = drop.m_amountMin,
                                Max = drop.m_amountMax,
                                Chance = drop.m_chance,
                                OnePerPlayer = drop.m_onePerPlayer,
                                LevelMultiplier = drop.m_levelMultiplier,
                                DontScale = drop.m_dontScale
                            }
                        };
                        extendedDrops.Add(extendedDrop);
                    }
                    characterModDrops.Add(name, extendedDrops);
                    Logger.LogDebug($"Adding {name} loot-table");
                }
                Logger.LogDebug($"Serializing data");
                var yaml = DataObjects.yamlserializer.Serialize(characterModDrops);
                Logger.LogDebug($"Writing file to disk");
                using (StreamWriter writetext = new StreamWriter(dumpfile))
                {
                    writetext.WriteLine(yaml);
                }
            }
        }
    }
}
