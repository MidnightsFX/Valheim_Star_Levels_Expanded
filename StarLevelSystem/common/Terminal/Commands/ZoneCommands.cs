namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterZoneCommands()
        {
            _ = new SLSCommand("sls-zone-rebuild",
                "Clears existing zones and regenerates the zone map from the world, then redraws the minimap overlay. Resets zone kill counts and levels.",
                ZoneRebuild, CommandArea.Zone,
                isCheat: true,
                aliases: "SLS-rebuild-zones");
        }

        private static void ZoneRebuild(SLSCommandArgs args)
        {
            modules.LevelSystem.ZoneScaleSystem.RebuildZones();
            args.Output.Info("Rebuilt the zone map and redrew the minimap overlay. Zone kill counts and levels are reset.");
        }
    }
}
