namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One call: which call it is, and where the sound comes from.
    ///
    /// <see cref="Index"/> is the whole schedule. It is
    /// <c>floor(elapsed / CallIntervalSeconds)</c> and nothing else, so two peers,
    /// two processes and a restarted server all agree about which call is current
    /// without anybody storing anything. The service compares the index a peer was
    /// last shown against the index that is current; a difference is a new call.
    /// </summary>
    /// <param name="Index">Which call this is, counting from the world's epoch.</param>
    /// <param name="Position">Where the sound is emitted from - the whale's own
    /// position at the instant the call began.</param>
    public readonly record struct SkyWhaleCall(long Index, FixedPointPosition Position);

    /// <summary>
    /// WHERE THE WHALE IS AND WHICH WAY IT FACES, and WHERE ITS LAST CALL CAME
    /// FROM - both as closed forms of the clock.
    ///
    /// This file is to <see cref="SkyWhaleCircuit"/> what
    /// <see cref="IslandFaunaMovement"/> is to a manta's orbit: the circuit knows
    /// the SHAPE, this knows what a whale does with it. It is pure, total,
    /// allocation-free and stateless, and every promise
    /// <see cref="IslandFaunaMovement"/> makes about that holds here for the same
    /// reasons - a restarted server replays the identical path, an unwatched whale
    /// is exactly where it would have been, and two peers watching the same animal
    /// are told the same position because the position is DERIVED rather than
    /// sampled.
    ///
    /// EVERYTHING HERE IS WAREBORN TUNING. Retail's sky whale had no movement
    /// controller at all - see <see cref="SkyWhalePolicy"/> for what was recovered
    /// and what was not.
    /// </summary>
    public static class SkyWhaleMotion
    {
        /// <summary>
        /// Below this squared tangent length the spline's derivative is treated as
        /// absent. It can only happen on a degenerate ring, which
        /// <see cref="SkyWhaleCircuit.Build"/> already refuses to construct; the
        /// guard exists so a caller passing a hand-built ring gets the identity
        /// rotation rather than a NaN quaternion.
        /// </summary>
        private const double MinimumTangentSquared = 1e-12;

        /// <summary>
        /// The whale's complete pose at one instant, from ONE evaluation of the
        /// circuit's position and tangent.
        ///
        /// A single call rather than two, for the reason
        /// <see cref="IslandFaunaMovement.WorldTransformAt"/> gives: two separate
        /// calls would be correct today and would rot the moment anything cached,
        /// batched or rescheduled one of them - which is exactly how the mantas
        /// ended up flying sideways.
        ///
        /// THE HEADING IS THE TANGENT, with world up. Not a banked or pitched
        /// variant: the animal carries one clip, <c>Whale_Swim</c>, with no turn
        /// state (RECOVERED), so the only honest thing to show is a creature
        /// pointing where it is going. The tangent of a C1 spline turns
        /// continuously, so this never snaps.
        /// </summary>
        public static FaunaTransform WorldTransformAt(
            SkyWhaleCircuit circuit, double elapsedSeconds)
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));

            double lap = circuit.LapAt(elapsedSeconds);
            (double x, double y, double z) = circuit.PositionAt(lap);
            (double tx, double ty, double tz) = circuit.TangentAt(lap);

            FaunaRotation rotation =
                (tx * tx) + (ty * ty) + (tz * tz) < MinimumTangentSquared
                    ? FaunaRotation.Identity
                    : IslandFaunaOrientation.LookRotation((tx, ty, tz), (0.0, 1.0, 0.0));

            return new FaunaTransform(FixedPointPosition.FromMetres(x, y, z), rotation);
        }

        /// <summary>Where the whale is at one instant, without the heading.</summary>
        public static FixedPointPosition WorldPositionAt(
            SkyWhaleCircuit circuit, double elapsedSeconds)
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            (double x, double y, double z) = circuit.PositionAtTime(elapsedSeconds);
            return FixedPointPosition.FromMetres(x, y, z);
        }

        /// <summary>
        /// Which call is current, and where it came from.
        ///
        /// THE CALL IS A STEP FUNCTION OF THE CLOCK, and it has to be, because of a
        /// RECOVERED client rule that shapes this whole mechanism:
        /// <c>BigCallVisualiser.OnCoordsUpdated</c> moves its transform ONLY if the
        /// new coordinates are within ONE METRE of where it already is. The caller
        /// cannot be slid along behind the whale. So a call is not a position that
        /// changes; it is an EVENT with a fixed location, and the location is
        /// wherever the whale was when that call began.
        ///
        /// Which makes the whole schedule this one expression: index
        /// <c>k = floor(t / interval)</c>, station = the circuit evaluated at
        /// <c>k x interval</c>. No state, no drift, and a peer that reconnects
        /// during call 4,912 is told about call 4,912 rather than about a fresh one.
        /// </summary>
        public static SkyWhaleCall CallAt(SkyWhaleCircuit circuit, double elapsedSeconds,
            double callIntervalSeconds = SkyWhalePolicy.CallIntervalSeconds)
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            if (callIntervalSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(callIntervalSeconds),
                    "a non-positive call interval would call once per main-loop turn");
            }

            long index = (long)Math.Floor(elapsedSeconds / callIntervalSeconds);
            return new SkyWhaleCall(index,
                WorldPositionAt(circuit, index * callIntervalSeconds));
        }

        /// <summary>
        /// How far the whale is from a peer, in SQUARED metres. The squared form is
        /// what the interest policy compares, so the square root is never taken on
        /// the hot path.
        /// </summary>
        public static double DistanceSquared(FixedPointPosition from, FixedPointPosition to)
        {
            double dx = to.MetresX - from.MetresX;
            double dy = to.MetresY - from.MetresY;
            double dz = to.MetresZ - from.MetresZ;
            return (dx * dx) + (dy * dy) + (dz * dz);
        }
    }
}
