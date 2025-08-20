using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StarLevelSystem.common
{
    internal static class LocalizationLoader
    {
        static CustomLocalization Localization;
        // This loads all localizations within the localization directory.
        // Localizations should be plain JSON objects with each of the two required entries being seperate eg:
        // "item_sword": "sword-name-here",
        // "item_sword_description": "sword-description-here",
        // the localization file itself should be a casematched language as defined by one of the "folder" language names from here:
        // https://valheim-modding.github.io/Jotunn/data/localization/language-list.html
        internal static void AddLocalizations()
        {
            Localization = LocalizationManager.Instance.GetLocalization();
            //LocalizationManager.Instance.AddLocalization(Localization);

            // Ensure localization folder exists
            var translationFolder = Path.Combine(BepInEx.Paths.ConfigPath, "StarLevelSystem", "localizations");
            Directory.CreateDirectory(translationFolder);
            //SimpleJson.SimpleJson.CurrentJsonSerializerStrategy


            // ValheimArmory.localizations.English.json,ValheimArmory.localizations.German.json,ValheimArmory.localizations.Russian.json
            // load all localization files within the localizations directory
            foreach (string embeddedResouce in typeof(StarLevelSystem).Assembly.GetManifestResourceNames())
            {
                if (!embeddedResouce.Contains("localizations")) { continue; }
                // Read the localization file

                string localization = ReadEmbeddedResourceFile(embeddedResouce);
                // since I use comments in the localization that are not valid JSON those need to be stripped
                string cleaned_localization = Regex.Replace(localization, @"\/\/.*", "");
                Dictionary<string, string> internal_localization = SimpleJson.SimpleJson.DeserializeObject<Dictionary<string, string>>(cleaned_localization);
                // Just the localization name
                var localization_name = embeddedResouce.Split('.');
                if (File.Exists($"{translationFolder}/{localization_name[2]}.json"))
                {
                    string cached_translation_file = File.ReadAllText($"{translationFolder}/{localization_name[2]}.json");
                    try
                    {
                        Dictionary<string, string> cached_localization = SimpleJson.SimpleJson.DeserializeObject<Dictionary<string, string>>(cached_translation_file);
                        UpdateLocalizationWithMissingKeys(internal_localization, cached_localization);
                        Logger.LogDebug($"Reading {translationFolder}/{localization_name[2]}.json");
                        File.WriteAllText($"{translationFolder}/{localization_name[2]}.json", SimpleJson.SimpleJson.SerializeObject(cached_localization));
                        string updated_local_translation = File.ReadAllText($"{translationFolder}/{localization_name[2]}.json");
                        Localization.AddJsonFile(localization_name[2], updated_local_translation);
                    }
                    catch
                    {
                        File.WriteAllText($"{translationFolder}/{localization_name[2]}.json", cleaned_localization);
                        Logger.LogDebug($"Reading {embeddedResouce}");
                        Localization.AddJsonFile(localization_name[2], cleaned_localization);
                    }
                }
                else
                {
                    File.WriteAllText($"{translationFolder}/{localization_name[2]}.json", cleaned_localization);
                    Logger.LogDebug($"Reading {embeddedResouce}");
                    Localization.AddJsonFile(localization_name[2], cleaned_localization);
                }

                Logger.LogDebug($"Added localization: '{localization_name[2]}'");
                // Logging some characters seem to cause issues sometimes
                // if (VAConfig.EnableDebugMode.Value == true) { Logger.LogInfo($"Localization Text: {cleaned_localization}"); }
                //Localization.AddTranslation(localization_name[2], localization);
                // Localization.AddJsonFile(localization_name[2], cleaned_localization);
            }
        }

        private static void UpdateLocalizationWithMissingKeys(Dictionary<string, string> internal_localization, Dictionary<string, string> cached_localization)
        {
            if (internal_localization.Keys.Count != cached_localization.Keys.Count)
            {
                Logger.LogDebug("Cached localization was missing some entries. They will be added.");
                foreach (KeyValuePair<string, string> entry in internal_localization)
                {
                    if (!cached_localization.ContainsKey(entry.Key))
                    {
                        cached_localization.Add(entry.Key, entry.Value);
                    }
                }
            }
        }

        // This reads an embedded file resouce name, these are all resouces packed into the DLL
        // they all have a format following this:
        // ValheimArmory.localizations.English.json
        private static string ReadEmbeddedResourceFile(string filename)
        {
            using (var stream = typeof(StarLevelSystem).Assembly.GetManifestResourceStream(filename))
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
