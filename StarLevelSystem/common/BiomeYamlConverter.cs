using StarLevelSystem.modules;
using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace StarLevelSystem.common {

    // Heightmap.Biome in the yaml config files. TolerantEnumConverter would turn a custom biome's name into None, where two
    // of them collide as one key, and the next save writes None over the name for good. Here a name vanilla does not have
    // becomes a token in BiomeRegistry, which keeps the entry apart and writes the same text back. See BiomeRegistry for why
    // names are not resolved while parsing. Added after TolerantEnumConverter, so YamlDotNet asks this one first.
    internal class BiomeYamlConverter : IYamlTypeConverter {
        public bool Accepts(Type type) {
            return type == typeof(Heightmap.Biome);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            Scalar scalar = parser.Consume<Scalar>();
            string raw = scalar.Value?.Trim();

            if (string.IsNullOrEmpty(raw) || raw == "~" || raw.Equals("null", StringComparison.OrdinalIgnoreCase)) {
                // A blank value means nothing either way, and None is what every biome field reads as nothing.
                Logger.LogWarning($"line {scalar.Start.Line}: '{scalar.Value}' is not a valid {type.Name}. Using {Heightmap.Biome.None}.");
                return Heightmap.Biome.None;
            }
            if (raw.IndexOf(',') < 0) { return BiomeRegistry.FromConfigName(raw); }

            int combined = 0;
            foreach (string part in raw.Split(',')) {
                if (BiomeRegistry.TryParsePlain(part.Trim(), out Heightmap.Biome single) == false) {
                    // A token is one biome, so this cannot be split into parts and rejoined. Keeping the whole text inert
                    // loses nothing, and the entries can be listed separately.
                    Logger.LogWarning($"line {scalar.Start.Line}: '{raw}' combines biomes that vanilla does not have, which is not supported. List them separately.");
                    return BiomeRegistry.TokenFor(raw);
                }
                combined |= (int)single;
            }
            return (Heightmap.Biome)combined;
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer) {
            emitter.Emit(new Scalar(value == null ? "" : BiomeRegistry.ConfigText((Heightmap.Biome)value)));
        }
    }
}
