using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.CreatureSetup;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.LevelSystem {
    internal static class UpdateLevelsOnChange {

        private static Coroutine pendingLevelPass;

        public static void ModifyLoadedCreatureLevels(object s, EventArgs e) {
            // Do not run before the area is loaded
            if (Player.m_localPlayer == null || ZNetScene.instance == null) { return; }
            if (ZNetScene.instance.IsAreaReady(Player.m_localPlayer.gameObject.transform.position) == false) { return; }
            // Dragging a cap slider raises one change per step, and each pass queues corrections that stick. Restart the
            // wait on every change so one pass walks the creatures once the value has settled, instead of one pass per
            // intermediate cap running over the same creatures at once.
            if (pendingLevelPass != null) { TaskRunner.Run().StopCoroutine(pendingLevelPass); }
            pendingLevelPass = TaskRunner.Run().StartCoroutine(ModifyLoadedCreaturesLevels());
        }

        public static void UpdateFishMaxLevel() {
            if (ValConfig.EnableScalingFish.Value == false) { return; }
            // Typed lookup - the old scan walked every GameObject in the heap, paying a name
            // allocation and a culture-aware StartsWith per object, on every world entry.
            foreach (Fish fish in Resources.FindObjectsOfTypeAll<Fish>()) {
                ItemDrop itemDrop = fish.GetComponent<ItemDrop>();
                if (itemDrop != null) {
                    //Logger.LogDebug($"Updating max quality {fish.gameObject.name}");
                    itemDrop.m_itemData.m_shared.m_maxQuality = ValConfig.FishMaxLevel.Value + 1;
                }
            }
        }

        // Disabled fish scaling keeps fish at their base size, whatever level they rolled.
        public static float FishScalePerLevel() {
            return ValConfig.EnableScalingFish.Value ? ValConfig.FishSizeScalePerLevel.Value : 0f;
        }

        // m_scaleByQuality lives in the item's shared data, which is never written to the ZDO, and vanilla applies
        // it from ItemDrop.Load and Fish.Awake before any spawn patch runs. So every peer sets it on the prefabs
        // (which new instances copy, and inventory items share) and on the instances already loaded.
        public static void UpdateFishScaleByQuality() {
            float scalePerLevel = FishScalePerLevel();
            foreach (Fish fish in Resources.FindObjectsOfTypeAll<Fish>()) {
                ItemDrop itemDrop = fish.GetComponent<ItemDrop>();
                if (itemDrop != null) {
                    itemDrop.m_itemData.m_shared.m_scaleByQuality = scalePerLevel;
                }
            }
        }

        public static IEnumerator ModifyLoadedCreaturesLevels() {
            // Realtime: a singleplayer pause (where the config panel opens) stops scaled time.
            yield return new WaitForSecondsRealtime(1f);
            int updated = 0;
            // A snapshot, since the walk spans frames and creatures come and go meanwhile.
            List<Character> creatures = new List<Character>(Character.GetAllCharacters());
            foreach (Character chara in creatures) {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0) {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                if (chara == null || chara.IsPlayer() || chara.m_nview == null || chara.m_nview.GetZDO() == null) { continue; }

                // Only creatures the over-level correction would act on: the same gate and the same bound it uses, so a
                // creature sitting legitimately at its cap is left alone.
                if (LevelSelection.OverLevelRerollEnabled(chara) == false) { continue; }
                LevelSelection.SelectCreatureBiomeSettings(chara.gameObject, out _, out CreatureSpecificSetting creatureSettings, out BiomeSpecificSetting biomeSettings, out Heightmap.Biome biome);
                if (chara.GetLevel() <= LevelSelection.GetMaxCreatureLevel(chara, creatureSettings, biomeSettings, biome)) { continue; }
                CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(chara, updateCache: true);

                CreatureSetupControl.CreatureSetup(chara, cce.Level);
                //LevelUI.InvalidateCacheEntry(chara);
            }
            pendingLevelPass = null;
        }

        public static void UpdateTreeSizeOnConfigChange(object s, EventArgs e) {
            // Do not run before the area is loaded
            if (Player.m_localPlayer == null) { return; }
            if (ZNetScene.instance.IsAreaReady(Player.m_localPlayer.gameObject.transform.position) == false) { return; }
            TaskRunner.Run().StartCoroutine(UpdateAllTreeSizesOnConfigChangeCoroutine());
        }

        public static void UpdateBirdSizeOnConfigChange(object s, EventArgs e) {
            // Do not run before the area is loaded
            if (Player.m_localPlayer == null) { return; }
            if (ZNetScene.instance.IsAreaReady(Player.m_localPlayer.gameObject.transform.position) == false) { return; }
            TaskRunner.Run().StartCoroutine(UpdateAllBirdSizesOnConfigChangeCoroutine());
        }

        public static void UpdateFishSizeOnConfigChange(object s, EventArgs e) {
            // Ahead of the area check: a server config sync lands while connecting, before the player exists, and
            // the fish loaded after it take their scaling from the prefab.
            UpdateFishScaleByQuality();
            // Do not run before the area is loaded
            if (Player.m_localPlayer == null) { return; }
            if (ZNetScene.instance.IsAreaReady(Player.m_localPlayer.gameObject.transform.position) == false) { return; }
            TaskRunner.Run().StartCoroutine(UpdateAllFishOnConfigChangeCoroutine());
        }

        public static IEnumerator UpdateAllTreeSizesOnConfigChangeCoroutine() {
            int updated = 0;
            IEnumerable<GameObject> trees = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.GetComponent<TreeBase>() != null);
            foreach (GameObject tree in trees) {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0) {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                TreeBase treeBase = tree.GetComponent<TreeBase>();
                if (treeBase == null || treeBase.m_nview == null || treeBase.m_nview.GetZDO() == null) { continue; }
                string treeName = Utils.GetPrefabName(tree.gameObject);
                // Check scalar objects, or fall back to the reference prefab scale
                Vector3 baseSize = treeBase.m_nview.GetZDO().GetVec3(ZDOVars.s_scaleHash, Vector3.zero);
                if (baseSize == Vector3.zero) {
                    float scaler = treeBase.m_nview.GetZDO().GetFloat(ZDOVars.s_scaleScalarHash, 0f);
                    baseSize = new Vector3(scaler, scaler, scaler);
                }
                // Falling back to the reference prefab scale will set tree size to be uniform, which will likely be adjusted when reloaded
                if (baseSize == Vector3.zero) {
                    baseSize = PrefabManager.Instance.GetPrefab(treeName).gameObject.transform.localScale;
                }
                if (ValConfig.EnableTreeScaling.Value == false) {
                    treeBase.transform.localScale = baseSize;
                    continue;
                }

                if (ValConfig.UseDeterministicTreeScaling.Value) {
                    float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * CompositeLazyCache.GetOrAddCachedTreeEntry(treeBase.m_nview));
                    treeBase.transform.localScale = baseSize * scale;
                } else {
                    int storedLevel = treeBase.m_nview.GetZDO().GetInt(SLS_TREE, 0);
                    if (storedLevel > 1) {
                        float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * storedLevel);
                        //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                        treeBase.transform.localScale = baseSize * scale;
                    }
                }
            }
            yield break;
        }

        public static IEnumerator UpdateAllBirdSizesOnConfigChangeCoroutine() {
            int updated = 0;
            Dictionary<string, Vector3> BirdSizeReferences = new Dictionary<string, Vector3>();
            IEnumerable<GameObject> birds = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.GetComponent<RandomFlyingBird>() != null);
            foreach (GameObject bird in birds) {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0) {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                RandomFlyingBird randomBird = bird.GetComponent<RandomFlyingBird>();
                if (randomBird == null || randomBird.m_nview == null || randomBird.m_nview.GetZDO() == null) { continue; }
                string birdName = Utils.GetPrefabName(bird.gameObject);
                if (BirdSizeReferences.ContainsKey(birdName) == false) {
                    BirdSizeReferences.Add(birdName, PrefabManager.Instance.GetPrefab(birdName).gameObject.transform.localScale);
                }
                if (ValConfig.EnableScalingBirds.Value == false) {
                    randomBird.transform.localScale = BirdSizeReferences[birdName];
                    continue;
                }

                int storedLevel = randomBird.m_nview.GetZDO().GetInt(SLS_BIRD, 0);
                if (storedLevel > 1) {
                    float scale = 1 + (ValConfig.BirdSizeScalePerLevel.Value * storedLevel);
                    //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                    randomBird.transform.localScale = BirdSizeReferences[birdName] * scale;
                }
            }
            yield break;
        }

        public static IEnumerator UpdateAllFishOnConfigChangeCoroutine() {
            int updated = 0;
            float scalePerLevel = FishScalePerLevel();
            foreach (Fish fish in Resources.FindObjectsOfTypeAll<Fish>()) {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0) {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                if (fish == null || fish.m_nview == null || fish.m_nview.GetZDO() == null) { continue; }
                ItemDrop id = fish.GetComponent<ItemDrop>();
                if (id == null) { continue; }
                // SetQuality resizes through vanilla's own formula, the same one ItemDrop.Load uses when the fish reloads.
                id.m_itemData.m_shared.m_scaleByQuality = scalePerLevel;
                id.SetQuality(id.m_itemData.m_quality);
            }
            yield break;
        }
    }
}
