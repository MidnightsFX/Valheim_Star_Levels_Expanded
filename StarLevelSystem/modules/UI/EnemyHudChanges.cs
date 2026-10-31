using System;
using System.Collections.Generic;
using System.Linq;

namespace StarLevelSystem.modules.UI {
    // The changes SLS makes to the enemy hud that a UI mod laying out or drawing the hud itself can turn off
    // (API.SetEnemyHudChangesDisabled). The values are the API's EnemyHud* constants, so never renumber them.
    [Flags]
    internal enum EnemyHudChange {
        None = 0,
        // Scaling the normal health bar, and sizing, stacking and reparenting the boss bars with their name and background.
        BarLayout = 1,
        // The star per level, the numbered star and its modifier icons, and hiding the hud's own level_2 and level_3.
        Stars = 2,
        EvolveBar = 4,
        HealthNumbers = 8,
        All = BarLayout | Stars | EvolveBar | HealthNumbers,
    }

    internal static class EnemyHudChanges {
        // Each UI mod's set, by its plugin GUID. A set of None is kept, so Auga can also hand back the default it gets.
        private static readonly Dictionary<string, EnemyHudChange> DisabledBySource = new Dictionary<string, EnemyHudChange>();
        private static bool augaDefault;

        // Every mod's set together, plus BarLayout for Auga until it sets its own.
        internal static EnemyHudChange Disabled { get; private set; }

        // What the current enemy hud was built with, taken in its Awake (LatchForHudBuild). These decide which objects SLS
        // made and where, so a mod changing its set later reaches them on the next build, which every world load does.
        // HealthNumbers is read live: the numbers are made per hud and hidden again when turned off.
        internal static bool BarLayoutOff { get; private set; }
        internal static bool StarsOff { get; private set; }
        internal static bool EvolveBarOff { get; private set; }
        internal static bool HealthNumbersOff => (Disabled & EnemyHudChange.HealthNumbers) != 0;

        internal static bool Set(string sourceId, int changes) {
            if (string.IsNullOrEmpty(sourceId)) {
                Logger.LogWarning("SLS-API: SetEnemyHudChangesDisabled needs the calling plugin's GUID as sourceId; nothing was changed.");
                return false;
            }
            EnemyHudChange set = (EnemyHudChange)changes & EnemyHudChange.All;
            if ((int)set != changes) {
                Logger.LogWarning($"SLS-API: {sourceId} turned off enemy hud changes {changes & ~(int)EnemyHudChange.All} that Star Level System does not have; they were ignored.");
            }
            DisabledBySource[sourceId] = set;
            Refresh();
            Logger.LogDebug($"SLS-API: {sourceId} turned off enemy hud changes: {set}. Now off: {Disabled}.");
            return true;
        }

        internal static void Refresh() {
            EnemyHudChange disabled = EnemyHudChange.None;
            foreach (EnemyHudChange set in DisabledBySource.Values) { disabled |= set; }
            augaDefault = DisabledBySource.ContainsKey(Compatibility.AugaGUID) == false && Compatibility.IsAugaEnabled;
            if (augaDefault) { disabled |= EnemyHudChange.BarLayout; }
            Disabled = disabled;
        }

        // Called first thing in the EnemyHud.Awake postfix, ahead of every ShowHud and UpdateHuds of that hud.
        internal static void LatchForHudBuild() {
            Refresh();
            BarLayoutOff = (Disabled & EnemyHudChange.BarLayout) != 0;
            StarsOff = (Disabled & EnemyHudChange.Stars) != 0;
            EvolveBarOff = (Disabled & EnemyHudChange.EvolveBar) != 0;
            if (Disabled == EnemyHudChange.None) { return; }
            List<string> sources = DisabledBySource.Where(s => s.Value != EnemyHudChange.None).Select(s => $"{s.Key} ({s.Value})").ToList();
            if (augaDefault) { sources.Add($"{Compatibility.AugaGUID} ({EnemyHudChange.BarLayout}, by default)"); }
            Logger.LogInfo($"Enemy hud changes turned off for other UI mods: {string.Join("; ", sources)}.");
        }
    }
}
