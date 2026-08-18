namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// The two creatures this server is willing to seed.
    ///
    /// Retail carried more: <c>SpeciesType.MantaRay</c> for the rays, and four
    /// separate basic-species values for the jellies (<c>JellyFishSeed</c>,
    /// <c>JellyFishFlower</c>, <c>JellyFishDesertA</c>, <c>JellyFishDesertB</c>).
    /// Those four are deliberately collapsed to one here: the surviving player
    /// client proves the NAMES existed but no surviving table proves which island
    /// got which, so four enum members would be four claims this project cannot
    /// support. One jelly is honest; four would be invention with better
    /// typography.
    ///
    /// Both names resolve against the packaged client prefab census
    /// (Ship/client-entity-prefabs.txt), so an AddEntity naming either one draws
    /// something rather than throwing on the client.
    /// </summary>
    public enum FaunaSpecies
    {
        /// <summary>
        /// A drifting jelly. AN EXPLICIT ERA CHOICE, not an oversight: jellyfish
        /// were DISCONTINUED late in retail's life, so a server that seeds them is
        /// choosing to present the earlier world rather than the last one. Stated
        /// here so nobody later reads their presence as a faithful reconstruction
        /// of the final live game.
        /// </summary>
        JellyFish,

        /// <summary>A perimeter-patrolling manta ray. Present for the whole of retail's life.</summary>
        MantaRay,
    }

    /// <summary>
    /// One seeded creature: its identity, its species, the island that owns it,
    /// and its position within that island's population.
    ///
    /// <see cref="Index"/> is not decoration. It is the ONLY thing that makes two
    /// mantas on the same island differ, and <see cref="IslandFaunaMovement"/>
    /// phases their orbits off it, so a population does not fly in a single stack.
    /// It is a position in the ordered population, so it is stable across restarts
    /// in exactly the way an entity id must be.
    /// </summary>
    public readonly record struct FaunaCreature(
        long EntityId,
        FaunaSpecies Species,
        IslandId IslandId,
        int Index);

    /// <summary>
    /// WHO LIVES ON AN ISLAND, and whether the feature is switched on at all.
    ///
    /// The pure half of island fauna: no clock, no state, no allocation beyond the
    /// population list it is asked for, and nothing outside the BCL. Everything
    /// here is a function of its arguments, which is what lets the population be
    /// re-derived identically on every process start instead of being persisted.
    ///
    /// PROVENANCE, because this is where the temptation to invent lives. Retail's
    /// ecology bookkeeping - habitat, flock, population - belonged to GSim, and
    /// GSim is not preserved. Nothing in the surviving player client states how
    /// many creatures an island carried. Every COUNT in this file is therefore
    /// labelled WAREBORN TUNING and is a choice this project is making, not a
    /// value it recovered. The DIRECTION the counts move in is different: the
    /// worldsadrift.fandom.com Biome and Creatures pages describe tier 1
    /// Wilderness as the calm end and tier 4 Badlands as the hostile end, so
    /// scaling with the surveyed tier is WIKI-SOURCED even though the numbers are
    /// not.
    /// </summary>
    public static class IslandFaunaPolicy
    {
        /// <summary>
        /// The operator switch. Fauna is a NEW RELAYED SENDER - every creature is
        /// an entity whose transform is pushed to interested peers - and the
        /// standing rule in docs/multiplayer.md is that such a feature arrives off
        /// and is turned on deliberately.
        /// </summary>
        public const string EnabledEnvVar = "WAREBORN_ISLAND_FAUNA";

        /// <summary>
        /// The first entity id a seeded creature may use.
        ///
        /// A DISJOINT BAND, and the band it must stay clear of is the one directly
        /// below it: <c>TreeFall.FirstLogEntityId</c> is 2_000_000_000L and felled
        /// logs count UPWARDS from there for the life of the process. A hundred
        /// million ids of headroom separates the two, which no plausible world can
        /// exhaust - the whole 254-island release catalogue seeds a few thousand
        /// creatures - and the bands MUST NOT OVERLAP, because a fauna transform
        /// and a falling-log transform naming the same entity would corrupt the
        /// client's entity table in a way that reads as a protocol bug rather than
        /// as an allocation bug.
        ///
        /// Like a log, a creature is deliberately NOT a world registration: it must
        /// not enter the connect-time spawn plan, the loading barrier's count, or
        /// the domain host's expected-owned list.
        /// </summary>
        public const long FirstFaunaEntityId = 2_100_000_000L;

        /// <summary>
        /// How many creatures may be live world-wide at once.
        ///
        /// WAREBORN TUNING. Nothing recovered from retail bounds this; the number
        /// is chosen from what the wire can afford. Each live creature is a pose
        /// pushed at <c>IslandFaunaRegistry</c>'s deliberately sub-20 Hz cadence to
        /// every peer that can see it, so this constant - not the per-island counts
        /// - is what actually caps the bandwidth the feature can spend. Twenty-four
        /// is a handful of populated islands' worth of creatures visible at once,
        /// which is the same order as a couple of flying ships.
        /// </summary>
        public const int DefaultMaxConcurrent = 24;

        /// <summary>
        /// Mantas on the calmest island. WAREBORN TUNING: a count retail never told
        /// us. Two rather than one so the perimeter reads as patrolled rather than
        /// as a single lost animal.
        /// </summary>
        private const int MantaCountAtTier1 = 2;

        /// <summary>
        /// Extra mantas per tier above 1. WAREBORN TUNING. One per step, so tier 4
        /// Badlands carries five - noticeably busier than Wilderness without
        /// turning the sky into a swarm.
        /// </summary>
        private const int MantaPerTierStep = 1;

        /// <summary>
        /// Jellies on the calmest island. WAREBORN TUNING. One, because a jelly is
        /// a hazard a player walks into rather than scenery, and the calm end of
        /// the world should carry a token one, not a field of them.
        /// </summary>
        private const int JellyFishCountAtTier1 = 1;

        /// <summary>Extra jellies per tier above 1. WAREBORN TUNING; see <see cref="MantaPerTierStep"/>.</summary>
        private const int JellyFishPerTierStep = 1;

        /// <summary>The lowest and highest surveyed tier. Mirrors IslandSurveyProfile's own 1..4 guard.</summary>
        private const int LowestTier = 1;
        private const int HighestTier = 4;

        /// <summary>
        /// Whether island fauna is switched on, from the operator's
        /// <see cref="EnabledEnvVar"/> string.
        ///
        /// OFF unless the operator opts in, and the accepted tokens are exactly
        /// <see cref="IslandTerrainInterestPolicy.EnabledFrom"/>'s - "1", "true" or
        /// "yes", case-insensitively - so an operator who has learned one flag has
        /// learned all of them. Null, empty, "0", "false" and anything unrecognised
        /// all mean off: a typo must fail SAFE, leaving the world exactly as it was
        /// before this feature existed.
        /// </summary>
        public static bool EnabledFrom(string? value) =>
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A creature budget from the operator, or null to accept
        /// <see cref="DefaultMaxConcurrent"/>.
        ///
        /// Shaped exactly like <c>TreeFall.ParseBudget</c>, including the two
        /// choices that look arbitrary and are not. ZERO IS ACCEPTED and means "no
        /// creatures" - a second kill switch, and the one an operator reaches for
        /// when they would rather starve the feature than hunt for its flag.
        /// NEGATIVE AND UNPARSEABLE FALL BACK rather than throwing, because a typo
        /// in an environment variable must never stop a server booting.
        /// </summary>
        public static int? ParseBudget(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)
                || !int.TryParse(raw!.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int budget)
                || budget < 0)
            {
                return null;
            }
            return budget;
        }

        /// <summary>
        /// The client prefab base name for a species.
        ///
        /// Both names are in the packaged census, so <c>ClientEntityPrefabs.CanResolve</c>
        /// returns true for either. That check matters more here than it looks: a
        /// name the client cannot resolve produces an entity that exists on the
        /// wire and draws NOTHING, which is indistinguishable from the feature
        /// being broken.
        /// </summary>
        public static string PrefabNameFor(FaunaSpecies species) => species switch
        {
            FaunaSpecies.JellyFish => "JellyFish",
            FaunaSpecies.MantaRay => "MantaRay",
            _ => throw new ArgumentOutOfRangeException(nameof(species),
                "no client prefab is known for species '" + species + "'"),
        };

        /// <summary>
        /// How many mantas a tier-<paramref name="tier"/> island carries. Counts are
        /// WAREBORN TUNING; the fact that they RISE with the tier is WIKI-SOURCED
        /// from the fandom Biome and Creatures pages, which place calm Wilderness at
        /// tier 1 and hostile Badlands at tier 4.
        /// </summary>
        public static int MantaCountFor(int tier) =>
            MantaCountAtTier1 + (MantaPerTierStep * (ClampTier(tier) - LowestTier));

        /// <summary>
        /// How many jellies a tier-<paramref name="tier"/> island carries. Same
        /// provenance as <see cref="MantaCountFor"/>: WAREBORN TUNING counts,
        /// WIKI-SOURCED direction.
        /// </summary>
        public static int JellyFishCountFor(int tier) =>
            JellyFishCountAtTier1 + (JellyFishPerTierStep * (ClampTier(tier) - LowestTier));

        /// <summary>
        /// Everything that lives on one island, in a fixed order, with contiguous
        /// distinct entity ids starting at <paramref name="firstEntityId"/>.
        ///
        /// PURE AND TOTAL, which is the whole point. The population is a function of
        /// the island's SURVEYED TIER and nothing else - no clock, no entropy, no
        /// accumulated state - so a restarted server re-derives byte-identical ids
        /// in byte-identical order and a reconnecting player is not handed a manta
        /// whose id used to mean something else. Nothing is persisted because
        /// nothing needs to be.
        ///
        /// Mantas are emitted before jellies so the ORDER is a property of the
        /// function rather than of dictionary iteration, and
        /// <see cref="FaunaCreature.Index"/> is the position in this list.
        /// </summary>
        public static IReadOnlyList<FaunaCreature> PopulationFor(
            ReleaseIslandRecord island, long firstEntityId)
        {
            if (island == null)
            {
                throw new ArgumentNullException(nameof(island));
            }

            int tier = ClampTier(island.Survey.Tier);
            int mantas = MantaCountFor(tier);
            int jellies = JellyFishCountFor(tier);
            IslandId id = island.Definition.Id;

            FaunaCreature[] population = new FaunaCreature[mantas + jellies];
            int index = 0;
            for (int i = 0; i < mantas; i++, index++)
            {
                population[index] = new FaunaCreature(
                    firstEntityId + index, FaunaSpecies.MantaRay, id, index);
            }
            for (int i = 0; i < jellies; i++, index++)
            {
                population[index] = new FaunaCreature(
                    firstEntityId + index, FaunaSpecies.JellyFish, id, index);
            }

            return Array.AsReadOnly(population);
        }

        /// <summary>
        /// A surveyed tier held to 1..4. <c>IslandSurveyProfile</c> already refuses
        /// anything else at construction, so this only defends against a caller
        /// passing a raw number; it clamps rather than throwing because a bad tier
        /// must degrade the population, never stop an island loading.
        /// </summary>
        private static int ClampTier(int tier) =>
            tier < LowestTier ? LowestTier : tier > HighestTier ? HighestTier : tier;
    }
}
