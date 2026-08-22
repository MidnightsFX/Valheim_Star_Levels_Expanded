using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StarLevelSystem.modules.LocationReset {
    // Carries Location Reset API calls from a client to the server and the answers back.
    //
    // Everything the Location Reset API does is server-owned: the ZDOs it destroys and recreates, the
    // per-zone state file, the sweep itself. But the mods that want to drive it are frequently
    // client-side -- an item a player uses to restore a dungeon runs its logic on whoever swung it --
    // so refusing off-server would mean the interesting half of the API only worked for whoever
    // happens to be hosting.
    //
    // This is a dedicated RPC pair rather than a ride on the existing console-command relay
    // (ClientCommandRequestRPC). That relay carries a command name and string arguments and answers
    // with lines of coloured text for a terminal, which cannot express a typed result or reach a
    // caller's callback; encoding API calls as console strings and scraping the reply would be a
    // worse contract in both directions.
    //
    // SECURITY. There is deliberately no permission gate: a server owner who installs a mod that
    // resets locations from gameplay wants it to work for the players using it, and admin-gating
    // would defeat that. The consequence is that any peer can craft this RPC, so the server is the
    // only thing bounding it. What bounds it:
    //
    //   - the protection scan, which is never bypassable and is what stops any of this touching a
    //     player's base, ward, bed or tombstone;
    //   - a radius clamp, so one request cannot span a continent;
    //   - a proximity check, so a peer can only ask about ground it is actually standing near;
    //   - a per-peer cooldown on anything that mutates.
    //
    // Those four are the whole envelope. They are configurable so an owner can tighten or open them.
    internal static class LocationResetNetwork {

        internal enum Op : byte {
            ResetNamed = 0,
            ResetRadius = 1,
            Register = 2,
            Unregister = 3,
            LastReset = 4,
            SecondsUntilDue = 5,
            LocationInfo = 6,
            ChunkInfo = 7,
            Status = 8,
            TargetInfo = 9,
            RegisteredNames = 10,
            IsKnownName = 11,
            Ready = 12,
        }

        // Scalar answers travel under this key so one dictionary codec serves every operation.
        internal const string ValueKey = "value";
        // Every refused answer carries these, matching what a refused reset summary carries, so a
        // caller reads the same keys whichever call it made and whichever side turned it away.
        internal const string OutcomeKey = "outcome";
        internal const string RefusalCodeKey = "refusalCode";
        internal const string ReasonKey = "reason";

        // ---------------------------------------------------------------------------------------
        // Client side
        // ---------------------------------------------------------------------------------------

        private class Pending {
            internal Op Op;
            internal Action<Dictionary<string, object>> Callback;
            internal float Deadline;
        }

        private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private static int nextRequestId = 1;
        private static bool expiryRunning;

        // A reset can legitimately take a long time: Safe mode waits for players to leave, for up to
        // whatever the caller asked for. The timeout is therefore derived per request rather than
        // fixed, and only exists so a dropped answer eventually frees the callback instead of
        // leaking it for the session.
        private const float QueryTimeoutSeconds = 20f;

        internal static bool IsServer {
            get { return ZNet.instance != null && ZNet.instance.IsServer(); }
        }

        // Send an API call to the server. Returns false when there is nothing to send it to, in which
        // case the callback never fires -- the same contract the local path uses.
        internal static bool Send(Op op, Dictionary<string, object> args, float timeoutSeconds,
                                  Action<Dictionary<string, object>> onResult) {
            if (ZNet.instance == null) {
                Logger.LogLocationResetWarning($"SLS-API: cannot send {op} to the server - not in a world.");
                return false;
            }
            ZNetPeer server = ZNet.instance.GetServerPeer();
            if (server == null) {
                Logger.LogLocationResetWarning($"SLS-API: cannot send {op} to the server - no server connection.");
                return false;
            }

            int requestId = nextRequestId++;
            if (onResult != null) {
                pending[requestId] = new Pending() {
                    Op = op,
                    Callback = onResult,
                    Deadline = Time.realtimeSinceStartup + Mathf.Max(QueryTimeoutSeconds, timeoutSeconds),
                };
                StartExpiryLoop();
            }

            ZPackage package = new ZPackage();
            package.Write(requestId);
            package.Write((byte)op);
            WriteDictionary(package, args);
            ValConfig.LocationApiRequestRPC.SendPackage(server.m_uid, package);
            return true;
        }

        // Client handler: the server's answer to one request.
        internal static IEnumerator OnClientReceiveResult(long sender, ZPackage package) {
            int requestId = package.ReadInt();
            Dictionary<string, object> result = ReadDictionary(package);

            if (pending.TryGetValue(requestId, out Pending entry)) {
                pending.Remove(requestId);
                Invoke(entry, result);
            }
            yield return null;
        }

        // A consumer's callback must never take the RPC handler down with it.
        private static void Invoke(Pending entry, Dictionary<string, object> result) {
            try {
                entry.Callback(result);
            } catch (Exception e) {
                Logger.LogLocationResetWarning($"A mod's {entry.Op} callback threw and was ignored: {e}");
            }
        }

        private static void StartExpiryLoop() {
            if (expiryRunning) { return; }
            expiryRunning = true;
            TaskRunner.Run().StartCoroutine(ExpirePending());
        }

        // Answers that never arrive -- the server restarted mid-request, the connection dropped --
        // would otherwise hold their callback forever. Expiring them means a caller waiting on a
        // result always hears something back, even if it is a refusal.
        private static IEnumerator ExpirePending() {
            while (pending.Count > 0) {
                yield return new WaitForSeconds(1f);

                List<int> expired = null;
                foreach (KeyValuePair<int, Pending> kvp in pending) {
                    if (Time.realtimeSinceStartup < kvp.Value.Deadline) { continue; }
                    if (expired == null) { expired = new List<int>(); }
                    expired.Add(kvp.Key);
                }
                if (expired == null) { continue; }

                for (int i = 0; i < expired.Count; i++) {
                    Pending entry = pending[expired[i]];
                    pending.Remove(expired[i]);
                    Invoke(entry, Failure(ResetSummary.CodeTimeout,
                        $"the server did not answer the {entry.Op} request in time."));
                }
            }
            expiryRunning = false;
        }

        // Drop everything in flight. A world change invalidates every pending answer, and leaving
        // them to time out one by one would fire stale callbacks into a different world.
        internal static void Reset() {
            // Peer ids are not stable across worlds, so carrying cooldowns over would throttle
            // whoever inherits an id and let whoever does not off entirely.
            lastMutatingRequest.Clear();

            List<Pending> orphans = new List<Pending>(pending.Values);
            pending.Clear();
            for (int i = 0; i < orphans.Count; i++) {
                Invoke(orphans[i], Failure(ResetSummary.CodeDisconnected,
                    "the world was unloaded before the server answered."));
            }
        }

        internal static Dictionary<string, object> Failure(string code, string reason) {
            return ResetSummary.RefusalDictionary(code, reason);
        }

        // ---------------------------------------------------------------------------------------
        // Server side
        // ---------------------------------------------------------------------------------------

        // When each peer last asked for something that changes state. Queries are not counted: they
        // are read-only, and rate-limiting them would break a mod that legitimately polls a few
        // locations to decide whether to act.
        private static readonly Dictionary<long, float> lastMutatingRequest = new Dictionary<long, float>();

        internal static IEnumerator OnServerReceiveRequest(long sender, ZPackage package) {
            if (IsServer == false) { yield break; }

            int requestId = package.ReadInt();
            Op op = (Op)package.ReadByte();
            Dictionary<string, object> args = ReadDictionary(package);

            Dictionary<string, object> result;
            try {
                result = Dispatch(sender, op, args, requestId);
            } catch (Exception e) {
                Logger.LogLocationResetWarning($"A {op} request from peer {sender} failed: {e}");
                result = Failure(ResetSummary.CodeServerError, $"the server failed to handle the {op} request.");
            }

            // A reset answers later, from its own completion callback, so Dispatch returns null for
            // one it accepted. Everything else answers now.
            if (result != null) { Reply(sender, requestId, result); }
            yield return null;
        }

        internal static void Reply(long sender, int requestId, Dictionary<string, object> result) {
            if (IsServer == false) { return; }
            // The peer may have disconnected while a Safe-mode reset was waiting.
            if (ZNet.instance.GetPeer(sender) == null) { return; }

            ZPackage package = new ZPackage();
            package.Write(requestId);
            WriteDictionary(package, result);
            ValConfig.LocationApiResultRPC.SendPackage(sender, package);
        }

        private static Dictionary<string, object> Dispatch(long sender, Op op, Dictionary<string, object> args, int requestId) {
            switch (op) {
                case Op.ResetNamed:
                case Op.ResetRadius:
                    return DispatchReset(sender, op, args, requestId);

                case Op.Register: {
                    if (CooldownBlocked(sender, out string reason)) { return Failure(ResetSummary.CodeCooldown, reason); }
                    NoteMutatingRequest(sender);
                    bool ok = APIReciever.LocalRegister(
                        Str(args, "name"), SourceFor(sender, args), Flt(args, "resetHours"),
                        Str(args, "resetSchedule"), Int(args, "mode"), Bool(args, "resetTerrain"),
                        Flt(args, "terrainRadius"), Flt(args, "extraTerrainRadius"), Bool(args, "resetInterior"),
                        Flt(args, "minDistance"), Flt(args, "maxDistance"), Bool(args, "enabled"));
                    return Scalar(ok);
                }

                case Op.Unregister: {
                    if (CooldownBlocked(sender, out string reason)) { return Failure(ResetSummary.CodeCooldown, reason); }
                    NoteMutatingRequest(sender);
                    return Scalar(LocationResetData.UnregisterAPIResetTarget(Str(args, "name"), SourceFor(sender, args)));
                }

                case Op.LastReset:
                    return Scalar(LocationResetQuery.GetLocationLastReset(Str(args, "name"), Pos(args), Flt(args, "radius")));

                case Op.SecondsUntilDue:
                    return Scalar(LocationResetQuery.GetSecondsUntilDue(Str(args, "name"), Pos(args), Flt(args, "radius")));

                case Op.LocationInfo:
                    return LocationResetQuery.GetLocationInfo(Str(args, "name"), Pos(args), Flt(args, "radius"));

                case Op.ChunkInfo:
                    return LocationResetQuery.GetChunkInfo(Pos(args), Bool(args, "includePrefabs"));

                case Op.Status:
                    return APIReciever.LocalStatus();

                case Op.TargetInfo:
                    return APIReciever.LocalTargetInfo(Str(args, "name"));

                case Op.RegisteredNames:
                    return Scalar(LocationResetData.GetAPIRegisteredNames(Str(args, "sourceId")));

                case Op.IsKnownName:
                    return Scalar(APIReciever.LocalIsKnownName(Str(args, "name")));

                case Op.Ready:
                    return Scalar(APIReciever.LocalReady());

                default:
                    return Failure(ResetSummary.CodeServerError, $"unknown request type {(byte)op}.");
            }
        }

        // Returns null when RequestReset has taken ownership of the answer -- either it accepted the
        // reset, in which case the summary is sent from the completion callback once the routine
        // finishes (minutes later for a Safe request), or it refused and sent that refusal through
        // the same callback. Only the guards in this method answer inline.
        private static Dictionary<string, object> DispatchReset(long sender, Op op, Dictionary<string, object> args, int requestId) {
            string target = op == Op.ResetNamed ? Str(args, "name") : "";
            Vector3 center = Pos(args);
            float requestedRadius = Flt(args, "radius");
            int safety = Int(args, "safety");

            // These two are the server's own envelope on a client request rather than anything the
            // reset machinery knows about, so they are refused here. They still answer in the summary
            // shape, because a caller's follow-up logic reads the same keys either way.
            if (CooldownBlocked(sender, out string cooldown)) {
                return ResetSummary.Refused(ResetSummary.CodeCooldown, cooldown, center, requestedRadius, safety, target).ToDictionary();
            }
            if (WithinReach(sender, center, out string reach) == false) {
                return ResetSummary.Refused(ResetSummary.CodeTooFar, reach, center, requestedRadius, safety, target).ToDictionary();
            }

            float radius = Mathf.Min(requestedRadius, ValConfig.ClientLocationResetMaxRadius.Value);
            if (radius < requestedRadius) {
                Logger.LogLocationReset($"Peer {sender} asked for a {requestedRadius:0}m reset; clamped to {radius:0}m.");
            }

            NoteMutatingRequest(sender);

            LocationResetControl.ResetRequest request = new LocationResetControl.ResetRequest() {
                Center = center,
                Radius = radius,
                Safety = safety == LocationResetControl.SafetyForce
                    ? LocationResetControl.SafetyForce
                    : LocationResetControl.SafetySafe,
                LocationName = target,
                ResetAllMatches = Bool(args, "resetAllMatches"),
                SafeWaitSeconds = Flt(args, "safeWaitSeconds"),
                IncludeDetail = Bool(args, "includeDetail"),
                Source = string.IsNullOrEmpty(target)
                    ? $"API (peer {sender}) r={radius:0}"
                    : $"API (peer {sender}) '{target}'",
            };

            // Captured for the closure rather than read again later: the routine outlives this call,
            // and `sender` and `requestId` are what tie its answer back to the right caller.
            long replyTo = sender;
            int replyId = requestId;

            // Null either way. RequestReset invokes this callback exactly once whatever it decides --
            // on a refusal before returning false, on acceptance when the routine finishes -- so the
            // reply is always sent from in here and answering again below would send a second one.
            LocationResetControl.RequestReset(request, null, (summary) => { Reply(replyTo, replyId, summary); });
            return null;
        }

        // A peer may only ask about ground it is near. Without this, one request could reset content
        // on the far side of the world that nobody is anywhere near, which is both the most abusable
        // shape this RPC has and never what a legitimate caller wants -- a mod resetting a dungeon
        // does it for the player standing in front of one.
        private static bool WithinReach(long sender, Vector3 center, out string reason) {
            reason = null;
            float limit = ValConfig.ClientLocationResetMaxDistance.Value;
            if (limit <= 0f) { return true; }

            ZNetPeer peer = ZNet.instance?.GetPeer(sender);
            if (peer == null) {
                reason = "the requesting peer is no longer connected.";
                return false;
            }

            Vector3 delta = peer.m_refPos - center;
            delta.y = 0f;
            if (delta.magnitude <= limit) { return true; }

            reason = $"the requested position is {delta.magnitude:0}m away, beyond the {limit:0}m a client may reach.";
            Logger.LogLocationResetWarning($"Peer {sender} asked to reset a position {delta.magnitude:0}m from where it is standing; refused.");
            return false;
        }

        private static bool CooldownBlocked(long sender, out string reason) {
            reason = null;
            float cooldown = ValConfig.ClientLocationResetCooldownSeconds.Value;
            if (cooldown <= 0f) { return false; }
            if (lastMutatingRequest.TryGetValue(sender, out float last) == false) { return false; }

            float elapsed = Time.realtimeSinceStartup - last;
            if (elapsed >= cooldown) { return false; }
            reason = $"too soon - a client may ask for one change every {cooldown:0}s ({cooldown - elapsed:0}s left).";
            return true;
        }

        private static void NoteMutatingRequest(long sender) {
            lastMutatingRequest[sender] = Time.realtimeSinceStartup;
        }

        internal static void ForgetPeer(long sender) {
            lastMutatingRequest.Remove(sender);
        }

        // A relayed registration is attributed to the peer as well as to the mod that asked, so a
        // registration that turns up in sls-loc-api can be traced to a machine and not just a GUID.
        private static string SourceFor(long sender, Dictionary<string, object> args) {
            string sourceId = Str(args, "sourceId");
            if (string.IsNullOrWhiteSpace(sourceId)) { sourceId = "unknown"; }
            return $"{sourceId} (client {sender})";
        }

        // ---------------------------------------------------------------------------------------
        // Argument helpers
        // ---------------------------------------------------------------------------------------
        //
        // Everything crossing the wire is optional by construction: an older client talking to a
        // newer server (or the reverse) simply omits a key, and a missing key has to read as its
        // default rather than throwing inside an RPC handler.

        internal static string Str(Dictionary<string, object> args, string key) {
            return args != null && args.TryGetValue(key, out object value) && value is string s ? s : "";
        }

        internal static float Flt(Dictionary<string, object> args, string key) {
            if (args == null || args.TryGetValue(key, out object value) == false || value == null) { return 0f; }
            try { return Convert.ToSingle(value); } catch (Exception) { return 0f; }
        }

        internal static int Int(Dictionary<string, object> args, string key) {
            if (args == null || args.TryGetValue(key, out object value) == false || value == null) { return 0; }
            try { return Convert.ToInt32(value); } catch (Exception) { return 0; }
        }

        internal static bool Bool(Dictionary<string, object> args, string key) {
            return args != null && args.TryGetValue(key, out object value) && value is bool b && b;
        }

        private static Vector3 Pos(Dictionary<string, object> args) {
            return new Vector3(Flt(args, "x"), Flt(args, "y"), Flt(args, "z"));
        }

        internal static Dictionary<string, object> Scalar(object value) {
            return new Dictionary<string, object>() { { ValueKey, value } };
        }

        // ---------------------------------------------------------------------------------------
        // Codec
        // ---------------------------------------------------------------------------------------
        //
        // The API's own result type is Dictionary<string, object> of plain values, so the same codec
        // serves requests and answers in both directions. Tagged rather than schema-per-op: adding a
        // field to a result must not desynchronise a client running an older build, and a tag stream
        // stays readable when one side writes a key the other has never heard of.

        private const byte TagNull = 0;
        private const byte TagBool = 1;
        private const byte TagInt = 2;
        private const byte TagLong = 3;
        private const byte TagFloat = 4;
        private const byte TagDouble = 5;
        private const byte TagString = 6;
        private const byte TagStringList = 7;
        private const byte TagDictList = 8;
        private const byte TagDict = 9;

        internal static void WriteDictionary(ZPackage package, Dictionary<string, object> values) {
            if (values == null) { package.Write(0); return; }
            package.Write(values.Count);
            foreach (KeyValuePair<string, object> kvp in values) {
                package.Write(kvp.Key ?? "");
                WriteValue(package, kvp.Value);
            }
        }

        internal static Dictionary<string, object> ReadDictionary(ZPackage package) {
            Dictionary<string, object> values = new Dictionary<string, object>();
            int count = package.ReadInt();
            for (int i = 0; i < count; i++) {
                string key = package.ReadString();
                values[key] = ReadValue(package);
            }
            return values;
        }

        private static void WriteValue(ZPackage package, object value) {
            if (value == null) { package.Write(TagNull); return; }

            if (value is bool b) { package.Write(TagBool); package.Write(b); return; }
            if (value is int i) { package.Write(TagInt); package.Write(i); return; }
            if (value is long l) { package.Write(TagLong); package.Write(l); return; }
            if (value is float f) { package.Write(TagFloat); package.Write(f); return; }
            if (value is double d) { package.Write(TagDouble); package.Write(d); return; }
            if (value is string s) { package.Write(TagString); package.Write(s); return; }

            if (value is List<string> strings) {
                package.Write(TagStringList);
                package.Write(strings.Count);
                for (int n = 0; n < strings.Count; n++) { package.Write(strings[n] ?? ""); }
                return;
            }
            if (value is List<Dictionary<string, object>> dicts) {
                package.Write(TagDictList);
                package.Write(dicts.Count);
                for (int n = 0; n < dicts.Count; n++) { WriteDictionary(package, dicts[n]); }
                return;
            }
            if (value is Dictionary<string, object> dict) {
                package.Write(TagDict);
                WriteDictionary(package, dict);
                return;
            }

            // Anything else becomes its string form rather than breaking the stream. Nothing in the
            // API produces one, but a value type added later must degrade instead of desynchronising
            // every reader after it.
            package.Write(TagString);
            package.Write(Convert.ToString(value) ?? "");
        }

        private static object ReadValue(ZPackage package) {
            byte tag = package.ReadByte();
            switch (tag) {
                case TagNull: return null;
                case TagBool: return package.ReadBool();
                case TagInt: return package.ReadInt();
                case TagLong: return package.ReadLong();
                case TagFloat: return package.ReadSingle();
                case TagDouble: return package.ReadDouble();
                case TagString: return package.ReadString();
                case TagStringList: {
                    int count = package.ReadInt();
                    List<string> values = new List<string>(count);
                    for (int i = 0; i < count; i++) { values.Add(package.ReadString()); }
                    return values;
                }
                case TagDictList: {
                    int count = package.ReadInt();
                    List<Dictionary<string, object>> values = new List<Dictionary<string, object>>(count);
                    for (int i = 0; i < count; i++) { values.Add(ReadDictionary(package)); }
                    return values;
                }
                case TagDict: return ReadDictionary(package);
                default:
                    // Unreadable tag: the rest of this package cannot be trusted, because we no
                    // longer know how many bytes the value occupied. Stop rather than return garbage.
                    throw new InvalidOperationException($"Unknown Location Reset API value tag {tag}.");
            }
        }
    }
}
