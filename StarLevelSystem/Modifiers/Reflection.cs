using HarmonyLib;
using StarLevelSystem.Data;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.Modifiers {

    internal static class Reflection {

        private class ReflectState {
            public int Count;
            // Creatures that already failed their roll against this projectile. Only piercing projectiles
            // (m_onlyStopOnTerrain) need it: they keep flying through what they hit, and would otherwise
            // re-roll the same creature every physics step until one succeeded.
            public HashSet<Character> Rolled;
        }

        // Keyed on the projectile so the state goes away with it. Only the projectile's ZDO owner simulates
        // it (Projectile.FixedUpdate), so this never needs to be shared between peers.
        private static readonly ConditionalWeakTable<Projectile, ReflectState> _state = new ConditionalWeakTable<Projectile, ReflectState>();

        // Two reflectors facing each other would otherwise pass a certain reflection back and forth until it expired.
        private const int MaxReflections = 3;
        // Fraction of the projectile's damage the reflected shot keeps. Overridable via ReflectedDamage in the
        // CreatureModConfig.Config dict in Modifiers.yaml; this is the fallback.
        private const float DefaultReflectedDamage = 0.75f;

        private const string ReflectionName = nameof(ModifierNames.Reflection);

        // Both carry a ZNetView, so instantiating them on the projectile owner replicates them to everyone.
        private static GameObject _reflectVfx;
        private static GameObject _reflectSfx;

        private static float ReadConfig(CreatureModConfig cfg, string key, float fallback) {
            if (cfg != null && cfg.Config != null && cfg.Config.TryGetValue(key, out float v)) { return v; }
            return fallback;
        }

        // Projectiles whose owner is wired into them by more than m_owner: the harpoon's line is drawn to its
        // owner, and the fishing float / grappling point a cast spawns look up the owner's rod and player ID.
        private static bool CanReflect(Projectile projectile) {
            if (projectile.GetComponent<LineConnect>() != null) { return false; }
            GameObject spawned = projectile.m_spawnOnHit;
            if (spawned != null && (spawned.GetComponent<FishingFloat>() != null || spawned.GetComponent<GrapplingPoint>() != null)) { return false; }
            return true;
        }

        // Runs on the projectile's owner (the shooter's client), which is the only peer that moves it. The new
        // velocity reaches everyone else through ZSyncTransform, which syncs Projectile.GetVelocity().
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        public static class ReflectProjectile {
            private static bool Prefix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water) {
                Projectile projectile = __instance;
                if (water || collider == null || projectile.m_didHit) { return true; }
                if (projectile.m_nview == null || projectile.m_nview.IsValid() == false || projectile.m_nview.IsOwner() == false) { return true; }
                // Vanilla ignores colliders a piercing projectile has already passed through.
                if (projectile.m_onlyStopOnTerrain && projectile.m_hitList != null && projectile.m_hitList.Contains(collider)) { return true; }
                _state.TryGetValue(projectile, out ReflectState state);
                if (state != null && state.Count >= MaxReflections) { return true; }

                GameObject hitObject = Projectile.FindHitObject(collider);
                Character target = hitObject ? hitObject.GetComponent<Character>() : null;
                if (target == null || target == projectile.m_owner || target.IsPlayer() || target.IsDead()) { return true; }
                if (target.m_nview == null || target.m_nview.IsValid() == false || target.m_collider == null) { return true; }
                if (state != null && state.Rolled != null && state.Rolled.Contains(target)) { return true; }

                Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(target);
                if (mods == null || mods.TryGetValue(ReflectionName, out ModifierType modType) == false) { return true; }
                if (CanReflect(projectile) == false) { return true; }
                // Vanilla's own filter (friendly fire, owner). Players were excluded above, so this skips the
                // dodge side effect it has for them; when we bail here vanilla runs it again and returns.
                if (projectile.IsValidTarget(target) == false) { return true; }

                CreatureModConfig cfg = CreatureModifiersData.GetConfig(ReflectionName, modType);
                float chance = Mathf.Clamp01(cfg.BasePower + (cfg.PerlevelPower * target.m_level));
                if (UnityEngine.Random.value >= chance) {
                    if (projectile.m_onlyStopOnTerrain) {
                        if (state == null) { state = _state.GetOrCreateValue(projectile); }
                        if (state.Rolled == null) { state.Rolled = new HashSet<Character>(); }
                        state.Rolled.Add(target);
                    }
                    return true;
                }

                Vector3 velocity = projectile.m_vel;
                if (velocity.sqrMagnitude < 0.0001f) { return true; }
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
                Vector3 normal;
                Vector3 reflected;
                if (flat.sqrMagnitude < 0.0001f) {
                    // Straight down onto it: there is no sideways motion to mirror, so it goes back up.
                    normal = Vector3.up;
                    reflected = -velocity;
                } else {
                    // The creature is treated as an upright cylinder: the surface normal is the flat direction
                    // from its axis to the hit point. A centered hit faces the shot head-on, an edge hit tilts it.
                    normal = hitPoint - target.GetCenterPoint();
                    normal.y = 0f;
                    // Degenerate, or the hit registered past the axis: treat it as head-on.
                    if (normal.sqrMagnitude < 0.0001f || Vector3.Dot(normal, flat) >= 0f) { normal = -flat; }
                    normal.Normalize();
                    Vector3 flatOut = Vector3.Reflect(flat, normal);
                    // Mirror the sideways motion and run the vertical motion backwards. Vanilla projectiles have no
                    // drag, so this is the incoming arc played in reverse: a head-on shot lands back where it was
                    // fired from, and a glancing one follows the same arc mirrored off to the side. Speed is kept.
                    reflected = new Vector3(flatOut.x, -velocity.y, flatOut.z);
                }

                // Give it at least as long to fly back as it took to arrive. With no drag the horizontal speed is
                // constant, so the flat distance from where it started over the flat speed is the time already
                // flown. Must read m_startPoint before it is moved below. A ttl of 0 or less never expires.
                if (projectile.m_ttl > 0f) {
                    Vector3 travelled = hitPoint - projectile.m_startPoint;
                    float flatSpeed = flat.magnitude;
                    float flown = flatSpeed > 1f ? new Vector2(travelled.x, travelled.z).magnitude / flatSpeed : travelled.magnitude / velocity.magnitude;
                    projectile.m_ttl = Mathf.Max(projectile.m_ttl, (flown * 1.25f) + 2f);
                }

                // The reflection skips the rest of OnHit, which is where a hit alerts nearby creatures. Do that
                // here while the shooter still owns the projectile, so the alert is attributed to them.
                if (projectile.m_hitNoise > 0f) {
                    BaseAI.DoProjectileHitNoise(hitPoint, projectile.m_hitNoise, projectile.m_owner);
                }

                // The reflector becomes the shooter. It has to: Character.RPC_Damage drops player-on-player hits
                // when PvP is off, so a shot still owned by the archer could never hurt them. Owning it also stops
                // the next step's ray (which starts back inside the creature) from hitting the reflector again.
                float damageKept = Mathf.Max(0f, ReadConfig(cfg, "ReflectedDamage", DefaultReflectedDamage));
                projectile.m_owner = target;
                projectile.m_hitOwner = false;
                // An untamed reflector's shot can hit other monsters too; a tamed one's spares its own side.
                projectile.m_hitFriendly = target.IsTamed() == false;
                projectile.m_noDamageFriendly = false;
                projectile.m_hitType = HitData.HitType.EnemyHit;
                // Shooter-side rewards would now go to the reflector (or a tame's master), so drop them.
                projectile.m_healthReturn = 0f;
                projectile.m_eitrAdd = 0f;
                projectile.m_adrenaline = 0f;
                projectile.m_skill = Skills.SkillType.None;
                projectile.m_raiseSkillAmount = 0f;
                projectile.m_backstabBonus = 1f;
                projectile.m_damage.Modify(damageKept);
                // Projectiles spawned when this one lands are set up from the original hit data, which vanilla
                // also changes in place when splitting damage, so swap in a cleaned copy rather than editing it.
                if (projectile.m_originalHitData != null) {
                    HitData hitData = projectile.m_originalHitData.Clone();
                    hitData.m_damage.Modify(damageKept);
                    hitData.SetAttacker(target);
                    hitData.m_hitType = HitData.HitType.EnemyHit;
                    hitData.m_healthReturn = 0f;
                    hitData.m_eitrAdd = 0f;
                    hitData.m_skill = Skills.SkillType.None;
                    hitData.m_skillRaiseAmount = 0f;
                    hitData.m_backstabBonus = 1f;
                    projectile.m_originalHitData = hitData;
                }

                projectile.m_vel = reflected;
                projectile.transform.position = hitPoint;
                // FixedUpdate only re-aims projectiles that don't spin their visual, and the root's facing is the
                // hit direction (push) and the spawn-on-hit rotation, so turn it here for all of them.
                projectile.transform.rotation = Quaternion.LookRotation(reflected);
                // ShieldGenerator stops projectiles that started outside a dome, so the new origin is the hit point.
                projectile.m_startPoint = hitPoint;
                // Ends this step's ray loop (Projectile.FixedUpdate), like a vanilla bounce.
                projectile.m_didBounce = true;
                if (state == null) { state = _state.GetOrCreateValue(projectile); }
                state.Count++;

                if (ZNetScene.instance != null) {
                    if (_reflectVfx == null) { _reflectVfx = ZNetScene.instance.GetPrefab("vfx_perfectblock"); }
                    if (_reflectSfx == null) { _reflectSfx = ZNetScene.instance.GetPrefab("sfx_perfectblock"); }
                    if (_reflectVfx != null) { Object.Instantiate(_reflectVfx, hitPoint, Quaternion.LookRotation(normal)); }
                    if (_reflectSfx != null) { Object.Instantiate(_reflectSfx, hitPoint, Quaternion.identity); }
                }

                if (Logger.IsDebugEnabled) { Logger.LogDebug($"Reflection: {target.name} reflected {projectile.name} (chance {chance}, damage kept {damageKept}, reflection {state.Count})"); }
                return false;
            }
        }
    }
}
