using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>Pure hysteresis and root/member lifecycle ordering for whole ships.</summary>
    public static class ShipDomainInterestPolicy
    {
        public const string LoadRadiusEnvVar = "WAREBORN_SHIP_INTEREST_RADIUS_M";
        public const string UnloadRadiusEnvVar = "WAREBORN_SHIP_INTEREST_UNLOAD_RADIUS_M";
        public const double DefaultLoadRadiusMetres = 800d;
        public const double DefaultUnloadRadiusMetres = 1000d;

        public static double LoadRadiusFrom(string? raw) =>
            RadiusFrom(raw, DefaultLoadRadiusMetres, 100d, 10000d);

        public static double UnloadRadiusFrom(string? raw, double loadRadiusMetres)
        {
            double fallback = Math.Max(DefaultUnloadRadiusMetres, loadRadiusMetres + 100d);
            return Math.Max(loadRadiusMetres,
                RadiusFrom(raw, fallback, 100d, 12000d));
        }

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

        /// <summary>
        /// An asset request spans two scheduler turns. Reconciliation may run between
        /// those turns (especially after a long poll-loop stall), so retain the request
        /// only when the same Add is still at the head of the rebuilt queue.
        /// </summary>
        public static long AssetRequestAfterReconcile(long requestedEntityId,
            long? nextAddEntityId) =>
            requestedEntityId != 0 && nextAddEntityId == requestedEntityId
                ? requestedEntityId
                : 0;

        /// <summary>Last-boundary stale-action guard for a per-peer checkout ledger.</summary>
        public static bool ShouldExecute(bool add, bool checkedOut) =>
            add ? !checkedOut : checkedOut;

        public static IReadOnlyList<long> AddOrder(long hullEntityId, IEnumerable<long> members) =>
            new[] { hullEntityId }.Concat((members ?? throw new ArgumentNullException(nameof(members)))
                .Where(x => x != hullEntityId).Distinct().OrderBy(x => x)).ToArray();

        public static IReadOnlyList<long> RemoveOrder(long hullEntityId, IEnumerable<long> members) =>
            (members ?? throw new ArgumentNullException(nameof(members)))
                .Where(x => x != hullEntityId).Distinct().OrderByDescending(x => x)
                .Concat(new[] { hullEntityId }).ToArray();

        private static double RadiusFrom(string? raw, double fallback,
            double minimum, double maximum) =>
            double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                ? Math.Clamp(parsed, minimum, maximum)
                : fallback;
    }
}
