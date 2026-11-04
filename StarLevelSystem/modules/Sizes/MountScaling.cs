using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Sizes {
    // Keeps rideable creatures usable once SLS scales them up. A mount is anything carrying a Sadle, so one from another mod
    // needs nothing registered: vanilla's Lox, Asksvin and Moose and most modded mounts have it on the prefab, and a mod can
    // also give each creature one as it wakes. RideableSizeFixes in a creature's LevelSettings entry turns all of this off for it.
    // Vanilla tunes the saddle for the prefab's own size:
    //  - Sadle.InUseDistance needs the player's feet within m_maxUseRange (6 on the vanilla mounts) of the seat, and
    //    Sadle.CalculateHaveValidUser drops a rider who is farther than that from the mount's root, so a big enough
    //    mount can neither be mounted nor steered.
    //  - Player.FindHoverObject only offers the saddle within m_maxInteractDistance (5) + Sadle.m_hoverOffset (0) of the eye,
    //    and only when the saddle is the first thing the look ray hits. On a big mount the body is, nearly always.
    //  - Player.AttachStop drops the rider at the seat + m_detachOffset, and fall damage starts past a 4 m drop. The
    //    vanilla Lox seat is already about 3.4 m up, so any growth at all hurts on every dismount.
    //  - Character.ApplySlide reads the slope from m_groundTiltNormal, which UpdateGroundTilt builds with
    //    InverseTransformVector. That divides by the scale, so a 2x mount's normal is half length and flat ground reads as
    //    60 degrees. Only a ridden creature has a slide limit it can pass (GetSlideAngle), and past it the velocity becomes
    //    the slide's, under 1 m/s. Any moment off the ground resets the normal, so whether a size crawls looks random.
    //  - Character.OnCollisionStay only counts a contact lower than the body's unscaled radius as ground, while the lowest
    //    point of a scaled body rises with the scale: its feet sink, and then it is never on the ground at all.
    // All of it is local, per-instance state on each peer: nothing here writes a ZDO.
    internal static class MountScaling {

        // A seat less than this many metres above the prefab's counts as vanilla size, and keeps vanilla's dismount.
        private const float RaisedSeatThreshold = 0.25f;
        // The slope a ridden creature may climb rises with its size toward this, from vanilla's 45 degrees. UpdateGroundTilt
        // caps its tilt at 50 degrees, so a limit from there up would be no limit at all.
        private const float MaxRiddenSlideAngle = 49f;
        // How far below the mount's feet the ground beside it may lie before that side counts as a ledge.
        private const float MaxDismountStepDown = 2f;
        // Space between the mount's body and a dismounted rider, on top of the rider's radius.
        private const float DismountClearance = 0.3f;
        // Aiming at a big mount's body above this share of the seat's height offers the saddle. Below it stays the tame's own
        // prompt, so it can still be petted and named.
        private const float SaddleAimHeight = 0.5f;
        // A multiplier this close to 1 counts as the prefab's size.
        private const float SizeTolerance = 0.001f;

        // The narrower bodies tame Lox and Asksvin have here, as radius and height. Any other mount keeps its prefab's.
        private static readonly Dictionary<string, Vector2> TameBodies = new Dictionary<string, Vector2>() {
            { "Lox", new Vector2(0.5f, 3f) },
            { "Asksvin", new Vector2(0.842f, 3f) },
        };

        // The mount's saddle values, and its seat and body in the root's local (unscaled) units.
        private sealed class MountBase {
            internal float UseRange;
            internal float HoverOffset;
            internal float SeatHeight;
            // From the ground beside the body to the seat, or root to seat if that is longer: how far a rider has to
            // reach, and how far CalculateHaveValidUser measures, both growing with the mount.
            internal float SeatReach;
            internal float BodyHalfWidth;
            internal float BodyHalfLength;
            // The root capsule as the mount came, for fitting it and for putting it back. None on a mount without one.
            internal bool HasBody;
            internal float BodyRadius;
            internal float BodyHeight;
            internal int BodyDirection;
            internal Vector3 BodyCenter;
        }

        // Keyed by prefab name. A null entry is a prefab with no usable saddle of its own (or one that could not be found),
        // whose creatures are still checked for one a mod gave them.
        private static readonly Dictionary<string, MountBase> MountBases = new Dictionary<string, MountBase>();

        // Size multiplier of each loaded mount with the fixes on whose scale is not 1. Read by the slide angle postfix on
        // every physics step of a ridden creature and the hover postfix on every frame the player aims at a creature, so
        // those lookups must not allocate: keyed by full ZDOID (a bare ID collides across peers), dropped with the other
        // per-ZDOID caches in CleanupDeletedCreatures.
        private static readonly Dictionary<ZDOID, float> MountSizeMultipliers = new Dictionary<ZDOID, float>();

        // Mounts these fixes have changed, the only ones turning them off has anything to put back on.
        private static readonly HashSet<ZDOID> FittedMounts = new HashSet<ZDOID>();

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
            FittedMounts.Remove(id);
        }

        // The global setting and the creature's own, RideableSizeFixes in its LevelSettings entry.
        private static bool FixesOn(string prefabName) {
            if (ValConfig.EnableRidableCreatureSizeFixes.Value == false) { return false; }
            Dictionary<string, CreatureSpecificSetting> creatures = LevelSystemData.SLE_Level_Settings?.CreatureConfiguration;
            if (creatures == null || creatures.TryGetValue(prefabName, out CreatureSpecificSetting settings) == false || settings == null) { return true; }
            return settings.RideableSizeFixes;
        }

        // Fits a mount to the size it is now: the saddle's reach, from its own values plus however much farther away the seat
        // now is, and a tame's body. Runs on every setup pass of every creature, and when one is tamed or saddled, so anything
        // without a saddle leaves on the cheap checks in GetMountBase. Never below vanilla: a shrunk mount keeps its own reach.
        internal static void FitToSize(GameObject creature) {
            MountBase mount = GetMountBase(creature);
            if (mount == null) { return; }
            Sadle saddle = FindSaddle(creature);
            if (saddle == null) { return; }
            ZNetView nview = creature.GetComponent<ZNetView>();
            ZDO zdo = nview != null ? nview.GetZDO() : null;

            string prefabName = Utils.GetPrefabName(creature);
            if (FixesOn(prefabName) == false) {
                PutBack(creature, mount, saddle, zdo);
                return;
            }

            Vector3 reference = SizeModifications.GetSizeReferenceForObject(prefabName);
            Vector3 scale = creature.transform.localScale;
            float extraReach = mount.SeatReach * Mathf.Max(0f, scale.y - reference.y);
            saddle.m_maxUseRange = mount.UseRange + extraReach;
            saddle.m_hoverOffset = mount.HoverOffset + extraReach;

            float multiplier = reference.x > 0f ? scale.x / reference.x : 1f;
            if (zdo != null) {
                FittedMounts.Add(zdo.m_uid);
                if (Mathf.Abs(multiplier - 1f) > SizeTolerance || Mathf.Abs(scale.y - 1f) > SizeTolerance) {
                    MountSizeMultipliers[zdo.m_uid] = multiplier;
                } else {
                    MountSizeMultipliers.Remove(zdo.m_uid);
                }
            }

            Tameable tame = creature.GetComponent<Tameable>();
            if (tame != null && tame.IsTamed()) { FitTameBody(creature, mount, multiplier); }

            // A mount resized under its rider gets its camera distance measured again.
            if (saddle == cameraSaddle) { cameraSaddle = null; }
        }

        // The fixes have just been turned off for a mount that had them.
        private static void PutBack(GameObject creature, MountBase mount, Sadle saddle, ZDO zdo) {
            if (zdo == null || FittedMounts.Remove(zdo.m_uid) == false) { return; }
            MountSizeMultipliers.Remove(zdo.m_uid);
            saddle.m_maxUseRange = mount.UseRange;
            saddle.m_hoverOffset = mount.HoverOffset;
            CapsuleCollider body = creature.GetComponent<CapsuleCollider>();
            if (body != null && mount.HasBody) {
                body.radius = mount.BodyRadius;
                body.height = mount.BodyHeight;
                body.center = mount.BodyCenter;
            }
            if (saddle == cameraSaddle) { cameraSaddle = null; }
        }

        // Keeps the body's lowest point as high above the root as it is at prefab size, whatever the scale, which keeps the
        // feet on the ground and the contact counting as ground. A body whose lowest point is already past its radius is not
        // what the creature stands on (the Moose stands on a lower collider), so it stays where it is.
        private static void FitTameBody(GameObject creature, MountBase mount, float multiplier) {
            CapsuleCollider body = creature.GetComponent<CapsuleCollider>();
            if (body == null || mount.HasBody == false) { return; }
            float radius = mount.BodyRadius;
            float height = mount.BodyHeight;
            if (TameBodies.TryGetValue(Utils.GetPrefabName(creature), out Vector2 tameBody)) {
                radius = tameBody.x;
                height = tameBody.y;
            }
            Vector3 center = mount.BodyCenter;
            float lowest = center.y - (mount.BodyDirection == 1 ? Mathf.Max(radius, height * 0.5f) : radius);
            if (multiplier > 1f && lowest < radius) { center.y -= lowest * (1f - 1f / multiplier); }
            body.radius = radius;
            body.height = height;
            body.center = center;
        }

        // How many metres the seat sits above where the prefab's seat would be. Negative for a shrunk mount.
        internal static float SeatRaise(Sadle saddle) {
            Character mountCharacter = saddle.m_character;
            if (mountCharacter == null) { return 0f; }
            MountBase mount = GetMountBase(mountCharacter.gameObject);
            if (mount == null) { return 0f; }
            string prefabName = Utils.GetPrefabName(mountCharacter.gameObject);
            float growth = mountCharacter.transform.localScale.y - SizeModifications.GetSizeReferenceForObject(prefabName).y;
            return mount.SeatHeight * growth;
        }

        // The angle to give GetSlideAngle for a scaled mount, which ApplySlide compares with the angle it reads off the tilt
        // normal. The slope actually allowed rises a little with the mount's size, then goes through the same shortening the
        // normal does, so the comparison comes out as it would at scale 1: a mount without tilt has a true normal and needs
        // none of that. Called from GetSlideAngle on every physics step of a ridden creature, so it must not allocate.
        internal static float RiddenSlideAngle(Character mount, float vanillaAngle) {
            ZNetView nview = mount.m_nview;
            if (nview == null) { return vanillaAngle; }
            ZDO zdo = nview.m_zdo;
            if (zdo == null || !MountSizeMultipliers.TryGetValue(zdo.m_uid, out float multiplier)) { return vanillaAngle; }
            float allowed = Mathf.Lerp(vanillaAngle, MaxRiddenSlideAngle, 1f - 1f / multiplier);
            if (mount.m_groundTilt == Character.GroundTiltType.None) { return allowed; }
            return Mathf.Acos(Mathf.Clamp01(Mathf.Cos(allowed * Mathf.Deg2Rad) / mount.transform.localScale.y)) * Mathf.Rad2Deg;
        }

        // After Player.FindHoverObject: when the look ray stops on the upper body of a grown, saddled mount, within reach of
        // the saddle, offers the saddle. Nothing changes for a saddle the ray hit itself, whose reach vanilla has already
        // judged, or for any other creature, which leaves on the lookup.
        internal static void AimAtSaddle(Player player, ref GameObject hover, Character creature) {
            ZNetView nview = creature.m_nview;
            ZDO zdo = nview != null ? nview.m_zdo : null;
            if (zdo == null || !MountSizeMultipliers.TryGetValue(zdo.m_uid, out float multiplier) || multiplier <= 1f + SizeTolerance) { return; }
            Tameable tame = creature.GetComponent<Tameable>();
            Sadle saddle = tame != null ? tame.m_saddle : null;
            if (saddle == null || saddle.isActiveAndEnabled == false || saddle.m_attachPoint == null) { return; }
            if (FirstHoverHit(player, out RaycastHit hit) == false || hit.collider.transform.IsChildOf(saddle.transform)) { return; }

            float rootY = creature.transform.position.y;
            if (hit.point.y - rootY < (saddle.m_attachPoint.position.y - rootY) * SaddleAimHeight) { return; }
            if (Vector3.Distance(player.m_eye.position, hit.point) >= player.m_maxInteractDistance + saddle.GetHoverOffset()) { return; }
            if (saddle.InUseDistance(player) == false) { return; }
            hover = saddle.gameObject;
        }

        // The hit FindHoverObject has just decided on: the nearest one that is not the player's own.
        private static bool FirstHoverHit(Player player, out RaycastHit hit) {
            RaycastHit[] hits = player.m_raycastHoverHits;
            for (int i = 0; i < hits.Length; i++) {
                hit = hits[i];
                if (hit.collider == null) { break; }
                Rigidbody body = hit.collider.attachedRigidbody;
                if (body == null || body.gameObject != player.gameObject) { return true; }
            }
            hit = default;
            return false;
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
            if (saddle.m_character == null || FixesOn(Utils.GetPrefabName(saddle.m_character.gameObject)) == false) { return null; }
            return SeatRaise(saddle) > RaisedSeatThreshold ? saddle : null;
        }

        // After Player.AttachStop: moves the rider from vanilla's drop point on top of the mount to the ground beside it.
        // Tries the mount's right, then its left, then behind it; keeps vanilla's drop when none of them has level
        // ground (a ledge, a tight build) or the mount is swimming, where the water takes the fall anyway.
        internal static void PlaceBesideMount(Player player, Sadle saddle) {
            if (player.m_attached || ZoneSystem.instance == null) { return; }
            Character mountCharacter = saddle.m_character;
            if (mountCharacter == null || mountCharacter.IsSwimming()) { return; }
            MountBase mount = GetMountBase(mountCharacter.gameObject);
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

            // The body as well as the transform: the rigidbody's own pose could win, putting the rider back on the mount's back.
            player.transform.position = spot;
            player.m_body.position = spot;
            player.m_body.linearVelocity = Vector3.zero;
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
                    bool fixesOn = riding.m_character != null && FixesOn(Utils.GetPrefabName(riding.m_character.gameObject));
                    cameraSaddleBonus = fixesOn ? Mathf.Max(0f, SeatRaise(riding)) : 0f;
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

        // The prefab's saddle when it has one, else the creature's own, which a mod can give it as it wakes. This creature
        // comes first through here with its body and saddle untouched, so what is measured off it is the mount as it came.
        private static MountBase GetMountBase(GameObject creature) {
            string prefabName = Utils.GetPrefabName(creature);
            if (MountBases.TryGetValue(prefabName, out MountBase cached)) {
                if (cached != null) { return cached; }
            } else {
                // Quiet: every creature passes through here, and a missing prefab was already reported by the reference
                // size lookup that sized it.
                GameObject prefab = PrefabLookup.Find(prefabName);
                if (prefab != null) {
                    MountBase fromPrefab = Measure(prefab, FindSaddle(prefab));
                    MountBases[prefabName] = fromPrefab;
                    if (fromPrefab != null) {
                        LogMountFound(prefabName);
                        return fromPrefab;
                    }
                } else if (ZNetScene.instance != null) {
                    // Before a world loads ZNetScene does not exist yet, so a miss then is not final.
                    MountBases[prefabName] = null;
                }
            }

            Tameable tame = creature.GetComponent<Tameable>();
            if (tame == null || tame.m_saddle == null) { return null; }
            MountBase fromCreature = Measure(creature, tame.m_saddle);
            if (fromCreature == null) { return null; }
            MountBases[prefabName] = fromCreature;
            LogMountFound(prefabName);
            return fromCreature;
        }

        private static void LogMountFound(string prefabName) {
            Logger.LogInfo($"{prefabName} carries a saddle, so it gets the rideable size fixes. RideableSizeFixes: false in its LevelSettings.yaml entry turns them off for it.");
        }

        private static MountBase Measure(GameObject source, Sadle saddle) {
            if (saddle == null || saddle.m_attachPoint == null) { return null; }
            Transform root = source.transform;
            Vector3 seat = root.InverseTransformPoint(saddle.m_attachPoint.position);
            // Character.m_collider is only filled in Awake, which a prefab never runs.
            CapsuleCollider body = source.GetComponent<CapsuleCollider>();
            float halfWidth = body != null ? CapsuleReach(body, 0) : 1f;
            float halfLength = body != null ? CapsuleReach(body, 2) : 1f;
            MountBase mount = new MountBase {
                UseRange = saddle.m_maxUseRange,
                HoverOffset = saddle.m_hoverOffset,
                SeatHeight = seat.y,
                SeatReach = Mathf.Max(seat.magnitude, new Vector2(seat.y, halfWidth).magnitude),
                BodyHalfWidth = halfWidth,
                BodyHalfLength = halfLength,
            };
            if (body != null) {
                mount.HasBody = true;
                mount.BodyRadius = body.radius;
                mount.BodyHeight = body.height;
                mount.BodyDirection = body.direction;
                mount.BodyCenter = body.center;
            }
            return mount;
        }

        // How far the capsule reaches from the root along one of its local axes (0 = x, 2 = z).
        private static float CapsuleReach(CapsuleCollider capsule, int axis) {
            float reach = capsule.direction == axis ? Mathf.Max(capsule.radius, capsule.height * 0.5f) : capsule.radius;
            return reach + Mathf.Abs(capsule.center[axis]);
        }
    }
}
