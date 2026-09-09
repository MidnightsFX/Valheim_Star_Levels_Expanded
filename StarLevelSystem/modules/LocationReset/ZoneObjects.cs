using System.Collections.Generic;

namespace StarLevelSystem.modules.LocationReset {
    // Compatibility shim for ZDOMan.FindObjects.
    //
    // Valheim 1.0.7 added a third parameter, HashSet<ZoneSystem.SectorIndex> visitedSectorIndices,
    // which FindObjects both consults and adds to: a sector already in the set contributes nothing.
    // Vanilla uses that to sweep a neighbourhood without counting a sector twice, clearing one
    // reusable set at the start of each sweep.
    //
    // Every SLS call site means "give me the ZDOs in this sector", and several sit inside loops that
    // refill the same buffer once per zone - so a set shared across calls would silently yield nothing
    // for every zone after the first. The set is cleared per call instead, which reproduces the
    // pre-1.0.7 two-argument semantics exactly.
    //
    // One behaviour does change and cannot be avoided from out here: 1.0.7 also folded the sector's
    // m_portalObjects into FindObjects, so sweeps now see those ZDOs too. That is safe for the reset
    // path, which is fail-closed - a ZDO is destroyed only if it classifies AND matches an explicit
    // ignore list - but it is worth knowing when reading raw ZDO counts out of an audit.
    internal static class ZoneObjects {

        // Deliberately not [ThreadStatic]: every caller runs on the main thread, so one instance for
        // the lifetime of the process is enough and keeps this allocation-free per call.
        private static readonly HashSet<ZoneSystem.SectorIndex> visitedScratch = new HashSet<ZoneSystem.SectorIndex>();

        // Appends the ZDOs registered to `sector` onto `objects`, as the two-argument
        // ZDOMan.FindObjects did before 1.0.7. Callers own `objects` and clear it themselves.
        internal static void FindObjects(Vector2s sector, List<ZDO> objects) {
            if (ZDOMan.instance == null) { return; }
            visitedScratch.Clear();
            ZDOMan.instance.FindObjects(sector, objects, visitedScratch);
        }
    }
}
