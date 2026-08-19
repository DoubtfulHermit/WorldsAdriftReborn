using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The half of chopping that the wire does not provide: a cadence.
    ///
    /// Every test here turns on the fact that <c>1037 TreeCutterState</c> is a
    /// LATCH - one packet when the beam arrives on a section, one when it leaves -
    /// so the difference between "holding the beam fells a tree" and "one aim
    /// yields one section" is entirely this module's timer.
    ///
    /// Time is injected, so a 0.75 s cadence is asserted on without any test
    /// sleeping, and - more importantly - so the cadence is measured in SECONDS
    /// rather than in main-loop turns. That distinction has already cost this
    /// project one debugging round (see MirrorSchedule): the loop turns once per
    /// ENet EVENT, so a busy server would chop hundreds of times a second.
    /// </summary>
    public class TreeHarvestTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }

            public void Advance(TimeSpan by) => Elapsed += by;
        }

        private const long TreeEntity = 500;
        private const long OtherTree = 501;
        private const long Cutter = 7;
        private const long OtherCutter = 8;

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.75);

        private static TreeHarvest Planted(FakeClock clock, params long[] trees)
        {
            TreeHarvest harvest = new TreeHarvest(clock, Interval);
            foreach (long tree in trees)
            {
                harvest.Plant(tree, Trees.Topology(), Trees.WoodType);
            }
            return harvest;
        }

        private static TreeCutSignal At(long tree, int section) => new TreeCutSignal(tree, section, false);

        // ------------------------------------------------------------------
        // Planting
        // ------------------------------------------------------------------

        [Fact]
        public void A_planted_tree_starts_whole()
        {
            TreeHarvest harvest = Planted(new FakeClock(), TreeEntity);

            Assert.True(harvest.IsTree(TreeEntity));
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
            Assert.Equal(Trees.WoodType, harvest.WoodTypeOf(TreeEntity));
        }

        [Fact]
        public void Planting_the_same_tree_twice_does_not_stand_it_back_up()
        {
            // EVERY joining client walks the same spawn plan and reaches the tree's
            // AddEntity step, but there is one tree. Without idempotence the second
            // player to log in would silently restore every section the first had
            // chopped - and the first player's client would never be told.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 8));
            clock.Advance(Interval);
            Assert.Single(harvest.Due());

            int chopped = harvest.MaskOf(TreeEntity)!.Value;
            Assert.NotEqual(Trees.FullSectionMask, chopped);

            Assert.False(harvest.Plant(TreeEntity, Trees.Topology(), Trees.WoodType));
            Assert.Equal(chopped, harvest.MaskOf(TreeEntity));
        }

        [Fact]
        public void An_entity_that_is_not_a_tree_reports_as_such_rather_than_throwing()
        {
            TreeHarvest harvest = Planted(new FakeClock(), TreeEntity);

            Assert.False(harvest.IsTree(999));
            Assert.Null(harvest.MaskOf(999));
            Assert.Null(harvest.WoodTypeOf(999));
            Assert.Null(harvest.TopologyOf(999));
        }

        // ------------------------------------------------------------------
        // The latch
        // ------------------------------------------------------------------

        [Fact]
        public void Nothing_is_due_while_no_beam_is_on_a_tree()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            clock.Advance(TimeSpan.FromHours(1));
            Assert.Empty(harvest.Due());
            Assert.Equal(0, harvest.EngagedCount);
        }

        [Fact]
        public void The_first_cut_waits_a_full_interval_so_a_beam_swept_past_a_tree_does_not_chop_it()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 8));

            clock.Advance(Interval - TimeSpan.FromMilliseconds(1));
            Assert.Empty(harvest.Due());
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Single(harvest.Due());
        }

        [Fact]
        public void A_repeat_of_the_same_latch_does_not_postpone_the_cut()
        {
            // If a re-sent identical latch restarted the timer, a client that
            // republished faster than the interval would postpone every cut
            // forever - and it would look exactly like the timer being broken.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            Assert.True(harvest.OnCutSignal(Cutter, At(TreeEntity, 8)));

            // Ten re-publishes over 700 ms: still short of the interval, so still
            // nothing due - and crucially none of them pushed the deadline out.
            for (int i = 0; i < 10; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(70));
                Assert.False(harvest.OnCutSignal(Cutter, At(TreeEntity, 8)));
            }
            Assert.Empty(harvest.Due());

            // The cut lands at 750 ms from the FIRST latch, not 750 ms from the last
            // repeat - which would be 1450 ms.
            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Single(harvest.Due());
        }

        [Fact]
        public void Moving_the_beam_to_another_section_restarts_the_timer()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 8));
            clock.Advance(TimeSpan.FromMilliseconds(700));

            Assert.True(harvest.OnCutSignal(Cutter, At(TreeEntity, 3)));

            clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.Empty(harvest.Due());

            clock.Advance(Interval);
            Assert.Single(harvest.Due());
        }

        [Fact]
        public void Taking_the_beam_off_a_tree_stops_the_chopping()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 8));
            Assert.Equal(1, harvest.EngagedCount);

            Assert.True(harvest.OnCutSignal(Cutter, TreeCutSignal.Disengaged));
            Assert.Equal(0, harvest.EngagedCount);

            clock.Advance(TimeSpan.FromHours(1));
            Assert.Empty(harvest.Due());
            Assert.Equal(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
        }

        [Theory]
        [InlineData(0L, 8)]      // an entity id that never resolved
        [InlineData(-1L, 8)]     // Improbable's InvalidEntityId
        [InlineData(TreeEntity, -1)] // the client's own "aiming at nothing" section
        [InlineData(999L, 8)]    // a real entity that is not a tree
        public void A_beam_resting_on_anything_that_is_not_a_tree_section_simply_disengages(long tree, int section)
        {
            // The beam legitimately rests on rocks, hulls and other players. This
            // is client input and is neither trusted nor fatal.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, new TreeCutSignal(tree, section, false));

            Assert.Equal(0, harvest.EngagedCount);
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Empty(harvest.Due());
        }

        [Fact]
        public void A_departed_players_latch_is_forgettable()
        {
            // Otherwise it keeps chopping a tree every 0.75 s on behalf of somebody
            // who logged out, for the life of the process.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 8));
            Assert.True(harvest.Forget(Cutter));
            Assert.False(harvest.Forget(Cutter));

            clock.Advance(TimeSpan.FromHours(1));
            Assert.Empty(harvest.Due());
        }

        // ------------------------------------------------------------------
        // Cutting
        // ------------------------------------------------------------------

        [Fact]
        public void A_beam_held_low_on_the_trunk_chops_once_per_interval_and_no_faster()
        {
            // Section 1 is the lowest harvestable one, and the beam keeps resting
            // on it because the stump below and it are the last things left - so
            // this is the one aim that stays valid across ticks without the client
            // re-latching. It fells everything but the stump on the FIRST tick,
            // then has nothing left to take.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);
            harvest.OnCutSignal(Cutter, At(TreeEntity, 1));

            clock.Advance(Interval - TimeSpan.FromMilliseconds(1));
            Assert.Empty(harvest.Due());

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Single(harvest.Due());
            Assert.Equal(1, harvest.MaskOf(TreeEntity));

            clock.Advance(Interval);
            Assert.Empty(harvest.Due());
        }

        [Fact]
        public void A_beam_that_follows_the_tree_down_fells_it_one_section_per_interval()
        {
            // THE END-TO-END STATEMENT, in the units the player experiences it:
            // eleven sections, one per 0.75 s, the stump stays, and the server then
            // goes quiet.
            //
            // Re-latching each tick is not test convenience, it is what the client
            // does: a felled section's GameObject is deactivated, so the beam's
            // raycast stops hitting it and lands on whatever is behind - and
            // TreeCuttingBehaviour publishes the new section id. The server never
            // advances the target itself.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);
            TreeTopology topology = Trees.Topology();

            int changes = 0;
            for (int i = 0; i < 40; i++)
            {
                harvest.OnCutSignal(Cutter, At(TreeEntity, Outermost(topology, harvest.MaskOf(TreeEntity)!.Value)));
                clock.Advance(Interval);

                foreach (TreeSectionMaskChange change in harvest.Due())
                {
                    Assert.Equal(1, change.SectionsFelled);
                    changes++;
                }
            }

            Assert.Equal(11, changes);
            Assert.Equal(1, harvest.MaskOf(TreeEntity));

            clock.Advance(Interval);
            Assert.Empty(harvest.Due());
        }

        /// <summary>
        /// The highest-numbered standing section - a stand-in for "what the beam
        /// now hits once the section in front of it was deactivated".
        /// </summary>
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

        [Fact]
        public void A_stale_latch_on_a_felled_section_does_not_spin_or_throw()
        {
            // Reachable from ordinary play: the latch keeps naming a section after
            // the tick that felled it. The timer is rearmed on a refusal too, so
            // this polls at the interval instead of re-evaluating every loop turn.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);
            harvest.OnCutSignal(Cutter, At(TreeEntity, 8));

            clock.Advance(Interval);
            Assert.Single(harvest.Due());
            int after = harvest.MaskOf(TreeEntity)!.Value;

            for (int i = 0; i < 5; i++)
            {
                clock.Advance(Interval);
                Assert.Empty(harvest.Due());
            }

            Assert.Equal(after, harvest.MaskOf(TreeEntity));
        }

        [Fact]
        public void The_stored_mask_is_what_a_later_joiner_would_be_seeded_with()
        {
            // The reason this module owns the mask at all: ComponentsSerializer's
            // 1036 branch reads MaskOf(entityId), so a client checking the tree out
            // after somebody chopped it is told what is standing. Seeding the
            // prefab's full mask instead would make the two clients disagree about
            // the world, and every later SetSectionMask would be a diff against the
            // wrong baseline.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);
            harvest.OnCutSignal(Cutter, At(TreeEntity, 8));

            clock.Advance(Interval);
            IReadOnlyList<TreeSectionMaskChange> changes = harvest.Due();

            Assert.Equal(changes[0].SectionMask, harvest.MaskOf(TreeEntity));
            Assert.NotEqual(Trees.FullSectionMask, harvest.MaskOf(TreeEntity));
        }

        [Fact]
        public void Two_players_chopping_two_trees_do_not_interfere()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity, OtherTree);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 10));
            harvest.OnCutSignal(OtherCutter, At(OtherTree, 1));

            clock.Advance(Interval);
            IReadOnlyList<TreeSectionMaskChange> changes = harvest.Due();

            Assert.Equal(2, changes.Count);
            Assert.Contains(changes, c => c.TreeEntityId == TreeEntity && c.CutterEntityId == Cutter);
            Assert.Contains(changes, c => c.TreeEntityId == OtherTree && c.CutterEntityId == OtherCutter);

            // One took the top section only; the other took everything but the stump.
            Assert.Equal(Trees.FullSectionMask & ~(1 << 10), harvest.MaskOf(TreeEntity));
            Assert.Equal(1, harvest.MaskOf(OtherTree));
        }

        [Fact]
        public void Two_players_chopping_the_SAME_tree_share_one_mask()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            harvest.OnCutSignal(Cutter, At(TreeEntity, 10));
            harvest.OnCutSignal(OtherCutter, At(TreeEntity, 9));

            clock.Advance(Interval);
            IReadOnlyList<TreeSectionMaskChange> changes = harvest.Due();

            // Both latches fire, but they apply in sequence against one mask, so
            // the second sees what the first left. Whatever the order, the tree
            // ends smaller than either cut alone would have made it and no bit is
            // resurrected.
            Assert.NotEmpty(changes);
            foreach (TreeSectionMaskChange change in changes)
            {
                Assert.Equal(0, change.SectionMask & change.FallingMask);
            }
            Assert.Equal(changes[changes.Count - 1].SectionMask, harvest.MaskOf(TreeEntity));
        }

        // ------------------------------------------------------------------
        // The inventory-grant seam's contract
        // ------------------------------------------------------------------

        [Fact]
        public void Every_change_carries_everything_a_wood_grant_would_need()
        {
            // The grant itself is deliberately not implemented here - 1081 has one
            // owner - so this is the contract that keeps the seam usable: who,
            // what, and how much, without the grant having to re-derive any of it.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);
            harvest.OnCutSignal(Cutter, At(TreeEntity, 8));

            clock.Advance(Interval);
            TreeSectionMaskChange change = harvest.Due().Single();

            Assert.Equal(Cutter, change.CutterEntityId);          // WHO gets the wood
            Assert.Equal("birch", change.WoodType);               // WHAT they get
            Assert.Equal(4, change.SectionsFelled);               // HOW MUCH: 8, 9, 10, 11
            Assert.Equal(TreeEntity, change.TreeEntityId);
            Assert.Equal(8, change.SectionId);
        }

        [Fact]
        public void SectionsFelled_always_matches_the_number_of_bits_that_left_the_mask()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);
            harvest.OnCutSignal(Cutter, At(TreeEntity, 10));

            TreeTopology topology = Trees.Topology();
            int before = Trees.FullSectionMask;

            for (int i = 0; i < 20; i++)
            {
                clock.Advance(Interval);
                foreach (TreeSectionMaskChange change in harvest.Due())
                {
                    Assert.Equal(topology.ActiveCount(before) - topology.ActiveCount(change.SectionMask),
                                 change.SectionsFelled);
                    before = change.SectionMask;
                }
            }
        }

        // ------------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------------

        [Fact]
        public void The_default_cadence_is_three_quarters_of_a_second()
        {
            Assert.Equal(TimeSpan.FromSeconds(0.75), TreeHarvest.DefaultCutInterval);
            Assert.Equal(TreeHarvest.DefaultCutInterval, new TreeHarvest(new FakeClock()).CutInterval);
        }

        [Fact]
        public void A_non_positive_interval_is_rejected_because_it_would_fell_a_tree_in_one_loop_turn()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TreeHarvest(new FakeClock(), TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TreeHarvest(new FakeClock(), TimeSpan.FromSeconds(-1)));
        }

        // ------------------------------------------------------------------
        // A FELLED LOG IS A HARVESTABLE STAND
        //
        // This is what makes a tree come apart piece by piece rather than in one
        // go. Retail's TreeSection.Harvest handed the severed sections to
        // SpawnNewTree as a WHOLE NEW TREE (acs/TreeSection.cs:81) which was then
        // chopped by exactly the same code as the standing one. Planting the log
        // here is that, and the rules that make a log different from a tree are
        // the two below: it never regrows, and its last section is takeable.
        // ------------------------------------------------------------------

        private const long LogEntity = 2_000_000_001L;

        [Fact]
        public void A_felled_log_is_planted_with_only_the_sections_it_carried_away()
        {
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = Planted(clock, TreeEntity);

            // A real cut off a real tree, so the log's mask is production arithmetic.
            TreeTopology tree = Trees.Topology();
            TreeCut cut = tree.Cut(Trees.FullSectionMask, 1, false);
            Assert.True(cut.DidCut);
            int logMask = cut.FallingMask & ~(1 << cut.SectionId);
            Assert.NotEqual(0, logMask);

            Assert.True(harvest.PlantFelled(LogEntity, tree, Trees.WoodType, logMask));

            Assert.True(harvest.IsTree(LogEntity));
            Assert.True(harvest.IsFelled(LogEntity));
            Assert.False(harvest.IsFelled(TreeEntity));

            // NOT the full mask, which is the whole difference from Plant: a log is a
            // fragment, and seeding it whole would have the severed crown check out
            // as a complete second tree standing inside the first.
            Assert.Equal(logMask, harvest.MaskOf(LogEntity));
            Assert.NotEqual(Trees.FullSectionMask, harvest.MaskOf(LogEntity));
            Assert.Equal(Trees.WoodType, harvest.WoodTypeOf(LogEntity));
        }

        [Fact]
        public void A_beam_held_on_a_log_takes_it_apart_one_piece_at_a_time()
        {
            // THE HEADLINE BEHAVIOUR. A trunk on the ground is chopped by the same
            // latch, the same timer and the same topology as a standing tree, so
            // "hold the beam on the log and it comes apart" is this and nothing else.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, Interval);
            TreeTopology tree = Trees.Topology();

            int logMask = Trees.FullSectionMask & ~1;
            Assert.True(harvest.PlantFelled(LogEntity, tree, Trees.WoodType, logMask));

            // Aim at the outermost section the log still has, the way a player
            // working along a trunk does, and keep the beam there.
            int cuts = 0;
            int lastMask = logMask;
            for (int turn = 0; turn < 200 && harvest.MaskOf(LogEntity) != 0; turn++)
            {
                int mask = harvest.MaskOf(LogEntity)!.Value;
                harvest.OnCutSignal(Cutter, At(LogEntity, HighestSectionIn(mask)));
                clock.Advance(Interval);

                foreach (TreeSectionMaskChange change in harvest.Due())
                {
                    cuts++;

                    // NEVER IN ONE GO: one section splinters into wood per cut, no
                    // matter how much the cut severed.
                    Assert.Equal(1, change.SectionsSplintered);

                    // And what it severed beyond that section is still owed - it
                    // leaves in a sub-log rather than being paid for here.
                    Assert.Equal(change.FallingMask & ~change.SplinterMask, change.LogMask);

                    lastMask = change.SectionMask;
                }
            }

            // The log is chopped away to nothing - it has no stump to keep - and it
            // took one cut per section to do it.
            Assert.Equal(0, lastMask);
            Assert.Equal(0, harvest.MaskOf(LogEntity));
            Assert.True(cuts > 1, "a log must take more than one cut to break up");
        }

        [Fact]
        public void The_last_section_of_a_log_is_takeable_where_a_trees_is_not()
        {
            // acs/TreeSection.cs:41-44 refuses at one active section so a rooted tree
            // always keeps a stump. A log has no stump to keep, and refusing here
            // would strand one section of every trunk on the ground for ever.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, Interval);
            Assert.True(harvest.PlantFelled(LogEntity, Trees.Topology(), Trees.WoodType, 1 << 4));

            harvest.OnCutSignal(Cutter, At(LogEntity, 4));
            clock.Advance(Interval);

            TreeSectionMaskChange change = Assert.Single(harvest.Due());
            Assert.Equal(4, change.SectionId);
            Assert.Equal(1, change.SectionsSplintered);
            Assert.Equal(0, change.LogMask);
            Assert.Equal(0, change.SectionMask);
            Assert.Equal(0, harvest.MaskOf(LogEntity));
        }

        [Fact]
        public void A_standing_tree_still_keeps_its_stump()
        {
            // The counterpart, so the log rule cannot leak onto rooted trees: a tree
            // chopped down to one section refuses, exactly as it always did.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, Interval);
            Assert.True(harvest.Plant(TreeEntity, Trees.Topology(), Trees.WoodType));

            harvest.OnCutSignal(Cutter, At(TreeEntity, 0));
            for (int turn = 0; turn < 100; turn++)
            {
                clock.Advance(Interval);
                harvest.Due();
            }

            Assert.NotEqual(0, harvest.MaskOf(TreeEntity));
        }

        [Fact]
        public void A_log_never_regrows()
        {
            // A trunk that sprouted back into a whole tree while it lay on the ground
            // would be absurd, and an understorm must not do it either.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, Interval, TimeSpan.FromSeconds(30));
            harvest.PlantFelled(LogEntity, Trees.Topology(), Trees.WoodType, Trees.FullSectionMask & ~1);

            harvest.OnCutSignal(Cutter, At(LogEntity, 8));
            clock.Advance(Interval);
            Assert.NotEmpty(harvest.Due());

            Assert.False(harvest.IsAwaitingRespawn(LogEntity));

            clock.Advance(TimeSpan.FromMinutes(10));
            Assert.Empty(harvest.DueRespawns());
            Assert.Empty(harvest.ResetAll());
            Assert.NotEqual(Trees.FullSectionMask, harvest.MaskOf(LogEntity));
        }

        [Fact]
        public void Uprooting_a_log_takes_the_beams_resting_on_it_with_it()
        {
            // A log that stopped existing must stop being choppable, and the beam on
            // it must disengage rather than keep cutting an id nobody can see.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, Interval);
            harvest.PlantFelled(LogEntity, Trees.Topology(), Trees.WoodType, Trees.FullSectionMask & ~1);

            harvest.OnCutSignal(Cutter, At(LogEntity, 8));
            Assert.Equal(1, harvest.EngagedCount);

            Assert.True(harvest.Uproot(LogEntity));

            Assert.False(harvest.IsTree(LogEntity));
            Assert.Null(harvest.IsFelled(LogEntity));
            Assert.Equal(0, harvest.EngagedCount);
            Assert.Equal(TreeCutSignal.Disengaged, harvest.SignalOf(Cutter));

            clock.Advance(Interval * 4);
            Assert.Empty(harvest.Due());

            Assert.False(harvest.Uproot(LogEntity));
        }

        [Fact]
        public void A_log_with_no_sections_is_not_planted_at_all()
        {
            TreeHarvest harvest = new TreeHarvest(new FakeClock(), Interval);

            Assert.False(harvest.PlantFelled(LogEntity, Trees.Topology(), Trees.WoodType, 0));
            Assert.False(harvest.IsTree(LogEntity));
        }

        private static int HighestSectionIn(int mask)
        {
            int highest = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0) highest = i;
            }
            return highest;
        }

        [Fact]
        public void A_signal_reads_as_engaged_only_when_it_names_a_real_target()
        {
            Assert.False(TreeCutSignal.Disengaged.IsEngaged);
            Assert.False(new TreeCutSignal(-1, 8, false).IsEngaged);   // InvalidEntityId
            Assert.False(new TreeCutSignal(0, 8, false).IsEngaged);    // never resolved
            Assert.False(new TreeCutSignal(5, -1, false).IsEngaged);   // aiming at nothing
            Assert.True(new TreeCutSignal(5, 0, false).IsEngaged);
        }
    }
}
