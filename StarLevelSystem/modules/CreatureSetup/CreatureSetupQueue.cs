using StarLevelSystem.Data;
using StarLevelSystem.Modifiers.Control;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {
    internal static class CreatureSetupQueue {

        internal class PendingSetupEntry {
            public Character Character;
            public uint ZdoId;
            public float ScheduledTime;
            public int LevelOverride;
            public bool SpawnMultiply;
            public Dictionary<string, ModifierType> RequiredModifiers;
            public List<string> NotAllowedModifiers;
            public int Attempts;
        }

        // Pending entries ordered by ScheduledTime (ascending).
        private static readonly List<PendingSetupEntry> Pending = new List<PendingSetupEntry>();
        // Fast lookup for dedup by ZDOID. Entries without a valid ZDOID at enqueue time live only in Pending.
        private static readonly Dictionary<uint, PendingSetupEntry> PendingById = new Dictionary<uint, PendingSetupEntry>();
        // Characters whose setup pipeline has run to completion. Cleared on creature destroy or explicit Invalidate.
        private static readonly HashSet<uint> CompletedSetups = new HashSet<uint>();
        // Characters whose setup coroutine is currently running. Prevents concurrent processing of the same creature.
        private static readonly HashSet<uint> InFlight = new HashSet<uint>();

        private static int _tickFrame = 0;

        // Drives one queue check every other frame.
        internal static void Tick() {
            _tickFrame++;
            if ((_tickFrame & 1) != 0) { return; }
            if (Pending.Count == 0) { return; }

            float now = Time.time;
            // Pending is kept sorted by ScheduledTime; pop from the front while due.
            while (Pending.Count > 0) {
                PendingSetupEntry head = Pending[0];
                if (head.ScheduledTime > now) { break; }
                Pending.RemoveAt(0);

                // Resolve a late ZDOID if we couldn't at enqueue time.
                if (head.ZdoId == 0 && head.Character != null && head.Character.m_nview != null && head.Character.m_nview.IsValid()) {
                    head.ZdoId = head.Character.GetZDOID().ID;
                }
                if (head.ZdoId != 0) { PendingById.Remove(head.ZdoId); }

                if (head.Character == null) { continue; }
                if (head.ZdoId != 0 && CompletedSetups.Contains(head.ZdoId)) { continue; }
                if (head.ZdoId != 0 && InFlight.Contains(head.ZdoId)) { continue; }

                if (head.ZdoId != 0) { InFlight.Add(head.ZdoId); }
                TaskRunner.Run().StartCoroutine(ProcessEntry(head));
            }
        }

        // Adds a creature to the queue. Returns false when the request is a duplicate.
        internal static bool Enqueue(Character chara, int levelOverride, bool spawnMultiply, float delay,
            Dictionary<string, ModifierType> requiredModifiers, List<string> notAllowedModifiers) {
            if (chara == null) { return false; }
            if (chara.IsPlayer()) { return false; }

            uint id = 0;
            if (chara.m_nview != null && chara.m_nview.IsValid() && chara.m_nview.GetZDO() != null) {
                id = chara.GetZDOID().ID;
                if (CompletedSetups.Contains(id)) { return false; }
                if (PendingById.ContainsKey(id)) { return false; }
                if (InFlight.Contains(id)) { return false; }
            }

            if (delay < 0f) { delay = 0f; }
            PendingSetupEntry entry = new PendingSetupEntry {
                Character = chara,
                ZdoId = id,
                ScheduledTime = Time.time + delay,
                LevelOverride = levelOverride,
                SpawnMultiply = spawnMultiply,
                RequiredModifiers = requiredModifiers,
                NotAllowedModifiers = notAllowedModifiers,
                Attempts = 0,
            };

            InsertSorted(entry);
            if (id != 0) { PendingById[id] = entry; }
            return true;
        }

        // Clears completion/queue state for a ZDOID so a re-setup can be scheduled.
        internal static void Invalidate(uint id) {
            if (id == 0) { return; }
            CompletedSetups.Remove(id);
            if (PendingById.TryGetValue(id, out PendingSetupEntry existing)) {
                PendingById.Remove(id);
                Pending.Remove(existing);
            }
        }

        internal static void Invalidate(Character chara) {
            if (chara == null || chara.m_nview == null || chara.m_nview.GetZDO() == null) { return; }
            Invalidate(chara.GetZDOID().ID);
        }

        // Cleanup hook for destroyed creatures - drops all tracking.
        internal static void RemoveTracking(uint id) {
            if (id == 0) { return; }
            CompletedSetups.Remove(id);
            InFlight.Remove(id);
            if (PendingById.TryGetValue(id, out PendingSetupEntry existing)) {
                PendingById.Remove(id);
                Pending.Remove(existing);
            }
        }

        internal static void Flush() {
            Pending.Clear();
            PendingById.Clear();
            CompletedSetups.Clear();
            InFlight.Clear();
        }

        private static void InsertSorted(PendingSetupEntry entry) {
            int lo = 0;
            int hi = Pending.Count;
            while (lo < hi) {
                int mid = (lo + hi) >> 1;
                if (Pending[mid].ScheduledTime <= entry.ScheduledTime) { lo = mid + 1; } else { hi = mid; }
            }
            Pending.Insert(lo, entry);
        }

        // Per-creature setup worker. Waits for a valid ZNetView, prepares the cache, and runs CharacterSetup.
        // Mirrors the prior DelayedSetupValidateZnet retry behavior but only for one popped entry.
        private static IEnumerator ProcessEntry(PendingSetupEntry entry) {
            int maxAttempts = ValConfig.FallbackDelayBeforeCreatureSetup.Value;
            float retryDelay = 1f;
            bool success = false;
            int attempts = 0;

            while (success == false) {
                if (entry.Character == null) { break; }
                Character chara = entry.Character;

                if (chara.m_nview == null || chara.m_nview.IsValid() == false) {
                    yield return new WaitForSeconds(retryDelay);
                    attempts++;
                    if (attempts >= maxAttempts) { break; }
                    continue;
                }

                if (chara.m_nview.m_zdo.Owned == false) { chara.m_nview.ClaimOwnership(); }

                CharacterCacheEntry cce;
                if (chara.m_nview.IsOwner()) {
                    cce = CompositeLazyCache.GetAndSetLocalCache(chara, entry.LevelOverride, entry.RequiredModifiers, entry.NotAllowedModifiers);
                    CompositeLazyCache.StartZOwnerCreatureRoutines(chara, cce, entry.SpawnMultiply);
                }

                cce = CompositeLazyCache.GetCacheEntry(chara);
                success = CreatureSetupControl.RunCharacterSetup(chara, cce);

                if (success == false) {
                    attempts++;
                    if (attempts >= maxAttempts - 1) {
                        // Fallback - force a fresh cache + zowner routine, then setup.
                        CharacterCacheEntry fallback = CompositeLazyCache.GetAndSetLocalCache(chara, entry.LevelOverride, entry.RequiredModifiers, entry.NotAllowedModifiers);
                        CompositeLazyCache.StartZOwnerCreatureRoutines(chara, fallback, entry.SpawnMultiply);
                        success = CreatureSetupControl.RunCharacterSetup(chara, fallback);
                        if (success) { Logger.LogDebug($"{fallback.RefCreatureName} running delayed setup."); }
                    }
                    if (attempts >= maxAttempts) { break; }
                    if (success == false) {
                        yield return new WaitForSeconds(retryDelay);
                    }
                }
            }

            uint id = entry.ZdoId;
            if (id == 0 && entry.Character != null && entry.Character.m_nview != null && entry.Character.m_nview.IsValid() && entry.Character.m_nview.GetZDO() != null) {
                id = entry.Character.GetZDOID().ID;
            }
            if (id != 0) {
                InFlight.Remove(id);
                if (success) { CompletedSetups.Add(id); }
            }
        }
    }
}
