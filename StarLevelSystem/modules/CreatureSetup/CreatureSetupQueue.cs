using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {
    internal static class CreatureSetupQueue {

        // Keyed by the full ZDOID (not the bare uint ZDOID.ID) so setup tracking for creatures created
        // by different peers with overlapping per-peer ID counters does not collide. See CompositeLazyCache.
        // The value is the instance the running worker serves. A worker can outlive its instance by up to one
        // poll, and if the creature streams back in during that window the new instance gets its own worker; the
        // old one must then not release the new one's entry on its way out (see the finally in ProcessEntry).
        private static readonly Dictionary<ZDOID, Character> InProgress = new Dictionary<ZDOID, Character>(ZDOIDComparer.Instance);

        // How often a worker re-checks a creature it cannot set up yet (no owner, or an owner whose roll has not
        // replicated). A client keeps tens of these loaded at the edge of its area for as long as it stays there,
        // so this is a timed wait rather than a per-frame check. It is also the longest a creature this machine
        // has just been handed stays unrolled, which is under the server's own 2 s ownership pass.
        private static readonly WaitForSeconds AwaitRollPoll = new WaitForSeconds(1f);

        // Adds a creature to the queue. Returns false when the request is a duplicate
        // (a setup coroutine is already running for this creature) or the character is invalid.
        internal static bool Enqueue(Character chara, int levelOverride, bool spawnMultiply, float delay, Dictionary<string, ModifierType> requiredModifiers, List<string> notAllowedModifiers) {
            if (chara == null) { return false; }
            if (chara.IsPlayer()) { return false; }
            if (chara.m_nview == null || chara.m_nview.IsValid() == false) { return false; }

            ZDOID id = chara.GetZDOID();
            if (id == ZDOID.None) { return false; }
            if (InProgress.TryGetValue(id, out Character running) && ReferenceEquals(running, chara)) { return false; }
            InProgress[id] = chara;

            TaskRunner.Run().StartCoroutine(ProcessEntry(chara, id, levelOverride, spawnMultiply, delay, requiredModifiers, notAllowedModifiers));
            return true;
        }


        // Cleanup hook for destroyed creatures - drops all tracking.
        internal static void RemoveTracking(ZDOID id) {
            if (id == ZDOID.None) { return; }
            InProgress.Remove(id);
        }

        // Per-creature setup worker. Waits for the requested delay, then waits for a valid ZNetView and for a
        // rolled level/modifier set it may show (or its own ownership, to roll one), prepares the cache, and runs
        // CharacterSetup. Only the setup itself is retried up to FallbackDelayBeforeCreatureSetup attempts.
        //
        // This never takes ownership of the creature. Owners come from the server alone: ZDOMan.ReleaseNearbyZDOS
        // hands every unowned persistent ZDO in a player's active area to that player every 2 s, in single player
        // too (the host runs it for itself). An unowned creature is invisible anyway, since Character.CustomFixedUpdate
        // calls SetVisible(zdo.HasOwner()), so waiting for the server costs nothing a player can see. Claiming here
        // used to make every client that loaded an unowned creature at the edge of its area (outside the active area,
        // where the server does not consider it present) take the creature, which the server then took back or gave
        // to someone else, and the next client to load it claimed it again.
        private static IEnumerator ProcessEntry(Character chara, ZDOID id, int levelOverride, bool spawnMultiply, float delay, Dictionary<string, ModifierType> requiredModifiers, List<string> notAllowedModifiers) {
            try {
            if (delay > 0f) {
                yield return new WaitForSeconds(delay);
            }

            int maxAttempts = ValConfig.FallbackDelayBeforeCreatureSetup.Value;
            float retryDelay = 1f;
            bool success = false;
            int attempts = 0;
            bool awaitedOwner = false;

            while (success == false) {
                // ZNetScene destroys the instance when the creature leaves this machine's loaded area or dies,
                // which is what ends a worker that is still waiting below.
                if (chara == null) { break; }

                if (chara.m_nview == null || chara.m_nview.IsValid() == false) {
                    yield return new WaitForSeconds(retryDelay);
                    attempts++;
                    if (attempts >= maxAttempts) { break; }
                    continue;
                }

                // Nothing to show until the owner has rolled, and only the owner may roll (strict ZDO-owner authority).
                // Either nobody owns the creature yet or its owner's roll has not replicated, so wait, spending no
                // attempts: the worker ends only with the instance. Re-checked every poll, so the pass after this
                // machine becomes the owner (the server assigns it, or anything else hands it over) takes the owner
                // branch below and rolls. A creature that already carries a roll skips this, owned or not, and is set
                // up from its ZDO at once.
                bool isOwner = chara.m_nview.IsOwner();
                if (isOwner == false && levelOverride <= 0 && AwaitsOwnerPass(chara, chara.m_nview.GetZDO())) {
                    awaitedOwner = true;
                    yield return AwaitRollPoll;
                    continue;
                }

                // ForceControlAllSpawns keeps its old meaning here: a non-owner still reaches the owner routines,
                // which return early for it (StartZOwnerCreatureRoutines checks IsZOwner itself).
                bool isRoller = isOwner || ValConfig.ForceControlAllSpawns.Value;
                CharacterCacheEntry cce;
                if (isRoller) {
                    cce = CompositeLazyCache.GetAndSetLocalCache(chara, levelOverride, requiredModifiers, notAllowedModifiers);
                    CompositeLazyCache.StartZOwnerCreatureRoutines(chara, cce, spawnMultiply);
                } else {
                    // Non-owner: build a read-only display cache straight from the synced ZDO. With the
                    // owner-only roll guards this never rolls or writes.
                    cce = CompositeLazyCache.GetAndSetLocalCache(chara, levelOverride, requiredModifiers, notAllowedModifiers);
                }

                success = CreatureSetupControl.RunCharacterSetup(chara, cce);
                if (success && awaitedOwner && Logger.IsDebugEnabled) {
                    Logger.LogDebug($"{cce.RefCreatureName} set up after waiting for its owner's pass ({(isOwner ? "rolled here, as the new owner" : "read from the owner's roll")}).");
                }

                if (success == false) {
                    attempts++;
                    if (attempts == maxAttempts - 1) {
                        // Fallback - force a fresh cache, and (only as the roller) the ZOwner routine, then setup.
                        CharacterCacheEntry fallback = CompositeLazyCache.GetAndSetLocalCache(chara, levelOverride, requiredModifiers, notAllowedModifiers, updateCache: true);
                        if (fallback != null) {
                            if (isRoller) {
                                CompositeLazyCache.StartZOwnerCreatureRoutines(chara, fallback, spawnMultiply);
                            }
                            success = CreatureSetupControl.RunCharacterSetup(chara, fallback);
                            if (success) { Logger.LogDebug($"{fallback.RefCreatureName} running delayed setup."); }
                        }
                    }
                    if (attempts >= maxAttempts) { break; }
                    if (success == false) {
                        yield return new WaitForSeconds(retryDelay);
                    }
                }
            }
            } finally {
                // Always release tracking: a throw anywhere in the setup above (or this coroutine
                // being stopped) would otherwise leave the ZDOID in InProgress forever, and the
                // creature could never be enqueued for setup again this session. Only this worker's own
                // entry, though: a new instance of the same creature may have enqueued since this one died.
                if (id != ZDOID.None && InProgress.TryGetValue(id, out Character tracked) && ReferenceEquals(tracked, chara)) {
                    InProgress.Remove(id);
                }
            }
        }

        // Whether the creature still needs its owner's pass before a non-owner may set it up: no finished roll yet, or
        // a stored level over its current maximum that the owner rerolls (OverLevelCreaturesGetRerolledOnLoad). The
        // second is DetermineLevel's own reroll test with the bound StartZOwnerCreatureRoutines corrects to. Without it
        // a non-owner would set the creature up at the stale level and finish, and since a player loading a creature is
        // almost never its owner yet, nothing would correct it when this machine was handed it a moment later.
        private static bool AwaitsOwnerPass(Character chara, ZDO zdo) {
            if (CompositeLazyCache.HasRolledSetup(zdo) == false) { return true; }
            if (LevelSelection.OverLevelRerollEnabled(chara) == false) { return false; }
            LevelSelection.SelectCreatureBiomeSettings(chara.gameObject, out _, out CreatureSpecificSetting creatureSettings, out BiomeSpecificSetting biomeSettings, out Heightmap.Biome biome);
            return zdo.GetInt(ZDOVars.s_level, 0) > LevelSelection.GetMaxCreatureLevel(chara, creatureSettings, biomeSettings, biome);
        }
    }
}
