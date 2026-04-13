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

        internal static void ApplySizeModifications(GameObject creature, CharacterCacheEntry cDetails, bool force_update = false, float bonus = 0f) {
            // Don't scale in dungeons
            if (creature.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false || cDetails == null) {
                return;
            }

            float current_size = creature.transform.localScale.x;
            float scale = bonus + cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size] + (cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel] * cDetails.Level);
            if (force_update == true || scale != current_size) {
                Vector3 creature_size = GetSizeReferenceForObject(creature.name);
                Vector3 sizeEstimate = creature_size * scale;
                creature.transform.localScale = sizeEstimate;
                //Logger.LogDebug($"Applying size modification {creature.name} refsize: {creature_size} | {bonus} + {cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size]} + ({cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel]} * {cDetails.Level}) => {sizeEstimate} | saved: {current_size}");
                creature.transform.localScale = sizeEstimate;
                UpdateRidingCreaturesForSizeScaling(creature, cDetails);
                Physics.SyncTransforms();
                return;
            }
        }

        internal static void ApplyWeaponSizeModifications(GameObject weapon, GameObject creature, CharacterCacheEntry cDetails) {
            if (weapon == null || cDetails == null) { return; }
            // We only want to apply scaling for weapons that are added AFTER the initial character size change
            if (creature.transform.localScale == GetSizeReferenceForObject(cDetails.RefCreatureName)) { return; }
            float scale = cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size] + (cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel] * cDetails.Level);
            Vector3 sizeEstimate = weapon.transform.localScale * scale;
            weapon.transform.localScale = sizeEstimate;
            //Logger.LogDebug($"Applying weapon size modification {weapon.name} | {cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size]} + ({cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel]} * {cDetails.Level}) => {sizeEstimate}");
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
