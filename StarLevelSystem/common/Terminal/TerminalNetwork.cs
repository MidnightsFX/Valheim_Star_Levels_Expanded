using HarmonyLib;
using UnityEngine;

namespace StarLevelSystem.common
{
    // The admin-client -> server relay for server-authoritative commands, and the output coming back.
    //
    // Both directions are needed because a dedicated server has no Terminal of its own: Console.Awake and
    // Terminal.InitTerminal only ever run on a client, so these commands are otherwise untypeable
    // anywhere. Vanilla's own relay (remoteCommand -> ZNet.RPC_RemoteCommand) ends in
    // Console.instance.TryRunCommand and null-references headless, so it cannot be used here.
    //
    // Both directions use the peer connection itself (ZNetPeer.m_rpc), as RPC_RemoteCommand does, never
    // ZRoutedRpc, which is what Jotunn's CustomRPC is built on. A routed RPC's sender id is written by the
    // sending client and relayed untouched by the server, so any client could claim to be an admin. A
    // ZRpc handler is bound to the socket the call arrived on: the server reads the requester's identity
    // from that socket, and a client registers the output handler only on its connection to the server,
    // so nothing else can print into its console.
    internal static class TerminalNetwork
    {
        // GUID-prefixed: every connection has one table of RPC name hashes, shared with vanilla and every
        // other mod, and a second Register under the same hash silently replaces the first.
        private const string RequestRpc = StarLevelSystem.PluginGUID + ".CommandRequest";
        private const string OutputRpc = StarLevelSystem.PluginGUID + ".CommandOutput";

        // ZNet registers its own per-connection RPCs here, on both ends of every connection: on the server
        // once per client, and on a client once, for its link to the server.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        private static class ZNet_OnNewConnection_Patch
        {
            private static void Postfix(ZNet __instance, ZNetPeer peer)
            {
                if (__instance.IsServer())
                {
                    peer.m_rpc.Register<ZPackage>(RequestRpc, OnServerReceiveCommandRequest);
                }
                else
                {
                    peer.m_rpc.Register<ZPackage>(OutputRpc, OnClientReceiveCommandOutput);
                }
            }
        }

        internal static void SendRequest(ZRpc server, ZPackage request)
        {
            server.Invoke(RequestRpc, request);
        }

        internal static void SendOutput(ZNetPeer peer, ZPackage package)
        {
            peer.m_rpc.Invoke(OutputRpc, package);
        }

        // Server handler: an admin client asked to run a server-authoritative SLS console command. These
        // commands read or mutate world state only the server owns. Gate on admin because any peer could
        // craft this RPC; the client-side check is only there for a clearer message. The admin check
        // reads the socket the request arrived on, which the client cannot choose, the same check
        // vanilla's RPC_RemoteCommand makes.
        private static void OnServerReceiveCommandRequest(ZRpc rpc, ZPackage package)
        {
            ZNetPeer requester = ZNet.instance.GetPeer(rpc);
            // Not ready means the handshake has not finished, so this is not yet a player in the world.
            if (requester == null || requester.IsReady() == false) { return; }

            string command = package.ReadString();
            string hostName = rpc.GetSocket().GetHostName();
            if (ZNet.instance.IsAdmin(hostName) == false)
            {
                Logger.LogWarning($"Rejecting '{command}' from non-admin {requester.m_playerName} ({hostName}).");
                // Answer rather than going quiet, so the sender sees a refusal instead of nothing.
                TerminalOutput refusal = TerminalOutput.Remote(requester);
                refusal.Error($"Only server admins can run {command}.", log: false);
                refusal.Flush();
                return;
            }

            int argCount = package.ReadInt();
            string[] args = new string[argCount];
            for (int i = 0; i < argCount; i++) { args[i] = package.ReadString(); }
            bool hasCenter = package.ReadBool();
            Vector3 center = package.ReadVector3();

            TerminalManager.ExecuteFromNetwork(command, args, center, hasCenter, TerminalOutput.Remote(requester));
        }

        // Client handler: a batch of output lines from a command this client asked the server to run.
        // Severity travels as a byte and the colour is applied here, so the server's log and chunk log
        // never contain markup and each client honours its own EnableTerminalColors setting.
        private static void OnClientReceiveCommandOutput(ZRpc rpc, ZPackage package)
        {
            int count = package.ReadInt();
            for (int i = 0; i < count; i++)
            {
                OutputLevel level = (OutputLevel)package.ReadByte();
                TerminalManager.PrintResponse(level, package.ReadString());
            }
        }
    }
}
