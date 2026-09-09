using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using StarLevelSystem.modules.Sizes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    class SpawnLevelExtension
    {
        // Note: This can't be dumped due to the name containing illegal characters
        //[HarmonyEmitIL(".dumps")]
        [HarmonyPatch]
        internal static class SpawnCommandDelegate {
            [HarmonyTargetMethod]
            internal static MethodBase FindSpawnCommandDelegateMethod() {
                return AccessTools.GetDeclaredMethods(typeof(Terminal)).Where(method => method.Name.IndexOf("__spawn|", StringComparison.Ordinal) >= 0).FirstOrDefault();
            }

            [HarmonyTranspiler]
            internal static IEnumerable<CodeInstruction> SpawnCommandDelegateTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
                Type[] parameters = new Type[] { typeof(int), typeof(int) };
                return new CodeMatcher(instructions, generator)
                    .Start()
                    .MatchStartForward(
                        // Both parameter types are pinned: AccessTools matches argumentTypes exactly and
                        // returns null rather than throwing when it misses, and a CodeMatch built on a null
                        // operand matches *any* Call - so a stale array silently anchors on the wrong
                        // instruction instead of tripping ThrowIfInvalid. 1.0.7 added `bool cheated`.
                        new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new Type[] { typeof(GameObject), typeof(bool) })))
                    .ThrowIfInvalid($"Could not patch Terminal.SpawnCommandDelegate()! (OnCreateNew anchor)")
                    // The level compare is searched for rather than assumed to sit a fixed distance from
                    // the anchor: 1.0.7 inserted a `component2.m_itemData.m_durability = ...` block between
                    // OnCreateNew and `if (level > 1)`, which broke the old contiguous four-instruction
                    // match. Only the display-class field load is Ldarg_1-based in between, so the first
                    // hit after the anchor is still the level test.
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_1),
                        new CodeMatch(OpCodes.Ldfld),
                        new CodeMatch(OpCodes.Ldc_I4_1))
                    .ThrowIfInvalid($"Could not patch Terminal.SpawnCommandDelegate()! (level-compare)")
                    .Advance(2).RemoveInstruction().InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldc_I4_0)
                    )
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Min), parameters)))
                    .ThrowIfInvalid($"Could not patch Terminal.SpawnCommandDelegate()! (mathf-min-4)")
                    .SetInstructionAndAdvance(
                        new CodeInstruction(
                            OpCodes.Call, AccessTools.Method(typeof(SpawnCommandDelegate), nameof(MathfMinDelegate))))
                    .MatchStartForward(
                        new CodeMatch(
                            OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Min), parameters)))
                    .ThrowIfInvalid($"Could not patch Terminal.SpawnCommandDelegate()! (mathf-min-9)")
                    .SetInstructionAndAdvance(
                        new CodeInstruction(
                            OpCodes.Call, AccessTools.Method(typeof(SpawnCommandDelegate), nameof(MathfMinDelegate))))
                    .MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel))
                    ))
                    .RemoveInstructions(1)
                    .InsertAndAdvance(
                        new CodeInstruction(
                            OpCodes.Call, AccessTools.Method(typeof(SpawnCommandDelegate), nameof(SetCreatureSpawnLevel)))
                    )
                    .ThrowIfInvalid($"Could not patch terminal.SpawnCommandDelegate()! (SetCreatureSpawnLevel)")
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_1),
                        new CodeMatch(OpCodes.Ldc_I4_4),
                        new CodeMatch(OpCodes.Stfld))
                    .ThrowIfInvalid($"Could not patch Terminal.SpawnCommandDelegate()! (set-level-4)")
                    .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop))
                    .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop))
                    .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop))
                    .InstructionEnumeration();
            }

            static int MathfMinDelegate(int level, int value) {
                return level;
            }

            static void SetCreatureSpawnLevel(Character chara, int level) {
                //Logger.LogDebug($"Setting {chara.name} to lvl: {level}");
                CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(chara, level, updateCache: true);
                chara.m_nview.GetZDO().Set(ZDOVars.s_level, level);
                CreatureSetupControl.CreatureSetup(chara, leveloverride: level, multiply: false, delay: 0.01f);
            }
        }
    }
}
