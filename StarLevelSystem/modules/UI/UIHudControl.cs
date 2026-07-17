using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.Modifiers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    internal static class UIHudControl {

        internal static List<StarLevelHud> CurrentBossHuds = new List<StarLevelHud>();
        internal static Color StarColor = new Color(1, 0.8174f, 0.3382f, 1f);

        public class StarLevelHud {
            public bool IsBoss { get; set; }
            public string CreatureNameLocalized { get; set; }
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
            public EnemyHud.HudData HudLink {
                get; set;
            }
            public TextMeshProUGUI HealthText {
                get; set;
            }
        }

        // Marks the boss hud layout as needing re-application; the actual Unity work is done
        // on the main thread inside StackBossHuds (called from the EnemyHud.UpdateHuds postfix).
        internal static bool BossHudConfigDirty = true;
        private static int lastBossHudCount = -1;

        // Keyed by the full ZDOID (not the bare uint ZDOID.ID) so creatures created by different
        // peers with the same per-peer ID counter value don't collide. See CompositeLazyCache.
        public static Dictionary<ZDOID, StarLevelHud> characterExtendedHuds = new Dictionary<ZDOID, StarLevelHud>();
        private static GameObject HealthText;
        static Sprite defaultStar;

        internal static GameObject BossHudRoot;

        internal static void SetDefaultStar() {
            defaultStar = PrefabManager.Cache.GetPrefab<Sprite>("craft_icon");
        }

        internal static void RemoveExtendedHudFromCache(ZDOID id) {
            if (characterExtendedHuds.ContainsKey(id)) {
                //Logger.LogDebug($"Removing extended hud from cache for {id}");
                if (characterExtendedHuds[id].HealthText != null) {
                    GameObject.Destroy(characterExtendedHuds[id].HealthText.gameObject);
                }
                characterExtendedHuds.Remove(id);
            }
        }

        public static void InvalidateCacheEntry(Character chara) {
            if (chara == null || chara.GetZDOID() == ZDOID.None) { return; }
            ZDOID id = chara.GetZDOID();
            if (characterExtendedHuds.ContainsKey(id) == false) { return; }

            CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(id);
            if (cce == null) {
                cce = CompositeLazyCache.GetAndSetLocalCache(chara);
                if (cce == null) { return; }
            }

            StarLevelHud extendedHUD = characterExtendedHuds[id];
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(extendedHUD.HudLink.m_character);
            cce.CreatureModifiers = mods;
            cce.CreatureNameLocalizable = CreatureModifiers.BuildCreatureLocalizableName(extendedHUD.HudLink.m_character, mods);

            extendedHUD.Level = cce.Level;
            UpdateHudModifiers(id, extendedHUD, mods);
        }

        internal static void StarLevelHudDisplay(GameObject star, Transform baseHud, Transform bossHud) {
            SetupStar(star, 2, baseHud);
            SetupStar(star, 3, baseHud);
            SetupStar(star, 4, baseHud);
            SetupStar(star, 5, baseHud);
            SetupStar(star, 6, baseHud);

            // Star N | this position is set as the same 
            GameObject star_7 = new GameObject(name: "SLS_level_n");
            star_7.transform.SetParent(baseHud);
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

            SetupStar(star, 2, bossHud, true);
            SetupStar(star, 3, bossHud, true);
            SetupStar(star, 4, bossHud, true);
            SetupStar(star, 5, bossHud, true);
            SetupStar(star, 6, bossHud, true);

            // Star N | this position is set as the same 
            GameObject star_7_boss = new GameObject(name: "SLS_level_n");
            star_7_boss.transform.SetParent(bossHud);
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

        public static void UpdateHudForAllLevels(EnemyHud.HudData ehud) {
            if (ehud == null || ehud.m_character == null) return;
            if (ehud.m_character.IsPlayer()) return;
            int level = ehud.m_character.GetLevel();
            // if (level <= 1) return;
            // Logger.LogInfo($"Creature Level {level}");
            ZDOID czid = ehud.m_character.GetZDOID();
            if (czid == ZDOID.None) { return; }
            StarLevelHud extended_hud = new StarLevelHud {
                Level = level
            };
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

                List<string> currentMods = mods.Keys.ToList();
                CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(czid);
                int levelCheck = ehud.m_character.GetLevel();
                if (currentMods.CompareListContents(extended_hud.DisplayedMods) == false || cce == null || levelCheck != extended_hud.Level) {
                    Logger.LogDebug($"UI Cache for {czid} outdated (level {ehud.m_character.GetLevel()}-{extended_hud.Level} or mods {extended_hud.DisplayedMods.Count}-{mods.Count} or name {extended_hud.HudLink.m_name.text}-{extended_hud.CreatureNameLocalized}), updating cache.");
                    CompositeLazyCache.ClearCachedCreature(ehud.m_character);
                    InvalidateCacheEntry(ehud.m_character);
                    
                    //Colorization.ApplyColorizationWithoutLevelEffects();
                    // Re set up the character, to ensure it gets updated visual effects, and sizing
                    
                    return;
                }

                extended_hud.HudLink.m_name.text = extended_hud.CreatureNameLocalized;

                if (ValConfig.EnableEnemyHealthbarNumberDisplay.Value && extended_hud.HealthText != null) {
                    extended_hud.HealthText.text = $"{ehud.m_character.GetHealth():N0}/{ehud.m_character.GetMaxHealth():N0}";
                }
            } else {
                extended_hud.HudLink = ehud;
                // if (mods == null) { return; }
                Logger.LogDebug($"Creating new hud for {ehud.m_character} with level {level}");

                extended_hud.IsBoss = ehud.m_character.IsBoss();

                // Track this boss hud for boss hud sizing and ordering
                if (extended_hud.IsBoss) {
                    CurrentBossHuds.Add(extended_hud);
                }

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
                if (ValConfig.EnableEnemyHealthbarNumberDisplay.Value && extended_hud.HealthText == null) {
                    if (HealthText == null) { LoadAssets(); }
                    GameObject HealthTextHolder = GameObject.Instantiate(HealthText, ehud.m_gui.transform.Find("Health"));
                    extended_hud.HealthText = HealthTextHolder.GetComponent<TextMeshProUGUI>();
                    // Use a slightly diminishing scale as otherwise things get over scaled easily
                    extended_hud.HealthText.fontSize = 10 * (ValConfig.EnemyHealthbarScalarY.Value * ValConfig.HealthDisplayFontSizeAdjustment.Value);

                    if (ValConfig.UseCustomHealthFont.Value == false) {
                        extended_hud.HealthText.font = extended_hud.HudLink.m_name.font;
                    }
                    // Health text boss fixes? resize?
                    if (extended_hud.IsBoss) {
                        RectTransform hthRT = HealthTextHolder.transform as RectTransform;
                        hthRT.localPosition = new Vector3(0, 4, 0);
                        extended_hud.HealthText.fontSize = 20f;
                        extended_hud.HealthText.characterSpacing = 12f;
                    }
                }

                UpdateHudModifiers(czid, extended_hud, mods);
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
                case > 6:
                    extended_hud.Starlevel[2].SetActive(false);
                    extended_hud.Starlevel[3].SetActive(false);
                    extended_hud.Starlevel[4].SetActive(false);
                    extended_hud.Starlevel[5].SetActive(false);
                    extended_hud.Starlevel[6].SetActive(false);
                    break;
            }

            // Enable dynamic levels
            if (level > 6) {
                // Logger.LogInfo("Setting NStar levels active");
                extended_hud.StarLevelN.SetActive(true);
                // get the text component here and set its display
                extended_hud.StarLevelNText.text = (level - 1).ToString();
            } else {
                extended_hud.StarLevelN.SetActive(false);
            }
        }

        public static void UpdateHudModifiers(ZDOID zdoid, StarLevelHud extended_hud, Dictionary<string, ModifierType> mods) {
            if (extended_hud.HudLink == null || extended_hud.HudLink.m_gui == null) {
                RemoveExtendedHudFromCache(zdoid);
                return;
            }

            CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(extended_hud.HudLink.m_character);
            if (cce != null) {
                if (cce.CreatureModifiers == null || cce.CreatureModifiers.Keys.ToList().CompareListContents(mods.Keys.ToList()) == false) {
                    //Logger.LogDebug($"Creature cache is outdated for {zdoid}-{cce.RefCreatureName}-{cce.Level}");
                    CompositeLazyCache.ClearCachedCreature(extended_hud.HudLink.m_character);
                    cce = CompositeLazyCache.GetAndSetLocalCache(extended_hud.HudLink.m_character);
                }
                //Logger.LogDebug($"Updating hud Name for {zdoid} with modifiers {string.Join(", ", mods.Keys)}");
                extended_hud.CreatureNameLocalized = extended_hud.HudLink.m_character.m_nview.GetZDO().GetString(ZDOVars.s_tamedName, Localization.instance.Localize(cce.CreatureNameLocalizable));
                extended_hud.HudLink.m_name.text = extended_hud.CreatureNameLocalized;
            }

            Dictionary<int, Sprite> starReplacements = new Dictionary<int, Sprite>();
            int star = 2;
            if (mods == null) { mods = new Dictionary<string, ModifierType>(); }
            // Logger.LogDebug($"Building sprite list");
            extended_hud.DisplayedMods = mods.Keys.ToList();
            // When the display style is None, leave starReplacements empty so the slots fall through to the default stars below.
            if (CreatureModifiersData.SelectedModifierDisplayStyle != ModifierDisplayStyle.None) {
                foreach (KeyValuePair<string, ModifierType> entry in mods) {
                    if (entry.Key == CreatureModifiers.NoMods) { continue; }
                    //Logger.LogDebug($"Checking modifier {entry.Key} of type {entry.Value}");
                    CreatureModifierDefinition cmd = CreatureModifiersData.ModifierDefinitions[entry.Key];
                    if (cmd.StarVisual != null) {
                        if (CreatureModifiersData.LoadedModifierSprites.ContainsKey(cmd.StarVisual)) {
                            Sprite starSprite = CreatureModifiersData.LoadedModifierSprites[cmd.StarVisual];
                            starReplacements.Add(star, starSprite);
                        }
                    }
                    star++;
                }
            }

            int star_index = 2;
            while (star_index < 7) {
                if (extended_hud.Starlevel.ContainsKey(star_index) == false) {
                    extended_hud.Starlevel.Add(star_index, extended_hud.HudLink.m_gui.transform.Find($"SLS_level_{star_index}").gameObject);
                }
                if (extended_hud.StarLevelBack.ContainsKey(star_index) == false) {
                    extended_hud.StarLevelBack.Add(star_index, extended_hud.HudLink.m_gui.transform.Find($"SLS_level_{star_index}/star(Clone)").gameObject.GetComponent<Image>());
                }
                if (extended_hud.StarLevelFront.ContainsKey(star_index) == false) {
                    extended_hud.StarLevelFront.Add(star_index, extended_hud.HudLink.m_gui.transform.Find($"SLS_level_{star_index}/star(Clone)/star (1)").gameObject.GetComponent<Image>());
                }
                //Logger.LogDebug($"Assigning star level {star_index}");
                if (starReplacements.ContainsKey(star_index)) {
                    //Logger.LogDebug($"Updating Icon to modifier icon {star_index}");
                    if (extended_hud.StarLevelFront[star_index] != null) {
                        extended_hud.StarLevelFront[star_index].sprite = starReplacements[star_index];
                        extended_hud.StarLevelFront[star_index].rectTransform.sizeDelta = new Vector2(17, 17);
                        extended_hud.StarLevelFront[star_index].color = Color.white;
                    }
                    if (extended_hud.StarLevelBack[star_index] != null) {
                        extended_hud.StarLevelBack[star_index].sprite = starReplacements[star_index];
                        extended_hud.StarLevelBack[star_index].rectTransform.sizeDelta = new Vector2(21, 21);
                    }
                } else {
                    // Reset the star to the default if there is no modifier, this is needed for when modifiers are removed and the cache is still valid
                    //Logger.LogDebug($"Assigning vanilla star {star_index}");
                    if (extended_hud.StarLevelFront[star_index] != null) {
                        extended_hud.StarLevelFront[star_index].sprite = defaultStar;
                        extended_hud.StarLevelFront[star_index].rectTransform.sizeDelta = new Vector2(14, 14);
                        extended_hud.StarLevelFront[star_index].color = StarColor;
                    }
                    if (extended_hud.StarLevelBack[star_index] != null) {
                        extended_hud.StarLevelBack[star_index].sprite = defaultStar;
                        extended_hud.StarLevelBack[star_index].rectTransform.sizeDelta = new Vector2(16, 16);
                    }
                }
                star_index++;
            }

            //Logger.LogDebug($"Assigning star level N");
            extended_hud.StarLevelN = extended_hud.HudLink.m_gui.transform.Find("SLS_level_n").gameObject;
            extended_hud.StarLevelNBackImage = extended_hud.HudLink.m_gui.transform.Find("SLS_level_n/star(Clone)").gameObject.GetComponent<Image>();
            extended_hud.StarLevelNFrontImage = extended_hud.HudLink.m_gui.transform.Find("SLS_level_n/star(Clone)/star (1)").gameObject.GetComponent<Image>();
            extended_hud.StarLevelNText = extended_hud.HudLink.m_gui.transform.Find("SLS_level_n/level_n_name(Clone)/Text").gameObject.GetComponent<Text>();
            if (starReplacements.Count > 0) {
                extended_hud.StarLevelNFrontImage.sprite = starReplacements.First().Value;
                extended_hud.StarLevelNFrontImage.rectTransform.sizeDelta = new Vector2(17, 17);
                extended_hud.StarLevelNBackImage.sprite = starReplacements.First().Value;
                extended_hud.StarLevelNBackImage.rectTransform.sizeDelta = new Vector2(21, 21);
            } else {
                extended_hud.StarLevelNFrontImage.sprite = defaultStar;
                extended_hud.StarLevelNFrontImage.rectTransform.sizeDelta = new Vector2(17, 17);
                extended_hud.StarLevelNBackImage.sprite = defaultStar;
                extended_hud.StarLevelNBackImage.rectTransform.sizeDelta = new Vector2(21, 21);
            }
        }

        public static void OnBossHudConfigChanged(object s, EventArgs e) {
            BossHudConfigDirty = true;
        }

        // Valheim's HUD canvas is a ConstantPixelSize CanvasScaler whose scaleFactor is driven by
        // GuiScaler (= Min(Screen.width/1920, Screen.height/1080) * GuiScale). Canvas-local units
        // therefore equal pixels / scaleFactor, which is what localPosition/sizeDelta operate in.
        internal static float GetHudScaleFactor(EnemyHud hud) {
            Canvas canvas = hud != null ? hud.GetComponentInParent<Canvas>() : null;
            float sf = canvas != null && canvas.rootCanvas != null ? canvas.rootCanvas.scaleFactor : 0f;
            if (sf <= 0f) {
                // CanvasScaler may not have run its first Update yet (e.g. during EnemyHud.Awake).
                sf = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f) * PlatformPrefs.GetFloat("GuiScale", 1f);
            }
            return sf <= 0f ? 1f : sf;
        }

        // Ensures BossHudRoot carries the correct layout group for the current stacking mode and
        // applies the configured spacing + top buffer. Vertical = stacked rows, Horizontal = squished.
        internal static HorizontalOrVerticalLayoutGroup EnsureBossLayoutGroup(bool stack) {
            if (BossHudRoot == null) { return null; }
            HorizontalOrVerticalLayoutGroup group = BossHudRoot.GetComponent<HorizontalOrVerticalLayoutGroup>();
            bool wrongType = group == null
                || (stack && group is HorizontalLayoutGroup)
                || (!stack && group is VerticalLayoutGroup);
            if (wrongType) {
                if (group != null) { GameObject.DestroyImmediate(group); }
                group = stack
                    ? (HorizontalOrVerticalLayoutGroup)BossHudRoot.AddComponent<VerticalLayoutGroup>()
                    : BossHudRoot.AddComponent<HorizontalLayoutGroup>();
            }
            group.childAlignment = TextAnchor.UpperCenter;
            group.childForceExpandHeight = false;
            group.spacing = ValConfig.BossHealthbarSpacing.Value;
            group.padding = new RectOffset(0, 0, ValConfig.BossHudTopBuffer.Value, 0);
            if (stack) {
                // Stacked rows: each full-width bar centered, one per row.
                group.childControlWidth = false;
                group.childForceExpandWidth = false;
                group.childControlHeight = true;
            } else {
                // Squished row: divide the configured total width evenly between the bars so they
                // always fit within it (root rect width is set to that budget in StackBossHuds).
                group.childControlWidth = true;
                group.childForceExpandWidth = true;
                group.childControlHeight = false;
            }
            return group;
        }

        // Applies a target width (canvas units) to a boss hud's name, health bars and backgrounds.
        // Works for both the m_baseHudBoss template (sizes future clones) and already-shown clones.
        internal static void SetBossBarWidth(GameObject bossGui, float width) {
            if (bossGui == null) { return; }
            LayoutElement le = bossGui.GetComponent<LayoutElement>();
            if (le != null) {
                le.minWidth = width;
                le.preferredWidth = width;
            }
            if (bossGui.transform.Find("Name") is RectTransform nameRT) {
                nameRT.sizeDelta = new Vector2(width, 40f);
            }
            Transform health = bossGui.transform.Find("Health");
            if (health == null) { return; }
            SetBossHealthBar(health.Find("health_slow"), width);
            SetBossHealthBar(health.Find("health_fast"), width);
            if (health.Find("Background/bkg") is RectTransform bkgRT) {
                bkgRT.sizeDelta = new Vector2(width, 20f);
            }
            if (health.Find("Background/darken") is RectTransform darkRT) {
                darkRT.sizeDelta = new Vector2(width, 24f);
            }
        }

        private static void SetBossHealthBar(Transform healthBar, float width) {
            if (healthBar == null) { return; }
            if (healthBar.Find("bar") is RectTransform barRT) {
                barRT.sizeDelta = new Vector2(width, 20f);
                barRT.localPosition = new Vector2(width * -0.5f, 0f);
            }
            // GuiBar overrides the bar sizeDelta every LateUpdate from its captured m_width, so a live
            // bar only resizes via SetWidth; setting sizeDelta above keeps fresh clones correct.
            GuiBar gb = healthBar.GetComponent<GuiBar>();
            if (gb != null) { gb.SetWidth(width); }
        }

        // Runs from the EnemyHud.UpdateHuds postfix (main thread). Gated on boss count / config change
        // so widths are only rewritten when needed, not every frame.
        internal static void StackBossHuds(EnemyHud instance) {
            if (instance == null || BossHudRoot == null) { return; }

            int count = 0;
            foreach (KeyValuePair<Character, EnemyHud.HudData> kv in instance.m_huds) {
                EnemyHud.HudData hd = kv.Value;
                if (hd != null && hd.m_character != null && hd.m_character.IsBoss() && hd.m_gui != null && hd.m_gui.activeSelf) {
                    count++;
                }
            }

            if (BossHudConfigDirty == false && count == lastBossHudCount) { return; }
            BossHudConfigDirty = false;
            lastBossHudCount = count;

            bool stack = ValConfig.StackMultipleBossHealthbars.Value;
            EnsureBossLayoutGroup(stack);

            // The configured percentage is the TOTAL width budget for the boss HUD.
            float bossWidth = (Screen.width / GetHudScaleFactor(instance)) * ValConfig.BossHealthbarWidthPercent.Value;

            RectTransform rootRT = BossHudRoot.GetComponent<RectTransform>();
            float perBarWidth = bossWidth;
            if (stack == false) {
                // Give the layout group the configured width and split it evenly so the bars are
                // shrunk to all fit side-by-side within that budget.
                int bars = Mathf.Max(1, count);
                float spacing = ValConfig.BossHealthbarSpacing.Value;
                perBarWidth = Mathf.Max(1f, (bossWidth - (spacing * (bars - 1))) / bars);
                if (rootRT != null) { rootRT.sizeDelta = new Vector2(bossWidth, rootRT.sizeDelta.y); }
            } else if (rootRT != null) {
                // Stacked: the root is a point; each full-width row is centered around it.
                rootRT.sizeDelta = new Vector2(0f, rootRT.sizeDelta.y);
            }

            // Template controls how freshly-shown bosses are sized.
            if (instance.m_baseHudBoss != null) { SetBossBarWidth(instance.m_baseHudBoss, perBarWidth); }
            // Resize the bosses that are already on screen.
            foreach (KeyValuePair<Character, EnemyHud.HudData> kv in instance.m_huds) {
                EnemyHud.HudData hd = kv.Value;
                if (hd != null && hd.m_character != null && hd.m_character.IsBoss() && hd.m_gui != null) {
                    SetBossBarWidth(hd.m_gui, perBarWidth);
                }
            }
        }

        internal static void LoadAssets() {
            HealthText = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>("HealthText.prefab");
        }
    }
}
