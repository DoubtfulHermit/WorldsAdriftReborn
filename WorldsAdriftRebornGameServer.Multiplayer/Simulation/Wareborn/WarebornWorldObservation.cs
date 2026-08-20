using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn
{
    /// <summary>
    /// One island as the game server saw it this pass.
    /// </summary>
    public readonly struct ObservedIsland
    {
        public ObservedIsland(string islandId, IReadOnlyList<long>? ownedEntityIds)
        {
            if (string.IsNullOrWhiteSpace(islandId))
                throw new ArgumentException("island id is required", nameof(islandId));
            IslandId = islandId.Trim();
            OwnedEntityIds = ownedEntityIds ?? Array.Empty<long>();
        }

        public string IslandId { get; }

        /// <summary>Straight from the island domain's ownership membership.</summary>
        public IReadOnlyList<long> OwnedEntityIds { get; }
    }

    /// <summary>
    /// One live ship hull as the game server saw it this pass.
    /// </summary>
    public readonly struct ObservedShip
    {
        public ObservedShip(
            long hullEntityId,
            IReadOnlyList<long>? memberEntityIds,
            IReadOnlyList<long>? aboardPlayerEntityIds,
            long? pilotPlayerEntityId,
            bool moving,
            string? nearestIslandId,
            double nearestIslandDistanceMetres)
        {
            if (hullEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(hullEntityId));
            HullEntityId = hullEntityId;
            MemberEntityIds = memberEntityIds ?? Array.Empty<long>();
            AboardPlayerEntityIds = aboardPlayerEntityIds ?? Array.Empty<long>();
            PilotPlayerEntityId = pilotPlayerEntityId;
            Moving = moving;
            NearestIslandId = string.IsNullOrWhiteSpace(nearestIslandId) ? null : nearestIslandId!.Trim();
            NearestIslandDistanceMetres = nearestIslandDistanceMetres;
        }

        public long HullEntityId { get; }

        /// <summary>Decks and mounted parts. The hull itself is added by the projection.</summary>
        public IReadOnlyList<long> MemberEntityIds { get; }

        public IReadOnlyList<long> AboardPlayerEntityIds { get; }
        public long? PilotPlayerEntityId { get; }

        /// <summary>Piloted, or under way, or still carrying throttle - anything but parked.</summary>
        public bool Moving { get; }

        public string? NearestIslandId { get; }
        public double NearestIslandDistanceMetres { get; }
    }

    /// <summary>
    /// One connected player as the game server saw it this pass.
    /// </summary>
    public readonly struct ObservedPlayer
    {
        public ObservedPlayer(
            long playerEntityId,
            long? aboardHullEntityId,
            IReadOnlyList<string>? interestedIslandIds)
        {
            if (playerEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(playerEntityId));
            PlayerEntityId = playerEntityId;
            AboardHullEntityId = aboardHullEntityId;
            InterestedIslandIds = interestedIslandIds ?? Array.Empty<string>();
        }

        public long PlayerEntityId { get; }

        /// <summary>
        /// Redundant with <see cref="ObservedShip.AboardPlayerEntityIds"/> on purpose:
        /// the two are read from the same tracker on the same pass, and the projection
        /// deriving containment from the SHIP side only means a player observed aboard
        /// a hull the observer did not enumerate cannot silently invent an edge.
        /// </summary>
        public long? AboardHullEntityId { get; }

        /// <summary>
        /// Islands this player currently holds resource checkout on. The interest
        /// services stay authoritative for checkout; this is a read of their result.
        /// </summary>
        public IReadOnlyList<string> InterestedIslandIds { get; }
    }

    /// <summary>
    /// Everything the shadow model is told about one pass of the world, as plain
    /// values. This is the SEAM: the game server fills it by reading its live
    /// services, and from here down nothing knows what ENet, Unity, a component id or
    /// a checkout is. Immutable, so a projection cannot write back into the world it
    /// was handed.
    /// </summary>
    public sealed class WarebornWorldObservation
    {
        public static readonly WarebornWorldObservation Empty = new WarebornWorldObservation(null, null, null);

        public WarebornWorldObservation(
            IReadOnlyList<ObservedIsland>? islands,
            IReadOnlyList<ObservedShip>? ships,
            IReadOnlyList<ObservedPlayer>? players)
        {
            Islands = islands ?? Array.Empty<ObservedIsland>();
            Ships = ships ?? Array.Empty<ObservedShip>();
            Players = players ?? Array.Empty<ObservedPlayer>();
        }

        public IReadOnlyList<ObservedIsland> Islands { get; }
        public IReadOnlyList<ObservedShip> Ships { get; }
        public IReadOnlyList<ObservedPlayer> Players { get; }
    }
}
