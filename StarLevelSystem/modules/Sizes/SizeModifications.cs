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

        internal static void ApplySaveSizeModifications(GameObject creature, ZNetView zview, CharacterCacheEntry cDetails, bool force_update = false, float bonus = 0f) {
            // Don't scale in dungeons
            if (creature.transform.position.y > 3000f && ValConfig.EnableScalingInDungeons.Value == false || cDetails == null) {
                return;
            }

            float current_size = creature.transform.localScale.x;
            float scale = bonus + cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size] + (cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel] * cDetails.Level);
            if (force_update == true || scale != current_size) {
                Vector3 creature_size;
                string objectName = Utils.GetPrefabName(creature);
                if (SizeEstimateCache.ContainsKey(objectName)) {
                    creature_size = SizeEstimateCache[objectName];
                } else {
                    creature_size = PrefabManager.Instance.GetPrefab(objectName).transform.localScale;
                    SizeEstimateCache.Add(objectName, creature_size);
                }

                Vector3 sizeEstimate = creature_size * scale;
                creature.transform.localScale = sizeEstimate;
                //Logger.LogDebug($"Applying size modification {creature.name} {bonus} + {cDetails.CreatureBaseValueModifiers[CreatureBaseAttribute.Size]} + ({cDetails.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel]} * {cDetails.Level}) => {sizeEstimate} | saved: {current_size}");
                creature.transform.localScale = sizeEstimate;
                UpdateRidingCreaturesForSizeScaling(creature, cDetails);
                Physics.SyncTransforms();
                return;
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
    }
}
