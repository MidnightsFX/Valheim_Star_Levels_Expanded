using Jotunn.Managers;
using StarLevelSystem.common;
using UnityEngine;

namespace StarLevelSystem.Modifiers.Shared
{
    // The optional circle under a death nova: an outer ring at the burst's reach and an inner one that grows to meet it
    // as the fuse runs down, so a player can tell both where it will hit and when.
    //
    // DeathNova puts this on each nova prefab once it is loaded. The novas are networked, so every player's copy of one
    // carries it and draws its own circle -- nothing is sent for it, and whether it shows is each player's own
    // ShowNovaIndicator setting. The circle is a child of the nova, so it goes when the owner destroys the nova.
    internal class NovaIndicator : MonoBehaviour
    {
        private const int Points = 48;
        // Radii, as shares of the burst's reach, at which the ground height is sampled for the growing ring.
        private static readonly float[] Bands = { 0.25f, 0.5f, 0.75f, 1f };
        private const float Lift = 0.08f;
        private const float RayHeight = 2f;
        private const float RayLength = 6f;

        private static Material material;
        private static bool warnedNoShader;

        // Set on the prefab by DeathNova and copied to every instance with it. Serialized fields survive Instantiate.
        public Color Tint = Color.white;

        private LineRenderer inner;
        private float[,] heights;
        private float radius;
        private float life;
        private float born;

        private void Start() {
            if (GUIManager.IsHeadless() || ValConfig.ShowNovaIndicator == null || ValConfig.ShowNovaIndicator.Value == false) {
                enabled = false;
                return;
            }
            Aoe aoe = GetComponent<Aoe>();
            if (aoe == null || RingShader() == null) {
                enabled = false;
                return;
            }
            radius = aoe.m_radius;
            // The time left before it bursts. Only the owner counts m_ttl down (Aoe.CustomFixedUpdate), so on any other
            // peer this is the full fuse -- which is what is left, give or take the time the nova took to arrive.
            life = Mathf.Max(0.1f, aoe.m_ttl);
            born = Time.time;
            SampleGround();

            LineRenderer outer = NewRing("SLS_NovaIndicatorOuter", 0.09f, new Color(Tint.r, Tint.g, Tint.b, 0.9f));
            SetRing(outer, 1f);
            inner = NewRing("SLS_NovaIndicatorInner", 0.14f, new Color(Tint.r, Tint.g, Tint.b, 0.65f));
            SetRing(inner, 0f);
        }

        private void Update() {
            if (inner == null) { return; }
            float filled = Mathf.Clamp01((Time.time - born) / life);
            SetRing(inner, filled);
            // A nova whose destroy is late to arrive keeps a full circle for a moment, then lets it go.
            if (Time.time - born > life + 1f) {
                Destroy(inner.transform.parent.gameObject);
                inner = null;
                enabled = false;
            }
        }

        // Once, at spawn: the ground under each point at each band, relative to the nova. Against the solid ray mask
        // rather than ZoneSystem.GetGroundHeight, which reads the heightmap and would float the ring in a dungeon.
        private void SampleGround() {
            heights = new float[Bands.Length, Points];
            Vector3 centre = transform.position;
            int mask = ZoneSystem.instance != null ? ZoneSystem.instance.m_solidRayMask : Physics.DefaultRaycastLayers;
            for (int b = 0; b < Bands.Length; b++) {
                for (int i = 0; i < Points; i++) {
                    Vector3 offset = Direction(i) * (radius * Bands[b]);
                    Vector3 from = centre + offset + Vector3.up * RayHeight;
                    heights[b, i] = Physics.Raycast(from, Vector3.down, out RaycastHit hit, RayLength, mask)
                        ? hit.point.y - centre.y
                        : 0f;
                }
            }
        }

        private void SetRing(LineRenderer ring, float share) {
            for (int i = 0; i < Points; i++) {
                ring.SetPosition(i, Direction(i) * (radius * share) + Vector3.up * (HeightAt(share, i) + Lift));
            }
        }

        // The sampled height between the two bands either side of share; the centre counts as the nova's own height.
        private float HeightAt(float share, int point) {
            float previousBand = 0f;
            float previousHeight = 0f;
            for (int b = 0; b < Bands.Length; b++) {
                if (share <= Bands[b]) {
                    float t = (share - previousBand) / (Bands[b] - previousBand);
                    return Mathf.Lerp(previousHeight, heights[b, point], t);
                }
                previousBand = Bands[b];
                previousHeight = heights[b, point];
            }
            return heights[Bands.Length - 1, point];
        }

        private static Vector3 Direction(int point) {
            float angle = point * Mathf.PI * 2f / Points;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        private LineRenderer NewRing(string name, float width, Color color) {
            Transform holder = transform.Find("SLS_NovaIndicator");
            if (holder == null) {
                holder = new GameObject("SLS_NovaIndicator").transform;
                holder.SetParent(transform, false);
            }
            GameObject go = new GameObject(name);
            go.transform.SetParent(holder, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = Points;
            line.widthMultiplier = width;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        // Unlit and coloured by its vertices, so the ring reads the same at night and in fog. The particle shader is the
        // fallback for a build that strips Sprites/Default.
        private static Shader RingShader() {
            if (material != null) { return material.shader; }
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) { shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended"); }
            if (shader == null) {
                if (warnedNoShader == false) { Logger.LogWarning("No shader for the nova indicator was found; the circle will not be drawn."); }
                warnedNoShader = true;
                return null;
            }
            material = new Material(shader);
            return shader;
        }
    }
}
