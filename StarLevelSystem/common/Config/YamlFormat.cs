using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

#pragma warning disable IDE0130
namespace StarLevelSystem.common {
#pragma warning restore IDE0130

    // A serializer/deserializer triple that a config file reads and writes through.
    //
    // The important asymmetry: the SERIALIZER carries a naming convention, the DESERIALIZER carries none
    // -- only WithCaseInsensitivePropertyMatching(). A deserializer built that way reads DisplayName,
    // displayName and displayname identically, so the format choice only ever decides what generated
    // files LOOK like; it can never decide what an existing file is allowed to say. That is what makes it
    // safe to change a mod's casing without breaking every file already on every server.
    //
    // Two YamlDotNet behaviours worth knowing before designing a config type:
    //
    //  - Public FIELDS serialize, not just properties. A POCO of public fields round-trips fine and does
    //    not need converting to auto-properties.
    //  - OmitDefaults compares against default(T) -- NOT against the C# field initializer -- unless the
    //    member carries [DefaultValue]. So `float Multiplier = 1f;` is still written out (1 != 0), while
    //    an enum member sitting on its zero value is omitted.
    //
    //    A member whose initializer is a NON-default value and which has no [DefaultValue] is a trap, and
    //    `bool Thing = true;` is the common case. An admin sets Thing: false, false == default(bool), so
    //    the serializer omits it -- and the initializer puts it straight back to true the next time the
    //    file is read. Their "off" silently becomes "on" the first time anything rewrites the file.
    //
    //    The rule: if a member's initializer is not default(T), give it [DefaultValue(<that same value>)].
    //    Then "off" is written and survives, and "on" is still omitted for a tidy file.
    internal sealed class YamlFormat {
        internal ISerializer Serializer { get; private set; }
        internal IDeserializer Deserializer { get; private set; }
        // Same settings plus IgnoreUnmatchedProperties. Used as the second pass when a strict parse
        // throws on an unrecognised key, so one typo costs one setting instead of the whole file.
        internal IDeserializer TolerantDeserializer { get; private set; }

        private readonly Action<SerializerBuilder> configureSerializer;
        private readonly Action<DeserializerBuilder> configureDeserializer;

        // Every format ever built, so AddTypeConverter can retrofit a converter onto all of them.
        private static readonly List<YamlFormat> built = new List<YamlFormat>();
        private static readonly List<IYamlTypeConverter> converters = new List<IYamlTypeConverter>();

        // PascalCase out. The default for new config files -- generated yaml then matches the C# member
        // names an admin sees in the header docs.
        internal static YamlFormat Default { get; private set; }
        // camelCase out. For a mod whose on-disk format predates this framework and should not shift.
        internal static YamlFormat CamelCase { get; private set; }
        // Flow style, for ZPackage payloads and ZDO string values rather than files on disk.
        internal static YamlFormat JsonCompat { get; private set; }

        static YamlFormat() {
            Default = Build(s => s.WithNamingConvention(PascalCaseNamingConvention.Instance), null);
            CamelCase = Build(s => s.WithNamingConvention(CamelCaseNamingConvention.Instance), null);
            JsonCompat = Build(s => s.WithNamingConvention(PascalCaseNamingConvention.Instance).JsonCompatible(), null);
        }

        private YamlFormat(Action<SerializerBuilder> serializerSetup, Action<DeserializerBuilder> deserializerSetup) {
            configureSerializer = serializerSetup;
            configureDeserializer = deserializerSetup;
            Rebuild();
        }

        internal static YamlFormat Build(Action<SerializerBuilder> configureSerializer, Action<DeserializerBuilder> configureDeserializer) {
            YamlFormat format = new YamlFormat(configureSerializer, configureDeserializer);
            built.Add(format);
            return format;
        }

        // Register a converter on every format, including ones already built. Call before
        // YamlConfigManager.Init() -- a converter added afterwards will not have been applied to
        // anything already loaded.
        internal static void AddTypeConverter(IYamlTypeConverter converter) {
            if (converter == null) { return; }
            converters.Add(converter);
            for (int i = 0; i < built.Count; i++) { built[i].Rebuild(); }
        }

        private void Rebuild() {
            SerializerBuilder serializerBuilder = new SerializerBuilder()
                // Without this, an object reused by reference emits &a1 / *a1 anchors, which read as file
                // corruption to an admin editing yaml by hand.
                .DisableAliases()
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults);
            configureSerializer?.Invoke(serializerBuilder);
            for (int i = 0; i < converters.Count; i++) { serializerBuilder.WithTypeConverter(converters[i]); }
            Serializer = serializerBuilder.Build();

            Deserializer = BuildDeserializer(tolerant: false);
            TolerantDeserializer = BuildDeserializer(tolerant: true);
        }

        private IDeserializer BuildDeserializer(bool tolerant) {
            DeserializerBuilder builder = new DeserializerBuilder().WithCaseInsensitivePropertyMatching();
            if (tolerant) { builder.IgnoreUnmatchedProperties(); }
            configureDeserializer?.Invoke(builder);
            for (int i = 0; i < converters.Count; i++) { builder.WithTypeConverter(converters[i]); }
            return builder.Build();
        }
    }
}
