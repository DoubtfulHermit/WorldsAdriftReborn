using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHY THIS LAYER IS TESTED AT ALL. Fauna is the first population this server
    /// seeds that nobody asked for by name: a player does not craft a manta ray, it
    /// is simply THERE when the island checks out. Two properties therefore decide
    /// whether the feature is safe to ship, and neither is visible by inspection.
    ///
    /// DETERMINISM. Every peer that checks the same island out must be told about
    /// the same creatures with the same entity ids, and a server restarted against
    /// the same world must re-seed identically - otherwise a reconnecting player is
    /// sent a manta whose id already meant something else, and the client's entity
    /// table is corrupt in a way that looks like a protocol bug. The population
    /// function is pure precisely so this can be asserted rather than hoped for.
    ///
    /// BOUNDEDNESS AND BAND SEPARATION. Fauna ids are drawn from a band above
    /// TreeFall's log band. If a tier-4 island could scale its population far
    /// enough to walk into that band, a fauna transform and a falling log would
    /// name the same entity on the wire. The gap is asserted here, not assumed.
    ///
    /// And because it is a new relayed sender, it is OFF unless an operator says
    /// otherwise - so the opt-in gate is tested with the same junk-token matrix
    /// that <see cref="IslandTerrainInterestPolicy"/> uses.
    /// </summary>
    public sealed class IslandFaunaPolicyTests
    {
        // --- The opt-in gate: a new relayed sender is off until an operator says so.

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("0", false)]
        [InlineData("false", false)]
        [InlineData("off", false)]
        [InlineData("banana", false)]
        [InlineData("1", true)]
        [InlineData("true", true)]
        [InlineData("TRUE", true)]
        [InlineData("Yes", true)]
        [InlineData("YES", true)]
        public void Feature_is_strictly_opt_in(string? value, bool expected) =>
            Assert.Equal(expected, IslandFaunaPolicy.EnabledFrom(value));

        [Fact]
        public void Opt_in_gate_is_named_so_operators_can_find_it()
        {
            Assert.Equal("WAREBORN_ISLAND_FAUNA", IslandFaunaPolicy.EnabledEnvVar);
            // The default must be OFF: an unset variable is the common case.
            Assert.False(IslandFaunaPolicy.EnabledFrom(
                Environment.GetEnvironmentVariable("WAREBORN_ISLAND_FAUNA_DEFINITELY_UNSET")));
        }

        // --- Budget parsing: a typo in an env var must never stop a server booting.

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("banana")]
        [InlineData("-1")]
        [InlineData("-42")]
        public void Absent_or_nonsense_budget_accepts_the_default(string? raw) =>
            Assert.Null(IslandFaunaPolicy.ParseBudget(raw));

        [Theory]
        [InlineData("0", 0)]
        [InlineData("1", 1)]
        [InlineData(" 12 ", 12)]
        [InlineData("64", 64)]
        public void A_stated_budget_is_taken_literally(string raw, int expected) =>
            Assert.Equal(expected, IslandFaunaPolicy.ParseBudget(raw));

        [Fact]
        public void Zero_is_a_valid_budget_and_means_no_fauna()
        {
            // Zero is the second kill switch: an operator who does not want to touch
            // the flag can starve the feature instead. It must NOT fall back.
            Assert.Equal(0, IslandFaunaPolicy.ParseBudget("0"));
            Assert.True(IslandFaunaPolicy.DefaultMaxConcurrent > 0);
        }

        // --- Prefab names: an unresolvable name is an invisible entity on the wire.

        [Theory]
        [InlineData(FaunaSpecies.JellyFish, "JellyFish")]
        [InlineData(FaunaSpecies.MantaRay, "MantaRay")]
        public void Species_map_to_their_retail_prefab_names(FaunaSpecies species, string expected) =>
            Assert.Equal(expected, IslandFaunaPolicy.PrefabNameFor(species));

        [Fact]
        public void Every_species_prefab_is_one_the_unmodified_client_can_resolve()
        {
            foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
            {
                Assert.True(ClientEntityPrefabs.CanResolve(IslandFaunaPolicy.PrefabNameFor(species)),
                    species + " names a prefab the client cannot load");
            }
        }

        // --- Determinism: the same island seeds identically on every process start.

        [Fact]
        public void The_same_island_populates_identically_every_time()
        {
            ReleaseIslandRecord island = AnyIsland(tier: 2);

            IReadOnlyList<FaunaCreature> first =
                IslandFaunaPolicy.PopulationFor(island, IslandFaunaPolicy.FirstFaunaEntityId);
            IReadOnlyList<FaunaCreature> second =
                IslandFaunaPolicy.PopulationFor(island, IslandFaunaPolicy.FirstFaunaEntityId);

            Assert.NotEmpty(first);
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i], second[i]);
            }
        }

        [Fact]
        public void Population_is_ordered_contiguous_and_distinct()
        {
            ReleaseIslandRecord island = AnyIsland(tier: 3);
            long start = IslandFaunaPolicy.FirstFaunaEntityId;

            IReadOnlyList<FaunaCreature> creatures = IslandFaunaPolicy.PopulationFor(island, start);

            Assert.NotEmpty(creatures);
            Assert.Equal(creatures.Count, creatures.Select(x => x.EntityId).Distinct().Count());
            for (int i = 0; i < creatures.Count; i++)
            {
                Assert.Equal(start + i, creatures[i].EntityId);
                Assert.Equal(i, creatures[i].Index);
                Assert.Equal(island.Definition.Id, creatures[i].IslandId);
            }
        }

        [Fact]
        public void A_population_can_be_seeded_from_any_first_id_without_changing_shape()
        {
            ReleaseIslandRecord island = AnyIsland(tier: 2);
            long offset = IslandFaunaPolicy.FirstFaunaEntityId + 5000;

            IReadOnlyList<FaunaCreature> baseline =
                IslandFaunaPolicy.PopulationFor(island, IslandFaunaPolicy.FirstFaunaEntityId);
            IReadOnlyList<FaunaCreature> shifted = IslandFaunaPolicy.PopulationFor(island, offset);

            Assert.Equal(baseline.Count, shifted.Count);
            for (int i = 0; i < baseline.Count; i++)
            {
                Assert.Equal(baseline[i].Species, shifted[i].Species);
                Assert.Equal(baseline[i].Index, shifted[i].Index);
                Assert.Equal(offset + i, shifted[i].EntityId);
            }
        }

        // --- Entity-id band: fauna must never be able to name a falling log.

        [Fact]
        public void Fauna_band_starts_far_above_the_falling_log_band()
        {
            Assert.Equal(2_100_000_000L, IslandFaunaPolicy.FirstFaunaEntityId);
            Assert.Equal(2_000_000_000L, TreeFall.FirstLogEntityId);
            Assert.True(IslandFaunaPolicy.FirstFaunaEntityId - TreeFall.FirstLogEntityId >= 100_000_000L,
                "the fauna and log bands must keep a hundred-million-id headroom");
        }

        [Fact]
        public void No_seeded_creature_can_reach_into_the_log_band()
        {
            long next = IslandFaunaPolicy.FirstFaunaEntityId;
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                IReadOnlyList<FaunaCreature> creatures = IslandFaunaPolicy.PopulationFor(island, next);
                foreach (FaunaCreature creature in creatures)
                {
                    Assert.True(creature.EntityId >= IslandFaunaPolicy.FirstFaunaEntityId);
                    Assert.True(creature.EntityId > TreeFall.FirstLogEntityId);
                }
                next += creatures.Count;
            }

            // Even the WHOLE release world seeded end to end stays inside the band.
            Assert.True(next - IslandFaunaPolicy.FirstFaunaEntityId < 100_000_000L);
        }

        // --- Tier scaling: tier 1 Wilderness calmest, tier 4 Badlands worst.

        [Fact]
        public void Population_never_shrinks_as_the_tier_worsens()
        {
            int previous = -1;
            for (int tier = 1; tier <= 4; tier++)
            {
                int count = IslandFaunaPolicy
                    .PopulationFor(AnyIsland(tier), IslandFaunaPolicy.FirstFaunaEntityId).Count;
                Assert.True(count >= previous,
                    "tier " + tier + " seeded fewer creatures than the calmer tier below it");
                previous = count;
            }
        }

        [Fact]
        public void Badlands_are_strictly_worse_than_wilderness()
        {
            int tier1 = IslandFaunaPolicy
                .PopulationFor(AnyIsland(1), IslandFaunaPolicy.FirstFaunaEntityId).Count;
            int tier4 = IslandFaunaPolicy
                .PopulationFor(AnyIsland(4), IslandFaunaPolicy.FirstFaunaEntityId).Count;

            Assert.True(tier4 > tier1,
                "a tier-4 island must carry more fauna than a tier-1 island");
        }

        [Fact]
        public void Both_species_are_represented_somewhere_in_the_release_world()
        {
            HashSet<FaunaSpecies> seen = new HashSet<FaunaSpecies>();
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                foreach (FaunaCreature creature in IslandFaunaPolicy.PopulationFor(
                    island, IslandFaunaPolicy.FirstFaunaEntityId))
                {
                    seen.Add(creature.Species);
                }
            }

            Assert.Contains(FaunaSpecies.MantaRay, seen);
            Assert.Contains(FaunaSpecies.JellyFish, seen);
        }

        /// <summary>
        /// A real release-world island of the requested tier. Real records rather
        /// than hand-built ones, because the population function reads the surveyed
        /// tier and the envelope, and a fixture that drifted from the catalogue
        /// would test arithmetic nobody ships.
        /// </summary>
        private static ReleaseIslandRecord AnyIsland(int tier) =>
            ReleaseWorldCatalog.All
                .Where(record => record.Survey.Tier == tier)
                .OrderBy(record => record.Definition.Id)
                .First();
    }
}
