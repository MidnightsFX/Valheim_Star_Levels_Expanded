using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.modules {

    internal static class TaskRunner {
        private static GameObject RunnerGO;
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
    }

    internal class Orchestrator : MonoBehaviour {

    }
}
