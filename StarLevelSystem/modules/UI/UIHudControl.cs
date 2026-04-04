using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Control;
using StarLevelSystem.modules.CreatureSetup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    internal static class UIHudControl {
        public class StarLevelHud {
            public bool IsBoss {
                get; set;
            }

            public int Level { get; set; } = 1;

            public List<string> DisplayedMods { get; set; } = new List<string>();

            public Dictionary<int, GameObject> Starlevel = new Dictionary<int, GameObject>();
            public Dictionary<int, Image> StarLevelFront = new Dictionary<int, Image>();
            public Dictionary<int, Image> StarLevelBack = new Dictionary<int, Image>();
            public GameObject StarLevelN {
                get; set;
            }
            public Image StarLevelNFrontImage {
                get; set;
            }
            public Image StarLevelNBackImage {
                get; set;
            }
            public Text StarLevelNText {
                get; set;
            }
            public EnemyHud.HudData Hudlink {
                get; set;
            }
            public TextMeshProUGUI HealthText {
                get; set;
            }
        }

        public static Dictionary<uint, StarLevelHud> characterExtendedHuds = new Dictionary<uint, StarLevelHud>();
        private static GameObject HealthText;

        public static void InvalidateCacheEntry(uint zdid) {
            if (characterExtendedHuds.ContainsKey(zdid)) {
                CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(zdid);

                cce.CreatureModifiers = CompositeLazyCache.GetCreatureModifiers(characterExtendedHuds[zdid].Hudlink.m_character);
                cce.CreatureNameLocalizable = CreatureModifiers.BuildCreatureLocalizableName(characterExtendedHuds[zdid].Hudlink.m_character, cce.CreatureModifiers);
                characterExtendedHuds[zdid].Hudlink.m_name.text = Localization.instance.Localize(cce.CreatureNameLocalizable);
                if (cce == null || cce.CreatureNameLocalizable == null) { return; }
            }
        }

        public static void RemoveExtenedHudFromCache(Character chara) {
            uint id = chara.GetZDOID().ID;
            RemoveExtendedHudFromCache(id);
        }

        internal static void RemoveExtendedHudFromCache(uint id) {

            if (characterExtendedHuds.ContainsKey(id)) {
                if (characterExtendedHuds[id].HealthText != null) {
                    GameObject.Destroy(characterExtendedHuds[id].HealthText.gameObject);
                }
                characterExtendedHuds.Remove(id);
            }
        }

        public static void InvalidateCacheEntry(Character chara) {
            uint id = chara.GetZDOID().ID;
            InvalidateCacheEntry(id);
        }

        internal static void StarLevelHudDisplay(GameObject star, Transform basehud, Transform bosshud) {
            SetupStar(star, 2, basehud);
            SetupStar(star, 3, basehud);
            SetupStar(star, 4, basehud);
            SetupStar(star, 5, basehud);
            SetupStar(star, 6, basehud);

            // Star N | this position is set as the same 
            GameObject star_7 = new GameObject(name: "SLS_level_n");
            star_7.transform.SetParent(basehud);
            GameObject star7 = UnityEngine.Object.Instantiate(star, star_7.transform);
            // star7.transform.SetParent(star_7.transform);
            star_7.transform.localPosition = new Vector3(x: -42, y: 19, z: 0);
            star7.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
            GameObject s7Name = new GameObject(name: "level_n_name");
            GameObject star7Name = UnityEngine.Object.Instantiate(s7Name, star_7.transform);
            star7Name.transform.SetParent(star_7.transform);
            star7Name.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
            GUIManager.Instance.CreateText(
                text: "999",
                parent: star7Name.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(185f, -13f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 14,
                color: GUIManager.Instance.ValheimYellow,
                outline: true,
                outlineColor: Color.black,
                width: 350f,
                height: 40f,
                addContentSizeFitter: false);
            star_7.SetActive(false);

            SetupStar(star, 2, bosshud, true);
            SetupStar(star, 3, bosshud, true);
            SetupStar(star, 4, bosshud, true);
            SetupStar(star, 5, bosshud, true);
            SetupStar(star, 6, bosshud, true);

            // Star N | this position is set as the same 
            GameObject star_7_boss = new GameObject(name: "SLS_level_n");
            star_7_boss.transform.SetParent(bosshud);
            GameObject star7_boss = UnityEngine.Object.Instantiate(star, star_7_boss.transform);
            // Size increase boss stars
            star7_boss.GetComponent<RectTransform>().sizeDelta = new Vector2(20, 20);
            star7_boss.transform.Find("star (1)").gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(16, 16);
            // star7.transform.SetParent(star_7.transform);
            star_7_boss.transform.localPosition = new Vector3(x: -17, y: -6, z: 0);
            star7_boss.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
            GameObject s7Name_boss = new GameObject(name: "level_n_name");
            GameObject star7Name_boss = UnityEngine.Object.Instantiate(s7Name_boss, star_7_boss.transform);
            star7Name_boss.transform.SetParent(star_7_boss.transform);
            star7Name_boss.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
            GUIManager.Instance.CreateText(
                text: "999",
                parent: star7Name_boss.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(185f, -7f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 23,
                color: GUIManager.Instance.ValheimYellow,
                outline: true,
                outlineColor: Color.black,
                width: 350f,
                height: 40f,
                addContentSizeFitter: false);
            star_7_boss.SetActive(false);
        }

        private static void SetupStar(GameObject star, int level, Transform parent_t, bool boss = false) {
            GameObject new_star_holder = new GameObject(name: $"SLS_level_{level}");
            new_star_holder.transform.SetParent(parent_t);
            GameObject new_star = UnityEngine.Object.Instantiate(star, new_star_holder.transform);

            // Make boss stars a little bigger
            if (boss) {
                RectTransform light_star_rect = new_star.transform.Find("star (1)").gameObject.GetComponent<RectTransform>();
                RectTransform dark_star_rect = new_star.GetComponent<RectTransform>();

                light_star_rect.sizeDelta = new Vector2(16, 16);
                dark_star_rect.sizeDelta = new Vector2(20, 20);
            }

            new_star_holder.SetActive(false);

            switch (level) {
                // Level 1 is the base level, so we don't need to do anything
                // Level 2-3 don't have a non-boss entry because they use the vanilla entries
                case 2:
                    if (boss) {
                        new_star_holder.transform.localPosition = new Vector3(x: 0, y: -6, z: 0);
                    } else {
                        new_star_holder.transform.localPosition = new Vector3(x: -41, y: 19, z: 0);
                        new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                    }
                    break;
                case 3:
                    if (boss) {
                        new_star_holder.transform.localPosition = new Vector3(x: -20, y: -6, z: 0);
                    } else {
                        new_star_holder.transform.localPosition = new Vector3(x: -25, y: 19, z: 0);
                        new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                    }
                    break;
                case 4:
                    if (boss) {
                        new_star_holder.transform.localPosition = new Vector3(x: 20, y: -6, z: 0);
                        new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                    } else {
                        new_star_holder.transform.localPosition = new Vector3(x: -9, y: 19, z: 0);
                        new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                    }
                    break;
                case 5:
                    if (boss) {
                        new_star_holder.transform.localPosition = new Vector3(x: -40, y: -6, z: 0);
                        new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                    } else {
                        new_star_holder.transform.localPosition = new Vector3(x: 7, y: 19, z: 0);
                        new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                    }
                    break;
                case 6:
                    if (boss) {
                        new_star_holder.transform.localPosition = new Vector3(x: 40, y: -6, z: 0);
                        new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                    } else {
                        new_star_holder.transform.localPosition = new Vector3(x: 23, y: 19, z: 0);
                        new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                    }
                    break;
            }
        }

        public static void UpdateHudforAllLevels(EnemyHud.HudData ehud) {
            if (ehud == null || ehud.m_character == null) return;
            if (ehud.m_character.IsPlayer()) return;
            int level = ehud.m_character.GetLevel();
            // if (level <= 1) return;
            // Logger.LogInfo($"Creature Level {level}");
            uint czid = ehud.m_character.GetZDOID().ID;
            if (czid == 0L) { return; }
            StarLevelHud extended_hud = new StarLevelHud();
            extended_hud.Level = level;
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(ehud.m_character);
            if (characterExtendedHuds.ContainsKey(czid)) {
                // Logger.LogInfo($"Hud already exists for {czoid}, loading");
                // Cached items which have been destroyed need to be removed and re-attached
                extended_hud = characterExtendedHuds[czid];
                if (extended_hud == null || extended_hud.Starlevel.ContainsKey(3) && extended_hud.Starlevel[3] == null) {
                    Logger.LogDebug($"UI Cache Invalid for {czid}, removing.");
                    RemoveExtendedHudFromCache(czid);
                    //CompositeLazyCache.ClearCachedCreature(ehud.m_character);
                    return;
                }

                // Mod count check here could be replaced by a Z request to refresh the cache by the controlling player
                List<string> cmods = mods.Keys.ToList();
                if (ehud.m_character.GetLevel() != extended_hud.Level || cmods.CompareListContents(mods.Keys.ToList()) == false) {
                    Logger.LogDebug($"UI Cache for {czid} outdated (level {ehud.m_character.GetLevel()}-{extended_hud.Level} or mods {extended_hud.DisplayedMods.Count}-{mods.Count}), updating cache.");
                    RemoveExtendedHudFromCache(czid);
                    CompositeLazyCache.ClearCachedCreature(ehud.m_character);
                    //Colorization.ApplyColorizationWithoutLevelEffects();
                    // Re set up the character, to ensure it gets updated visual effects, and sizing
                    CreatureSetupControl.CreatureSetup(ehud.m_character);
                    return;
                }

                if (ValConfig.EnableEnemyHeathbarNumberDisplay.Value && extended_hud.HealthText != null) {
                    extended_hud.HealthText.text = $"{ehud.m_character.GetHealth():N0}/{ehud.m_character.GetMaxHealth():N0}";
                }
            } else {
                extended_hud.Hudlink = ehud;
                if (mods == null) { return; }
                //Logger.LogDebug($"Creating new hud for {czoid} with level {level} and modifiers {ccd.Modifiers.Count()}");
                Dictionary<int, Sprite> starReplacements = new Dictionary<int, Sprite>();
                int star = 2;
                // Logger.LogDebug($"Building sprite list");
                extended_hud.DisplayedMods = mods.Keys.ToList();
                foreach (KeyValuePair<string, ModifierType> entry in mods) {
                    if (entry.Key == CreatureModifiers.NoMods) { continue; }
                    //Logger.LogDebug($"Checking modifier {entry.Key} of type {entry.Value}");
                    CreatureModifierDefinition cmd = CreatureModifiersData.ModifierDefinitions[entry.Key];
                    if (cmd.StarVisual != null) {
                        if (CreatureModifiersData.LoadedModifierSprites.ContainsKey(cmd.StarVisual)) {
                            Sprite starsprite = CreatureModifiersData.LoadedModifierSprites[cmd.StarVisual];
                            starReplacements.Add(star, starsprite);
                        }
                    }
                    star++;
                }

                //Logger.LogDebug($"Determined replacement stars {string.Join(",", starReplacements.Keys)}");

                extended_hud.IsBoss = ehud.m_character.IsBoss();

                // Modify the size of the health bar if its enabled
                if ((ValConfig.EnemyHealthbarScalarX.Value != 1f || ValConfig.EnemyHealthbarScalarY.Value != 1f) && extended_hud.IsBoss == false) {
                    RectTransform healthRect = ehud.m_gui.transform.Find("Health").GetComponent<RectTransform>();
                    Vector2 newHudSize = new Vector2(100f * ValConfig.EnemyHealthbarScalarX.Value, 5 * ValConfig.EnemyHealthbarScalarY.Value);
                    //Logger.LogDebug($"Resizing healthbar for {czid} from {healthRect.sizeDelta} to {newHudSize}");
                    healthRect.sizeDelta = newHudSize;

                    // Update the two health bars
                    RectTransform hs_rct = healthRect.Find("health_slow").GetComponent<RectTransform>();
                    hs_rct.GetComponent<GuiBar>().m_width = newHudSize.x;
                    RectTransform hs_guib = hs_rct.Find("bar").GetComponent<RectTransform>();
                    hs_guib.sizeDelta = newHudSize;

                    RectTransform hf_rct = healthRect.Find("health_fast").GetComponent<RectTransform>();
                    RectTransform hf_guib = hf_rct.Find("bar").GetComponent<RectTransform>();
                    hf_rct.GetComponent<GuiBar>().m_width = newHudSize.x;
                    hf_guib.sizeDelta = newHudSize;
                }

                // Setup the health text if enabled
                if (ValConfig.EnableEnemyHeathbarNumberDisplay.Value && extended_hud.HealthText == null) {
                    GameObject HealthTextHolder = GameObject.Instantiate(HealthText, ehud.m_gui.transform.Find("Health"));
                    extended_hud.HealthText = HealthTextHolder.GetComponent<TextMeshProUGUI>();
                    // Use a slightly diminishing scale as otherwise things get overscaled easily
                    extended_hud.HealthText.fontSize = 10 * (ValConfig.EnemyHealthbarScalarY.Value * ValConfig.HealthDisplayFontSizeAdjustment.Value);
                }

                int star_index = 2;
                while (star_index < 7) {
                    //Logger.LogDebug($"Assigning star level {star_index}");
                    extended_hud.Starlevel.Add(star_index, ehud.m_gui.transform.Find($"SLS_level_{star_index}").gameObject);
                    extended_hud.StarLevelBack.Add(star_index, ehud.m_gui.transform.Find($"SLS_level_{star_index}/star(Clone)").gameObject.GetComponent<Image>());
                    extended_hud.StarLevelFront.Add(star_index, ehud.m_gui.transform.Find($"SLS_level_{star_index}/star(Clone)/star (1)").gameObject.GetComponent<Image>());
                    if (starReplacements.ContainsKey(star_index)) {
                        //Logger.LogDebug($"Updating Icon to modifier icon {star_index} Front: {string.Join(",", extended_hud.starlevel_front.Keys)} Back: {string.Join(",", extended_hud.starlevel_back.Keys)}");
                        if (extended_hud.StarLevelFront[star_index] != null) {
                            extended_hud.StarLevelFront[star_index].sprite = starReplacements[star_index];
                            extended_hud.StarLevelFront[star_index].rectTransform.sizeDelta = new Vector2(17, 17);
                            extended_hud.StarLevelFront[star_index].color = Color.white;
                        }
                        if (extended_hud.StarLevelBack[star_index] != null) {
                            extended_hud.StarLevelBack[star_index].sprite = starReplacements[star_index];
                            extended_hud.StarLevelBack[star_index].rectTransform.sizeDelta = new Vector2(21, 21);
                        }
                    }
                    star_index++;
                }

                //Logger.LogDebug($"Assigning star level N");
                extended_hud.StarLevelN = ehud.m_gui.transform.Find("SLS_level_n").gameObject;
                extended_hud.StarLevelNBackImage = ehud.m_gui.transform.Find("SLS_level_n/star(Clone)").gameObject.GetComponent<Image>();
                extended_hud.StarLevelNFrontImage = ehud.m_gui.transform.Find("SLS_level_n/star(Clone)/star (1)").gameObject.GetComponent<Image>();
                extended_hud.StarLevelNText = ehud.m_gui.transform.Find("SLS_level_n/level_n_name(Clone)/Text").gameObject.GetComponent<Text>();
                if (starReplacements.Count > 0) {
                    extended_hud.StarLevelNFrontImage.sprite = starReplacements.First().Value;
                    extended_hud.StarLevelNFrontImage.rectTransform.sizeDelta = new Vector2(17, 17);
                    extended_hud.StarLevelNBackImage.sprite = starReplacements.First().Value;
                    extended_hud.StarLevelNBackImage.rectTransform.sizeDelta = new Vector2(21, 21);
                }

                // Need to find and add the N level text for updating here
                characterExtendedHuds.Add(czid, extended_hud);
            }
            // Show the creatures health value


            // Set star level, this can change if the characters level gets modified
            switch (level) {
                case 2:
                    extended_hud.Starlevel[2].SetActive(true);
                    extended_hud.Starlevel[3].SetActive(false);
                    extended_hud.Starlevel[4].SetActive(false);
                    extended_hud.Starlevel[5].SetActive(false);
                    extended_hud.Starlevel[6].SetActive(false);
                    break;
                case 3:
                    extended_hud.Starlevel[2].SetActive(true);
                    extended_hud.Starlevel[3].SetActive(true);
                    extended_hud.Starlevel[4].SetActive(false);
                    extended_hud.Starlevel[5].SetActive(false);
                    extended_hud.Starlevel[6].SetActive(false);
                    break;
                case 4:
                    extended_hud.Starlevel[2].SetActive(true);
                    extended_hud.Starlevel[3].SetActive(true);
                    extended_hud.Starlevel[4].SetActive(true);
                    extended_hud.Starlevel[5].SetActive(false);
                    extended_hud.Starlevel[6].SetActive(false);
                    break;
                case 5:
                    extended_hud.Starlevel[2].SetActive(true);
                    extended_hud.Starlevel[3].SetActive(true);
                    extended_hud.Starlevel[4].SetActive(true);
                    extended_hud.Starlevel[5].SetActive(true);
                    extended_hud.Starlevel[6].SetActive(false);
                    break;
                case 6:
                    extended_hud.Starlevel[2].SetActive(true);
                    extended_hud.Starlevel[3].SetActive(true);
                    extended_hud.Starlevel[4].SetActive(true);
                    extended_hud.Starlevel[5].SetActive(true);
                    extended_hud.Starlevel[6].SetActive(true);
                    break;
            }

            // Enable dynamic levels
            if (level > 6) {
                // Logger.LogInfo("Setting Nstar levels active");
                extended_hud.StarLevelN.SetActive(true);
                // get the text componet here and set its display
                extended_hud.StarLevelNText.text = (level - 1).ToString();
            } else {
                extended_hud.StarLevelN.SetActive(false);
            }
        }

        internal static void LoadAssets() {
            HealthText = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>("HealthText.prefab");
        }
    }
}
