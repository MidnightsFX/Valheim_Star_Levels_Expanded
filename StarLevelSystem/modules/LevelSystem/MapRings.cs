using Jotunn.Managers;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Analytics;
using UnityEngine.SceneManagement;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class MapRings {
        public static Vector3 center = new Vector3(0, 0, 0);
        private static bool buildingMapRings = false;
        private static bool ringAvailable = false;

        public static void DelayedMinimapSetup() {
            if (SceneManager.GetActiveScene().name != "main") {
                // Dont try to draw the map when we are not in the game
                return;
            }
            TaskRunner.Run().StartCoroutine(CheckAndDrawMapRings());
        }

        private static IEnumerator CheckAndDrawMapRings() {
            // Check to ensure that the config synchronziation has occured if we are on a dedicated server
            Logger.LogDebug("Waiting to draw map");
            yield return new WaitForSeconds(10f);
            if (ZNet.instance.IsCurrentServerDedicated()) {
                int iterations = 0;
                while (ValConfig.RecievedConfigsFromServer == false) {
                    Logger.LogDebug("Waiting for config sync to complete before drawing map rings on dedicated server.");
                    yield return new WaitForSeconds(5f);
                    iterations++;
                    if (iterations >= 25) {
                        Logger.LogWarning("Config sync not detected. Waiting timeframe expired.");
                        break;
                    }
                }
            }
            CreateLevelBonusRingMapOverlays();
            yield break;
        }

        private static void CreateLevelBonusRingMapOverlays() {
            if (ValConfig.EnableMapRingsForDistanceBonus.Value == false) { return; }
            SetRingCenter();
            Logger.LogDebug("Creating Level Bonus Rings on Map");
            if (buildingMapRings == false) {
                buildingMapRings = true;
                TaskRunner.Run().StartCoroutine(BuildMapRingOverlay());
            }
        }

        public static void OnRingCenterChanged(object s, EventArgs e) {
            if (ZNet.instance.IsCurrentServerDedicated()) { return; }
            SetRingCenter();
            DelayedMinimapSetup();
        }

        public static void SetRingCenter() {
            if (ValConfig.DistanceBonusIsFromStarterTemple.Value) {
                GameObject startTemple = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.name == "StartTemple").FirstOrDefault();
                if (startTemple != null) {
                    center = startTemple.transform.position;
                } else {
                    Logger.LogWarning("Unable to find starter temple, bonus rings will use world center. (0,0,0)");
                    center = new Vector3(0, 0, 0);
                }
            } else {
                center = new Vector3(0, 0, 0);
            }
        }

        public static void UpdateMapColorSettingsOnChange(object s, EventArgs e) {
            Colorization.UpdateMapColorSelection();
            DelayedMinimapSetup();
        }

        public static void UpdateMapRingEnableSettingOnChange(object s, EventArgs e) {
            if (ValConfig.EnableMapRingsForDistanceBonus.Value) {
                DelayedMinimapSetup();
            } else {
                // prevent invoking the minimap manager if we don't need it
                if (ringAvailable == true) {
                    MinimapManager.MapOverlay ringbonuses = MinimapManager.Instance.GetMapOverlay("SLS-LevelBonus");
                    if (ringbonuses == null) { return; }
                    ringbonuses.Enabled = false;
                }
            }
        }

        private static IEnumerator BuildMapRingOverlay() {
            // Skip if distances are not defined.
            if (LevelSystemData.SLE_Level_Settings.DistanceLevelBonus == null || LevelSystemData.SLE_Level_Settings.DistanceLevelBonus.Keys.Count <= 0) {
                yield break;
            }
            if (ZNet.instance.IsDedicated()) {
                Logger.LogDebug("Server is headless, skipping minimap generation");
                yield break;
            }
            MinimapManager.MapOverlay ringbonuses = MinimapManager.Instance.GetMapOverlay("SLS-LevelBonus");
            ringbonuses.Enabled = true;

            // Create a Color array with space for every pixel of the map
            int mapSize = ringbonuses.TextureSize * ringbonuses.TextureSize;
            Color[] mainPixels = new Color[mapSize];

            // Clear the existing map?
            ringbonuses.OverlayTex.SetPixels(mainPixels);
            // Determine size of the world
            //float worlddiameter = WorldGenerator.worldSize * 2; // - to + range, we need the diameter
            // float meters_per_pixel = (Minimap.instance.m_textureSize / 2) + ValConfig.PixelMapOffsetRatio.Value; // ValConfig.PixelMapOffsetRatio.Value; // worlddiameter / ringbonuses.TextureSize; // 9.765625

            Minimap.instance.WorldToPixel(center, out int world_x, out int world_y);
            Logger.LogDebug($"Map centered: x:{world_x} y:{world_y}");

            int updates = 0;
            int levelring_color_index = 0;
            foreach (int ringDistance in LevelSystemData.SLE_Level_Settings.DistanceLevelBonus.Keys) {
                if (levelring_color_index >= Colorization.mapRingColors.Count) {
                    levelring_color_index = 0;
                }
                Color selectedColor = Colorization.mapRingColors[levelring_color_index];
                levelring_color_index++;

                int granularity = ringDistance * 10; // number of vertices per ring

                Vector3 radii = new Vector3(center.x + ringDistance, center.y, center.z);
                Minimap.instance.WorldToPixel(radii, out int radii_x, out int raddi_y);
                int map_radii = radii_x - world_x;
                Logger.LogDebug($"Set Ringsize: {ringDistance} -PixelMap-> {radii_x} | {map_radii}");
                //Vector2[] circle = new Vector2[granularity];
                float delta = (2 * Mathf.PI) / granularity;

                for (int i = 0; i < granularity; i++) {
                    // Ensure we do not overwhelm the system and get the task killed
                    updates++;
                    if (updates % 3_000 == 0) {
                        yield return new WaitForEndOfFrame();
                    }

                    float t = delta * i;
                    int x = Mathf.RoundToInt(world_x + Mathf.Cos(t) * map_radii);
                    int y = Mathf.RoundToInt(world_y + Mathf.Sin(t) * map_radii);
                    //circle[i] = new Vector2(x, y);
                    if (ringbonuses == null) { yield break; }

                    int index = (y * ringbonuses.TextureSize) + x;
                    // Index must be less than pixels due to zero indexing and greater than zero
                    if (index >= mainPixels.Length || index < 0) {
                        continue;
                    }
                    //Logger.LogDebug($"Drawing ring for distance {ringDistance} pixels idx:{index}[{mainPixels.Length}] x:{x} y:{y}");
                    mainPixels[index] = selectedColor;
                }
            }

            if (ringbonuses == null) { yield break; }
            ringbonuses.OverlayTex.SetPixels(mainPixels);
            ringbonuses.OverlayTex.Apply();
            Logger.LogDebug("Finished Creating Level Bonus Rings on Minimap");
            buildingMapRings = false;
            ringAvailable = true;
            yield break;
        }
    }
}
