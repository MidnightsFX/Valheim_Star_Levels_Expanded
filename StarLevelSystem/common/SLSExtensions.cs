using HarmonyLib;
using Splatform;
using StarLevelSystem.modules.Modifiers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using UnityEngine.Analytics;
using static StarLevelSystem.common.DataObjects;
using static ZNet;

namespace StarLevelSystem.common
{
    public static class SLSExtensions
    {
        /// <summary>
        /// Take any list of Objects and return it with Fischer-Yates shuffle
        /// </summary>
        /// <returns></returns>
        public static List<T> ShuffleList<T>(this List<T> inputList)
        {
            int i = 0;
            int t = inputList.Count;
            int r = 0;
            T p = default(T);
            List<T> tempList = new List<T>();
            tempList.AddRange(inputList);

            while (i < t)
            {
                r = UnityEngine.Random.Range(i, tempList.Count);
                p = tempList[i];
                tempList[i] = tempList[r];
                tempList[r] = p;
                i++;
            }

            return tempList;
        }

        public static bool CompareListContents<T>(this List<T> listA, List<T> listB)
        {
            if (listA == null && listB == null) return true;
            if (listA == null || listB == null) return false;
            if (listA.Count != listB.Count) return false;

            // Check for items in both lists, regardless of order, extras are left out
            // if both entries are the same count they are equal
            var firstNotSecond = listA.Except(listB).ToList();
            var secondNotFirst = listB.Except(listA).ToList();

            return !firstNotSecond.Any() && !secondNotFirst.Any();
        }

        public static int GetLabelValue(Label label) {
            MethodInfo privateMethod = typeof(Label).GetMethod("GetLabelValue", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)privateMethod.Invoke(label, null);
        }

        public static CodeMatcher CreateLabelOffset(this CodeMatcher matcher, out Label label, int offset = 0)
        {
            return matcher.CreateLabelAt(matcher.Pos + offset, out label);
        }

        public static CodeMatcher ExtractLabel( this CodeMatcher matcher, out Label elabel, int searchrange = 1, int labelToSelect = 0) {
            elabel = default;

            var list = matcher.Instructions();
            int end = Math.Min(list.Count, matcher.Pos + 1 + searchrange);
            bool found = false;
            List<Label> foundLabels = new List<Label>();
            for (int i = matcher.Pos + 1; i < end; i++) {
                var instr = list[i];
                if (instr.labels is { Count: > 0 }) {
                    Logger.LogDebug($"found labels: {instr.labels.Count}");
                    int labelindex = 0;
                    foreach(Label lb in instr.labels) {
                        foundLabels.Add(lb);
                        if (foundLabels.Count == labelToSelect + 1) {
                            Logger.LogDebug($"Selected Label {GetLabelValue(lb)}");
                            instr.labels.RemoveAt(labelindex);
                        }
                        labelindex++;
                    }
                    return matcher; // keep matcher at current position
                }
            }

            if (found == false) {
                throw new InvalidOperationException($"No label found within {searchrange} instructions ahead of position {matcher.Pos}.");
            }
            
            return matcher; // keep matcher at current position
        }

        public static CodeMatcher ExtractLabelOnNextInstructionOfType(this CodeMatcher matcher, CodeInstruction op, out Label label) {
            List<CodeInstruction> list = matcher.Instructions().GetRange(matcher.Pos - 1, matcher.Instructions().Count - matcher.Pos - 1);
            bool matched = false;
            label = default;
            foreach (CodeInstruction ci in list) {
                if (ci != op) { continue; }

                matched = true;
                if (ci.labels.Count > 0) {
                    label = ci.labels[0];
                } else {
                    Logger.LogWarning($"No Labels found on the selected CodeInstruction {ci.opcode}");
                }
            }
            if (!matched) {
                throw new InvalidOperationException($"Did not match an opcode.");
            }

            return matcher; // keep matcher at current position
        }

        public static CodeMatcher SelectLabelInRange(this CodeMatcher matcher, int key, out Label label, int keyindex = 0) {
            List<CodeInstruction> list = matcher.Instructions();
            int index = 0;
            label = default;
            Dictionary<int, List<Label>> labelLoc = new Dictionary<int, List<Label>>();
            foreach (CodeInstruction ci in list) {
                if (ci.labels.Count > 0) {
                    labelLoc.Add(index, ci.labels);
                    Logger.LogDebug($"Found Label at index {index} on {ci.opcode} label-target: {ci.labels[0]}");
                }
                index++;
            }
            if (labelLoc.ContainsKey(key)) {
                if (labelLoc[key].Count > 0) {
                    label = labelLoc[key][keyindex];
                } else {
                    label = labelLoc[key][0];
                }
            } else {
                Logger.LogWarning($"Keyed label was not found with key {key}");
            }

            Logger.LogDebug($"Label indexes {String.Join(",",labelLoc.Keys)} current position: {matcher.Pos}");

            return matcher; // keep matcher at current position
        }


        public static CodeMatcher ExtractFirstLabel(this CodeMatcher matcher, out Label label) {
            label = matcher.Labels.First();
            matcher.Labels.Clear();

            return matcher;
        }


        public static void Times(this int count, Action action)
        {
            for (int i = 0; i < count; i++)
            {
                action();
            }
        }

        public static KeyValuePair<string, List<string>> RandomEntry(Dictionary<string, List<string>> dict, List<string> removedKeys = null) {
            List<string> keys = dict.Keys.ToList();
            if (removedKeys != null) {
                keys = keys.Where(k => !removedKeys.Contains(k)).ToList();
            }
            if (keys.Count == 0) {
                return new KeyValuePair<string, List<string>>(key: CreatureModifiers.NoMods, value: null);
            }
            string key = keys[UnityEngine.Random.Range(0, keys.Count)];
            return new KeyValuePair<string, List<string>>(key: key, value: dict[key]);
        }

        public static List<Character> GetCharactersInRange(Vector3 position, float range)
        {
            // Vanilla's registry walk: no physics query, no component lookups, no duplicate hits from
            // multi-collider creatures. The old Physics.OverlapSphere ran with no layer mask (touching
            // terrain and props) and did a GetComponentInChildren walk per collider - on death paths
            // like SoulEater and per-redirect paths like LifeLink.
            List<Character> characters = new List<Character>();
            Character.GetCharactersInRange(position, range, characters);
            return characters;
        }

        public static List<Player> GetPlayersInRange(Vector3 position, float range) {
            Collider[] objs_near = Physics.OverlapSphere(position, range);
            List<Player> players = new List<Player>();

            foreach (var col in objs_near) {
                var chara = col.GetComponentInChildren<Player>();
                if (chara != null) { players.Add(chara); }
            }

            return players;
        }

        public static List<ZNetPeer> ServerGetPeersInArea(Vector3 pos, float radius) {
            var result = new List<ZNetPeer>();
            if (!ZNet.instance || !ZNet.instance.IsServer())
                return result;

            float radiusSqr = radius * radius;
            foreach (ZNetPeer peer in ZNet.instance.m_peers) {
                if (!peer.IsReady() || peer.m_characterID == ZDOID.None)
                    continue;
                if (Utils.DistanceSqr(peer.m_refPos, pos) <= radiusSqr)
                    result.Add(peer);
            }
            return result;
        }

        internal static float GetTotalDamageOptions(this HitData.DamageTypes hitdmg, bool include_poison = false, bool include_spirit = false, bool include_pickaxe_and_chop = false, float modElement = 1f, float modPhysical = 1f) {
            float physical = (hitdmg.m_damage + hitdmg.m_blunt + hitdmg.m_slash + hitdmg.m_pierce) * modPhysical;
            float elemental = (hitdmg.m_fire + hitdmg.m_frost + hitdmg.m_lightning) * modElement;
            float dmg = physical + elemental;
            if (include_poison) { dmg += (hitdmg.m_poison * modElement); }
            if (include_spirit) { dmg += (hitdmg.m_spirit* modElement); }
            if (include_pickaxe_and_chop) { dmg += hitdmg.m_pickaxe + hitdmg.m_chop; }
            //Logger.LogDebug($"Total Damage calc: {dmg} (with modifiers E:{modElement}, P:{modPhysical}) = true:{hitdmg.m_damage} + blunt:{hitdmg.m_blunt} + slash:{hitdmg.m_slash} + pierce:{hitdmg.m_pierce} + fire:{hitdmg.m_fire} + frost:{hitdmg.m_frost} + Lightning:{hitdmg.m_lightning}");
            //Logger.LogDebug($"Optionals: Poison:{hitdmg.m_poison} Spirit:{hitdmg.m_spirit} Pickaxe:{hitdmg.m_pickaxe} Chop:{hitdmg.m_chop}");
            return dmg;
        }

        public static float EstimateCharacterDamage(Character chara, DamageEstimateType det) {
            if (chara == null || chara.IsPlayer()) return 0;
            Humanoid noid = chara as Humanoid;
            if (noid == null) return 0;
            float dmg = 0;
            float elementMod = 0.5f;

            switch (det) {
                case DamageEstimateType.Highest:
                    foreach (var defweapon in noid.m_defaultItems) {
                        float wepdmg = defweapon.GetComponent<ItemDrop>().m_itemData.m_shared.m_damages.GetTotalDamageOptions(true, true, false, modElement: elementMod);
                        //Logger.LogDebug($"Checking damage of {defweapon.name} - dmg:{wepdmg}");
                        if (wepdmg > dmg) { dmg = wepdmg; }
                    }
                    break;

                case DamageEstimateType.Average:
                    float dmgsum = 0;
                    foreach (var defweapon in noid.m_defaultItems) {
                        float wepdmg = defweapon.GetComponent<ItemDrop>().m_itemData.m_shared.m_damages.GetTotalDamageOptions(true, true, false, modElement: elementMod);
                        dmgsum += wepdmg;
                        //Logger.LogDebug($"Checking damage of {defweapon.name} - dmg:{wepdmg}");
                    }
                    dmg = dmgsum / noid.m_defaultItems.Count();
                    break;

                case DamageEstimateType.Lowest:
                    // Track the minimum properly: comparing against a 0-initialized dmg meant no
                    // non-negative weapon damage could ever be "lower", so Lowest always returned 0.
                    float lowest = float.MaxValue;
                    foreach (var defweapon in noid.m_defaultItems) {
                        float wepdmg = defweapon.GetComponent<ItemDrop>().m_itemData.m_shared.m_damages.GetTotalDamageOptions(true, true, false, modElement: elementMod);
                        //Logger.LogDebug($"Checking damage of {defweapon.name} - dmg:{wepdmg}");
                        if (wepdmg < lowest) { lowest = wepdmg; }
                    }
                    // No default items: leave dmg at 0 so the current-weapon fallback below applies.
                    if (lowest != float.MaxValue) { dmg = lowest; }
                    break;
            }

            // Fallback
            if (dmg == 0) {
                ItemDrop.ItemData item = noid.GetCurrentWeapon();
                if (item != null) {
                    HitData.DamageTypes dmgs = item.GetDamage();
                    // Spirit and Poison get reduced weights here because they are dmg over time primarily and taking into account the whole value immediately results in a dmg spike
                    dmg = dmgs.m_fire + dmgs.m_frost + dmgs.m_lightning + (dmgs.m_spirit / 2) + (dmgs.m_poison / 6) + dmgs.m_blunt + dmgs.m_pierce + dmgs.m_slash;
                }
            }
            dmg = Mathf.Clamp(dmg, 0, 500f);
            if (float.IsNaN(dmg)) { dmg = 100f; }
            if (Logger.IsDebugEnabled) { Logger.LogDebug($"Estimated {chara.m_name} damage as: {dmg}"); }
            return dmg;
        }

        public static SortedDictionary<int, float> MergeSortedDictionary(this SortedDictionary<int, float> primaryDict, SortedDictionary<int, float> otherDict, bool addative = true) {
            foreach (var key in otherDict.Keys) {
                if (primaryDict.ContainsKey(key)) {
                    if (addative) {
                        primaryDict[key] += otherDict[key];
                    } else {
                        primaryDict[key] = otherDict[key];
                    }
                } else {
                    primaryDict.Add(key, otherDict[key]);
                }
            }
            return primaryDict;
        }

        // Merges a biome-specific config over the Biome.All config into a FRESH instance: biome-specific
        // values win where they are set, the All config fills the gaps, and neither input is modified.
        // The previous version wrote the All values INTO the live biome-specific config (inverting the
        // precedence and permanently polluting the loaded settings on every cache build) and returned an
        // object sharing the All config's dictionary references.
        public static BiomeSpecificSetting MergeBiomeConfigs(BiomeSpecificSetting prioritycfg, BiomeSpecificSetting othercfg)
        {
            BiomeSpecificSetting biomecfg = new BiomeSpecificSetting() {
                BiomeMaxLevelOverride = prioritycfg.BiomeMaxLevelOverride != 0 ? prioritycfg.BiomeMaxLevelOverride : othercfg.BiomeMaxLevelOverride,
                BiomeMinLevelOverride = prioritycfg.BiomeMinLevelOverride != 0 ? prioritycfg.BiomeMinLevelOverride : othercfg.BiomeMinLevelOverride,
                DistanceScaleModifier = prioritycfg.DistanceScaleModifier != 1f ? prioritycfg.DistanceScaleModifier : othercfg.DistanceScaleModifier,
                // Biome-specific spawn rate overrides the All-biome value only when explicitly changed.
                SpawnRateModifier = prioritycfg.SpawnRateModifier != 1f ? prioritycfg.SpawnRateModifier : othercfg.SpawnRateModifier,
            };

            SortedDictionary<int, float> levelupSource = prioritycfg.CustomCreatureLevelUpChance ?? othercfg.CustomCreatureLevelUpChance;
            if (levelupSource != null) { biomecfg.CustomCreatureLevelUpChance = new SortedDictionary<int, float>(levelupSource); }

            biomecfg.CreatureBaseValueModifiers = MergeDictionaryPreferPriority(prioritycfg.CreatureBaseValueModifiers, othercfg.CreatureBaseValueModifiers);
            biomecfg.CreaturePerLevelValueModifiers = MergeDictionaryPreferPriority(prioritycfg.CreaturePerLevelValueModifiers, othercfg.CreaturePerLevelValueModifiers);
            biomecfg.DamageRecievedModifiers = MergeDictionaryPreferPriority(prioritycfg.DamageRecievedModifiers, othercfg.DamageRecievedModifiers);

            if (prioritycfg.CreatureSpawnsDisabled != null || othercfg.CreatureSpawnsDisabled != null) {
                List<string> disabled = new List<string>();
                if (othercfg.CreatureSpawnsDisabled != null) { disabled.AddRange(othercfg.CreatureSpawnsDisabled); }
                if (prioritycfg.CreatureSpawnsDisabled != null) { disabled = disabled.Union(prioritycfg.CreatureSpawnsDisabled).ToList(); }
                biomecfg.CreatureSpawnsDisabled = disabled;
            }

            if (prioritycfg.NightSettings != null || othercfg.NightSettings != null) {
                BiomeNightSettings priorityNight = prioritycfg.NightSettings;
                BiomeNightSettings baseNight = othercfg.NightSettings;
                // Need to ensure that we build a fresh config instance instead of modifying one of the sources
                BiomeNightSettings mergedNight = new BiomeNightSettings();

                if (priorityNight != null) {
                    mergedNight.SpawnRateModifier = priorityNight.SpawnRateModifier;
                    mergedNight.NightLevelUpChanceScaler = priorityNight.NightLevelUpChanceScaler;
                } else {
                    mergedNight.SpawnRateModifier = baseNight.SpawnRateModifier;
                    mergedNight.NightLevelUpChanceScaler = baseNight.NightLevelUpChanceScaler;
                }

                List<string> nightDisabled = new List<string>();
                if (baseNight != null && baseNight.CreatureSpawnsDisabled != null) {
                    nightDisabled = nightDisabled.Union(baseNight.CreatureSpawnsDisabled).ToList();
                }
                if (priorityNight != null && priorityNight.CreatureSpawnsDisabled != null) {
                    nightDisabled = nightDisabled.Union(priorityNight.CreatureSpawnsDisabled).ToList();
                }
                mergedNight.CreatureSpawnsDisabled = nightDisabled;

                biomecfg.NightSettings = mergedNight;
            } else {
                biomecfg.NightSettings = null;
            }
            return biomecfg;
        }

        // Fresh dictionary containing the baseline entries with the priority entries overlaid on top.
        // Null when both inputs are null, matching the "section not configured" convention.
        private static Dictionary<TKey, float> MergeDictionaryPreferPriority<TKey>(Dictionary<TKey, float> priority, Dictionary<TKey, float> baseline) {
            if (priority == null && baseline == null) { return null; }
            Dictionary<TKey, float> merged = new Dictionary<TKey, float>();
            if (baseline != null) {
                foreach (KeyValuePair<TKey, float> kvp in baseline) { merged[kvp.Key] = kvp.Value; }
            }
            if (priority != null) {
                foreach (KeyValuePair<TKey, float> kvp in priority) { merged[kvp.Key] = kvp.Value; }
            }
            return merged;
        }

        /// <summary>
        /// Resolve a connected peer to its canonical Splatform identity.
        ///
        /// The host name a socket reports is backend-dependent: ZSteamSocket.GetHostName() returns a bare
        /// numeric SteamID ("76561198..."), while ZPlayFabSocket.GetHostName() returns the already-prefixed
        /// PlatformUserID ("Steam_76561198..."). This mirrors ZNet.UpdatePlayerList exactly, so the id
        /// produced here is identical to the one vanilla puts in ZNet.GetPlayerList() -- which is where every
        /// raid registry key ultimately comes from -- on Steamworks, PlayFab/crossplay, EOS and CustomSocket
        /// alike. Stripping a "Steam_" prefix by hand only ever lined up on Steamworks.
        /// </summary>
        public static PlatformUserID GetPeerPlatformUserID(ZNetPeer peer) {
            if (peer == null || peer.m_socket == null) { return PlatformUserID.None; }

            string hostName = peer.m_socket.GetHostName();
            if (string.IsNullOrEmpty(hostName)) { return PlatformUserID.None; }

            // Steamworks is the only backend whose host name is an unprefixed platform id.
            if (ZNet.m_onlineBackend == OnlineBackendType.Steamworks) {
                Splatform.Platform steamPlatform = ZNet.instance != null ? ZNet.instance.m_steamPlatform : new Splatform.Platform("Steam");
                return new PlatformUserID(steamPlatform, hostName);
            }

            // Every other backend already reports "<Platform>_<id>". TryParse rather than the string ctor:
            // the ctor yields this same PlatformUserID.None on a failed parse, but also emits a
            // UnityEngine.Debug.Log every time -- and a CustomSocket backend reports a bare IP here.
            if (PlatformUserID.TryParse(hostName, out PlatformUserID parsed)) { return parsed; }
            return PlatformUserID.None;
        }

        /// <summary>Find the ready peer whose platform identity matches, or null.</summary>
        public static ZNetPeer GetPeerByPlatformUserID(PlatformUserID target) {
            // The IsValid guard is load-bearing, not defensive: PlatformUserID equality returns true when
            // both sides are invalid, so an unparseable target would match the first peer that also failed
            // to resolve.
            if (ZNet.instance == null || target.IsValid == false) { return null; }

            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) {
                if (peer == null || peer.IsReady() == false) { continue; }
                if (GetPeerPlatformUserID(peer) == target) { return peer; }
            }
            return null;
        }

        /// <summary>
        /// Find the ready peer for a "&lt;Platform&gt;_&lt;id&gt;" key -- the form every ServerPlayerRaidData
        /// key uses.
        /// </summary>
        public static ZNetPeer GetPeerByPlatformID(string platformAndID) {
            if (ZNet.instance == null || string.IsNullOrEmpty(platformAndID)) { return null; }

            if (PlatformUserID.TryParse(platformAndID, out PlatformUserID target) == false) {
                Logger.LogWarning($"'{platformAndID}' is not a platform id, so no peer can be resolved for it.");
                return null;
            }

            ZNetPeer match = GetPeerByPlatformUserID(target);
            if (match == null) {
                // Reaching here means the raid registry and the connected peers genuinely disagree. Name the
                // backend and what the peers actually resolve to, so it is diagnosable from a log alone.
                Logger.LogWarning($"No connected peer resolved to {platformAndID} (backend: {ZNet.m_onlineBackend}). Ready peers: {DescribeReadyPeers()}");
            }
            return match;
        }

        /// <summary>One-line dump of every ready peer's raw socket host name and resolved platform id.</summary>
        internal static string DescribeReadyPeers() {
            if (ZNet.instance == null) { return "<no ZNet>"; }

            List<string> described = new List<string>();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) {
                if (peer == null || peer.IsReady() == false) { continue; }
                string hostName = peer.m_socket == null ? "<no socket>" : peer.m_socket.GetHostName();
                described.Add($"{peer.m_playerName}(uid:{peer.m_uid} host:'{hostName}' id:'{GetPeerPlatformUserID(peer)}')");
            }
            return described.Count == 0 ? "<none>" : string.Join(", ", described);
        }

        /// <summary>
        /// Resolve a peer uid to its platform identity, from the socket -- which is the source vanilla itself
        /// derives ZNet.PlayerInfo.m_userInfo.m_id from.
        ///
        /// Deliberately does not fall back to joining peer.m_characterID against ZNet.GetPlayerList(): that
        /// join is both lossier (m_players is only rebuilt on SendPlayerList) and unsound (m_characterID is
        /// ZDOID.None through death/respawn, and ZDOID.None == ZDOID.None, so two peers in that window can
        /// resolve to each other's identity -- filing one player's private keys or raid cooldown under
        /// another's id).
        /// </summary>
        public static PlatformUserID GetPlatformUserID(long peerID) {
            if (ZNet.instance == null) { return PlatformUserID.None; }

            ZNetPeer peer = ZNet.instance.GetPeer(peerID);
            if (peer == null || peer.IsReady() == false) { return PlatformUserID.None; }

            return GetPeerPlatformUserID(peer);
        }

        public static string GetLocalUserPlatformAndID() {
            IUser local = (IUser)PlatformManager.DistributionPlatform.LocalUser;
            return local.PlatformUserID.ToString();
        }

        public static bool PlatformAndIDIsPlayerOnline(string PlatformAndID) {
            if (ZNet.instance == null || string.IsNullOrEmpty(PlatformAndID)) { return false; }

            foreach (PlayerInfo playerInfo in ZNet.instance.GetPlayerList()) {
                // ToString() rather than building "<platform>_<id>" by hand: the two agree for any valid id,
                // but an invalid one renders as "" here and as "_" by hand -- and "_" is the value that could
                // match a junk registry key.
                if (playerInfo.m_userInfo.m_id.ToString() == PlatformAndID) {
                    return true;
                }
            }
            return false;
        }

        public static Vector3 GetPlayerPosition(ZDOID characterID) {
            if (characterID.IsNone() || ZDOMan.instance == null) return Vector3.zero;

            ZDO zdo = ZDOMan.instance.GetZDO(characterID);
            if (zdo == null) { return Vector3.zero; } 

            return zdo.GetPosition();
        }

        public static List<string> GetPrivateKeysSanitize(this Player player) {
            List<string> keys = player.GetUniqueKeys();
            keys = keys.Where(x => string.IsNullOrEmpty(x) == false).ToList();
            return keys;
        }

        public static ZNetPeer GetNearestReadyPeer(Vector3 pos) {
            if (ZNet.instance == null) { return null; }
            return ZNet.instance.GetPeers()
                .Where(peer => peer != null && peer.IsReady() && peer.m_characterID != ZDOID.None)
                .OrderBy(peer => Utils.DistanceXZ(peer.m_refPos, pos))
                .FirstOrDefault();
        }

    }
}
