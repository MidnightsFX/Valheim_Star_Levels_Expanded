using HarmonyLib;
using JetBrains.Annotations;
using StarLevelSystem.common;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    // Carries a shield over its health: power x its maximum health worth of damage is soaked up before any reaches it.
    // Once it has gone RegenDelay seconds without being hit, the shield refills from empty to full over RegenDuration
    // seconds, and any hit pauses the refill. While any shield is left a faint shimmer surrounds the creature.
    internal static class Shielded
    {
        // Overridable via RegenDuration and RegenDelay in the CreatureModConfig.Config dict in Modifiers.yaml.
        private const float DefaultRegenDuration = 15f;
        private const float DefaultRegenDelay = 4f;

        // The modifier's VisualEffect, which CreatureModifiers.SetupCreatureVFX puts under the creature's visuals holder.
        internal const string ShimmerEffect = "creatureShield";

        [UsedImplicitly]
        public static void Setup(Character creature, CreatureModConfig config, CharacterCacheEntry ccache) {
            if (creature == null || config == null || creature.m_nview == null || creature.m_nview.IsValid() == false) { return; }
            SLSShield shield = creature.GetComponent<SLSShield>();
            if (shield == null) { shield = creature.gameObject.AddComponent<SLSShield>(); }
            // Every setup pass, not only the first: this is what picks up a config reload or a level-up.
            shield.Configure(creature, config.PowerAt(creature.m_level),
                config.Get("RegenDuration", DefaultRegenDuration), config.Get("RegenDelay", DefaultRegenDelay));
        }

        // Wired as the Shielded TeardownEvent.
        [UsedImplicitly]
        public static void Teardown(Character creature) {
            if (creature == null) { return; }
            SLSShield shield = creature.GetComponent<SLSShield>();
            if (shield != null) { shield.Remove(); }
        }

        // Last among ApplyDamage prefixes, so the shield soaks the hit as everything else has left it. ApplyDamage runs
        // after RPC_Damage's backstab, stagger and armour steps, which an RPC_Damage prefix would run ahead of.
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        public static class ShieldAbsorbPatch
        {
            public static void Prefix(Character __instance, HitData hit) {
                if (__instance == null || hit == null || __instance.IsPlayer()) { return; }
                SLSShield shield = __instance.GetComponent<SLSShield>();
                if (shield != null) { shield.Absorb(hit); }
            }
        }
    }

    // One per Shielded creature, on every peer. The shield's value lives in the ZDO (SLS_SHIELD) so it survives an
    // ownership change and every peer can show the shimmer from it; only the owner absorbs and refills. -1 means not set
    // up yet, which the owner fills to the maximum on its first tick.
    internal class SLSShield : MonoBehaviour
    {
        private const float TickSeconds = 0.5f;
        private const float FadeSeconds = 0.4f;
        // The hit flash replicates to everyone, so a flurry of hits does not get one each.
        private const float HitEffectGap = 0.3f;
        private const string HitEffect = "fx_GoblinShieldHit";
        private const string BreakEffect = "fx_GoblinShieldBreak";

        private static GameObject hitEffectPrefab;
        private static GameObject breakEffectPrefab;

        // Share of the shimmer bubble's own alpha it is drawn with (see Dim).
        private const float ShimmerAlpha = 0.5f;
        // How much bigger than the creature's body box the bubble is: a share of each side, then metres all round.
        private const float BodyPadding = 1.2f;
        private const float BodyMargin = 0.3f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        // The vanilla material each dimmed copy was made from, so every bubble shares one copy per source.
        private static readonly System.Collections.Generic.Dictionary<Material, Material> DimmedCopies = new System.Collections.Generic.Dictionary<Material, Material>();

        private Character creature;
        private float power;
        private float regenDuration;
        private float regenDelay;
        // Owner-local, so a new owner starts refilling straight away. The shield's value itself carries over.
        private float lastHit = -1000f;
        private float nextTick;
        private float nextHitEffect;
        private Transform shimmer;
        private Vector3 shimmerScale;
        private float shown;
        private bool shielded = true;

        internal void Configure(Character owner, float shieldPower, float duration, float delay) {
            creature = owner;
            power = Mathf.Max(0f, shieldPower);
            regenDuration = Mathf.Max(0.1f, duration);
            regenDelay = Mathf.Max(0f, delay);
        }

        // Read live: max health is set later in the setup pass than Setup runs, and moves with level-ups.
        private float Maximum => creature == null ? 0f : power * creature.GetMaxHealth();

        private ZDO Zdo => creature != null && creature.m_nview != null && creature.m_nview.IsValid() ? creature.m_nview.GetZDO() : null;

        private void Update() {
            if (creature == null) { return; }
            if (Time.time >= nextTick) { Tick(); }
            Fade();
        }

        private void Tick() {
            nextTick = Time.time + TickSeconds;
            ZDO zdo = Zdo;
            if (zdo == null) { return; }
            float value = zdo.GetFloat(SLS_SHIELD, -1f);
            if (creature.m_nview.IsOwner() && creature.IsDead() == false) {
                float max = Maximum;
                float next = value;
                if (value < 0f || value > max) {
                    next = max;
                } else if (value < max && Time.time - lastHit >= regenDelay) {
                    next = Mathf.Min(max, value + max * TickSeconds / regenDuration);
                }
                if (next != value) {
                    zdo.Set(SLS_SHIELD, next);
                    value = next;
                }
            }
            // Not set up yet counts as shielded: the owner is about to fill it.
            shielded = value < 0f || value > 0.01f;
            if (shimmer == null) { FindShimmer(); }
        }

        internal void Absorb(HitData hit) {
            ZDO zdo = Zdo;
            if (zdo == null || creature.m_nview.IsOwner() == false || creature.IsDead() || Game.instance == null) { return; }
            // Any hit pauses the refill, one that finds the shield empty included.
            lastHit = Time.time;
            float value = zdo.GetFloat(SLS_SHIELD, -1f);
            if (value < 0f) { value = Maximum; }
            if (value <= 0f) { return; }

            // The two multipliers ApplyDamage is about to apply, so the shield soaks what would actually land.
            float factor = Game.instance.GetDifficultyDamageScaleEnemy(creature.transform.position) * Game.m_playerDamageRate;
            float expected = hit.GetTotalDamage() * factor;
            if (expected <= 0f) { return; }
            float absorbed = Mathf.Min(value, expected);
            // A fully soaked hit drops below ApplyDamage's 0.1 floor and does nothing at all, stagger included.
            hit.ApplyModifier((expected - absorbed) / expected);
            value -= absorbed;
            zdo.Set(SLS_SHIELD, value);
            shielded = value > 0.01f;

            if (DamageText.instance != null) { DamageText.instance.ShowText(DamageText.TextType.Blocked, hit.m_point, absorbed); }
            PlayEffect(shielded == false);
        }

        // Owner-side and networked: both effects carry a ZNetView, so spawning them here shows them to everyone.
        private void PlayEffect(bool broke) {
            if (broke == false && Time.time < nextHitEffect) { return; }
            nextHitEffect = Time.time + HitEffectGap;
            if (ZNetScene.instance == null) { return; }
            if (hitEffectPrefab == null) { hitEffectPrefab = ZNetScene.instance.GetPrefab(HitEffect); }
            if (breakEffectPrefab == null) { breakEffectPrefab = ZNetScene.instance.GetPrefab(BreakEffect); }
            GameObject prefab = broke ? breakEffectPrefab : hitEffectPrefab;
            if (prefab != null) { Instantiate(prefab, creature.GetCenterPoint(), Quaternion.identity); }
        }

        private void FindShimmer() {
            Transform found = creature.transform.Find($"SLS_Visuals(Clone)/{Shielded.ShimmerEffect}(Clone)");
            if (found == null) { return; }
            shimmer = found;
            Fit(found);
            shimmerScale = found.localScale;
            shown = found.gameObject.activeSelf ? 1f : 0f;
            Dim(found);
        }

        // The generic effect placement (CreatureModifiers.SetupCreatureVFX) scales by height and radius and sits at half
        // the collider height, which misses a body that is wide or floats, like the gjall. Sizes and centres the bubble
        // on the creature's own meshes instead, in the creature's space so it turns with it and follows its scale.
        private void Fit(Transform bubbleRoot) {
            if (TryBodyBounds(out Bounds body) == false) { return; }
            Transform bubble = bubbleRoot.Find("bubble");
            if (bubble == null || bubbleRoot.parent == null) { return; }
            // The prefab's children carry a base scale over a unit sphere; work back from it to the size wanted.
            Vector3 baseScale = bubble.localScale;
            Vector3 wanted = Vector3.Scale(body.size * BodyPadding + Vector3.one * BodyMargin, creature.transform.lossyScale);
            Vector3 parent = bubbleRoot.parent.lossyScale;
            bubbleRoot.localScale = new Vector3(
                wanted.x / Mathf.Max(0.001f, parent.x * baseScale.x),
                wanted.y / Mathf.Max(0.001f, parent.y * baseScale.y),
                wanted.z / Mathf.Max(0.001f, parent.z * baseScale.z));
            bubbleRoot.position = creature.transform.TransformPoint(body.center);
            bubbleRoot.rotation = creature.transform.rotation;
        }

        // The creature's body as a box in its own local space: the meshes under its visual root, or failing that its
        // collider. Effects, particles and SLS's own visuals are left out.
        private bool TryBodyBounds(out Bounds body) {
            body = default;
            bool any = false;
            Transform root = creature.m_visual != null ? creature.m_visual.transform : creature.transform;
            // Only matters without a visual root, when the search starts at the creature and would find the effects too.
            Transform ours = creature.transform.Find("SLS_Visuals(Clone)");
            Matrix4x4 toCreature = creature.transform.worldToLocalMatrix;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false)) {
                if (renderer.enabled == false) { continue; }
                if (ours != null && renderer.transform.IsChildOf(ours)) { continue; }
                Bounds local;
                Matrix4x4 toWorld;
                if (renderer is SkinnedMeshRenderer skinned) {
                    local = skinned.localBounds;
                    toWorld = (skinned.rootBone != null ? skinned.rootBone : skinned.transform).localToWorldMatrix;
                } else if (renderer is MeshRenderer) {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) { continue; }
                    local = filter.sharedMesh.bounds;
                    toWorld = renderer.transform.localToWorldMatrix;
                } else {
                    continue;
                }
                Matrix4x4 m = toCreature * toWorld;
                Vector3 c = local.center, e = local.extents;
                for (int i = 0; i < 8; i++) {
                    Vector3 corner = m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                    if (any) { body.Encapsulate(corner); } else { body = new Bounds(corner, Vector3.zero); any = true; }
                }
            }
            if (any && body.size.y > 0.05f) { return true; }

            CapsuleCollider collider = creature.m_collider;
            if (collider == null) { return false; }
            float r = collider.radius;
            Vector3 size = new Vector3(r * 2f, r * 2f, r * 2f);
            size[collider.direction] = Mathf.Max(collider.height, r * 2f);
            body = new Bounds(creature.transform.InverseTransformPoint(collider.transform.TransformPoint(collider.center)), size);
            return true;
        }

        // The bubble wears vanilla's Fuling shaman shield material, which is far too bright stretched over a whole
        // creature. That material is the game's own, shared with every shaman, and Jotunn swaps it in at load, so the
        // bubble is given a copy of it with less alpha instead. One copy serves every Shielded creature.
        private static void Dim(Transform shimmer) {
            foreach (MeshRenderer renderer in shimmer.GetComponentsInChildren<MeshRenderer>(true)) {
                Material dimmed = DimmedCopyOf(renderer.sharedMaterial);
                if (dimmed != null) { renderer.sharedMaterial = dimmed; }
            }
        }

        private static Material DimmedCopyOf(Material source) {
            if (source == null) { return null; }
            if (DimmedCopies.ContainsValue(source)) { return source; }
            if (DimmedCopies.TryGetValue(source, out Material copy) && copy != null) { return copy; }
            int id = source.HasProperty(ColorId) ? ColorId : (source.HasProperty(TintColorId) ? TintColorId : 0);
            if (id == 0) { return null; }
            copy = new Material(source) { name = source.name + "_SLS_Shielded" };
            Color color = copy.GetColor(id);
            color.a *= ShimmerAlpha;
            copy.SetColor(id, color);
            DimmedCopies[source] = copy;
            return copy;
        }

        // Scaled in and out rather than faded through its material, so it works whatever the effect is built from.
        private void Fade() {
            if (shimmer == null) { return; }
            float target = shielded ? 1f : 0f;
            if (shown == target) { return; }
            shown = Mathf.MoveTowards(shown, target, Time.deltaTime / FadeSeconds);
            shimmer.localScale = shimmerScale * shown;
            bool active = shown > 0f;
            if (shimmer.gameObject.activeSelf != active) { shimmer.gameObject.SetActive(active); }
        }

        internal void Remove() {
            ZDO zdo = Zdo;
            if (zdo != null && creature.m_nview.IsOwner()) { zdo.Set(SLS_SHIELD, -1f); }
            // SetupCreatureVFX never takes a modifier's effect away on its own.
            if (shimmer != null) { Destroy(shimmer.gameObject); }
            Destroy(this);
        }
    }
}
