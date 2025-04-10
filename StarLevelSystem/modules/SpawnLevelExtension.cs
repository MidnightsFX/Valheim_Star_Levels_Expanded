using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace StarLevelSystem.modules
{
    class SpawnLevelExtension
    {

        [HarmonyPatch(typeof(CreatureSpawner))]
        public static class ModifyMaxLevel
        {
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CreatureSpawner.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(CreatureSpawner), nameof(CreatureSpawner.m_maxLevel)))
                    ).RemoveInstructions(2).InsertAndAdvance(
                    Transpilers.EmitDelegate(CreatureSpawnerLevel)
                    ).ThrowIfNotMatch("Unable to patch Spawner max level.");

                return codeMatcher.Instructions();
            }

            private static int CreatureSpawnerLevel() {
                return ValConfig.MaxLevel.Value + 1;
            }
        }


        [HarmonyPatch]
        static class SpawnCommandDelegate
        {
            [HarmonyTargetMethod]
            static MethodBase FindSpawnCommandDelegateMethod()
            {
                return AccessTools.GetDeclaredMethods(typeof(Terminal))
                  .Where(method => method.Name.IndexOf("__spawn|", StringComparison.Ordinal) >= 0)
                  .FirstOrDefault();
            }

            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> SpawnCommandDelegateTranspiler(
                IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                Type[] parameters = new Type[] { typeof(int), typeof(int) };
                return new CodeMatcher(instructions, generator)
                    .Start()
                    .MatchStartForward(
                        new CodeMatch(
                            OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Min), parameters)))
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
                        new CodeMatch(OpCodes.Ldarg_1),
                        new CodeMatch(OpCodes.Ldc_I4_4),
                        new CodeMatch(OpCodes.Stfld))
                    .ThrowIfInvalid($"Could not patch Terminal.SpawnCommandDelegate()! (set-level-4)")
                    .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop))
                    .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop))
                    .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop))
                    .InstructionEnumeration();
            }

            static int MathfMinDelegate(int level, int value)
            {
                return level;
            }
        }
    }
}
