using HarmonyLib;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using StarLevelSystem.modules.AnimationAndSpeed;
using StarLevelSystem.modules.Damage;
using StarLevelSystem.modules.Health;
using StarLevelSystem.modules.LevelSystem;
using StarLevelSystem.modules.Modifiers;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules.CreatureSetup {

    // The four stat dictionaries of a creature's cache entry, each persisted under its own ZDO key.
    // Wire format of RPC_SetStat; append only.
    internal enum StatGroup { Base, PerLevel, DamageTaken, DamageBonus }

    // Sets stats on one live creature, for sls-creature-setstat and the API's attribute setters (see Apply).
    //
    // The value is the creature's own base for that stat - what its LevelSettings creature entry would set - and is
    // stored on its ZDO under the same keys Nemesis spawns use, so every later cache build applies it again. Modifiers
    // stack on top of it the way they do on a configured value: the change rebuilds the cache entry and re-runs
    // modifier setup on it. Written into the existing entry instead, which modifiers have already added to, the value
    // would hold until the next rebuild and then gain the modifiers' share a second time.
    //
    // The command is sent to every peer that has the creature loaded rather than only to its owner. Size is a transform
    // scale each peer sets on its own instance, and damage taken and damage bonuses are read from the hitting peer's
    // cache entry, so a peer that only saw the ZDO change would keep the old size and resistances until it next rebuilt
    // the entry. Only the owner writes the ZDO; everyone else changes their own view.
    internal static class CreatureStatOverrides {

        private const string RPC_SetStat = "SLS_SetStat";

        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static class RegisterSetStatRPC {
            private static void Postfix(Character __instance) {
                // Same condition vanilla registers its own Character RPCs under.
                if (__instance.IsPlayer() || __instance.m_nview == null || __instance.m_nview.GetZDO() == null) { return; }
                __instance.m_nview.Register<ZPackage>(RPC_SetStat, (long sender, ZPackage call) => Receive(__instance, call));
            }
        }

        private static Type StatType(StatGroup group) {
            switch (group) {
                case StatGroup.Base: return typeof(CreatureBaseAttribute);
                case StatGroup.PerLevel: return typeof(CreaturePerLevelAttribute);
                default: return typeof(DamageType);
            }
        }

        internal static List<string> StatNames(StatGroup group) => Enum.GetNames(StatType(group)).ToList();

        internal static string StatName(StatGroup group, int stat) => Enum.GetName(StatType(group), stat) ?? stat.ToString();

        // By name only. Enum.TryParse also takes any number, defined or not.
        internal static bool TryParseStat(StatGroup group, string raw, out int stat) {
            stat = -1;
            Type type = StatType(group);
            string name = Enum.GetNames(type).FirstOrDefault(n => string.Equals(n, raw, StringComparison.OrdinalIgnoreCase));
            if (name == null) { return false; }
            stat = (int)Enum.Parse(type, name);
            return true;
        }

        // Null when the value is usable, otherwise why not.
        internal static string Validate(StatGroup group, int stat, float value) {
            if (group == StatGroup.PerLevel) { return null; }
            if (group == StatGroup.Base && (stat == (int)CreatureBaseAttribute.BaseHealth || stat == (int)CreatureBaseAttribute.Size)) {
                return value > 0f ? null : $"{StatName(group, stat)} must be above 0.";
            }
            return value >= 0f ? null : $"{StatName(group, stat)} cannot be negative.";
        }

        // The stat as the creature has it now, modifiers included. A damage type the entry has no value for takes no
        // extra damage and gets no bonus.
        internal static float CurrentValue(CharacterCacheEntry entry, StatGroup group, int stat) {
            float value;
            switch (group) {
                case StatGroup.Base:
                    return entry.CreatureBaseValueModifiers.TryGetValue((CreatureBaseAttribute)stat, out value) ? value : 1f;
                case StatGroup.PerLevel:
                    return entry.CreaturePerLevelValueModifiers.TryGetValue((CreaturePerLevelAttribute)stat, out value) ? value : 0f;
                case StatGroup.DamageTaken:
                    return entry.DamageRecievedModifiers.TryGetValue((DamageType)stat, out value) ? value : 1f;
                default:
                    return entry.CreatureDamageBonus.TryGetValue((DamageType)stat, out value) ? value : 0f;
            }
        }

        // The stats of a group this creature carries its own value for, rather than the configured one.
        internal static HashSet<int> StoredStats(Character chara, StatGroup group) {
            HashSet<int> stored = new HashSet<int>();
            ZDO zdo = chara.m_nview.GetZDO();
            switch (group) {
                case StatGroup.Base:
                    AddKeys(stored, CompositeLazyCache.ReadPersistedStats<CreatureBaseAttribute>(zdo, SLS_BASE_STATS, chara.name));
                    break;
                case StatGroup.PerLevel:
                    AddKeys(stored, CompositeLazyCache.ReadPersistedStats<CreaturePerLevelAttribute>(zdo, SLS_PERLEVEL_STATS, chara.name));
                    break;
                case StatGroup.DamageTaken:
                    AddKeys(stored, CompositeLazyCache.ReadPersistedStats<DamageType>(zdo, SLS_DMGRECV_STATS, chara.name));
                    break;
                default:
                    AddKeys(stored, CompositeLazyCache.ReadPersistedStats<DamageType>(zdo, SLS_DMGBONUS_STATS, chara.name));
                    break;
            }
            return stored;
        }

        private static void AddKeys<TKey>(HashSet<int> into, Dictionary<TKey, float> stats) where TKey : Enum {
            if (stats == null) { return; }
            foreach (TKey key in stats.Keys) { into.Add(Convert.ToInt32(key)); }
        }

        // Sets the stat to `value`, or with reset drops the creature's own value so the configured one applies again,
        // on every peer that has the creature loaded. This peer handles it before the call returns.
        internal static void Send(Character chara, StatGroup group, int stat, bool reset, float value) {
            ZNetView nview = chara.m_nview;
            if (nview == null || nview.IsValid() == false) { return; }
            // Nobody would store it otherwise. The same rule as the API setters, see APIOwnerRelay.
            APIOwnerRelay.ClaimIfUnowned(chara);
            ZPackage call = new ZPackage();
            call.Write((int)group);
            call.Write(stat);
            call.Write(reset);
            call.Write(value);
            nview.InvokeRPC(ZNetView.Everybody, RPC_SetStat, call);
        }

        private static void Receive(Character chara, ZPackage call) {
            StatGroup group = (StatGroup)call.ReadInt();
            int stat = call.ReadInt();
            bool reset = call.ReadBool();
            float value = call.ReadSingle();
            Apply(chara, group, new Dictionary<int, float?>() { { stat, reset ? (float?)null : value } });
        }

        // Sets stats of one group on this peer's view of the creature and, on its owner, stores them on the ZDO. A null
        // value is a reset. Shared with the API setters, which reach the owner through APIOwnerRelay instead of an RPC
        // to everyone. False when the creature has no live ZDO to work from.
        internal static bool Apply(Character chara, StatGroup group, Dictionary<int, float?> stats) {
            if (chara == null || chara.m_nview == null || chara.m_nview.IsValid() == false) { return false; }
            bool owner = chara.m_nview.IsOwner();
            // The owner writes first, so its rebuild below already reads the new values back.
            if (owner) { Store(chara, group, stats); }

            CharacterCacheEntry current = CompositeLazyCache.GetCacheEntry(chara);
            Dictionary<(StatGroup group, int stat), float?> values = new Dictionary<(StatGroup group, int stat), float?>();
            if (owner == false && current != null && Unsaved.TryGetValue(current, out Dictionary<(StatGroup group, int stat), float?> earlier)) {
                foreach (KeyValuePair<(StatGroup group, int stat), float?> stat in earlier) { values[stat.Key] = stat.Value; }
            }
            foreach (KeyValuePair<int, float?> stat in stats) { values[(group, stat.Key)] = stat.Value; }

            // Not rolled, so not set up yet: setup builds from the ZDO and adds the modifiers on top itself. Running
            // modifier setup now would mark it done on an entry with no modifiers yet, and a rebuild would drop the level
            // and modifier requirements a spawner cached for that setup. An entry already cached for it has no
            // modifier share in it yet, so it takes the values as they are.
            if (CompositeLazyCache.HasRolledSetup(chara.m_nview.GetZDO()) == false) {
                if (current != null && current.RunOnceDone == false) { SetInEntry(chara, current, values, owner); }
                return true;
            }

            CharacterCacheEntry entry = CompositeLazyCache.GetAndSetLocalCache(chara, updateCache: true);
            if (entry == null || entry.Level <= 0) { return true; }
            // Anyone else rebuilt from a ZDO the owner's write may not have reached yet.
            SetInEntry(chara, entry, values, owner);

            // A fresh entry has not had its modifiers' stat changes yet; these add them on top of the new values.
            CreatureModifiers.RunOnceModifierSetup(chara, entry);
            CreatureModifiers.SetupModifiers(chara, entry, entry.CreatureModifiers);
            SpeedModifications.ApplySpeedModifications(chara, entry);
            // The multiplier the owner's attacks read, rewritten for a new base damage only, since rewriting it drops
            // whatever SoulEater has grown it by. Set directly: ApplyDamageModification skips a multiplier of 1 as
            // "no change", which here would leave the old one stored.
            if (owner && group == StatGroup.Base && stats.ContainsKey((int)CreatureBaseAttribute.BaseDamage)) {
                chara.m_nview.GetZDO().Set(SLS_DAMAGE_MODIFIER, entry.CreatureBaseValueModifiers[CreatureBaseAttribute.BaseDamage]);
            }
            SizeModifications.SetSizeModification(chara.gameObject, chara.m_nview, entry, true);
            HealthModifications.ForceApplyHealthModifications(chara, entry);
            UIHudControl.InvalidateCacheEntry(chara);
            return true;
        }

        // The values a non-owner set, by the cache entry they went into, until the owner's write reaches its ZDO. Each
        // rebuild in Apply sets them again, so a second call in a row does not lose the first one's values to a rebuild
        // from a ZDO that does not carry them yet. Any other rebuild drops them with the entry it replaces; by then the
        // ZDO has caught up.
        private static readonly ConditionalWeakTable<CharacterCacheEntry, Dictionary<(StatGroup group, int stat), float?>> Unsaved =
            new ConditionalWeakTable<CharacterCacheEntry, Dictionary<(StatGroup group, int stat), float?>>();

        private static void SetInEntry(Character chara, CharacterCacheEntry entry, Dictionary<(StatGroup group, int stat), float?> values, bool owner) {
            foreach (KeyValuePair<(StatGroup group, int stat), float?> stat in values) {
                SetInEntry(chara, entry, stat.Key.group, stat.Key.stat, stat.Value);
            }
            SpeedModifications.InvalidateAttackSpeed(entry);
            if (owner) { return; }
            Unsaved.Remove(entry);
            Unsaved.Add(entry, values);
        }

        private static void Store(Character chara, StatGroup group, Dictionary<int, float?> stats) {
            switch (group) {
                case StatGroup.Base:
                    Store<CreatureBaseAttribute>(chara, SLS_BASE_STATS, stats);
                    break;
                case StatGroup.PerLevel:
                    Store<CreaturePerLevelAttribute>(chara, SLS_PERLEVEL_STATS, stats);
                    break;
                case StatGroup.DamageTaken:
                    Store<DamageType>(chara, SLS_DMGRECV_STATS, stats);
                    break;
                default:
                    Store<DamageType>(chara, SLS_DMGBONUS_STATS, stats);
                    break;
            }
        }

        // The values in one write, then each reset.
        private static void Store<TKey>(Character chara, string key, Dictionary<int, float?> stats) where TKey : Enum {
            Dictionary<TKey, float> values = new Dictionary<TKey, float>();
            foreach (KeyValuePair<int, float?> stat in stats) {
                TKey typed = (TKey)Enum.ToObject(typeof(TKey), stat.Key);
                if (stat.Value.HasValue) {
                    values[typed] = stat.Value.Value;
                } else {
                    CompositeLazyCache.ClearStatOverride(chara, key, typed);
                }
            }
            CompositeLazyCache.PersistStatOverrides(chara, key, values);
        }

        // A null value is a reset, which puts back what the configuration gives this creature. A creature with no
        // configured value for the stat (a damage bonus, say) loses it from the entry.
        private static void SetInEntry(Character chara, CharacterCacheEntry entry, StatGroup group, int stat, float? value) {
            BiomeSpecificSetting biomeSettings = null;
            CreatureSpecificSetting creatureSettings = null;
            if (value.HasValue == false) {
                LevelSelection.SelectCreatureBiomeSettings(chara.gameObject, out _, out creatureSettings, out biomeSettings, out _);
            }
            switch (group) {
                case StatGroup.Base:
                    SetInEntry(entry.CreatureBaseValueModifiers, (CreatureBaseAttribute)stat, value,
                        () => DamageModifications.DetermineCreatureBaseStats(biomeSettings, creatureSettings));
                    break;
                case StatGroup.PerLevel:
                    SetInEntry(entry.CreaturePerLevelValueModifiers, (CreaturePerLevelAttribute)stat, value,
                        () => DamageModifications.DetermineCharacterPerLevelStats(biomeSettings, creatureSettings, chara.IsBoss()));
                    break;
                case StatGroup.DamageTaken:
                    SetInEntry(entry.DamageRecievedModifiers, (DamageType)stat, value,
                        () => DamageModifications.DetermineCreatureDamageRecievedModifiers(biomeSettings, creatureSettings));
                    break;
                default:
                    SetInEntry(entry.CreatureDamageBonus, (DamageType)stat, value, () => new Dictionary<DamageType, float>());
                    break;
            }
        }

        private static void SetInEntry<TKey>(Dictionary<TKey, float> stats, TKey stat, float? value, Func<Dictionary<TKey, float>> configured) {
            if (value.HasValue) {
                stats[stat] = value.Value;
                return;
            }
            if (configured().TryGetValue(stat, out float configuredValue)) {
                stats[stat] = configuredValue;
            } else {
                stats.Remove(stat);
            }
        }
    }
}
