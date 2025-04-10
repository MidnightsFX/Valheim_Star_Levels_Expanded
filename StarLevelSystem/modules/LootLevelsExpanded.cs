using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace StarLevelSystem.modules
{
    public static class LootLevelsExpanded
    {
        [HarmonyPatch(typeof(CharacterDrop))]
        public static class ModifyLootPerLevelEffect
        {
            // [HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CharacterDrop.GenerateDropList))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
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
