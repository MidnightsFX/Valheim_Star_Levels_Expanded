using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.LevelSystem;
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

        // How long to wait before retrying a HUD whose creature cache isn't ready yet. Creatures whose level
        // hasn't resolved are deliberately not cached (see CompositeLazyCache.GetAndSetLocalCache), so without
        // a backoff the miss would drive a full cache rebuild every single frame for the whole time a
        // non-owner waits on ZDO replication.
        private const float NotReadyRetryInterval = 0.5f;

        // Evolving progress bar under the health bar, see UpdateEvolveBar.
        private const string EvolveBarName = "SLS_evolve_bar";
        private const string EvolvingKey = nameof(CreatureModifiersData.ModifierNames.Evolving);
        private static readonly int EvolveKillsKey = SLS_EVOLVE.GetStableHashCode();
        // Kills and level are watched every frame; this re-reads the kill requirement and level cap now and then, so a
        // Modifiers.yaml reload or walking into a biome with another cap still reaches a bar whose kills haven't moved.
        private const float EvolveRefreshInterval = 2f;
        // How far the star row drops while the bar shows. The stars already touch the health bar, so the bar would
        // otherwise be drawn behind them.
        private const float EvolveStarShift = 6f;
        private const float BossEvolveStarShift = 3f;

        // Above five stars the numbered star stands for the level alone: a separator and one icon per modifier follow it.
        private const string HighLevelSeparatorName = "SLS_level_n_sep";
        private const string HighLevelIconName = "SLS_level_n_mod_";
        private const int HighLevelIconSlots = 5;
        private const float HighLevelIconStart = 29f;
        private const float HighLevelIconSpacing = 16f;
        private const float HighLevelIconSpacingBoss = 20f;

        // Template positions of the star holders. Huds are moved from these rather than from where they are, so a hud
        // rebuilt over a gui that was already moved (a display style change rebuilds them all) lands in the same place.
        private static readonly Dictionary<int, Vector3> StarPositions = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, Vector3> BossStarPositions = new Dictionary<int, Vector3>();
        private static Vector3 StarNPosition;
        private static Vector3 BossStarNPosition;

        public class StarLevelHud {
            public bool IsBoss { get; set; }
            public string CreatureNameLocalized { get; set; }
            // False while CreatureNameLocalized is only the fallback prefab name, because the creature had
            // no CharacterCacheEntry when the hud was built. The staleness guard in UpdateHudForAllLevels
            // keeps retrying until this flips, otherwise a hud created inside the setup window latches the
            // unresolved name forever - the mod list and level it compares against are already correct.
            public bool NameResolved { get; set; }
            public int Level { get; set; } = 1;

            public List<string> DisplayedMods { get; set; } = new List<string>();

            // Last values actually written to the hud, so per-frame updates can skip the TMP text
            // assignment (which re-runs layout) and the star SetActive calls when nothing changed.
            public int LastHealthShown { get; set; } = -1;
            public int LastMaxHealthShown { get; set; } = -1;
            public int DisplayedStarLevel { get; set; } = 0;

            // Raw SLS_MODSV2 ZDO string the DisplayedMods/CreatureModifiers were last built from.
            // Used to skip the YAML deserialize when nothing has changed - this runs per visible
            // creature per frame off EnemyHud.UpdateHuds.
            public string ModifierSource { get; set; }
            public Dictionary<string, ModifierType> CachedModifiers { get; set; }

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

            // Time.time after which an unresolved creature may rebuild its cache again. Creatures whose
            // level/modifiers have not replicated yet fail the staleness check every frame; without this
            // they would run a full GetAndSetLocalCache (biome lookup, colorization, damage tables) per frame.
            public float NextResolveAttempt {
                get; set;
            }

            // The numbered star's modifier row (children of SLS_level_n) and how many of its icons are in use.
            public List<GameObject> HighLevelIcons = new List<GameObject>();
            public List<Image> HighLevelIconFront = new List<Image>();
            public List<Image> HighLevelIconBack = new List<Image>();
            public GameObject HighLevelSeparator { get; set; }
            public int HighLevelIconCount { get; set; }

            public bool HasEvolving { get; set; }
            public ModifierType EvolvingType { get; set; }
            public GameObject EvolveBar { get; set; }
            public RectTransform EvolveFill { get; set; }
            public bool EvolveBarShown { get; set; }
            // Last kills and level the fill was drawn from; -1 forces a redraw.
            public int LastEvolveKills { get; set; } = -1;
            public int LastEvolveLevel { get; set; } = -1;
            public float NextEvolveRefresh { get; set; }
        }

        // Marks the boss hud layout as needing re-application; the actual Unity work is done
        // on the main thread inside StackBossHuds (called from the EnemyHud.UpdateHuds postfix).
        internal static bool BossHudConfigDirty = true;
        private static int lastBossHudCount = -1;

        // Keyed by the full ZDOID (not the bare uint ZDOID.ID) so creatures created by different
        // peers with the same per-peer ID counter value don't collide. See CompositeLazyCache.
        public static Dictionary<ZDOID, StarLevelHud> characterExtendedHuds = new Dictionary<ZDOID, StarLevelHud>(ZDOIDComparer.Instance);
        private static GameObject HealthText;
        static Sprite defaultStar;

        internal static GameObject BossHudRoot;

        internal static void SetDefaultStar() {
            defaultStar = PrefabManager.Cache.GetPrefab<Sprite>("craft_icon");
        }

        internal static void RemoveExtendedHudFromCache(ZDOID id) {
            // TryGetValue + Remove is two hash lookups where ContainsKey + indexer + Remove was
            // three; net48 has no Remove(key, out value) overload, so two is the floor here.
            if (characterExtendedHuds.TryGetValue(id, out StarLevelHud removed) == false) { return; }
            //Logger.LogDebug($"Removing extended hud from cache for {id}");
            if (removed.HealthText != null) {
                GameObject.Destroy(removed.HealthText.gameObject);
            }
            // CurrentBossHuds is otherwise append-only and grows for the whole session.
            if (removed.IsBoss) { CurrentBossHuds.Remove(removed); }
            characterExtendedHuds.Remove(id);
        }

        // Reads the creature's modifiers, reusing the previously deserialized dictionary when the backing
        // ZDO string is unchanged. GetCreatureModifiers runs a YAML deserialize on every call, and this is
        // on the per-frame EnemyHud path, so the fast path here matters.
        private static Dictionary<string, ModifierType> GetModifiersCached(StarLevelHud hud, Character chara) {
            if (chara == null || chara.m_nview == null || chara.m_nview.GetZDO() == null) { return null; }
            string source = chara.m_nview.GetZDO().GetString(SLS_MODSV2, null);
            if (hud != null && hud.CachedModifiers != null && hud.ModifierSource == source) {
                return hud.CachedModifiers;
            }
            Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(chara);
            if (hud != null) {
                hud.ModifierSource = source;
                hud.CachedModifiers = mods;
            }
            return mods;
        }

        // Allocation-free replacement for Keys.ToList() + the double-Except CompareListContents on the
        // per-frame hud path: same set-equality answer without building any intermediate collections.
        private static bool ModsMatchDisplayed(Dictionary<string, ModifierType> mods, List<string> displayed) {
            if (displayed == null) { return mods.Count == 0; }
            if (mods.Count != displayed.Count) { return false; }
            for (int i = 0; i < displayed.Count; i++) {
                if (mods.ContainsKey(displayed[i]) == false) { return false; }
            }
            return true;
        }

        public static void InvalidateCacheEntry(Character chara) {
            if (chara == null || chara.GetZDOID() == ZDOID.None) { return; }
            ZDOID id = chara.GetZDOID();
            if (characterExtendedHuds.ContainsKey(id) == false) { return; }

            StarLevelHud extendedHUD = characterExtendedHuds[id];
            CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(id);
            if (cce == null) {
                cce = CompositeLazyCache.GetAndSetLocalCache(chara);
                // A creature whose level hasn't resolved yet is never stored in the session cache, so this
                // stays a miss until the owner's roll replicates. Back off instead of retrying every frame.
                if (cce == null || CompositeLazyCache.GetCacheEntry(id) == null) {
                    extendedHUD.NextResolveAttempt = Time.time + NotReadyRetryInterval;
                    return;
                }
                // A fresh build, which is how the staleness check in UpdateHudForAllLevels gets here (it clears the entry
                // first), has none of the modifiers' changes, and this peer's hits read the creature's resists from it.
                CreatureModifiers.SetupRebuiltEntry(chara, cce);
            }

            Dictionary<string, ModifierType> mods = GetModifiersCached(extendedHUD, extendedHUD.HudLink.m_character);
            cce.CreatureModifiers = mods;
            cce.CreatureNameLocalizable = CreatureModifiers.BuildCreatureLocalizableName(extendedHUD.HudLink.m_character, mods);

            // Must track the same value the outdated-check below compares against (and that the star display
            // renders from), otherwise the check can never converge and re-invalidates every frame.
            extendedHUD.Level = chara.GetLevel();
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
            StarNPosition = star_7.transform.localPosition;
            star7.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
            GameObject s7Name = new GameObject(name: "level_n_name");
            GameObject star7Name = UnityEngine.Object.Instantiate(s7Name, star_7.transform);
            star7Name.transform.SetParent(star_7.transform);
            star7Name.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
            // Right aligned and ending just left of the star, so the modifier row after the star never depends on how
            // many digits the level has.
            GUIManager.Instance.CreateText(
                text: "999",
                parent: star7Name.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(-188f, -13f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 14,
                color: GUIManager.Instance.ValheimYellow,
                outline: true,
                outlineColor: Color.black,
                width: 350f,
                height: 40f,
                addContentSizeFitter: false).GetComponent<Text>().alignment = TextAnchor.UpperRight;
            SetupHighLevelModifierRow(star, star_7.transform, false);
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
            BossStarNPosition = star_7_boss.transform.localPosition;
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
                position: new Vector2(-188f, -7f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 23,
                color: GUIManager.Instance.ValheimYellow,
                outline: true,
                outlineColor: Color.black,
                width: 350f,
                height: 40f,
                addContentSizeFitter: false).GetComponent<Text>().alignment = TextAnchor.UpperRight;
            SetupHighLevelModifierRow(star, star_7_boss.transform, true);
            star_7_boss.SetActive(false);

            SetupEvolveBar(baseHud.Find("Health"), false);
            SetupEvolveBar(bossHud.Find("Health"), true);
        }

        // A separator and a row of icon holders after the numbered star, filled in per hud by UpdateHudModifiers. They
        // are children of SLS_level_n, so they show and hide with it.
        private static void SetupHighLevelModifierRow(GameObject star, Transform nHolder, bool boss) {
            GameObject separator = new GameObject(HighLevelSeparatorName, typeof(RectTransform), typeof(Image));
            RectTransform separatorRT = (RectTransform)separator.transform;
            separatorRT.SetParent(nHolder, false);
            separatorRT.sizeDelta = new Vector2(2f, boss ? 18f : 14f);
            separatorRT.localPosition = new Vector3(15f, 0f, 0f);
            Image separatorImage = separator.GetComponent<Image>();
            separatorImage.color = new Color(StarColor.r, StarColor.g, StarColor.b, 0.6f);
            separatorImage.raycastTarget = false;
            separator.SetActive(false);

            float spacing = boss ? HighLevelIconSpacingBoss : HighLevelIconSpacing;
            for (int i = 0; i < HighLevelIconSlots; i++) {
                GameObject holder = new GameObject(name: $"{HighLevelIconName}{i}");
                holder.transform.SetParent(nHolder, false);
                GameObject icon = UnityEngine.Object.Instantiate(star, holder.transform);
                icon.transform.localPosition = Vector3.zero;
                holder.transform.localPosition = new Vector3(HighLevelIconStart + (i * spacing), 0f, 0f);
                holder.SetActive(false);
            }
        }

        // A thin bar hanging under the health bar. Its fill is anchored to the left edge, so drawing progress is a single
        // anchorMax write (UpdateEvolveBar).
        private static void SetupEvolveBar(Transform health, bool boss) {
            if (health == null) { return; }
            GameObject bar = new GameObject(EvolveBarName, typeof(RectTransform), typeof(Image));
            bar.layer = health.gameObject.layer;
            RectTransform barRT = (RectTransform)bar.transform;
            barRT.SetParent(health, false);
            barRT.pivot = new Vector2(0.5f, 1f);
            if (boss) {
                // The boss Health rect keeps its prefab size while SetBossBarWidth sizes the bars, so the width is set
                // there too. The visible bar is 20 tall inside the 15 tall rect, which is why this sits lower.
                barRT.anchorMin = barRT.anchorMax = new Vector2(0.5f, 0f);
                barRT.sizeDelta = new Vector2(((RectTransform)health).sizeDelta.x, 4f);
                barRT.anchoredPosition = new Vector2(0f, -3.5f);
            } else {
                // Stretched along Health, so the healthbar scale settings resize it with the bar.
                barRT.anchorMin = new Vector2(0f, 0f);
                barRT.anchorMax = new Vector2(1f, 0f);
                barRT.sizeDelta = new Vector2(0f, 3f);
                barRT.anchoredPosition = new Vector2(0f, -1f);
            }
            Image background = bar.GetComponent<Image>();
            background.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            background.raycastTarget = false;

            GameObject fill = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fill.layer = bar.layer;
            RectTransform fillRT = (RectTransform)fill.transform;
            fillRT.SetParent(barRT, false);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(0f, 1f);
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = StarColor;
            fillImage.raycastTarget = false;
            bar.SetActive(false);
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
            (boss ? BossStarPositions : StarPositions)[level] = new_star_holder.transform.localPosition;
        }

        // The health numbers come straight off the Character, so this is deliberately independent of the
        // level/modifier cache: it has to keep running for creatures SLS has not resolved yet, otherwise the
        // instantiated HealthText keeps showing the placeholder text baked into the prefab.
        // Also handles creating/hiding the text so the config toggle applies to huds that already exist.
        private static void UpdateHealthText(StarLevelHud hud, Character chara) {
            if (hud == null || chara == null) { return; }
            if (hud.HudLink == null || hud.HudLink.m_gui == null) { return; }

            if (ValConfig.EnableEnemyHealthbarNumberDisplay.Value == false) {
                if (hud.HealthText != null) { hud.HealthText.gameObject.SetActive(false); }
                return;
            }

            if (hud.HealthText == null) {
                Transform healthTForm = hud.HudLink.m_gui.transform.Find("Health");
                if (healthTForm == null) { return; }
                if (HealthText == null) { LoadAssets(); }
                GameObject HealthTextHolder = GameObject.Instantiate(HealthText, healthTForm);
                hud.HealthText = HealthTextHolder.GetComponent<TextMeshProUGUI>();
                if (hud.HealthText == null) { return; }
                // Use a slightly diminishing scale as otherwise things get over scaled easily
                hud.HealthText.fontSize = 10 * (ValConfig.EnemyHealthbarScalarY.Value * ValConfig.HealthDisplayFontSizeAdjustment.Value);

                if (ValConfig.UseCustomHealthFont.Value == false) {
                    hud.HealthText.font = hud.HudLink.m_name.font;
                }
                // Health text boss fixes? resize?
                if (hud.IsBoss) {
                    RectTransform hthRT = HealthTextHolder.transform as RectTransform;
                    hthRT.localPosition = new Vector3(0, 4, 0);
                    hud.HealthText.fontSize = 20f;
                    hud.HealthText.characterSpacing = 12f;
                }
            }

            if (hud.HealthText.gameObject.activeSelf == false) { hud.HealthText.gameObject.SetActive(true); }
            // Skip the string build + TMP assignment (which re-runs text layout) when the displayed
            // values have not changed - this runs per visible creature per frame.
            int health = Mathf.RoundToInt(chara.GetHealth());
            int maxHealth = Mathf.RoundToInt(chara.GetMaxHealth());
            if (hud.LastHealthShown != health || hud.LastMaxHealthShown != maxHealth) {
                hud.LastHealthShown = health;
                hud.LastMaxHealthShown = maxHealth;
                hud.HealthText.text = $"{health:N0}/{maxHealth:N0}";
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
            // GetCreatureModifiers returns null when the ZDO value is missing or fails to deserialize;
            // treat that as "no modifiers" so the staleness comparison below can't NRE inside the
            // transpiled UpdateHuds (which would abort the hud update for every other creature too).
            // Read through the hud's cache so the underlying YAML deserialize only runs when the backing
            // ZDO string actually changed - this method runs per visible creature per frame.
            characterExtendedHuds.TryGetValue(czid, out StarLevelHud known_hud);
            Dictionary<string, ModifierType> mods = GetModifiersCached(known_hud, ehud.m_character);
            if (mods == null) { mods = new Dictionary<string, ModifierType>(); }
            StarLevelHud extended_hud;
            if (characterExtendedHuds.ContainsKey(czid)) {
                // Logger.LogInfo($"Hud already exists for {czoid}, loading");
                // Cached items which have been destroyed need to be removed and re-attached
                extended_hud = known_hud;
                if (extended_hud == null || extended_hud.Starlevel.ContainsKey(3) && extended_hud.Starlevel[3] == null) {
                    Logger.LogDebug($"UI Cache Invalid for {czid}, removing.");
                    RemoveExtendedHudFromCache(czid);
                    //CompositeLazyCache.ClearCachedCreature(ehud.m_character);
                    return;
                }

                // Kept above the staleness guard below: the health numbers do not depend on the level or
                // modifier cache, so a creature SLS has not resolved yet must still show real numbers.
                UpdateHealthText(extended_hud, ehud.m_character);

                CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(czid);
                int levelCheck = ehud.m_character.GetLevel();
                // NameResolved is part of this check because the mod list and level can both already match
                // while the name is still the unresolved fallback - without it the hud goes quiet and keeps
                // rendering the prefab name next to a modifier icon.
                if (ModsMatchDisplayed(mods, extended_hud.DisplayedMods) == false || cce == null || levelCheck != extended_hud.Level || extended_hud.NameResolved == false) {
                    // A creature whose level/modifiers have not replicated yet fails this check every frame.
                    // Rebuilding the cache is expensive, so back off between attempts instead of thrashing.
                    if (Time.time < extended_hud.NextResolveAttempt) { return; }
                    extended_hud.NextResolveAttempt = Time.time + NotReadyRetryInterval;

                    // Interpolating this string is not free on the per-frame hud path, so only build it
                    // when debug output is actually going somewhere.
                    if (ValConfig.EnableDebugMode.Value) {
                        Logger.LogDebug($"UI Cache for {czid} outdated (level {levelCheck}-{extended_hud.Level} or mods {extended_hud.DisplayedMods.Count}-{mods.Count} or name {extended_hud.HudLink.m_name.text}-{extended_hud.CreatureNameLocalized}), updating cache.");
                    }
                    CompositeLazyCache.ClearCachedCreature(ehud.m_character);
                    InvalidateCacheEntry(ehud.m_character);

                    //Colorization.ApplyColorizationWithoutLevelEffects();
                    // Re set up the character, to ensure it gets updated visual effects, and sizing

                    return;
                }

                // Only assign when changed: a TMP text write re-runs layout even for an identical string.
                if (extended_hud.HudLink.m_name.text != extended_hud.CreatureNameLocalized) {
                    extended_hud.HudLink.m_name.text = extended_hud.CreatureNameLocalized;
                }
            } else {
                extended_hud = new StarLevelHud {
                    Level = level,
                    HudLink = ehud
                };
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

                // Setup the health text if enabled. Assigning the value here (rather than waiting for the
                // next frame's update branch) stops the prefab's placeholder text rendering on frame one.
                UpdateHealthText(extended_hud, ehud.m_character);

                UpdateHudModifiers(czid, extended_hud, mods);
                // Need to find and add the N level text for updating here
                characterExtendedHuds.Add(czid, extended_hud);
            }
            // Show the creatures health value

            // Ahead of the early out below, which only cares about the level: kills change the bar without changing it.
            UpdateEvolveBar(extended_hud, ehud.m_character);

            // Set star level, this can change if the characters level gets modified. Only touch the
            // star GameObjects when the displayed level actually changed - SetActive is a
            // managed->native call and this method runs per visible creature per frame.
            if (extended_hud.DisplayedStarLevel == level) { return; }
            extended_hud.DisplayedStarLevel = level;
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

            // InvalidateCacheEntry passes the raw GetCreatureModifiers result, which is null when the ZDO
            // value is missing or fails to deserialize. Normalize before anything dereferences it.
            if (mods == null) { mods = new Dictionary<string, ModifierType>(); }

            CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(extended_hud.HudLink.m_character);
            if (cce != null) {
                if (cce.CreatureModifiers == null || cce.CreatureModifiers.Keys.ToList().CompareListContents(mods.Keys.ToList()) == false) {
                    //Logger.LogDebug($"Creature cache is outdated for {zdoid}-{cce.RefCreatureName}-{cce.Level}");
                    CompositeLazyCache.ClearCachedCreature(extended_hud.HudLink.m_character);
                    cce = CompositeLazyCache.GetAndSetLocalCache(extended_hud.HudLink.m_character);
                    CreatureModifiers.SetupRebuiltEntry(extended_hud.HudLink.m_character, cce);
                }
            }
            // Always resolve a name here. Leaving CreatureNameLocalized null when the cache entry is not
            // ready yet strands the hud: DisplayedMods and the modifier icons below are written regardless,
            // so the staleness guard in UpdateHudForAllLevels starts passing on the next frame and assigns
            // that null straight into m_name.text - a creature showing its modifier icon with a blank name.
            Character hudChara = extended_hud.HudLink.m_character;
            if (hudChara != null && hudChara.m_nview != null && hudChara.m_nview.GetZDO() != null) {
                string nameLocalizable = cce?.CreatureNameLocalizable;
                extended_hud.NameResolved = nameLocalizable != null;
                if (nameLocalizable == null) {
                    // Fall back to the plain prefab name until the cache resolves. NameResolved stays false
                    // so the staleness guard keeps retrying rather than latching this.
                    nameLocalizable = hudChara.m_name;
                    if (ValConfig.EnableDebugMode.Value) {
                        Logger.LogDebug($"No cache entry for {zdoid} while building its hud name, falling back to '{hudChara.m_name}'. Modifiers: {string.Join(", ", mods.Keys)}");
                    }
                }
                extended_hud.CreatureNameLocalized = hudChara.m_nview.GetZDO().GetString(ZDOVars.s_tamedName, Localization.instance.Localize(nameLocalizable));
                extended_hud.HudLink.m_name.text = extended_hud.CreatureNameLocalized;
            }

            Dictionary<int, Sprite> starReplacements = new Dictionary<int, Sprite>();
            int star = 2;
            // Logger.LogDebug($"Building sprite list");
            extended_hud.DisplayedMods = mods.Keys.ToList();
            // When the display style is None, leave starReplacements empty so the slots fall through to the default stars below.
            if (CreatureModifiersData.SelectedModifierDisplayStyle != ModifierDisplayStyle.None) {
                foreach (KeyValuePair<string, ModifierType> entry in mods) {
                    if (entry.Key == CreatureModifiers.NoMods) { continue; }
                    //Logger.LogDebug($"Checking modifier {entry.Key} of type {entry.Value}");
                    // A persisted modifier with no definition (removed/renamed modifier, or one another peer
                    // registered through the API) must not throw out of the transpiled EnemyHud.UpdateHuds -
                    // that would abort the hud update for every other creature this frame.
                    if (CreatureModifiersData.ModifierDefinitions.TryGetValue(entry.Key, out CreatureModifierDefinition cmd) == false || cmd == null) {
                        Logger.LogWarning($"Modifier {entry.Key} has no definition, skipping its hud icon.");
                        continue;
                    }
                    // A null or destroyed sprite (an API mod's sprite can be destroyed after it was cached) would
                    // draw as a white box; leaving the slot out falls through to the default star below.
                    if (cmd.StarVisual != null && CreatureModifiersData.LoadedModifierSprites.TryGetValue(cmd.StarVisual, out Sprite starSprite) && starSprite != null) {
                        starReplacements.Add(star, starSprite);
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
                // No replacement resets the slot to the default star, which is needed for when modifiers are removed and the cache is still valid
                starReplacements.TryGetValue(star_index, out Sprite slotSprite);
                ApplyStarSprite(extended_hud.StarLevelFront[star_index], extended_hud.StarLevelBack[star_index], slotSprite);
                star_index++;
            }

            //Logger.LogDebug($"Assigning star level N");
            Transform guiT = extended_hud.HudLink.m_gui.transform;
            extended_hud.StarLevelN = guiT.Find("SLS_level_n").gameObject;
            extended_hud.StarLevelNBackImage = guiT.Find("SLS_level_n/star(Clone)").gameObject.GetComponent<Image>();
            extended_hud.StarLevelNFrontImage = guiT.Find("SLS_level_n/star(Clone)/star (1)").gameObject.GetComponent<Image>();
            extended_hud.StarLevelNText = guiT.Find("SLS_level_n/level_n_name(Clone)/Text").gameObject.GetComponent<Text>();
            // The numbered star is the level alone; the modifiers get the icon row after it.
            extended_hud.StarLevelNFrontImage.sprite = defaultStar;
            extended_hud.StarLevelNFrontImage.rectTransform.sizeDelta = new Vector2(17, 17);
            extended_hud.StarLevelNBackImage.sprite = defaultStar;
            extended_hud.StarLevelNBackImage.rectTransform.sizeDelta = new Vector2(21, 21);

            if (extended_hud.HighLevelIcons.Count == 0) {
                for (int i = 0; i < HighLevelIconSlots; i++) {
                    Transform icon = guiT.Find($"SLS_level_n/{HighLevelIconName}{i}");
                    if (icon == null) { break; }
                    extended_hud.HighLevelIcons.Add(icon.gameObject);
                    extended_hud.HighLevelIconBack.Add(icon.Find("star(Clone)")?.GetComponent<Image>());
                    extended_hud.HighLevelIconFront.Add(icon.Find("star(Clone)/star (1)")?.GetComponent<Image>());
                }
                extended_hud.HighLevelSeparator = guiT.Find($"SLS_level_n/{HighLevelSeparatorName}")?.gameObject;
            }
            // Walked by slot, so the icons keep the order the stars use. Modifiers without a sprite are left out here rather
            // than drawn as a plain star, which would read as another level star.
            int iconCount = 0;
            for (int slot = 2; slot < star && iconCount < extended_hud.HighLevelIcons.Count; slot++) {
                if (starReplacements.TryGetValue(slot, out Sprite iconSprite) == false) { continue; }
                ApplyStarSprite(extended_hud.HighLevelIconFront[iconCount], extended_hud.HighLevelIconBack[iconCount], iconSprite);
                iconCount++;
            }
            for (int i = 0; i < extended_hud.HighLevelIcons.Count; i++) {
                extended_hud.HighLevelIcons[i].SetActive(i < iconCount);
            }
            if (extended_hud.HighLevelSeparator != null) { extended_hud.HighLevelSeparator.SetActive(iconCount > 0); }
            extended_hud.HighLevelIconCount = iconCount;

            extended_hud.HasEvolving = mods.TryGetValue(EvolvingKey, out ModifierType evolvingType);
            extended_hud.EvolvingType = evolvingType;
            extended_hud.LastEvolveKills = -1;
            if (extended_hud.EvolveBar == null) {
                Transform bar = guiT.Find($"Health/{EvolveBarName}");
                if (bar != null) {
                    extended_hud.EvolveBar = bar.gameObject;
                    extended_hud.EvolveFill = bar.Find("fill") as RectTransform;
                    // A hud rebuilt over an existing gui starts from what that gui shows.
                    extended_hud.EvolveBarShown = bar.gameObject.activeSelf;
                }
            }
            PositionStarRow(extended_hud);
        }

        // A modifier's sprite, or the plain star when modSprite is null.
        private static void ApplyStarSprite(Image front, Image back, Sprite modSprite) {
            bool modifier = modSprite != null;
            if (front != null) {
                front.sprite = modifier ? modSprite : defaultStar;
                front.rectTransform.sizeDelta = modifier ? new Vector2(17, 17) : new Vector2(14, 14);
                front.color = modifier ? Color.white : StarColor;
            }
            if (back != null) {
                back.sprite = modifier ? modSprite : defaultStar;
                back.rectTransform.sizeDelta = modifier ? new Vector2(21, 21) : new Vector2(16, 16);
            }
        }

        // Puts the star holders back at their template positions, dropped below the evolve bar while it shows. On the boss
        // hud, whose stars spread out from the middle, the numbered star is pulled left by half its icon row to stay centered.
        private static void PositionStarRow(StarLevelHud hud) {
            Vector3 shift = hud.EvolveBarShown ? new Vector3(0f, hud.IsBoss ? -BossEvolveStarShift : -EvolveStarShift, 0f) : Vector3.zero;
            Dictionary<int, Vector3> positions = hud.IsBoss ? BossStarPositions : StarPositions;
            foreach (KeyValuePair<int, GameObject> slot in hud.Starlevel) {
                if (slot.Value != null && positions.TryGetValue(slot.Key, out Vector3 position)) {
                    slot.Value.transform.localPosition = position + shift;
                }
            }
            if (hud.StarLevelN != null) {
                Vector3 nPosition = hud.IsBoss ? BossStarNPosition : StarNPosition;
                if (hud.IsBoss) { nPosition.x -= hud.HighLevelIconCount * HighLevelIconSpacingBoss * 0.5f; }
                hud.StarLevelN.transform.localPosition = nPosition + shift;
            }
        }

        // Runs per visible creature per frame, so it only redraws when the kills or level move (or on the slow refresh).
        // The kill count is the ZDO value the killer's owner writes in Evolve.CountKill, and the requirement and cap
        // below are the ones it uses, so every peer draws the same bar.
        private static void UpdateEvolveBar(StarLevelHud hud, Character chara) {
            if (hud.EvolveBar == null || hud.EvolveFill == null || chara == null) { return; }
            bool show = hud.HasEvolving && ValConfig.EnableEvolvingProgressBar.Value;
            if (show != hud.EvolveBarShown) {
                hud.EvolveBarShown = show;
                hud.EvolveBar.SetActive(show);
                hud.LastEvolveKills = -1;
                PositionStarRow(hud);
            }
            if (show == false) { return; }

            ZDO zdo = chara.m_nview != null ? chara.m_nview.GetZDO() : null;
            if (zdo == null) { return; }
            int kills = zdo.GetInt(EvolveKillsKey, 0);
            int level = chara.GetLevel();
            if (kills == hud.LastEvolveKills && level == hud.LastEvolveLevel && Time.time < hud.NextEvolveRefresh) { return; }
            hud.LastEvolveKills = kills;
            hud.LastEvolveLevel = level;
            hud.NextEvolveRefresh = Time.time + EvolveRefreshInterval;

            // Full once it can't evolve any further.
            float progress = 1f;
            if (IsAtEvolveCap(chara, level) == false) {
                CreatureModConfig cmcfg = CreatureModifierOverrides.GetEffectiveConfig(chara, EvolvingKey, hud.EvolvingType);
                int levelupReq = Mathf.RoundToInt(cmcfg.BasePower + (cmcfg.PerlevelPower * level));
                if (levelupReq > 0) { progress = Mathf.Clamp01((float)kills / levelupReq); }
            }
            hud.EvolveFill.anchorMax = new Vector2(progress, 1f);
        }

        // Same gate and bound Evolve.CountKill stops counting kills at.
        private static bool IsAtEvolveCap(Character chara, int level) {
            if (LevelSelection.OverLevelRerollEnabled(chara) == false) { return false; }
            LevelSelection.SelectCreatureBiomeSettings(chara.gameObject, out _, out CreatureSpecificSetting creatureSettings, out BiomeSpecificSetting biomeSettings, out Heightmap.Biome biome);
            return level >= LevelSelection.GetMaxCreatureLevel(chara, creatureSettings, biomeSettings, biome);
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
            if (health.Find(EvolveBarName) is RectTransform evolveRT) {
                evolveRT.sizeDelta = new Vector2(width, evolveRT.sizeDelta.y);
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

            // Cheap out for the common case: no boss huds tracked and none displayed last pass.
            // Without this the m_huds walk below (virtual IsBoss calls per entry) ran on every frame
            // of ordinary combat just to recount zero.
            if (BossHudConfigDirty == false && lastBossHudCount == 0 && CurrentBossHuds.Count == 0) { return; }

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
