namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>The five states of an island population's hidden rhythm.</summary>
    public enum FaunaPopulationPhase
    {
        /// <summary>The floor: a quarter of capacity, holding.</summary>
        Dormant,

        /// <summary>Climbing from the floor to full capacity.</summary>
        Growing,

        /// <summary>Full capacity, holding. The island teems.</summary>
        Bloom,

        /// <summary>Falling from capacity to the trough - deeper than Dormant.</summary>
        Collapse,

        /// <summary>Creeping from the trough back up to the Dormant floor.</summary>
        Recovery,
    }

    /// <summary>
    /// THE POPULATION RHYTHM: what fraction of an island's carrying capacity is
    /// EXPRESSED right now, as a pure function of (seed, island, time).
    ///
    /// THE TARGET ARCHITECTURE (plan-fauna-liveness.md 4b, principle 3): NOT
    /// literal Lotka-Volterra - an ODE breaks closed-form evaluation - but a
    /// hidden state machine per island, Dormant -> Growing -> Bloom -> Collapse
    /// -> Recovery, with procedural phase lengths derived from
    /// hash(seed, islandId, cycle, phase), so "which phase, and how far through"
    /// is computable at any instant by anyone holding the seed. The predator
    /// (manta) runs THE SAME rhythm as its prey (jelly), evaluated a hashed lag
    /// behind it - the classic predator-follows-food read at zero state.
    ///
    /// THE RAMPS ARE THE CONVERGENCE. The architecture sketched
    /// N_{t+1} = N_t + lambda(N* - N_t) for streaming; iterating that is state.
    /// The smoothstep ramps below ARE that gradual convergence made closed-form:
    /// an expressed count never jumps, it crosses each integer once per
    /// minutes-long ramp, so the checkout layer streams one arrival or departure
    /// every few tens of seconds during a transition and none at all during a
    /// hold.
    ///
    /// CYCLES ARE WALKED, NOT INDEXED, and the walk is the price of procedural
    /// phase lengths: which cycle contains time t is the smallest prefix sum of
    /// hashed durations exceeding t. A cycle averages ~24 minutes, so a week of
    /// uptime is ~420 iterations of trivial arithmetic - cheap for the server at
    /// 0.5 Hz per island and cheap for a browser doing the same walk.
    ///
    /// PROVENANCE. WAREBORN TUNING throughout - every duration, level and lag is
    /// a choice. The recovered GESTURE behind it (plan proposal J):
    /// <c>PopulationManagementState</c> is a map of "time since this species'
    /// population went CRITICALLY LOW" per habitat, and <c>LibidoState</c>
    /// carries a global cease-breeding brake - retail populations genuinely
    /// swung, and were neither uniform nor guaranteed non-zero. The thresholds
    /// and rates are lost; this module is a reconstruction of the swing's
    /// EXISTENCE, not of its law.
    /// </summary>
    public static class IslandFaunaRhythm
    {
        /// <summary>
        /// The Dormant hold level, as a fraction of capacity.
        ///
        /// RAISED FROM 0.25 AFTER A LIVE REGRESSION (2026-08-18). At 0.25 a
        /// Dormant island showed a QUARTER of its capacity, which on the common
        /// small island is the two-animal floor - and since every island began
        /// its walk in Dormant (see <see cref="StartOffsetSeconds"/>), the whole
        /// world showed "2 rays and 2 jellyfish" for the first minutes of every
        /// boot. The player's verdict was "looks so empty", and they were right:
        /// uniform emptiness reads as a broken spawner, not as ecology. Dormant
        /// is now an ORDINARY QUIET LEVEL - a bit over half strength - and the
        /// contrast with Bloom (16 animals against 9 on a big island) carries
        /// the rhythm instead of the difference between 3 and 12.
        /// </summary>
        public const double DormantLevel = 0.55;

        /// <summary>
        /// The Collapse trough - deeper than Dormant, so a crash reads as a
        /// crash. Raised with <see cref="DormantLevel"/> and for the same
        /// reason: the trough should read as "thin here today", not as "the
        /// spawner is broken".
        /// </summary>
        public const double TroughLevel = 0.30;

        /// <summary>
        /// Base phase durations, in seconds, scaled per cycle by a hashed factor
        /// in [0.7, 1.3]. Long enough that a phase is a fact about the place
        /// rather than a flicker, short enough that a session sees the world
        /// change state.
        ///
        /// BLOOM IS THE DOMINANT STATE, deliberately: a world is supposed to
        /// feel inhabited, with scarcity as the exception that means something.
        /// The time-weighted mean expression is about 0.75 of capacity, and
        /// <see cref="IslandFaunaCapacity.EcologyDensityScale"/> is set so that
        /// 0.75 of the AVERAGE island's capacity is at least the flat population
        /// the pre-ecology world put on every island.
        /// </summary>
        public static readonly IReadOnlyList<double> BasePhaseSeconds = new double[]
        {
            300.0, // Dormant
            240.0, // Growing
            600.0, // Bloom
            150.0, // Collapse
            240.0, // Recovery
        };

        /// <summary>
        /// The nominal length of one full cycle, in seconds - the sum of the
        /// base durations, before the per-cycle hash jitter. It is the span the
        /// per-island start offset is spread over.
        /// </summary>
        public static double NominalCycleSeconds
        {
            get
            {
                double total = 0.0;
                for (int i = 0; i < BasePhaseSeconds.Count; i++) total += BasePhaseSeconds[i];
                return total;
            }
        }

        /// <summary>
        /// WHERE AN ISLAND'S WALK STARTS - the fix for the live regression of
        /// 2026-08-18, and the property that makes this an ecology rather than
        /// one global dimmer.
        ///
        /// THE BUG: the walk began at cycle 0, phase 0 for EVERY island, so at
        /// t=0 all 46 tier-1 islands were Dormant together and stayed in
        /// lockstep for the first minutes of every boot (hashed DURATIONS pull
        /// them apart only gradually - measured, they need about ten minutes to
        /// scatter). A player who logged in after a restart therefore saw the
        /// entire world at its emptiest simultaneously. The phase lengths were
        /// per-island; the STARTING POINT was not, and that was the whole
        /// defect.
        ///
        /// THE FIX: each island's clock is advanced by a hashed offset spread
        /// over a full nominal cycle, so at ANY instant - including t=0 - the
        /// world is scattered across all five phases. It costs nothing: the
        /// offset is a pure function of (seed, island), so every property this
        /// module promised still holds.
        /// </summary>
        public static double StartOffsetSeconds(int worldSeed, IslandId islandId) =>
            NominalCycleSeconds * Unit(worldSeed, islandId, cycle: -2, phase: 0);

        /// <summary>
        /// How far behind its prey the predator's rhythm runs, in seconds:
        /// 120-360 by island hash. The rays thin out AFTER the jellies do.
        /// </summary>
        public static double PredatorLagSeconds(int worldSeed, IslandId islandId) =>
            120.0 + (240.0 * Unit(worldSeed, islandId, cycle: -1, phase: 0));

        /// <summary>
        /// WHERE THE RHYTHM IS at <paramref name="elapsedSeconds"/>: the phase,
        /// how far through it (0..1), and which cycle. Total for negative input
        /// (clamped to zero - the predator lag can ask about the world before
        /// boot, and the answer is "the start of cycle zero").
        /// </summary>
        public static (FaunaPopulationPhase Phase, double PhaseFraction, int Cycle) At(
            int worldSeed, IslandId islandId, double elapsedSeconds)
        {
            // The per-island start offset is what desynchronises the world; see
            // StartOffsetSeconds. Negative input (the predator lag can ask about
            // the world before boot) clamps to zero BEFORE the offset, so an
            // island's identity still decides where it is.
            double remaining = (elapsedSeconds < 0.0 ? 0.0 : elapsedSeconds)
                + StartOffsetSeconds(worldSeed, islandId);
            int cycle = 0;
            while (true)
            {
                for (int phase = 0; phase < BasePhaseSeconds.Count; phase++)
                {
                    double duration = PhaseDuration(worldSeed, islandId, cycle, phase);
                    if (remaining < duration)
                    {
                        return ((FaunaPopulationPhase)phase, remaining / duration, cycle);
                    }
                    remaining -= duration;
                }
                cycle++;
            }
        }

        /// <summary>One phase's duration in one cycle, in seconds: base x [0.7, 1.3] by hash.</summary>
        public static double PhaseDuration(int worldSeed, IslandId islandId, int cycle, int phase) =>
            BasePhaseSeconds[phase] * (0.7 + (0.6 * Unit(worldSeed, islandId, cycle, phase)));

        /// <summary>
        /// The PREY's expressed fraction of capacity at an instant: the phase
        /// levels joined by smoothstep ramps, so the function is C1 everywhere -
        /// a kink in expression would be a burst of arrivals on the wire.
        /// </summary>
        public static double PreyExpressionAt(
            int worldSeed, IslandId islandId, double elapsedSeconds)
        {
            (FaunaPopulationPhase phase, double f, _) = At(worldSeed, islandId, elapsedSeconds);
            return phase switch
            {
                FaunaPopulationPhase.Dormant => DormantLevel,
                FaunaPopulationPhase.Growing =>
                    DormantLevel + ((1.0 - DormantLevel) * SmoothStep(f)),
                FaunaPopulationPhase.Bloom => 1.0,
                FaunaPopulationPhase.Collapse =>
                    1.0 - ((1.0 - TroughLevel) * SmoothStep(f)),
                _ => TroughLevel + ((DormantLevel - TroughLevel) * SmoothStep(f)),
            };
        }

        /// <summary>The predator's fraction: the SAME rhythm, a hashed lag behind.</summary>
        public static double PredatorExpressionAt(
            int worldSeed, IslandId islandId, double elapsedSeconds) =>
            PreyExpressionAt(worldSeed, islandId,
                elapsedSeconds - PredatorLagSeconds(worldSeed, islandId));

        /// <summary>The species dispatch: jellies are the prey, mantas follow the food.</summary>
        public static double ExpressionAt(
            int worldSeed, IslandId islandId, FaunaSpecies species, double elapsedSeconds) =>
            species == FaunaSpecies.JellyFish
                ? PreyExpressionAt(worldSeed, islandId, elapsedSeconds)
                : PredatorExpressionAt(worldSeed, islandId, elapsedSeconds);

        /// <summary>
        /// A capacity expressed through a fraction, as a COUNT.
        ///
        /// THE FLOOR IS PROPORTIONAL, not a flat two, and the difference is what
        /// the live regression taught. A flat floor says the same thing about a
        /// two-animal rock and a twelve-animal island - and on the twelve-animal
        /// island "2 of 12" reads as a broken spawner rather than as a lean
        /// season. The floor is therefore <see cref="TroughLevel"/> of the
        /// island's OWN capacity, so a big island's worst day is still a group
        /// and a small island's is still two animals.
        ///
        /// Never one: a lone animal is the reading every count in this feature
        /// is chosen to avoid. Never zero for a populated island either - zero
        /// is reserved for the quiet doctrine, where it is a DELIBERATE fact
        /// about the place (<see cref="IslandFaunaCapacity.QuietFactorFor"/>)
        /// rather than a moment in a cycle.
        /// </summary>
        public static int ExpressedCount(int capacity, double fraction)
        {
            if (capacity <= 0) return 0;
            int expressed = (int)Math.Round(capacity * Math.Clamp(fraction, 0.0, 1.0));
            int floor = Math.Max(
                Math.Min(2, capacity),
                (int)Math.Round(capacity * TroughLevel));
            return Math.Clamp(expressed, floor, capacity);
        }

        /// <summary>
        /// The phase a species' population is in, for telemetry - the predator
        /// reports its LAGGED phase, which is the honest one: during the jellies'
        /// collapse the rays are still in their bloom, and the map should say so.
        /// </summary>
        public static (FaunaPopulationPhase Phase, double PhaseFraction) PhaseFor(
            int worldSeed, IslandId islandId, FaunaSpecies species, double elapsedSeconds)
        {
            double t = species == FaunaSpecies.JellyFish
                ? elapsedSeconds
                : elapsedSeconds - PredatorLagSeconds(worldSeed, islandId);
            (FaunaPopulationPhase phase, double fraction, _) = At(worldSeed, islandId, t);
            return (phase, fraction);
        }

        private static double SmoothStep(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * (3.0 - (2.0 * t));
        }

        /// <summary>
        /// The deterministic uniform, FNV-1a over the textual tuple - the same
        /// stable-across-processes discipline as every other fauna hash, tagged
        /// "rhythm" so it can never collide with the ecology's bloom channels.
        /// </summary>
        public static double Unit(int worldSeed, IslandId islandId, int cycle, int phase)
        {
            const uint OffsetBasis = 2166136261;
            const uint Prime = 16777619;
            uint hash = OffsetBasis;
            void Mix(string s)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    hash = (hash ^ s[i]) * Prime;
                }
                hash = (hash ^ '|') * Prime;
            }
            Mix("rhythm");
            Mix(worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(islandId.ToString());
            Mix(cycle.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(phase.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return hash / 4294967296.0;
        }
    }
}
