using HarmonyLib;
using StarLevelSystem.common;
using System;
using System.Collections.Generic;

namespace StarLevelSystem.modules {

    // Makes a creature API call persistent whichever peer makes it. The APIReciever setters write the creature's ZDO
    // only on its owner (strict ZDO-owner authority: a non-owner's write races the owner's through vanilla's
    // highest-revision-wins sync, and carries the sender's view of who owns it), so on any other peer they would only
    // change that peer's own view. Each setter therefore runs where it was called, for an immediate local view, and is
    // then replayed on the owner, which persists it. InvokeRPC routes to the current owner and runs in place when that
    // is this peer.
    //
    // A creature with no owner is claimed instead, before the setter runs, so the write lands in the same call. Nobody
    // simulates an unowned creature, so the claim takes it from no one - unlike claiming one another player is
    // simulating, which is what the server then has to undo. API calls are explicit and rare, not the per-load claim
    // the setup queue used to make.
    internal static class APIOwnerRelay {

        private const string RPC_ApiRelay = "SLS_ApiRelay";
        // A replayed call that reaches a peer which has meanwhile lost the creature is passed on to the owner that peer
        // now sees, at most this many times, so two peers that briefly disagree about the owner cannot bounce it forever.
        private const int MaxHops = 3;

        // Wire format of a replayed call; append only, peers of one version always match.
        internal enum Op {
            SetLevel,
            SetSpawnManaged,
            UpdateBaseAttribute,
            SetAllBaseAttributes,
            UpdatePerLevelAttribute,
            SetAllPerLevelAttributes,
            UpdateDamageRecieved,
            SetAllDamageRecieved,
            UpdateDamageBonus,
            SetAllDamageBonus,
            ApplyUpdates,
            AddModifier,
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class RegisterApiRelayRPC {
            private static void Postfix(Character __instance) {
                // Same condition vanilla registers its own Character RPCs under.
                if (__instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.GetZDO() == null) { return; }
                __instance.m_nview.Register<ZPackage>(RPC_ApiRelay, (long sender, ZPackage call) => Replay(__instance, call));
            }
        }

        // First thing every API setter does: take an unowned creature so the setter's own writes persist.
        internal static void ClaimIfUnowned(Character chara) {
            ZNetView nview = chara != null ? chara.m_nview : null;
            if (nview == null || nview.IsValid() == false || nview.GetZDO().HasOwner()) { return; }
            nview.ClaimOwnership();
        }

        // Last thing every API setter does: replay the call on the owner when that is another peer. Nothing to do on the
        // owner itself, whose setter already wrote the ZDO.
        internal static void SendToOwner(Character chara, Op op, Action<ZPackage> writeArgs = null) {
            ZNetView nview = chara != null ? chara.m_nview : null;
            if (nview == null || nview.IsValid() == false || nview.IsOwner()) { return; }
            ZPackage args = new ZPackage();
            writeArgs?.Invoke(args);
            Send(nview, 0, op, args);
        }

        internal static void WriteAttributes(ZPackage package, Dictionary<int, float> attributes) {
            package.Write(attributes.Count);
            foreach (KeyValuePair<int, float> attribute in attributes) {
                package.Write(attribute.Key);
                package.Write(attribute.Value);
            }
        }

        private static Dictionary<int, float> ReadAttributes(ZPackage package) {
            int count = package.ReadInt();
            Dictionary<int, float> attributes = new Dictionary<int, float>(count);
            for (int i = 0; i < count; i++) {
                int key = package.ReadInt();
                attributes[key] = package.ReadSingle();
            }
            return attributes;
        }

        private static void Send(ZNetView nview, int hops, Op op, ZPackage args) {
            ZPackage call = new ZPackage();
            call.Write(hops);
            call.Write((int)op);
            call.Write(args);
            nview.InvokeRPC(RPC_ApiRelay, call);
        }

        private static void Replay(Character chara, ZPackage call) {
            if (chara == null || chara.m_nview == null || chara.m_nview.IsValid() == false) { return; }
            int hops = call.ReadInt();
            Op op = (Op)call.ReadInt();
            ZPackage args = call.ReadPackage();

            ZNetView nview = chara.m_nview;
            if (nview.IsOwner() == false) {
                // Ownership moved while the call was in flight. Follow it rather than drop the change.
                if (nview.GetZDO().HasOwner()) {
                    if (hops < MaxHops) {
                        Send(nview, hops + 1, op, args);
                    } else {
                        Logger.LogWarning($"SLS-API: dropping a replayed {op} for {chara.name}: its owner kept changing while the call was in flight.");
                    }
                    return;
                }
                // Unowned by now: the same rule the caller follows. The setter below claims it.
            }

            switch (op) {
                case Op.SetLevel:
                    APIReciever.UpdateCreatureLevel(chara, args.ReadInt());
                    break;
                case Op.SetSpawnManaged:
                    APIReciever.SetCreatureSpawnManaged(chara, args.ReadBool());
                    break;
                case Op.UpdateBaseAttribute: {
                        int attribute = args.ReadInt();
                        APIReciever.UpdateCreatureBaseAttributes(chara, attribute, args.ReadSingle());
                        break;
                    }
                case Op.SetAllBaseAttributes:
                    APIReciever.SetAllBaseAttributes(chara, ReadAttributes(args));
                    break;
                case Op.UpdatePerLevelAttribute: {
                        int attribute = args.ReadInt();
                        APIReciever.UpdateCreaturePerLevelAttributes(chara, attribute, args.ReadSingle());
                        break;
                    }
                case Op.SetAllPerLevelAttributes:
                    APIReciever.SetAllPerLevelAttributes(chara, ReadAttributes(args));
                    break;
                case Op.UpdateDamageRecieved: {
                        int attribute = args.ReadInt();
                        APIReciever.UpdateCreatureDamageRecievedModifier(chara, attribute, args.ReadSingle());
                        break;
                    }
                case Op.SetAllDamageRecieved:
                    APIReciever.SetAllDamageRecievedModifiers(chara, ReadAttributes(args));
                    break;
                case Op.UpdateDamageBonus: {
                        int attribute = args.ReadInt();
                        APIReciever.UpdateCreatureDamageBonus(chara, attribute, args.ReadSingle());
                        break;
                    }
                case Op.SetAllDamageBonus:
                    APIReciever.SetAllDamageBonus(chara, ReadAttributes(args));
                    break;
                case Op.ApplyUpdates:
                    APIReciever.ApplyUpdatesToCreature(chara);
                    break;
                case Op.AddModifier: {
                        string modifierName = args.ReadString();
                        int modifierType = args.ReadInt();
                        APIReciever.AddModifierToCreature(chara, modifierName, modifierType, args.ReadBool());
                        break;
                    }
                default:
                    Logger.LogWarning($"SLS-API: unknown replayed call {(int)op} for {chara.name}.");
                    break;
            }
        }
    }
}
