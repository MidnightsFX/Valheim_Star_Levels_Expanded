using BepInEx;
using Jotunn.Entities;
using StarLevelSystem.common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using static CharacterDrop;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    internal class Commands
    {
        internal class DumpLootTablesCommand : ConsoleCommand
        {
            public override string Name => "SLS_Dump_LootTables";

            public override string Help => "Writes all creature loot-tables to a debug file.";

            public override bool IsCheat => true;

            public override void Run(string[] args)
            {
                string dumpfile = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "LootTablesDump.yaml");
                Dictionary<string, List<ExtendedDrop>> characterModDrops = new Dictionary<string, List<ExtendedDrop>>();
                foreach (var chardrop in Resources.FindObjectsOfTypeAll<CharacterDrop>().Where( cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList<CharacterDrop>()) {
                    Logger.LogDebug($"Checking {chardrop.name} for loot tables");
                    string name = chardrop.name;
                    if (characterModDrops.ContainsKey(name)) { continue; }
                    Logger.LogDebug($"checking {name}");
                    var extendedDrops = new List<ExtendedDrop>();
                    Logger.LogDebug($"drops {chardrop.m_drops.Count}");
                    foreach (var drop in chardrop.m_drops) {
                        var extendedDrop = new ExtendedDrop {
                            Drop = new DataObjects.Drop {
                                prefab = drop.m_prefab.name,
                                min = drop.m_amountMin,
                                max = drop.m_amountMax,
                                chance = drop.m_chance,
                                onePerPlayer = drop.m_onePerPlayer,
                                levelMultiplier = drop.m_levelMultiplier,
                                dontScale = drop.m_dontScale
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
                using (StreamWriter writetext = new StreamWriter(dumpfile)) {
                    writetext.WriteLine(yaml);
                }
            }
        }
    }
}
