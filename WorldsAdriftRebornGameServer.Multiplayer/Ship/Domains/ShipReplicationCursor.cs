using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains
{
    /// <summary>Internal ordering stamp for one coherent ship-domain replication frame.</summary>
    public readonly record struct ShipReplicationStamp(
        long HullEntityId,
        long AuthorityGeneration,
        long Sequence);

    /// <summary>
    /// Monotonic per-hull replication ordering. A new authority generation restarts
    /// the sequence at one; an old authority can never publish after a handoff.
    /// This metadata stays internal for the legacy client, which continues receiving
    /// the exact same component updates in root-then-members order.
    /// </summary>
    public sealed class ShipReplicationCursor
    {
        private readonly Dictionary<long, (long Generation, long Sequence)> _state = new();

        public bool TryNext(long hullEntityId, long authorityGeneration, out ShipReplicationStamp stamp)
        {
            stamp = default;
            if (hullEntityId <= 0 || authorityGeneration <= 0)
            {
                return false;
            }

            if (_state.TryGetValue(hullEntityId, out var current))
            {
                if (authorityGeneration < current.Generation)
                {
                    return false;
                }

                long next = authorityGeneration == current.Generation ? current.Sequence + 1 : 1;
                _state[hullEntityId] = (authorityGeneration, next);
                stamp = new ShipReplicationStamp(hullEntityId, authorityGeneration, next);
                return true;
            }

            _state.Add(hullEntityId, (authorityGeneration, 1));
            stamp = new ShipReplicationStamp(hullEntityId, authorityGeneration, 1);
            return true;
        }

        public void Forget(long hullEntityId) => _state.Remove(hullEntityId);
    }

    /// <summary>Whole-domain delivery invariants, kept engine-free for tests.</summary>
    public static class ShipDomainDeliveryPolicy
    {
        public static bool RootTargetsHull(long hullEntityId, long rootEntityId, long? auxiliaryEntityId)
            => hullEntityId > 0
                && rootEntityId == hullEntityId
                && (!auxiliaryEntityId.HasValue || auxiliaryEntityId.Value == hullEntityId);

        /// <summary>
        /// A member update is never delivered independently: the domain must be
        /// relevant, its root update must have reached this peer in this frame, and
        /// the member entity/component must already be checked out.
        /// </summary>
        public static bool DeliverMember(
            bool domainRelevant,
            bool rootDelivered,
            bool auxiliaryRequired,
            bool auxiliaryDelivered,
            bool memberCheckedOut)
            => domainRelevant
                && rootDelivered
                && (!auxiliaryRequired || auxiliaryDelivered)
                && memberCheckedOut;
    }
}
