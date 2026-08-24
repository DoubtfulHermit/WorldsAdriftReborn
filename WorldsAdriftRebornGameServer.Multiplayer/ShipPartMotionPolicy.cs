namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHEN and WITH WHAT TIMESTAMP the server must re-publish a bolted ship
    /// part's 190602 TransformState so the part keeps FOLLOWING the moving hull.
    ///
    /// THE PROBLEM THIS SOLVES. Seeding a part hull-relative (parent = Parent(hullId,
    /// "~"), see <see cref="BoltedPartTransform"/>) is necessary but NOT sufficient.
    /// The client's <c>FixedUpdateLerpLocalTransformBehaviour</c> - the visualizer that
    /// composes the hull's live position with the part's local offset - goes to SLEEP
    /// one second after its last transform change (<c>_timeUntilSleep</c> ->
    /// <c>UpdatesEnabled = false</c>, FixedUpdateLerpLocalTransformBehaviour.cs:167)
    /// and only WAKES on the part's OWN <c>TransformState.PropertyUpdated</c>
    /// (:376 -> <c>WakeUp</c>). We move the HULL via a 1130 control point but never
    /// touch the part's 190602, so the part's follow-visualizer sleeps and the part
    /// parks in place while the hull flies off ("beams flew up, floor stayed").
    ///
    /// THE FIX. Re-send the part's 190602 as a value UPDATE (not a re-seed) carrying
    /// the SAME hull-relative transform on a cadence BELOW the one-second sleep, which
    /// fires <c>PropertyUpdated</c> -> <c>OnTransformChanged</c> -> <c>WakeUp</c> and
    /// keeps the visualizer recomposing against the hull. This is exactly what the
    /// shipped worker's <c>RelativeParentTransformUpdater</c> does continuously.
    ///
    /// This module is the PURE half - the cadence and the monotonic timestamp - so
    /// they are asserted in unit tests rather than by watching a client. The ENet
    /// send and the game-typed <c>TransformState.Update</c> live in the GameServer
    /// assembly (Game.ShipPartMotionService / Game.ShipPartTransform).
    /// </summary>
    public static class ShipPartMotionPolicy
    {
        /// <summary>
        /// 190602 TransformState - the component the wake update carries. The same id
        /// the seed uses; a value update on it is what fires PropertyUpdated.
        /// </summary>
        public const uint TransformStateComponentId = 190602;

        /// <summary>
        /// The wake cadence, seconds. Strictly BELOW the client's 1 s sleep timeout,
        /// and with margin (0.5 s = two wakes per sleep window) so a single manual
        /// nudge and any idle stretch still keep the parts awake: one wake buys ~1 s
        /// of following, and the next lands ~0.5 s later. Faster would only add
        /// reliable packets for no benefit; 1 s or slower would let the parts nod off
        /// between wakes and stutter.
        /// </summary>
        public const double HeartbeatIntervalSeconds = 0.5;

        /// <summary>
        /// A bounded MEMBER-ONLY drain after the final hull control point. The
        /// decompiled client defaults allow its root PathFollower to continue
        /// extrapolating for 5 s and then halt for 1 s, while a mounted
        /// FixedUpdateLerpLocalTransformBehaviour sleeps 1 s after its own last
        /// 190602 update. Keeping only mounted followers awake across that 7 s
        /// window lets them finish against the root's final rendered pose.
        ///
        /// This is a WAReborn guard derived from the decompiled defaults, not a
        /// claim that the lost serialized ShipConfig used those exact values.
        /// It must never be used to publish another hull 1130 point: a late root
        /// heartbeat revives stale PathFollower velocity and caused the measured
        /// multi-metre drift/snap regression.
        /// </summary>
        public const double RestFollowerDrainSeconds = 7.0;

        public static bool ShouldDrainRestingFollowers(
            bool hullAtRest, bool isManned, double remainingSeconds)
        {
            return hullAtRest
                && !isManned
                && double.IsFinite(remainingSeconds)
                && remainingSeconds >= 0.0;
        }

        /// <summary>
        /// The timeline origin, i.e. the stamp the first wake carries. Mirrors
        /// <see cref="RelayTimestampPolicy.SeedTimestampSeconds"/>: a small positive
        /// epoch (2x the client's 0.1 s interpolation delay) rather than 0, so the
        /// child's synthetic timeline sits just ahead of the receiver's playback clock.
        /// </summary>
        public const float SeedStampSeconds = 0.2f;

        /// <summary>
        /// A STRICTLY INCREASING synthetic stamp for wake number <paramref name="sampleIndex"/>,
        /// exactly the shape <see cref="RelayTimestampPolicy.StampFor"/> uses: origin
        /// plus one step per emitted wake. Monotonicity is the only property the client
        /// needs of it - the interpolator discards a stamp that does not advance, which
        /// would silently stop waking the part - and because a bolted part's local
        /// offset is CONSTANT, the absolute scale is irrelevant (interpolating between
        /// equal values yields that value at any time), so a synthetic per-emit counter
        /// is both sufficient and provably monotonic. Computed in double, narrowed once,
        /// so a test can assert it strictly increases for every index a session reaches.
        /// </summary>
        public static float StampFor(long sampleIndex, double stepSeconds)
        {
            return (float)(SeedStampSeconds + sampleIndex * stepSeconds);
        }

        /// <summary>
        /// The PARENT hull's 190602 timestamp for the same mount its <c>Parent(hull,"~")</c>
        /// CHILD is stamped at - identical to <see cref="StampFor"/>, and that identity is
        /// the whole fix.
        ///
        /// THE BUG THIS CLOSES (findings-mount-placement.md section 2). The client samples a
        /// <c>"~"</c> child's local-transform interpolator at the PARENT hull's 190602
        /// timestamp, not at wall-clock. Mounted-part 190602 updates advance
        /// (<see cref="StampFor"/> climbs from <see cref="SeedStampSeconds"/>), but the built
        /// hull's own 190602 is a SEED frozen at timestamp 0 and its 1130 motion never touches
        /// that stamp - so the parent keeps asking for time 0, the child interpolator returns
        /// its FIRST sample, and every later mount (a re-position, a rotation change) sits
        /// enqueued behind it and is never selected. Re-positioning is a visible no-op.
        ///
        /// THE FIX. Put the hull and its <c>"~"</c> children on ONE timeline: advance the
        /// hull's 190602 on the SAME clock as the child update, and stamp the child at the
        /// current parent time. With <c>parent stamp == child stamp</c> the parent's own
        /// interpolation ramps up to and clamps at the latest sample, so the parent-sampling
        /// time REACHES the child's newest sample and it is selected. This is a per-mount
        /// value-UPDATE (event-driven, one extra 190602 on the hull per accepted place), NOT
        /// a re-seed and NOT a per-frame stream, so it never re-fires the client's
        /// OnDisable-&gt;Clear. Preferred over an interpolator-reset hack because it matches
        /// the client's relative-interpolation design and scales to a MOVING hull, where the
        /// hull's motion clock owns this stamp instead of the mount counter.
        /// </summary>
        public static float ParentStampFor(long sampleIndex, double stepSeconds)
        {
            return StampFor(sampleIndex, stepSeconds);
        }

        /// <summary>
        /// Whether the client's parent-sampling time can REACH a child sample stamped at the
        /// same mount index - i.e. the hull's 190602 timeline has advanced far enough that the
        /// <c>"~"</c> child's newest local transform is selectable rather than stuck behind the
        /// first. True under the shared-timeline fix (<see cref="ParentStampFor"/> ==
        /// <see cref="StampFor"/>); the pure regression test contrasts it with the old frozen
        /// hull seed at timestamp 0, which never reaches a positive child stamp.
        /// </summary>
        public static bool ParentSamplingReaches(long sampleIndex, double stepSeconds)
        {
            return ParentStampFor(sampleIndex, stepSeconds) >= StampFor(sampleIndex, stepSeconds);
        }
    }
}
