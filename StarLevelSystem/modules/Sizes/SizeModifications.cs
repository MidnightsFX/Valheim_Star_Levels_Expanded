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

        // Set while a config/YAML reload resizes one loaded creature (CreatureSetupQueue.RunSetup). SetSizeModification
        // normally short-circuits on the persisted SLS_SIZE, which would otherwise re-apply the stale size and make
        // a changed Size/SizePerLevel appear to do nothing until the creature respawns.
        // Mirrors HealthModifications.ForceUpdateHealth.
        internal static bool ForceUpdateSize = false;

        // SoulEater's growth has no stat of its own: it is this much per death in SLS_SOULEATER, on top of the rest.
        private const float SoulEaterGrowthPerDeath = 0.01f;

        // The scale multiplier for a creature, before it is applied to the prefab's reference scale.
        // Clamped so a negative SizePerLevel shrinks creatures toward MinimumCreatureScale instead of
        // passing through zero into negative (mirrored/inside-out) territory. The clamp also keeps the
        // result well clear of Vector3.zero, which SetSizeModification uses as its "not sized yet" sentinel
        // (Unity's Vector3 == is an epsilon compare, so near-zero would read back as unset).
        // The top is capped at MaxScaleMultiplier, so high stars stacked with Big or a SoulEater's growth stop there.
        internal static float DetermineScaleMultiplier(CharacterCacheEntry characterCache, float bonus = 0f) {
            // Level - 1, matching health/damage/speed: a 0 star creature sits at exactly the base Size.
            int levels = Mathf.Max(0, characterCache.Level - 1);
            float perLevelSize = ValConfig.EnableCreatureScalingPerLevel.Value
                ? characterCache.CreaturePerLevelValueModifiers[CreaturePerLevelAttribute.SizePerLevel] * levels
                : 0f;
            float scale = bonus + characterCache.CreatureBaseValueModifiers[CreatureBaseAttribute.Size] + perLevelSize;
            return Mathf.Clamp(scale, ValConfig.MinimumCreatureScale.Value, MaxScaleMultiplier(characterCache));
        }

        // The largest multiplier this creature may reach: its creature entry's MaxSizeScale when that is set, else the
        // global MaximumCreatureScale. Never below MinimumCreatureScale, so a cap set under the floor pins the creature
        // at the floor rather than inverting the clamp.
        internal static float MaxScaleMultiplier(CharacterCacheEntry characterCache) {
            float cap = ValConfig.MaximumCreatureScale.Value;
            float creatureCap = characterCache?.CreatureSettings?.MaxSizeScale ?? -1f;
            if (creatureCap > 0f) { cap = creatureCap; }
            return Mathf.Max(ValConfig.MinimumCreatureScale.Value, cap);
        }

        // A stored SLS_SIZE held to the creature's current cap. A config reload only resizes creatures that are loaded,
        // so without this one that streamed out while the cap was lowered would come back at its old size.
        private static Vector3 CapStoredSize(GameObject obj, Vector3 size, CharacterCacheEntry characterCache) {
            float reference = GetSizeReferenceForObject(obj.name).x;
            if (reference <= 0f) { return size; }
            float multiplier = size.x / reference;
            float cap = MaxScaleMultiplier(characterCache);
            if (multiplier <= cap || Mathf.Approximately(multiplier, cap)) { return size; }
            return size * (cap / multiplier);
        }

        // SLS_SIZE holds the size SLS chose (reference scale x its multiplier) and never the world's Combat size factor,
        // which is multiplied in wherever a size is put on a transform. The cap and every stored size stay in SLS's own
        // terms that way, and a world whose modifier changes resizes its creatures on their next load.
        internal static void SetSizeModification(GameObject obj, ZNetView zview, CharacterCacheEntry characterCache, bool update = false) {
            Vector3 size = zview.m_zdo.GetVec3(SLS_SIZE, Vector3.zero);

            // Size setting exists and we are not updating it
            if (update == false && ForceUpdateSize == false && size != Vector3.zero) {
                Vector3 capped = CapStoredSize(obj, size, characterCache);
                if (capped != size) {
                    size = capped;
                    if (zview.IsOwner()) { zview.m_zdo.Set(SLS_SIZE, size); }
                }
                Vector3 shown = size * WorldRates.CreatureSizeFactor(obj.transform.position);
                if (obj.transform.localScale != shown) {
                    obj.transform.localScale = shown;
                    TaskRunner.RequestPhysicsSync();
                }
                // Outside the change check: ApplyStoredSize usually set this scale already in Character.Awake, and a
                // tame's collider still needs fitting to it. The collider fixes and the saddle reach all set absolute values
                // (the reach from the prefab's own), so a repeat is harmless.
                UpdateRidingCreaturesForSizeScaling(obj);
                return;
            }

            // Skip size scaling for creatures inside dungeons/interiors.
            // Valheim places interiors at world y+5000; Character.InInterior() is y > 3000.
            if (ValConfig.EnableScalingInDungeons.Value == false && Character.InInterior(obj.transform.position)) {
                return;
            }

            // Set or update the size
            float scale = DetermineScaleMultiplier(characterCache, SoulEaterGrowthPerDeath * zview.m_zdo.GetInt(SLS_SOULEATER, 0));
            Vector3 creatureScale = (GetSizeReferenceForObject(obj.name) * scale);
            Vector3 shownScale = creatureScale * WorldRates.CreatureSizeFactor(obj.transform.position);
            if (obj.transform.localScale != shownScale) {
                obj.transform.localScale = shownScale;
                //Logger.LogDebug($"Setting size of {obj.name} using ref {cdetails.RefCreatureName} to {creatureScale}");
                TaskRunner.RequestPhysicsSync();
            }
            // Outside the change check, like the stored size above: a settings change can turn a mount's fixes on or off at
            // the size it already is.
            UpdateRidingCreaturesForSizeScaling(obj);
            // Only the owner persists the size. A non-owner still applies it to its own instance above, but its write
            // would race the owner's (a SoulEater bonus, say) through vanilla's highest-revision-wins ZDO sync.
            if (zview.IsOwner()) { zview.m_zdo.Set(SLS_SIZE, creatureScale); }
        }

        // Puts a creature that was sized before straight back at that size from Character.Awake. The full setup runs
        // InitialDelayBeforeSetup later, so without this every reloaded creature showed up at vanilla size and then
        // popped. A read of the ZDO with no roll, so it is safe on any peer; the queued setup still runs the stored-size
        // path in SetSizeModification afterwards, which applies the size cap and the tame collider fixes.
        internal static void ApplyStoredSize(Character creature, ZDO zdo) {
            if (zdo == null || creature.IsPlayer()) { return; }
            Vector3 size = zdo.GetVec3(SLS_SIZE, Vector3.zero);
            if (size == Vector3.zero) { return; }
            size *= WorldRates.CreatureSizeFactor(creature.transform.position);
            if (creature.transform.localScale == size) { return; }
            creature.transform.localScale = size;
            TaskRunner.RequestPhysicsSync();
        }

        // The factor this creature is scaled by, read off its transform against the prefab's own scale: SLS's sizing and
        // the world's Combat size factor together, which is what the visual fixes that use it need to match.
        // 1 for a creature nothing has resized, or one whose prefab has no usable reference scale.
        internal static float CurrentScaleMultiplier(GameObject obj) {
            float reference = GetSizeReferenceForObject(obj.name).x;
            if (reference <= 0f) { return 1f; }
            return obj.transform.localScale.x / reference;
        }

        internal static Vector3 GetSizeReferenceForObject(string name) {
            Vector3 objSize;
            string objectName = Utils.GetPrefabName(name);
            if (SizeEstimateCache.ContainsKey(objectName)) {
                objSize = SizeEstimateCache[objectName];
            } else {
                // Unregistered or modded prefab names reach this; treat them as unit scale rather than NREing
                // inside the setup pipeline (which would abort every remaining step for that creature).
                GameObject prefab = PrefabLookup.FindOrWarn(objectName, "its reference size (assuming a scale of 1)");
                if (prefab == null) {
                    objSize = Vector3.one;
                } else {
                    objSize = prefab.transform.localScale;
                }
                SizeEstimateCache.Add(objectName, objSize);
            }
            return objSize;
        }

        // This is important because some creatures or objects have their sizes adjusted during runtime, we want all of the original sizes
        // Runs on every world load. Every entry is checked with Unity's == null: another mod can leave a destroyed prefab in
        // ObjectDB.m_items (seen on the second world load of a session), and reading .name off one threw and abandoned the rest of the cache.
        internal static void PrepareSizeRefCache() {
            string clone = "(Clone)";

            foreach (Character charGO in Resources.FindObjectsOfTypeAll<Character>().Where(obj => obj != null && obj.name.EndsWith(clone) == false).ToList()) {
                if (SizeEstimateCache.ContainsKey(charGO.name)) { continue; }
                SizeEstimateCache.Add(charGO.name, charGO.transform.localScale);
            }

            foreach (Humanoid humGO in Resources.FindObjectsOfTypeAll<Humanoid>().Where(obj => obj != null && obj.name.EndsWith(clone) == false).ToList()) {
                if (SizeEstimateCache.ContainsKey(humGO.name)) { continue; }
                SizeEstimateCache.Add(humGO.name, humGO.transform.localScale);
            }

            if (ObjectDB.m_instance != null) {
                List<GameObject> items = ObjectDB.m_instance.m_items;
                for (int i = 0; i < items.Count; i++) {
                    GameObject itemGO = items[i];
                    if (itemGO == null) {
                        // The entry before it names whose block of items the dead one sits in.
                        string after = i > 0 && items[i - 1] != null ? items[i - 1].name : "nothing";
                        Logger.LogDebug($"Skipping a missing or destroyed ObjectDB item at index {i} (after {after}) while caching reference sizes.");
                        continue;
                    }
                    if (SizeEstimateCache.ContainsKey(itemGO.name)) { continue; }
                    SizeEstimateCache.Add(itemGO.name, itemGO.transform.localScale);
                }
            }

            foreach (Ragdoll ragDollGO in Resources.FindObjectsOfTypeAll<Ragdoll>().Where(obj => obj != null && obj.name.EndsWith(clone) == false).ToList()) {
                if (SizeEstimateCache.ContainsKey(ragDollGO.name)) { continue; }
                SizeEstimateCache.Add(ragDollGO.name, ragDollGO.transform.localScale);
            }

        }

        // Also run when a creature is tamed or saddled (MountScalingPatches): one sized while still wild would otherwise
        // never get the tame collider fit until its size next changed.
        internal static void UpdateRidingCreaturesForSizeScaling(GameObject creature) {
            MountScaling.FitToSize(creature);
        }

        // Re-runs the real setup pipeline over every live creature so the new scale settings take effect
        // immediately. This deliberately does NOT resize transforms itself: doing so used a second, divergent
        // formula that ignored per-biome/per-creature Size values, never wrote SLS_SIZE (so the next setup
        // pass reverted it), and - because Resources.FindObjectsOfTypeAll also returns non-instantiated
        // prefab assets - permanently corrupted the prefab scales that SizeEstimateCache is built from.
        internal static void StarLevelScaleChanged(object s, EventArgs e) {
            if (ValConfig.EnableCreatureScalingPerLevel.Value == false) { return; }
            ResizeLiveCreatures();
        }

        // Resizes whether or not per-star growth is on: the cap holds every creature's size (Big alone can pass it), and
        // turning growth off has to shrink grown creatures back.
        internal static void SizeSettingChanged(object s, EventArgs e) {
            ResizeLiveCreatures();
        }

        // Through LevelSystemData's single pass: one save can change several size settings and the level settings at once.
        private static void ResizeLiveCreatures() {
            Logger.LogInfo($"Updating size scale: {ValConfig.PerLevelScaleBonus.Value} (minimum {ValConfig.MinimumCreatureScale.Value}, maximum {ValConfig.MaximumCreatureScale.Value})");
            // A cache entry holds its own copy of PerLevelScaleBonus, so the pass would size from the old value.
            CompositeLazyCache.FlushCache();
            LevelSystemData.RestartCreatureAttributeUpdate();
        }
    }
}
