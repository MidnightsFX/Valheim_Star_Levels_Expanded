using HarmonyLib;
using Jotunn.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

#pragma warning disable IDE0130
namespace StarLevelSystem.common {
#pragma warning restore IDE0130

    // Server -> client sync for yaml config files.
    //
    // One Jotunn CustomRPC per file, carrying the file's text verbatim in a ZPackage. Jotunn's
    // SynchronizationManager already handles ConfigEntry sync for anything marked IsAdminOnly; this is
    // the equivalent for the structured half of a mod's configuration, which BepInEx knows nothing about.
    //
    // Every handler here closes over its YamlConfigFile. That is the whole reason this file is short:
    // AddRPC wants stateless delegates, so the obvious implementation ends up with one hand-written
    // Send/Receive/Update trio per config file. A lambda that captures the file and CALLS an iterator
    // method (the lambda itself cannot contain yield) collapses all of them into the three below.
    internal static class ConfigNetwork {
        private static bool initialized;
        private static Harmony harmony;
        private static readonly HashSet<string> usedRpcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Both directions of the admin edit channel carry this, so the payload can grow later without a
        // second compatibility break.
        private const byte EditProtocolVersion = 1;

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
            string editName = file.RpcName + "_Edit";
            if (usedRpcNames.Add(editName) == false) {
                Logger.LogError($"Config RPC name '{editName}' is already in use; {file.FileName} will not " +
                    "accept admin edits.");
                return;
            }

            file.EditRpc = NetworkManager.Instance.AddRPC(editName,
                (sender, package) => OnServerReceiveEdit(file, sender, package),
                (sender, package) => OnClientReceiveEditResult(file, sender, package));
        }

        // Send an edited copy of a file to the server for validation. Client side; the server decides.
        internal static bool RequestEdit(YamlConfigFile file, string yaml, out string refusal) {
            refusal = "";
            if (file == null || file.EditRpc == null) {
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

            ZPackage package = new ZPackage();
            package.Write(EditProtocolVersion);
            package.Write(yaml);
            file.EditRpc.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), package);
            return true;
        }

        private static IEnumerator OnServerReceiveEdit(YamlConfigFile file, long sender, ZPackage package) {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { yield break; }

            byte version = package.ReadByte();
            if (version != EditProtocolVersion) {
                SendEditResult(sender, file, false, $"This server expects edit protocol v{EditProtocolVersion}, " +
                    $"the sender used v{version}. Update so both sides match.");
                yield break;
            }

            string yaml = package.ReadString();

            if (SenderIsAdmin(sender) == false) {
                Logger.LogWarning($"Rejecting an edit of {file.FileName} from non-admin peer {sender}.");
                // Answer rather than going quiet, so the sender sees a refusal instead of nothing.
                SendEditResult(sender, file, false, $"Only server admins can change {file.FileName}.");
                yield break;
            }

            if (YamlConfigManager.ApplyEdited(file, yaml, out string message) == false) {
                Logger.LogWarning($"Admin peer {sender} sent a {file.FileName} that was rejected: {message}");
                SendEditResult(sender, file, false, message);
                yield break;
            }

            // ApplyEdited already broadcast to every peer, the uploader included, so the admin's own copy
            // arrives back through the ordinary sync path and ends up byte-identical to the server's.
            Logger.LogInfo($"{file.FileName} was replaced by admin peer {sender}.");
            SendEditResult(sender, file, true, message);
            yield return null;
        }

        private static IEnumerator OnClientReceiveEditResult(YamlConfigFile file, long sender, ZPackage package) {
            byte version = package.ReadByte();
            if (version != EditProtocolVersion) {
                EditResult?.Invoke(file, false, "The server answered with an edit protocol this build does not understand.");
                yield break;
            }

            bool accepted = package.ReadBool();
            string message = package.ReadString();
            EditResult?.Invoke(file, accepted, message);
            yield return null;
        }

        private static void SendEditResult(long peer, YamlConfigFile file, bool accepted, string message) {
            if (file.EditRpc == null) { return; }
            ZPackage package = new ZPackage();
            package.Write(EditProtocolVersion);
            package.Write(accepted);
            package.Write(message ?? "");
            file.EditRpc.SendPackage(peer, package);
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
        // Jotunn's IsAdminOnly covers ConfigEntry values only. A CustomRPC has no such protection, so any
        // peer can craft this package. A mod that genuinely wants an upload channel should write its own
        // handler and gate it on SenderIsAdmin.
        private static IEnumerator OnServerReceive(YamlConfigFile file, long sender, ZPackage package) {
            Logger.LogDebug($"Peer {sender} sent {file.FileName}; this config is server-authoritative, ignoring.");
            yield break;
        }

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
                package.Write(File.Exists(file.Path) ? File.ReadAllText(file.Path) : file.SerializeCurrent());
            } catch (Exception e) {
                Logger.LogError($"Could not read {file.FileName} to send to peers: {e.Message}");
                package.Write("");
            }
            return package;
        }

        // True when the peer uid belongs to a connected admin. The integrated host never routes through an
        // RPC, so it is not considered here.
        //
        // Intentionally duplicated from Common/Terminal/TerminalNetwork.cs rather than shared: these two
        // folders have to stay independently droppable into another mod, which is the same reason each
        // carries its own Harmony instance. Do not "fix" this by extracting it.
        internal static bool SenderIsAdmin(long sender) {
            ZNetPeer peer = ZNet.instance?.GetPeer(sender);
            if (peer == null || peer.m_socket == null) { return false; }
            return ZNet.instance.IsAdmin(peer.m_socket.GetHostName());
        }
    }
}
