using BepInEx;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterLootCommands()
        {
            _ = new SLSCommand("sls-loot-dump",
                "Writes all creature and object loot-tables to a debug file.",
                LootDump, CommandArea.Loot,
                isCheat: true,
                aliases: "SLS-Dump-LootTables");
        }

        private static void LootDump(SLSCommandArgs args)
        {
            string dumpfile = Path.Combine(Paths.ConfigPath, "StarLevelSystem", "LootTablesDump.yaml");
            Dictionary<string, List<ExtendedCharacterDrop>> characterModDrops = new Dictionary<string, List<ExtendedCharacterDrop>>();
            foreach (var chardrop in Resources.FindObjectsOfTypeAll<CharacterDrop>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList<CharacterDrop>())
            {
                Logger.LogDebug($"Checking {chardrop.name} for loot tables");
                string name = chardrop.name;
                if (characterModDrops.ContainsKey(name)) { continue; }
                var extendedDrops = new List<ExtendedCharacterDrop>();
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
            }

            Dictionary<string, List<ExtendedObjectDrop>> objectDrops = new Dictionary<string, List<ExtendedObjectDrop>>();
            foreach (TreeLog tdrop in Resources.FindObjectsOfTypeAll<TreeLog>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList()) {
                string name = Utils.GetPrefabName(tdrop.gameObject);
                if (objectDrops.ContainsKey(name)) { continue; }
                List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
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

            foreach (MineRock5 tdrop in Resources.FindObjectsOfTypeAll<MineRock5>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList()) {
                string name = Utils.GetPrefabName(tdrop.gameObject);
                if (objectDrops.ContainsKey(name)) { continue; }
                List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
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

            foreach (MineRock tdrop in Resources.FindObjectsOfTypeAll<MineRock>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true).ToList()) {
                string name = Utils.GetPrefabName(tdrop.gameObject);
                if (objectDrops.ContainsKey(name)) { continue; }
                List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
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

            foreach (DropOnDestroyed tdrop in Resources.FindObjectsOfTypeAll<DropOnDestroyed>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true && !cdrop.name.Contains(" ")).ToList()) {
                string name = Utils.GetPrefabName(tdrop.gameObject);
                if (objectDrops.ContainsKey(name)) { continue; }
                List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
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

            foreach (TreeBase tdrop in Resources.FindObjectsOfTypeAll<TreeBase>().Where(cdrop => cdrop.name.EndsWith("(Clone)") != true && !cdrop.name.Contains(" ")).ToList()) {
                string name = Utils.GetPrefabName(tdrop.gameObject);
                if (objectDrops.ContainsKey(name)) { continue; }
                List<ExtendedObjectDrop> extendedDrops = new List<ExtendedObjectDrop>();
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

            LootSettings lootSettings = new LootSettings();
            lootSettings.CharacterSpecificLoot = characterModDrops;
            lootSettings.NonCharacterSpecificLoot = objectDrops;
            var yaml = DataObjects.yamlSerializer.Serialize(lootSettings);
            using (StreamWriter writetext = new StreamWriter(dumpfile))
            {
                writetext.WriteLine(yaml);
            }
            args.Output.Info($"Wrote {characterModDrops.Count} creature and {objectDrops.Count} object loot-tables to {dumpfile}");
        }
    }
}
