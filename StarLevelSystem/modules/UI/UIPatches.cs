using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YamlDotNet.Core.Tokens;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.UI {
    internal static class UIPatches {

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.Awake))]
        public static class EnableLevelDisplay {
            public static void Postfix(EnemyHud __instance) {
                // Logger.LogDebug($"Updating Enemy Hud, expanding stars");
                // Need a patch to show the number of stars something is
                // Need to setup the 1-5 stars, and the 5-n stars
                GameObject star = __instance.m_baseHud.transform.Find("level_2/star").gameObject;

                // Destroys the extra star for level 3, so that we can just enable levels 2-6 to add their respective star
                // Object.Destroy(__instance.m_baseHud.transform.Find("level_3/star").gameObject);
                __instance.m_baseHud.transform.Find("level_3/star").gameObject.SetActive(false);

                // Levels 1-5 get their stars, then we also get the n* setup
                UIHudControl.StarLevelHudDisplay(star, __instance.m_baseHud.transform, __instance.m_baseHudBoss.transform);

                // Setup boss huds for stacking support
                Logger.LogDebug("Setting BossHud root");
                UIHudControl.BossHudRoot = GameObject.Instantiate(new GameObject("BossHuds"), __instance.transform.Find("HudRoot").transform);
                UIHudControl.BossHudRoot.name = "BossHuds"; // remove the "Cloned"

                // Layout group (vertical = stacked, horizontal = squished) + spacing/top-buffer from config.
                UIHudControl.EnsureBossLayoutGroup(ValConfig.StackMultipleBossHealthbars.Value);

                // Pin the root to the top-center of the screen via anchors so it stays in place at any
                // resolution / GUI scale. The buffer below the top edge is the layout group's top padding.
                RectTransform rootRT = UIHudControl.BossHudRoot.GetComponent<RectTransform>();
                if (rootRT != null) {
                    rootRT.anchorMin = rootRT.anchorMax = rootRT.pivot = new Vector2(0.5f, 1f);
                    rootRT.anchoredPosition = Vector2.zero;
                }

                // Add a layout element to the top level of this, if it doesn't already exist
                Logger.LogDebug("Setting root layout element");
                if (__instance.m_baseHudBoss.GetComponent<LayoutElement>() == null) {
                    LayoutElement le = __instance.m_baseHudBoss.AddComponent<LayoutElement>();
                    le.minHeight = 50f;
                }

                // Pull down the name tag slightly (width is applied by SetBossBarWidth)
                Logger.LogDebug("Modifying name position");
                RectTransform nameRT = (RectTransform)__instance.m_baseHudBoss.transform.Find("Name");
                nameRT.localPosition = new Vector3(0, 42f, 0);

                // Create a container for scaling the background shaders
                Logger.LogDebug("Building boss health bar background container");
                Transform HealthTForm = __instance.m_baseHudBoss.transform.Find("Health");
                GameObject backgroundContainer = GameObject.Instantiate(new GameObject("Background"), HealthTForm);
                backgroundContainer.name = "Background";
                RectTransform bkgRT = (RectTransform)HealthTForm.Find("bkg").transform;
                RectTransform darkenRT = (RectTransform)HealthTForm.Find("darken").transform;
                bkgRT.SetParent(backgroundContainer.transform, false);
                darkenRT.SetParent(backgroundContainer.transform, false);
                // Setting as the first sibling to render behind other elements, ensuring this is the "background"
                backgroundContainer.transform.SetSiblingIndex(0);

                // Size the template (and request a re-layout on the next UpdateHuds for live bosses).
                float bossWidth = (Screen.width / UIHudControl.GetHudScaleFactor(__instance)) * ValConfig.BossHealthbarWidthPercent.Value;
                UIHudControl.SetBossBarWidth(__instance.m_baseHudBoss, bossWidth);
                UIHudControl.BossHudConfigDirty = true;
            }
        }

        // Runs after EnemyHud has updated every hud; stacks (vertical) or squishes (horizontal) the boss
        // bars depending on StackMultipleBossHealthbars. Gated on count/config change inside StackBossHuds.
        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
        public static class StackBossHealthbars {
            public static void Postfix(EnemyHud __instance) {
                UIHudControl.StackBossHuds(__instance);
            }
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.SetName))]
        public static class UpdateTamedName {
            public static void Postfix(Tameable __instance) {
                //Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance.m_character);
                //__instance.m_character.m_nview.GetZDO().Set(SLS_CHARNAME, CreatureModifiers.BuildCreatureLocalizableName(__instance.m_character, mods)); 
                UIHudControl.InvalidateCacheEntry(__instance.m_character);
            }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))]
        public static class DisableVanillaStarsByDefault {
            public static void Postfix(EnemyHud __instance, Character c) {
                if (__instance == null || c == null) { return; }
                // non-bosses and players
                __instance.m_huds.TryGetValue(c, out var value);
                if (!c.IsBoss()) {
                    if (value != null) {
                        if (value.m_level2 == null || value.m_level3 == null) {
                            return;
                        }
                        value.m_level2?.gameObject?.SetActive(false);
                        value.m_level3?.gameObject?.SetActive(false);
                    }
                } else {
                    // Boss hud, setup elements
                    // Reparent to our container
                    // It is important that the world position is NOT KEPT. This allows existing, but extremely fragile scale settings to be kept
                    // Because valheims healthbar setup is entirely not structured (ie purely offset based and no layout elements used) it won't get resized
                    value.m_gui.transform.SetParent(UIHudControl.BossHudRoot.transform, false);

                    // Set the health bar display properly
                    //RectTransform rt = (RectTransform)value.m_gui.transform.Find("Health").transform;
                    //rt.sizeDelta = new Vector2(15, Screen.width);
                    //value.m_healthSlow.m_width = Screen.width;
                    //value.m_healthFast.m_width = Screen.width;
                    // Fix the health text display
                    // This needs to happen where the health text is added, since its not in this frame
                    //RectTransform healthNumRT = (RectTransform)rt.Find("HealthText(Clone)").transform;
                    //TextMeshPro healthTMP = healthNumRT.gameObject.GetComponent<TextMeshPro>();
                    //healthTMP.font = value.m_name.font;

                }
            }
        }

        [HarmonyPatch(typeof(EnemyHud))]
        public static class SetupCreatureLevelDisplay {
            //[HarmonyDebug]
            //[HarmonyEmitIL(".dump")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(EnemyHud.UpdateHuds))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
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
                    Transpilers.EmitDelegate(UIHudControl.UpdateHudForAllLevels)
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
                Tameable component = __instance.gameObject.GetComponent<Tameable>();
                if (component && __instance.IsTamed()) {
                    __result = component.m_nview.GetZDO().GetString(ZDOVars.s_tamedName, __result);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
        public static class AddPauseMenuButton {
            public static void Postfix(Menu __instance) {
                QuickConfigureTool.CreatePauseMenuButton(__instance);
            }
        }
    }
}
