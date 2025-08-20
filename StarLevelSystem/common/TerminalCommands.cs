using Jotunn.Entities;
using Jotunn.Managers;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    internal static class TerminalCommands
    {
        internal static void AddCommands()
        {
            CommandManager.Instance.AddConsoleCommand(new ResetZOIDModifiers());
        }

        internal class ResetZOIDModifiers : ConsoleCommand
        {
            public override string Name => "SLS-reset-player-modifiers";

            public override string Help => "Resets all of the modified damage, movementspeed, scale, health values that are assigned to the player.";

            public override void Run(string[] args)
            {
                var id = Player.m_localPlayer.GetZDOID().ID;
                if (CompositeLazyCache.sessionCache.ContainsKey(id)) {
                    CompositeLazyCache.sessionCache.Remove(id);
                    Logger.LogInfo($"Removed Cached modifiers {id}");
                }
                // Set damage modifier to 1
                Player.m_localPlayer.m_nview.GetZDO().Set("SLE_DMod", 1f);
                // Set base attribute modifers to 1
                DictionaryDmgNetProperty existingDmgMods = new DictionaryDmgNetProperty("SLE_DBon", Player.m_localPlayer.m_nview, new Dictionary<DamageType, float>());
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
    }
}
