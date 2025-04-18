using HarmonyLib;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;
using UnityEngine;
using static LevelEffects;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    public class ColorDef
    {
        public float hue {  get; set; }
        public float saturation { get; set; }
        public float value { get; set; }
        public bool is_emissive { get; set; } = false;

        public LevelEffects.LevelSetup toLevelEffect() {
            return new LevelEffects.LevelSetup() {
                m_scale = 1f,
                m_hue = hue,
                m_saturation = saturation,
                m_value = value,
                m_setEmissiveColor = is_emissive,
                m_emissiveColor = new Color(hue, saturation, value)
            };
        }
    }
    public static class Colorization
    {
        public static CreatureColorizationSettings creatureColorizationSettings = defaultColorizationSettings;
        private static CreatureColorizationSettings defaultColorizationSettings = new CreatureColorizationSettings()
        {
            characterSpecificColorization = new Dictionary<string, Dictionary<int,ColorDef>>() {
                {"Deer", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.05f, saturation = -0.1f, value = 0f }},
                    { 3, new ColorDef() { hue = 0.09f, saturation = -0.5f, value = -0.05f }}
                }},
                {"Boar", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.05f, saturation = -0.1f, value = 0f }},
                    { 3, new ColorDef() { hue = 0.09f, saturation = -0.5f, value = -0.05f }}
                }},
                {"Neck", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.24f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0.42f, saturation = 0f, value = 0f }}
                }},
                {"Greyling", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.06f, saturation = 0.1f, value = 0.05f }},
                    { 3, new ColorDef() { hue = -0.5f, saturation = 0.1f, value = 0f }}
                }},
                {"Greydwarf", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.06f, saturation = 0.1f, value = 0.05f }},
                    { 3, new ColorDef() { hue = -0.5f, saturation = 0.1f, value = 0f }}
                }},
                {"Greydwarf_Shaman", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.174f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0.415f, saturation = 0f, value = 0f }}
                }},
                {"Greydwarf_Elite", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.046f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.108f, saturation = 0f, value = 0f }}
                }},
                {"Skeleton", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.03f, saturation = 0.3f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.3f, value = -0.1f }}
                }},
                {"Skeleton_NoArcher", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.03f, saturation = 0.3f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.3f, value = -0.1f }}
                }},
                {"Skeleton_Poison", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.16f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.2f, saturation = 0f, value = 0f }}
                }},
                {"Troll", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.14f, saturation = 0.1f, value = 0f }},
                    { 3, new ColorDef() { hue = 0.44f, saturation = 0.2f, value = 0f }}
                }},
                {"Draugr", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.27f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.25f, saturation = 0.04f, value = 0f }}
                }},
                {"Draugr_Ranged", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.27f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.25f, saturation = 0.04f, value = 0f }}
                }},
                {"Draugr_Elite", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.1f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.2f, saturation = 0f, value = 0f }}
                }},
                {"Abomination", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.1f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.2f, saturation = 0f, value = 0f }}
                }},
                {"Surtling", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }}
                }},
                {"Leech", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.23f, saturation = 0.26f, value = 0.04f }},
                    { 3, new ColorDef() { hue = -0.139f, saturation = 0.47f, value = 0.06f }}
                }},
                {"Blob", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.23f, saturation = 0.26f, value = 0.04f }},
                    { 3, new ColorDef() { hue = -0.139f, saturation = 0.47f, value = 0.06f }}
                }},
                {"BlobElite", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.23f, saturation = 0.26f, value = 0.04f }},
                    { 3, new ColorDef() { hue = -0.139f, saturation = 0.47f, value = 0.06f }}
                }},
                {"Wolf", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0.1f, value = -0.03f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.2f, value = -0.05f }}
                }},
                {"Ulv", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0.1f, value = -0.03f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.2f, value = -0.05f }}
                }},
                {"Fenring", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0.1f, value = -0.03f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.2f, value = -0.05f }}
                }},
                {"Fenring_Cultist", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0.1f, value = -0.03f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.2f, value = -0.05f }}
                }},
                {"StoneGolem", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0.1f, value = -0.03f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.2f, value = -0.05f }}
                }},
                {"Gjall", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.201f, saturation = 0.29f, value = -0.42f }},
                    { 3, new ColorDef() { hue = -0.103f, saturation = -0.08f, value = -0.42f }}
                }},
                {"Asksvin_hatchling", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.477f, saturation = -1f, value = 0.02f }},
                    { 3, new ColorDef() { hue = 0.5f, saturation = -0.08f, value = 0.08f }}
                }},
                {"GoblinBrute", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.1f, saturation = -0.1f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.18f, saturation = 0f, value = 0f }}
                }},
                {"Charred_Twitcher", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }} 
                }},
                {"GoblinArcher", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.05f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.15f, saturation = 0f, value = 0f }}
                }},
                {"Charred_Archer", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }}
                }},
                {"Seeker", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.28f, saturation = -0.3f, value = -0.1f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.1f, value = -0.2f }} 
                }},
                {"DvergerMageIce", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.1f, saturation = 0.2f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = -1f, value = 0f }} 
                }},
                {"Charred_Melee", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }}
                }},
                {"Skeleton_Hildir", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.16f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.2f, saturation = 0f, value = 0f }}
                }},
                {"Goblin", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.05f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.15f, saturation = 0f, value = 0f }}
                }},
                {"DvergerMageSupport", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.1f, saturation = 0.2f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = -1f, value = 0f }}
                }},
                {"DvergerMageFire", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.1f, saturation = 0.2f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = -1f, value = 0f }}
                }},
                {"Asksvin", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.477f, saturation = -1f, value = 0.02f }},
                    { 3, new ColorDef() { hue = 0.5f, saturation = -0.08f, value = 0.08f }}
                }},
                {"Tick", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.337f, saturation = -0.09f, value = -0.41f }},
                    { 3, new ColorDef() { hue = -0.027f, saturation = 0.29f, value = -0.41f }}
                }},
                {"SeekerBrute", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.5f, saturation = 0.05f, value = -0.08f }},
                    { 3, new ColorDef() { hue = -0.351f, saturation = -0.04f, value = -0.1f }}
                }},
                {"Skeleton_Hildir_nochest", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.16f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.2f, saturation = 0f, value = 0f }}
                }},
                {"Dverger", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.1f, saturation = 0.2f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = -1f, value = 0f }}
                }},
                {"Charred_Mage", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }}
                }},
                {"DvergerAshlands", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.1f, saturation = 0.2f, value = 0f }},
                    { 3,new ColorDef() { hue = 0f, saturation = -1f, value = 0f }}
                }},
                {"Charred_Melee_Dyrnwyn", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3,new ColorDef() { hue = 0f, saturation = 0f, value = 0f }}
                }},
                {"Troll_Summoned", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.14f, saturation = 0.1f, value = 0f }},
                    { 3, new ColorDef() { hue = 0.44f, saturation = 0.2f, value = 0f }}
                }},
                {"Skeleton_Friendly", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.03f, saturation = 0.3f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.1f, saturation = 0.3f, value = -0.1f }},
                    { 4, new ColorDef() { hue = -0.15f, saturation = 0.3f, value = -0.151f }},
                    { 5, new ColorDef() { hue = -0.1f, saturation = 0.3f, value = -0.18f }}
                }},
                {"DvergerMage", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.1f, saturation = 0.2f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = -1f, value = 0f }}
                }},
                {"Charred_Twitcher_Summoned", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }}
                }},
                {"Hen", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0.05f, saturation = -0.1f, value = 0f }},
                    { 3, new ColorDef() { hue = 0.09f, saturation = -0.5f, value = -0.05f }}
                }},
                {"Charred_Melee_Fader", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }}
                }},
                {"Charred_Archer_Fader", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }},
                    { 3, new ColorDef() { hue = 0f, saturation = 0f, value = 0f }}
                }},
                {"GoblinBrute_Hildir", new Dictionary<int, ColorDef>() {
                    { 2, new ColorDef() { hue = -0.1f, saturation = -0.1f, value = 0f }},
                    { 3, new ColorDef() { hue = -0.18f, saturation = 0f, value = 0f }}
                }},
            },
            defaultLevelColorization = new Dictionary<int, ColorDef>() {
                { 2,new ColorDef() { hue = 0.07130837f, saturation = 0.2f, value = 0.130073f } },
                { 3,new ColorDef() { hue = -0.07488244f, saturation = 0.4406867f, value = 0.01721987f } },
                { 4,new ColorDef() { hue = -0.04446793f, saturation = 0.05205864f, value = -0.191282f } },
                { 5,new ColorDef() { hue = 0.08774569f, saturation = -0.3959962f, value = 0.224104f } },
                { 6,new ColorDef() { hue = -0.05712423f, saturation = 0.09905273f, value = -0.1369882f } },
                { 7,new ColorDef() { hue = 0.08566696f, saturation = -0.3398137f, value = -0.2270744f } },
                { 8,new ColorDef() { hue = 0.03924248f, saturation = -0.04047304f, value = -0.1966226f } },
                { 9,new ColorDef() { hue = -0.3575756f, saturation = 0.09342755f, value = 0.3008582f } }
            }
        };

        public static void Init() {
            creatureColorizationSettings = defaultColorizationSettings;
        }

        public static string YamlDefaultConfig() {
            var yaml = DataObjects.yamlserializer.Serialize(defaultColorizationSettings);
            return yaml;
        }
        public static void UpdateYamlConfig(DataObjects.CreatureColorizationSettings newcfg) {
            creatureColorizationSettings = newcfg;
        }
        public static bool UpdateYamlConfig(string yaml) {
            try {
                creatureColorizationSettings = DataObjects.yamldeserializer.Deserialize<DataObjects.CreatureColorizationSettings>(yaml);
            } catch (System.Exception ex) {
                StarLevelSystem.Log.LogError($"Failed to parse ColorizationSettings YAML: {ex.Message}");
                return false;
            }
            return true;
        }

        // Consider if we want to use emissive colors?
        public static void SetupLevelEffects() {
            for (int level = defaultColorizationSettings.defaultLevelColorization.Count + 2; 103 > level; level++)
            {
                float sat = UnityEngine.Random.Range(-0.1f, 0.1f);
                float hue = UnityEngine.Random.Range(-0.5f, 0.5f);
                float value = UnityEngine.Random.Range(-0.5f, 0.5f);
                Logger.LogDebug($"LevelEffects: {level} - hue:{hue}, sat:{sat}, val:{value}");
                defaultColorizationSettings.defaultLevelColorization.Add(level, new ColorDef() { hue = hue, saturation = sat, value = value });
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        public static class AddLevelEffectsWhenSpawned {
            public static void Postfix(Character __instance) {
                if (__instance.m_level <= 1) { return; }
                //LevelEffects le = __instance.GetComponentInChildren<LevelEffects>();
                //if (le == null) { ApplyColorizationWithoutLevelEffects(__instance); }
                ApplyColorizationWithoutLevelEffects(__instance);
                float scale = 1 + (ValConfig.PerLevelScaleBonus.Value * __instance.m_level);
                Logger.LogInfo($"Setting character size {scale} and color.");
                __instance.transform.localScale *= scale;
                Physics.SyncTransforms();
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class AddLevelEffectsWhenLoaded
        {
            // Characters that are spawned are spawned at level 1- so this won't trigger
            // This will only trigger for characters that already have their level set
            public static void Postfix(Character __instance) {
                if (__instance.m_level <= 1) { return; }
                //LevelEffects le = __instance.GetComponentInChildren<LevelEffects>();
                //if (le == null) { ApplyColorizationWithoutLevelEffects(__instance); }
                ApplyColorizationWithoutLevelEffects(__instance);
                float scale = 1 + (ValConfig.PerLevelScaleBonus.Value * __instance.m_level);
                Logger.LogInfo($"Setting character size {scale} and color.");
                __instance.transform.localScale *= scale;
                Physics.SyncTransforms();
            }
        }


        private static void ApplyColorizationWithoutLevelEffects(Character cgo) {
            LevelSetup genlvlup = creatureColorizationSettings.defaultLevelColorization[cgo.m_level].toLevelEffect();
            string cname = Utils.GetPrefabName(cgo.gameObject);
            Logger.LogDebug($"Checking for character specific colorization {cname}");
            if (creatureColorizationSettings.characterSpecificColorization.ContainsKey(cname) && creatureColorizationSettings.characterSpecificColorization[cname].Count >= cgo.m_level) {
                Logger.LogDebug($"Found character specific colorization for {cname}");
                genlvlup = creatureColorizationSettings.characterSpecificColorization[cname][cgo.m_level].toLevelEffect();
            } else { Logger.LogDebug($"No character specific colorization for {cname} - {cgo.m_level}"); }

            foreach (var smr in cgo.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                Material[] sharedMaterials2 = smr.sharedMaterials;
                sharedMaterials2[0] = new Material(sharedMaterials2[0]);
                sharedMaterials2[0].SetFloat("_Hue", genlvlup.m_hue);
                sharedMaterials2[0].SetFloat("_Saturation", genlvlup.m_saturation);
                sharedMaterials2[0].SetFloat("_Value", genlvlup.m_value);
                if (genlvlup.m_setEmissiveColor)
                {
                    sharedMaterials2[0].SetColor("_EmissionColor", genlvlup.m_emissiveColor);
                }
                smr.sharedMaterials = sharedMaterials2;
            }
        }

        //public static void DumpDefaultColorizations()
        //{
        //    foreach(var noid in  Resources.FindObjectsOfTypeAll<Humanoid>()) {
        //        if (noid == null) { continue; }
        //        LevelEffects le = noid.GetComponentInChildren<LevelEffects>();
        //        if (le != null) {
        //            string name = noid.name.Replace("(Clone)", "");
        //            if (!defaultColorizationSettings.characterSpecificColorization.ContainsKey(name)) {
        //                defaultColorizationSettings.characterSpecificColorization.Add(name, new List<ColorDef>());
        //            }

        //            foreach(var colset in le.m_levelSetups) {
        //                defaultColorizationSettings.characterSpecificColorization[noid.name].Add(new ColorDef() { value = colset.m_value, hue = colset.m_hue, saturation = colset.m_saturation, is_emissive = colset.m_setEmissiveColor });
        //            }
        //        }
        //    }
        //    // Copy over the updated defaults
        //    Init();
        //    Logger.LogInfo(YamlDefaultConfig());
        //}


        private static void StarLevelScaleChanged(object s, EventArgs e)
        {
            IEnumerable<Character> objects = Resources.FindObjectsOfTypeAll<Character>();
        }
    }
}
