using StarLevelSystem.Data;
using UnityEngine;
using UnityEngine.Audio;

namespace StarLevelSystem.Modifiers.Shared
{
    // The sounds the bundled modifier effects play themselves -- each death nova's fizz and Earth Caller's rumble -- made to
    // behave like the game's own:
    //  - 3D. The bundle built them flat (spatial blend 0), so a nova going off anywhere in reach played at full volume in
    //    the player's ears however far away it was, several at once stacked, and their top priority stole voices from the
    //    game's sounds;
    //  - on the game's mixer. Valheim holds AudioListener.volume at 1 and applies the Master and SFX sliders only through
    //    AudioMan's mixer. The bundle points the sources at JVLmock_ groups, which Jotunn resolves by name alone, keeping the
    //    last AudioMixerGroup of that name it finds, so a mod shipping its own copy of the mixer could take them off the
    //    sliders. Each source is pointed at the game's own group of the same name.
    // The vanilla effects the novas and spikes spawn alongside are the game's prefabs and already do both. Effects other
    // mods register through the API are theirs and are left alone.
    internal static class ModifierSounds
    {
        private const string MockPrefix = "JVLmock_";
        private const string DefaultGroup = "SFX";
        // As the game's own explosions (the fenring ice nova, Eikthyr's stomp): full volume within 5 m, fading out by 30 m.
        private const float MinDistance = 5f;
        private const float MaxDistance = 30f;
        private const int Priority = 128;

        // PrefabManager.OnPrefabsRegistered, after Jotunn's mock pass on every world load, and after every modifier prefab
        // load, so a modifier first loaded in-world (a config reload or a server's config) is covered too. The mixer part is
        // skipped where there is no AudioMan: before the first world load, which the world load repeats, and on a server.
        internal static void Apply() {
            AudioMixer mixer = AudioMan.instance != null ? AudioMan.instance.m_masterMixer : null;
            foreach (GameObject prefab in CreatureModifiersData.BundledEffectPrefabs()) { Apply(prefab, mixer); }
        }

        private static void Apply(GameObject prefab, AudioMixer mixer) {
            foreach (AudioSource source in prefab.GetComponentsInChildren<AudioSource>(true)) {
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = MinDistance;
                source.maxDistance = MaxDistance;
                source.priority = Priority;
                if (mixer == null) { continue; }
                AudioMixerGroup group = source.outputAudioMixerGroup;
                if (group != null && group.audioMixer == mixer) { continue; }
                AudioMixerGroup gameGroup = GameGroup(mixer, group != null ? group.name : DefaultGroup) ?? GameGroup(mixer, DefaultGroup);
                if (gameGroup != null) { source.outputAudioMixerGroup = gameGroup; }
            }
        }

        private static AudioMixerGroup GameGroup(AudioMixer mixer, string name) {
            if (name.StartsWith(MockPrefix)) { name = name.Substring(MockPrefix.Length); }
            foreach (AudioMixerGroup group in mixer.FindMatchingGroups(name)) {
                if (group.name == name) { return group; }
            }
            return null;
        }
    }
}
