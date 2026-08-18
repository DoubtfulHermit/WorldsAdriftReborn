using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The ecology's population plan carries the same two load-bearing rules the
    /// flat plan does - id blocks allocated before any reduction, islands seeded
    /// whole or not at all - plus the new ones: quiet islands leave their whole
    /// reserved block unused, no island can exceed the per-peer budget (a
    /// too-big island would be admitted as NOTHING by whole-island interest),
    /// and a school's members still hold consecutive ids so they arrive
    /// together.
    /// </summary>
    public sealed class IslandFaunaEcologyPlanTests
    {
        private const int Budget = 4000;
        private const int PeerBudget = 24;

        private static IReadOnlyList<ReleaseIslandRecord> Tier1() =>
            ReleaseWorldCatalog.All.Where(r => r.Survey.Tier == 1)
                .OrderBy(r => r.Definition.Id).ToList();

        [Fact]
        public void The_plan_is_deterministic()
        {
            IReadOnlyList<FaunaPlacement> first =
                IslandFaunaPlan.BuildEcology(Tier1(), Budget, PeerBudget);
            IReadOnlyList<FaunaPlacement> second =
                IslandFaunaPlan.BuildEcology(Tier1(), Budget, PeerBudget);

            Assert.NotEmpty(first);
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Creature, second[i].Creature);
            }
        }

        [Fact]
        public void Every_creature_sits_inside_its_own_islands_reserved_block()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier1();
            IReadOnlyList<FaunaPlacement> plan =
                IslandFaunaPlan.BuildEcology(islands, Budget, PeerBudget);

            // Recompute the block walk independently and hold every id to it.
            Dictionary<IslandId, (long Start, long End)> blocks = new();
            long next = IslandFaunaPolicy.FirstFaunaEntityId;
            foreach (ReleaseIslandRecord island in islands)
            {
                long start = next;
                next += IslandFaunaCapacity.IdBlockFor(
                        FaunaSpecies.MantaRay, island.Survey.Tier, island.Envelope)
                    + IslandFaunaCapacity.IdBlockFor(
                        FaunaSpecies.JellyFish, island.Survey.Tier, island.Envelope);
                blocks[island.Definition.Id] = (start, next);
            }

            foreach (FaunaPlacement placement in plan)
            {
                (long start, long end) = blocks[placement.Creature.IslandId];
                Assert.InRange(placement.Creature.EntityId, start, end - 1);
            }
            Assert.True(next - IslandFaunaPolicy.FirstFaunaEntityId < 100_000_000L,
                "the whole ecology world must stay inside the fauna band");
        }

        [Fact]
        public void The_id_block_is_never_smaller_than_what_can_go_live()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
                {
                    int block = IslandFaunaCapacity.IdBlockFor(
                        species, island.Survey.Tier, island.Envelope);
                    int live = IslandFaunaCapacity.CapacityFor(
                        species, island.Survey.Tier, island.Envelope, island.Definition.Id);
                    Assert.True(live <= block,
                        island.Definition.Id + " " + species + ": live " + live
                        + " would spill out of its reserved block of " + block);
                }
            }
        }

        [Fact]
        public void Quiet_islands_are_absent_from_the_plan_but_present_in_the_id_walk()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier1();
            ReleaseIslandRecord? quiet = islands.FirstOrDefault(island =>
                IslandFaunaCapacity.QuietFactorFor(island.Definition.Id) == 0.0);
            Assert.NotNull(quiet);

            IReadOnlyList<FaunaPlacement> plan =
                IslandFaunaPlan.BuildEcology(islands, Budget, PeerBudget);
            Assert.DoesNotContain(plan,
                p => p.Creature.IslandId == quiet!.Definition.Id);
            // Its block is still reserved: proven by the previous test's
            // independent walk, which counts every island quiet or not.
        }

        [Fact]
        public void No_island_ever_exceeds_the_per_peer_budget()
        {
            IReadOnlyList<FaunaPlacement> plan =
                IslandFaunaPlan.BuildEcology(Tier1(), Budget, PeerBudget);
            foreach (IGrouping<IslandId, FaunaPlacement> island in
                plan.GroupBy(p => p.Creature.IslandId))
            {
                Assert.True(island.Count() <= PeerBudget,
                    island.Key + " carries " + island.Count()
                    + " creatures - whole-island admission would refuse it");
            }
        }

        [Fact]
        public void Schools_hold_contiguous_ids_and_complete_member_runs()
        {
            IReadOnlyList<FaunaPlacement> plan =
                IslandFaunaPlan.BuildEcology(Tier1(), Budget, PeerBudget);

            foreach (var school in plan.GroupBy(p =>
                (p.Creature.IslandId, p.Creature.Species, p.Creature.SchoolIndex)))
            {
                FaunaCreature[] members = school
                    .Select(p => p.Creature).OrderBy(c => c.MemberIndex).ToArray();
                for (int i = 0; i < members.Length; i++)
                {
                    Assert.Equal(i, members[i].MemberIndex);
                    if (i > 0)
                    {
                        Assert.Equal(members[i - 1].EntityId + 1, members[i].EntityId);
                    }
                }
            }
        }

        [Fact]
        public void The_world_actually_gets_layers_multiple_groups_exist_somewhere()
        {
            IReadOnlyList<FaunaPlacement> plan =
                IslandFaunaPlan.BuildEcology(Tier1(), Budget, PeerBudget);
            bool layered = plan.Any(p => p.Creature.SchoolIndex > 0);
            Assert.True(layered,
                "no island produced more than one group - the layering never happens");
        }

        [Fact]
        public void Demand_matches_what_an_unbounded_budget_seeds()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = Tier1();
            Assert.Equal(
                IslandFaunaPlan.EcologyDemand(islands, PeerBudget),
                IslandFaunaPlan.BuildEcology(islands, int.MaxValue, PeerBudget).Count);
        }

        [Fact]
        public void Population_sizes_now_differ_between_islands()
        {
            // The point of the whole layer: the old world put exactly 10 on
            // every tier-1 island.
            int[] totals = IslandFaunaPlan.BuildEcology(Tier1(), Budget, PeerBudget)
                .GroupBy(p => p.Creature.IslandId)
                .Select(g => g.Count()).ToArray();
            Assert.True(totals.Distinct().Count() > 3,
                "an 8.4x size spread produced near-identical populations");
        }
    }
}
