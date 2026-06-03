using BepInEx.Configuration;
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
        public static bool IsFGNEnabled = false;
        public static bool IsCustomRaidsEnabled = false;

        // Active only when CustomRaids is installed AND the compat toggle is enabled.
        public static bool CustomRaidsCompatActive =>
            IsCustomRaidsEnabled && ValConfig.EnableCustomRaidsCompat.Value;


        private static Type DropThatDropTableSessionManager;
        public static bool DropThatMethodsAvailable => DropThatDropTableSessionManager != null;

        // ModifyInstantiatedObjectDrop(GameObject drop, DropTable dropTable, int index)
        private static MethodInfo DropThatModifyInstantiatedObjectDrop;

        internal static void CheckModCompat() {
            try {
                Dictionary<string, BepInEx.BaseUnityPlugin> plugins = BepInExUtils.GetPlugins();
                if (plugins == null) { return; }
                //Logger.LogDebug($"Checking for mod compatibility... {string.Join(",", plugins.Keys)}");
                if (plugins.Keys.Contains("asharppen.valheim.drop_that")) {
                    IsDropThatEnabled = true;
                    DropThatDropTableSessionManager = Type.GetType("DropThat.Drop.CharacterDropSystem.Managers.CharacterDropSessionManager, DropThat");
                    if (DropThatMethodsAvailable) {
                        DropThatModifyInstantiatedObjectDrop = DropThatDropTableSessionManager.GetMethod("ModifyDrop", BindingFlags.Public | BindingFlags.Static);
                    } else {
                        Logger.LogWarning("Warning: Compat methods for DropThat not found, strict compatibility patches will be used.");
                    }
                }
                if (plugins.Keys.Contains("com.Fire.FiresGhettoNetworkMod")) {
                    IsFGNEnabled = true;
                }
                if (plugins.Keys.Contains("expand_world_size")) {
                    IsExpandWorldEnabled = true;
                }
                if (plugins.Keys.Contains("asharppen.valheim.custom_raids")) {
                    IsCustomRaidsEnabled = true;
                    Logger.LogInfo("Valheim.CustomRaids detected; CustomRaids raids will fire alongside SLS raids while EnableCustomRaidsCompat is enabled.");
                }
            } catch {
                Logger.LogWarning("Unable to check mod compatibility. Ensure that Bepinex can load.");
            }

        }

        public static bool DropThat_ModifyInstantiatedObjectDrop(GameObject drop, int index) {
            if (DropThatMethodsAvailable == false) { return false; }
            return (bool)DropThatModifyInstantiatedObjectDrop.Invoke(null, new object[] { drop, index });
        }


    }
}
