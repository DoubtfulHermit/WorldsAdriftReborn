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
    }
}
