namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One region's LIVE whale: which entity it is, and where its most recent call
    /// came from.
    ///
    /// NO POSITION FOR THE ANIMAL, deliberately, and for the reason
    /// <see cref="FaunaIslandStat"/> gives about creatures: the whale's pose is a
    /// closed form of <c>clockSeconds</c> (<see cref="SkyWhaleMotion"/>), so a
    /// reader that has the clock and the circuit places it exactly, and a
    /// three-second snapshot could not carry a moving position honestly anyway.
    ///
    /// THE CALL IS DIFFERENT, and that is why it IS carried. A call is not motion;
    /// it is a discrete event pinned to one place for its whole window, and the
    /// window is minutes long. A reader cannot derive it without knowing the call
    /// interval AND the circuit AND agreeing about the epoch, so publishing the
    /// station makes the map's ring exactly the place the sound is coming from
    /// rather than a second derivation that could disagree.
    /// </summary>
    /// <param name="RegionId">The region this whale never leaves.</param>
    /// <param name="EntityId">The animal's wire identity.</param>
    /// <param name="CallEntityId">Its invisible caller's wire identity.</param>
    /// <param name="CallIndex">Which call is current, counting from the world's epoch.</param>
    /// <param name="CallX">The call station, world metres.</param>
    /// <param name="CallY">The call station, world metres.</param>
    /// <param name="CallZ">The call station, world metres.</param>
    public readonly record struct SkyWhaleRegionStat(
        string RegionId,
        long EntityId,
        long CallEntityId,
        long CallIndex,
        double CallX,
        double CallY,
        double CallZ);

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
    /// console holding this number and the region's circuit can place the animal
    /// exactly where this server has it - which is why nothing here is a position.
    /// </summary>
    public readonly struct SkyWhaleRuntimeStat
    {
        /// <summary>The section a server with the whale switched off writes.</summary>
        public static readonly SkyWhaleRuntimeStat Off = new SkyWhaleRuntimeStat(
            enabled: false, clockSeconds: 0.0, loadRadiusMetres: 0.0,
            callRadiusMetres: 0.0, poseIntervalMs: 0, callIntervalSeconds: 0.0,
            regions: Array.Empty<SkyWhaleRegionStat>());

        public SkyWhaleRuntimeStat(bool enabled, double clockSeconds,
            double loadRadiusMetres, double callRadiusMetres, int poseIntervalMs,
            double callIntervalSeconds, IReadOnlyList<SkyWhaleRegionStat> regions)
        {
            Enabled = enabled;
            ClockSeconds = clockSeconds;
            LoadRadiusMetres = loadRadiusMetres;
            CallRadiusMetres = callRadiusMetres;
            PoseIntervalMs = poseIntervalMs;
            CallIntervalSeconds = callIntervalSeconds;
            Regions = regions ?? Array.Empty<SkyWhaleRegionStat>();
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

        /// <summary>One row per region that carries a whale.</summary>
        public IReadOnlyList<SkyWhaleRegionStat> Regions { get; }

        /// <summary>How many whales are live.</summary>
        public int WhaleCount => Regions.Count;
    }
}
