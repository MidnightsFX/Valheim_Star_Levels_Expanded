using HarmonyLib;
using Jotunn.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

#pragma warning disable IDE0130
namespace StarLevelSystem.common {
#pragma warning restore IDE0130

    // Server -> client sync for yaml config files, and the opt-in admin edit channel.
    //
    // Sync is one Jotunn CustomRPC per file, carrying the file's text verbatim in a ZPackage. Jotunn's
    // SynchronizationManager already handles ConfigEntry sync for anything marked IsAdminOnly; this is
    // the equivalent for the structured half of a mod's configuration, which BepInEx knows nothing about.
    //
    // Every handler here closes over its YamlConfigFile. That is the whole reason this file is short:
    // AddRPC wants stateless delegates, so the obvious implementation ends up with one hand-written
    // Send/Receive/Update trio per config file. A lambda that captures the file and CALLS an iterator
    // method (the lambda itself cannot contain yield) collapses all of them into the three below.
    //
    // The edit channel is deliberately not a CustomRPC. CustomRPC rides ZRoutedRpc, whose sender id is
    // written by the sending client and relayed untouched by the server, so any client could claim to be
    // an admin. Edits use the peer connection itself (ZNetPeer.m_rpc), as vanilla's RPC_RemoteCommand
    // does. A ZRpc handler is bound to the socket the call arrived on: the server reads the uploader's
    // identity from that socket, and a client registers the result handler only on its connection to the
    // server, so nothing else can answer its upload.
    internal static class ConfigNetwork {
        private static bool initialized;
        private static Harmony harmony;
        private static readonly HashSet<string> usedRpcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Files with an open edit channel. Their handlers are registered per connection, so the list is
        // all that has to be remembered here.
        private static readonly List<YamlConfigFile> editableFiles = new List<YamlConfigFile>();
        private static bool connectionPatchApplied;

        // Both directions of the admin edit channel carry this, so the payload can grow later without a
        // second compatibility break.
        private const byte EditProtocolVersion = 1;

        // A ZRpc call goes out as one socket message, unsliced. Steam refuses a reliable message over
        // 512 KB, and the socket then retries it forever, holding up everything queued behind it. This is
        // the slice size Jotunn's CustomRPC uses for the same reason.
        private const int MaxEditRequestBytes = 250000;

        // Raised on the requesting admin's client when the server answers an upload: (file, accepted,
        // message). message carries the refusal reason, or any validation warnings on an accept.
        internal static event Action<YamlConfigFile, bool, string> EditResult;

        // True once this client has received the server's configuration. A mod that must not act on
        // half-synced values -- drawing UI from them, scaling a spawn -- should wait on this.
        internal static bool ServerConfigsSynced { get; private set; }

        internal static void Init() {
            if (initialized) { return; }
            initialized = true;

            SynchronizationManager.OnConfigurationSynchronized += OnConfigurationSynchronized;

            // The reset lives in here rather than in the plugin because forgetting it is invisible until
            // someone joins a second server in one session: the flag stays true from the first world, the
            // wait is skipped, and the previous server's values are used until the real sync lands.
            // Owning it makes this folder something that cannot be assembled wrong.
            //
            // Patched with a private Harmony instance, not [HarmonyPatch] attributes, so a plugin that
            // also calls Harmony.CreateAndPatchAll(assembly) does not apply it a second time.
            try {
                harmony = new Harmony(StarLevelSystem.PluginGUID + ".config");
                harmony.Patch(AccessTools.Method(typeof(ZNet), nameof(ZNet.Shutdown)),
                    prefix: new HarmonyMethod(typeof(ConfigNetwork), nameof(ResetOnWorldUnload)));
            } catch (Exception e) {
                Logger.LogWarning($"Could not patch ZNet.Shutdown for config sync teardown: {e.Message}");
            }
        }

        internal static void RegisterFile(YamlConfigFile file) {
            if (file == null || file.Sync == ConfigSyncMode.LocalOnly) { return; }

            if (string.IsNullOrEmpty(file.RpcName)) {
                file.RpcName = StarLevelSystem.PluginName + "_" + Path.GetFileNameWithoutExtension(file.FileName);
            }

            // RPC names are hashed onto a channel, so two files that derive the same name would silently
            // share one and overwrite each other. Loud here beats mysterious later.
            if (usedRpcNames.Add(file.RpcName) == false) {
                Logger.LogError($"Config RPC name '{file.RpcName}' is already in use; {file.FileName} will not " +
                    "be synced. Give it an explicit RpcName.");
                return;
            }

            file.Rpc = NetworkManager.Instance.AddRPC(file.RpcName,
                (sender, package) => OnServerReceive(file, sender, package),
                (sender, package) => OnClientReceive(file, sender, package));

            SynchronizationManager.Instance.AddInitialSynchronization(file.Rpc, () => SendFileAsZPackage(file));

            if (file.AllowAdminEdit == false) { return; }

            // A separate channel rather than a status byte on the one above: that payload shape already
            // ships, and an extra name-hashed channel costs nothing while old clients simply never use it.
            editableFiles.Add(file);
            PatchNewConnection();

            // Registering after Init is supported, so a file can arrive while connections are already open,
            // and those never pass through OnNewConnection again.
            if (ZNet.instance != null) {
                foreach (ZNetPeer peer in ZNet.instance.GetPeers()) { RegisterEditRpcs(ZNet.instance, peer, file); }
            }
        }

        // Applied once the first file opens an edit channel, not from Init, so a mod that never sets
        // AllowAdminEdit adds nothing to its connections.
        //
        // This mirrors Common/Terminal/TerminalNetwork.cs rather than sharing code with it: the two folders
        // have to stay independently droppable into another mod, which is the same reason each carries its
        // own Harmony instance. Do not "fix" this by extracting a helper.
        private static void PatchNewConnection() {
            if (connectionPatchApplied) { return; }
            try {
                MethodInfo target = AccessTools.Method(typeof(ZNet), "OnNewConnection");
                if (target == null) {
                    Logger.LogError("ZNet.OnNewConnection not found; admins cannot edit server configs remotely.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(ConfigNetwork), nameof(RegisterConnectionRpcs))));
                connectionPatchApplied = true;
            } catch (Exception e) {
                Logger.LogError($"Could not patch ZNet.OnNewConnection for admin config edits: {e.Message}");
            }
        }

        // ZNet registers its own per-connection RPCs here, on both ends of every connection: on the server
        // once per client, and on a client once, for its link to the server.
        private static void RegisterConnectionRpcs(ZNet __instance, ZNetPeer peer) {
            foreach (YamlConfigFile file in editableFiles) { RegisterEditRpcs(__instance, peer, file); }
        }

        private static void RegisterEditRpcs(ZNet znet, ZNetPeer peer, YamlConfigFile file) {
            if (znet.IsServer()) {
                peer.m_rpc.Register<ZPackage>(EditRequestRpc(file), (rpc, package) => OnServerReceiveEdit(file, rpc, package));
            } else {
                peer.m_rpc.Register<ZPackage>(EditResultRpc(file), (rpc, package) => OnClientReceiveEditResult(file, package));
            }
        }

        // GUID-prefixed: every connection has one table of RPC name hashes, shared with vanilla and every
        // other mod, and a second Register under the same hash silently replaces the first. RpcName is
        // already unique within this mod.
        private static string EditRequestRpc(YamlConfigFile file) =>
            StarLevelSystem.PluginGUID + "." + file.RpcName + ".Edit";
        private static string EditResultRpc(YamlConfigFile file) =>
            StarLevelSystem.PluginGUID + "." + file.RpcName + ".EditResult";

        // Send an edited copy of a file to the server for validation. Client side; the server decides.
        internal static bool RequestEdit(YamlConfigFile file, string yaml, out string refusal) {
            refusal = "";
            if (file == null || editableFiles.Contains(file) == false || connectionPatchApplied == false) {
                refusal = "this config cannot be edited remotely.";
                return false;
            }
            if (ZNet.instance == null || ZNet.instance.IsServer()) {
                refusal = "not connected to a server as a client.";
                return false;
            }
            // A courtesy check so a non-admin gets a clear message instead of a silent refusal. The real
            // gate is on the server, because any peer can craft this package.
            if (SynchronizationManager.Instance != null && SynchronizationManager.Instance.PlayerIsAdmin == false) {
                refusal = "only server admins can change this.";
                return false;
            }
            ZRpc server = ZNet.instance.GetServerRPC();
            if (server == null) {
                refusal = "not connected to a server as a client.";
                return false;
            }

            ZPackage package = new ZPackage();
            package.Write(EditProtocolVersion);
            package.Write(yaml);
            if (package.Size() > MaxEditRequestBytes) {
                refusal = $"{file.FileName} is too large to send as an edit; change it on the server instead.";
                return false;
            }
            server.Invoke(EditRequestRpc(file), package);
            return true;
        }

        // Server handler. The admin check reads the socket the request arrived on, which the client cannot
        // choose, the same check vanilla's RPC_RemoteCommand makes.
        private static void OnServerReceiveEdit(YamlConfigFile file, ZRpc rpc, ZPackage package) {
            ZNetPeer requester = ZNet.instance.GetPeer(rpc);
            // Not ready means the handshake has not finished, so this is not yet a player in the world.
            if (requester == null || requester.IsReady() == false) { return; }

            byte version = package.ReadByte();
            if (version != EditProtocolVersion) {
                SendEditResult(requester, file, false, $"This server expects edit protocol v{EditProtocolVersion}, " +
                    $"the sender used v{version}. Update so both sides match.");
                return;
            }

            string yaml = package.ReadString();

            string hostName = rpc.GetSocket().GetHostName();
            if (ZNet.instance.IsAdmin(hostName) == false) {
                Logger.LogWarning($"Rejecting an edit of {file.FileName} from non-admin {requester.m_playerName} ({hostName}).");
                // Answer rather than going quiet, so the sender sees a refusal instead of nothing.
                SendEditResult(requester, file, false, $"Only server admins can change {file.FileName}.");
                return;
            }

            if (YamlConfigManager.ApplyEdited(file, yaml, out string message) == false) {
                Logger.LogWarning($"Admin {requester.m_playerName} ({hostName}) sent a {file.FileName} that was rejected: {message}");
                SendEditResult(requester, file, false, message);
                return;
            }

            // ApplyEdited already broadcast to every peer, the uploader included, so the admin's own copy
            // arrives back through the ordinary sync path and ends up byte-identical to the server's.
            Logger.LogInfo($"{file.FileName} was replaced by admin {requester.m_playerName} ({hostName}).");
            SendEditResult(requester, file, true, message);
        }

        // Client handler. Registered only on the connection to the server, so it can only come from there.
        private static void OnClientReceiveEditResult(YamlConfigFile file, ZPackage package) {
            byte version = package.ReadByte();
            if (version != EditProtocolVersion) {
                EditResult?.Invoke(file, false, "The server answered with an edit protocol this build does not understand.");
                return;
            }

            bool accepted = package.ReadBool();
            string message = package.ReadString();
            EditResult?.Invoke(file, accepted, message);
        }

        private static void SendEditResult(ZNetPeer peer, YamlConfigFile file, bool accepted, string message) {
            ZPackage package = new ZPackage();
            package.Write(EditProtocolVersion);
            package.Write(accepted);
            package.Write(message ?? "");
            peer.m_rpc.Invoke(EditResultRpc(file), package);
        }

        // Push a changed file out to the peers.
        //
        // Server-only, and not just for authority reasons: the file watcher is DontDestroyOnLoad and keeps
        // polling in the main menu where ZNet.instance is null, and on a client m_peers holds the server,
        // so an unguarded broadcast would upload a client's local edits. The LOCAL apply that precedes
        // this is deliberately left unguarded, so editing yaml still works in single player.
        internal static void Broadcast(YamlConfigFile file) {
            if (file == null || file.Rpc == null) { return; }
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            file.Rpc.SendPackage(ZNet.instance.m_peers, SendFileAsZPackage(file));
        }

        internal static void ResetServerSyncState() {
            ServerConfigsSynced = false;
        }

        private static void OnConfigurationSynchronized(object sender, EventArgs e) {
            ServerConfigsSynced = true;
        }

        private static void ResetOnWorldUnload() {
            ResetServerSyncState();
        }

        // Config is server-authoritative by default: this rejects rather than admin-gating, because
        // Jotunn's IsAdminOnly covers ConfigEntry values only. A CustomRPC has no such protection, and its
        // sender id cannot be trusted, so any peer can craft this package as anyone. A file that genuinely
        // wants uploads sets AllowAdminEdit, which opens the per-connection channel above.
        private static IEnumerator OnServerReceive(YamlConfigFile file, long sender, ZPackage package) {
            Logger.LogDebug($"Peer {sender} sent {file.FileName}; this config is server-authoritative, ignoring.");
            yield break;
        }

        // Takes whatever arrives, without comparing the sender to the server's uid. That check would only
        // look like protection: the routed sender id is written by whoever sent the package and relayed
        // untouched, so another client can name the server and still reach this. Server-only delivery would
        // need this channel moved onto the peer connection like the edit channel. It stays a CustomRPC
        // because that is what Jotunn's initial synchronization takes, and that is what delivers the
        // server's copy as part of the join.
        private static IEnumerator OnClientReceive(YamlConfigFile file, long sender, ZPackage package) {
            string yaml = package.ReadString();
            file.LoadFrom(yaml, ConfigOrigin.ServerSync);

            // The bytes the server sent, not a re-serialization of what we parsed out of them: a round
            // trip through the object model drops anything the current version does not model and
            // reformats everything else, so the file on disk stops matching the server's.
            if (file.ClientWritesToDisk) { YamlConfigManager.WriteRawToDisk(file, yaml); }
            yield return null;
        }

        private static ZPackage SendFileAsZPackage(YamlConfigFile file) {
            ZPackage package = new ZPackage();
            try {
                // What this machine applied, not what is on disk. Under KeepLastGood a file that fails to load
                // stays on disk while the owner keeps its last good values; sending the disk handed every
                // joining client that broken text, which each then failed to parse and ran on its own values.
                package.Write(file.LastAppliedText ?? file.SerializeCurrent());
            } catch (Exception e) {
                Logger.LogError($"Could not prepare {file.FileName} to send to peers: {e.Message}");
                package.Write("");
            }
            return package;
        }
    }
}
