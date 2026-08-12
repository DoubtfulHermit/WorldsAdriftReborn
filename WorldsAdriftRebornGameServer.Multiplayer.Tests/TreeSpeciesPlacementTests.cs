using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Placing more than one species in the world.
    ///
    /// The point of these tests is the GATES, not the rotation: a species may only
    /// be placed if its skeleton is recovered, its wood is recovered, and its
    /// component requirements are ones this server already serves. A miss on any of
    /// those renders as a visibly broken tree rather than as an error, so each gate
    /// is asserted rather than assumed.
    /// </summary>
    public class TreeSpeciesPlacementTests
    {
        [Fact]
        public void Species_variety_is_off_by_default_so_todays_proven_behaviour_is_untouched()
        {
            foreach (WorldEntity tree in WorldEntities.DistributedTrees())
            {
                Assert.Equal(Trees.AssetName, tree.AssetName);
            }
        }

        [Fact]
        public void With_variety_on_the_trees_cycle_through_the_verified_species()
        {
            List<WorldEntity> trees = WorldEntities.DistributedTrees(varySpecies: true).ToList();

            Assert.NotEmpty(trees);
            for (int i = 0; i < trees.Count; i++)
            {
                Assert.Equal(WorldEntities.VerifiedSpecies[i % WorldEntities.VerifiedSpecies.Count],
                             trees[i].AssetName);
            }
        }

        [Fact]
        public void Every_wood_in_the_catalogue_becomes_gatherable()
        {
            // The reason to do this at all: eight woods exist as items and only one
            // was obtainable.
            HashSet<string> woods = new HashSet<string>();
            foreach (string species in WorldEntities.VerifiedSpecies)
            {
                woods.Add(TreeSpecies.WoodFor(species)!);
            }

            Assert.Equal(TreeSpecies.Woods.OrderBy(w => w), woods.OrderBy(w => w));
        }

        // ------------------------------------------------------------------
        // The three gates
        // ------------------------------------------------------------------

        [Fact]
        public void Gate_one_every_placed_species_has_its_own_recovered_skeleton()
        {
            // Without this the tree is cut with birch arithmetic and comes apart
            // wrongly - the whole reason TreeTopologies was recovered.
            foreach (string species in WorldEntities.VerifiedSpecies)
            {
                Assert.True(TreeTopologies.Has(species), species + " has no recovered skeleton");
            }
        }

        [Fact]
        public void Gate_two_every_placed_species_has_a_recovered_wood_that_is_a_real_item()
        {
            foreach (string species in WorldEntities.VerifiedSpecies)
            {
                string? wood = TreeSpecies.WoodFor(species);
                Assert.NotNull(wood);
                Assert.Contains(wood, TreeSpecies.Woods);
            }
        }

        [Fact]
        public void Gate_three_TreePalmBlue2_is_never_placed_because_it_needs_a_component_we_do_not_serve()
        {
            // It carries LocalTransformTeleportBehaviour, which [Require]s
            // TeleportRequestState (190607). That id has no branch on this server,
            // and the client's batch is failOnComponentInitError: true - so placing
            // it would abort the batch and the tree would come up broken with its
            // break VFX silent. It is the one species of the 65 that fails the
            // component gate, and this test is the guard against it being added to
            // the rotation by someone reading only the topology and wood tables.
            Assert.DoesNotContain("TreePalmBlue2", WorldEntities.VerifiedSpecies);
        }

        [Fact]
        public void The_near_spawn_tree_stays_birch_whatever_the_knob_says()
        {
            // The one tree every session walks up to must not change behaviour
            // behind a switch - it is the proven path.
            Assert.Equal(Trees.AssetName, WorldEntities.HavenTree().AssetName);
            Assert.Equal("birch", TreeSpecies.WoodFor(WorldEntities.HavenTree().AssetName));
        }

        [Fact]
        public void The_rotation_starts_with_the_proven_prefab()
        {
            Assert.Equal(Trees.AssetName, WorldEntities.VerifiedSpecies[0]);
        }

        [Fact]
        public void Distributed_tree_keys_stay_unique_with_variety_on()
        {
            // The keys are the registry's identity for an entity; a collision would
            // silently drop a tree.
            List<string> keys = WorldEntities.DistributedTrees(varySpecies: true)
                .Select(t => t.Key).ToList();

            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [Fact]
        public void Varying_species_changes_only_the_asset_not_the_positions()
        {
            // Same island spots either way; only which prefab stands there differs.
            List<WorldEntity> plain = WorldEntities.DistributedTrees().ToList();
            List<WorldEntity> varied = WorldEntities.DistributedTrees(varySpecies: true).ToList();

            Assert.Equal(plain.Count, varied.Count);
            for (int i = 0; i < plain.Count; i++)
            {
                Assert.Equal(plain[i].Key, varied[i].Key);
                Assert.Equal(plain[i].Position, varied[i].Position);
                Assert.Equal(plain[i].Order, varied[i].Order);
            }
        }

        [Fact]
        public void A_placed_species_is_cut_with_its_own_skeleton_not_birchs()
        {
            // The end-to-end statement: plant one of each verified species and check
            // the harvest uses that species' own section count and pays its own wood.
            FakeClock clock = new FakeClock();
            TreeHarvest harvest = new TreeHarvest(clock, TimeSpan.FromSeconds(0.75));

            long id = 100;
            foreach (string species in WorldEntities.VerifiedSpecies)
            {
                TreeTopology topology = TreeTopologies.For(species)!;
                string wood = TreeSpecies.WoodFor(species)!;

                Assert.True(harvest.Plant(id, topology, wood));
                Assert.Equal(topology.FullMask, harvest.MaskOf(id));
                Assert.Equal(wood, harvest.WoodTypeOf(id));
                Assert.Equal(topology.SectionCount, harvest.TopologyOf(id)!.SectionCount);
                id++;
            }
        }

        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
        }
    }
}
