using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.Modifiers.Shared
{
    // Keeps the bundled modifier effects (fire, frost, poison, lightning, Lifelink, Earth Caller's spikes, the shield shimmer)
    // from drawing as solid magenta squares when another mod loads a copy of their shader that this graphics card cannot run
    // (issue #34). They use Jotunn's mock of "Legacy Shaders/Particles/Alpha Blended", which Jotunn resolves by name to the
    // last loaded shader of that name it finds, without checking that it runs here. ChebsNecromancy's bundle carries copies of
    // built-in shaders compiled for OpenGL and Vulkan only, so on DirectX the effects could be given one of those. Any effect
    // material left on a shader that cannot run is moved to a loaded copy of the same name that can.
    internal static class ModifierShaders
    {
        // Unity's stand-in for a material with no usable shader. Jotunn's walk gives one to each empty material slot, such as
        // the disabled renderer in the fire effect. Those slots are never drawn, so there is nothing to switch or report.
        private const string ErrorShader = "Hidden/InternalErrorShader";
        private static readonly HashSet<string> reportedMissing = new HashSet<string>();

        // Hooked where ModifierSounds is: after Jotunn's mock pass on every world load, and after every modifier prefab load.
        // Jotunn's walk replaces each prefab renderer's materials with copies, so the materials are read again each time.
        internal static void Apply() {
            if (GUIManager.IsHeadless()) { return; }
            Dictionary<string, Shader> working = null;
            Dictionary<string, int> moved = new Dictionary<string, int>();
            foreach (Material material in EffectMaterials()) {
                if (material.shader == null || material.shader.isSupported) { continue; }
                string name = material.shader.name;
                if (name == ErrorShader) { continue; }
                // Only built when something needs it, which is never on a normal install.
                if (working == null) { working = WorkingShadersByName(); }
                if (working.TryGetValue(name, out Shader shader) == false) {
                    if (reportedMissing.Add(name)) {
                        Logger.LogWarning($"Shader {name} used by modifier effects cannot run on this graphics card and no working copy of it is loaded; those effects may draw as magenta squares.");
                    }
                    continue;
                }
                material.shader = shader;
                moved.TryGetValue(name, out int count);
                moved[name] = count + 1;
            }
            foreach (KeyValuePair<string, int> entry in moved) {
                Logger.LogInfo($"Shader {entry.Key} on {entry.Value} modifier effect materials cannot run on this graphics card, likely a copy loaded by another mod. Switched them to a working copy.");
            }
        }

        private static HashSet<Material> EffectMaterials() {
            HashSet<Material> found = new HashSet<Material>();
            foreach (GameObject prefab in CreatureModifiersData.BundledEffectPrefabs()) {
                // A ParticleSystemRenderer lists its trail material here too. Reading material or materials instead would
                // make yet more copies.
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true)) {
                    foreach (Material material in renderer.sharedMaterials) {
                        if (material != null) { found.Add(material); }
                    }
                }
            }
            return found;
        }

        private static Dictionary<string, Shader> WorkingShadersByName() {
            Dictionary<string, Shader> working = new Dictionary<string, Shader>();
            foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>()) {
                if (shader.isSupported && working.ContainsKey(shader.name) == false) { working.Add(shader.name, shader); }
            }
            return working;
        }
    }
}
