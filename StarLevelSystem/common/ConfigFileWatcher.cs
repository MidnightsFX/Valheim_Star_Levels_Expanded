using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.common {
    internal static class ConfigFileWatcher {

        private class WatchEntry {
            public DateTime LastWriteUTC {
                get; set;
            }
            public long FileLength {
                get; set;
            }
            public Action<string> Callback;

            public void Update(DateTime lastwrite, long len) {
                LastWriteUTC = lastwrite;
                FileLength = len;
            }
        }

        private static Dictionary<string, WatchEntry> WatchedFiles = new Dictionary<string, WatchEntry>();
        private static ConfigFileWatcherBehaviour watchProcess;

        internal static void Initialize() {
            if (watchProcess != null) return;
            GameObject go = new GameObject("VE_ConfigFileWatcher");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            watchProcess = go.AddComponent<ConfigFileWatcherBehaviour>();
            Logger.LogDebug("ConfigFileWatcher initialized.");
        }

        internal static void Register(string fullPath, Action<string> onChanged) {
            if (File.Exists(fullPath)) {
                var info = new FileInfo(fullPath);
                DateTime mtime = info.LastWriteTimeUtc;
                long size = info.Length;
                WatchedFiles.Add(fullPath, new WatchEntry() { LastWriteUTC = mtime, FileLength = size, Callback = onChanged });
            } else {
                WatchedFiles.Add(fullPath, new WatchEntry() { LastWriteUTC = DateTime.MinValue, FileLength = 0, Callback = onChanged });
            }
            Logger.LogDebug($"ConfigFileWatcher watching {fullPath}");
        }



        internal class ConfigFileWatcherBehaviour : MonoBehaviour {
            private float nextPollTime;

            public void Update() {
                if (Time.unscaledTime < nextPollTime) { return; }

                nextPollTime = Time.unscaledTime + ValConfig.ConfigPollIntervalSeconds.Value;
                Poll();
            }

            private static void Poll() {
                if (WatchedFiles.Count == 0) { return; }

                foreach (string key in WatchedFiles.Keys) {
                    if (File.Exists(key) == false) { continue; }

                    FileInfo info = new FileInfo(key);
                    DateTime mtime = info.LastWriteTimeUtc;
                    long size = info.Length;

                    WatchEntry we = WatchedFiles[key];

                    //Logger.LogDebug($"Comparing file details:\n lastwrite: {mtime} == {we.LastWriteUTC} ({mtime == we.LastWriteUTC})\n  size {size} == {we.FileLength} ({size == we.FileLength})");
                    if (mtime == we.LastWriteUTC && size == we.FileLength) { continue; }

                    WatchedFiles[key].LastWriteUTC = mtime;
                    WatchedFiles[key].FileLength = size;

                    try {
                        if (we.Callback != null) {
                            we.Callback(key);
                        }
                    } catch (Exception e) {
                        Logger.LogWarning($"ConfigFileWatcher callback for {key} threw: {e.Message}");
                    }
                }
            }
        }
    }
}
