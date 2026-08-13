using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StarLevelSystem.common
{
    // Argument readers and the option lists commands share for tab-completion.
    internal static class TerminalArgs
    {
        internal static string GetString(this string[] args, int index, string fallback = "")
        {
            if (args == null || args.Length <= index) { return fallback; }
            return args[index];
        }

        internal static string GetStringFrom(this string[] args, int index, string fallback = "")
        {
            if (args == null || args.Length <= index) { return fallback; }
            return string.Join(" ", args.Skip(index));
        }

        internal static int GetInt(this string[] args, int index, int fallback = 0)
        {
            string raw = args.GetString(index, null);
            if (raw == null) { return fallback; }
            return int.TryParse(raw, out int parsed) ? parsed : fallback;
        }

        internal static float GetFloat(this string[] args, int index, float fallback = 0f)
        {
            string raw = args.GetString(index, null);
            if (raw == null) { return fallback; }
            return float.TryParse(raw, out float parsed) ? parsed : fallback;
        }

        internal static T GetEnum<T>(this string[] args, int index, T fallback) where T : struct, Enum
        {
            string raw = args.GetString(index, null);
            if (raw == null) { return fallback; }
            return Enum.TryParse(raw, true, out T parsed) ? parsed : fallback;
        }

        // Shared radius/range reader: reports the fallback rather than silently using it, and clamps so
        // a fat-fingered radius cannot ask for the whole world.
        internal static float ReadRadius(this SLSCommandArgs args, int index, float fallback, float max)
        {
            string raw = args.Args.GetString(index, null);
            if (raw == null) { return fallback; }
            if (float.TryParse(raw, out float parsed) == false)
            {
                args.Output.Warning($"Radius must be a number; using {fallback}.");
                return fallback;
            }
            return Mathf.Clamp(parsed, 0f, max);
        }

        internal static List<string> Names<T>() where T : struct, Enum
        {
            return Enum.GetNames(typeof(T)).ToList();
        }

        // Only the biomes that exist as a single playable biome; None and the combined flag values are
        // not somewhere anything can be placed.
        private static readonly List<string> biomeNames = new List<string>() {
            "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands", "AshLands", "DeepNorth", "Ocean"
        };

        internal static List<string> Biomes(string[] input) => biomeNames;

        internal static List<string> RadiusPresets(string[] input)
        {
            return new List<string>() { "32", "64", "128", "256", "512" };
        }
    }
}
