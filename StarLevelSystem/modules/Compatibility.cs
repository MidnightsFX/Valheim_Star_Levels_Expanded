using Jotunn.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.modules
{
    internal static class Compatibility
    {
        // Mod flags
        public static bool IsDropThatEnabled = false;
        public static bool IsExpandWorldEnabled = false;

        private static Type DropThatDropTableSessionManager;
        private static Type DropThatCharacterDropSessionManager;
        public static bool DropThatMethodsAvailable => DropThatDropTableSessionManager != null && DropThatCharacterDropSessionManager != null;

        // ModifyDrop(GameObject drop, List<KeyValuePair<GameObject, int>> drops, int index)
        private static MethodInfo DropThatModifyDrop;
        // ModifyInstantiatedObjectDrop(GameObject drop, DropTable dropTable, int index)
        private static MethodInfo DropThatModifyInstantiatedObjectDrop;

        internal static void CheckModCompat() {
            try {
                Dictionary<string, BepInEx.BaseUnityPlugin> plugins = BepInExUtils.GetPlugins();
                if (plugins == null) { return; }
                //Logger.LogDebug($"Checking for mod compatibility... {string.Join(",", plugins.Keys)}");
                if (plugins.Keys.Contains("asharppen.valheim.drop_that")) {
                    IsDropThatEnabled = true;
                    DropThatCharacterDropSessionManager = Type.GetType("DropThat.Drop.DropTableSystem.Managers.DropTableSessionManager, DropThat");
                    DropThatDropTableSessionManager = Type.GetType("DropThat.Drop.CharacterDropSystem.Managers.CharacterDropSessionManager, DropThat");
                    if (DropThatMethodsAvailable) {
                        DropThatModifyInstantiatedObjectDrop = DropThatDropTableSessionManager.GetMethod("ModifyDrop", BindingFlags.Public | BindingFlags.Static);
                        DropThatModifyDrop = DropThatCharacterDropSessionManager.GetMethod("ModifyDrop", BindingFlags.Public | BindingFlags.Static);
                    } else {
                        Logger.LogWarning("Warning: Compat methods for DropThat not found, strict compatibility patches will be used.");
                    }
                }
                if (plugins.Keys.Contains("expand_world_size")) {
                    IsExpandWorldEnabled = true;
                }
            } catch {
                Logger.LogWarning("Unable to check mod compatibility. Ensure that Bepinex can load.");
            }

        }

        public static bool DropThat_ModifyDrop(GameObject drop, List<KeyValuePair<GameObject, int>> drops, int index) {
            if (DropThatMethodsAvailable == false) { return false; }
            return (bool)DropThatModifyDrop.Invoke(null, new object[] { drop, index });
        }

        public static bool DropThat_ModifyInstantiatedObjectDrop(GameObject drop, int index) {
            if (DropThatMethodsAvailable == false) { return false; }
            return (bool)DropThatModifyInstantiatedObjectDrop.Invoke(null, new object[] { drop, index });
        }


    }
}
