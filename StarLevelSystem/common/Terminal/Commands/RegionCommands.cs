namespace StarLevelSystem.common
{
    internal static partial class TerminalManager
    {
        private static void RegisterRegionCommands()
        {
            _ = new SLSCommand("sls-region-rebuild",
                "Cuts the region map again from the world with the current size settings, then sends it to everyone. Resets region kill counts and levels.",
                RegionRebuild, CommandArea.Zone,
                isCheat: false,
                serverAuthoritative: true,
                requiresAdmin: true);
        }

        private static void RegionRebuild(SLSCommandArgs args)
        {
            if (modules.LevelSystem.RegionScaleSystem.RebuildRegions(out string outcome))
            {
                args.Output.Info(outcome);
            }
            else
            {
                args.Output.Error(outcome);
            }
        }
    }
}
