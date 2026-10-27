using JetBrains.Annotations;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Shared;
using StarLevelSystem.modules.Modifiers;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    // A boss modifier. Within FirstCastDelay seconds of a fight starting, and then every MinInterval to MaxInterval
    // seconds, it calls a stone spike up under each player within Range -- on the ground below anyone in the air. The
    // spot rumbles for Delay seconds -- it stays where it was when the cast began -- then the spike erupts, dealing blunt
    // and pierce damage and throwing whoever is on it a metre or two into the air.
    internal static class EarthCaller
    {
        // Overridable via the CreatureModConfig.Config dict in Modifiers.yaml.
        internal const float DefaultFirstCastDelay = 5f;
        internal const float DefaultMinInterval = 5f;
        internal const float DefaultMaxInterval = 15f;
        internal const float DefaultDelay = 0.75f;
        internal const float DefaultRange = 40f;
        // Metres the spike's hit reaches around the spot; the rumble and the cluster of spikes are laid out to cover it.
        internal const float DefaultRadius = 3.2f;

        // The networked prefab each spike is, from the asset bundle: an Aoe that hits when its fuse runs out, with a
        // "Rumble" child shown until then and a "Spike" child that erupts (EarthSpikeVisual).
        internal const string SpikeEffect = "EarthSpike";

        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CharacterCacheEntry ccache) {
            if (creature == null || config == null) { return; }
            SLSEarthCaller caller = creature.GetComponent<SLSEarthCaller>();
            if (caller == null) { caller = creature.gameObject.AddComponent<SLSEarthCaller>(); }
            // Every setup pass, so a config reload or level-up reaches a boss already in the world.
            caller.Configure(creature, config);
        }

        // Wired as the EarthCaller TeardownEvent.
        [UsedImplicitly]
        public static void Teardown(Character creature) {
            if (creature == null) { return; }
            SLSEarthCaller caller = creature.GetComponent<SLSEarthCaller>();
            if (caller != null) { Object.Destroy(caller); }
        }

        // Puts the eruption timing on the loaded spike prefab, so every copy any peer gets carries it. Called after each
        // modifier prefab load.
        internal static void AttachVisual() {
            if (CreatureModifiersData.LoadedSecondaryEffects.TryGetValue(SpikeEffect, out GameObject prefab) == false || prefab == null) { return; }
            if (prefab.GetComponent<EarthSpikeVisual>() == null) { prefab.AddComponent<EarthSpikeVisual>(); }
        }

        // Owner-side. The prefab carries a ZNetView, so the spike shows for every player; only this machine runs its hit.
        internal static void CallSpike(Character boss, Vector3 at, float damage, float delay, float radius) {
            GameObject go = CreatureModifiers.ApplySecondaryVFX(SpikeEffect, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (go == null) { return; }
            go.SetActive(false);
            ZNetView nview = go.GetComponent<ZNetView>();
            // Read by EarthSpikeVisual on every peer, so a changed Delay or Radius looks the same everywhere.
            if (nview != null && nview.IsValid()) {
                nview.GetZDO().Set(EarthSpikeVisual.DelayKey, delay);
                nview.GetZDO().Set(EarthSpikeVisual.RadiusKey, radius);
            }
            Aoe aoe = go.GetComponent<Aoe>();
            if (aoe != null) {
                aoe.m_damage = new HitData.DamageTypes() { m_blunt = damage * 0.5f, m_pierce = damage * 0.5f };
                // Awake has already run (Instantiate wakes it before SetActive(false)), so its rolled fuse and its status
                // effect hash are both set here rather than through the prefab's fields.
                aoe.m_ttl = delay;
                aoe.m_radius = radius;
                aoe.m_hitAfterTtl = true;
                aoe.m_statusEffectHash = ModifierStatusEffects.LaunchedHash;
                aoe.m_hitOwner = false;
                aoe.m_hitSame = false;
                aoe.m_hitFriendly = false;
                aoe.m_hitEnemy = true;
                aoe.m_hitCharacters = true;
                // The boss is the attacker: vanilla applies the difficulty and enemy damage rates to its hit, and the
                // owner checks above keep the spike off the boss and its allies.
                aoe.Setup(boss, Vector3.zero, -1f, null, null, null);
            }
            go.SetActive(true);
        }
    }

    // One per Earth Caller creature, on every peer; only the owner ever casts.
    internal class SLSEarthCaller : MonoBehaviour
    {
        // Twice a second, so noticing a fight has started plus waiting for the next tick still fits in FirstCastDelay.
        private const float TickSeconds = 0.5f;
        private const float RayHeight = 1f;
        // Far enough to find the ground under a player falling off a cliff or high on a jump.
        private const float RayLength = 200f;

        private Character boss;
        private float power;
        private float firstCastDelay;
        private float minInterval;
        private float maxInterval;
        private float delay;
        private float range;
        private float radius;
        private bool wasFighting;
        private float nextCast;

        internal void Configure(Character creature, CreatureModConfig config) {
            boss = creature;
            power = Mathf.Max(0f, config.PowerAt(creature.m_level));
            firstCastDelay = Mathf.Max(TickSeconds * 2f, config.Get("FirstCastDelay", EarthCaller.DefaultFirstCastDelay));
            minInterval = Mathf.Max(1f, config.Get("MinInterval", EarthCaller.DefaultMinInterval));
            maxInterval = Mathf.Max(minInterval, config.Get("MaxInterval", EarthCaller.DefaultMaxInterval));
            delay = Mathf.Max(0.1f, config.Get("Delay", EarthCaller.DefaultDelay));
            range = Mathf.Max(1f, config.Get("Range", EarthCaller.DefaultRange));
            radius = Mathf.Max(0.5f, config.Get("Radius", EarthCaller.DefaultRadius));
            if (IsInvoking(nameof(Tick)) == false) { InvokeRepeating(nameof(Tick), TickSeconds, TickSeconds); }
        }

        private void Tick() {
            if (boss == null || boss.IsDead() || boss.m_nview == null || boss.m_nview.IsValid() == false || boss.m_nview.IsOwner() == false) {
                wasFighting = false;
                return;
            }
            bool fighting = Fighting();
            // The first spike comes early in the fight: by FirstCastDelay seconds after it starts, allowing a tick to notice
            // the start and a tick to act on the cast. The intervals take over after that.
            if (fighting && wasFighting == false) {
                nextCast = Time.time + Random.Range(TickSeconds, firstCastDelay - TickSeconds);
            }
            wasFighting = fighting;
            if (fighting == false || Time.time < nextCast) { return; }
            nextCast = Time.time + Random.Range(minInterval, maxInterval);
            Cast();
        }

        // An idle boss on its altar, or one that has lost every target, calls nothing up.
        private bool Fighting() {
            MonsterAI ai = boss.GetBaseAI() as MonsterAI;
            return ai != null && (ai.GetTargetCreature() != null || ai.IsAlerted());
        }

        private void Cast() {
            float estimate = SLSExtensions.EstimateCharacterDamage(boss, DamageEstimateType.Average);
            if (estimate <= 0f) { estimate = 10f * boss.m_level; }
            float damage = Mathf.Clamp(power * estimate, 0f, 5000f);
            if (damage <= 0f) { return; }

            foreach (Player player in Player.GetAllPlayers()) {
                if (player == null || player.IsDead()) { continue; }
                if (Vector3.Distance(player.transform.position, boss.transform.position) > range) { continue; }
                if (BaseAI.IsEnemy(boss, player) == false) { continue; }
                EarthCaller.CallSpike(boss, Ground(player.transform.position), damage, delay, radius);
            }
        }

        // The ground under a player, through the solid ray mask so floors, pieces and dungeons count. A player in the air --
        // jumping, falling, gliding off a ledge -- gets the spike on the ground below them. Nothing under the ray at all
        // falls back to the terrain height, so no player is ever left out of a cast.
        private static Vector3 Ground(Vector3 position) {
            int mask = ZoneSystem.instance != null ? ZoneSystem.instance.m_solidRayMask : Physics.DefaultRaycastLayers;
            if (Physics.Raycast(position + Vector3.up * RayHeight, Vector3.down, out RaycastHit hit, RayLength, mask)) { return hit.point; }
            if (ZoneSystem.instance != null) { return new Vector3(position.x, ZoneSystem.instance.GetGroundHeight(position), position.z); }
            return position;
        }

        private void OnDestroy() {
            CancelInvoke(nameof(Tick));
        }
    }

    // On every copy of the spike prefab, on every peer: hides the "Spike" child until the fuse runs out, then stops the
    // "Rumble" child and lets the spikes erupt -- the prefab's spike in the middle and smaller copies of it around it,
    // spread over the hit's reach. The spikes are unparented first, because the owner destroys the spike object for
    // everyone the moment it hits.
    internal class EarthSpikeVisual : MonoBehaviour
    {
        internal const string DelayKey = "SLS_SpikeDelay";
        internal const string RadiusKey = "SLS_SpikeRadius";
        private const float SpikeLifetime = 3f;
        // Erupts this much ahead of the hit. The owner's Aoe destroys the object from FixedUpdate on the frame its fuse
        // ends, which can come before this Update; a child cannot be saved from a parent that is already going.
        private const float Lead = 0.1f;
        // The reach the prefab's rumble was built for; it is scaled from this to the reach in use.
        private const float PrefabRadius = 3.2f;
        // The ring around the centre spike: how many there are and how tall they stand, in metres.
        private const int SideSpikes = 5;
        private const float CentreHeight = 2.4f;
        private const float SideHeightMin = 1.1f;
        private const float SideHeightMax = 1.7f;

        private Transform spike;
        private Transform rumble;
        private float radius;
        private float eruptAt;
        private bool erupted;

        private void Start() {
            spike = transform.Find("Spike");
            rumble = transform.Find("Rumble");
            if (spike != null) { spike.gameObject.SetActive(false); }
            Aoe aoe = GetComponent<Aoe>();
            float fuse = aoe != null ? aoe.m_ttl : EarthCaller.DefaultDelay;
            radius = aoe != null ? aoe.m_radius : EarthCaller.DefaultRadius;
            ZNetView nview = GetComponent<ZNetView>();
            if (nview != null && nview.IsValid()) {
                fuse = nview.GetZDO().GetFloat(DelayKey, fuse);
                radius = nview.GetZDO().GetFloat(RadiusKey, radius);
            }
            // The rumble's emitters scale their shape, not their particles, with this.
            if (rumble != null) { rumble.localScale = Vector3.one * (radius / PrefabRadius); }
            eruptAt = Time.time + Mathf.Max(0f, fuse - Lead);
        }

        private void Update() {
            if (erupted == false && Time.time >= eruptAt) { Erupt(); }
        }

        private void Erupt() {
            erupted = true;
            if (rumble != null) { rumble.gameObject.SetActive(false); }
            if (spike == null) { return; }
            Vector3 centre = spike.position;
            spike.SetParent(null, true);
            spike.gameObject.SetActive(true);
            // Copied before the centre spike is raised, so every copy starts from the prefab's own pose and scale.
            for (int i = 0; i < SideSpikes; i++) {
                float angle = (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / SideSpikes;
                float distance = radius * Random.Range(0.45f, 0.8f);
                Vector3 spot = Ground(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance);
                GameObject side = Instantiate(spike.gameObject, spot, spike.rotation);
                Raise(side, spot, Random.Range(SideHeightMin, SideHeightMax), Random.Range(0.02f, 0.12f));
            }
            Raise(spike.gameObject, centre, CentreHeight, 0f);
        }

        private static void Raise(GameObject go, Vector3 ground, float height, float delay) {
            go.AddComponent<EarthSpikeRise>().Begin(ground, height, delay);
            // A backstop: EarthSpikeRise removes the spike itself once it has sunk.
            Destroy(go, SpikeLifetime + delay);
        }

        // A side spike stands on the ground at its own spot, not at the centre's height.
        private static Vector3 Ground(Vector3 spot) {
            int mask = ZoneSystem.instance != null ? ZoneSystem.instance.m_solidRayMask : Physics.DefaultRaycastLayers;
            return Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, mask) ? hit.point : spot;
        }
    }

    // Shoots one erupted spike up out of the ground, holds it, then sinks it again. On the spike itself, which
    // EarthSpikeVisual has unparented: the spike prefab's root is gone long before this finishes.
    internal class EarthSpikeRise : MonoBehaviour
    {
        private const float RiseSeconds = 0.12f;
        private const float HoldSeconds = 1.3f;
        private const float SinkSeconds = 0.6f;
        private const float Tilt = 8f;

        private Vector3 top;
        private Vector3 buried;
        private float start;

        // Called straight after AddComponent, before the spike is drawn. The mesh is vanilla's (a Jotunn mock of the
        // cave stalagmite), so its size is only known in game: it is scaled to height metres and its base stood on the
        // ground, wherever the mesh's own pivot is.
        internal void Begin(Vector3 ground, float height, float delay) {
            transform.position = ground;
            transform.rotation = Quaternion.Euler(Random.Range(-Tilt, Tilt), Random.Range(0f, 360f), Random.Range(-Tilt, Tilt));
            Renderer shape = GetComponentInChildren<Renderer>();
            float risen = height;
            if (shape != null && shape.bounds.size.y > 0.01f) {
                transform.localScale *= height / shape.bounds.size.y;
                Bounds bounds = shape.bounds;
                risen = bounds.size.y;
                transform.position += Vector3.up * (ground.y - bounds.min.y);
            }
            top = transform.position;
            buried = top - Vector3.up * risen;
            transform.position = buried;
            start = Time.time + delay;
        }

        private void Update() {
            float t = Time.time - start;
            if (t < 0f) { return; }
            if (t < RiseSeconds) {
                float k = t / RiseSeconds;
                transform.position = Vector3.Lerp(buried, top, 1f - (1f - k) * (1f - k));
            } else if (t < RiseSeconds + HoldSeconds) {
                transform.position = top;
            } else {
                float k = Mathf.Clamp01((t - RiseSeconds - HoldSeconds) / SinkSeconds);
                transform.position = Vector3.Lerp(top, buried, k * k);
                if (k >= 1f) { Destroy(gameObject); }
            }
        }
    }
}
