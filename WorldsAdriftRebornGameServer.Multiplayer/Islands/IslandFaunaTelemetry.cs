namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One island's LIVE fauna, as the game server actually seeded it.
    ///
    /// Counts, not positions. Positions are a closed form of the clock
    /// (<see cref="IslandFaunaMovement"/>), so a reader that has the clock and the
    /// island's geometry can place every creature exactly - and a three-second
    /// snapshot could not carry a moving position honestly anyway.
    /// </summary>
    public readonly struct FaunaIslandStat
    {
        public FaunaIslandStat(string islandId, int mantaRays, int jellyFish)
        {
            IslandId = islandId ?? string.Empty;
            MantaRays = mantaRays < 0 ? 0 : mantaRays;
            JellyFish = jellyFish < 0 ? 0 : jellyFish;
        }

        /// <summary>The island that owns them.</summary>
        public string IslandId { get; }

        /// <summary>How many manta rays are live on it.</summary>
        public int MantaRays { get; }

        /// <summary>How many jellyfish are live on it.</summary>
        public int JellyFish { get; }

        /// <summary>Every live creature on the island.</summary>
        public int Total => MantaRays + JellyFish;
    }

    /// <summary>
    /// The whole world's live fauna, and THE CLOCK THAT PLACES IT.
    ///
    /// <see cref="ClockSeconds"/> is the load-bearing field and the reason this
    /// section exists at all. Every fauna pose on this server is
    /// <c>f(creature, envelope, elapsedSeconds)</c> where <c>elapsedSeconds</c> is
    /// the process clock's absolute elapsed time - so a second evaluator that knows
    /// the same elapsed time computes the same metres, and one that guesses at it
    /// draws creatures that are somewhere else. It is reported as of the snapshot's
    /// own <c>generatedAtUnixMs</c>, so a reader can carry it forward with its own
    /// monotonic clock instead of trusting two machines' wall clocks to agree.
    ///
    /// It is NOT derivable from <c>uptimeSeconds</c>. Uptime is measured from a
    /// captured <c>DateTimeOffset</c> and the fauna clock is a <c>Stopwatch</c>
    /// started at type initialisation; they are close but they are two different
    /// clocks, and "close" is the kind of assumption that produces a map which is
    /// subtly and permanently wrong.
    ///
    /// <see cref="Off"/> is what a server with no fauna reports - written
    /// unconditionally, so a reader can tell "off" from "this server predates fauna
    /// telemetry" rather than inferring anything from absence.
    /// </summary>
    public readonly struct FaunaRuntimeStat
    {
        public FaunaRuntimeStat(bool enabled, double clockSeconds, int liveCount,
            int budget, int demand, int perPeerBudget, int poseIntervalMs,
            IReadOnlyList<FaunaIslandStat>? islands)
        {
            Enabled = enabled;
            ClockSeconds = clockSeconds;
            LiveCount = liveCount < 0 ? 0 : liveCount;
            Budget = budget < 0 ? 0 : budget;
            Demand = demand < 0 ? 0 : demand;
            PerPeerBudget = perPeerBudget < 0 ? 0 : perPeerBudget;
            PoseIntervalMs = poseIntervalMs < 0 ? 0 : poseIntervalMs;
            Islands = islands ?? Array.Empty<FaunaIslandStat>();
        }

        /// <summary>Whether the operator switched island fauna on.</summary>
        public bool Enabled { get; }

        /// <summary>
        /// The movement clock's elapsed seconds at the instant the snapshot was
        /// generated. See the type remarks: this is what lets somebody else place
        /// the same creature in the same place.
        /// </summary>
        public double ClockSeconds { get; }

        /// <summary>How many creatures are live world-wide.</summary>
        public int LiveCount { get; }

        /// <summary>The world-wide budget the operator set.</summary>
        public int Budget { get; }

        /// <summary>How many the world WANTED, before the budget was applied.</summary>
        public int Demand { get; }

        /// <summary>The per-peer creature ceiling - the number that governs the wire.</summary>
        public int PerPeerBudget { get; }

        /// <summary>How often one live creature's transform is pushed.</summary>
        public int PoseIntervalMs { get; }

        /// <summary>Every populated island, in seeding order.</summary>
        public IReadOnlyList<FaunaIslandStat> Islands { get; }

        /// <summary>What a server with fauna switched off reports.</summary>
        public static FaunaRuntimeStat Off => new FaunaRuntimeStat(
            enabled: false, clockSeconds: 0, liveCount: 0, budget: 0, demand: 0,
            perPeerBudget: 0, poseIntervalMs: 0, islands: null);
    }
}
