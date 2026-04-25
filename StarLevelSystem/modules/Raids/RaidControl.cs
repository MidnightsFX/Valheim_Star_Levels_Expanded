using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Raids
{
    internal static class RaidControl
    {

        internal static void ApplyRaidConfiguration(RandEventSystem res) {
            if (res == null) { return; }
            if (ValConfig.UseVanillaRaidConfiguration != null && ValConfig.UseVanillaRaidConfiguration.Value) {
                Logger.LogDebug("UseVanillaRaidConfiguration is true; leaving vanilla raid list untouched.");
                return;
            }

            RaidConfiguration cfg = RaidsData.SLE_Raid_Settings ?? RaidsData.DefaultConfiguration;

            if (cfg.GlobalSettings != null) {
                if (cfg.GlobalSettings.GlobalRaidIntervalScalar > 0f) {
                    res.m_eventIntervalMin *= cfg.GlobalSettings.GlobalRaidIntervalScalar;
                }
                if (cfg.GlobalSettings.GlobalRaidChanceScalar > 0f) {
                    res.m_eventChance *= cfg.GlobalSettings.GlobalRaidChanceScalar;
                }
                if (cfg.GlobalSettings.DisableAllRaids) {
                    res.m_events.Clear();
                    Logger.LogInfo("SLS raid system: DisableAllRaids set, cleared all random events.");
                    return;
                }
            }

            if (cfg.Raids == null || cfg.Raids.Count == 0) {
                Logger.LogDebug("SLS raid system: no raids configured, leaving vanilla raid list untouched.");
                return;
            }

            List<RandomEvent> newEvents = new List<RandomEvent>();
            foreach (KeyValuePair<string, RaidDefinition> kv in cfg.Raids) {
                if (kv.Value == null || kv.Value.Enabled == false) { continue; }
                newEvents.Add(kv.Value.ToRaid(kv.Key, Vector3.zero));
            }

            res.m_events = newEvents;
            Logger.LogInfo($"SLS raid system: applied {newEvents.Count} raid definitions.");
        }

        internal static RaidDefinition GetActiveRaidForPosition(Vector3 pos) {
            if (RandEventSystem.instance == null) { return null; }
            RandomEvent re = RandEventSystem.instance.GetCurrentRandomEvent();
            if (re == null) { return null; }
            if (!RandEventSystem.instance.IsInsideRandomEventArea(re, pos)) { return null; }

            RaidConfiguration cfg = RaidsData.SLE_Raid_Settings;
            if (cfg == null || cfg.Raids == null) { return null; }
            if (!cfg.Raids.TryGetValue(re.m_name, out RaidDefinition def)) { return null; }
            return def;
        }
    }
}
