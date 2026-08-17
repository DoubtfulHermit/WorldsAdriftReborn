using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The per-species skeletons, recovered from the shipped prefabs.
    ///
    /// These are regression locks on a DECODE, not assertions about a design: the
    /// numbers came out of resources.assets, and the reason they matter is that the
    /// mask is the protocol - a wrong skeleton is not an error anyone sees in a log,
    /// it is a tree that visibly comes apart wrongly.
    /// </summary>
    public class TreeTopologiesTests
    {
        [Fact]
        public void All_sixty_five_shipped_tree_prefabs_have_a_recovered_skeleton()
        {
            Assert.Equal(65, TreeTopologies.Count);
        }

        [Fact]
        public void Every_prefab_with_a_species_also_has_a_skeleton()
        {
            // The two tables are recovered from the same 65 prefabs and must not
            // drift: a tree we know the wood of but not the shape of would be
            // planted on the fallback skeleton and come apart wrongly.
            foreach (string prefab in TreeSpecies.Prefabs)
            {
                Assert.True(TreeTopologies.Has(prefab), prefab + " has a wood but no skeleton");
            }
        }

        [Fact]
        public void Every_prefab_with_a_skeleton_also_has_a_species()
        {
            foreach (string prefab in TreeTopologies.Prefabs)
            {
                Assert.NotNull(TreeSpecies.WoodFor(prefab));
            }
        }

        // ------------------------------------------------------------------
        // The regression lock: the table must reproduce the hand-recovered
        // constants that predate it.
        // ------------------------------------------------------------------

        [Fact]
        public void The_table_reproduces_the_hand_recovered_Tree_topology_exactly()
        {
            // Trees.Branches / Trees.Harvestable were recovered by hand long before
            // this table existed, by a separate parse. If the two ever disagree, one
            // of the two recoveries is wrong and the tree everybody chops is the
            // thing that breaks.
            TreeTopology fromTable = TreeTopologies.For(Trees.AssetName)!;
            TreeTopology byHand = Trees.Topology();

            Assert.Equal(byHand.SectionCount, fromTable.SectionCount);
            Assert.Equal(byHand.FullMask, fromTable.FullMask);

            for (int s = 0; s < byHand.SectionCount; s++)
            {
                Assert.Equal(byHand.IsHarvestable(s), fromTable.IsHarvestable(s));
            }

            Assert.Equal(byHand.Branches.Count, fromTable.Branches.Count);
            for (int b = 0; b < byHand.Branches.Count; b++)
            {
                Assert.Equal(byHand.Branches[b].Root, fromTable.Branches[b].Root);
                Assert.Equal(byHand.Branches[b].Sections, fromTable.Branches[b].Sections);
            }
        }

        [Fact]
        public void Cutting_the_same_section_agrees_between_the_table_and_the_hand_recovery()
        {
            // The stronger form of the lock: identical ARITHMETIC, not just
            // identical fields, on every section from every angle.
            TreeTopology fromTable = TreeTopologies.For(Trees.AssetName)!;
            TreeTopology byHand = Trees.Topology();

            for (int section = 0; section < byHand.SectionCount; section++)
            {
                foreach (bool above in new[] { false, true })
                {
                    TreeCut a = byHand.Cut(Trees.FullSectionMask, section, above);
                    TreeCut b = fromTable.Cut(Trees.FullSectionMask, section, above);

                    Assert.Equal(a.Outcome, b.Outcome);
                    Assert.Equal(a.SectionId, b.SectionId);
                    Assert.Equal(a.FallingMask, b.FallingMask);
                    Assert.Equal(a.RemainingMask, b.RemainingMask);
                }
            }
        }

        // ------------------------------------------------------------------
        // Shape of the recovered set
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("Tree", 12)]
        [InlineData("TreePalmStubby", 13)]
        [InlineData("TreePalmStubby02", 14)]
        [InlineData("TreeStraightBlue", 12)]
        public void A_sample_of_prefabs_have_their_recovered_section_counts(string prefab, int sections)
        {
            Assert.Equal(sections, TreeTopologies.For(prefab)!.SectionCount);
        }

        [Fact]
        public void The_skeletons_genuinely_differ_which_is_why_this_table_exists()
        {
            // If every prefab were `Tree`-shaped, birch arithmetic would have been
            // fine and none of this would be needed. They are not.
            HashSet<int> counts = new HashSet<int>();
            foreach (string prefab in TreeTopologies.Prefabs)
            {
                counts.Add(TreeTopologies.For(prefab)!.SectionCount);
            }

            Assert.Equal(new[] { 9, 10, 11, 12, 13, 14 }, counts.OrderBy(c => c).ToArray());
        }

        [Fact]
        public void Every_skeleton_has_an_unfellable_stump_at_section_zero()
        {
            // True on all 65: section 0 is the only section that is never a child of
            // any branch, and it is flagged non-harvestable. It is why a tree cannot
            // be felled at the base.
            foreach (string prefab in TreeTopologies.Prefabs)
            {
                Assert.False(TreeTopologies.For(prefab)!.IsHarvestable(0), prefab + " has a fellable stump");
            }
        }

        [Fact]
        public void Only_TreePalmStubby02_has_a_second_unfellable_section()
        {
            // An authoring quirk carried through verbatim rather than corrected -
            // the client reads the same authored data, so "fixing" it here would be
            // a disagreement. Locked so it cannot be tidied away by accident.
            List<string> extras = new List<string>();
            foreach (string prefab in TreeTopologies.Prefabs)
            {
                TreeTopology t = TreeTopologies.For(prefab)!;
                for (int s = 1; s < t.SectionCount; s++)
                {
                    if (!t.IsHarvestable(s))
                    {
                        extras.Add(prefab + ":" + s);
                    }
                }
            }

            Assert.Equal(new[] { "TreePalmStubby02:1" }, extras.ToArray());
        }

        [Fact]
        public void TreePalmStubbys_stale_authored_mask_is_ignored_in_favour_of_the_section_count()
        {
            // The prefab carries an authored sectionMask of 16383 - a 14-bit mask on
            // a 13-section tree. Neither side uses it: the client derives the mask as
            // 2^treeSections.Length - 1, and so does TreeTopology.FullMask.
            TreeTopology stubby = TreeTopologies.For("TreePalmStubby")!;

            Assert.Equal(13, stubby.SectionCount);
            Assert.Equal((1 << 13) - 1, stubby.FullMask);
            Assert.NotEqual(16383, stubby.FullMask);
        }

        // ------------------------------------------------------------------
        // Every recovered skeleton must actually be cuttable
        // ------------------------------------------------------------------

        [Fact]
        public void Every_species_can_be_chopped_down_to_its_stump_without_throwing_or_stalling()
        {
            // The real acceptance test for a skeleton: hold a beam on the outermost
            // standing section until nothing more comes away, and check the tree
            // ends as a stump with no bit ever resurrected. A malformed branch graph
            // would show up here as a stall or an exception.
            foreach (string prefab in TreeTopologies.Prefabs)
            {
                TreeTopology t = TreeTopologies.For(prefab)!;
                int mask = t.FullMask;
                int guard = 0;

                while (t.ActiveCount(mask) > 1 && guard++ < 100)
                {
                    int outermost = -1;
                    for (int s = t.SectionCount - 1; s >= 0; s--)
                    {
                        if (TreeTopology.IsInMask(s, mask) && t.IsHarvestable(s))
                        {
                            outermost = s;
                            break;
                        }
                    }

                    if (outermost < 0)
                    {
                        break;
                    }

                    TreeCut cut = t.Cut(mask, outermost, above: false);
                    if (!cut.DidCut)
                    {
                        break;
                    }

                    Assert.Equal(0, cut.RemainingMask & cut.FallingMask);
                    Assert.Equal(mask, cut.RemainingMask | cut.FallingMask);
                    mask = cut.RemainingMask;
                }

                Assert.True(guard < 100, prefab + " did not stop being choppable");

                // What survives is exactly the unfellable sections. That is the
                // stump alone on 64 of the 65 prefabs, and stump + section 1 on
                // TreePalmStubby02, whose second non-harvestable section is an
                // authoring quirk carried through verbatim.
                TreeTopology done = t;
                for (int s = 0; s < done.SectionCount; s++)
                {
                    if (TreeTopology.IsInMask(s, mask))
                    {
                        Assert.False(done.IsHarvestable(s),
                            prefab + " left harvestable section " + s + " standing");
                    }
                }

                Assert.True(TreeTopology.IsInMask(0, mask), prefab + " lost its stump");
            }
        }

        [Fact]
        public void No_species_can_have_its_stump_cut_away()
        {
            foreach (string prefab in TreeTopologies.Prefabs)
            {
                TreeTopology t = TreeTopologies.For(prefab)!;
                TreeCut cut = t.Cut(1, 0, above: false); // stump alone standing

                Assert.False(cut.DidCut);
                Assert.Equal(1, cut.RemainingMask);
            }
        }

        // ------------------------------------------------------------------
        // Lookup behaviour
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("Tree_unityclient")]
        [InlineData("Tree_unityworker")]
        [InlineData("tree")]
        [InlineData("TREE")]
        public void The_lookup_tolerates_worker_suffixes_and_casing(string prefab)
        {
            Assert.NotNull(TreeTopologies.For(prefab));
            Assert.Equal(12, TreeTopologies.For(prefab)!.SectionCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("MetalNugget")]
        [InlineData("Island")]
        public void A_non_tree_has_no_skeleton_rather_than_getting_Trees(string? asset)
        {
            // The fallback is a decision with a visible consequence, so it belongs
            // at the call site where it is logged - not hidden in this lookup.
            Assert.Null(TreeTopologies.For(asset));
            Assert.False(TreeTopologies.Has(asset));
        }
    }
}
