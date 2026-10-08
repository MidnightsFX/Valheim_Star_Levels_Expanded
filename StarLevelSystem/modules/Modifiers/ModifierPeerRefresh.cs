using HarmonyLib;
using StarLevelSystem.Data;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System;
using System.Collections;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Modifiers {

    // Brings every peer's view of a creature up to date once its owner has given it a modifier through
    // APIReciever.AddModifierToCreature (sls-mod-give and other mods, replayed on the owner by APIOwnerRelay).
    //
    // Only the owner stores the modifier list. Every other peer with the creature loaded keeps the cache entry it built
    // when it set the creature up, and that entry is where a modifier's stat changes live: size is a transform scale each
    // peer sets on its own instance, and damage taken is read from the hitting peer's entry. The only thing that noticed
    // the stored list change was the hud, which rebuilt the entry from the ZDO without running modifier setup. So a Big
    // modifier never grew the creature for anyone else, and a resist modifier did not lower their damage, until the
    // creature reloaded for them.
    //
    // The owner sends this to every peer, itself included, after storing the modifier. Each rebuilds its entry from the
    // ZDO and runs modifier setup on it as the creature's own setup does, the way sls-creature-setstat refreshes a stat
    // (CreatureStatOverrides). Everyone, the owner included, then shows what a reload would, which is also where
    // LimitCreatureModifiersToCreatureStarLevel leaves out a modifier past the creature's star budget.
    //
    // A level the owner gives a creature after setup (an Evolve level-up) has the same problem with no modifier involved.
    // Another peer's live level, m_level, which vanilla reads from s_level only in Character.Awake, follows the ZDO only
    // when its entry is rebuilt, and the hud's check for a new level compares against that same m_level, so nothing
    // noticed. The stars, the per-level speed and the modifiers' per-level power stayed at the old level until the
    // creature reloaded. SendLevel runs the same refresh on every other peer once their ZDO shows the new level.
    internal static class ModifierPeerRefresh {

        private const string RPC_ModifierAdded = "SLS_ModifierAdded";
        private const string RPC_LevelChanged = "SLS_LevelChanged";
        // The call is sent the moment the owner stores the change, so on another peer it usually arrives ahead of the
        // ZDO sync carrying it. That peer waits for the sync, re-checking this often, for up to AwaitStoredSeconds.
        private static readonly WaitForSeconds AwaitStoredPoll = new WaitForSeconds(0.25f);
        private const float AwaitStoredSeconds = 10f;

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class RegisterModifierAddedRPC {
            private static void Postfix(Character __instance) {
                // Same condition vanilla registers its own Character RPCs under.
                if (__instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.GetZDO() == null) { return; }
                __instance.m_nview.Register<string>(RPC_ModifierAdded, (long sender, string modifier) => Receive(__instance, modifier));
                __instance.m_nview.Register<int>(RPC_LevelChanged, (long sender, int level) => ReceiveLevel(__instance, sender, level));
            }
        }

        // Owner only, once it has stored `modifier` on the creature's ZDO. This peer handles it before the call returns.
        internal static void Send(Character chara, string modifier) {
            ZNetView nview = chara != null ? chara.m_nview : null;
            if (nview == null || nview.IsValid() == false || nview.IsOwner() == false) { return; }
            nview.InvokeRPC(ZNetView.Everybody, RPC_ModifierAdded, modifier);
        }

        // Owner only, once it has stored `level` as the creature's s_level and set its own view up at that level. Not
        // needed when the same change also gave the creature a modifier: that Send's refresh waits for the same ZDO
        // sync, which carries the new level too, and every peer then rebuilds at it.
        internal static void SendLevel(Character chara, int level) {
            ZNetView nview = chara != null ? chara.m_nview : null;
            if (nview == null || nview.IsValid() == false || nview.IsOwner() == false) { return; }
            nview.InvokeRPC(ZNetView.Everybody, RPC_LevelChanged, level);
        }

        private static void Receive(Character chara, string modifier) {
            if (chara == null || chara.m_nview == null || chara.m_nview.IsValid() == false) { return; }
            // The rebuild reads the modifier list from this peer's copy of the ZDO. Built before the owner's write reaches
            // it, the entry would lack the modifier. The hud rebuilds it once more when the write does arrive, and that
            // rebuild sets the modifiers up in the entry, but applies no size, speed or health.
            RefreshOnceStored(chara, () => CompositeLazyCache.TryGetCreatureModifier(chara, modifier, out _), $"the added modifier {modifier}");
        }

        private static void ReceiveLevel(Character chara, long sender, int level) {
            if (chara == null || chara.m_nview == null || chara.m_nview.IsValid() == false) { return; }
            // The sender set its own view up when it levelled the creature.
            if (sender == ZNet.GetUID()) { return; }
            // Built before the owner's write reaches this peer, the entry, and with it m_level, would keep the old level.
            // A later level than this one is fine to show: its own call refreshes once more.
            RefreshOnceStored(chara, () => chara.m_nview.GetZDO().GetInt(ZDOVars.s_level, 0) >= level, $"level {level}");
        }

        // Refreshes now if this peer's copy of the ZDO already shows the change (`isStored`), else once it does.
        private static void RefreshOnceStored(Character chara, Func<bool> isStored, string change) {
            if (isStored()) {
                Refresh(chara);
                return;
            }
            TaskRunner.Run().StartCoroutine(AwaitStored(chara, isStored, change));
        }

        private static IEnumerator AwaitStored(Character chara, Func<bool> isStored, string change) {
            float giveUpAt = Time.time + AwaitStoredSeconds;
            while (Time.time < giveUpAt) {
                yield return AwaitStoredPoll;
                // Unloaded meanwhile: it is set up from the ZDO when it next loads here.
                if (chara == null || chara.m_nview == null || chara.m_nview.IsValid() == false) { yield break; }
                if (isStored()) {
                    Refresh(chara);
                    yield break;
                }
            }
            Logger.LogDebug($"{chara.name} did not show {change} within {AwaitStoredSeconds}s, so its view here was not refreshed.");
        }

        private static void Refresh(Character chara) {
            CharacterCacheEntry entry = CompositeLazyCache.GetAndSetLocalCache(chara, updateCache: true);
            // Not set up yet: its setup will build from the ZDO, which already carries the change.
            if (entry == null || entry.Level <= 0) { return; }
            // A fresh entry has not had any modifier's stat changes yet; these add all of them, a new one included, at
            // the stored level (the rebuild moves a non-owner's m_level to it). Damage and health are ZDO values and only
            // change on the owner.
            CreatureModifiers.RunOnceModifierSetup(chara, entry);
            CreatureModifiers.SetupModifiers(chara, entry, entry.CreatureModifiers);
            SpeedModifications.ApplySpeedModifications(chara, entry);
            DamageModifications.ApplyDamageModification(chara, entry);
            SizeModifications.SetSizeModification(chara.gameObject, chara.m_nview, entry, true);
            HealthModifications.ForceApplyHealthModifications(chara, entry);
            UIHudControl.InvalidateCacheEntry(chara);
        }
    }
}
