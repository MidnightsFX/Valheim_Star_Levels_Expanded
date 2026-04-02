using HarmonyLib;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
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
            }
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.SetName))]
        public static class UpdateTamedName {
            public static void Postfix(Tameable __instance) {
                //Dictionary<string, ModifierType> mods = CompositeLazyCache.GetCreatureModifiers(__instance.m_character);
                //__instance.m_character.m_nview.GetZDO().Set(SLS_CHARNAME, CreatureModifiers.BuildCreatureLocalizableName(__instance.m_character, mods)); 
                UIHudControl.InvalidateCacheEntry(__instance.m_character.GetZDOID().ID);
            }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))]
        public static class DisableVanillaStarsByDefault {
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

        [HarmonyPatch(typeof(EnemyHud))]
        public static class SetupCreatureLevelDisplay {
            // [HarmonyDebug]
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
                    Transpilers.EmitDelegate(UIHudControl.UpdateHudforAllLevels)
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
    }
}
