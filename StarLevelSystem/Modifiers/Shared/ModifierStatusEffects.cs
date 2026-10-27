using Jotunn.Entities;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers.Shared
{
    // Status effects the modifiers hand to their victims. Registered once from Awake; Jotunn adds them to every ObjectDB
    // the game builds, so a hit naming one by hash finds it on every peer.
    internal static class ModifierStatusEffects
    {
        internal const string LaunchedName = "SE_SLS_Launched";
        internal static readonly int LaunchedHash = LaunchedName.GetStableHashCode();

        internal static void Register() {
            SE_SLSLaunched launched = ScriptableObject.CreateInstance<SE_SLSLaunched>();
            launched.name = LaunchedName;
            launched.m_name = "$se_sls_launched";
            launched.m_ttl = 0.2f;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(launched, fixReference: false));
        }
    }

    // Throws its target into the air the moment it lands: Earth Caller's spike carries it on its hit.
    //
    // Launching is done here rather than by the spike's Aoe (m_launchCharacters) because the Aoe runs on the boss's owner
    // and calls ForceJump right there, which only moves characters that machine simulates. Vanilla adds a hit's status
    // effect on the target's own machine (Character.RPC_Damage), so this launches every player, whoever owns the boss.
    // A dodged hit never arrives, so a dodge roll avoids the launch along with the damage.
    internal class SE_SLSLaunched : StatusEffect
    {
        private const float DefaultLaunchSpeed = 8f;

        public override void Setup(Character character) {
            base.Setup(character);
            if (character == null || character.IsOwner() == false || character.IsDead()) { return; }
            // Read from the synced Modifiers.yaml, so it is the server's number on every machine.
            CreatureModConfig cfg = CreatureModifiersData.GetConfig(nameof(ModifierNames.EarthCaller), ModifierType.Boss);
            float speed = Mathf.Max(0f, cfg.Get("LaunchSpeed", DefaultLaunchSpeed));
            if (speed > 0f) { character.ForceJump(Vector3.up * speed, effects: false); }
        }
    }
}
