using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static ClutterSystem;
using static Mono.Security.X509.X520;

namespace StarLevelSystem.modules
{
    class SpawnerLevelExtension
    {
        [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Awake))]
        public static class ModifyMaxLevel
        {
            public static void Postfix(CreatureSpawner __instance)
            {
                __instance.m_maxLevel = ValConfig.MaxLevel.Value + 1;
            }
        }


        //[HarmonyPatch(typeof(Terminal))]
        //public static class ModifySpawnableLevels
        //{
        //    static bool TryGetDelegateMethod(Terminal.ConsoleCommand command, out MethodInfo method)
        //    {
        //        method = (command == default ? default : command.action?.Method ?? command.actionFailable?.Method);
        //        return method != default;
        //    }

        //    [HarmonyPostfix]
        //    [HarmonyPatch(nameof(Terminal.InitTerminal))]
        //    static void InitTerminalPatch()
        //    {
        //        if (TryGetDelegateMethod(Terminal.commands["spawn"], out MethodInfo spawnMethod))
        //        {
        //            StarLevelSystem.HarmonyInstance.Patch(
        //                spawnMethod,
        //                transpiler: new HarmonyMethod(AccessTools.Method(typeof(ModifySpawnableLevels), nameof(SpawnTranspiler)), debug: true));
        //        }
        //    }

        //    [HarmonyDebug]
        //    static IEnumerable<CodeInstruction> SpawnTranspiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
        //    {
        //        var codeMatcher = new CodeMatcher(instructions);
        //        codeMatcher.MatchStartForward(
        //            new CodeMatch(OpCodes.Ldarg_1)
        //            )
        //            .ThrowIfNotMatch("Unable to patch spawn command, item and creature spawn quality capped at 4 and 8.");
        //        // Need to remove a few lines

        //        return codeMatcher.Instructions();
        //    }
        //}

        //[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        //public static class PatchSpawnLevels
        //{
        //    [HarmonyDebug]
        //    [HarmonyTranspiler]
        //    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
        //    {
        //        var codeMatcher = new CodeMatcher(instructions);
        //        codeMatcher.MatchStartForward(
        //            new CodeMatch(OpCodes.Ldarg_1),
        //            new CodeMatch(OpCodes.Ldarg_1),
        //            new CodeMatch(OpCodes.Ldfld),
        //            new CodeMatch(OpCodes.Ldc_I4_4),
        //            new CodeMatch(OpCodes.Call),
        //            new CodeMatch(OpCodes.Stfld)
        //            ).RemoveInstructions(7)
        //            .ThrowIfNotMatch("Unable to patch spawn command, item and creature spawn quality capped at 4 and 8.");
        //        // Need to remove a few lines

        //        return codeMatcher.Instructions();
        //    }
        //}

        internal class ExtendedSpawnCommand : ConsoleCommand
        {
            public override string Name => "SLE_Spawn";

            public override string Help => "Allows Spawning higher level creatures";

            public override bool IsCheat => true;

            public override void Run(string[] args)
            {
                int amount = 1;
                string prefab = "greydwarf";
                int level = 0;
                try {
                    if (args.Length == 3) {
                        prefab = args[0];
                        amount = int.Parse(args[1]);
                        level = int.Parse(args[2]);
                    }
                    else if (args.Length == 2) {
                        prefab = args[0];
                        amount = int.Parse(args[1]);
                    }
                    else if (args.Length == 1) {
                        prefab = args[0];
                    } else {
                        Console.instance.Print($"Using Spawn Defaults: SLE_Spawn prefab: {prefab} amount: {amount} level: {level}");
                    }
                }
                catch {
                    Console.instance.Print($"lucktest invalid arguments, was 'SLE_Spawn {string.Join(" ", args)}' using the default: 'SLE_Spawn {prefab} {amount} {level}'");
                }

                GameObject prefab_go = PrefabManager.Instance.GetPrefab(prefab);
                if (!prefab_go) {
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Missing object " + prefab);
                    return;
                }
                Vector3 vector = UnityEngine.Random.insideUnitSphere;
                for (int num12 = 0; num12 < amount; num12++) {
                    GameObject spawned_go = UnityEngine.Object.Instantiate(prefab_go, Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 2f + Vector3.up + vector, Quaternion.identity);
                    spawned_go.GetComponent<Character>()?.SetLevel(level);
                }

            }
        }
    }
}
