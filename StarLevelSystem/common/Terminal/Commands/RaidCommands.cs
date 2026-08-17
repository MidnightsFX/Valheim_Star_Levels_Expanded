using StarLevelSystem.Data;
using StarLevelSystem.modules.Raids;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterRaidCommands()
        {
            // Raid dispatch branches on IsDedicated and walks ZNet.GetPeers, neither of which means anything on
            // a client, so this has to run on the server. The relay carries the caller's position across as
            // SLSCommandArgs.Center, which is how a headless server learns where "here" is.
            _ = new SLSCommand("sls-raid-spawn",
                "Format: [required: raid name] [optional: x z] Force-starts a raid at a location, ignoring cooldowns " +
                "and activation requirements. Defaults to your position. eg: sls-raid-spawn army_eikthyr",
                RaidSpawn, CommandArea.Raid, RaidSpawnOptions,
                serverAuthoritative: true, requiresAdmin: true);
        }

        private static List<string> RaidSpawnOptions(string[] input)
        {
            // input[0] is the command name, so the argument at index N is input[N + 1].
            if (input.Length <= 2) { return RaidsData.RaidsByName.Keys.OrderBy(name => name).ToList(); }
            if (input.Length > 4) { return new List<string>(); }
            // Coordinates: hint the axis being typed with wherever the player is standing. Completion always runs
            // client-side, so there is a local player here even when the command itself targets a dedicated server.
            if (Player.m_localPlayer == null) { return new List<string>(); }
            Vector3 pos = Player.m_localPlayer.transform.position;
            return new List<string>() { Mathf.Round(input.Length == 3 ? pos.x : pos.z).ToString("0") };
        }

        private static void RaidSpawn(SLSCommandArgs args)
        {
            if (ValConfig.UseVanillaRaidConfiguration.Value)
            {
                args.Output.Error("SLS raids are disabled (UseVanillaRaidConfiguration is on); use the vanilla 'event' command instead.");
                return;
            }
            if (RaidsData.RaidsByName.Count == 0)
            {
                args.Output.Error("No raids are defined, so there is nothing to start. Check RaidSettings.yaml.");
                return;
            }

            RaidDefinition raid = ResolveRaid(args);
            if (raid == null) { return; }

            if (TryResolveRaidPosition(args, out Vector3 pos) == false) { return; }

            // Deliberately warnings rather than refusals: force-spawning is how you test a raid before turning
            // it on, so a disabled raid is a thing to flag, not a thing to block.
            if (raid.Enabled == false)
            {
                args.Output.Warning($"Raid '{raid.Name}' is disabled in the configuration; starting it anyway.");
            }
            if (RaidsData.SLE_Raid_Settings.GlobalSettings != null && RaidsData.SLE_Raid_Settings.GlobalSettings.DisableAllRaids)
            {
                args.Output.Warning("DisableAllRaids is on, so no raids will start on their own; starting this one anyway.");
            }
            WarnIfNobodyIsNearby(args, raid, pos);

            if (RaidControl.DispatchForcedRaid(raid, pos, skipCooldown: true) == false)
            {
                args.Output.Error($"Could not start '{raid.Name}': no connected client was available to run it. A raid needs a player whose game has that area loaded.");
                return;
            }
            args.Output.Info($"Force-started raid '{raid.Name}' at {Describe(pos)} ({BiomeAt(pos)}). Cooldowns were ignored and none were set.");
        }

        private static RaidDefinition ResolveRaid(SLSCommandArgs args)
        {
            string name = args.Args.GetString(0, null);
            if (string.IsNullOrEmpty(name))
            {
                args.Output.Error($"A raid name is required. eg: sls-raid-spawn {RaidsData.RaidsByName.Keys.First()}");
                args.Output.Detail($"Known raids: {KnownRaidNames()}");
                return null;
            }
            if (RaidsData.RaidsByName.TryGetValue(name, out RaidDefinition raid)) { return raid; }

            // Raid names are typed by hand as often as they are tab-completed, so don't fail on casing alone.
            foreach (KeyValuePair<string, RaidDefinition> entry in RaidsData.RaidsByName)
            {
                if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)) { return entry.Value; }
            }

            args.Output.Error($"No raid named '{name}'.");
            args.Output.Detail($"Known raids: {KnownRaidNames()}");
            return null;
        }

        private static string KnownRaidNames()
        {
            return string.Join(", ", RaidsData.RaidsByName.Keys.OrderBy(name => name));
        }

        // No coordinates: the caller's position. Otherwise x and z, with the height taken from world generation
        // rather than a live Heightmap, so it also answers for zones a headless server has not loaded.
        private static bool TryResolveRaidPosition(SLSCommandArgs args, out Vector3 pos)
        {
            pos = Vector3.zero;

            // Arguments are [name] [x] [z]; the name has already been validated by the time we get here.
            if (args.Length <= 1)
            {
                if (args.HasCenter == false)
                {
                    args.Output.Error("This console has no player position, so an x and z are required. eg: sls-raid-spawn army_eikthyr 1200 -430");
                    return false;
                }
                pos = args.Center;
                return true;
            }
            if (args.Length == 2)
            {
                args.Output.Error("Both an x and a z are required when giving a location. eg: sls-raid-spawn army_eikthyr 1200 -430");
                return false;
            }

            if (float.TryParse(args.Args.GetString(1, null), out float x) == false ||
                float.TryParse(args.Args.GetString(2, null), out float z) == false)
            {
                // Falling back to 0 here would quietly drop the raid on the world origin.
                args.Output.Error($"'{args.Args.GetString(1)} {args.Args.GetString(2)}' is not a coordinate pair; x and z must both be numbers.");
                return false;
            }
            if (args.Length > 3)
            {
                args.Output.Warning("Only a raid name, an x and a z are used; the extra arguments were ignored.");
            }

            float y = 0f;
            if (WorldGenerator.instance != null)
            {
                y = WorldGenerator.instance.GetHeight(x, z);
            }
            if (ZoneSystem.instance != null)
            {
                // A point out at sea would otherwise sit on the seabed, which is not somewhere a raid can run.
                y = Mathf.Max(y, ZoneSystem.instance.m_waterLevel);
            }
            pos = new Vector3(x, y, z);
            return true;
        }

        // A RaidRunner lives on a client, so a location nobody has loaded produces a raid that dispatches and
        // then does nothing visible. That is by far the most likely reason a far-flung coordinate looks broken.
        private static void WarnIfNobodyIsNearby(SLSCommandArgs args, RaidDefinition raid, Vector3 pos)
        {
            float range = raid.EventRange * 4f;
            if (Player.m_localPlayer != null && Utils.DistanceXZ(Player.m_localPlayer.transform.position, pos) <= range) { return; }

            ZNetPeer nearest = SLSExtensions.GetNearestReadyPeer(pos);
            if (nearest != null && Utils.DistanceXZ(nearest.m_refPos, pos) <= range) { return; }

            args.Output.Warning($"No player is within {range:0}m of {Describe(pos)}. The raid will be handed to the nearest client, but it may not spawn anything until someone is close enough to load the area.");
        }

        private static string Describe(Vector3 pos) => $"x {pos.x:0} y {pos.y:0} z {pos.z:0}";

        // World generation rather than Heightmap.FindBiome: a headless server has no loaded Heightmap for an
        // arbitrary point and would report None. Same source RaidControl.GetValidRaidsForPlayer uses.
        private static string BiomeAt(Vector3 pos)
        {
            if (WorldGenerator.instance == null) { return "unknown biome"; }
            return WorldGenerator.instance.GetBiome(pos).ToString();
        }
    }
}
