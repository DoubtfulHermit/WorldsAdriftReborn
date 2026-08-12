using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The other half of the tree loop the wire does not provide: regrowth.
    ///
    /// P1-9 was "chopped trees never respawn" - the island deforested for the life
    /// of the process. The shipped <c>respawnTime</c> field is inert (the client
    /// never reads it), so regrowth here is server-authored: a chopped tree's
    /// <c>sectionMask</c> is reset to whole after a delay, and the client stands the
    /// sections back up off that one mask.
    ///
    /// Time is injected, so the delay is asserted on in SECONDS without any test
    /// sleeping - the same discipline the cut cadence uses, and for the same reason
    /// (the main loop turns once per ENet event, not once per second).
    /// </summary>
    public class TreeRespawnTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }

            public void Advance(TimeSpan by) => Elapsed += by;
        }

        private const long TreeEntity = 500;
        private const long OtherTree = 501;
        private const long Cutter = 7;

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.75);
        private static readonly TimeSpan Respawn = TimeSpan.FromMinutes(2);

        private static TreeHarvest Planted(FakeClock clock, params long[] trees)
        {
            TreeHarvest harvest = new TreeHarvest(clock, Interval, Respawn);
            foreach (long tree in trees)
            {
                harvest.Plant(tree, Trees.Topology(), Trees.WoodType);
            }
            return harvest;
        }

        private static TreeCutSignal At(long tree, int section) => new TreeCutSignal(tree, section, false);

        /// <summary>Holds a beam on the tree's outermost standing section and takes one chunk.</summary>
        private static void ChopOnce(TreeHarvest harvest, FakeClock clock, long tree)
        {
            TreeTopology topology = Trees.Topology();
            int mask = harvest.MaskOf(tree)!.Value;
            int outermost = -1;
            for (int s = topology.SectionCount - 1; s >= 0; s--)
            {
                if (TreeTopology.IsInMask(s, mask))
                {
                    outermost = s;
                    break;
                }
            }

            harvest.OnCutSignal(Cutter, At(tree, outermost));
            clock.Advance(Interval);
            harvest.Due();
        }

        // ------------------------------------------------------------------
        // A whole tree has nothing to regrow
        // ------------------------------------------------------------------

        [Fact]
        public void A_tree_nobody_has_touched_never_respawns()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            Assert.False(harvest.IsAwaitingRespawn(TreeEntity));

            clock.Advance(TimeSpan.FromHours(1));
            Assert.Empty(harvest.DueRespawns());
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
        }

        [Fact]
        public void An_entity_that_is_not_a_tree_is_never_awaiting_respawn()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            Assert.False(harvest.IsAwaitingRespawn(999));
        }

        // ------------------------------------------------------------------
        // A chopped tree regrows after the delay
        // ------------------------------------------------------------------

        [Fact]
        public void A_chopped_tree_is_awaiting_respawn()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            ChopOnce(harvest, clock, TreeEntity);

            Assert.True(harvest.IsAwaitingRespawn(TreeEntity));
            Assert.NotEqual(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
        }

        [Fact]
        public void The_regrowth_waits_the_full_delay_after_the_cut()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            ChopOnce(harvest, clock, TreeEntity);

            clock.Advance(Respawn - TimeSpan.FromMilliseconds(1));
            Assert.Empty(harvest.DueRespawns());
            Assert.NotEqual(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));

            clock.Advance(TimeSpan.FromMilliseconds(1));
            System.Collections.Generic.IReadOnlyList<TreeRespawn> respawns = harvest.DueRespawns();

            Assert.Single(respawns);
            Assert.Equal(TreeEntity, respawns[0].TreeEntityId);
            Assert.Equal(Trees.FullSectionMask, respawns[0].SectionMask);
        }

        [Fact]
        public void A_respawn_stands_the_whole_tree_back_up_in_the_stored_mask()
        {
            // The reason the mask lives in this module: ComponentsSerializer's 1036
            // branch seeds a late joiner from MaskOf, so a tree that respawned must
            // read as whole again or a player arriving after regrowth sees a stump.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            ChopOnce(harvest, clock, TreeEntity);
            clock.Advance(Respawn);
            harvest.DueRespawns();

            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
            Assert.False(harvest.IsAwaitingRespawn(TreeEntity));
        }

        [Fact]
        public void A_fully_felled_tree_at_the_stump_still_respawns()
        {
            // The loudest deforestation case: the tree chopped all the way down to
            // its unfellable stump. It must come back too.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 1)); // lowest harvestable fells all but the stump
            clock.Advance(Interval);
            harvest.Due();
            Assert.Equal(1, harvest.MaskOf(TreeEntity)); // only section 0, the stump

            clock.Advance(Respawn);
            System.Collections.Generic.IReadOnlyList<TreeRespawn> respawns = harvest.DueRespawns();

            Assert.Single(respawns);
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
        }

        [Fact]
        public void A_partially_chopped_tree_regrows_the_whole_tree_like_retail()
        {
            // Retail's Respawn resets to every section, not to what was standing.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 11)); // one limb only
            clock.Advance(Interval);
            harvest.Due();
            int afterOneLimb = harvest.MaskOf(TreeEntity)!.Value;
            Assert.NotEqual(Trees.FullSectionMask, afterOneLimb);

            clock.Advance(Respawn);
            harvest.DueRespawns();
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
        }

        // ------------------------------------------------------------------
        // An actively-harvested tree does not regrow under the player
        // ------------------------------------------------------------------

        [Fact]
        public void Each_further_cut_pushes_the_regrowth_out_so_a_tree_under_a_beam_never_resets()
        {
            // If the timer armed once and never moved, a slow player chopping a big
            // tree would watch it regrow the sections they were still working.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);
            TreeTopology topology = Trees.Topology();

            // Chop a section every (Respawn - a bit): each cut rearms the timer, so
            // across many such cuts the tree is continuously below whole and never
            // fires a respawn.
            for (int i = 0; i < 6; i++)
            {
                harvest.OnCutSignal(Cutter, At(TreeEntity, Outermost(topology, harvest.MaskOf(TreeEntity)!.Value)));
                clock.Advance(Respawn - TimeSpan.FromSeconds(1));
                harvest.Due();
                Assert.Empty(harvest.DueRespawns());
            }

            Assert.True(harvest.IsAwaitingRespawn(TreeEntity));

            // Stop cutting; a full delay later it finally regrows.
            clock.Advance(Respawn);
            Assert.Single(harvest.DueRespawns());
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
        }

        // ------------------------------------------------------------------
        // Firing is once-only and deterministic
        // ------------------------------------------------------------------

        [Fact]
        public void A_respawn_fires_exactly_once()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            ChopOnce(harvest, clock, TreeEntity);
            clock.Advance(Respawn);

            Assert.Single(harvest.DueRespawns());

            clock.Advance(TimeSpan.FromHours(1));
            Assert.Empty(harvest.DueRespawns()); // whole again; nothing pending
        }

        [Fact]
        public void A_tree_can_be_chopped_again_after_it_regrows()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            ChopOnce(harvest, clock, TreeEntity);
            clock.Advance(Respawn);
            harvest.DueRespawns();
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));

            ChopOnce(harvest, clock, TreeEntity);
            Assert.NotEqual(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
            Assert.True(harvest.IsAwaitingRespawn(TreeEntity));

            clock.Advance(Respawn);
            Assert.Single(harvest.DueRespawns());
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
        }

        [Fact]
        public void Two_trees_chopped_at_different_times_regrow_at_their_own_times()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity, OtherTree);

            ChopOnce(harvest, clock, TreeEntity);

            // A minute later, chop the other one.
            clock.Advance(TimeSpan.FromMinutes(1));
            ChopOnce(harvest, clock, OtherTree);

            // Advance to just after the first tree's respawn is due (Respawn from
            // its cut) but before the second's. The first regrows alone.
            clock.Advance(Respawn - TimeSpan.FromMinutes(1));
            System.Collections.Generic.IReadOnlyList<TreeRespawn> first = harvest.DueRespawns();
            Assert.Single(first);
            Assert.Equal(TreeEntity, first[0].TreeEntityId);
            Assert.True(harvest.IsAwaitingRespawn(OtherTree));

            // A minute more brings the second one.
            clock.Advance(TimeSpan.FromMinutes(1));
            System.Collections.Generic.IReadOnlyList<TreeRespawn> second = harvest.DueRespawns();
            Assert.Single(second);
            Assert.Equal(OtherTree, second[0].TreeEntityId);
        }

        [Fact]
        public void Nothing_is_due_to_respawn_when_no_tree_has_been_chopped()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity, OtherTree);

            clock.Advance(TimeSpan.FromHours(1));
            Assert.Empty(harvest.DueRespawns());
        }

        // ------------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------------

        [Fact]
        public void The_default_respawn_delay_is_five_minutes()
        {
            Assert.Equal(TimeSpan.FromMinutes(5), TreeHarvest.DefaultRespawnDelay);
            Assert.Equal(TreeHarvest.DefaultRespawnDelay, new TreeHarvest(new FakeClock()).RespawnDelay);
        }

        [Fact]
        public void A_non_positive_respawn_delay_is_rejected_because_a_tree_would_regrow_the_instant_it_was_cut()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TreeHarvest(new FakeClock(), Interval, TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TreeHarvest(new FakeClock(), Interval, TimeSpan.FromSeconds(-1)));
        }

        private static int Outermost(TreeTopology topology, int mask)
        {
            for (int s = topology.SectionCount - 1; s >= 0; s--)
            {
                if (TreeTopology.IsInMask(s, mask))
                {
                    return s;
                }
            }
            return -1;
        }
    }
}
