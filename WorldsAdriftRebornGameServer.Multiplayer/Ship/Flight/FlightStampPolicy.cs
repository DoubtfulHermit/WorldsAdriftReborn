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
        /// <param name="lostSimulationMs">
        /// Simulated time the fixed clock DROPPED before this point, milliseconds.
        ///
        /// WHY A PHASE-LOCKED STAMP NEEDS THIS (docs/research/findings-turn-vibration.md,
        /// production section). <see cref="FixedFlightClock.Advance"/> caps a backlog at
        /// <see cref="FixedFlightClock.DefaultMaxCatchUpSteps"/> steps and then consumes
        /// the WHOLE accumulator anyway, including the part it refused to simulate. The
        /// publication schedule still encloses exactly twelve EXECUTED steps per point,
        /// so the point is honest about the simulation it contains - but the wall clock
        /// moved further than the wire clock did, and <see cref="FlightStampMode.PhaseLocked"/>
        /// advances the stamp by exactly one step regardless. Every dropped step is
        /// therefore 20 ms of PERMANENT, never-recovered lag of the wire clock behind
        /// wall clock.
        ///
        /// The stock client turns that lag into the reported re-snap.
        /// <c>PathFollower.AddControlPoint</c> derives
        /// <c>_serverLatency = SynchronisedTime.UpdateNow - (stamp - ExtrapolationTime)</c>
        /// at arrival (decompile PathFollower.cs:146-147) and clamps it to
        /// <c>MaximumServerLatency = 5 s</c> (ShipConfiguration.cs). Once accumulated lag
        /// passes that clamp the playback time outruns the newest buffered point,
        /// <c>SplineInterpolator.Interpolate</c> returns false (SplineInterpolator.cs:23-26),
        /// and the follower enters its halt/extrapolate branch (PathFollower.cs:280-305).
        /// <c>ControlPoint.ExtrapolateWithConstantVelocity</c> copies the previous
        /// ROTATION unchanged (ControlPoint.cs:71-76) because there is no angular velocity
        /// on the wire - so the hull's yaw FREEZES - and the next real point then triggers
        /// <c>StartSplineCorrection</c>, blended over <c>SlowSplineCorrectionTime = 5 s</c>
        /// and applied multiplicatively to rotation (PathFollower.cs:209).
        ///
        /// Adding the lost time here keeps the wire clock locked to wall clock with zero
        /// permanent drift. It confines the rate error to the one segment that genuinely
        /// lost simulation - which is the honest place for it - instead of banking it
        /// forever. Zero (the default) reproduces the historic behaviour exactly.
        /// </param>
        public static long NextStamp(
            FlightStampMode mode, bool everEmitted, long lastStampMs, long nowMs, long stepMs,
            long lostSimulationMs = 0)
        {
            if (!everEmitted)
            {
                return nowMs;
            }

            long phaseLocked = lastStampMs + stepMs + NonNegative(lostSimulationMs);
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
        /// The simulated milliseconds a dropped-step batch threw away, for
        /// <paramref name="droppedSteps"/> steps of <paramref name="stepSeconds"/>.
        /// Rounded to whole milliseconds because the wire stamp is integral; a
        /// negative or non-finite input contributes nothing rather than rewinding
        /// the timeline.
        /// </summary>
        public static long LostSimulationMilliseconds(long droppedSteps, double stepSeconds)
        {
            if (droppedSteps <= 0 || !double.IsFinite(stepSeconds) || stepSeconds <= 0.0)
            {
                return 0;
            }
            return (long)System.Math.Round(droppedSteps * stepSeconds * 1000.0);
        }

        private static long NonNegative(long value) => value > 0 ? value : 0;

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
