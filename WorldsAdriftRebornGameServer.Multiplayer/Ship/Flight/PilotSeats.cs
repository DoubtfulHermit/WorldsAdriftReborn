using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>What a Man interaction on a helm resolved to.</summary>
    public enum ManOutcome
    {
        /// <summary>The player took the helm. Push 1109 driving, start the flight.</summary>
        StartPiloting,

        /// <summary>
        /// The player was already at THIS helm and manned it again - the toggle
        /// dismount. Push 1109 cleared, settle the flight. This is the primary
        /// dismount path: while driving, the ship-controls InputSink deliberately
        /// leaves the Interact button free (ShipControlsBehaviour.Awake excludes
        /// InputButtons.Interact), so the helm right in front of the pilot can be
        /// interacted with again.
        /// </summary>
        StopPiloting,

        /// <summary>Someone else is at this helm. Nothing changes.</summary>
        RejectedOccupied,

        /// <summary>
        /// The player is already piloting a DIFFERENT hull. Nothing changes -
        /// they must dismount first (and while driving they cannot walk to
        /// another helm anyway, so this is a wire-spoof guard, not a UX path).
        /// </summary>
        RejectedAlreadyPiloting,
    }

    /// <summary>
    /// Who is at which helm, pure and unit-tested: hull -> pilot and
    /// pilot -> (hull, helm), kept in lockstep. ONE pilot per hull, ONE hull per
    /// pilot; every transition goes through <see cref="TryMan"/> or
    /// <see cref="Release"/> so the two maps cannot drift.
    /// </summary>
    public sealed class PilotSeats
    {
        public readonly struct Seat
        {
            public Seat(long playerEntityId, long helmEntityId, long hullEntityId)
            {
                PlayerEntityId = playerEntityId;
                HelmEntityId = helmEntityId;
                HullEntityId = hullEntityId;
            }

            public long PlayerEntityId { get; }
            public long HelmEntityId { get; }
            public long HullEntityId { get; }
        }

        private readonly Dictionary<long, Seat> _byHull = new Dictionary<long, Seat>();
        private readonly Dictionary<long, Seat> _byPlayer = new Dictionary<long, Seat>();

        /// <summary>
        /// A Man interaction: seats the player, toggles them off their own helm,
        /// or rejects. On StartPiloting/StopPiloting the ledgers are already
        /// updated when this returns.
        /// </summary>
        public ManOutcome TryMan(long playerEntityId, long helmEntityId, long hullEntityId)
        {
            if (_byPlayer.TryGetValue(playerEntityId, out Seat current))
            {
                if (current.HullEntityId == hullEntityId)
                {
                    // Their own helm again: the toggle dismount.
                    RemoveSeat(current);
                    return ManOutcome.StopPiloting;
                }
                return ManOutcome.RejectedAlreadyPiloting;
            }

            if (_byHull.ContainsKey(hullEntityId))
            {
                return ManOutcome.RejectedOccupied;
            }

            Seat seat = new Seat(playerEntityId, helmEntityId, hullEntityId);
            _byHull[hullEntityId] = seat;
            _byPlayer[playerEntityId] = seat;
            return ManOutcome.StartPiloting;
        }

        /// <summary>
        /// Unseats a player (release-interaction, disconnect). Returns the seat
        /// they held, or null when they were not piloting.
        /// </summary>
        public Seat? Release(long playerEntityId)
        {
            if (!_byPlayer.TryGetValue(playerEntityId, out Seat seat))
            {
                return null;
            }
            RemoveSeat(seat);
            return seat;
        }

        /// <summary>The seat a player holds, or null.</summary>
        public Seat? SeatOf(long playerEntityId)
        {
            return _byPlayer.TryGetValue(playerEntityId, out Seat seat) ? seat : (Seat?)null;
        }

        /// <summary>The pilot of a hull, or null when the helm is free.</summary>
        public Seat? PilotOf(long hullEntityId)
        {
            return _byHull.TryGetValue(hullEntityId, out Seat seat) ? seat : (Seat?)null;
        }

        public int Count => _byPlayer.Count;

        private void RemoveSeat(Seat seat)
        {
            _byHull.Remove(seat.HullEntityId);
            _byPlayer.Remove(seat.PlayerEntityId);
        }
    }
}
