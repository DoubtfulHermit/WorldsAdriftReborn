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
        /// <summary>The Dormant hold level, as a fraction of capacity. WAREBORN TUNING.</summary>
        public const double DormantLevel = 0.25;

        /// <summary>The Collapse trough - deeper than Dormant, so a crash reads as a crash.</summary>
        public const double TroughLevel = 0.15;

        /// <summary>
        /// Base phase durations, in seconds, scaled per cycle by a hashed factor
        /// in [0.7, 1.3]. The averages put a full cycle at ~24 minutes: long
        /// enough that a phase is a fact about the place rather than a flicker,
        /// short enough that a session sees the world change state.
        /// </summary>
        public static readonly IReadOnlyList<double> BasePhaseSeconds = new double[]
        {
            300.0, // Dormant
            240.0, // Growing
            480.0, // Bloom
            180.0, // Collapse
            240.0, // Recovery
        };

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
            double remaining = elapsedSeconds < 0.0 ? 0.0 : elapsedSeconds;
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
        /// A capacity expressed through a fraction, as a COUNT. Never below two
        /// on a populated island - one animal is a lost animal, the reading every
        /// count in this feature is chosen to avoid - and never above capacity.
        /// </summary>
        public static int ExpressedCount(int capacity, double fraction)
        {
            if (capacity <= 0) return 0;
            int expressed = (int)Math.Round(capacity * Math.Clamp(fraction, 0.0, 1.0));
            return Math.Clamp(expressed, Math.Min(2, capacity), capacity);
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
