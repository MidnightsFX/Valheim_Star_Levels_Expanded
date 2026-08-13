using System.Collections.Generic;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterPlayerCommands()
        {
            _ = new SLSCommand("sls-player-reset",
                "Resets all of the modified damage, movementspeed, scale and health values that are assigned to the player.",
                PlayerReset, CommandArea.Player,
                aliases: "SLS-reset-player-modifiers");
        }

        private static void PlayerReset(SLSCommandArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                args.Output.Error("This needs a local player.");
                return;
            }
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
            args.Output.Info($"Reset player {id}.");
        }
    }
}
