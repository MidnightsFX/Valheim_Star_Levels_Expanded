using HarmonyLib;
using StarLevelSystem.Data;
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

            //[HarmonyDebug]
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
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel))
                    ))
                    .RemoveInstructions(1)
                    .InsertAndAdvance(
                        new CodeInstruction(
                            OpCodes.Call, AccessTools.Method(typeof(SpawnCommandDelegate), nameof(SetCreatureSpawnLevel)))
                    ).ThrowIfInvalid($"Could not patch terminal.SpawnCommandDelegate()! (SetCreatureSpawnLevel)")
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

            static void SetCreatureSpawnLevel(Character chara, int level) {
                //Logger.LogWarning($"Setting creature level");
                chara.m_nview?.GetZDO()?.Set("SLS_DSpwnMlt", true); // prevent multispawn from these spawned creatures
                chara.SetLevel(level);
                // Rebuild the cache entry to ensure the creature gets the specified level effects
                //Logger.LogDebug("Setting spawned creature details.");
                CompositeLazyCache.GetAndSetDetailCache(chara, true, level);
                //ModificationExtensionSystem.CreatureSetup(chara, true, level, 0f, true);
                //chara.SetupMaxHealth();
            }
        }
    }
}
