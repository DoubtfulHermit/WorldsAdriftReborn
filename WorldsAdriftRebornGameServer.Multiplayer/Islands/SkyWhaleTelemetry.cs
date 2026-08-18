namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// The world's LIVE whale: which entity it is, where its most recent call came
    /// from, and - the fields the migration added - WHICH ZONE IT IS IN AND WHERE
    /// IT IS GOING NEXT.
    ///
    /// NO POSITION FOR THE ANIMAL, deliberately, and for the reason
    /// <see cref="FaunaIslandStat"/> gives about creatures: the whale's pose is a
    /// closed form of <c>clockSeconds</c> (<see cref="SkyWhaleMotion"/>), so a
    /// reader that has the clock and the route places it exactly, and a
    /// three-second snapshot could not carry a moving position honestly anyway.
    ///
    /// THE CALL IS DIFFERENT, and that is why it IS carried. A call is not motion;
    /// it is a discrete event pinned to one place for its whole window, and the
    /// window is minutes long. A reader cannot derive it without knowing the call
    /// interval AND the route AND agreeing about the epoch, so publishing the
    /// station makes the map's route exactly the place the sound is coming from
    /// rather than a second derivation that could disagree.
    ///
    /// THE WHEREABOUTS ARE CARRIED FOR THE SAME REASON, and the reason is worth
    /// stating because a reader COULD in principle derive them: the published route
    /// carries a zone name on every waypoint, so a browser could classify the
    /// current segment itself. It is not asked to. "Which zone is the whale in" is
    /// the one question this feature exists to answer, the classification has edge
    /// cases (a crossing is any segment that is not between two island waypoints of
    /// the same zone), and a second implementation of it is a second thing that can
    /// disagree with the server - about the headline fact. The server answers it
    /// once, with <see cref="SkyWhaleCircuit.WhereAt"/>, and everything else quotes
    /// that.
    /// </summary>
    /// <param name="RouteId">The route it flies - the join key to the map's
    /// published geometry.</param>
    /// <param name="EntityId">The animal's wire identity.</param>
    /// <param name="CallEntityId">Its invisible caller's wire identity.</param>
    /// <param name="CallIndex">Which call is current, counting from the world's epoch.</param>
    /// <param name="CallX">The call station, world metres.</param>
    /// <param name="CallY">The call station, world metres.</param>
    /// <param name="CallZ">The call station, world metres.</param>
    /// <param name="RegionId">The zone it is over right now, or EMPTY while it is
    /// crossing open sky between two zones. Empty is a real answer, not missing
    /// data.</param>
    /// <param name="NextRegionId">The zone it enters next - while in transit, the
    /// one it is crossing towards.</param>
    /// <param name="NextRegionIslandId">The island it will be over when it gets
    /// there. Where a player of that zone should stand.</param>
    /// <param name="NextRegionSeconds">How long until it does.</param>
    /// <param name="NextIslandId">The next island of any zone it passes over.</param>
    /// <param name="NextIslandSeconds">How long until it does.</param>
    public readonly record struct SkyWhaleStat(
        string RouteId,
        long EntityId,
        long CallEntityId,
        long CallIndex,
        double CallX,
        double CallY,
        double CallZ,
        string RegionId,
        string NextRegionId,
        string NextRegionIslandId,
        double NextRegionSeconds,
        string NextIslandId,
        double NextIslandSeconds);

    /// <summary>
    /// The sky whale section of the stats snapshot.
    ///
    /// ALWAYS WRITTEN, with an explicit <see cref="Enabled"/>, for exactly the
    /// reason <see cref="FaunaRuntimeStat"/> is: a reader must be able to tell a
    /// server that has the feature switched OFF from a server that PREDATES it.
    /// Those are different facts and a map that draws neither must still be able to
    /// say which one it is looking at.
    ///
    /// <see cref="ClockSeconds"/> is the load-bearing field. It is the same
    /// <c>ServerClock.Elapsed</c> the pose function is evaluated against, so a
    /// console holding this number and the route can place the animal exactly where
    /// this server has it - which is why nothing here is a position.
    ///
    /// STILL A LIST, holding at most one. The world carries one whale and
    /// <see cref="SkyWhalePlan.Build"/> returns one placement; the list survives
    /// because "enabled but nothing seeded" and "enabled with a whale" are different
    /// states a reader has to tell apart, and an empty array says the first without
    /// a second flag. It is not an invitation to seed a second animal.
    /// </summary>
    public readonly struct SkyWhaleRuntimeStat
    {
        /// <summary>The section a server with the whale switched off writes.</summary>
        public static readonly SkyWhaleRuntimeStat Off = new SkyWhaleRuntimeStat(
            enabled: false, clockSeconds: 0.0, loadRadiusMetres: 0.0,
            callRadiusMetres: 0.0, poseIntervalMs: 0, callIntervalSeconds: 0.0,
            whales: Array.Empty<SkyWhaleStat>());

        public SkyWhaleRuntimeStat(bool enabled, double clockSeconds,
            double loadRadiusMetres, double callRadiusMetres, int poseIntervalMs,
            double callIntervalSeconds, IReadOnlyList<SkyWhaleStat> whales)
        {
            Enabled = enabled;
            ClockSeconds = clockSeconds;
            LoadRadiusMetres = loadRadiusMetres;
            CallRadiusMetres = callRadiusMetres;
            PoseIntervalMs = poseIntervalMs;
            CallIntervalSeconds = callIntervalSeconds;
            Whales = whales ?? Array.Empty<SkyWhaleStat>();
        }

        /// <summary>Whether the feature is switched on at all.</summary>
        public bool Enabled { get; }

        /// <summary>
        /// The server clock the pose is a function of. See the type remarks - this
        /// is what lets a reader draw the animal moving without being sent it.
        /// </summary>
        public double ClockSeconds { get; }

        /// <summary>How near the animal a peer must be to be shown it, in metres.</summary>
        public double LoadRadiusMetres { get; }

        /// <summary>How near a call a peer must be to hear it, in metres.</summary>
        public double CallRadiusMetres { get; }

        /// <summary>How often a held whale's transform is pushed.</summary>
        public int PoseIntervalMs { get; }

        /// <summary>How often it calls, in seconds.</summary>
        public double CallIntervalSeconds { get; }

        /// <summary>The world's whale, or nothing. See the type remarks.</summary>
        public IReadOnlyList<SkyWhaleStat> Whales { get; }

        /// <summary>How many whales are live. One, or none.</summary>
        public int WhaleCount => Whales.Count;
    }
}
