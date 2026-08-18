namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// HOW MUCH CAN LIVE ON AN ISLAND - capacity, not count.
    ///
    /// THE TARGET ARCHITECTURE (plan-fauna-liveness.md 4b, principle 4): island
    /// SIZE drives carrying capacity, and the population rhythm (Phase 3)
    /// decides what fraction of it is expressed at any moment. A big island can
    /// feel empty; a small one can occasionally teem. That irregularity is the
    /// point - uniformity reads as generated even when the numbers vary.
    ///
    /// PROVENANCE. The INPUT is recovered twice over: retail sized its own
    /// ecology by the island's AABB - the habitat's height is literally
    /// <c>isd.BoundsExtents.y</c> (HabitatVisualiser), the flock's roaming
    /// radius is <c>lateralIslandBounds.magnitude</c> (FlockVisualiser), and
    /// the patrol radius is the horizontal half-diagonal (PatrolVisualiser) -
    /// and the half-diagonal is exactly the measure used here. The measured
    /// spread inside tier 1 alone is 8.4x (73.7 m to 615.8 m against a 291 m
    /// median, plan 2.5), which is far more variation than tier gives and,
    /// unlike biome (constant across all 46 tier-1 islands), actually
    /// discriminates. The CONSTANTS - the proportionality, the clamps, the
    /// quiet thresholds - are WAREBORN TUNING, unavoidably: no retail count
    /// survives anywhere (plan 2.6).
    ///
    /// QUIET ISLANDS are deliberate, not emergent: a seeded minority of islands
    /// carry a reduced or zero population, so "how much wildlife is here" is a
    /// fact about the place rather than a dial. The recovered gesture behind it:
    /// <c>PopulationManagementState</c> tracked a species population going
    /// CRITICALLY LOW per habitat and <c>LibidoState</c> carried a global
    /// cease-breeding brake - retail populations were neither uniform nor
    /// guaranteed non-zero. An empty island must be LEGIBLY empty: the telemetry
    /// design publishes the quiet factor per island so the admin map shows a
    /// deliberate zero, never missing data.
    ///
    /// THE PER-PEER INVARIANT SURVIVES SCALING. Whole-island admission means an
    /// island whose population exceeds the per-peer budget would be admitted as
    /// NOTHING (IslandFaunaInterestPolicy's own rule), so capacity is clamped to
    /// the budget here, where the number is decided - a big island is dense,
    /// never invisible.
    ///
    /// NOT WIRED YET - Phase 2 stages the functions and their tests; the live
    /// population still comes from <see cref="IslandFaunaPolicy.PopulationFor"/>
    /// until the wiring step (docs/research/design-fauna-ecology-wiring.md).
    /// </summary>
    public static class IslandFaunaCapacity
    {
        /// <summary>
        /// The tier-1 median horizontal half-diagonal, in metres - MEASURED from
        /// the release catalogue's own extracted AABBs (plan 2.5). The island of
        /// this size carries exactly the tier baseline; everything else scales
        /// around it.
        /// </summary>
        public const double MedianHalfDiagonalMetres = 291.0;

        /// <summary>
        /// How far size may swing a population, either way. WAREBORN TUNING:
        /// 0.4x keeps a tiny island's school above the "two lost animals"
        /// floor once rounding is applied, and 2.0x is chosen as EXACTLY two
        /// full baseline schools - which is what makes a second group reachable
        /// inside tier 1 at all (a 1.8x ceiling rounds to 7 mantas against a
        /// group size of 4, so no tier-1 island could ever layer, and layering
        /// was the point). The biggest island's worst case, 8 + 12 = 20, still
        /// clears the per-peer budget of 24 without the clamp.
        /// </summary>
        public const double MinSizeScale = 0.4;
        public const double MaxSizeScale = 2.0;

        /// <summary>
        /// How much MORE than the flat pre-ecology population an island's
        /// capacity is, before size, quiet and the rhythm reduce it.
        ///
        /// THE ARITHMETIC THIS EXISTS FOR, learned from a live regression
        /// (2026-08-18). Capacity is a CEILING that the rhythm expresses a
        /// fraction of - time-weighted, about 0.75 - and the quiet doctrine
        /// removes another ~15% of the world's islands or half their
        /// population. Setting capacity equal to the old flat count therefore
        /// guaranteed a world systematically emptier than the one it replaced:
        /// measured, 250 live against the old 460, about 6 creatures on the
        /// average island against a flat 10. The player saw exactly that and
        /// said so.
        ///
        /// 1.5 puts the AVERAGE island's expressed population at or above the
        /// flat count it replaced, while keeping the spread that is the point:
        /// small islands land near 6, the largest are clamped by the per-peer
        /// budget at 24, and a Bloom on a big island is now visibly a crowd.
        /// WAREBORN TUNING, like every count in this feature.
        /// </summary>
        public const double EcologyDensityScale = 1.5;

        /// <summary>Fraction of islands that are EMPTY - see the quiet doctrine above. WAREBORN TUNING.</summary>
        public const double EmptyFraction = 0.08;

        /// <summary>Fraction that are SPARSE (on top of the empty ones). WAREBORN TUNING.</summary>
        public const double SparseFraction = 0.14;

        /// <summary>The population multiplier a sparse island keeps.</summary>
        public const double SparseFactor = 0.5;

        /// <summary>
        /// The island's horizontal half-diagonal - the recovered measure retail
        /// sized habitats by, without the patrol's +10 m standoff.
        /// </summary>
        public static double HalfDiagonalOf(IslandTerrainEnvelope envelope)
        {
            double halfX = (envelope.MaxX - envelope.MinX) / 2.0;
            double halfZ = (envelope.MaxZ - envelope.MinZ) / 2.0;
            double diagonal = Math.Sqrt((halfX * halfX) + (halfZ * halfZ));
            return diagonal > 0.0 ? diagonal : 1.0;
        }

        /// <summary>How much this island's size scales its populations, clamped.</summary>
        public static double SizeScaleFor(IslandTerrainEnvelope envelope) =>
            Math.Clamp(HalfDiagonalOf(envelope) / MedianHalfDiagonalMetres,
                MinSizeScale, MaxSizeScale);

        /// <summary>
        /// The quiet multiplier for an island: 0 (empty), <see cref="SparseFactor"/>
        /// (sparse) or 1 (ordinary), from a stable hash of the island id - the
        /// same FNV discipline as every other per-island decision, so a restart
        /// cannot re-roll which islands are the quiet ones.
        /// </summary>
        public static double QuietFactorFor(IslandId islandId)
        {
            const uint OffsetBasis = 2166136261;
            const uint Prime = 16777619;
            uint hash = OffsetBasis;
            string id = "quiet|" + islandId;
            for (int i = 0; i < id.Length; i++)
            {
                hash = (hash ^ id[i]) * Prime;
            }
            double u = hash / 4294967296.0;
            if (u < EmptyFraction) return 0.0;
            if (u < EmptyFraction + SparseFraction) return SparseFactor;
            return 1.0;
        }

        /// <summary>
        /// The island's carrying capacity for one species, in creatures: the
        /// tier baseline (<see cref="IslandFaunaPolicy"/>'s recovered-direction
        /// counts) scaled by the island's own size and its quiet factor, then
        /// clamped so the WHOLE island stays admissible per peer.
        ///
        /// A non-empty island never rounds a species below 2: one animal is a
        /// lost animal, which is the exact reading the school sizes were chosen
        /// to avoid.
        /// </summary>
        public static int CapacityFor(
            FaunaSpecies species, int tier, IslandTerrainEnvelope envelope, IslandId islandId)
        {
            double quiet = QuietFactorFor(islandId);
            if (quiet <= 0.0) return 0;

            int baseline = species == FaunaSpecies.MantaRay
                ? IslandFaunaPolicy.MantaCountFor(tier)
                : IslandFaunaPolicy.JellyFishCountFor(tier);
            int scaled = (int)Math.Round(
                baseline * EcologyDensityScale * SizeScaleFor(envelope) * quiet);
            return Math.Max(scaled, 2);
        }

        /// <summary>
        /// The WIDTH of the entity-id block an island reserves for one species
        /// under the ecology: the size-scaled capacity with NO quiet factor and
        /// NO per-peer clamp - the most creatures the island could ever express.
        ///
        /// This is what keeps the operator's knobs off the id layout: the live
        /// population is <see cref="CapacityFor"/> (quiet-scaled) clamped to the
        /// per-peer budget, and both of those only ever REDUCE, so the live
        /// count fits the block whatever the operator sets. The block itself is
        /// a pure function of the catalogue, so ids are identical on every boot
        /// of a given build. (Across BUILDS a tuning-constant change may re-lay
        /// them, which is safe: the env is read once at boot and no client
        /// session survives a server restart.)
        /// </summary>
        public static int IdBlockFor(FaunaSpecies species, int tier, IslandTerrainEnvelope envelope)
        {
            int baseline = species == FaunaSpecies.MantaRay
                ? IslandFaunaPolicy.MantaCountFor(tier)
                : IslandFaunaPolicy.JellyFishCountFor(tier);
            return Math.Max((int)Math.Round(
                baseline * EcologyDensityScale * SizeScaleFor(envelope)), 2);
        }

        /// <summary>
        /// Both species' capacities, reduced together if their sum would exceed
        /// the per-peer budget - proportionally, jellies rounding down first,
        /// because the budget must hold EXACTLY (whole-island admission refuses
        /// an island one creature over it).
        /// </summary>
        public static (int MantaRays, int JellyFish) ClampedToPeerBudget(
            int mantaRays, int jellyFish, int perPeerBudget)
        {
            if (mantaRays < 0 || jellyFish < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mantaRays),
                    "a capacity cannot be negative");
            }
            int total = mantaRays + jellyFish;
            if (perPeerBudget <= 0) return (0, 0);
            if (total <= perPeerBudget) return (mantaRays, jellyFish);

            double factor = (double)perPeerBudget / total;
            int jellies = (int)Math.Floor(jellyFish * factor);
            int mantas = Math.Min(mantaRays, perPeerBudget - jellies);
            return (mantas, jellies);
        }

        /// <summary>
        /// How many GROUPS of one species the island's capacity supports: the
        /// capacity divided by the tier-1 group size, held to 1..3. More than
        /// one group is what turns "there is a school here" into "this place is
        /// inhabited" - and the multi-group STRUCTURE is recovered
        /// (HabitatPatrolState ran a separate orbit phase per species on one
        /// island); how many groups of ONE species an island had is not, so the
        /// division and the cap are WAREBORN TUNING.
        /// </summary>
        public static int GroupCountFor(FaunaSpecies species, int capacity)
        {
            if (capacity <= 0) return 0;
            int groupSize = IslandFaunaPolicy.SchoolSizeFor(species, 1);
            return Math.Clamp(capacity / Math.Max(groupSize, 1), 1, 3);
        }
    }
}
