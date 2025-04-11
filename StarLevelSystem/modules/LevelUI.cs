using HarmonyLib;
using Jotunn.Managers;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;

namespace StarLevelSystem.modules
{
    public class StarLevelHud
    {
        public bool isBoss { get; set; }
        public GameObject starlevel2 { get; set; }
        public GameObject starlevel3 { get; set; }
        public GameObject starlevel4 {  get; set; }
        public GameObject starlevel5 { get; set; }
        public GameObject starlevel6 { get; set; }
        public GameObject starlevel_N { get; set; }
        public Text starlevel_N_Text { get; set; }
    }
    public static class LevelUI
    {
        public static Dictionary<ZDOID, StarLevelHud> characterExtendedHuds = new Dictionary<ZDOID, StarLevelHud>();
        private static GameObject star;

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.Awake))]
        public static class EnableLevelDisplay
        {
            public static void Postfix(EnemyHud __instance) {
                // Logger.LogDebug($"Updating Enemy Hud, expanding stars");
                // Need a patch to show the number of stars something is
                // Need to setup the 1-5 stars, and the 5-n stars
                star = __instance.m_baseHud.transform.Find("level_2/star").gameObject;

                // Destroys the extra star for level 3, so that we can just enable levels 2-6 to add their respective star
                Object.Destroy(__instance.m_baseHud.transform.Find("level_3/star").gameObject);

                // Levels 1-5 get their stars, then we also get the n* setup
                StarLevelHudDisplay(star, __instance.m_baseHud.transform, __instance.m_baseHudBoss.transform);
            }

            private static void StarLevelHudDisplay(GameObject star, Transform basehud, Transform bosshud)
            {
                SetupStar(star, 4, basehud);
                SetupStar(star, 5, basehud);
                SetupStar(star, 6, basehud);

                // Star N | this position is set as the same 
                GameObject star_7 = new GameObject(name: "level_n");
                star_7.transform.SetParent(basehud);
                GameObject star7 = Object.Instantiate(star, star_7.transform);
                // star7.transform.SetParent(star_7.transform);
                star_7.transform.localPosition = new Vector3(x: -42, y: 19, z: 0);
                star7.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                GameObject s7Name = new GameObject(name: "level_n_name");
                GameObject star7Name = Object.Instantiate(s7Name, star_7.transform);
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
                GameObject star_7_boss = new GameObject(name: "level_n");
                star_7_boss.transform.SetParent(bosshud);
                GameObject star7_boss = Object.Instantiate(star, star_7_boss.transform);
                // star7.transform.SetParent(star_7.transform);
                star_7_boss.transform.localPosition = new Vector3(x: -42, y: 19, z: 0);
                star7_boss.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                GameObject s7Name_boss = new GameObject(name: "level_n_name");
                GameObject star7Name_boss = Object.Instantiate(s7Name_boss, star_7_boss.transform);
                star7Name_boss.transform.SetParent(star_7_boss.transform);
                star7Name_boss.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                GUIManager.Instance.CreateText(
                    text: "999",
                    parent: star7Name_boss.transform,
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
                star_7_boss.SetActive(false);
            }

            private static void SetupStar(GameObject star, int level, Transform parent_t, bool boss = false)
            {
                GameObject new_star_holder = new GameObject(name: $"level_{level}");
                new_star_holder.transform.SetParent(parent_t);
                GameObject new_star = Object.Instantiate(star, new_star_holder.transform);
                new_star_holder.SetActive(false);

                switch(level) {
                    // Level 1 is the base level, so we don't need to do anything
                    // Level 2-3 don't have a non-boss entry because they use the vanilla entries
                    case 2:
                        if (boss) {
                            new_star_holder.transform.localPosition = new Vector3(x: -41, y: 19, z: 0);
                        }
                        break;
                    case 3:
                        if (boss) {
                            new_star_holder.transform.localPosition = new Vector3(x: -26, y: 19, z: 0);
                        }
                        break;
                    case 4:
                        if (boss) {
                            new_star_holder.transform.localPosition = new Vector3(x: -9, y: 19, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        } else {
                            new_star_holder.transform.localPosition = new Vector3(x: -9, y: 19, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        }
                        break;
                    case 5:
                        if (boss) {
                            new_star_holder.transform.localPosition = new Vector3(x: 7, y: 19, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        } else {
                            new_star_holder.transform.localPosition = new Vector3(x: 7, y: 19, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        }
                        break;
                    case 6:
                        if (boss) {
                            new_star_holder.transform.localPosition = new Vector3(x: 23, y: 19, z: 0);
                            new_star.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                        } else {
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
                // characters are only non-bosses
                if (!c.IsBoss()) {
                    __instance.m_huds.TryGetValue(c, out var value);
                    value.m_level2.gameObject.SetActive(false);
                    value.m_level3.gameObject.SetActive(false);
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
                characterExtendedHuds.Remove(char3.GetZDOID());
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

        public static void UpdateHudforAllLevels(EnemyHud.HudData ehud) {
            if (ehud == null || ehud.m_character == null) return;
            int level = ehud.m_character.GetLevel();
            // Logger.LogInfo($"Creature Level {level}");
            ZDOID czoid = ehud.m_character.GetZDOID();
            StarLevelHud extended_hud = new StarLevelHud();
            if (characterExtendedHuds.ContainsKey(czoid)) {
                // Logger.LogInfo($"Hud already exists for {czoid}, loading");
                // Cached items which have been destroyed need to be removed and re-attached
                extended_hud = characterExtendedHuds[czoid];
            } else {
                // Logger.LogInfo($"Hooking up Extended Hud Data. Children: {ehud.m_gui.transform.childCount}");
                //foreach (Transform child in ehud.m_gui.transform)
                //{
                //    Logger.LogInfo($"child path: {child.GetPath()}");
                //}

                extended_hud.isBoss = ehud.m_character.IsBoss();

                // Add the entry to the cache, and provide references
                extended_hud.starlevel2 = ehud.m_gui.transform.Find("level_2").gameObject;
                extended_hud.starlevel3 = ehud.m_gui.transform.Find("level_3").gameObject;
                extended_hud.starlevel4 = ehud.m_gui.transform.Find("level_4").gameObject;
                extended_hud.starlevel5 = ehud.m_gui.transform.Find("level_5").gameObject;
                extended_hud.starlevel6 = ehud.m_gui.transform.Find("level_6").gameObject;
                extended_hud.starlevel_N = ehud.m_gui.transform.Find("level_n").gameObject;
                extended_hud.starlevel_N_Text = ehud.m_gui.transform.Find("level_n/level_n_name(Clone)/Text").gameObject.GetComponent<Text>();
                // Need to find and add the N level text for updating here
                characterExtendedHuds.Add(czoid, extended_hud);
            }
            // Star level display starts at 2
            // Enable static star levels
            // Logger.LogInfo("Setting star levels active");
            switch (level) {
                case 2:
                    extended_hud.starlevel2.SetActive(true);
                    break;
                case 3:
                    extended_hud.starlevel2.SetActive(true);
                    extended_hud.starlevel3.SetActive(true);
                    break;
                case 4:
                    extended_hud.starlevel2.SetActive(true);
                    extended_hud.starlevel3.SetActive(true);
                    extended_hud.starlevel4.SetActive(true);
                    break;
                case 5:
                    extended_hud.starlevel2.SetActive(true);
                    extended_hud.starlevel3.SetActive(true);
                    extended_hud.starlevel4.SetActive(true);
                    extended_hud.starlevel5.SetActive(true);
                    break;
                case 6:
                    extended_hud.starlevel2.SetActive(true);
                    extended_hud.starlevel3.SetActive(true);
                    extended_hud.starlevel4.SetActive(true);
                    extended_hud.starlevel5.SetActive(true);
                    extended_hud.starlevel6.SetActive(true);
                    break;
            }

            // Logger.LogInfo("Setting Nstar levels active");
            // Enable dynamic levels
            if (level > 6) {
                extended_hud.starlevel_N.SetActive(true);
                // get the text componet here and set its display
                extended_hud.starlevel_N_Text.text = (level - 1).ToString();
            }
        }
    }
}
