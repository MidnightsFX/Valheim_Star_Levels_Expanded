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
        public GameObject starlevel4 {  get; set; }
        public GameObject starlevel5 { get; set; }
        public GameObject starlevel6 { get; set; }
        public GameObject starlevel_N { get; set; }
        public Text starlevel_N_Text { get; set; }
    }
    public static class LevelSystem
    {
        public static Dictionary<ZDOID, StarLevelHud> characterExtendedHuds = new Dictionary<ZDOID, StarLevelHud>();
        public static List<LevelEffects.LevelSetup> characterLevelEffects = new List<LevelEffects.LevelSetup>();
        private static GameObject star;

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.Awake))]
        public static class EnableLevelDisplay
        {
            public static void Postfix(EnemyHud __instance) {
                Logger.LogDebug($"Updating Enemy Hud, expanding stars");
                // Need a patch to show the number of stars something is
                // Need to setup the 1-5 stars, and the 5-n stars
                star = __instance.m_baseHud.transform.Find("level_2/star").gameObject;

                // Levels 1-5 get their stars, then we also get the n* setup

                // Star 4 (3)
                GameObject star_4 = new GameObject(name: "level_4");
                star_4.transform.SetParent(__instance.m_baseHud.transform);
                GameObject star4 = Object.Instantiate(star, star_4.transform);
                star4.transform.SetParent(star_4.transform);
                star_4.SetActive(false);
                star_4.transform.localPosition = new Vector3(x: -9, y: 19, z: 0);
                star4.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);

                // Star 5 (4)
                GameObject star_5 = new GameObject(name: "level_5");
                star_5.transform.SetParent(__instance.m_baseHud.transform);
                GameObject star5 = Object.Instantiate(star, star_5.transform);
                star5.transform.SetParent(star_5.transform);
                star_5.SetActive(false);
                star_5.transform.localPosition = new Vector3(x: 7, y: 19, z: 0);
                star5.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);

                // Star 6 (5)
                GameObject star_6 = new GameObject(name: "level_6");
                star_6.transform.SetParent(__instance.m_baseHud.transform);
                GameObject star6 = Object.Instantiate(star, star_6.transform);
                star6.transform.SetParent(star_6.transform);
                star_6.SetActive(false);
                star_6.transform.localPosition = new Vector3(x: 23, y: 19, z: 0);
                star6.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);

                // Star N | this position is set as the same 
                GameObject star_7 = new GameObject(name: "level_n");
                star_7.transform.SetParent(__instance.m_baseHud.transform);
                GameObject star7 = Object.Instantiate(star, star_7.transform);
                star7.transform.SetParent(star_7.transform);
                star_7.transform.localPosition = new Vector3(x: -42, y: 19, z: 0);
                star7.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                GameObject s7Name = new GameObject(name: "level_n_name");
                GameObject star7Name = Object.Instantiate(s7Name, star_7.transform);
                star7Name.transform.SetParent(star_7.transform);
                star7Name.transform.localPosition = new Vector3(x: 0, y: 0, z: 0);
                var level = GUIManager.Instance.CreateText(
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
            }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))]
        public static class DisableVanillaStarsByDefault
        {
            public static void Postfix(EnemyHud __instance, Character c) {
                __instance.m_huds.TryGetValue(c, out var value);
                value.m_level2.gameObject.SetActive(false);
                value.m_level3.gameObject.SetActive(false);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetMaxHealth))]
        public static class SetupMaxLevelHealthPatch {
            // Modify the max health that a creature can have based on the levelup health bonus value
            public static void Prefix(Character __instance, ref float health) {
                health = ValConfig.LevelUpHealthBonus.Value * __instance.GetMaxHealthBase();
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
                extended_hud = characterExtendedHuds[czoid];
            } else {
                //Logger.LogInfo($"Hooking up Extended Hud Data. Children: {ehud.m_gui.transform.childCount}");
                //foreach (Transform child in ehud.m_gui.transform) {
                //    Logger.LogInfo($"child path: {child.GetPath()}");
                //}

                // Add the entry to the cache, and provide references
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
            switch (level) {
                case 2:
                    ehud.m_level2.gameObject.SetActive(true);
                    break;
                case 3:
                    ehud.m_level3.gameObject.SetActive(true);
                    break;
                case 4:
                    ehud.m_level3.gameObject.SetActive(true);
                    extended_hud.starlevel4.SetActive(true);
                    break;
                case 5:
                    ehud.m_level3.gameObject.SetActive(true);
                    extended_hud.starlevel4.SetActive(true);
                    extended_hud.starlevel5.SetActive(true);
                    break;
                case 6:
                    ehud.m_level3.gameObject.SetActive(true);
                    extended_hud.starlevel4.SetActive(true);
                    extended_hud.starlevel5.SetActive(true);
                    extended_hud.starlevel6.SetActive(true);
                    break;
            }

            // Enable dynamic levels
            if (level > 6) {
                extended_hud.starlevel_N.SetActive(true);
                // get the text componet here and set its display
                extended_hud.starlevel_N_Text.text = (level - 1).ToString();
            }
        }

        // Consider if we want to use emissive colors?
        public static void SetupLevelEffects() {
            // Ensure known good color combos are available
            Colorization.AddGoodColorCombos();
            bool added_color_combos = false;
            for (int level = 2; 101 > level; level++) {
                // Add all of the known good color combinations first, then generate fillers for the rest
                if (added_color_combos == false) {
                    foreach(ColorDef col in Colorization.LevelColors) {
                        float colscale = 1f;
                        if (ValConfig.EnableCreatureScalingPerLevel.Value) { colscale += (ValConfig.PerLevelScaleBonus.Value * level); }
                        characterLevelEffects.Add(new LevelEffects.LevelSetup() { m_setEmissiveColor = false, m_scale = colscale, m_hue = col.hue, m_saturation = col.saturation, m_value = col.value });
                        Logger.LogDebug($"LevelEffects: {level} - scale:{colscale}, hue:{col.hue}, sat:{col.saturation}, val:{col.value}");
                        level++;
                    }
                    added_color_combos = true;
                }
                float scale = 1f;
                if (ValConfig.EnableCreatureScalingPerLevel.Value) {
                    // This flattens out at the end of the level cap, everything past that up to the max of 100 will have the same size
                    scale += (ValConfig.PerLevelScaleBonus.Value * level);
                }
                //float sat = UnityEngine.Random.Range(0f, 1f);
                float sat = UnityEngine.Random.Range(0f, 1f);
                float hue = UnityEngine.Random.Range(0f, 1f);
                float value = UnityEngine.Random.Range(0f, 1f);
                Logger.LogDebug($"LevelEffects: {level} - scale:{scale}, hue:{hue}, sat:{sat}, val:{value}");
                characterLevelEffects.Add(new LevelEffects.LevelSetup() { m_setEmissiveColor = false, m_scale = scale, m_hue = hue, m_saturation = sat, m_value = value });
            }
            

        }

        [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.Start))]
        public static class DefineLevelupModifiers {
            public static void Prefix(LevelEffects __instance) {
                // Remove the vanilla level setups so we can add level setups for every level needed.
                //__instance.m_levelSetups.Clear();
                __instance.m_levelSetups.AddRange(characterLevelEffects);
            }
        }

        [HarmonyPatch(typeof(CharacterDrop))]
        public static class ModifyLootPerLevelEffect
        {
            // [HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CharacterDrop.GenerateDropList))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldloc_1),
                    new CodeMatch(OpCodes.Mul),
                    new CodeMatch(OpCodes.Stloc_S)
                    ).Advance(2).RemoveInstruction().InsertAndAdvance(
                    Transpilers.EmitDelegate(ModifyDropsForExtendedStars)
                    ).ThrowIfNotMatch("Unable to patch Character drop generator, drops will not be modified.");

                return codeMatcher.Instructions();
            }
        }

        public static int ModifyDropsForExtendedStars(int base_drop_amount, int level)
        {
            return (int)(ValConfig.PerLevelLootScale.Value * level * base_drop_amount);
        }
        
    }
}
