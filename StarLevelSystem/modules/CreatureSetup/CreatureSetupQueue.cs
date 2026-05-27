using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Control;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {
    internal static class CreatureSetupQueue {

        private static readonly HashSet<uint> InProgress = new HashSet<uint>();

        // Adds a creature to the queue. Returns false when the request is a duplicate
        // (a setup coroutine is already running for this creature) or the character is invalid.
        internal static bool Enqueue(Character chara, int levelOverride, bool spawnMultiply, float delay, Dictionary<string, ModifierType> requiredModifiers, List<string> notAllowedModifiers) {
            if (chara == null) { return false; }
            if (chara.IsPlayer()) { return false; }
            if (chara.m_nview == null || chara.m_nview.IsValid() == false) { return false; }

            uint id = chara.GetZDOID().ID;
            if (id == 0) { return false; }
            if (InProgress.Add(id) == false) { return false; }

            TaskRunner.Run().StartCoroutine(ProcessEntry(chara, id, levelOverride, spawnMultiply, delay, requiredModifiers, notAllowedModifiers));
            return true;
        }


        // Cleanup hook for destroyed creatures - drops all tracking.
        internal static void RemoveTracking(uint id) {
            if (id == 0) { return; }
            InProgress.Remove(id);
        }

        // Per-creature setup worker. Waits for the requested delay, then waits for a valid ZNetView,
        // prepares the cache, and runs CharacterSetup. Retries up to FallbackDelayBeforeCreatureSetup
        // attempts before giving up.
        private static IEnumerator ProcessEntry(Character chara, uint id, int levelOverride, bool spawnMultiply, float delay, Dictionary<string, ModifierType> requiredModifiers, List<string> notAllowedModifiers) {
            if (delay > 0f) {
                yield return new WaitForSeconds(delay);
            }

            int maxAttempts = ValConfig.FallbackDelayBeforeCreatureSetup.Value;
            float retryDelay = 1f;
            bool success = false;
            int attempts = 0;
            bool ownershipClaimAttempted = false;

            while (success == false) {
                if (chara == null) { break; }

                if (chara.m_nview == null || chara.m_nview.IsValid() == false) {
                    yield return new WaitForSeconds(retryDelay);
                    attempts++;
                    if (attempts >= maxAttempts) { break; }
                    continue;
                }

                // FGN Server-Authority compat: FGN's ReleaseNearbyZDOS_Prefix constantly forces ZDO ownership
                // back to the server. If we claim here on a client, we just kick off an ownership ping-pong that
                // can cause the cache build to read a stale (empty) SLS_MODSV2 before the server's write replicates,
                // which then re-rolls and clobbers the persisted modifiers. When FGN+SA is configured, let the
                // server be the sole ZOwner driver.
                bool deferToServerOwner = Compatibility.IsFGNEnabled && ZNet.instance != null && !ZNet.instance.IsServer();
                if (!deferToServerOwner && !ownershipClaimAttempted && chara.m_nview.m_zdo.Owned == false) {
                    chara.m_nview.ClaimOwnership();
                    ownershipClaimAttempted = true;
                }

                CharacterCacheEntry cce;
                if (chara.m_nview.IsOwner() || ValConfig.ForceControlAllSpawns.Value) {
                    cce = CompositeLazyCache.GetAndSetLocalCache(chara, levelOverride, requiredModifiers, notAllowedModifiers);
                    CompositeLazyCache.StartZOwnerCreatureRoutines(chara, cce, spawnMultiply);
                } else {
                    cce = CompositeLazyCache.GetCacheEntry(chara);
                }

                success = CreatureSetupControl.RunCharacterSetup(chara, cce);

                if (success == false) {
                    attempts++;
                    if (attempts == maxAttempts - 1) {
                        // Fallback - force a fresh cache + zowner routine, then setup.
                        CharacterCacheEntry fallback = CompositeLazyCache.GetAndSetLocalCache(chara, levelOverride, requiredModifiers, notAllowedModifiers, updateCache: true);
                        CompositeLazyCache.StartZOwnerCreatureRoutines(chara, fallback, spawnMultiply);
                        success = CreatureSetupControl.RunCharacterSetup(chara, fallback);
                        if (success) { Logger.LogDebug($"{fallback.RefCreatureName} running delayed setup."); }
                    }
                    if (attempts >= maxAttempts) { break; }
                    if (success == false) {
                        yield return new WaitForSeconds(retryDelay);
                    }
                }
            }

            if (id != 0) { InProgress.Remove(id); }
        }
    }
}
