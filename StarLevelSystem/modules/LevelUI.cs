using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    public class StarLevelHud
    {
        public bool IsBoss { get; set; }

        public int Level { get; set; } = 1;

        public List<string> DisplayedMods { get; set; } = new List<string>();

        public Dictionary<int, GameObject> Starlevel = new Dictionary<int, GameObject>();
        public Dictionary<int, Image> StarLevelFront = new Dictionary<int, Image>();
        public Dictionary<int, Image> StarLevelBack = new Dictionary<int, Image>();
        public GameObject StarLevelN { get; set; }
        public Image StarLevelNFrontImage { get; set; }
        public Image StarLevelNBackImage { get; set; }
        public Text StarLevelNText { get; set; }
        public EnemyHud.HudData hudlink { get; set;
        }
    }
    public static class LevelUI
    {
        public static Dictionary<uint, StarLevelHud> characterExtendedHuds = new Dictionary<uint, StarLevelHud>();
        private static GameObject star;

        public static void InvalidateCacheEntry(uint zdid)
        {
            if (characterExtendedHuds.ContainsKey(zdid)) {
                CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(zdid);
                
                cce.CreatureModifiers = CompositeLazyCache.GetCreatureModifiers(characterExtendedHuds[zdid].hudlink.m_character);
                cce.CreatureNameLocalizable = CreatureModifiers.BuildCreatureLocalizableName(characterExtendedHuds[zdid].hudlink.m_character, cce.CreatureModifiers);
                characterExtendedHuds[zdid].hudlink.m_name.text = Localization.instance.Localize(cce.CreatureNameLocalizable);
                if (cce == null || cce.CreatureNameLocalizable == null) { return; }
            }
        }

        public static void InvalidateCacheEntry(Character chara)
        {
            uint id = chara.GetZDOID().ID;
            InvalidateCacheEntry(id);
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.SetName))]
        public static class UpdateTamedName
        {
            public static void Postfix(Tameable __instance)
            {
                //Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance.m_character);
                //__instance.m_character.m_nview.GetZDO().Set(SLS_CHARNAME, CreatureModifiers.BuildCreatureLocalizableName(__instance.m_character, mods)); 
                LevelUI.InvalidateCacheEntry(__instance.m_character.GetZDOID().ID);
            }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.Awake))]
        public static class EnableLevelDisplay
        {
            public static void Postfix(EnemyHud __instance) {
                // Logger.LogDebug($"Updating Enemy Hud, expanding stars");
                // Need a patch to show the number of stars something is
                // Need to setup the 1-5 stars, and the 5-n stars
                star = __instance.m_baseHud.transform.Find("level_2/star").gameObject;

                // Destroys the extra star for level 3, so that we can just enable levels 2-6 to add their respective star
                // Object.Destroy(__instance.m_baseHud.transform.Find("level_3/star").gameObject);
                __instance.m_baseHud.transform.Find("level_3/star").gameObject.SetActive(false);

                // Levels 1-5 get their stars, then we also get the n* setup
                StarLevelHudDisplay(star, __instance.m_baseHud.transform, __instance.m_baseHudBoss.transform);
            }

            private static void StarLevelHudDisplay(GameObject star, Transform basehud, Transform bosshud)
            {
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

            private static void SetupStar(GameObject star, int level, Transform parent_t, bool boss = false)
            {
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

                switch (level)
                {
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
                        if (boss)
                        {
                            new_star_holder.transform.localPosition = new Vector3(x: 20, y: -6, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        }
                        else
                        {
                            new_star_holder.transform.localPosition = new Vector3(x: -9, y: 19, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        }
                        break;
                    case 5:
                        if (boss)
                        {
                            new_star_holder.transform.localPosition = new Vector3(x: -40, y: -6, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        }
                        else
                        {
                            new_star_holder.transform.localPosition = new Vector3(x: 7, y: 19, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        }
                        break;
                    case 6:
                        if (boss)
                        {
                            new_star_holder.transform.localPosition = new Vector3(x: 40, y: -6, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        }
                        else
                        {
                            new_star_holder.transform.localPosition = new Vector3(x: 23, y: 19, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        }
                        break;
                }
            }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))]
        public static class DisableVanillaStarsByDefault
        {
            public static void Postfix(EnemyHud __instance, Character c) {
                if (__instance == null || c == null) { return; }
                // non-bosses and players
                if (!c.IsBoss()) {
                    __instance.m_huds.TryGetValue(c, out var value);
                    if (value != null) {
                        if (value.m_level2 == null || value.m_level3 == null) {
                            return;
                        }
                        value.m_level2?.gameObject?.SetActive(false);
                        value.m_level3?.gameObject?.SetActive(false);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
        public static class DestroyEnemyHud {
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(EnemyHud.UpdateHuds))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchForward(true,
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(EnemyHud), nameof(EnemyHud.m_huds))),
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Callvirt),
                    new CodeMatch(OpCodes.Pop)
                    ).InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc_3), // Load the hud instance that is being manipulated
                    Transpilers.EmitDelegate(RemoveExtenedHudFromCache)
                    ).ThrowIfNotMatch("Unable to patch Enemy Hud removal update, levels will not be displayed properly.");

                return codeMatcher.Instructions();
            }

            public static void RemoveExtenedHudFromCache(Character char3) {
                characterExtendedHuds.Remove(char3.GetZDOID().ID);
            }
        }

        [HarmonyPatch(typeof(EnemyHud))]
        public static class SetupCreatureLevelDisplay {
            // [HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(EnemyHud.UpdateHuds))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchForward(true,
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldfld),
                    new CodeMatch(OpCodes.Ldc_I4_1),
                    new CodeMatch(OpCodes.Callvirt),
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldfld),
                    new CodeMatch(OpCodes.Callvirt),
                    new CodeMatch(OpCodes.Stloc_S)
                    ).InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc_S, (byte)6), // Load the hud instance that is being manipulated
                    Transpilers.EmitDelegate(UpdateHudforAllLevels)
                    ).RemoveInstructions(23).ThrowIfNotMatch("Unable to patch Enemy Hud update, levels will not be displayed properly.");

                return codeMatcher.Instructions();
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        public static class DisplayCreatureNameChanges {
            public static bool Prefix(Character __instance, ref string __result) {
                CharacterCacheEntry cce = CompositeLazyCache.GetCacheEntry(__instance);
                if (cce == null || cce.CreatureNameLocalizable == null) { return true; }
                __result = Localization.instance.Localize(cce.CreatureNameLocalizable);
                return false;
            }
        }

        public static void UpdateHudforAllLevels(EnemyHud.HudData ehud) {
            if (ehud == null || ehud.m_character == null) return;
            if (ehud.m_character.IsPlayer()) return;
            int level = ehud.m_character.GetLevel();
            if (level <= 1) return;
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
                    characterExtendedHuds.Remove(czid);
                    //CompositeLazyCache.ClearCachedCreature(ehud.m_character);
                    return;
                }

                // Mod count check here could be replaced by a Z request to refresh the cache by the controlling player
                List<string> cmods = mods.Keys.ToList();
                if (ehud.m_character.GetLevel() != extended_hud.Level || cmods.CompareListContents(mods.Keys.ToList()) == false) {
                    Logger.LogDebug($"UI Cache for {czid} outdated (level {ehud.m_character.GetLevel()}-{extended_hud.Level} or mods {extended_hud.DisplayedMods.Count}-{mods.Count}), updating cache.");
                    characterExtendedHuds.Remove(czid);
                    //CompositeLazyCache.ClearCachedCreature(ehud.m_character);
                    return;
                }
            } else {
                extended_hud.hudlink = ehud;
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
                    if (cmd.StarVisual != null && CreatureModifiersData.LoadedModifierSprites.ContainsKey(cmd.StarVisual)) {
                        Sprite starsprite = CreatureModifiersData.LoadedModifierSprites[cmd.StarVisual];
                        starReplacements.Add(star, starsprite);
                    }
                    star++;
                }

                //Logger.LogDebug($"Determined replacement stars {string.Join(",", starReplacements.Keys)}");

                extended_hud.IsBoss = ehud.m_character.IsBoss();
                int star_index = 2;
                while (star_index < 7)
                {
                    //Logger.LogDebug($"Assigning star level {star_index}");
                    extended_hud.Starlevel.Add(star_index, ehud.m_gui.transform.Find($"SLS_level_{star_index}").gameObject);
                    extended_hud.StarLevelBack.Add(star_index, ehud.m_gui.transform.Find($"SLS_level_{star_index}/star(Clone)").gameObject.GetComponent<Image>());
                    extended_hud.StarLevelFront.Add(star_index, ehud.m_gui.transform.Find($"SLS_level_{star_index}/star(Clone)/star (1)").gameObject.GetComponent<Image>());
                    if (starReplacements.ContainsKey(star_index)) {
                        //Logger.LogDebug($"Updating Icon to modifier icon {star_index} Front: {string.Join(",", extended_hud.starlevel_front.Keys)} Back: {string.Join(",", extended_hud.starlevel_back.Keys)}");
                        if (extended_hud.StarLevelFront[star_index] != null) {
                            extended_hud.StarLevelFront[star_index].sprite = starReplacements[star_index];
                            extended_hud.StarLevelFront[star_index].rectTransform.sizeDelta = new Vector2(17, 17);
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
            // Star level display starts at 2
            // Enable static star levels
            //Logger.LogInfo("Setting star levels active");

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
    }
}
