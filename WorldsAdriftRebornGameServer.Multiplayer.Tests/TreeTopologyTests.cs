using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// WHICH SECTIONS FALL. Every assertion here is on the MASK, because the mask
    /// is the entire protocol: the client renders whatever bits it is handed, so a
    /// wrong answer here is not an error message, it is a tree that comes apart
    /// wrongly and no log line anywhere.
    ///
    /// The topology under test is Bossa's own, recovered from the shipped
    /// <c>Tree_unityclient</c> prefab, so these are tests against real authored
    /// content rather than against a fixture invented to make them pass.
    /// </summary>
    public class TreeTopologyTests
    {
        private static TreeTopology Tree() => Trees.Topology();

        // ------------------------------------------------------------------
        // The recovered shape itself
        // ------------------------------------------------------------------

        [Fact]
        public void The_prefab_has_twelve_sections_and_a_full_mask_of_4095()
        {
            // 4095 is asserted twice over: derived here from the section count,
            // and independently READ off the prefab's own serialized sectionMask.
            // The two agreeing is what makes it a recovery and not an assumption.
            Assert.Equal(12, Tree().SectionCount);
            Assert.Equal(4095, Tree().FullMask);
            Assert.Equal(Trees.FullSectionMask, Tree().FullMask);
        }

        [Fact]
        public void Section_zero_is_the_stump_and_is_the_only_unharvestable_one()
        {
            TreeTopology tree = Tree();
            Assert.False(tree.IsHarvestable(0));
            for (int s = 1; s < tree.SectionCount; s++)
            {
                Assert.True(tree.IsHarvestable(s), "section " + s + " should be harvestable");
            }
        }

        [Fact]
        public void Every_section_except_the_stump_is_a_child_of_exactly_one_branch()
        {
            // The structural statement behind "section 0 is the stump": it is the
            // only id that appears solely as a branch ROOT and never as a child.
            // If this ever fails, the recovered topology has been edited by hand.
            List<int> children = new List<int>();
            foreach (TreeBranch branch in Trees.Branches)
            {
                children.AddRange(branch.Sections);
            }

            Assert.Equal(11, children.Count);
            Assert.Equal(11, children.Distinct().Count());
            Assert.DoesNotContain(0, children);
        }

        // ------------------------------------------------------------------
        // WalkTree - the traversal ported from TreeBase
        // ------------------------------------------------------------------

        [Fact]
        public void Walking_from_the_topmost_section_takes_only_that_section()
        {
            // 10 is the end of the trunk chain: nothing hangs off it.
            Assert.Equal(1 << 10, Tree().FallingMaskFor(10, Trees.FullSectionMask));
        }

        [Fact]
        public void Walking_from_a_trunk_section_takes_everything_above_it_and_the_limbs_that_hang_off_those()
        {
            // Trunk is 0 -> 1 -> 2 -> 3 -> 4 -> 6 -> 8 -> 9 -> 10, with limb 11
            // off 9. So cutting 8 takes 8, 9, 10 and 11.
            int falling = Tree().FallingMaskFor(8, Trees.FullSectionMask);
            Assert.Equal(Mask(8, 9, 10, 11), falling);
        }

        [Fact]
        public void Walking_from_a_trunk_section_leaves_the_limbs_BELOW_it_standing()
        {
            // The whole reason a tree is not a single chain. Limb 5 hangs off 4 and
            // limb 7 off 6, both below 8; a naive "everything with a higher id"
            // would take them and would be wrong.
            int falling = Tree().FallingMaskFor(8, Trees.FullSectionMask);
            Assert.Equal(0, falling & Mask(5, 7));
        }

        [Fact]
        public void Walking_from_a_mid_trunk_section_takes_the_limbs_that_hang_off_it_and_above()
        {
            // 4 carries limb 5; 6 (above it) carries limb 7; 9 carries limb 11.
            Assert.Equal(Mask(4, 5, 6, 7, 8, 9, 10, 11), Tree().FallingMaskFor(4, Trees.FullSectionMask));
        }

        [Fact]
        public void Walking_from_a_limb_takes_only_that_limb()
        {
            Assert.Equal(Mask(5), Tree().FallingMaskFor(5, Trees.FullSectionMask));
            Assert.Equal(Mask(7), Tree().FallingMaskFor(7, Trees.FullSectionMask));
            Assert.Equal(Mask(11), Tree().FallingMaskFor(11, Trees.FullSectionMask));
        }

        [Fact]
        public void The_start_section_is_included_even_when_it_is_already_gone()
        {
            // Deliberate fidelity to TreeBase.WalkTree, which adds the start
            // section before any liveness test. Cut() relies on it: the returned
            // bit that the mask no longer has is exactly what its correction
            // clauses detect, and detecting it is what turns a stale aim into a
            // refusal instead of a wrong cut.
            int missingEight = Trees.FullSectionMask & ~Mask(8, 9, 10, 11);
            Assert.Equal(Mask(8), Tree().FallingMaskFor(8, missingEight));
        }

        // ------------------------------------------------------------------
        // The split is a partition
        // ------------------------------------------------------------------

        [Fact]
        public void Every_successful_cut_splits_the_mask_with_nothing_lost_and_nothing_duplicated()
        {
            // Swept over every section rather than sampled: a section that lost or
            // duplicated a bit would leave the client with a tree that has either
            // a floating fragment or a hole, and nothing would say so.
            TreeTopology tree = Tree();
            for (int section = 0; section < tree.SectionCount; section++)
            {
                TreeCut cut = tree.Cut(Trees.FullSectionMask, section, above: false);
                if (!cut.DidCut)
                {
                    continue;
                }

                Assert.Equal(Trees.FullSectionMask, cut.RemainingMask | cut.FallingMask);
                Assert.Equal(0, cut.RemainingMask & cut.FallingMask);
                Assert.NotEqual(0, cut.FallingMask);
                Assert.NotEqual(0, cut.RemainingMask);
            }
        }

        // ------------------------------------------------------------------
        // Cut - the refusals
        // ------------------------------------------------------------------

        [Fact]
        public void Aiming_at_the_stump_fells_the_whole_tree_above_it_and_never_the_stump_itself()
        {
            // Section 0 is not harvestable, so the hit forwards up to section 1 -
            // and cutting 1 takes everything except 0. That is the shipped
            // behaviour of chopping at the base, and the stump surviving is the
            // point rather than a rounding error.
            TreeCut cut = Tree().Cut(Trees.FullSectionMask, 0, above: false);

            Assert.Equal(TreeCutOutcome.Cut, cut.Outcome);
            Assert.Equal(1, cut.SectionId);
            Assert.Equal(Mask(0), cut.RemainingMask);
            Assert.Equal(Trees.FullSectionMask & ~Mask(0), cut.FallingMask);
        }

        [Fact]
        public void The_last_standing_section_is_never_cleared()
        {
            // acs/TreeSection.cs:41 - `if (tree.sectionsActive <= 1) return;`. The
            // shipped game leaves a stump; a tree that could be cleared entirely
            // would leave an entity with an empty mask and no way back.
            TreeCut cut = Tree().Cut(Mask(0), 0, above: false);
            Assert.Equal(TreeCutOutcome.RefusedNoTargetAbove, cut.Outcome);
            Assert.Equal(Mask(0), cut.RemainingMask);

            TreeCut harvestableLast = Tree().Cut(Mask(10), 10, above: false);
            Assert.Equal(TreeCutOutcome.RefusedLastSection, harvestableLast.Outcome);
            Assert.Equal(Mask(10), harvestableLast.RemainingMask);
        }

        [Fact]
        public void A_latch_still_naming_an_already_felled_section_refuses_instead_of_throwing()
        {
            // THIS IS REACHABLE FROM ORDINARY PLAY, which is why it matters. The
            // cut signal is a latch: it keeps naming the same section until the
            // player moves the beam, so the tick after a cut asks to cut something
            // that is no longer there. The client answers this case with
            // `throw new Exception("This shouldn't happen")`; a server that did
            // that would die on a held mouse button.
            int afterFirstCut = Trees.FullSectionMask & ~Mask(8, 9, 10, 11);

            TreeCut cut = Tree().Cut(afterFirstCut, 8, above: false);

            Assert.Equal(TreeCutOutcome.RefusedDegenerate, cut.Outcome);
            Assert.Equal(afterFirstCut, cut.RemainingMask);
            Assert.Equal(0, cut.FallingMask);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(12)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void A_section_id_outside_the_tree_is_refused_rather_than_trusted(int sectionId)
        {
            // sectionId arrives from the client, unvalidated, on every 1037 packet.
            TreeCut cut = Tree().Cut(Trees.FullSectionMask, sectionId, above: false);
            Assert.Equal(TreeCutOutcome.RefusedUnknownSection, cut.Outcome);
            Assert.Equal(Trees.FullSectionMask, cut.RemainingMask);
        }

        [Fact]
        public void A_refusal_always_returns_the_mask_it_was_given()
        {
            // So a caller may store RemainingMask unconditionally without first
            // asking whether the cut succeeded. Every refusal path, one test.
            TreeTopology tree = Tree();
            Assert.Equal(4095, tree.Cut(4095, 99, false).RemainingMask);
            Assert.Equal(Mask(10), tree.Cut(Mask(10), 10, false).RemainingMask);
            Assert.Equal(Mask(0), tree.Cut(Mask(0), 0, false).RemainingMask);
            Assert.Equal(Mask(0, 1), tree.Cut(Mask(0, 1), 8, false).RemainingMask);
        }

        // ------------------------------------------------------------------
        // aboveOrBelow
        // ------------------------------------------------------------------

        [Fact]
        public void A_hit_above_a_sections_origin_cuts_the_next_section_up()
        {
            // The wire's aboveOrBelow, and what makes chopping follow the
            // crosshair: a beam landing near the top of section 4 takes section 6
            // (the next trunk section) rather than section 4.
            TreeCut low = Tree().Cut(Trees.FullSectionMask, 4, above: false);
            TreeCut high = Tree().Cut(Trees.FullSectionMask, 4, above: true);

            Assert.Equal(4, low.SectionId);
            Assert.Equal(6, high.SectionId);
            Assert.True(high.RemainingMask > low.RemainingMask);
        }

        [Fact]
        public void A_hit_above_the_topmost_section_still_cuts_that_section()
        {
            // Nothing above 10 to forward to, so `above` is ignored rather than
            // swallowing the hit.
            TreeCut cut = Tree().Cut(Trees.FullSectionMask, 10, above: true);
            Assert.Equal(TreeCutOutcome.Cut, cut.Outcome);
            Assert.Equal(10, cut.SectionId);
        }

        // ------------------------------------------------------------------
        // The whole tree, chopped
        // ------------------------------------------------------------------

        [Fact]
        public void Chopping_the_outermost_standing_section_each_time_ends_at_the_stump_after_eleven_hits()
        {
            // The end-to-end statement, and it models what the CLIENT does rather
            // than what a naive caller might: the section under the crosshair
            // changes as sections vanish, because a deactivated section's collider
            // stops being hit and the raycast lands on whatever is behind it. The
            // server never advances the target itself - it cuts what the latch
            // names, and the latch is refreshed by the client.
            //
            // Eleven hits for eleven harvestable sections: one hit each, because
            // acs/TreeSection.cs:73-74 is `connectionStrength = 0;
            // connectionStrength--;` - the authored strength of 3 is overwritten,
            // not decremented.
            TreeTopology tree = Tree();
            int mask = Trees.FullSectionMask;
            int cuts = 0;

            for (int i = 0; i < 100; i++)
            {
                int target = OutermostStanding(tree, mask);
                TreeCut cut = tree.Cut(mask, target, above: false);
                if (!cut.DidCut)
                {
                    break;
                }

                // One at a time, from the outside in: the outermost section has
                // nothing hanging off it, so nothing else comes with it.
                Assert.Equal(1, tree.ActiveCount(cut.FallingMask));
                mask = cut.RemainingMask;
                cuts++;
            }

            Assert.Equal(Mask(0), mask);
            Assert.Equal(11, cuts);
            Assert.False(tree.Cut(mask, OutermostStanding(tree, mask), above: false).DidCut);
        }

        [Fact]
        public void A_latch_that_stays_on_a_felled_section_stops_after_one_cut()
        {
            // The counterpart, and the reason the test above has to model the
            // re-latch: the server does NOT walk down the tree on its own. Hold
            // the beam on section 10, the section goes, and the SAME latch then
            // names something that is no longer there - which refuses. Chopping
            // continues because the client publishes a new latch, not because the
            // server invents one.
            TreeTopology tree = Tree();
            TreeCut first = tree.Cut(Trees.FullSectionMask, 10, above: false);
            Assert.True(first.DidCut);

            TreeCut second = tree.Cut(first.RemainingMask, 10, above: false);
            Assert.Equal(TreeCutOutcome.RefusedDegenerate, second.Outcome);
        }

        /// <summary>
        /// The highest-numbered standing section - a stand-in for "what the beam
        /// now hits after the section in front of it disappeared".
        /// </summary>
        private static int OutermostStanding(TreeTopology tree, int mask)
        {
            for (int s = tree.SectionCount - 1; s >= 0; s--)
            {
                if (TreeTopology.IsInMask(s, mask))
                {
                    return s;
                }
            }
            return -1;
        }

        [Fact]
        public void Chopping_the_same_section_twice_in_a_row_takes_the_whole_tree_in_one_cut_then_refuses()
        {
            // Aiming low is the fast way down: the first cut takes everything
            // above the stump, and there is nothing left to take.
            TreeTopology tree = Tree();
            TreeCut first = tree.Cut(Trees.FullSectionMask, 1, above: false);
            Assert.True(first.DidCut);
            Assert.Equal(Mask(0), first.RemainingMask);

            TreeCut second = tree.Cut(first.RemainingMask, 1, above: false);
            Assert.False(second.DidCut);
        }

        [Fact]
        public void ActiveCount_counts_standing_sections()
        {
            TreeTopology tree = Tree();
            Assert.Equal(12, tree.ActiveCount(Trees.FullSectionMask));
            Assert.Equal(1, tree.ActiveCount(Mask(0)));
            Assert.Equal(0, tree.ActiveCount(0));
            Assert.Equal(3, tree.ActiveCount(Mask(0, 5, 11)));
        }

        // ------------------------------------------------------------------
        // Defences against a caller-supplied topology
        // ------------------------------------------------------------------

        [Fact]
        public void A_branch_cycle_refuses_rather_than_overflowing_the_stack()
        {
            // Bossa's data is a tree, so this is unreachable from Trees.Branches -
            // but the constructor is public, and a stack overflow cannot be caught
            // in .NET: it takes the whole server down with no handler.
            TreeTopology cyclic = new TreeTopology(3,
                new[] { new TreeBranch(0, 1, 2), new TreeBranch(2, 1) },
                new[] { true, true, true });

            TreeCut cut = cyclic.Cut(0b111, 1, above: false);
            Assert.True(cut.Outcome == TreeCutOutcome.Cut || !cut.DidCut);
        }

        [Fact]
        public void A_topology_that_cannot_be_put_on_the_wire_is_rejected_at_construction()
        {
            bool[] ok = new bool[1] { true };

            // sectionMask is a SIGNED int on the wire, so 32 sections would put the
            // top section in the sign bit.
            Assert.Throws<ArgumentOutOfRangeException>(() => new TreeTopology(32, Array.Empty<TreeBranch>(), new bool[32]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TreeTopology(0, Array.Empty<TreeBranch>(), Array.Empty<bool>()));

            // A harvestable list that does not line up with the sections would
            // silently mis-answer IsHarvestable for the tail.
            Assert.Throws<ArgumentException>(() => new TreeTopology(2, Array.Empty<TreeBranch>(), ok));

            // A section id no section has.
            Assert.Throws<ArgumentOutOfRangeException>(() => new TreeTopology(1, new[] { new TreeBranch(0, 7) }, ok));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TreeTopology(1, new[] { new TreeBranch(9, 0) }, ok));
        }

        private static int Mask(params int[] sections)
        {
            int mask = 0;
            foreach (int section in sections)
            {
                mask |= 1 << section;
            }
            return mask;
        }
    }
}
