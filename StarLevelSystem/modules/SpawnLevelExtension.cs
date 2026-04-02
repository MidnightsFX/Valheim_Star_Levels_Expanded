using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
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
                        new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new Type[] { typeof(GameObject) })),
                        new CodeMatch(OpCodes.Ldarg_1),
                        new CodeMatch(OpCodes.Ldfld),
                        new CodeMatch(OpCodes.Ldc_I4_1)
                    ).ThrowIfInvalid($"Could not patch Terminal.SpawnCommandDelegate()! (level-compare)")
                    .Advance(3).RemoveInstruction().InsertAndAdvance(
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
                ModificationExtensionSystem.CreatureSetup(chara, leveloverride: level, multiply: false, delay: 0.01f);
            }
        }
    }
}
