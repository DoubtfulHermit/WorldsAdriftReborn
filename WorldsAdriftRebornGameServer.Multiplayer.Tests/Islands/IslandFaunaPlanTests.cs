using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHAT THIS LAYER IS FOR. <c>IslandFaunaPolicy.PopulationFor</c> answers a
    /// question about ONE island and is right to know nothing about any other. At
    /// release-world scale that leaves a gap somebody has to fill: the tier 1
    /// rollout is forty-six Wilderness islands wanting three creatures each, and
    /// the world-wide budget is twenty-four. This plan is the thing that decides
    /// who is left out, and these tests pin the two properties that decision has to
    /// have.
    ///
    /// IDS MUST NOT MOVE WHEN THE BUDGET MOVES. The operator's cap is a tuning
    /// knob. If lowering it re-packed the id blocks, a creature id would name a
    /// different animal after a restart - the exact class of corruption the
    /// disjoint fauna band exists to prevent.
    ///
    /// THE CAP MUST BE OBEYED WITHOUT HALF-FILLING AN ISLAND. A budget spread one
    /// creature per island puts a single lost manta on each, which is the reading
    /// <c>IslandFaunaPolicy</c>'s own counts were chosen to avoid.
    /// </summary>
    public sealed class IslandFaunaPlanTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
        }

        // --- Demand: the number an operator has to see before judging the cap.

        [Fact]
        public void Demand_is_the_sum_of_every_island_population()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier(1, 5);

            int expected = 0;
            foreach (ReleaseIslandRecord island in islands)
            {
                expected += IslandFaunaPolicy
                    .PopulationFor(island, IslandFaunaPolicy.FirstFaunaEntityId).Count;
            }

            Assert.Equal(expected, IslandFaunaPlan.Demand(islands));
        }

        [Fact]
        public void The_default_budget_now_populates_the_whole_release_world()
        {
            // THIS IS THE "MORE WILDLIFE" FIX, pinned. The world-wide cap used to be
            // 24 against a tier-1 demand of 138, so eight of forty-six Wilderness
            // islands carried anything and thirty-eight were empty. That cap existed
            // because it was the only bound on what ONE PEER could be sent; now that
            // IslandFaunaInterestPolicy bounds the wire per peer directly, the
            // world-wide number bounds only how much wildlife EXISTS, and every
            // island can carry its own.
            //
            // Asserted for the production rollout AND for the complete catalogue, so
            // an operator who turns the whole world on is not quietly handed empty
            // islands either.
            Assert.True(IslandFaunaPlan.Demand(ReleaseWorldRolloutPolicy.Select("tier1"))
                <= IslandFaunaPolicy.DefaultMaxConcurrent,
                "the production tier-1 world must fit inside the default budget");
            Assert.True(IslandFaunaPlan.Demand(ReleaseWorldRolloutPolicy.Select("all"))
                <= IslandFaunaPolicy.DefaultMaxConcurrent,
                "the complete release catalogue must fit inside the default budget");
        }

        [Fact]
        public void Every_tier_one_island_is_populated_under_the_default_budget()
        {
            IReadOnlyList<ReleaseIslandRecord> tierOne =
                ReleaseWorldRolloutPolicy.Select("tier1");
            IReadOnlyList<FaunaPlacement> plan =
                IslandFaunaPlan.Build(tierOne, IslandFaunaPolicy.DefaultMaxConcurrent);

            Assert.Equal(tierOne.Count, IslandFaunaPlan.IslandCount(plan));
            Assert.Equal(IslandFaunaPlan.Demand(tierOne), plan.Count);
        }

        [Fact]
        public void No_island_can_out_populate_what_one_peer_is_allowed_to_hold()
        {
            // The whole-island admission rule in IslandFaunaInterestPolicy skips a
            // population that does not fit the per-peer budget. If any island in the
            // catalogue were bigger than that budget, a player standing on it would be
            // shown NO fauna at all - the loudest possible version of the bug this
            // work exists to fix. This is the guard.
            foreach (ReleaseIslandRecord island in ReleaseWorldRolloutPolicy.Select("all"))
            {
                int population = IslandFaunaPolicy
                    .PopulationFor(island, IslandFaunaPolicy.FirstFaunaEntityId).Count;
                Assert.True(population <= IslandFaunaInterestPolicy.DefaultPerPeerCreatures,
                    island.Definition.Id + " carries " + population
                        + " creature(s), more than one peer may hold ("
                        + IslandFaunaInterestPolicy.DefaultPerPeerCreatures + ")");
            }
        }

        [Fact]
        public void An_empty_world_wants_and_plans_nothing()
        {
            Assert.Equal(0, IslandFaunaPlan.Demand(Array.Empty<ReleaseIslandRecord>()));
            Assert.Empty(IslandFaunaPlan.Build(
                Array.Empty<ReleaseIslandRecord>(), IslandFaunaPolicy.DefaultMaxConcurrent));
        }

        [Fact]
        public void A_null_world_is_a_programming_error_not_an_empty_one()
        {
            Assert.Throws<ArgumentNullException>(() => IslandFaunaPlan.Demand(null!));
            Assert.Throws<ArgumentNullException>(() => IslandFaunaPlan.Build(null!, 24));
            Assert.Throws<ArgumentNullException>(() => IslandFaunaPlan.IslandCount(null!));
        }

        // --- The budget is a hard cap, and zero is a kill switch.

        [Fact]
        public void The_plan_never_exceeds_the_budget()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier(1, 20);
            for (int budget = 0; budget <= 24; budget++)
            {
                Assert.True(IslandFaunaPlan.Build(islands, budget).Count <= budget);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void A_zero_or_negative_budget_seeds_nothing_and_does_not_throw(int budget) =>
            Assert.Empty(IslandFaunaPlan.Build(Tier(1, 10), budget));

        [Fact]
        public void An_island_is_populated_whole_or_not_at_all()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier(1, 20);
            IReadOnlyList<FaunaPlacement> plan = IslandFaunaPlan.Build(islands, 8);

            // Tier 1 is three creatures an island, so a budget of eight fits two
            // complete islands and must not spend the two spare on a third.
            Dictionary<IslandId, int> perIsland = new Dictionary<IslandId, int>();
            foreach (FaunaPlacement placement in plan)
            {
                perIsland.TryGetValue(placement.Creature.IslandId, out int count);
                perIsland[placement.Creature.IslandId] = count + 1;
            }

            foreach (ReleaseIslandRecord island in islands)
            {
                if (!perIsland.TryGetValue(island.Definition.Id, out int seeded)) continue;
                Assert.Equal(
                    IslandFaunaPolicy.PopulationFor(
                        island, IslandFaunaPolicy.FirstFaunaEntityId).Count,
                    seeded);
            }
        }

        [Fact]
        public void A_budget_that_covers_the_whole_world_leaves_nobody_out()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier(1, 6);
            int demand = IslandFaunaPlan.Demand(islands);

            IReadOnlyList<FaunaPlacement> plan = IslandFaunaPlan.Build(islands, demand);

            Assert.Equal(demand, plan.Count);
            Assert.Equal(islands.Count, IslandFaunaPlan.IslandCount(plan));
        }

        // --- The property that makes the cap safe to tune.

        [Fact]
        public void Lowering_the_budget_never_moves_an_id_onto_a_different_creature()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier(1, 20);

            // Both budgets must admit SOMETHING, so they are expressed in whole
            // island populations rather than in raw creatures: one tier-1 island now
            // costs ten, and a budget of nine would admit nobody and prove nothing.
            int islandCost = IslandFaunaPolicy
                .PopulationFor(islands[0], IslandFaunaPolicy.FirstFaunaEntityId).Count;
            Dictionary<long, FaunaCreature> generous =
                ById(IslandFaunaPlan.Build(islands, islandCost * 12));
            Dictionary<long, FaunaCreature> mean =
                ById(IslandFaunaPlan.Build(islands, islandCost * 3));

            Assert.NotEmpty(mean);
            foreach ((long entityId, FaunaCreature creature) in mean)
            {
                Assert.True(generous.TryGetValue(entityId, out FaunaCreature same),
                    "entity " + entityId + " exists only under the smaller budget");
                Assert.Equal(same, creature);
            }
        }

        [Fact]
        public void The_same_world_plans_identically_every_time()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier(1, 12);

            IReadOnlyList<FaunaPlacement> first = IslandFaunaPlan.Build(islands, 24);
            IReadOnlyList<FaunaPlacement> second = IslandFaunaPlan.Build(islands, 24);

            Assert.NotEmpty(first);
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i], second[i]);
            }
        }

        [Fact]
        public void Every_planned_id_is_distinct_and_inside_the_fauna_band()
        {
            IReadOnlyList<FaunaPlacement> plan = IslandFaunaPlan.Build(Tier(1, 20), 24);
            HashSet<long> seen = new HashSet<long>();

            Assert.NotEmpty(plan);
            foreach (FaunaPlacement placement in plan)
            {
                Assert.True(placement.Creature.EntityId >= IslandFaunaPolicy.FirstFaunaEntityId,
                    "a planned creature escaped the fauna id band");
                Assert.True(seen.Add(placement.Creature.EntityId),
                    "entity " + placement.Creature.EntityId + " was planned twice");
            }
        }

        [Fact]
        public void Every_placement_carries_its_own_island_and_envelope()
        {
            foreach (FaunaPlacement placement in IslandFaunaPlan.Build(Tier(1, 20), 24))
            {
                // The registry stores these verbatim and the movement maths reads
                // them; a placement wired to the wrong island would fly a manta
                // around somebody else's rock.
                Assert.Equal(placement.Creature.IslandId, placement.Island.Id);
                Assert.Equal(placement.Creature.IslandId, placement.Envelope.IslandId);
            }
        }

        [Fact]
        public void A_planned_world_fits_inside_the_registry_that_will_hold_it()
        {
            // The plan and the registry share one budget; if they ever disagreed the
            // registry would silently refuse the tail of the plan.
            IslandFaunaRegistry registry = new IslandFaunaRegistry(
                new FakeClock(), IslandFaunaMovement.WorldTransformAt);

            foreach (FaunaPlacement placement in
                IslandFaunaPlan.Build(Tier(1, 20), registry.MaxConcurrent))
            {
                Assert.True(registry.Add(placement.Creature, placement.Island, placement.Envelope),
                    "the registry refused a creature the plan had already budgeted for");
            }
        }

        private static Dictionary<long, FaunaCreature> ById(IReadOnlyList<FaunaPlacement> plan)
        {
            Dictionary<long, FaunaCreature> map = new Dictionary<long, FaunaCreature>();
            foreach (FaunaPlacement placement in plan)
            {
                map[placement.Creature.EntityId] = placement.Creature;
            }
            return map;
        }

        /// <summary>
        /// Real catalogue islands of one tier. Real records rather than fixtures,
        /// for the reason <see cref="IslandFaunaPolicyTests"/> gives: the population
        /// reads the surveyed tier and the envelope, and a drifting fixture would
        /// test arithmetic nobody ships.
        /// </summary>
        private static IReadOnlyList<ReleaseIslandRecord> Tier(int tier, int count) =>
            ReleaseWorldCatalog.All
                .Where(record => record.Survey.Tier == tier)
                .OrderBy(record => record.Definition.Id)
                .Take(count)
                .ToArray();
    }
}
