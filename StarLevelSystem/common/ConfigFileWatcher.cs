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
            GameObject go = new GameObject("SLS_ConfigFileWatcher");
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

        // Re-seed a watched file's stamp after we rewrite it ourselves, so our own write is not seen as an
        // external change on the next poll. No-op for paths that are not (yet) watched.
        internal static void RefreshStamp(string fullPath) {
            if (WatchedFiles.TryGetValue(fullPath, out var entry)) {
                try {
                    var info = new FileInfo(fullPath);
                    entry.LastWriteUTC = info.LastWriteTimeUtc;
                    entry.FileLength = info.Length;
                } catch {
                    // File gone or inaccessible after our write; reset so the next poll skips it.
                    entry.LastWriteUTC = DateTime.MinValue;
                    entry.FileLength = 0;
                }
            }
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
