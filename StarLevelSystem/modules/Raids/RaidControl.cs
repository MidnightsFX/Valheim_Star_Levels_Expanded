using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Mono.Security.Authenticode;
using PlayFab.ClientModels;
using Splatform;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.LevelSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Heightmap;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.Raids
{
    internal static class RaidControl
    {
        private static Dictionary<string, PlayerRaidData> serverPlayerRaidData = new Dictionary<string, PlayerRaidData>();

        // A missing or empty ServerRaidSavedData.yaml deserializes to null rather than throwing (YamlDotNet maps
        // an empty document to default(T)), which used to leave this null and NRE the entire raid check every
        // interval. Coerce here so no reader has to guard; RaidManager.Setup reports when the fallback was used.
        internal static Dictionary<string, PlayerRaidData> ServerPlayerRaidData {
            get => serverPlayerRaidData;
            set => serverPlayerRaidData = value ?? new Dictionary<string, PlayerRaidData>();
        }

        internal static RaidManager RaidMan;

        internal const string RaidRunnerPrefabName = "RaidRunner";
        internal static GameObject RaidRunnerGO;

        // Raids that have committed but not yet entered their wind-down phase. While any are active, SLS reports an
        // active event (SlsRaidCountsAsActiveEvent) so event creatures keep hunting; once a raid winds down it
        // unregisters here, letting vanilla MonsterAI wander those creatures off and despawn them.
        private static readonly HashSet<RaidRunner> ActiveRaidRunners = new HashSet<RaidRunner>();
        internal static void RegisterActiveRaid(RaidRunner runner) { if (runner != null) { ActiveRaidRunners.Add(runner); } }
        internal static void UnregisterActiveRaid(RaidRunner runner) { if (runner != null) { ActiveRaidRunners.Remove(runner); } }
        internal static bool AnyActiveRaid() {
            // MonsterAI.HuntPlayer() reaches this several times per creature per AI tick via the InEvent postfix,
            // so skip the RemoveWhere walk in the overwhelmingly common no-raid case.
            if (ActiveRaidRunners.Count == 0) { return false; }
            ActiveRaidRunners.RemoveWhere(x => x == null);
            return ActiveRaidRunners.Count > 0;
        }

        // The prefab is a persistent ZNetView holder plus the RaidRunner component. It used to be loaded and
        // instantiated only, never registered, so no machine could rebuild a runner from its ZDO: ZNetScene logged
        // "Missing prefab hash" for every runner ZDO in range on every object pass, forever, because a client never
        // destroys an unresolvable ZDO and a dedicated server only streams its own origin area. Any raid whose owner
        // left before EndRaid (or that 1.12.0's component-only teardown ended) left one of those behind at the base.
        // Idempotent. Registration mirrors NemesisRemoteSpawnControl.
        internal static void LoadAssets() {
            if (RaidRunnerGO != null) { return; }
            RaidRunnerGO = StarLevelSystem.EmbeddedResourceBundle.LoadAsset<GameObject>(RaidRunnerPrefabName + ".prefab");
            if (RaidRunnerGO == null) {
                Logger.LogWarning($"Raid runner prefab '{RaidRunnerPrefabName}.prefab' was not found in the asset bundle; raids cannot start.");
                return;
            }
            if (PrefabManager.Instance.GetPrefab(RaidRunnerPrefabName) == null) {
                PrefabManager.Instance.AddPrefab(new CustomPrefab(RaidRunnerGO, false));
            }
        }

        // Guarantee the prefab is in ZNetScene.m_namedPrefabs on THIS machine. Jotunn copies PrefabManager prefabs
        // into ZNetScene from its own ZNetScene.Awake postfix, but order against ours is not guaranteed and a
        // dedicated server skips the main-menu path that Jotunn's registration leans on, so the raid patches call
        // this from a ZNetScene.Awake postfix too. Idempotent, and safe alongside Jotunn's registration.
        internal static void EnsureRegisteredToZNetScene() {
            LoadAssets();
            if (RaidRunnerGO == null || ZNetScene.instance == null) { return; }
            int hash = RaidRunnerPrefabName.GetStableHashCode();
            if (ZNetScene.instance.HasPrefab(hash) == false) {
                PrefabManager.Instance.RegisterToZNetScene(RaidRunnerGO);
            }
            Logger.LogRaid($"RaidRunner prefab registered to ZNetScene: {ZNetScene.instance.HasPrefab(hash)}");
        }

        internal static void StartRaidRunner(RaidDefinition targetRaid, Vector3 pos) {
            GameObject raidGo = GameObject.Instantiate(RaidRunnerGO, pos, Quaternion.identity);
            RaidRunner raidRun = raidGo.GetComponent<RaidRunner>();
            raidRun.StartRaid(targetRaid, Player.m_localPlayer);
        }

        // Shared start sequence for a forced raid (e.g. console 'event' command, sls-raid-spawn, or the
        // vanilla SetRandomEvent passthrough). Returns whether a raid runner was actually dispatched, so a
        // caller with somewhere to report can say so rather than leaving a warning in the log as the only trace.
        //
        // skipCooldown tags the raid so FinalizeRaidCommit leaves the target player's raid schedule alone;
        // see MarkForcedRaid. The eligibility/cooldown checks on the way in are skipped regardless, since this
        // path never consults GetValidRaidsForPlayer.
        internal static bool DispatchForcedRaid(RaidDefinition targetRaid, Vector3 pos, bool skipCooldown = false) {
            // Special case for when the server itself tries to start a raid, as it does not have a player.
            if (ZNet.instance != null && ZNet.instance.IsDedicated()) {
                if (StartNetworkedRaidRunner(targetRaid, pos, skipCooldown) == false) {
                    Logger.LogWarning($"Networked raid dispatch failed for '{targetRaid.Name}' at {pos}; event will be skipped this cycle.");
                    return false;
                }
                return true;
            }

            if (skipCooldown) {
                MarkForcedRaid(SLSExtensions.GetLocalUserPlatformAndID(), targetRaid.Name);
            }
            StartRaidRunner(targetRaid, pos);
            if (Player.m_localPlayer) {
                Player.m_localPlayer.ShowTutorial("randomevent", false);
            }
            return true;
        }

        internal static bool StartNetworkedRaidRunner(RaidDefinition targetRaid, Vector3 pos, bool skipCooldown = false) {
            ZNetPeer peer = SLSExtensions.GetNearestReadyPeer(pos);
            if (peer == null) {
                Logger.LogWarning($"Unable to start raid {targetRaid.Name}; no ready peers were available for the client-side raid runner.");
                return false;
            }

            Vector3 raidPosition = pos;
            if (raidPosition.y == 0f) {
                raidPosition.y = peer.m_refPos.y;
            }

            if (StartNetworkedRaidForPeer(targetRaid, raidPosition, peer) == false) { return false; }
            // Tag after dispatch succeeded, and only once the peer that will actually own the raid is known --
            // that peer is who reports the commit back, so it is who the exemption has to be keyed against.
            if (skipCooldown) {
                PlatformUserID peerPlatformUserID = SLSExtensions.GetPeerPlatformUserID(peer);
                if (peerPlatformUserID.IsValid) {
                    MarkForcedRaid(peerPlatformUserID.ToString(), targetRaid.Name);
                } else {
                    Logger.LogWarning($"Force-started raid '{targetRaid.Name}' was sent to {peer.m_playerName}, but their platform ID could not be resolved; their raid cooldown will be set as normal when it commits.");
                }
            }
            ForceMusicForClientsInArea(targetRaid.ForceMusic, raidPosition, targetRaid.EventRange * 1.5f);
            return true;
        }

        internal static bool StartNetworkedRaidForPeer(RaidDefinition targetRaid, Vector3 pos, ZNetPeer peer) {
            if (peer == null) {
                Logger.LogWarning($"Unable to start raid {targetRaid.Name}; target peer is null.");
                return false;
            }

            Logger.LogDebug($"Sending networked raid runner for {targetRaid.Name} to {peer.m_playerName} at {pos}");
            ValConfig.ClientStartRaidRPC.SendPackage(peer.m_uid, CreateStartRaidPackage(targetRaid, pos));
            return true;
        }

        internal static ZPackage CreateStartRaidPackage(RaidDefinition targetRaid, Vector3 pos) {
            ZPackage zpack = new ZPackage();
            zpack.Write(DataObjects.yamlSerializer.Serialize(new NetworkRaidRequest() { Raid = targetRaid, RaidPostion = pos}));
            return zpack;
        }

        public static RaidDefinition RandomSelectValidRaidForPlayer(string playerPlatformID) {
            if (RaidsData.SLE_Raid_Settings.Raids.Count == 0) {
                Logger.LogWarning("No Raids were defined.");
                return new RaidDefinition() { };
            }

            Logger.LogDebug($"Checking for raids for {playerPlatformID}");

            if (string.IsNullOrEmpty(playerPlatformID)) {
                Logger.LogWarning("A raid was requested without a resolvable player, no raid can be selected.");
                return new RaidDefinition() { };
            }

            if (ServerPlayerRaidData.ContainsKey(playerPlatformID) == false) {
                Logger.LogWarning($"Player {playerPlatformID} was not found and an appropriate raid can't be determined, a random one will be selected. \n  Currently tracked: {string.Join(",", ServerPlayerRaidData.Keys.ToList())}");
                return RaidsData.SLE_Raid_Settings.Raids.ElementAt(UnityEngine.Random.Range(0, RaidsData.SLE_Raid_Settings.Raids.Count));
            }

            // A tracked player whose available-raid list hasn't been computed yet (or matched nothing)
            // would throw on the index below; fall back to a random raid like the untracked case.
            List<RaidDefinition> availableRaids = ServerPlayerRaidData[playerPlatformID].PlayerAvailableRaids;
            if (availableRaids == null || availableRaids.Count == 0) {
                Logger.LogWarning($"Player {playerPlatformID} has no available raids computed, a random one will be selected.");
                return RaidsData.SLE_Raid_Settings.Raids.ElementAt(UnityEngine.Random.Range(0, RaidsData.SLE_Raid_Settings.Raids.Count));
            }
            return availableRaids.ElementAt(UnityEngine.Random.Range(0, availableRaids.Count));
        }

        internal static void UpdateOrAddPlayerPrivateKeys(string playerPlatformID, List<string> privatekeys) {
            if (string.IsNullOrEmpty(playerPlatformID)) { return; }
            UpdateOrAddPlayerPrivateKeysToRegistry(playerPlatformID, privatekeys);
        }

        internal static void UpdateOrAddPlayerPrivateKeys(long playerID, List<string> privatekeys) {
            PlatformUserID platformUserID = SLSExtensions.GetPlatformUserID(playerID);
            // An unresolvable peer yields PlatformUserID.None, which stringifies to "" -- without this it would
            // be registered as a bogus player entry that no online player ever matches. Reaching here means the
            // peer's socket host name could not be parsed on this backend, which is worth surfacing.
            if (platformUserID.IsValid == false) {
                Logger.LogWarning($"Received private keys from peer {playerID} but their platform ID could not be resolved (backend: {ZNet.m_onlineBackend}); the update will be ignored. Ready peers: {SLSExtensions.DescribeReadyPeers()}");
                return;
            }
            string playerPlatformID = platformUserID.ToString();
            if (string.IsNullOrEmpty(playerPlatformID)) { return; }
            UpdateOrAddPlayerPrivateKeysToRegistry(playerPlatformID, privatekeys);
        }

        private static void UpdateOrAddPlayerPrivateKeysToRegistry(string playerPlatformID, List<string> privatekeys) {
            if (privatekeys == null) { privatekeys = new List<string>(); }
            if (ServerPlayerRaidData.ContainsKey(playerPlatformID)) {
                ServerPlayerRaidData[playerPlatformID].PlayerPrivatekeys = privatekeys;
            } else {
                ServerPlayerRaidData.Add(playerPlatformID, new DataObjects.PlayerRaidData() { PlayerPrivatekeys = privatekeys });
            }
            // Mark for the next periodic flush instead of writing here: this fires on every
            // Player.AddUniqueKey/RemoveUniqueKey, and each write serializes the whole registry to
            // disk on the main thread.
            MarkPlayerRaidDataDirty();
        }

        private static bool playerRaidDataDirty = false;

        internal static void MarkPlayerRaidDataDirty() { playerRaidDataDirty = true; }

        // ------------------------------------------------------------------------------------------
        // Raid schedule clocks
        //
        // Cooldowns used to be stamped straight off ZNet.GetTimeSeconds(). That is already an in-game
        // clock -- net time only advances while somebody is playing and is persisted with the world save
        // -- but it is the *world's* clock: it runs while other players are on without you, and it jumps
        // forward by up to ~25 minutes whenever anyone sleeps through a night. PlayerTime measures each
        // player's own seconds in the world instead, so a cooldown only burns down while that player is
        // actually there. Which one is in use is a config choice; the stored stamps are re-based whenever
        // it changes, so nobody gains or loses raid time by the switch.
        // ------------------------------------------------------------------------------------------

        // The clock the in-memory stamps are expressed in. NOT the same as the configured clock: the two
        // differ between a config change and the re-base that follows, and writing the configured value
        // into the save file while the stamps are still in the other clock is what would make the next
        // load trust them.
        internal static RaidCooldownClockSource StampedClock { get; private set; } = RaidCooldownClockSource.WorldTime;

        // Configured clock, resolved defensively. AcceptableValueList constrains the local file, but this
        // value also arrives over the config sync from another peer's build, so an unrecognised string
        // falls back to the legacy world clock rather than throwing.
        private static RaidCooldownClockSource ConfiguredClock() {
            return ValConfig.RaidCooldownClock.Value == RaidCooldownClockSource.PlayerTime.ToString()
                ? RaidCooldownClockSource.PlayerTime : RaidCooldownClockSource.WorldTime;
        }

        private static double ClockNow(RaidCooldownClockSource clock, PlayerRaidData data) {
            if (clock == RaidCooldownClockSource.PlayerTime) { return data == null ? 0d : data.PlayedTime; }
            return ZNet.instance.GetTimeSeconds();
        }

        // "Now" for one player's stored cooldown stamps, in whatever clock those stamps are already in.
        // Every comparison against NextRaidableTime / LastRaidByName has to go through this: under
        // PlayerTime each player has their own clock, so a single shared "current time" means nothing.
        internal static double CooldownNow(PlayerRaidData data) { return ClockNow(StampedClock, data); }

        // Keeps the stored stamps and the configured clock in the same units, re-basing every player when
        // they diverge. The two are numerically incompatible -- world time is however long the world has
        // been played, player time however long one player has been in it -- so an unhandled switch would
        // either free everyone from cooldown at once (player -> world) or strand them on it for the rest
        // of the world's life (world -> player). Remaining cooldown and elapsed-since-last-raid are both
        // preserved; only the origin they are measured from moves. Returns true when it re-based.
        internal static bool EnsureCooldownClock() {
            RaidCooldownClockSource configured = ConfiguredClock();
            if (configured == StampedClock) { return false; }
            RaidCooldownClockSource previous = StampedClock;
            foreach (PlayerRaidData data in ServerPlayerRaidData.Values) {
                if (data == null) { continue; }
                RebaseCooldowns(data, ClockNow(previous, data), ClockNow(configured, data));
            }
            StampedClock = configured;
            MarkPlayerRaidDataDirty();
            Logger.LogInfo($"Raid cooldown clock changed ({previous} -> {configured}). Re-based {ServerPlayerRaidData.Count} player raid schedule(s); remaining cooldowns are unchanged.");
            return true;
        }

        // Moves one player's stamps from oldNow's origin to newNow's, preserving the intervals either side
        // of "now". A LastRaidByName entry can land negative under PlayerTime (a raid further back than the
        // player has been in the world at all); that is fine, every read of it is an interval comparison.
        private static void RebaseCooldowns(PlayerRaidData data, double oldNow, double newNow) {
            data.NextRaidableTime = newNow + Math.Max(0d, data.NextRaidableTime - oldNow);
            if (data.LastRaidByName == null) { return; }
            foreach (string raidName in data.LastRaidByName.Keys.ToList()) {
                data.LastRaidByName[raidName] = newNow - Math.Max(0d, oldNow - data.LastRaidByName[raidName]);
            }
        }

        // The raid check loop re-bases on its own next tick, so this only makes it happen now rather than
        // up to one check interval later. Guarded on registryLoaded because Jotunn's SynchronizationManager
        // restores every synced entry to its local value from a ZNet.OnDestroy prefix on world unload,
        // raising SettingChanged after raid state has already been torn down.
        internal static void OnCooldownClockChanged(object sender, EventArgs e) {
            if (registryLoaded == false || ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            if (EnsureCooldownClock()) { FlushPlayerRaidData(force: true); }
        }

        private static double lastPlayTimeAccrual = -1d;
        // Two raid-check ticks' worth. Net time can leap ahead of real elapsed time -- a bed sleep skips
        // most of a night in a few seconds, and toggling SLS raids back on resumes accrual after an
        // arbitrary gap -- and the player clock is meant to count time a player actually sat through.
        private const double MaxPlayTimeAccrualPerTick = 60d;

        // Advances every online player's own clock by the net time elapsed since the last tick. Only
        // players already in the registry are credited: someone with no entry has no cooldown to burn, and
        // their clock starts at zero when their entry is created.
        internal static void AccruePlayerTime() {
            if (ZNet.instance == null) { return; }
            double now = ZNet.instance.GetTimeSeconds();
            double previous = lastPlayTimeAccrual;
            lastPlayTimeAccrual = now;
            // First tick of the world establishes the baseline; there is no elapsed span to credit yet.
            if (previous < 0d) { return; }
            double delta = now - previous;
            if (delta <= 0d) { return; }
            if (delta > MaxPlayTimeAccrualPerTick) { delta = MaxPlayTimeAccrualPerTick; }
            if (ServerPlayerRaidData.Count == 0) { return; }
            foreach (ZNet.PlayerInfo player in ZNet.instance.GetPlayerList()) {
                string playerPlatformID = player.m_userInfo.m_id.ToString();
                if (string.IsNullOrEmpty(playerPlatformID)) { continue; }
                if (ServerPlayerRaidData.TryGetValue(playerPlatformID, out PlayerRaidData data) == false || data == null) { continue; }
                data.PlayedTime += delta;
            }
            // Deliberately does not mark the registry dirty: this moves every tick, and dirtying it here
            // would turn the periodic flush into a whole-registry disk write every 30 seconds. The clock
            // is persisted by the forced flushes instead -- each raid check, every world save, and world
            // unload -- so at worst a player re-enters with the play time they had at the last world save.
        }

        // ------------------------------------------------------------------------------------------
        // Registry lifecycle
        // ------------------------------------------------------------------------------------------

        // The global raid-check schedule, always in world time. Persisted with the registry: session-local
        // it started at 0 every launch, so the first tick after any world load always passed and handed
        // the player a full raid roll ~30 seconds after logging in.
        private static double nextRaidCheckTime = 0d;
        internal static double NextRaidCheckTime {
            get => nextRaidCheckTime;
            set { nextRaidCheckTime = value; MarkPlayerRaidDataDirty(); }
        }

        private static bool registryLoaded = false;
        private static string loadedWorld = null;

        // Loads this world's raid schedule the first time it can, and reports whether the registry is
        // usable. Deliberately not done in RaidManager.Setup: that runs from RandEventSystem.Awake, where
        // ZNet.instance is still null, so ValConfig.raidsServerSavedData resolved to the legacy shared path
        // instead of this world's file -- every world loaded one other world's cooldowns and then wrote its
        // own back, which is what made raids fire moments after logging in.
        internal static bool EnsureRegistryLoaded() {
            if (ZNet.instance == null) { return false; }
            // Only the authority holds the registry; clients receive raids over ClientStartRaidRPC.
            if (ZNet.instance.IsServer() == false) { return false; }
            string world = ZNet.instance.GetWorldName();
            // ZNet.m_world lands a little after ZNet.Awake, and the file path depends on it.
            if (string.IsNullOrEmpty(world)) { return false; }
            if (registryLoaded && loadedWorld == world) { return true; }
            if (registryLoaded && loadedWorld != world) {
                // A world switch that missed the unload hook would otherwise keep the previous world's
                // cooldowns in memory and then write them into this world's file.
                Logger.LogWarning($"Raid schedule was still loaded for world '{loadedWorld}' while '{world}' is running; reloading it for this world.");
                ResetRegistry();
            }
            LoadRegistry(world);
            loadedWorld = world;
            registryLoaded = true;
            return true;
        }

        private static void ResetRegistry() {
            ServerPlayerRaidData = new Dictionary<string, PlayerRaidData>();
            nextRaidCheckTime = 0d;
            StampedClock = RaidCooldownClockSource.WorldTime;
            lastPlayTimeAccrual = -1d;
            forcedRaidCommits.Clear();
            ActiveRaidRunners.Clear();
            playerRaidDataDirty = false;
            registryLoaded = false;
            loadedWorld = null;
        }

        // Leaving a world or quitting (ZNet.Shutdown / ShutdownWithoutSave, via LevelScalingPatches).
        // ZNet is still alive in those prefixes, so the flush still resolves this world's own file.
        internal static void OnWorldUnload() {
            // Forced: the player clock and the check schedule both move without marking the registry
            // dirty, so a world whose last raid check was uneventful still has something to write.
            FlushPlayerRaidData(force: true);
            ResetRegistry();
        }

        private static void LoadRegistry(string world) {
            // Private keys can arrive from Player.Load before the registry is readable; those entries are
            // the only thing in memory worth keeping, so carry their keys across the load.
            Dictionary<string, PlayerRaidData> preLoad = ServerPlayerRaidData;
            RaidSaveState state = null;
            try {
                state = ParseRegistry(RaidsData.LoadServerRaidData());
            } catch (Exception e) {
                Logger.LogWarning($"There was an error loading saved player raid data ({ValConfig.raidsServerSavedData}). New data will be requested from players. Exception: {e}");
            }

            if (state != null && string.IsNullOrEmpty(state.WorldName) == false && state.WorldName != world) {
                // Another world's schedule sitting at this world's path -- ValConfig.PerWorldStatePath
                // seeds each new world's file from the legacy shared one, so this is the expected state for
                // a world that has never written its own. None of it belongs here.
                Logger.LogInfo($"Saved raid data belongs to a different world ({state.WorldName} vs {world}); starting this world's raid schedule fresh.");
                state = null;
            }

            if (state == null) {
                Logger.LogInfo($"No usable saved raid data for world '{world}' ({ValConfig.raidsServerSavedData}); starting from an empty raid schedule. Player data will be requested from connected clients.");
                state = new RaidSaveState();
            }

            ServerPlayerRaidData = state.Players;
            nextRaidCheckTime = state.NextRaidCheckTime;
            StampedClock = state.CooldownClock == RaidCooldownClockSource.PlayerTime.ToString()
                ? RaidCooldownClockSource.PlayerTime : RaidCooldownClockSource.WorldTime;
            lastPlayTimeAccrual = -1d;

            foreach (KeyValuePair<string, PlayerRaidData> pending in preLoad) {
                if (pending.Value == null) { continue; }
                if (ServerPlayerRaidData.TryGetValue(pending.Key, out PlayerRaidData loaded) && loaded != null) {
                    loaded.PlayerPrivatekeys = pending.Value.PlayerPrivatekeys;
                } else {
                    ServerPlayerRaidData[pending.Key] = pending.Value;
                }
            }

            // Re-base before anything reads a stamp, then repair whatever the file could not vouch for.
            EnsureCooldownClock();
            SanitizeLoadedSchedule();

            Logger.LogInfo($"Loaded raid schedule for world '{world}': {ServerPlayerRaidData.Count} player(s), cooldown clock {StampedClock}, next raid check at {nextRaidCheckTime:F0} (world time is {ZNet.instance.GetTimeSeconds():F0}).");
        }

        // The save file has had two shapes. Current is a RaidSaveState; everything written before that is a
        // bare platformID -> PlayerRaidData map, which the shared deserializer rejects outright (it does not
        // ignore unmatched properties), so the legacy read only runs once the wrapper read has failed.
        private static RaidSaveState ParseRegistry(string yaml) {
            if (string.IsNullOrEmpty(yaml)) { return null; }
            try {
                RaidSaveState state = yamlDeserializer.Deserialize<RaidSaveState>(yaml);
                if (state != null && state.Players != null) { return state; }
            } catch (Exception) {
                // Not the current shape. A file that is neither shape throws out of the legacy read below,
                // where the caller reports it against the file name.
            }
            Dictionary<string, PlayerRaidData> legacy = yamlDeserializer.Deserialize<Dictionary<string, PlayerRaidData>>(yaml);
            if (legacy == null) { return null; }
            // Pre-wrapper file: no recorded owner to check against, no saved check time, and its stamps can
            // only be world time, because that is the only clock that existed when it was written.
            return new RaidSaveState() {
                Players = legacy,
                CooldownClock = RaidCooldownClockSource.WorldTime.ToString(),
            };
        }

        // Nothing in the file is trustworthy as an absolute value. A pre-wrapper file carries no world name,
        // so a schedule seeded from another world is accepted above, and a world restored from a backup
        // rolls net time backwards underneath stamps written after it. Both leave stamps arbitrarily far in
        // the future, which reads as "on cooldown long past when anyone is still playing". Clamp to the
        // longest cooldown the configuration can actually produce; nothing beyond that could have been
        // written by this world's raid system.
        private static void SanitizeLoadedSchedule() {
            double maxCooldown = MaxConfiguredCooldownSeconds();
            int clamped = 0;
            foreach (PlayerRaidData data in ServerPlayerRaidData.Values) {
                if (data == null) { continue; }
                double now = CooldownNow(data);
                if (data.NextRaidableTime > now + maxCooldown) {
                    data.NextRaidableTime = now + maxCooldown;
                    clamped++;
                }
                if (data.LastRaidByName == null) { continue; }
                foreach (string raidName in data.LastRaidByName.Keys.ToList()) {
                    // A raid cannot have happened later than now; a stamp that reads that way is not ours.
                    if (data.LastRaidByName[raidName] > now) { data.LastRaidByName[raidName] = now; }
                }
            }
            double worldNow = ZNet.instance.GetTimeSeconds();
            double checkInterval = ValConfig.ServerTimeBetweenRaidStartChecks.Value * 60d;
            if (nextRaidCheckTime > worldNow + checkInterval) { nextRaidCheckTime = worldNow + checkInterval; }
            if (clamped > 0) {
                Logger.LogInfo($"{clamped} saved raid cooldown(s) were further out than any configured raid could set ({maxCooldown / 60d:F0} minutes) and have been clamped; those players become raidable again on the normal schedule.");
                MarkPlayerRaidDataDirty();
            }
        }

        private static double MaxConfiguredCooldownSeconds() {
            RaidConfiguration cfg = RaidsData.SLE_Raid_Settings;
            double scalar = cfg != null && cfg.GlobalSettings != null ? cfg.GlobalSettings.GlobalRaidIntervalScalar : 1d;
            if (scalar <= 0d) { scalar = 1d; }
            double longest = 0d;
            if (cfg != null && cfg.Raids != null) {
                foreach (RaidDefinition raid in cfg.Raids) {
                    if (raid == null) { continue; }
                    double cooldown = raid.RaidCoolDownMinutes * 60d * scalar;
                    if (cooldown > longest) { longest = cooldown; }
                }
            }
            // MarkRaidPending writes a check interval rather than a raid cooldown, so that is the floor.
            return Math.Max(longest, ValConfig.ServerTimeBetweenRaidStartChecks.Value * 60d);
        }

        // Serializes + writes the registry. force writes regardless; otherwise only when dirty.
        // Flushed from RaidManager's periodic tick, raid dispatch/commit, the world save and teardown.
        internal static void FlushPlayerRaidData(bool force = false) {
            if (force == false && playerRaidDataDirty == false) { return; }
            // Only the authority owns this file. A client's GetWorldName() is null, so it would resolve
            // the legacy shared path and overwrite it with a registry it never loaded.
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            // Same reason, for the authority: a world that never got as far as reading its schedule (SLS
            // raids off all session, say) must not write an empty one over it on the way out.
            if (registryLoaded == false) { return; }
            playerRaidDataDirty = false;
            RaidsData.SaveServerRaidData(DataObjects.yamlSerializer.Serialize(new RaidSaveState() {
                WorldName = ZNet.instance.GetWorldName(),
                NextRaidCheckTime = NextRaidCheckTime,
                CooldownClock = StampedClock.ToString(),
                Players = ServerPlayerRaidData,
            }));
        }

        internal static void UpdatePlayerRaidHistory(PlayerRaidData playerRaidData, RaidDefinition raidDef, string key) {
            double now = CooldownNow(playerRaidData);
            // Update history of this raid happening
            if (playerRaidData.LastRaidByName.ContainsKey(key)) {
                playerRaidData.LastRaidByName[key] = now;
            } else {
                playerRaidData.LastRaidByName.Add(key, now);
            }
            // Set the current raid
            playerRaidData.ActiveRaid = raidDef;
            // Update cooldown
            playerRaidData.NextRaidableTime = now + (raidDef.RaidCoolDownMinutes * 60 * RaidsData.SLE_Raid_Settings.GlobalSettings.GlobalRaidIntervalScalar);
        }

        // Lightweight dispatch-time marker. Holds the player off re-dispatch for one check interval and records the
        // pending raid/position, but does NOT consume the real cooldown or per-raid history — that only happens once
        // the client confirms the raid actually started (FinalizeRaidCommit). A raid that fails to start therefore
        // won't burn the player's cooldown beyond this short hold.
        internal static void MarkRaidPending(PlayerRaidData playerRaidData, RaidDefinition raidDef, Vector3 pos) {
            playerRaidData.ActiveRaid = raidDef;
            playerRaidData.CurrentRaidPosition = pos;
            playerRaidData.NextRaidableTime = CooldownNow(playerRaidData) + (ValConfig.ServerTimeBetweenRaidStartChecks.Value * 60);
        }

        // Raids force-started by sls-raid-spawn. FinalizeRaidCommit consumes the entry instead of writing the
        // player's cooldown, so an admin/debug raid never eats into their natural raid schedule. Entries expire
        // so a forced raid that never commits cannot silently exempt a later natural raid of the same name for
        // the same player.
        private static readonly Dictionary<string, double> forcedRaidCommits = new Dictionary<string, double>();
        private const double ForcedRaidCommitWindowSeconds = 300d;

        private static string ForcedRaidKey(string playerPlatformID, string raidName) => $"{playerPlatformID}|{raidName}";

        internal static void MarkForcedRaid(string playerPlatformID, string raidName) {
            if (ZNet.instance == null || string.IsNullOrEmpty(playerPlatformID) || string.IsNullOrEmpty(raidName)) { return; }
            forcedRaidCommits[ForcedRaidKey(playerPlatformID, raidName)] = ZNet.instance.GetTimeSeconds() + ForcedRaidCommitWindowSeconds;
            Logger.LogRaid($"Raid '{raidName}' force-started for {playerPlatformID}; their cooldown will be left untouched when it commits.");
        }

        private static bool ConsumeForcedRaid(string playerPlatformID, string raidName) {
            if (forcedRaidCommits.Count == 0 || ZNet.instance == null) { return false; }
            double now = ZNet.instance.GetTimeSeconds();
            // A forced raid that aborted before committing leaves its entry behind forever otherwise, and would
            // then exempt whichever natural raid of that name happened to commit next.
            foreach (string stale in forcedRaidCommits.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList()) {
                forcedRaidCommits.Remove(stale);
            }
            return forcedRaidCommits.Remove(ForcedRaidKey(playerPlatformID, raidName));
        }

        // Server-side commit, invoked once the owning client confirms its raid started (directly on an integrated
        // host, or via RaidCommittedRPC from a networked client). Sets the full cooldown and broadcasts combat music
        // to nearby clients — both deferred from dispatch so an aborted raid produces no visible side effects.
        internal static void FinalizeRaidCommit(string playerPlatformID, string raidName, Vector3 pos) {
            // Usually already loaded by the raid check that dispatched this, but a raid can also reach
            // here from the vanilla SetRandomEvent passthrough, which never consults the registry.
            EnsureRegistryLoaded();
            if (string.IsNullOrEmpty(playerPlatformID)) {
                Logger.LogWarning("Raid commit received without a resolvable player; cooldown will not be set.");
                return;
            }
            if (RaidsData.RaidsByName.TryGetValue(raidName, out RaidDefinition raidDef) == false) {
                Logger.LogWarning($"Raid commit received for unknown raid '{raidName}', ignoring.");
                return;
            }
            // Checked before the registry lookup below, so a force-started raid for an otherwise untracked player
            // does not create an entry for them as a side effect. Music still plays -- that is a the-raid-is-here
            // effect, not cooldown bookkeeping -- but nothing is written, so there is nothing to flush.
            if (ConsumeForcedRaid(playerPlatformID, raidName)) {
                Logger.LogRaid($"Raid '{raidName}' for {playerPlatformID} was force-started; leaving their raid cooldown untouched.");
                ForceMusicForClientsInArea(raidDef.ForceMusic, pos, raidDef.EventRange * 1.5f);
                return;
            }
            if (ServerPlayerRaidData.TryGetValue(playerPlatformID, out PlayerRaidData playerData) == false) {
                playerData = new PlayerRaidData();
                ServerPlayerRaidData[playerPlatformID] = playerData;
            }
            Logger.LogRaid($"Finalizing raid commit '{raidName}' for {playerPlatformID} at {pos}");
            UpdatePlayerRaidHistory(playerData, raidDef, raidDef.Name);
            ForceMusicForClientsInArea(raidDef.ForceMusic, pos, raidDef.EventRange * 1.5f);
            FlushPlayerRaidData(force: true);
        }

        // The vanilla m_eventIntervalMin, captured before SLS scales it. ApplyRaidConfiguration runs on every
        // config sync and every ConfigFileWatcher reload, so scaling in place compounded (2x -> 4x -> 8x ...)
        // and was never restored when switching back to vanilla raids. Tracked alongside the instance it came
        // from so a world reload re-captures rather than reusing a stale baseline.
        private static RandEventSystem vanillaRaidBaselineSource;
        private static float vanillaEventIntervalMin;

        internal static void CaptureVanillaRaidBaseline(RandEventSystem res) {
            if (res == null || vanillaRaidBaselineSource == res) { return; }
            vanillaRaidBaselineSource = res;
            vanillaEventIntervalMin = res.m_eventIntervalMin;
        }

        internal static void ApplyRaidConfiguration(RandEventSystem res) {
            if (res == null) { return; }
            // Awake normally captures this, but a client that joins mid-session or a re-entered scene can reach
            // here first; capturing lazily keeps the baseline honest either way.
            CaptureVanillaRaidBaseline(res);

            if (ValConfig.UseVanillaRaidConfiguration.Value) {
                // Hand the vanilla interval back, otherwise vanilla raids stay rate-suppressed for the session.
                res.m_eventIntervalMin = vanillaEventIntervalMin;
                return;
            }

            RaidConfiguration cfg = RaidsData.SLE_Raid_Settings ?? RaidsData.DefaultConfiguration;

            if (cfg.GlobalSettings != null && cfg.GlobalSettings.GlobalRaidIntervalScalar > 0f) {
                res.m_eventIntervalMin = vanillaEventIntervalMin * cfg.GlobalSettings.GlobalRaidIntervalScalar;
            } else {
                res.m_eventIntervalMin = vanillaEventIntervalMin;
            }

            Logger.LogInfo($"SLS raid system: applied {cfg.Raids.Count} raid definitions.");
        }

        // UseVanillaRaidConfiguration is server-authoritative and arrives on clients through Jotunn's config sync,
        // which assigns BoxedValue and so raises SettingChanged on every machine. Without this, flipping to vanilla
        // mid-session stranded any live raid: RaidRunner.Update early-returns on the flag before it can wind down,
        // leaving creatures, map pins, music and the forced environment in place permanently.
        internal static void OnVanillaRaidModeChanged(object sender, EventArgs e) {
            RandEventSystem res = RandEventSystem.instance;

            if (ValConfig.UseVanillaRaidConfiguration.Value == false) {
                ApplyRaidConfiguration(res);
                return;
            }

            // Switching to vanilla: hard-stop everything SLS owns. Each RaidRunner.OnDestroy unregisters the raid,
            // removes its map pins, stops the music, releases the environment override and clears its creatures.
            RemoveNearbyRunningEvents();
            // Restores m_eventIntervalMin to the vanilla baseline.
            ApplyRaidConfiguration(res);
        }

        internal static void UpdateAvailableRaidsPerPlayer() {
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) {
                if (peer.IsReady() == false) { continue; }
                PlatformUserID peerPlatformUserID = SLSExtensions.GetPeerPlatformUserID(peer);
                // Without this guard an unresolvable peer keys the registry off PlatformUserID.None, whose
                // string form is empty -- a junk entry that gets persisted and never matches anyone.
                if (peerPlatformUserID.IsValid == false) { continue; }
                string playerPlatformID = peerPlatformUserID.ToString();
                List<RaidDefinition> playerAvailableRaids = GetValidRaidsForPlayer(peer.GetRefPos(), playerPlatformID);
                if (ServerPlayerRaidData.ContainsKey(playerPlatformID)) {
                    ServerPlayerRaidData[playerPlatformID].PlayerAvailableRaids = playerAvailableRaids;
                } else {
                    ServerPlayerRaidData.Add(playerPlatformID, new DataObjects.PlayerRaidData() { PlayerAvailableRaids = playerAvailableRaids });
                }
            }
        }

        internal static List<RaidDefinition> GetValidRaidsForPlayer(Vector3 position, string playerPlatformID) {
            //Logger.LogDebug("Starting valid raid check");
            List<RaidDefinition> playerAvailableRaids = new List<RaidDefinition>();
            //Logger.LogDebug("Base area check");
            bool inBase = EffectArea.IsPointInsideArea(position, EffectArea.Type.PlayerBase, 30f);
            //Logger.LogDebug("Biome check ");
            if (WorldGenerator.instance == null) return playerAvailableRaids;
            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(position);

            foreach (RaidDefinition raid in RaidsData.SLE_Raid_Settings.Raids) {
                //Logger.LogDebug($"Starting check for {raid.Name}");

                if (raid.Activation == null || raid.Enabled == false) { continue; }

                // Biome Check
                //Logger.LogDebug($"Checking for Raid biome requirements");
                if (raid.Activation.Biomes != null && raid.Activation.Biomes.Contains(biome) == false) {
                    Logger.LogRaid($"Player is not in a target biome, skipping selection of Raid: {raid.Name}");
                    continue;
                }
                // BaseCheck
                //Logger.LogDebug($"Checking for Raid player base requirements");
                if (raid.Activation.NearBaseOnly && inBase == false ) {
                    Logger.LogRaid($"Player is not in base, skipping selection of Raid: {raid.Name}");
                    continue;
                }
                // Required Global Key Check
                //Logger.LogDebug($"Checking for global key requirements");
                if (raid.Activation.RequiredGlobalKeys != null) {
                    bool hasRequiredGlobalKeys = true;
                    if (ZoneSystem.instance == null) continue;
                    List<string> currentGlobalKeys = ZoneSystem.instance.GetGlobalKeys();
                    foreach (string gkey in raid.Activation.RequiredGlobalKeys) {
                        if (currentGlobalKeys.Contains(gkey) == false) {
                            hasRequiredGlobalKeys = false;
                            break;
                        }
                    }
                    if (hasRequiredGlobalKeys == false) {
                        Logger.LogRaid($"Server does not have a required global key [{string.Join(",", raid.Activation.RequiredGlobalKeys)}], skipping Raid: {raid.Name}");
                        continue;
                    }
                }

                // Global key Anti-key check
                if (raid.Activation.NotRequiredGlobalKeys != null) {
                    bool hasAnAntiKey = false;
                    if (ZoneSystem.instance == null) continue;
                    List<string> currentGlobalKeys = ZoneSystem.instance.GetGlobalKeys();
                    foreach (string gkey in raid.Activation.NotRequiredGlobalKeys) {
                        if (currentGlobalKeys.Contains(gkey)) {
                            hasAnAntiKey = true;
                            break;
                        }
                    }
                    if (hasAnAntiKey == true) {
                        Logger.LogRaid($"Server has a key that must be missing [{string.Join(",", raid.Activation.NotRequiredGlobalKeys)}], skipping Raid: {raid.Name}");
                        continue;
                    }
                }

                //Logger.LogDebug($"Finding Player Raid Data");
                PlayerRaidData playerData = new PlayerRaidData();
                if (ServerPlayerRaidData.ContainsKey(playerPlatformID)) {
                    playerData = ServerPlayerRaidData[playerPlatformID];
                }

                List<string> playerPrivateKeys = playerData.PlayerPrivatekeys ?? new List<string>();


                // Player Private keys will require an RPC requeast from the client for the data, since it is not stored server side.
                // Required private key check
                //Logger.LogDebug($"Checking for required private keys");
                if (raid.Activation.RequiredPlayerKeys != null) {
                    bool hasRequiredPlayerKeys = true;
                    foreach (string pkey in raid.Activation.RequiredPlayerKeys) {
                        if (playerPrivateKeys.Contains(pkey) == false) {
                            hasRequiredPlayerKeys = false;
                            break;
                        }
                    }
                    if (hasRequiredPlayerKeys == false) {
                        Logger.LogRaid($"Player {playerPlatformID} does not have a required private key, skipping Raid: {raid.Name}");
                        continue;
                    }
                }

                // Check for partial match player keys
                if (raid.Activation.AnyRequiredPlayerKeys != null) {
                    bool hasAnyRequiredPlayerKeys = false;
                    foreach (string pkey in raid.Activation.AnyRequiredPlayerKeys) {
                        if (playerPrivateKeys.Contains(pkey)) {
                            hasAnyRequiredPlayerKeys = true;
                            break;
                        }
                    }
                    if (hasAnyRequiredPlayerKeys == false) {
                        Logger.LogRaid($"Player {playerPlatformID} does not have any of the required private keys, skipping Raid: {raid.Name}");
                        continue;
                    }
                }

                // Check to validate ensure that required missing keys are not present
                if (raid.Activation.NotRequiredPlayerKeys != null) {
                    bool hasAntiPrivateKey = false;
                    foreach (string pkey in raid.Activation.NotRequiredPlayerKeys) {
                        if (playerPrivateKeys.Contains(pkey)) {
                            hasAntiPrivateKey = true;
                            break;
                        }
                    }
                    if (hasAntiPrivateKey == true) {
                        Logger.LogRaid($"Player {playerPlatformID} has a private key which must be avoided, skipping Raid: {raid.Name}");
                        continue;
                    }
                }

                // Check if the raid has been activated too recently
                //Logger.LogDebug($"Checking recent activations of specified raid");
                if (playerData.LastRaidByName.Count > 0) {
                    double playerNow = CooldownNow(playerData);
                    if (playerData.NextRaidableTime > playerNow) {
                        Logger.LogRaid($"Player {playerPlatformID} is next raidable in {playerData.NextRaidableTime - playerNow} seconds, skipping Raid: {raid.Name}");
                        continue;
                    }
                    if (playerData.LastRaidByName != null && playerData.LastRaidByName.ContainsKey(raid.Name) && (playerData.LastRaidByName[raid.Name] + (raid.RaidCoolDownMinutes * 60)) > playerNow) {
                        Logger.LogRaid($"Player {playerPlatformID} has activated Raid {raid.Name} too recently, skipping. Raidable again in {(playerData.LastRaidByName[raid.Name] + (raid.RaidCoolDownMinutes * 60)) - playerNow} seconds.");
                        continue;
                    }
                }

                Logger.LogRaid($"Raid {raid.Name} valid for player {playerPlatformID}");
                playerAvailableRaids.Add(raid);
            }


            return playerAvailableRaids;
        }

        public static void ForceMusicForClientsInArea(Music music, Vector3 position, float range) {
            // Validate the requested music is valid
            // if music is invalid return
            // if this is not a server, dedicated or integrated, return

            List<ZNetPeer> peersInArea = SLSExtensions.ServerGetPeersInArea(position, range);
            ZPackage package = new ZPackage();
            package.Write(music.ToString());
            foreach (ZNetPeer peer in peersInArea) {
                ValConfig.ClientForcePlayMusicRPC.SendPackage(peer.m_uid, package);
            }
        }

        public static void RemoveNearbyRunningEvents() {
            Logger.LogRaid($"Client recieved remove nearby event command.");
            // Reachable outside a loaded world (config change in the main menu), where there is nothing to destroy.
            if (ZNetScene.instance == null) { return; }

            // Avoid the original
            IEnumerable<RaidRunner> objects = Resources.FindObjectsOfTypeAll<RaidRunner>();
            //Logger.LogDebug($"Removing {objects.Count()} nearby events.");
            foreach (RaidRunner obj in objects) {
                if (obj.name == "RaidRunner") { continue; } // skip the original
                Logger.LogRaid($"Removing {obj.name}");
                obj.EndRaid(destroyCreatures: true);
            }
        }

        public static IEnumerator DetermineRemoteSpawnLocations(Vector3 origin, ListVectorZNetProperty resultset,  int numTargets, BoolZNetProperty pointsReady, float maxDistance = 300f, Heightmap.Biome targetBiome = Heightmap.Biome.None) {
            List<SerializableVector3> spawn_locations = new List<SerializableVector3>();
            //Logger.LogDebug($"Starting spawn destination in incrments of {range_increment} from x{origin.x} y{origin.y} z{origin.z}");
            int spawn_location_attempts = 0;
            float originalMaxDistance = maxDistance;
            bool allowBaseSpawns = false;
            Vector3 determinedSpawn = origin;

            while (spawn_locations.Count < numTargets) {
                var offset = UnityEngine.Random.insideUnitCircle * (maxDistance * 0.8f);
                determinedSpawn = origin + new Vector3(offset.x, 0, offset.y);

                // Progressively widen the search the longer we fail, then relax the base restriction as a last
                // resort, so cramped/coastal/heavily-built-up areas still yield points instead of aborting the raid.
                if (spawn_location_attempts > 100 && spawn_location_attempts % 50 == 0) {
                    if (spawn_locations.Count > 0) {
                        // At least one valid spawn has been found
                        break;
                    }
                    if (maxDistance < originalMaxDistance * 4) {
                        maxDistance += 50f;
                    } else if (allowBaseSpawns == false) {
                        // Base detection is imperfect (esp. Ashlands POIs that read as "player base"); allow base
                        // spawns rather than aborting the raid for lack of room.
                        Logger.LogRaid("Spawn search exhausted normal range; relaxing the player-base restriction as a last resort.");
                        allowBaseSpawns = true;
                    } else {
                        break; // Genuinely nowhere valid to spawn; the raid will abort harmlessly.
                    }
                }

                // Sleep to avoid locking the thread
                if (spawn_location_attempts > 1 && spawn_location_attempts % 10 == 0) { yield return new WaitForSeconds(0.1f); }

                ZoneSystem.instance.GetGroundData(ref determinedSpawn, out var normal, out var foundBiome, out var biomeArea, out var hmap);

                // Prevent spawns that are in the wrong biome if we are targeting a biome
                if (targetBiome != Heightmap.Biome.None) {
                    if (hmap == null || foundBiome != targetBiome) {
                        spawn_location_attempts += 1;
                        Logger.LogRaid($"Spawn location in the wrong biome, skipping. {foundBiome} | {determinedSpawn}");
                        continue;
                    }
                }

                // Prevent spawns that are inside of objects
                float terrainHeight = determinedSpawn.y;
                float solidHeight = 1000f; // This stars high in the sky for the raycast down, gets modified next
                if (ZoneSystem.instance.FindFloor(new Vector3(determinedSpawn.x, determinedSpawn.y + 100f, determinedSpawn.z), out solidHeight)) {
                    float terrainDiff = solidHeight - terrainHeight;

                    // Prevent spawns in objects and too high off the ground
                    if (terrainDiff > 1f) {
                        Logger.LogRaid($"Spawn location blocked by an existing object skipping. {terrainDiff} | {determinedSpawn}");
                        spawn_location_attempts += 1;
                        continue;
                    }

                    if (terrainDiff > 0f) {
                        determinedSpawn.y = solidHeight;
                    }
                } else {
                    spawn_location_attempts += 1;
                    continue;
                }

                // Prevent spawns in a players base | This does not work in the ashlands as all of the existing fortresses, POIs etc are considered "player bases"
                // However ignoring player bases entirely means that the spawn can happen directly inside a players base/walls
                // foundBiome != Heightmap.Biome.AshLands &&
                if (allowBaseSpawns == false && (bool)EffectArea.IsPointInsideArea(determinedSpawn, EffectArea.Type.PlayerBase)) {
                    Logger.LogRaid($"Spawn location in a players base zone, skipping. | {determinedSpawn}");
                    spawn_location_attempts += 1;
                    continue;
                }

                // Prevent water spawns
                if (determinedSpawn.y < 27) {
                    Logger.LogRaid($"Spawn location below water level, skipping. | {determinedSpawn}");
                    spawn_location_attempts += 1;
                    continue;
                }

                // Prevent spawning in Lava unless a last resort
                if (foundBiome == Heightmap.Biome.AshLands && hmap.GetVegetationMask(determinedSpawn) > 0.45f) {
                    spawn_location_attempts += 1;
                    Logger.LogRaid($"Spawn location is in lava, skipping. | {determinedSpawn}");
                    continue;
                }

                determinedSpawn.y += 1f;

                Logger.LogRaid($"Determined valid spawn target: {determinedSpawn}");
                spawn_locations.Add(determinedSpawn);
            }

            if (spawn_locations.Count < numTargets) {
                Logger.LogWarning($"Unable to find the requested number of spawn points. Found {spawn_locations.Count} spawn locations");
            }
            // The runner that started this coroutine can be destroyed while it runs (raid aborted,
            // world unload); writing through its ZNetProperties would then throw on a dead ZNetView.
            if (resultset.IsHostValid() == false || pointsReady.IsHostValid() == false) {
                Logger.LogRaid("Raid spawn-point search finished after its runner was destroyed; discarding results.");
                yield break;
            }
            resultset.ForceSet(spawn_locations);
            pointsReady.ForceSet(true);
            yield break;
        }

    }
}
