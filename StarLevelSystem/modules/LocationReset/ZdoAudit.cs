using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.modules.LocationReset {
    // Safety net and diagnostic for world bloat.
    //
    // A reset system that runs for months only stays safe if every regeneration is balanced. The
    // sweep's own before/after ZDO accounting catches drift as it happens; this catches whatever
    // slipped through, and cleans up worlds already bloated by other reset mods.
    internal static class ZdoAudit {

        // Two objects of the same prefab this close together are the same object placed twice.
        // Vegetation placement is deterministic, so a genuine re-place lands exactly on the original.
        private const float DuplicateEpsilon = 0.25f;

        internal class AuditReport {
            internal int ZonesScanned;
            internal int DuplicatesFound;
            internal int DuplicatesRemoved;
            internal int ExtraTerrainCompilers;

            public override string ToString() {
                return $"Scanned {ZonesScanned} zones: {DuplicatesFound} duplicate objects found, " +
                       $"{DuplicatesRemoved} removed, {ExtraTerrainCompilers} surplus terrain compilers removed.";
            }
        }

        internal static AuditReport Run(Vector3 center, float radius, bool removeDuplicates) {
            AuditReport report = new AuditReport();
            if (ZDOMan.instance == null || ZoneSystem.instance == null) { return report; }

            Vector2i centerZone = ZoneSystem.GetZone(center);
            int span = Mathf.Max(0, Mathf.CeilToInt(radius / 64f));

            List<ZDO> buffer = new List<ZDO>();
            // prefab hash -> quantised position -> first ZDO seen there.
            Dictionary<int, Dictionary<Vector3Int, ZDO>> seen = new Dictionary<int, Dictionary<Vector3Int, ZDO>>();
            List<ZDO> doomed = new List<ZDO>();

            for (int dx = -span; dx <= span; dx++) {
                for (int dy = -span; dy <= span; dy++) {
                    Vector2i zone = new Vector2i(centerZone.x + dx, centerZone.y + dy);
                    buffer.Clear();
                    ZDOMan.instance.FindObjects(zone, buffer);
                    if (buffer.Count == 0) { continue; }
                    report.ZonesScanned++;

                    seen.Clear();
                    int terrainCompilers = 0;

                    for (int i = 0; i < buffer.Count; i++) {
                        ZDO zdo = buffer[i];
                        if (zdo == null || zdo.IsValid() == false) { continue; }

                        // Exactly one terrain compiler per zone; extras corrupt terrain edits.
                        if (zdo.m_prefab == ZoneProtectionScan.TerrainCompilerHash) {
                            terrainCompilers++;
                            if (terrainCompilers > 1) {
                                report.ExtraTerrainCompilers++;
                                if (removeDuplicates) { doomed.Add(zdo); }
                            }
                            continue;
                        }

                        // Never touch player property or the structural objects.
                        if (zdo.m_prefab == ZoneProtectionScan.ZoneCtrlHash) { continue; }
                        if (zdo.m_prefab == ZoneProtectionScan.LocationProxyHash) { continue; }
                        if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L) { continue; }

                        Vector3 pos = zdo.GetPosition();
                        Vector3Int key = Quantise(pos);
                        if (seen.TryGetValue(zdo.m_prefab, out Dictionary<Vector3Int, ZDO> byPos) == false) {
                            byPos = new Dictionary<Vector3Int, ZDO>();
                            seen[zdo.m_prefab] = byPos;
                        }
                        if (byPos.ContainsKey(key)) {
                            report.DuplicatesFound++;
                            if (removeDuplicates) { doomed.Add(zdo); }
                        } else {
                            byPos[key] = zdo;
                        }
                    }
                }
            }

            for (int i = 0; i < doomed.Count; i++) {
                ZDO zdo = doomed[i];
                if (zdo == null || zdo.IsValid() == false) { continue; }
                if (zdo.IsOwner() == false) { zdo.SetOwner(ZDOMan.GetSessionID()); }
                if (ZNetScene.instance != null && ZNetScene.instance.m_instances.TryGetValue(zdo, out ZNetView view) && view != null) {
                    ZNetScene.instance.Destroy(view.gameObject);
                } else {
                    ZDOMan.instance.DestroyZDO(zdo);
                }
                report.DuplicatesRemoved++;
            }

            return report;
        }

        private static Vector3Int Quantise(Vector3 pos) {
            float inv = 1f / DuplicateEpsilon;
            return new Vector3Int(
                Mathf.RoundToInt(pos.x * inv),
                Mathf.RoundToInt(pos.y * inv),
                Mathf.RoundToInt(pos.z * inv));
        }
    }
}
