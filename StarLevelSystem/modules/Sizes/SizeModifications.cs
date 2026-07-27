using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Sizes {
    internal static class SizeModifications {

        private static readonly Dictionary<string, Vector3> SizeEstimateCache = new Dictionary<string, Vector3>();

        // Set while a config/YAML reload is resizing every loaded creature. SetSizeModification normally
        // short-circuits on the persisted SLS_SIZE, which would otherwise re-apply the stale size and make
        // a changed Size/SizePerLevel appear to do nothing until the creature respawns.
        // Mirrors HealthModifications.ForceUpdateHealth.
        internal static bool ForceUpdateSize = false;

        // The scale multiplier for a creature, before it is applied to the prefab's reference scale.
        // Clamped so a negative SizePerLevel shrinks creatures toward MinimumCreatureScale instead of
        // passing through zero into negative (mirrored/inside-out) territory. The clamp also keeps the
        // result well clear of Vector3.zero, which SetSizeModification uses as its "not sized yet" sentinel
        // (Unity's Vector3 == is an epsilon compare, so near-zero would read back as unset).
        internal static float DetermineScaleMultiplier(CharacterCacheEntry characterCache, float bonus = 0f) {
            // Level - 1, matching health/damage/speed: a 0 star creature sits at exactly the base Size.
            int levels = Mathf.Max(0, characterCache.Level - 1);
            float perLevelSize = ValConfig.EnableCreatureScalingPerLevel.Value
                ? characterCache.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel] * levels
                : 0f;
            float scale = bonus + characterCache.CreatureBaseValueModifiers[CreatureBaseAttribute.Size] + perLevelSize;
            return Mathf.Max(ValConfig.MinimumCreatureScale.Value, scale);
        }

        internal static void SetSizeModification(GameObject obj, ZNetView zview, CharacterCacheEntry characterCache, bool update = false, float bonus = 0f) {
            Vector3 size = zview.m_zdo.GetVec3(SLS_SIZE, Vector3.zero);

            // Size setting exists and we are not updating it
            if (update == false && ForceUpdateSize == false && size != Vector3.zero) {
                obj.transform.localScale = size;
                UpdateRidingCreaturesForSizeScaling(obj, characterCache);
                Physics.SyncTransforms();
                return;
            }

            // Skip size scaling for creatures inside dungeons/interiors.
            // Valheim places interiors at world y+5000; Character.InInterior() is y > 3000.
            if (ValConfig.EnableScalingInDungeons.Value == false && Character.InInterior(obj.transform.position)) {
                return;
            }

            // Set or update the size
            float scale = DetermineScaleMultiplier(characterCache, bonus);
            Vector3 creatureScale = (GetSizeReferenceForObject(obj.name) * scale);
            obj.transform.localScale = creatureScale;
            UpdateRidingCreaturesForSizeScaling(obj, characterCache);
            zview.m_zdo.Set(SLS_SIZE, creatureScale);
            //Logger.LogDebug($"Setting size of {obj.name} using ref {cdetails.RefCreatureName} to {creatureScale}");
            Physics.SyncTransforms();
        }

        internal static Vector3 GetSizeReferenceForObject(string name) {
            Vector3 objSize;
            string objectName = Utils.GetPrefabName(name);
            if (SizeEstimateCache.ContainsKey(objectName)) {
                objSize = SizeEstimateCache[objectName];
            } else {
                // Unregistered or modded prefab names reach this; treat them as unit scale rather than NREing
                // inside the setup pipeline (which would abort every remaining step for that creature).
                GameObject prefab = PrefabManager.Instance.GetPrefab(objectName);
                if (prefab == null) {
                    Logger.LogWarning($"No prefab found for '{objectName}', assuming a reference scale of 1.");
                    objSize = Vector3.one;
                } else {
                    objSize = prefab.transform.localScale;
                }
                SizeEstimateCache.Add(objectName, objSize);
            }
            return objSize;
        }

        // This is important because some creatures or objects have their sizes adjusted during runtime, we want all of the original sizes
        internal static void PrepareSizeRefCache() {
            string clone = "(Clone)";

            foreach (Character charGO in Resources.FindObjectsOfTypeAll<Character>().Where(obj => obj.name.EndsWith(clone) == false).ToList()) {
                if (SizeEstimateCache.ContainsKey(charGO.name)) { continue; }
                SizeEstimateCache.Add(charGO.name, charGO.transform.localScale);
            }

            foreach (Humanoid humGO in Resources.FindObjectsOfTypeAll<Humanoid>().Where(obj => obj.name.EndsWith(clone) == false).ToList()) {
                if (SizeEstimateCache.ContainsKey(humGO.name)) { continue; }
                SizeEstimateCache.Add(humGO.name, humGO.transform.localScale);
            }

            foreach (GameObject itemGO in ObjectDB.m_instance.m_items) {
                if (SizeEstimateCache.ContainsKey(itemGO.name)) { continue; }
                SizeEstimateCache.Add(itemGO.name, itemGO.transform.localScale);
            }

            foreach (Ragdoll ragDollGO in Resources.FindObjectsOfTypeAll<Ragdoll>().Where(obj => obj.name.EndsWith(clone) == false).ToList()) {
                if (SizeEstimateCache.ContainsKey(ragDollGO.name)) { continue; }
                SizeEstimateCache.Add(ragDollGO.name, ragDollGO.transform.localScale);
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
            CapsuleCollider loxCC = go.GetComponent<CapsuleCollider>();
            if (loxCC == null) { return; }
            // Uses the same clamped multiplier as the visual scale so a shrunk lox stays consistent.
            float size_set = DetermineScaleMultiplier(cDetails);
            float levelChange = (size_set - 1) * 0.1555f;
            //float levelChange = cDetails.Level * 0.016f;  // 3.31 -lvl 20 (size 3), 3.15 -lvl 10 (size 2) or 0.016f per level at default sizing
            loxCC.height = Mathf.Max(0.1f, 3f + levelChange);
            loxCC.radius = 0.5f; //1.22?
        }

        private static void UpdateAskavinCollider(GameObject go) {
            CapsuleCollider askCC = go.GetComponent<CapsuleCollider>();
            askCC.radius = 0.842f;
        }

        // Re-runs the real setup pipeline over every live creature so the new scale settings take effect
        // immediately. This deliberately does NOT resize transforms itself: doing so used a second, divergent
        // formula that ignored per-biome/per-creature Size values, never wrote SLS_SIZE (so the next setup
        // pass reverted it), and - because Resources.FindObjectsOfTypeAll also returns non-instantiated
        // prefab assets - permanently corrupted the prefab scales that SizeEstimateCache is built from.
        internal static void StarLevelScaleChanged(object s, EventArgs e) {
            if (ValConfig.EnableCreatureScalingPerLevel.Value == false) { return; }
            Logger.LogInfo($"Updating size scale: {ValConfig.PerLevelScaleBonus.Value} (minimum {ValConfig.MinimumCreatureScale.Value})");
            // Live instances only - a prefab asset has no valid ZNetView.
            List<Character> liveCharacters = Resources.FindObjectsOfTypeAll<Character>()
                .Where(chara => chara != null && chara.m_nview != null && chara.m_nview.IsValid())
                .ToList();
            ForceUpdateSize = true;
            TaskRunner.Run().StartCoroutine(ResizeLoadedCreatures(liveCharacters));
        }

        private static IEnumerator ResizeLoadedCreatures(List<Character> characters) {
            try {
                yield return LevelSystemData.UpdateCreatureAttributes(characters);
            }
            finally {
                ForceUpdateSize = false;
            }
        }
    }
}
