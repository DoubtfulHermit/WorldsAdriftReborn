namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// What changed about a player's ship the moment one 1073 update was ingested.
    /// </summary>
    public enum AboardChange
    {
        /// <summary>No change: same ship (or still not aboard), or the update carried no aboard-bearing field.</summary>
        None,

        /// <summary>The player was not aboard and now is. <see cref="AboardTransition.ShipRootEntityId"/> is the ship.</summary>
        Boarded,

        /// <summary>The player was aboard and now is not. <see cref="AboardTransition.PreviousShipRootEntityId"/> is the ship left.</summary>
        Disembarked,

        /// <summary>
        /// The player stepped straight from one ship onto another without a
        /// not-aboard sample in between. Both ids are set. Not reachable with a
        /// single ship, but modelled so a second ship cannot silently look like a
        /// board with no matching leave.
        /// </summary>
        ChangedShip,
    }

    /// <summary>The result of ingesting one update into <see cref="AboardTracker"/>.</summary>
    public readonly struct AboardTransition
    {
        public AboardTransition(AboardChange change, long shipRootEntityId, long previousShipRootEntityId)
        {
            Change = change;
            ShipRootEntityId = shipRootEntityId;
            PreviousShipRootEntityId = previousShipRootEntityId;
        }

        public AboardChange Change { get; }

        /// <summary>The ship now aboard (Boarded / ChangedShip); 0 otherwise.</summary>
        public long ShipRootEntityId { get; }

        /// <summary>The ship just left (Disembarked / ChangedShip); 0 otherwise.</summary>
        public long PreviousShipRootEntityId { get; }

        public static readonly AboardTransition NoChange = new AboardTransition(AboardChange.None, 0, 0);

        public override string ToString() => Change switch
        {
            AboardChange.Boarded => "boarded ship " + ShipRootEntityId,
            AboardChange.Disembarked => "disembarked ship " + PreviousShipRootEntityId,
            AboardChange.ChangedShip => "moved from ship " + PreviousShipRootEntityId + " to ship " + ShipRootEntityId,
            _ => "no change",
        };
    }

    /// <summary>
    /// Who is aboard which ship, tracked from the 1073 stream. This is the piece
    /// the flight publisher, the abandonment timer and the eventual pilot grant
    /// all consume: "is anyone on ship X" and "which ship is player P on".
    ///
    /// It exists because a single 1073 update cannot answer either question -
    /// see <see cref="AboardSample"/>. relativeTo and relativeBias arrive only when
    /// they CHANGE, so this accumulates them per player into a resolved state,
    /// re-evaluates <see cref="AboardPolicy"/> on every ingest, and emits a
    /// transition only on the edges. A player standing still on a deck sends
    /// updates that touch neither field; those are correctly a no-op that leaves
    /// them aboard, not a spurious disembark.
    ///
    /// Pure and allocation-light on the hot path (one dictionary lookup, no
    /// allocation unless the player is new or actually changes ship). NOT
    /// thread-safe, in the mold of the other server-state holders: one poll loop.
    /// </summary>
    public sealed class AboardTracker
    {
        private sealed class PlayerRelativeState
        {
            // Accumulated 1073 fields. Seeded to match the 1073 SEED the server
            // sends (relativeTo = InvalidEntityId, relativeBias = 0): a freshly
            // seeded player is not aboard until a delta says otherwise.
            public bool RelativeToKnown;
            public long RelativeTo;
            public float RelativeBias;

            // The last verdict emitted for this player, so transitions are edges.
            public bool IsAboard;
            public long ShipRootEntityId;
            public TimeSpan? PendingDisembarkAt;
        }

        // Live 2026-08-14 traces measured moving-hull collider gaps of 0.09-0.79 s
        // which always returned to the same hull. One second bridges those seams
        // without indefinitely claiming a player who genuinely jumped overboard.
        public static readonly TimeSpan ContactGapGrace = TimeSpan.FromSeconds(1);
        private readonly ShipMembership _membership;
        private readonly IClock _clock;
        private readonly Dictionary<ulong, PlayerRelativeState> _players = new Dictionary<ulong, PlayerRelativeState>();

        public AboardTracker(ShipMembership membership)
            : this(membership, new MonotonicClock())
        {
        }

        public AboardTracker(ShipMembership membership, IClock clock)
        {
            _membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Folds one 1073 delta into <paramref name="playerId"/>'s accumulated
        /// state and returns what, if anything, changed about their ship.
        ///
        /// A delta that carries no relativeTo and no relativeBias cannot change the
        /// aboard verdict, so it short-circuits to <see cref="AboardTransition.NoChange"/>
        /// without re-evaluating - that is the standing-still-on-a-deck case, which
        /// must not be read as leaving.
        /// </summary>
        public AboardTransition Observe(ulong playerId, in AboardSample sample)
        {
            if (!_players.TryGetValue(playerId, out PlayerRelativeState? state))
            {
                state = new PlayerRelativeState();
                _players[playerId] = state;
            }

            bool aboardBearing = sample.RelativeToChanged || sample.RelativeBiasChanged;
            if (!aboardBearing && !state.PendingDisembarkAt.HasValue)
            {
                return AboardTransition.NoChange;
            }

            if (sample.RelativeToChanged)
            {
                state.RelativeToKnown = true;
                state.RelativeTo = sample.RelativeTo;
            }
            if (sample.RelativeBiasChanged)
            {
                state.RelativeBias = sample.RelativeBias;
            }

            AboardVerdict verdict = AboardPolicy.Evaluate(
                state.RelativeToKnown, state.RelativeTo, state.RelativeBias, _membership);

            // Physics contact can flicker to Invalid/bias=0 for one or two frames
            // while walking across hull/deck/part collider seams. Keep the semantic
            // ship root during that short gap. A positive non-ship surface (island)
            // is an unambiguous real leave and is applied immediately.
            long? contactShipRoot = state.RelativeToKnown
                ? _membership.RootOf(state.RelativeTo)
                : null;
            bool transientContactGap = !verdict.IsAboard
                && (!state.RelativeToKnown
                    || state.RelativeTo <= 0
                    || (contactShipRoot.HasValue
                        && state.RelativeBias <= AboardPolicy.AttachedBiasThreshold));
            if (state.IsAboard && transientContactGap)
            {
                if (!state.PendingDisembarkAt.HasValue)
                {
                    state.PendingDisembarkAt = _clock.Elapsed + ContactGapGrace;
                    return AboardTransition.NoChange;
                }
                if (_clock.Elapsed < state.PendingDisembarkAt.Value)
                {
                    return AboardTransition.NoChange;
                }
            }
            else
            {
                state.PendingDisembarkAt = null;
            }

            bool wasAboard = state.IsAboard;
            long wasShip = state.ShipRootEntityId;

            state.IsAboard = verdict.IsAboard;
            state.ShipRootEntityId = verdict.IsAboard ? verdict.ShipRootEntityId : 0;
            state.PendingDisembarkAt = null;

            if (!wasAboard && verdict.IsAboard)
            {
                return new AboardTransition(AboardChange.Boarded, verdict.ShipRootEntityId, 0);
            }
            if (wasAboard && !verdict.IsAboard)
            {
                return new AboardTransition(AboardChange.Disembarked, 0, wasShip);
            }
            if (wasAboard && verdict.IsAboard && wasShip != verdict.ShipRootEntityId)
            {
                return new AboardTransition(AboardChange.ChangedShip, verdict.ShipRootEntityId, wasShip);
            }
            return AboardTransition.NoChange;
        }

        /// <summary>The ship a player is currently aboard, or null.</summary>
        public long? ShipOf(ulong playerId)
        {
            return _players.TryGetValue(playerId, out PlayerRelativeState? state) && state.IsAboard
                ? state.ShipRootEntityId
                : (long?)null;
        }

        /// <summary>Whether the player is aboard any ship.</summary>
        public bool IsAboardAnything(ulong playerId) => ShipOf(playerId).HasValue;

        /// <summary>
        /// The peers currently aboard a given ship root. THE query the abandonment
        /// timer needs ("is anyone on ship X"), and the ferry needs to know whom to
        /// carry. Recomputed rather than indexed because the population is tiny and
        /// an index is a second thing to keep consistent.
        /// </summary>
        public IReadOnlyList<ulong> AboardShip(long shipRootEntityId)
        {
            List<ulong> result = new List<ulong>();
            foreach (KeyValuePair<ulong, PlayerRelativeState> pair in _players)
            {
                if (pair.Value.IsAboard && pair.Value.ShipRootEntityId == shipRootEntityId)
                {
                    result.Add(pair.Key);
                }
            }
            return result;
        }

        /// <summary>Whether anyone at all is aboard the given ship. Fast-out for the abandonment timer.</summary>
        public bool AnyoneAboard(long shipRootEntityId)
        {
            foreach (PlayerRelativeState state in _players.Values)
            {
                if (state.IsAboard && state.ShipRootEntityId == shipRootEntityId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Drops a peer's state, and reports whether they were aboard a ship when
        /// they left - a disconnect is a disembark the 1073 stream will never send,
        /// and the abandonment timer must not wait forever for a leave that cannot
        /// arrive. Part of a ForgetPeer everything-contract.
        /// </summary>
        public AboardTransition Forget(ulong playerId)
        {
            if (_players.TryGetValue(playerId, out PlayerRelativeState? state) && state.IsAboard)
            {
                long ship = state.ShipRootEntityId;
                _players.Remove(playerId);
                return new AboardTransition(AboardChange.Disembarked, 0, ship);
            }
            _players.Remove(playerId);
            return AboardTransition.NoChange;
        }
    }
}
