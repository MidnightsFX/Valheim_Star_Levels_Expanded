using HarmonyLib;
using JetBrains.Annotations;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.modules.Modifiers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.Modifiers
{
    // Carries a shield over its health: power x its maximum health worth of damage is soaked up before any reaches it.
    // Once it has gone RegenDelay seconds without being hit, the shield refills from empty to full over RegenDuration
    // seconds, and any hit pauses the refill. While any shield is left a see-through shell covers the creature's body,
    // fainter the less shield it has.
    internal static class Shielded
    {
        // Overridable via RegenDuration and RegenDelay in the CreatureModConfig.Config dict in Modifiers.yaml.
        private const float DefaultRegenDuration = 15f;
        private const float DefaultRegenDelay = 4f;

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
    // ownership change and every peer can show the shell from it; only the owner absorbs and refills. -1 means not set
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

        // The shell's strength with almost no shield left; a full shield draws it at 1.
        private const float MinStrength = 0.3f;
        // How far the shell stands off the body, in metres: a share of the creature's height, kept to a few centimetres.
        private const float OffsetPerHeight = 0.012f;
        private const float MinOffset = 0.015f;
        private const float MaxOffset = 0.08f;

        internal const string ShellName = "SLS_ShieldShell";
        private static readonly int ExtrudeId = Shader.PropertyToID("_Extrude");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static Material shellMaterial;
        private static bool shellMaterialLoaded;
        private static MaterialPropertyBlock shellProperties;

        private Character creature;
        private float power;
        private float regenDuration;
        private float regenDelay;
        // Owner-local, so a new owner starts refilling straight away. The shield's value itself carries over.
        private float lastHit = -1000f;
        private float nextTick;
        private float nextHitEffect;

        // Each body renderer and the shell drawn over it, index for index.
        private readonly List<Renderer> sources = new List<Renderer>();
        private readonly List<Renderer> shells = new List<Renderer>();
        private bool built;
        private float offset;
        private float strength;
        private float targetStrength = 1f;
        private bool shellsDirty;

        internal static bool IsShell(Component component) => component != null && component.gameObject.name == ShellName;

        internal void Configure(Character owner, float shieldPower, float duration, float delay) {
            creature = owner;
            power = Mathf.Max(0f, shieldPower);
            regenDuration = Mathf.Max(0.1f, duration);
            regenDelay = Mathf.Max(0f, delay);
            // GetHeight is the collider's own height, so the creature's scale (size modifiers, per-level size) is added here.
            offset = Mathf.Clamp(creature.GetHeight() * creature.transform.lossyScale.y * OffsetPerHeight, MinOffset, MaxOffset);
            shellsDirty = true;
        }

        // Read live: max health is set later in the setup pass than Setup runs, and moves with level-ups.
        private float Maximum => creature == null ? 0f : power * creature.GetMaxHealth();

        private ZDO Zdo => creature != null && creature.m_nview != null && creature.m_nview.IsValid() ? creature.m_nview.GetZDO() : null;

        private void Update() {
            if (creature == null) { return; }
            if (Time.time >= nextTick) { Tick(); }
            if (built == false) { return; }
            if (strength != targetStrength) {
                strength = Mathf.MoveTowards(strength, targetStrength, Time.deltaTime / FadeSeconds);
                shellsDirty = true;
            }
            if (shellsDirty) { UpdateShells(); }
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
            SetTargetStrength(value);
            if (built == false) { built = BuildShells(); }
            // Picks up a body renderer another script switched on or off since the last tick.
            shellsDirty = true;
        }

        // Not set up yet (-1) counts as full: the owner is about to fill it.
        private void SetTargetStrength(float value) {
            if (value >= 0f && value <= 0.01f) {
                targetStrength = 0f;
                return;
            }
            float max = Maximum;
            float share = value < 0f || max <= 0f ? 1f : Mathf.Clamp01(value / max);
            targetStrength = Mathf.Lerp(MinStrength, 1f, share);
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
            SetTargetStrength(value);

            if (DamageText.instance != null) { DamageText.instance.ShowText(DamageText.TextType.Blocked, hit.m_point, absorbed); }
            PlayEffect(value <= 0.01f);
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

        private static Material ShellMaterial() {
            if (shellMaterialLoaded) { return shellMaterial; }
            shellMaterialLoaded = true;
            if (GUIManager.IsHeadless() || StarLevelSystem.EmbeddedResourceBundle == null) { return null; }
            Material material = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<Material>(ShellName);
            if (material == null) {
                Logger.LogWarning($"The asset bundle has no {ShellName} material; Shielded creatures will show no shield.");
                return null;
            }
            // The shader is compiled for Direct3D, Vulkan and OpenGL only, which leaves out Metal on macOS.
            if (material.shader == null || material.shader.isSupported == false) {
                Logger.LogWarning($"The Shielded shell shader cannot run on this graphics API ({SystemInfo.graphicsDeviceType}); Shielded creatures will show no shield, though it still soaks damage.");
                return null;
            }
            shellMaterial = material;
            return shellMaterial;
        }

        // A copy of each body renderer that reuses its mesh (and bones, for a skinned one) with the shell material, as a
        // child of that renderer so it follows it, shares whether it is active, and goes when it goes, as swapped
        // equipment does. Returns false while the creature has no renderers yet, so the next tick tries again.
        private bool BuildShells() {
            Material material = ShellMaterial();
            if (material == null) { return true; }
            Transform root = creature.m_visual != null ? creature.m_visual.transform : creature.transform;
            // Only matters without a visual root, when the search starts at the creature and would find the effects too.
            Transform effects = creature.transform.Find(Visible_Toggle.SLSVISUALS);
            foreach (Renderer source in root.GetComponentsInChildren<Renderer>(true)) {
                if (IsShell(source) || (effects != null && source.transform.IsChildOf(effects))) { continue; }
                Renderer shell = null;
                if (source is SkinnedMeshRenderer skinned) {
                    shell = SkinnedShell(skinned, material);
                } else if (source is MeshRenderer) {
                    shell = StaticShell(source, material);
                }
                if (shell == null) { continue; }
                sources.Add(source);
                shells.Add(shell);
            }
            if (shells.Count == 0) { return false; }
            AddShellsToLodGroups();
            shellsDirty = true;
            return true;
        }

        // Shares the body's bones, as vanilla's VisEquipment does to put armour on a player.
        private static Renderer SkinnedShell(SkinnedMeshRenderer source, Material material) {
            if (source.sharedMesh == null) { return null; }
            SkinnedMeshRenderer shell = NewShellObject(source).AddComponent<SkinnedMeshRenderer>();
            shell.sharedMesh = source.sharedMesh;
            shell.bones = source.bones;
            shell.rootBone = source.rootBone;
            shell.localBounds = source.localBounds;
            shell.quality = source.quality;
            shell.updateWhenOffscreen = source.updateWhenOffscreen;
            return Dress(shell, material, source.sharedMesh.subMeshCount);
        }

        private static Renderer StaticShell(Renderer source, Material material) {
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) { return null; }
            GameObject shell = NewShellObject(source);
            shell.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            return Dress(shell.AddComponent<MeshRenderer>(), material, filter.sharedMesh.subMeshCount);
        }

        private static GameObject NewShellObject(Renderer source) {
            GameObject shell = new GameObject(ShellName) { layer = source.gameObject.layer };
            shell.transform.SetParent(source.transform, false);
            return shell;
        }

        // One material slot per submesh: with fewer, Unity would leave the rest of a multi-material body uncovered.
        private static Renderer Dress(Renderer shell, Material material, int submeshes) {
            Material[] materials = new Material[Mathf.Max(1, submeshes)];
            for (int i = 0; i < materials.Length; i++) { materials[i] = material; }
            shell.sharedMaterials = materials;
            shell.shadowCastingMode = ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.lightProbeUsage = LightProbeUsage.Off;
            shell.reflectionProbeUsage = ReflectionProbeUsage.Off;
            shell.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            shell.enabled = false;
            return shell;
        }

        // Each shell joins the LOD level that holds its body renderer, so it switches detail with the body and hides with
        // it: vanilla's Character.SetVisible hides a creature by moving its LOD group's reference point far away. Searched
        // from the creature itself, since some keep the group above their visual root (Yagluth).
        private void AddShellsToLodGroups() {
            foreach (LODGroup group in creature.GetComponentsInChildren<LODGroup>(true)) {
                LOD[] lods = group.GetLODs();
                bool changed = false;
                for (int i = 0; i < lods.Length; i++) {
                    List<Renderer> renderers = null;
                    foreach (Renderer renderer in lods[i].renderers) {
                        int index = renderer == null ? -1 : sources.IndexOf(renderer);
                        if (index < 0) { continue; }
                        if (renderers == null) { renderers = new List<Renderer>(lods[i].renderers); }
                        renderers.Add(shells[index]);
                    }
                    if (renderers == null) { continue; }
                    lods[i].renderers = renderers.ToArray();
                    changed = true;
                }
                if (changed) { group.SetLODs(lods); }
            }
        }

        private void RemoveShellsFromLodGroups() {
            foreach (LODGroup group in creature.GetComponentsInChildren<LODGroup>(true)) {
                LOD[] lods = group.GetLODs();
                bool changed = false;
                for (int i = 0; i < lods.Length; i++) {
                    List<Renderer> renderers = new List<Renderer>(lods[i].renderers);
                    if (renderers.RemoveAll(renderer => shells.Contains(renderer)) == 0) { continue; }
                    lods[i].renderers = renderers.ToArray();
                    changed = true;
                }
                if (changed) { group.SetLODs(lods); }
            }
        }

        // Off at zero strength, which also spares the extra skinning.
        private void UpdateShells() {
            shellsDirty = false;
            if (shells.Count == 0) { return; }
            if (shellProperties == null) { shellProperties = new MaterialPropertyBlock(); }
            shellProperties.SetFloat(ExtrudeId, offset);
            shellProperties.SetFloat(StrengthId, strength);
            for (int i = 0; i < shells.Count; i++) {
                Renderer shell = shells[i];
                if (shell == null) { continue; }
                bool show = strength > 0f && sources[i] != null && sources[i].enabled;
                if (shell.enabled != show) { shell.enabled = show; }
                if (show) { shell.SetPropertyBlock(shellProperties); }
            }
        }

        internal void Remove() {
            ZDO zdo = Zdo;
            if (zdo != null && creature.m_nview.IsOwner()) { zdo.Set(SLS_SHIELD, -1f); }
            // Out of the LOD groups before they are destroyed, while they can still be told apart.
            if (shells.Count > 0 && creature != null) { RemoveShellsFromLodGroups(); }
            foreach (Renderer shell in shells) {
                if (shell != null) { Destroy(shell.gameObject); }
            }
            shells.Clear();
            sources.Clear();
            Destroy(this);
        }
    }
}
