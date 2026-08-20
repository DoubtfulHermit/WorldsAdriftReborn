using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE FOUR LEDGERS, RESET ONE ISLAND AT A TIME (S2, §14.10).
    ///
    /// An understorm strikes ONE island's underside. S1 could only reset the whole
    /// world, so it had to defer its single reset to the LAST island's storm end and
    /// therefore landed ~3 m 32 s late on production (MEASURED 2026-08-20, 47
    /// islands, 900 s cadence). The fix is a scoped overload on each of the four
    /// ledgers the reset drives, and these tests exist for the two ways a scoped
    /// reset fails silently:
    ///
    ///   1. it ignores the predicate and resets everything anyway - the old defect
    ///      wearing a new signature, and every storm test still green;
    ///   2. it honours the predicate but restores nothing, because the predicate
    ///      never matches - a dead feature, and every storm test still green.
    ///
    /// So each ledger is asserted on BOTH sides: the in-scope resource comes back,
    /// and the out-of-scope one is left exactly as the player left it.
    /// </summary>
    public class IslandScopedResetTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
            public void Advance(TimeSpan by) => Elapsed += by;
        }

        // "haven" owns the low ids, "b3-01" owns the high ones. The map is what the
        // game server derives from ResourceInterestService._resourceIslands.
        private static bool OnHaven(long entityId) => entityId < 100;

        // ------------------------------------------------------------------
        // Trees
        // ------------------------------------------------------------------

        private static readonly TimeSpan CutInterval = TimeSpan.FromSeconds(0.75);
        private static readonly TimeSpan RespawnDelay = TimeSpan.FromMinutes(2);

        private static void ChopOnce(TreeHarvest harvest, FakeClock clock, long tree)
        {
            TreeTopology topology = Trees.Topology();
            int mask = harvest.MaskOf(tree)!.Value;
            int outermost = -1;
            for (int s = topology.SectionCount - 1; s >= 0; s--)
            {
                if (TreeTopology.IsInMask(s, mask)) { outermost = s; break; }
            }

            harvest.OnCutSignal(7, new TreeCutSignal(tree, outermost, false));
            clock.Advance(CutInterval);
            harvest.Due();
        }

        [Fact]
        public void A_storm_on_one_island_regrows_only_that_islands_trees()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, CutInterval, RespawnDelay);
            harvest.Plant(10, Trees.Topology(), Trees.WoodType);    // haven
            harvest.Plant(200, Trees.Topology(), Trees.WoodType);   // b3-01

            ChopOnce(harvest, clock, 10);
            ChopOnce(harvest, clock, 200);

            int choppedElsewhere = harvest.MaskOf(200)!.Value;

            IReadOnlyList<TreeRespawn> reset = harvest.ResetAll(OnHaven);

            Assert.Single(reset);
            Assert.Equal(10, reset[0].TreeEntityId);
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(10));

            // ⚠ THE HALF THAT CATCHES THE OLD DEFECT. A calm island's forest must be
            // exactly as the player left it - a storm over Haven does not regrow
            // trees the player is halfway through chopping on B3-01.
            Assert.Equal(choppedElsewhere, harvest.MaskOf(200));
            Assert.NotEqual(Trees.FullSectionMask, harvest.MaskOf(200));
        }

        [Fact]
        public void A_null_scope_is_still_the_whole_world_for_the_operator()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, CutInterval, RespawnDelay);
            harvest.Plant(10, Trees.Topology(), Trees.WoodType);
            harvest.Plant(200, Trees.Topology(), Trees.WoodType);
            ChopOnce(harvest, clock, 10);
            ChopOnce(harvest, clock, 200);

            Assert.Equal(2, harvest.ResetAll(null).Count);
            Assert.Equal(2, new[] { 10L, 200L }.Count(t => harvest.MaskOf(t) == Trees.FullSectionMask));
        }

        [Fact]
        public void The_no_argument_overload_and_a_null_scope_are_the_same_thing()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, CutInterval, RespawnDelay);
            harvest.Plant(10, Trees.Topology(), Trees.WoodType);
            harvest.Plant(200, Trees.Topology(), Trees.WoodType);
            ChopOnce(harvest, clock, 10);
            ChopOnce(harvest, clock, 200);

            Assert.Equal(2, harvest.ResetAll().Count);
        }

        [Fact]
        public void A_scoped_reset_still_refuses_to_regrow_a_felled_log()
        {
            // The S1 rule survives the S2 scoping: a log is a piece of a tree, not a
            // damaged tree, and an understorm that "restored" it would sprout a whole
            // birch out of a trunk on the ground.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, CutInterval, RespawnDelay);
            harvest.Plant(10, Trees.Topology(), Trees.WoodType);

            const long Log = 90;                              // also on haven
            int partialLog = Trees.FullSectionMask & ~1;
            Assert.True(harvest.PlantFelled(Log, Trees.Topology(), Trees.WoodType, partialLog));

            ChopOnce(harvest, clock, 10);

            IReadOnlyList<TreeRespawn> reset = harvest.ResetAll(OnHaven);

            Assert.Single(reset);
            Assert.Equal(10, reset[0].TreeEntityId);
            Assert.Equal(partialLog, harvest.MaskOf(Log));
        }

        // ------------------------------------------------------------------
        // Metal nodes (NodeRegistry - the crust/destroyed half)
        // ------------------------------------------------------------------

        private static MetalNode Node(string key) =>
            new MetalNode(key, "iron", 6, new FixedPointPosition(70534881, -1286551, -4612781));

        [Fact]
        public void A_storm_on_one_island_stands_up_only_that_islands_nodes()
        {
            NodeRegistry registry = new NodeRegistry();
            registry.Register(11, Node("metal-haven"));
            registry.Register(201, Node("metal-b3"));
            registry.MarkDestroyed(11);
            registry.MarkDestroyed(201);

            Assert.Equal(1, registry.ResetAll(OnHaven));

            Assert.False(registry.IsDestroyed(11));
            Assert.True(registry.IsDestroyed(201),
                "a node on a calm island must stay mined - the whole point of S2");
        }

        [Fact]
        public void An_unscoped_node_reset_still_stands_up_the_world()
        {
            NodeRegistry registry = new NodeRegistry();
            registry.Register(11, Node("metal-haven"));
            registry.Register(201, Node("metal-b3"));
            registry.MarkDestroyed(11);
            registry.MarkDestroyed(201);

            Assert.Equal(2, registry.ResetAll());
            Assert.False(registry.IsDestroyed(11));
            Assert.False(registry.IsDestroyed(201));
        }

        // ------------------------------------------------------------------
        // Metal deposits (MetalHarvest - the hit-count half)
        // ------------------------------------------------------------------

        [Fact]
        public void A_storm_on_one_island_refills_only_that_islands_deposits()
        {
            MetalHarvest harvest = new MetalHarvest();
            harvest.Place(11, unitsYield: 5);
            harvest.Place(201, unitsYield: 5);
            harvest.Hit(11);
            harvest.Hit(201);

            Assert.Equal(1, harvest.ResetAll(OnHaven));

            Assert.Equal(0, harvest.HitsOn(11));
            Assert.Equal(1, harvest.HitsOn(201));
        }

        // ------------------------------------------------------------------
        // Fuel canisters
        // ------------------------------------------------------------------

        [Fact]
        public void A_storm_on_one_island_refills_only_that_islands_canisters()
        {
            FuelCanisterRegistry canisters = new FuelCanisterRegistry();
            canisters.Register(12);
            canisters.Register(202);
            canisters.Hit(12);
            canisters.Hit(202);

            Assert.Equal(1, canisters.ResetAll(OnHaven));

            Assert.Equal(0, canisters.ShotsOn(12));
            Assert.Equal(1, canisters.ShotsOn(202));
        }

        // ------------------------------------------------------------------
        // The predicate is genuinely consulted
        // ------------------------------------------------------------------

        [Fact]
        public void Every_ledger_actually_asks_the_predicate_about_every_resource()
        {
            // MUTATION GUARD. An overload that accepts a predicate and then ignores
            // it compiles, passes every storm test, and is the S1 defect verbatim.
            // Here the predicate records who it was asked about and refuses everyone,
            // so a ledger that reset anything at all has ignored it.
            List<long> asked = new List<long>();
            bool Never(long id) { asked.Add(id); return false; }

            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, CutInterval, RespawnDelay);
            harvest.Plant(10, Trees.Topology(), Trees.WoodType);
            ChopOnce(harvest, clock, 10);
            int chopped = harvest.MaskOf(10)!.Value;
            Assert.Empty(harvest.ResetAll(Never));
            Assert.Equal(chopped, harvest.MaskOf(10));

            NodeRegistry nodes = new NodeRegistry();
            nodes.Register(11, Node("metal-haven"));
            nodes.MarkDestroyed(11);
            Assert.Equal(0, nodes.ResetAll(Never));
            Assert.True(nodes.IsDestroyed(11));

            MetalHarvest deposits = new MetalHarvest();
            deposits.Place(11, unitsYield: 5);
            deposits.Hit(11);
            Assert.Equal(0, deposits.ResetAll(Never));
            Assert.Equal(1, deposits.HitsOn(11));

            FuelCanisterRegistry canisters = new FuelCanisterRegistry();
            canisters.Register(12);
            canisters.Hit(12);
            Assert.Equal(0, canisters.ResetAll(Never));
            Assert.Equal(1, canisters.ShotsOn(12));

            Assert.Equal(new[] { 10L, 11L, 11L, 12L }, asked.OrderBy(a => a).ToArray());
        }

        // ------------------------------------------------------------------
        // The scope decision itself (IslandResourceScope)
        // ------------------------------------------------------------------

        private static readonly IslandId Haven = new IslandId("haven");
        private static readonly IslandId B301 = new IslandId("b3-01");

        private static IslandId? OwnerOf(long entityId) =>
            entityId < 100 ? Haven : entityId < 1000 ? B301 : (IslandId?)null;

        [Fact]
        public void No_island_means_the_whole_world_which_is_reset_resources_all()
        {
            Assert.Null(IslandResourceScope.Include(null, OwnerOf));
        }

        [Fact]
        public void Mutation_an_islands_scope_admits_only_that_islands_resources()
        {
            // ⚠ THIS IS THE GUARD THE FIRST ATTEMPT DID NOT HAVE.
            // Writing this decision inline in the game server left it covered only by
            // a source-reading test for `ResetAll(include)` at the call sites, and
            // replacing the declaration with `= null` reinstated the S1 world-wide
            // reset with all 4215 tests green. The decision lives in this assembly
            // now, so nulling it out is a test failure rather than a silent
            // regression to a defect a player already felt.
            Func<long, bool>? include = IslandResourceScope.Include(Haven, OwnerOf);

            Assert.NotNull(include);
            Assert.True(include!(10), "a resource on the storming island must be in scope");
            Assert.False(include(200), "a resource on a calm island must NOT be in scope");
        }

        [Fact]
        public void An_unclassifiable_resource_is_left_alone_rather_than_swept_up()
        {
            // Wrong in the direction a player can see and report (a node stays mined
            // one more cadence) rather than wrong in the direction nobody notices (a
            // storm over Haven restoring a node on the far side of the world).
            Func<long, bool>? include = IslandResourceScope.Include(Haven, OwnerOf);
            Assert.False(include!(5000));
        }

        [Fact]
        public void A_scope_for_an_island_nothing_belongs_to_admits_nothing()
        {
            Func<long, bool>? include =
                IslandResourceScope.Include(new IslandId("b9-99"), OwnerOf);

            foreach (long id in new long[] { 10, 200, 5000 })
                Assert.False(include!(id));
        }

        [Fact]
        public void A_scope_without_an_owner_lookup_is_refused_rather_than_matching_nothing()
        {
            // A null lookup that quietly produced a never-matching predicate is a
            // storm that resets nothing, forever, with no log line and no red test.
            Assert.Throws<ArgumentNullException>(
                () => IslandResourceScope.Include(Haven, null!));
        }
    }
}
