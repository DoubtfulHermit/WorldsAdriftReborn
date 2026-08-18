using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// Capacity is what makes islands DIFFER, and difference is easy to lose to
    /// a clamp, a rounding rule or a hash rewrite without any single change
    /// looking wrong. So the tests hold the distribution against the real
    /// catalogue: sizes genuinely spread the numbers, quiet islands genuinely
    /// exist (and are deliberate zeros, not accidents), no island can ever exceed
    /// the per-peer budget that whole-island admission depends on, and every
    /// decision replays identically on a restarted server.
    /// </summary>
    public sealed class IslandFaunaCapacityTests
    {
        // --- Size actually discriminates.

        [Fact]
        public void Island_size_spreads_the_capacities_within_one_tier()
        {
            List<int> totals = new List<int>();
            foreach (ReleaseIslandRecord island in Tier1())
            {
                if (IslandFaunaCapacity.QuietFactorFor(island.Definition.Id) != 1.0) continue;
                totals.Add(
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay, 1,
                        island.Envelope, island.Definition.Id)
                    + IslandFaunaCapacity.CapacityFor(FaunaSpecies.JellyFish, 1,
                        island.Envelope, island.Definition.Id));
            }

            Assert.True(totals.Count > 20, "too few ordinary tier-1 islands to say anything");
            Assert.True(totals.Max() > totals.Min(),
                "an 8.4x size spread produced identical populations - size is not driving");
            // The old world was EXACTLY 10 on every tier-1 island. At least a
            // third must now differ from the old constant, or nothing changed.
            Assert.True(totals.Count(total => total != 10) * 3 >= totals.Count);
        }

        [Fact]
        public void A_bigger_island_never_carries_less_than_a_smaller_one()
        {
            ReleaseIslandRecord[] ordinary = Tier1()
                .Where(island => IslandFaunaCapacity.QuietFactorFor(island.Definition.Id) == 1.0)
                .OrderBy(island => IslandFaunaCapacity.HalfDiagonalOf(island.Envelope))
                .ToArray();

            for (int i = 1; i < ordinary.Length; i++)
            {
                int smaller = IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay, 1,
                    ordinary[i - 1].Envelope, ordinary[i - 1].Definition.Id);
                int bigger = IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay, 1,
                    ordinary[i].Envelope, ordinary[i].Definition.Id);
                Assert.True(bigger >= smaller,
                    ordinary[i].Definition.Id + " is larger but carries fewer mantas");
            }
        }

        // --- Quiet islands: deliberate, stable, legible.

        [Fact]
        public void Quiet_factors_are_only_ever_the_three_documented_values()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                double factor = IslandFaunaCapacity.QuietFactorFor(island.Definition.Id);
                Assert.True(factor == 0.0 || factor == IslandFaunaCapacity.SparseFactor
                    || factor == 1.0, island.Definition.Id + " got factor " + factor);
            }
        }

        [Fact]
        public void The_world_contains_empty_sparse_and_ordinary_islands()
        {
            int empty = 0, sparse = 0, ordinary = 0;
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                double factor = IslandFaunaCapacity.QuietFactorFor(island.Definition.Id);
                if (factor == 0.0) empty++;
                else if (factor == IslandFaunaCapacity.SparseFactor) sparse++;
                else ordinary++;
            }

            Assert.True(empty > 0, "no island is empty - the quiet doctrine is dead code");
            Assert.True(sparse > 0, "no island is sparse");
            Assert.True(ordinary > empty + sparse,
                "most of the world must remain ordinary or 'quiet' stops meaning anything");
        }

        [Fact]
        public void An_empty_island_has_capacity_zero_for_every_species()
        {
            ReleaseIslandRecord? empty = ReleaseWorldCatalog.All.FirstOrDefault(island =>
                IslandFaunaCapacity.QuietFactorFor(island.Definition.Id) == 0.0);
            Assert.NotNull(empty);
            foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
            {
                Assert.Equal(0, IslandFaunaCapacity.CapacityFor(species,
                    empty!.Survey.Tier, empty.Envelope, empty.Definition.Id));
            }
        }

        [Fact]
        public void A_populated_species_never_rounds_below_two()
        {
            // One animal is a lost animal - the reading the school sizes were
            // chosen to avoid must survive the size scaling.
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
                {
                    int capacity = IslandFaunaCapacity.CapacityFor(species,
                        island.Survey.Tier, island.Envelope, island.Definition.Id);
                    Assert.True(capacity == 0 || capacity >= 2,
                        island.Definition.Id + " " + species + " capacity " + capacity);
                }
            }
        }

        // --- The per-peer invariant survives the scaling.

        [Fact]
        public void No_island_at_any_tier_can_exceed_the_per_peer_budget_after_the_clamp()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                int mantas = IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay,
                    island.Survey.Tier, island.Envelope, island.Definition.Id);
                int jellies = IslandFaunaCapacity.CapacityFor(FaunaSpecies.JellyFish,
                    island.Survey.Tier, island.Envelope, island.Definition.Id);
                (int clampedMantas, int clampedJellies) = IslandFaunaCapacity.ClampedToPeerBudget(
                    mantas, jellies, IslandFaunaInterestPolicy.DefaultPerPeerCreatures);

                Assert.True(clampedMantas + clampedJellies
                    <= IslandFaunaInterestPolicy.DefaultPerPeerCreatures,
                    island.Definition.Id + " exceeds the per-peer budget: whole-island"
                    + " admission would refuse it and the island would be INVISIBLE");
                if (mantas + jellies > 0)
                {
                    Assert.True(clampedMantas + clampedJellies > 0,
                        island.Definition.Id + " was clamped to nothing");
                }
            }
        }

        [Theory]
        [InlineData(10, 10, 24, 10, 10)]  // under budget: untouched
        [InlineData(0, 0, 24, 0, 0)]      // empty island stays empty
        [InlineData(16, 16, 24, 12, 12)]  // over: proportional, exact
        [InlineData(30, 0, 24, 24, 0)]    // single species clamps alone
        [InlineData(5, 5, 0, 0, 0)]       // zero budget means zero creatures
        public void The_budget_clamp_is_proportional_and_exact(
            int mantas, int jellies, int budget, int expectedMantas, int expectedJellies)
        {
            (int clampedMantas, int clampedJellies) =
                IslandFaunaCapacity.ClampedToPeerBudget(mantas, jellies, budget);
            Assert.Equal((expectedMantas, expectedJellies), (clampedMantas, clampedJellies));
        }

        // --- Groups.

        [Fact]
        public void Group_counts_are_zero_only_for_empty_capacity_and_never_exceed_three()
        {
            Assert.Equal(0, IslandFaunaCapacity.GroupCountFor(FaunaSpecies.MantaRay, 0));
            for (int capacity = 1; capacity <= 40; capacity++)
            {
                foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
                {
                    int groups = IslandFaunaCapacity.GroupCountFor(species, capacity);
                    Assert.InRange(groups, 1, 3);
                }
            }
            // And they GROW: the biggest capacities produce layered islands.
            Assert.True(IslandFaunaCapacity.GroupCountFor(FaunaSpecies.MantaRay, 12)
                > IslandFaunaCapacity.GroupCountFor(FaunaSpecies.MantaRay, 4));
        }

        // --- Determinism.

        [Fact]
        public void Every_capacity_decision_replays_identically()
        {
            foreach (ReleaseIslandRecord island in Tier1().Take(5))
            {
                Assert.Equal(
                    IslandFaunaCapacity.QuietFactorFor(island.Definition.Id),
                    IslandFaunaCapacity.QuietFactorFor(new IslandId(island.Definition.Id.Value)));
                Assert.Equal(
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay, 1,
                        island.Envelope, island.Definition.Id),
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay, 1,
                        island.Envelope, island.Definition.Id));
            }
        }

        private static IEnumerable<ReleaseIslandRecord> Tier1() =>
            ReleaseWorldCatalog.All.Where(record => record.Survey.Tier == 1);
    }
}
