using Jotunn.Managers;
using StarLevelSystem.modules.CreatureSetup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.modules {

    internal static class TaskRunner {
        internal static GameObject RunnerGO;
        internal static Orchestrator Instance = null;

        internal static void Setup() {
            GameObject go = new GameObject($"{StarLevelSystem.PluginName}_TaskRunner");
            Instance = go.AddComponent<Orchestrator>();
            RunnerGO = go;
            //PrefabManager.Instance.AddPrefab(go);
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // Reconnect dontDestroyOnload Runner if needs be?
        internal static Orchestrator Run() {
            if (Instance != null) { return Instance; }

            Setup();
            return Instance;
        }

        // Physics.SyncTransforms pushes every moved or rescaled transform in the scene to PhysX, so calling it once per
        // resized object repeated the same global work many times a frame. Valheim leaves autoSyncTransforms off and
        // physics syncs on its own before each simulation step; this only keeps physics queries current in the gap
        // before that step, with at most one sync per frame from Orchestrator.LateUpdate.
        internal static void RequestPhysicsSync() {
            Orchestrator.PhysicsSyncRequested = true;
        }
    }

    internal class Orchestrator : MonoBehaviour {
        internal static bool PhysicsSyncRequested = false;

        private void LateUpdate() {
            if (PhysicsSyncRequested == false) { return; }
            PhysicsSyncRequested = false;
            Physics.SyncTransforms();
        }
    }
}
