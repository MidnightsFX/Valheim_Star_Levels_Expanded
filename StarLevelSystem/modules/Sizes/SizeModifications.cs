using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Sizes {
    internal static class SizeModifications {

        private static Dictionary<string, Vector3> SizeEstimateCache = new Dictionary<string, Vector3>();
        internal static void SetSizeModification(GameObject obj, ZNetView zview, CharacterCacheEntry cdetails, bool update = false, float bonus = 0f) {
            Vector3 size = zview.m_zdo.GetVec3(ZDOVars.s_scaleHash, Vector3.zero);

            // Size setting exists and we are not updating it
            if (update == false && size != Vector3.zero) {
                obj.transform.localScale = size;
                UpdateRidingCreaturesForSizeScaling(obj, cdetails);
                Physics.SyncTransforms();
                return;
            }

            // Set or update the size
            float scale = bonus + cdetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size] + (cdetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel] * cdetails.Level);
            Vector3 creatureScale = (GetSizeReferenceForObject(obj.name) * scale);
            UpdateRidingCreaturesForSizeScaling(obj, cdetails);
            zview.m_zdo.Set(ZDOVars.s_scaleHash, creatureScale);
            Logger.LogDebug($"Setting size of {cdetails.RefCreatureName} to {creatureScale}");
            Physics.SyncTransforms();
        }

        internal static Vector3 GetSizeReferenceForObject(string name) {
            Vector3 objSize;
            string objectName = Utils.GetPrefabName(name);
            if (SizeEstimateCache.ContainsKey(objectName)) {
                objSize = SizeEstimateCache[objectName];
            } else {
                objSize = PrefabManager.Instance.GetPrefab(objectName).transform.localScale;
                SizeEstimateCache.Add(objectName, objSize);
            }
            return objSize;
        }

        // This is important because some creatures or objects have their sizes adjusted during runtime, we want all of the original sizes
        internal static void PrepareSizeRefCache() {
            string clone = "(Clone)";

            foreach (Character chargo in Resources.FindObjectsOfTypeAll<Character>().Where(obj => obj.name.EndsWith(clone) == false).ToList()) {
                if (SizeEstimateCache.ContainsKey(chargo.name)) { continue; }
                SizeEstimateCache.Add(chargo.name, chargo.transform.localScale);
            }

            foreach (Humanoid humgo in Resources.FindObjectsOfTypeAll<Humanoid>().Where(obj => obj.name.EndsWith(clone) == false).ToList()) {
                if (SizeEstimateCache.ContainsKey(humgo.name)) { continue; }
                SizeEstimateCache.Add(humgo.name, humgo.transform.localScale);
            }

            foreach (GameObject itemgo in ObjectDB.m_instance.m_items) {
                if (SizeEstimateCache.ContainsKey(itemgo.name)) { continue; }
                SizeEstimateCache.Add(itemgo.name, itemgo.transform.localScale);
            }

        }

        internal static void UpdateRidingCreaturesForSizeScaling(GameObject creature, CharacterCacheEntry cDetails) {
            if (ValConfig.EnableRidableCreatureSizeFixes.Value == false) { return; }
            // Handle tame specific collider scaling
            Tameable tame = creature.GetComponent<Tameable>();
            if (tame != null && tame.IsTamed()) {
                string name = Utils.GetPrefabName(creature.gameObject);
                //Logger.LogDebug($"Checking Tame collider adjustment for {name} with for level {cDetails.Level}");
                if (name == "Lox") {
                    UpdateLoxCollider(creature.gameObject, cDetails);
                }
                if (name == "Askvin") {
                    UpdateAskavinCollider(creature.gameObject);
                }
            }
        }

        private static void UpdateLoxCollider(GameObject go, CharacterCacheEntry cDetails) {
            CapsuleCollider loxcc = go.GetComponent<CapsuleCollider>();
            float size_set = (cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel] * cDetails.Level) + cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size];
            float levelchange = (size_set - 1) * 0.1555f;
            //float levelchange = cDetails.Level * 0.016f;  // 3.31 -lvl 20 (size 3), 3.15 -lvl 10 (size 2) or 0.016f per level at default sizing
            loxcc.height = 3f + levelchange;
            loxcc.radius = 0.5f; //1.22?
        }

        private static void UpdateAskavinCollider(GameObject go) {
            CapsuleCollider askcc = go.GetComponent<CapsuleCollider>();
            askcc.radius = 0.842f;
        }

        internal static void StarLevelScaleChanged(object s, EventArgs e) {
            // This might need to be async
            Logger.LogInfo($"Updating size scale: {ValConfig.PerLevelScaleBonus.Value}");
            foreach (var chara in Resources.FindObjectsOfTypeAll<Character>()) {
                chara.transform.localScale = Vector3.one;
                float scale = 1 + (ValConfig.PerLevelScaleBonus.Value * (chara.m_level - 1));
                //Logger.LogDebug($"Setting {chara.name} size {scale}.");
                chara.transform.localScale *= scale;
            }
            Physics.SyncTransforms();
        }
    }
}
