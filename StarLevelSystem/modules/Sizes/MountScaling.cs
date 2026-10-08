using StarLevelSystem.common;
using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.modules.Sizes {
    // Keeps rideable creatures (anything carrying a Sadle: Lox, Asksvin, modded mounts) usable once SLS scales them up.
    // Vanilla tunes the saddle for the prefab's own size:
    //  - Sadle.InUseDistance needs the player's feet within m_maxUseRange (6 on the vanilla mounts) of the seat, and
    //    Sadle.CalculateHaveValidUser drops a rider who is farther than that from the mount's root, so a big enough
    //    mount can neither be mounted nor steered.
    //  - Player.FindHoverObject only offers the saddle within m_maxInteractDistance (5) + Sadle.m_hoverOffset (0) of the eye.
    //  - Player.AttachStop drops the rider at the seat + m_detachOffset, and fall damage starts past a 4 m drop. The
    //    vanilla Lox seat is already about 3.4 m up, so any growth at all hurts on every dismount.
    //  - Character.GetSlideAngle gives every ridden creature the same 45 degree slope limit, whatever its size.
    // All of it is local, per-instance state on each peer: nothing here writes a ZDO.
    internal static class MountScaling {

        // A seat less than this many metres above the prefab's counts as vanilla size, and keeps vanilla's dismount.
        private const float RaisedSeatThreshold = 0.25f;
        // The slope a ridden creature may climb rises with its size toward this, from vanilla's 45 degrees.
        private const float MaxRiddenSlideAngle = 65f;
        // How far below the mount's feet the ground beside it may lie before that side counts as a ledge.
        private const float MaxDismountStepDown = 2f;
        // Space between the mount's body and a dismounted rider, on top of the rider's radius.
        private const float DismountClearance = 0.3f;

        // The prefab's saddle values, and its seat and body in the prefab root's local (unscaled) units.
        private sealed class MountBase {
            internal float UseRange;
            internal float HoverOffset;
            internal float SeatHeight;
            // From the ground beside the body to the seat, or root to seat if that is longer: how far a rider has to
            // reach, and how far CalculateHaveValidUser measures, both growing with the mount.
            internal float SeatReach;
            internal float BodyHalfWidth;
            internal float BodyHalfLength;
        }

        // Keyed by prefab name. A null entry is a prefab without a usable saddle (or one that could not be found).
        private static readonly Dictionary<string, MountBase> MountBases = new Dictionary<string, MountBase>();

        // Size multiplier of each loaded saddle-carrying creature that is bigger than its prefab. Read by the slide
        // angle postfix on every physics step of a ridden creature, so that lookup must not allocate: keyed by full
        // ZDOID (a bare ID collides across peers), dropped with the other per-ZDOID caches in CleanupDeletedCreatures.
        private static readonly Dictionary<ZDOID, float> MountSizeMultipliers = new Dictionary<ZDOID, float>();

        // The camera distance currently added for the local player's mount, and the mount it was measured on.
        private static GameCamera cameraWithBonus;
        private static float appliedCameraBonus;
        private static Sadle cameraSaddle;
        private static float cameraSaddleBonus;

        internal static Sadle FindSaddle(GameObject creature) {
            Tameable tame = creature.GetComponent<Tameable>();
            if (tame != null && tame.m_saddle != null) { return tame.m_saddle; }
            return creature.GetComponentInChildren<Sadle>(true);
        }

        internal static void Forget(ZDOID id) {
            MountSizeMultipliers.Remove(id);
        }

        // Sets the saddle's reach from the prefab's values plus however much farther away the seat now is. Runs on every
        // setup pass of every creature, and when one is tamed or saddled, so anything without a saddle leaves on the
        // cached prefab check before any hierarchy search. Never below vanilla: a shrunk mount keeps the prefab's reach.
        internal static void ApplySaddleScaling(GameObject creature) {
            string prefabName = Utils.GetPrefabName(creature);
            MountBase mount = GetMountBase(prefabName);
            if (mount == null) { return; }
            Sadle saddle = FindSaddle(creature);
            if (saddle == null) { return; }

            Vector3 reference = SizeModifications.GetSizeReferenceForObject(prefabName);
            Vector3 scale = creature.transform.localScale;
            float extraReach = mount.SeatReach * Mathf.Max(0f, scale.y - reference.y);
            saddle.m_maxUseRange = mount.UseRange + extraReach;
            saddle.m_hoverOffset = mount.HoverOffset + extraReach;

            ZNetView nview = creature.GetComponent<ZNetView>();
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            if (zdo != null) {
                float multiplier = reference.x > 0f ? scale.x / reference.x : 1f;
                if (multiplier > 1.001f) {
                    MountSizeMultipliers[zdo.m_uid] = multiplier;
                } else {
                    MountSizeMultipliers.Remove(zdo.m_uid);
                }
            }

            // A mount resized under its rider gets its camera distance measured again.
            if (saddle == cameraSaddle) { cameraSaddle = null; }
        }

        // How many metres the seat sits above where the prefab's seat would be. Negative for a shrunk mount.
        internal static float SeatRaise(Sadle saddle) {
            Character mountCharacter = saddle.m_character;
            if (mountCharacter == null) { return 0f; }
            string prefabName = Utils.GetPrefabName(mountCharacter.gameObject);
            MountBase mount = GetMountBase(prefabName);
            if (mount == null) { return 0f; }
            float growth = mountCharacter.transform.localScale.y - SizeModifications.GetSizeReferenceForObject(prefabName).y;
            return mount.SeatHeight * growth;
        }

        // Rises with the creature's size and levels off toward the cap: 55 degrees at twice the size, about 58 at three.
        // Called from GetSlideAngle on every physics step of a ridden creature, so it must not allocate.
        internal static float RiddenSlideAngle(Character mount, float vanillaAngle) {
            ZNetView nview = mount.m_nview;
            if (nview == null) { return vanillaAngle; }
            ZDO zdo = nview.m_zdo;
            if (zdo == null || !MountSizeMultipliers.TryGetValue(zdo.m_uid, out float multiplier)) { return vanillaAngle; }
            return Mathf.Lerp(vanillaAngle, MaxRiddenSlideAngle, 1f - 1f / multiplier);
        }

        // Before Player.AttachStop: the saddle the local player is getting off, when its seat sits meaningfully above
        // the prefab's. Null keeps vanilla's dismount.
        internal static Sadle RaisedSaddleBeingLeft(Player player) {
            if (ValConfig.EnableRidableCreatureSizeFixes.Value == false) { return null; }
            if (player != Player.m_localPlayer || player.m_sleeping || player.m_attached == false || player.m_attachPoint == null) { return null; }
            Sadle saddle = player.m_doodadController as Sadle;
            if (saddle == null || saddle.m_attachPoint != player.m_attachPoint) {
                Tameable tame = player.m_attachPoint.GetComponentInParent<Tameable>();
                saddle = tame != null ? tame.m_saddle : null;
                if (saddle == null || saddle.m_attachPoint != player.m_attachPoint) { return null; }
            }
            return SeatRaise(saddle) > RaisedSeatThreshold ? saddle : null;
        }

        // After Player.AttachStop: moves the rider from vanilla's drop point on top of the mount to the ground beside it.
        // Tries the mount's right, then its left, then behind it; keeps vanilla's drop when none of them has level
        // ground (a ledge, a tight build) or the mount is swimming, where the water takes the fall anyway.
        internal static void PlaceBesideMount(Player player, Sadle saddle) {
            if (player.m_attached || ZoneSystem.instance == null) { return; }
            Character mountCharacter = saddle.m_character;
            if (mountCharacter == null || mountCharacter.IsSwimming()) { return; }
            MountBase mount = GetMountBase(Utils.GetPrefabName(mountCharacter.gameObject));
            if (mount == null) { return; }

            Transform root = mountCharacter.transform;
            float scale = root.localScale.x;
            float clearance = player.GetRadius() + DismountClearance;
            Vector3 right = Flat(root.right, Vector3.right);
            Vector3 back = -Flat(root.forward, Vector3.forward);
            float side = mount.BodyHalfWidth * scale + clearance;
            float rear = mount.BodyHalfLength * scale + clearance;
            float seatY = saddle.m_attachPoint.position.y;

            if (!TryGroundBeside(root, right * side, seatY, out Vector3 spot) &&
                !TryGroundBeside(root, -right * side, seatY, out spot) &&
                !TryGroundBeside(root, back * rear, seatY, out spot)) {
                Logger.LogDebug($"No level ground beside {mountCharacter.name} to dismount onto, using the vanilla drop.");
                return;
            }

            player.transform.position = spot;
            // The last UpdateAttach left m_maxAirAltitude at the seat, and the first landing measures the fall from it,
            // so moving the rider has to reset it the way vanilla's teleport does.
            player.m_maxAirAltitude = spot.y;
        }

        // Called before GameCamera.UpdateCamera clamps the zoom: while the local player rides a raised mount the
        // camera may pull back as far as the seat went up. Tracks what it added, so it comes off again on dismount,
        // death or a config change, and a new camera starts clean.
        internal static void UpdateCameraDistance(GameCamera camera) {
            Sadle riding = null;
            Player player = Player.m_localPlayer;
            if (ValConfig.EnableRidableCreatureSizeFixes.Value && player != null && player.m_attached) {
                Sadle saddle = player.m_doodadController as Sadle;
                if (saddle != null && saddle.m_attachPoint == player.m_attachPoint) { riding = saddle; }
            }
            // Measured once per ride (SeatRaise allocates), and again if the mount is resized under its rider.
            float wanted = 0f;
            if (riding == null) {
                cameraSaddle = null;
            } else {
                if (riding != cameraSaddle) {
                    cameraSaddle = riding;
                    cameraSaddleBonus = Mathf.Max(0f, SeatRaise(riding));
                }
                wanted = cameraSaddleBonus;
            }

            if (camera != cameraWithBonus) {
                cameraWithBonus = camera;
                appliedCameraBonus = 0f;
            }
            if (wanted == appliedCameraBonus) { return; }
            camera.m_maxDistance += wanted - appliedCameraBonus;
            appliedCameraBonus = wanted;
        }

        private static bool TryGroundBeside(Transform root, Vector3 offset, float seatY, out Vector3 spot) {
            // From seat height down: above the ground the mount stands on, but under any roof over the mount.
            Vector3 probe = root.position + offset;
            probe.y = seatY;
            float reach = seatY - root.position.y + MaxDismountStepDown;
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, reach, ZoneSystem.instance.m_solidRayMask) == false) {
                spot = Vector3.zero;
                return false;
            }
            spot = hit.point;
            // Deep water: the rider would be put down on the bottom.
            return Floating.GetLiquidLevel(spot) < spot.y + 1f;
        }

        private static Vector3 Flat(Vector3 direction, Vector3 fallback) {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
        }

        private static MountBase GetMountBase(string prefabName) {
            if (MountBases.TryGetValue(prefabName, out MountBase cached)) { return cached; }
            // Quiet: every creature passes through here, and a missing prefab was already reported by the reference
            // size lookup that sized it.
            GameObject prefab = PrefabLookup.Find(prefabName);
            if (prefab == null) {
                // Before a world loads ZNetScene does not exist yet, so a miss then is not final.
                if (ZNetScene.instance != null) { MountBases[prefabName] = null; }
                return null;
            }

            MountBase mount = null;
            Sadle saddle = FindSaddle(prefab);
            if (saddle != null && saddle.m_attachPoint != null) {
                Transform root = prefab.transform;
                Vector3 seat = root.InverseTransformPoint(saddle.m_attachPoint.position);
                // Character.m_collider is only filled in Awake, which a prefab never runs.
                CapsuleCollider body = prefab.GetComponent<CapsuleCollider>();
                float halfWidth = body != null ? CapsuleReach(body, 0) : 1f;
                float halfLength = body != null ? CapsuleReach(body, 2) : 1f;
                mount = new MountBase {
                    UseRange = saddle.m_maxUseRange,
                    HoverOffset = saddle.m_hoverOffset,
                    SeatHeight = seat.y,
                    SeatReach = Mathf.Max(seat.magnitude, new Vector2(seat.y, halfWidth).magnitude),
                    BodyHalfWidth = halfWidth,
                    BodyHalfLength = halfLength,
                };
            }
            MountBases[prefabName] = mount;
            return mount;
        }

        // How far the capsule reaches from the root along one of its local axes (0 = x, 2 = z).
        private static float CapsuleReach(CapsuleCollider capsule, int axis) {
            float reach = capsule.direction == axis ? Mathf.Max(capsule.radius, capsule.height * 0.5f) : capsule.radius;
            return reach + Mathf.Abs(capsule.center[axis]);
        }
    }
}
