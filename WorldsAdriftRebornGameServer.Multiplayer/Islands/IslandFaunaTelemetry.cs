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
    /// One bloom's published parameters - everything a second evaluator needs to
    /// place the moving maximum, nothing else. Field names in the JSON mirror
    /// <see cref="FaunaBloom"/> one to one so nobody has to keep a mapping table.
    /// </summary>
    public readonly record struct FaunaBloomStat(
        string Species,
        int Index,
        double Amplitude,
        double SigmaMetres,
        double AnnulusRadiusMetres,
        double RadialDriftMetres,
        double AngularDriftRadians,
        double OmegaRadial,
        double OmegaAngular,
        double OmegaMigration,
        double PhaseRadial,
        double PhaseAngular,
        double BaseAngleRadians)
    {
        /// <summary>The projection of one computed bloom, labelled for the wire.</summary>
        public static FaunaBloomStat From(FaunaSpecies species, int index, FaunaBloom bloom) =>
            new FaunaBloomStat(
                species == FaunaSpecies.MantaRay ? "manta" : "jelly", index,
                bloom.Amplitude, bloom.SigmaMetres, bloom.AnnulusRadiusMetres,
                bloom.RadialDriftMetres, bloom.AngularDriftRadians,
                bloom.OmegaRadial, bloom.OmegaAngular, bloom.OmegaMigration,
                bloom.PhaseRadial, bloom.PhaseAngular, bloom.BaseAngleRadians);
    }

    /// <summary>
    /// One live group and its published (behaviour, epoch) pair - THE one piece
    /// of state the target architecture permits. Until Phase 4 wires behaviours
    /// the pair is the constant ("Cruise", 0): published now so the contract is
    /// stable and the map can already key on it.
    /// </summary>
    public readonly record struct FaunaGroupStat(
        string Species,
        int Index,
        int BloomIndex,
        int Members,
        string Behaviour,
        double EpochSeconds);

    /// <summary>
    /// One island's ecology: what it could carry, what it expresses, whether it
    /// is deliberately quiet, and the field its groups follow. `Expressed`
    /// equals capacity until the Phase 3 rhythm wires; the split exists NOW so
    /// the admin map can render "capacity vs expressed" without a schema change
    /// later.
    /// </summary>
    public readonly struct FaunaEcologyIslandStat
    {
        public FaunaEcologyIslandStat(string islandId, double quietFactor,
            int mantaCapacity, int jellyCapacity,
            int mantaExpressed, int jellyExpressed,
            IReadOnlyList<FaunaGroupStat>? groups,
            IReadOnlyList<FaunaBloomStat>? blooms,
            string? mantaPhase = null, double mantaPhaseFraction = 0,
            string? jellyPhase = null, double jellyPhaseFraction = 0)
        {
            IslandId = islandId ?? string.Empty;
            QuietFactor = quietFactor < 0 ? 0 : quietFactor > 1 ? 1 : quietFactor;
            MantaCapacity = mantaCapacity < 0 ? 0 : mantaCapacity;
            JellyCapacity = jellyCapacity < 0 ? 0 : jellyCapacity;
            MantaExpressed = mantaExpressed < 0 ? 0 : mantaExpressed;
            JellyExpressed = jellyExpressed < 0 ? 0 : jellyExpressed;
            Groups = groups ?? Array.Empty<FaunaGroupStat>();
            Blooms = blooms ?? Array.Empty<FaunaBloomStat>();
            MantaPhase = mantaPhase ?? nameof(FaunaPopulationPhase.Bloom);
            MantaPhaseFraction = Fraction01(mantaPhaseFraction);
            JellyPhase = jellyPhase ?? nameof(FaunaPopulationPhase.Bloom);
            JellyPhaseFraction = Fraction01(jellyPhaseFraction);
        }

        public string IslandId { get; }
        public double QuietFactor { get; }
        public int MantaCapacity { get; }
        public int JellyCapacity { get; }
        public int MantaExpressed { get; }
        public int JellyExpressed { get; }
        public IReadOnlyList<FaunaGroupStat> Groups { get; }
        public IReadOnlyList<FaunaBloomStat> Blooms { get; }

        /// <summary>
        /// Where each species' population rhythm is (Phase 3). The predator
        /// reports its LAGGED phase - during the jellies' collapse the rays are
        /// honestly still in their bloom.
        /// </summary>
        public string MantaPhase { get; }
        public double MantaPhaseFraction { get; }
        public string JellyPhase { get; }
        public double JellyPhaseFraction { get; }

        private static double Fraction01(double value) =>
            double.IsNaN(value) || value < 0 ? 0 : value > 1 ? 1 : value;
    }

    /// <summary>
    /// The world's ecology, or the explicit statement that it is off. Written
    /// unconditionally inside the fauna section for the standing reason: absence
    /// must mean "older server", never "no ecology".
    /// </summary>
    public readonly struct FaunaEcologyStat
    {
        public FaunaEcologyStat(bool enabled, int worldSeed,
            IReadOnlyList<FaunaEcologyIslandStat>? islands)
        {
            Enabled = enabled;
            WorldSeed = worldSeed;
            Islands = islands ?? Array.Empty<FaunaEcologyIslandStat>();
        }

        public bool Enabled { get; }
        public int WorldSeed { get; }
        public IReadOnlyList<FaunaEcologyIslandStat> Islands { get; }

        public static FaunaEcologyStat Off =>
            new FaunaEcologyStat(enabled: false, worldSeed: 0, islands: null);
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
            IReadOnlyList<FaunaIslandStat>? islands,
            FaunaEcologyStat? ecology = null)
        {
            Enabled = enabled;
            ClockSeconds = clockSeconds;
            LiveCount = liveCount < 0 ? 0 : liveCount;
            Budget = budget < 0 ? 0 : budget;
            Demand = demand < 0 ? 0 : demand;
            PerPeerBudget = perPeerBudget < 0 ? 0 : perPeerBudget;
            PoseIntervalMs = poseIntervalMs < 0 ? 0 : poseIntervalMs;
            Islands = islands ?? Array.Empty<FaunaIslandStat>();
            Ecology = ecology ?? FaunaEcologyStat.Off;
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

        /// <summary>The ecological layer, or its explicit Off. Schema v9+.</summary>
        public FaunaEcologyStat Ecology { get; }

        /// <summary>What a server with fauna switched off reports.</summary>
        public static FaunaRuntimeStat Off => new FaunaRuntimeStat(
            enabled: false, clockSeconds: 0, liveCount: 0, budget: 0, demand: 0,
            perPeerBudget: 0, poseIntervalMs: 0, islands: null);
    }
}
