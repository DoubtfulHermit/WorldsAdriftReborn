namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// HOW FAST the AfterPlayer world entities are walked into a joining client,
    /// and nothing else.
    ///
    /// WHY THIS EXISTS. The spawn handshake is already ack-gated per step - the
    /// server sends one op, the client acks it, the server sends the next (see
    /// <see cref="SpawnPlan"/> and the SyncStep loop). But on a LAN the round trip
    /// is a couple of milliseconds, so ~44 world entities (island, ship, ~21
    /// trees, ~21 ore) drain back-to-back the instant the loading screen lifts,
    /// and the client's asset loader is SYNCHRONOUS and unbudgeted: every
    /// RequestAsset blocks its frame while a prefab bundle loads. Forty-odd
    /// bundle loads with no frame yield between them is the first-load hitch.
    ///
    /// The fix is not interest streaming (that is the documented long-term item);
    /// it is a floor on how OFTEN a new AfterPlayer entity is allowed to appear.
    /// Spacing them by a few tens of milliseconds turns "one long hitch" into "the
    /// world fades in over a second or two", which is what a player reads as
    /// normal. The player's OWN avatar and every BeforePlayer entity (the ground)
    /// are never paced - they gate the loading screen.
    ///
    /// WHICH OP IS PACED: AddEntity, not RequestAsset. An earlier version of this
    /// paragraph said the opposite and was wrong; see
    /// <see cref="PacesInstantiation"/> for the measurement that settled it -
    /// pacing RequestAsset did not throttle anything, because a client with the
    /// bundle already cached acks the load instantly and the AddEntity followed
    /// unpaced.
    ///
    /// The metronome itself is <see cref="CadenceTimer"/>, reused verbatim: "at
    /// most one release per interval, and NO burst catch-up after a stall" is
    /// precisely the behaviour wanted here too - a paused main loop must not dump
    /// the whole backlog of pending entities in one frame, which would rebuild the
    /// hitch. This type is only the env-to-interval policy and the arithmetic that
    /// says how long a given world takes to stream, both asserted natively.
    /// </summary>
    public static class SpawnPacePolicy
    {
        /// <summary>
        /// Default spacing between successive AfterPlayer entity releases, in
        /// milliseconds. At ~43 AfterPlayer entities this streams the whole world
        /// in ~1.7 s (the first is immediate, the rest are 40 ms apart) - long
        /// enough that the client instantiates them across many frames instead of
        /// one, short enough that a player walking off the spawn point does not
        /// outrun the world appearing around them.
        /// </summary>
        public const int DefaultMs = 40;

        /// <summary>
        /// Upper clamp on the spacing. Beyond this a value is almost certainly a
        /// typo, and since the spacing multiplies by the entity count a large one
        /// stalls world streaming for a very long time (1 s x 43 entities = 43 s
        /// of the world trickling in). A perf knob must never do that by accident.
        /// </summary>
        public const int MaxMs = 1000;

        /// <summary>
        /// The pacing interval for a WAREBORN_SPAWN_PACE_MS environment value.
        ///
        /// Rules, and each fails SAFE rather than throwing - a perf knob must
        /// never stop the server booting or breaking spawn:
        /// <list type="bullet">
        /// <item>unset, empty or unparsable =&gt; <see cref="DefaultMs"/>.</item>
        /// <item>exactly 0 =&gt; <see cref="TimeSpan.Zero"/>, which
        ///   <see cref="IsEnabled"/> reads as DISABLED - the old one-burst
        ///   behaviour, kept as a one-line rollback for anyone who needs it.</item>
        /// <item>negative (nonsense) =&gt; <see cref="DefaultMs"/>.</item>
        /// <item>above <see cref="MaxMs"/> =&gt; <see cref="MaxMs"/>.</item>
        /// </list>
        /// </summary>
        public static TimeSpan IntervalFrom(string? env)
        {
            if (!int.TryParse(env, out int ms))
            {
                return TimeSpan.FromMilliseconds(DefaultMs);
            }

            if (ms == 0)
            {
                return TimeSpan.Zero;
            }

            if (ms < 0)
            {
                return TimeSpan.FromMilliseconds(DefaultMs);
            }

            return TimeSpan.FromMilliseconds(ms > MaxMs ? MaxMs : ms);
        }

        /// <summary>
        /// Whether an interval actually paces. <see cref="TimeSpan.Zero"/> means
        /// "do not pace" (release every AfterPlayer entity the moment the client
        /// acks the previous step, as before). Anything positive paces.
        /// </summary>
        public static bool IsEnabled(TimeSpan interval) => interval > TimeSpan.Zero;

        /// <summary>
        /// Whether ONE spawn-plan step should be held back by the pacer.
        ///
        /// The op that matters is <see cref="SpawnOp.AddEntity"/>, not RequestAsset:
        /// AddEntity is what INSTANTIATES the prefab on the client's main thread - the
        /// per-entity frame cost, and the exact op a joiner was measured receiving in a
        /// burst (17 in one second). Pacing RequestAsset instead did not throttle it,
        /// because a client with the bundle already cached acks the asset load
        /// instantly and the AddEntity follows unpaced. Pacing AddEntity directly caps
        /// instantiation at one per interval; the RequestAsset stays unpaced but cannot
        /// run ahead, since the single step pointer is held at the paced AddEntity, so
        /// at most one asset load is ever outstanding.
        ///
        /// The player's own avatar and every BeforePlayer entity (the ground) are never
        /// paced - they gate the loading screen and must go out at once.
        ///
        /// When the loading barrier holds the initial set (<paramref name="barrierHoldsInitialSet"/>),
        /// that set - island, static ship, and nearby built-ship domains -
        /// instantiates while the player is FROZEN behind the loading screen, out of
        /// view, so pacing it would only lengthen the loading screen for no visible
        /// benefit. It streams at full speed and only the DISTANT scenery that appears
        /// in view after release is paced. With no barrier there is no screen, so
        /// everything appears in view and everything AfterPlayer is paced.
        /// </summary>
        public static bool PacesInstantiation(SpawnOp op, SpawnOrder order, bool isInitialSet, bool barrierHoldsInitialSet)
        {
            if (op != SpawnOp.AddEntity)
            {
                return false;
            }

            if (order != SpawnOrder.AfterPlayer)
            {
                return false;
            }

            if (barrierHoldsInitialSet && isInitialSet)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// How long a run of <paramref name="entities"/> AfterPlayer entities takes
        /// to release at a given spacing, assuming the first is immediate and each
        /// subsequent one waits one interval. Purely for the boot log line and the
        /// tests; the running server never needs it. Zero for zero or one entity.
        /// </summary>
        public static TimeSpan StreamDurationFor(int entities, TimeSpan interval)
        {
            if (entities <= 1 || interval <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(interval.Ticks * (entities - 1));
        }
    }
}
