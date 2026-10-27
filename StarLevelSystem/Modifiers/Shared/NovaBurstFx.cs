using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.Modifiers.Shared
{
    // The bursts the frost and lightning death novas play, built from vanilla effects that do not fit as they are:
    //  - frost: the fenring ice nova cut down to a single wave, the way Epic Loot's frost wave does it. The original runs
    //    each emission burst for three cycles with staggered sounds, far too long for a creature's death;
    //  - lightning: Eikthyr's stomp shrunk to the nova's reach. The stomp is drawn for a boss's 11 m shockwave, where the
    //    lightning staff hit the bundle points at is a speck next to a 4 m blast.
    // The shared vanilla prefabs cannot be edited without changing the fenring's and Eikthyr's own effects, so each is a copy.
    //
    // A copy keeps its vanilla ZNetView, so the nova's Aoe spawning it on the owner makes a networked object whose ZDO
    // carries the copy's name as the prefab hash, and every peer builds its own from the template. That only works if every
    // peer has the template before the ZDO arrives, which is why both are built once and injected into ZNetScene on every
    // world load, on clients and the server alike.
    internal static class NovaBurstFx
    {
        internal const string FrostTemplate = "SLS_FrostNovaWave";
        internal const string LightningTemplate = "SLS_LightningNovaStomp";
        private const string FrostSource = "fx_fenring_icenova";
        private const string LightningSource = "fx_eikthyr_stomp";
        // Trimmed off the surviving frost sound's trigger delay so it lands with the single wave.
        private const float SfxDelayReduction = 1.2f;
        // The stomp's shockwave is a 22 m ring; this brings it to the nova's 4 m reach.
        private const float LightningScale = 0.36f;

        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        // Disabled parent that keeps the cached templates from waking; it outlives the world.
        private static GameObject container;
        private static readonly HashSet<string> MissingLogged = new HashSet<string>();

        // A registered template on this world, or null before the first world load.
        internal static GameObject Registered(string templateName) => ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(templateName) : null;

        // PrefabManager.OnPrefabsRegistered: a ZNetScene.Awake postfix on every client and the server, each world load,
        // after Jotunn has resolved the nova prefabs' mocks.
        internal static void Register() {
            ZNetScene zns = ZNetScene.instance;
            if (zns == null) { return; }
            Inject(zns, Build(zns, FrostTemplate, FrostSource, TrimToOneWave));
            Inject(zns, Build(zns, LightningTemplate, LightningSource, root => Scale(root, LightningScale)));
            DeathNova.UseNovaBursts();
        }

        private static void Inject(ZNetScene zns, GameObject template) {
            if (template == null) { return; }
            if (zns.m_prefabs.Contains(template) == false) { zns.m_prefabs.Add(template); }
            zns.m_namedPrefabs[template.name.GetStableHashCode()] = template;
        }

        private static GameObject Build(ZNetScene zns, string templateName, string sourceName, System.Action<GameObject> adjust) {
            if (Templates.TryGetValue(templateName, out GameObject cached) && cached != null) { return cached; }
            GameObject source = zns.GetPrefab(sourceName);
            if (source == null) {
                if (MissingLogged.Add(sourceName)) { Logger.LogWarning($"Could not find '{sourceName}'; a death nova will use its default burst."); }
                return null;
            }
            if (container == null) {
                container = new GameObject("SLS_NovaBurstContainer");
                container.SetActive(false);
                Object.DontDestroyOnLoad(container);
            }
            // Cloned under the disabled container, so nothing on it wakes while activeSelf stays true: ZNetScene builds
            // remote copies straight from the registered prefab and never calls SetActive on them.
            GameObject template = Object.Instantiate(source, container.transform);
            template.name = templateName;
            adjust(template);
            Templates[templateName] = template;
            return template;
        }

        // Every particle system runs its bursts for three cycles; one cycle and no start delay makes a single wave. Of the
        // three ZSFX sources, staggered to match the waves, the first is kept and pulled in to match the one wave left.
        private static void TrimToOneWave(GameObject root) {
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true)) {
                ParticleSystem.MainModule main = ps.main;
                main.startDelay = 0f;
                ParticleSystem.EmissionModule emission = ps.emission;
                int count = emission.burstCount;
                if (count <= 0) { continue; }
                ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[count];
                emission.GetBursts(bursts);
                for (int i = 0; i < bursts.Length; i++) { bursts[i].cycleCount = 1; }
                emission.SetBursts(bursts);
            }
            bool kept = false;
            foreach (ZSFX sfx in root.GetComponentsInChildren<ZSFX>(true)) {
                if (kept) {
                    // DestroyImmediate, so the extra sources are gone before the template is ever instantiated.
                    Object.DestroyImmediate(sfx.gameObject);
                    continue;
                }
                sfx.m_minDelay = Mathf.Max(0f, sfx.m_minDelay - SfxDelayReduction);
                sfx.m_maxDelay = Mathf.Max(0f, sfx.m_maxDelay - SfxDelayReduction);
                kept = true;
            }
        }

        // Shrinks a whole effect by factor. Each particle system picks its own scaling mode, so scaling the root transform
        // would leave most of them full size: every distance a system works in is scaled directly instead -- particle size
        // and speed, emitter shape, gravity, the velocity and force modules -- along with child offsets and light ranges.
        private static void Scale(GameObject root, float factor) {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
                if (child != root.transform) { child.localPosition *= factor; }
            }
            foreach (Light light in root.GetComponentsInChildren<Light>(true)) { light.range *= factor; }
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true)) {
                ParticleSystem.MainModule main = ps.main;
                if (main.startSize3D) {
                    main.startSizeXMultiplier *= factor;
                    main.startSizeYMultiplier *= factor;
                    main.startSizeZMultiplier *= factor;
                } else {
                    main.startSizeMultiplier *= factor;
                }
                main.startSpeedMultiplier *= factor;
                main.gravityModifierMultiplier *= factor;

                ParticleSystem.ShapeModule shape = ps.shape;
                shape.radius *= factor;
                shape.scale *= factor;
                shape.position *= factor;
                shape.boxThickness *= factor;

                ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
                velocity.xMultiplier *= factor;
                velocity.yMultiplier *= factor;
                velocity.zMultiplier *= factor;
                velocity.radialMultiplier *= factor;
                velocity.orbitalOffsetXMultiplier *= factor;
                velocity.orbitalOffsetYMultiplier *= factor;
                velocity.orbitalOffsetZMultiplier *= factor;

                ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
                if (limit.separateAxes) {
                    limit.limitXMultiplier *= factor;
                    limit.limitYMultiplier *= factor;
                    limit.limitZMultiplier *= factor;
                } else {
                    limit.limitMultiplier *= factor;
                }

                ParticleSystem.ForceOverLifetimeModule force = ps.forceOverLifetime;
                force.xMultiplier *= factor;
                force.yMultiplier *= factor;
                force.zMultiplier *= factor;

                ParticleSystem.NoiseModule noise = ps.noise;
                noise.strengthMultiplier *= factor;

                // A trail whose width follows its particle's size has already shrunk with startSize.
                ParticleSystem.TrailModule trails = ps.trails;
                if (trails.sizeAffectsWidth == false) { trails.widthOverTrailMultiplier *= factor; }
            }
        }
    }
}
