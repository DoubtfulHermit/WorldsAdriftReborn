namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// How an emitted 1130 control point's timestamp is chosen. One enum rather
    /// than a bool because there are now three genuinely different answers and a
    /// second bool beside <c>phaseLockedEmit</c> would let two of them be
    /// requested at once.
    /// </summary>
    public enum FlightStampMode
    {
        /// <summary>
        /// The historic legacy behaviour: pin the stamp to WALL CLOCK whenever the
        /// poll loop has already passed <c>last + step</c>. Kept as the default so
        /// nothing changes without an explicit opt-in, and kept nameable so the
        /// regression test that pins the defect it causes cannot be deleted by
        /// accident.
        /// </summary>
        WallClock = 0,

        /// <summary>
        /// The fixed-step publisher's stamp: always exactly <c>last + step</c>,
        /// because a phase-locked point represents an exact, known amount of
        /// simulation time and a late poll must not stretch it.
        /// </summary>
        PhaseLocked = 1,

        /// <summary>
        /// <c>WAREBORN_FLIGHT_STAMP_CONTINUITY=1</c>. Phase-lock the legacy
        /// publisher too, but RESYNC to wall clock once the wire clock has fallen a
        /// whole publication interval behind - which is exactly the signature of
        /// <see cref="CadenceTimer"/> having skipped a stalled interval.
        /// </summary>
        Continuity = 2,
    }

    /// <summary>
    /// WHICH TIMESTAMP an emitted 1130 control point carries - the pure half of
    /// <c>FlightSession.NextStamp</c>, extracted so the invariant that matters can
    /// be asserted in unit tests instead of inferred from a live client.
    ///
    /// THE DEFECT THIS EXISTS TO FIX (docs/research/findings-turn-vibration.md).
    /// The legacy publisher integrates EXACTLY one
    /// <see cref="ShipMotionPolicy.SendIntervalSeconds"/> of simulation per emitted
    /// point (<c>FlightSession.Advance</c> calls <c>AdvanceFixed</c> with
    /// <c>fixedStepCount: 1</c> and <c>fixedStepSeconds: stepSeconds</c>), but then
    /// stamps that point at WALL CLOCK whenever the poll loop was late. The server's
    /// main loop turns once per ENet event with a 50 ms poll timeout, so "late" is
    /// the normal case and the lateness varies point to point. The wire therefore
    /// carries a constant 240 ms of simulated motion under a timestamp delta of
    /// <c>240 + jitter</c> ms.
    ///
    /// WHY THAT IS A TURN-ONLY ARTEFACT. A control point carries position and LINEAR
    /// VELOCITY (<c>FlightIntegrator.ToControlPoint</c>) but NO angular velocity, so
    /// the client hermite-interpolates position with real tangents - which absorbs an
    /// uneven interval into a slightly eased curve - while it can only slerp rotation
    /// piecewise between the two endpoint attitudes. The rendered angular rate is
    /// therefore <c>trueRate * step / stampDelta</c> and wobbles by the full poll
    /// jitter on every single point. In straight flight the attitude delta is zero
    /// and the wobble is unobservable; in a sustained turn it is a ~4 Hz shudder that
    /// grows with the lever arm, which is why the HELM and the mounted parts show it
    /// far more than the hull's own origin does.
    ///
    /// WHY NOT SIMPLY ALWAYS PHASE-LOCK. <c>FlightSession</c>'s own remarks record the
    /// reason the legacy branch pins to wall clock: the stamp is what the client's
    /// smoothed server-latency estimate is built from, and after a real stall the
    /// <see cref="CadenceTimer"/> SKIPS the missed intervals
    /// (<c>RelayCadence.cs</c> - <c>_nextDue = now + _interval</c>), so simulated time
    /// and wall time genuinely diverge and a permanently phase-locked wire clock would
    /// fall further behind for ever. <see cref="FlightStampMode.Continuity"/> keeps the
    /// phase lock for ordinary poll jitter and resyncs exactly when the cadence timer
    /// skipped, which is the only case where the divergence is real rather than
    /// incidental.
    /// </summary>
    public static class FlightStampPolicy
    {
        /// <summary>
        /// How far the phase-locked wire clock may fall behind wall clock before
        /// <see cref="FlightStampMode.Continuity"/> resyncs, expressed in whole
        /// publication intervals.
        ///
        /// ONE interval, and the value is not a taste call. <see cref="CadenceTimer"/>
        /// is drift-free while the loop keeps up - it advances <c>_nextDue</c> by a
        /// fixed interval - so ordinary poll jitter offsets each fire without
        /// accumulating, and the phase-locked wire clock stays within one poll period
        /// (at most 50 ms) of wall clock indefinitely. The ONLY way to fall a whole
        /// 240 ms interval behind is the timer's stall branch, which re-bases
        /// <c>_nextDue</c> to <c>now</c> and drops the missed ticks. So "lag >= one
        /// interval" is precisely "the cadence timer skipped", and resyncing there
        /// bounds the client's latency estimate without ever stretching a point that
        /// represents contiguous simulation.
        /// </summary>
        public const int ContinuityResyncIntervals = 1;

        /// <summary>
        /// The stamp for the next emitted point.
        ///
        /// Every mode is monotonic and never returns anything below
        /// <paramref name="lastStampMs"/> + <paramref name="stepMs"/> once a session
        /// has emitted, so <see cref="ShipMotionPolicy.IsLegalSeparation"/> holds by
        /// construction in all three - the client's 0.228 s reject floor is not at
        /// risk from any of them.
        /// </summary>
        /// <param name="mode">Which of the three answers to use.</param>
        /// <param name="everEmitted">False for a session's very first point, which is always wall clock.</param>
        /// <param name="lastStampMs">The stamp the previous point carried.</param>
        /// <param name="nowMs">Wall clock at the instant this point is being built.</param>
        /// <param name="stepMs">The publication interval in milliseconds (240).</param>
        public static long NextStamp(
            FlightStampMode mode, bool everEmitted, long lastStampMs, long nowMs, long stepMs)
        {
            if (!everEmitted)
            {
                return nowMs;
            }

            long phaseLocked = lastStampMs + stepMs;
            switch (mode)
            {
                case FlightStampMode.PhaseLocked:
                    return phaseLocked;

                case FlightStampMode.Continuity:
                    // Resync only when the wire clock has fallen a whole publication
                    // interval behind - the cadence timer's stall signature. Anything
                    // smaller is poll jitter and must NOT stretch the point, because
                    // the point represents exactly one step of simulation.
                    return ShouldResyncToWallClock(phaseLocked, nowMs, stepMs)
                        ? nowMs
                        : phaseLocked;

                default:
                    return nowMs < phaseLocked ? phaseLocked : nowMs;
            }
        }

        /// <summary>
        /// Whether <see cref="FlightStampMode.Continuity"/> abandons the phase lock
        /// for this point. Exposed so the boundary is asserted directly rather than
        /// only through <see cref="NextStamp"/>.
        /// </summary>
        public static bool ShouldResyncToWallClock(long phaseLockedStampMs, long nowMs, long stepMs)
        {
            return nowMs - phaseLockedStampMs >= ContinuityResyncIntervals * stepMs;
        }

        /// <summary>
        /// The angular rate a client renders for a point that represents
        /// <paramref name="simulatedStepMs"/> of simulation but is played back over
        /// <paramref name="stampDeltaMs"/>, as a fraction of the authoritative rate.
        ///
        /// This is the whole defect in one line, and it is here rather than in a test
        /// so the arithmetic is stated once: a control point carries no angular
        /// velocity, so the client's only choice is to slerp the attitude delta across
        /// the timestamp gap. 1.0 means the rendered turn rate equals the commanded
        /// one; anything else is the visible wobble.
        /// </summary>
        public static double RenderedAngularRateFraction(long simulatedStepMs, long stampDeltaMs)
        {
            if (stampDeltaMs <= 0)
            {
                return 0.0;
            }
            return (double)simulatedStepMs / stampDeltaMs;
        }
    }
}
