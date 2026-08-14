using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>Pure hysteresis and root/member lifecycle ordering for whole ships.</summary>
    public static class ShipDomainInterestPolicy
    {
        public static IReadOnlyList<long> Members(IEnumerable<long> decks,
            IEnumerable<long> mountedParts) =>
            (decks ?? throw new ArgumentNullException(nameof(decks)))
                .Concat(mountedParts ?? throw new ArgumentNullException(nameof(mountedParts)))
                .Distinct().OrderBy(x => x).ToArray();

        public static bool ShouldBeLoaded(bool rootLoaded, bool protectedByLocalInteraction,
            bool hasAnyCrew,
            FixedPointPosition peerPosition, FixedPointPosition hullPosition,
            double loadRadiusMetres, double unloadRadiusMetres)
        {
            // Remote avatars are still globally checked out. Until player entities
            // join domain lifecycle, any crew makes the whole ship globally visible
            // so an observer cannot retain a floating aboard avatar without its ship.
            if (protectedByLocalInteraction || hasAnyCrew) return true;
            double radius = rootLoaded ? unloadRadiusMetres : loadRadiusMetres;
            return InterestPolicy.InRange(peerPosition, hullPosition, radius);
        }

        public static bool MayServeComponents(bool domainManaged, bool checkedOut) =>
            !domainManaged || checkedOut;

        public static IReadOnlyList<long> AddOrder(long hullEntityId, IEnumerable<long> members) =>
            new[] { hullEntityId }.Concat((members ?? throw new ArgumentNullException(nameof(members)))
                .Where(x => x != hullEntityId).Distinct().OrderBy(x => x)).ToArray();

        public static IReadOnlyList<long> RemoveOrder(long hullEntityId, IEnumerable<long> members) =>
            (members ?? throw new ArgumentNullException(nameof(members)))
                .Where(x => x != hullEntityId).Distinct().OrderByDescending(x => x)
                .Concat(new[] { hullEntityId }).ToArray();
    }
}
