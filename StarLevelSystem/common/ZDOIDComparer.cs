using System.Collections.Generic;

namespace StarLevelSystem.common
{
    // Equality comparer for the per-ZDOID containers, to keep ZDOID.GetHashCode off the lookup path.
    //
    // Vanilla hashes a ZDOID as GetUserID(UserKey).GetHashCode() ^ ID.GetHashCode(), and GetUserID
    // is a bounds-checked index into a static List<long> (ZDOID.cs:97-100). Mono inlines none of it,
    // so every lookup in every ZDOID-keyed container pays a list walk. These containers are evicted
    // from the ZNetView.ResetZDO prefix in CompositeLazyCache, which runs for every object that
    // streams out - a zone crossing near a large base fires it for hundreds of objects in one
    // frame - so the per-lookup cost is multiplied by the batch size.
    //
    // Equality still goes through ZDOID.Equals (IEquatable, a straight UserKey/ID compare with no
    // list access), so keys remain fully distinguished and the containers stay correct. Only the
    // bucket choice changes, and ID is the only input available: UserKey is private and UserID just
    // reads the same list again.
    //
    // The tradeoff: ZDOID.ID is a per-creator-peer counter, so on a dedicated server creatures
    // spawned by different players can share a low ID and land in the same bucket, separated by a
    // two-int Equals each. Where every entry shares one creator - single player, or any single
    // peer's objects - the spread is unchanged: vanilla's xor with a then-constant UserID hash is a
    // bijection, so it produces the same distinct values, merely permuted.
    //
    // Hashing ID is also strictly more stable than vanilla's. ZDOID.Reset() clears and rebuilds
    // m_userIDs, and it runs from ZDOMan's constructor - so on every world load, the same UserKey
    // can map to a different long. A key already stored in a live container would then hash to a
    // different bucket than it was filed under and become unfindable. ID cannot drift that way.
    internal sealed class ZDOIDComparer : IEqualityComparer<ZDOID>
    {
        internal static readonly ZDOIDComparer Instance = new ZDOIDComparer();

        private ZDOIDComparer() { }

        public bool Equals(ZDOID a, ZDOID b) => a.Equals(b);

        public int GetHashCode(ZDOID id) => (int)id.ID;
    }
}
