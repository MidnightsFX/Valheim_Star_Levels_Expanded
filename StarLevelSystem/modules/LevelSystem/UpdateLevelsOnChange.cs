using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.modules.Sizes;
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

        public static void ModifyLoadedCreatureLevels(object s, EventArgs e) {
            // Do not run before the area is loaded
            if (Player.m_localPlayer == null) { return; }
            if (ZNetScene.instance.IsAreaReady(Player.m_localPlayer.gameObject.transform.position) == false) { return; }
            TaskRunner.Run().StartCoroutine(ModifyLoadedCreaturesLevels());
        }

        public static IEnumerator ModifyLoadedCreaturesLevels() {
            int updated = 0;
            IEnumerable<GameObject> creatures = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.GetComponent<Character>() != null || obj.GetComponent<Humanoid>());
            foreach (GameObject creature in creatures) {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0) {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                if (creature == null) { continue; }
                Character chara = creature.GetComponent<Character>();
                if (chara == null) { chara = creature.GetComponent<Humanoid>(); }
                if (chara == null || chara.m_nview == null || chara.m_nview.GetZDO() == null) { continue; }

                if (chara.GetLevel() <= ValConfig.MaxLevel.Value) { continue; }
                //CompositeLazyCache.ClearCachedCreature(chara);
                CharacterCacheEntry cce = CompositeLazyCache.GetAndSetLocalCache(chara, updateCache: true);

                //cce.Level = LevelSystem.DetermineLevel(chara, chara.m_nview.GetZDO(), cce.CreatureSettings, cce.BiomeSettings);
                //CompositeLazyCache.UpdateCharacterCacheEntry(chara, cce);
                //CompositeLazyCache.StartZOwnerCreatureRoutines(chara, cce, false);
                SizeModifications.ApplySizeModifications(chara.gameObject, cce, force_update: true);
                ModificationExtensionSystem.CreatureSetup(chara, cce.Level);
                //LevelUI.InvalidateCacheEntry(chara);
            }
            yield break;
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
                TreeBase treebase = tree.GetComponent<TreeBase>();
                if (treebase == null || treebase.m_nview == null || treebase.m_nview.GetZDO() == null) { continue; }
                string treename = Utils.GetPrefabName(tree.gameObject);
                // Check scalar objects, or fall back to the reference prefab scale
                Vector3 baseSize = treebase.m_nview.GetZDO().GetVec3(ZDOVars.s_scaleHash, Vector3.zero);
                if (baseSize == Vector3.zero) {
                    float scaler = treebase.m_nview.GetZDO().GetFloat(ZDOVars.s_scaleScalarHash, 0f);
                    baseSize = new Vector3(scaler, scaler, scaler);
                }
                // Falling back to the reference prefab scale will set tree size to be uniform, which will likely be adjusted when reloaded
                if (baseSize == Vector3.zero) {
                    baseSize = PrefabManager.Instance.GetPrefab(treename).gameObject.transform.localScale;
                }
                if (ValConfig.EnableTreeScaling.Value == false) {
                    treebase.transform.localScale = baseSize;
                    continue;
                }

                if (ValConfig.UseDeterministicTreeScaling.Value) {
                    float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * CompositeLazyCache.GetOrAddCachedTreeEntry(treebase.m_nview));
                    treebase.transform.localScale = baseSize * scale;
                } else {
                    int storedLevel = treebase.m_nview.GetZDO().GetInt(SLS_TREE, 0);
                    if (storedLevel > 1) {
                        float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * storedLevel);
                        //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                        treebase.transform.localScale = baseSize * scale;
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
                RandomFlyingBird randombird = bird.GetComponent<RandomFlyingBird>();
                if (randombird == null || randombird.m_nview == null || randombird.m_nview.GetZDO() == null) { continue; }
                string birdname = Utils.GetPrefabName(bird.gameObject);
                if (BirdSizeReferences.ContainsKey(birdname) == false) {
                    BirdSizeReferences.Add(birdname, PrefabManager.Instance.GetPrefab(birdname).gameObject.transform.localScale);
                }
                if (ValConfig.EnableScalingBirds.Value == false) {
                    randombird.transform.localScale = BirdSizeReferences[birdname];
                    continue;
                }

                int storedLevel = randombird.m_nview.GetZDO().GetInt(SLS_BIRD, 0);
                if (storedLevel > 1) {
                    float scale = 1 + (ValConfig.BirdSizeScalePerLevel.Value * storedLevel);
                    //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                    randombird.transform.localScale = BirdSizeReferences[birdname] * scale;
                }
            }
            yield break;
        }

        public static IEnumerator UpdateAllFishOnConfigChangeCoroutine() {
            int updated = 0;
            Dictionary<string, Vector3> FishSizeReference = new Dictionary<string, Vector3>();
            IEnumerable<GameObject> loadedFish = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.GetComponent<Fish>() != null);
            foreach (GameObject fish in loadedFish) {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0) {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                Fish fishComp = fish.GetComponent<Fish>();
                if (fishComp == null || fishComp.m_nview == null || fishComp.m_nview.GetZDO() == null) { continue; }
                string fishname = Utils.GetPrefabName(fish.gameObject);
                if (FishSizeReference.ContainsKey(fishname) == false) {
                    FishSizeReference.Add(fishname, PrefabManager.Instance.GetPrefab(fishname).gameObject.transform.localScale);
                }
                if (ValConfig.EnableScalingFish.Value == false) {
                    fishComp.transform.localScale = FishSizeReference[fishname];
                    continue;
                }

                int storedLevel = fishComp.m_nview.GetZDO().GetInt(SLS_FISH, 0);
                if (storedLevel > 1) {
                    float scale = 1 + (ValConfig.FishSizeScalePerLevel.Value * storedLevel);
                    //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                    fishComp.transform.localScale = FishSizeReference[fishname] * scale;
                    continue;
                }
                ItemDrop id = fish.GetComponent<ItemDrop>();
                if (id.m_itemData.m_quality > 1) {
                    float scale = 1 + (ValConfig.FishSizeScalePerLevel.Value * id.m_itemData.m_quality);
                    //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                    fishComp.transform.localScale = FishSizeReference[fishname] * scale;
                    id.m_itemData.m_shared.m_scaleByQuality = ValConfig.FishSizeScalePerLevel.Value;
                    id.Save();
                }
            }
            yield break;
        }
    }
}
