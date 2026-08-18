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
    /// its position within that island's population, and WHICH SCHOOL IT SWIMS IN.
    ///
    /// <see cref="Index"/> is not decoration. It is a position in the ordered
    /// population, so it is stable across restarts in exactly the way an entity id
    /// must be, and <see cref="IslandFaunaPlan"/> allocates ids from it.
    ///
    /// <see cref="SchoolIndex"/> and <see cref="MemberIndex"/> are what make a
    /// population a set of SCHOOLS rather than a set of loners, and they are carried
    /// on the creature rather than recomputed because <see cref="IslandFaunaMovement"/>
    /// is handed one creature at a time and must never need to know what else lives
    /// on the island. The school is the thing that moves; the member is an offset
    /// from it (<see cref="IslandFaunaSchool"/>).
    /// </summary>
    /// <param name="EntityId">The wire identity. Inside the fauna band; see
    /// <see cref="IslandFaunaPolicy.FirstFaunaEntityId"/>.</param>
    /// <param name="Species">What it is.</param>
    /// <param name="IslandId">The island that owns it.</param>
    /// <param name="Index">Its position in the island's whole ordered population.</param>
    /// <param name="SchoolIndex">Which school of its own species on this island it
    /// belongs to, counting from zero. Schools are phase-spread around the island so
    /// two of them are never in the same place.</param>
    /// <param name="MemberIndex">Its position INSIDE that school, counting from zero.
    /// This is the only thing that separates two members of one school.</param>
    public readonly record struct FaunaCreature(
        long EntityId,
        FaunaSpecies Species,
        IslandId IslandId,
        int Index,
        int SchoolIndex,
        int MemberIndex);

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
        /// WAREBORN TUNING, and NO LONGER THE WIRE'S SAFETY VALVE. It used to be:
        /// while fauna checked out per creature at the global resource radius, the
        /// world-wide count was the only bound on what one peer could be sent, so it
        /// was held at 24 and the tier-1 world got 8 populated islands out of 46.
        /// That is what the player saw when they said there should be more wildlife.
        ///
        /// <see cref="IslandFaunaInterestPolicy.DefaultPerPeerCreatures"/> now bounds
        /// the wire directly, PER PEER, so this constant bounds only how much of the
        /// world exists at once - a dictionary entry and a closed-form pose each,
        /// costing nothing a peer can feel. Four thousand covers the complete
        /// 254-island release catalogue (3,866 creatures) with headroom, so no
        /// operator has to discover the cap by finding empty islands. The number that
        /// governs desync is the per-peer one, and it did not move.
        /// </summary>
        public const int DefaultMaxConcurrent = 4000;

        /// <summary>
        /// How many mantas swim in ONE school on the calmest island. WAREBORN TUNING
        /// - the sweep of the decompiled client finds no group size anywhere, because
        /// <c>FlockStateData</c>'s membership is two unbounded lists and the bookkeeping
        /// that filled them lived in GSim. See <see cref="IslandFaunaSchool"/> for the
        /// full negative result and for the two PROVED distances (10 m ready, 15 m
        /// caught up) that do anchor how big a school is in metres.
        ///
        /// Four, because that is the smallest count that reads unambiguously as a
        /// group rather than as two animals that happen to be near each other.
        /// </summary>
        private const int MantaSchoolSizeAtTier1 = 4;

        /// <summary>
        /// Extra mantas per school per tier above 1. WAREBORN TUNING. The DIRECTION
        /// is WIKI-SOURCED, from the worldsadrift.fandom.com Biome and Creatures
        /// pages placing tier-1 Wilderness at the calm end and tier-4 Badlands at the
        /// hostile end; the step is a choice.
        /// </summary>
        private const int MantaSchoolPerTierStep = 1;

        /// <summary>
        /// How many jellies drift in ONE shoal on the calmest island. WAREBORN TUNING.
        ///
        /// Six, and much larger than a manta school on purpose. Retail jellies did
        /// NOT flock - proved three ways in <see cref="IslandFaunaSchool"/> - so the
        /// only thing that ever made jellyfish read as a shoal was DENSITY. One
        /// jelly per island, which is what this server seeded before, cannot look
        /// like anything; it is a single animal drifting under a rock, which is why
        /// the player had never seen a jellyfish school.
        /// </summary>
        private const int JellyFishShoalSizeAtTier1 = 6;

        /// <summary>Extra jellies per shoal per tier above 1. WAREBORN TUNING; see <see cref="MantaSchoolPerTierStep"/>.</summary>
        private const int JellyFishShoalPerTierStep = 2;

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
        /// How many SCHOOLS of each species a single island carries.
        ///
        /// One, across every tier, and that is a deliberate shape rather than an
        /// unfinished one. A tier's danger is expressed by making its school BIGGER
        /// (<see cref="MantaCountFor"/>), not by scattering more separate groups
        /// around the same rock: two schools on one island are two things a player
        /// must be in two places to see, whereas one larger school is the same
        /// animals arriving together. It is also what keeps
        /// <see cref="PopulationFor"/>'s largest output (19, on a tier-4 island)
        /// inside <see cref="IslandFaunaInterestPolicy.DefaultPerPeerCreatures"/>, so
        /// a player standing on an island is never shown a truncated school.
        /// </summary>
        public const int SchoolsPerIsland = 1;

        /// <summary>
        /// How many mantas a tier-<paramref name="tier"/> island carries, across all
        /// its schools. Counts are WAREBORN TUNING; the fact that they RISE with the
        /// tier is WIKI-SOURCED from the fandom Biome and Creatures pages, which
        /// place calm Wilderness at tier 1 and hostile Badlands at tier 4.
        /// </summary>
        public static int MantaCountFor(int tier) =>
            SchoolsPerIsland * (MantaSchoolSizeAtTier1
                + (MantaSchoolPerTierStep * (ClampTier(tier) - LowestTier)));

        /// <summary>
        /// How many jellies a tier-<paramref name="tier"/> island carries, across all
        /// its shoals. Same provenance as <see cref="MantaCountFor"/>: WAREBORN
        /// TUNING counts, WIKI-SOURCED direction.
        /// </summary>
        public static int JellyFishCountFor(int tier) =>
            SchoolsPerIsland * (JellyFishShoalSizeAtTier1
                + (JellyFishShoalPerTierStep * (ClampTier(tier) - LowestTier)));

        /// <summary>How many members one school of <paramref name="species"/> has at <paramref name="tier"/>.</summary>
        public static int SchoolSizeFor(FaunaSpecies species, int tier) =>
            (species == FaunaSpecies.MantaRay ? MantaCountFor(tier) : JellyFishCountFor(tier))
            / SchoolsPerIsland;

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
        ///
        /// A SCHOOL IS A CONTIGUOUS RUN OF IDS, which is not cosmetic: it is what
        /// lets <see cref="IslandFaunaInterestPolicy.Reconcile"/> order additions by
        /// id and have a school arrive together rather than interleaved with another
        /// island's, and it is what makes a school a readable block in the log.
        /// </summary>
        public static IReadOnlyList<FaunaCreature> PopulationFor(
            ReleaseIslandRecord island, long firstEntityId)
        {
            if (island == null)
            {
                throw new ArgumentNullException(nameof(island));
            }

            int tier = ClampTier(island.Survey.Tier);
            IslandId id = island.Definition.Id;

            List<FaunaCreature> population = new List<FaunaCreature>(
                MantaCountFor(tier) + JellyFishCountFor(tier));
            AddSchools(population, FaunaSpecies.MantaRay, tier, id, firstEntityId);
            AddSchools(population, FaunaSpecies.JellyFish, tier, id, firstEntityId);
            return population.AsReadOnly();
        }

        /// <summary>
        /// Appends every school of one species, school by school and member by
        /// member, so a school's members hold consecutive indices and therefore
        /// consecutive entity ids.
        /// </summary>
        private static void AddSchools(List<FaunaCreature> population, FaunaSpecies species,
            int tier, IslandId island, long firstEntityId)
        {
            int size = SchoolSizeFor(species, tier);
            for (int school = 0; school < SchoolsPerIsland; school++)
            {
                for (int member = 0; member < size; member++)
                {
                    int index = population.Count;
                    population.Add(new FaunaCreature(
                        firstEntityId + index, species, island, index, school, member));
                }
            }
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
