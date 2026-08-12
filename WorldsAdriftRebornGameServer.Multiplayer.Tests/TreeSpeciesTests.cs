using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The authored species table: which wood each tree prefab drops.
    ///
    /// Retail gave every tree type its own wood, and the eight woods differ in
    /// weight and strength (worldsadrift.fandom.com/wiki/Wood). The map is
    /// RECOVERED from the shipped _unityworker prefabs, so these tests are
    /// regression locks on a decode, not assertions about a design choice.
    /// </summary>
    public class TreeSpeciesTests
    {
        [Fact]
        public void All_sixty_five_shipped_tree_prefabs_have_a_species()
        {
            Assert.Equal(65, TreeSpecies.Count);
        }

        [Fact]
        public void Every_mapped_wood_is_one_of_the_eight_known_woods()
        {
            // 65/65 landed on a known wood in the parse; nothing needed a guess, and
            // an unknown wood here would be an itemTypeId the client cannot look up.
            foreach (string prefab in TreeSpecies.Prefabs)
            {
                Assert.Contains(TreeSpecies.WoodFor(prefab), TreeSpecies.Woods);
            }
        }

        [Fact]
        public void There_are_eight_woods()
        {
            Assert.Equal(8, TreeSpecies.Woods.Count);
            Assert.Equal(
                new[] { "ash", "birch", "cedar", "chestnut", "elm", "hemlock", "oak", "palm" },
                TreeSpecies.Woods);
        }

        [Fact]
        public void The_tree_this_world_plants_is_birch()
        {
            // The one species the world places today, and it must keep agreeing with
            // Trees.WoodType or the yield silently changes species.
            Assert.Equal("birch", TreeSpecies.WoodFor(Trees.AssetName));
            Assert.Equal(Trees.WoodType, TreeSpecies.WoodFor(Trees.AssetName));
        }

        [Theory]
        [InlineData("TreePalm1", "palm")]
        [InlineData("TreePalmStubby02", "palm")]
        [InlineData("TreeDessert2", "hemlock")]
        [InlineData("TreeWonky1Leaf6", "oak")]
        [InlineData("TreeWonky1Leaf3", "elm")]
        [InlineData("TreeStraightRed", "chestnut")]
        [InlineData("TreeWonky1LongLeaf2", "cedar")]
        [InlineData("TreeStraightBlue", "ash")]
        [InlineData("TreeOrange", "birch")]
        public void A_sample_of_each_wood_decodes_to_its_authored_species(string prefab, string wood)
        {
            Assert.Equal(wood, TreeSpecies.WoodFor(prefab));
        }

        [Fact]
        public void Bossas_lower_cased_prefab_name_still_resolves()
        {
            // treewonky2Leaf1 ships lower-cased where its thirteen siblings do not.
            // The lookup is case-insensitive precisely so that typo is not a hole.
            Assert.Equal("ash", TreeSpecies.WoodFor("treewonky2Leaf1"));
            Assert.Equal("ash", TreeSpecies.WoodFor("TreeWonky2Leaf1"));
        }

        [Fact]
        public void The_lookup_is_case_insensitive_because_the_prefab_container_is_lower_case()
        {
            Assert.Equal("palm", TreeSpecies.WoodFor("treepalm1"));
            Assert.Equal("birch", TreeSpecies.WoodFor("TREE"));
        }

        [Theory]
        [InlineData("Tree_unityclient", "birch")]
        [InlineData("Tree_unityworker", "birch")]
        [InlineData("TreePalm1_unityclient", "palm")]
        public void A_worker_suffixed_name_still_resolves(string prefab, string wood)
        {
            // The wire name is bare, but a caller may hold either form.
            Assert.Equal(wood, TreeSpecies.WoodFor(prefab));
        }

        [Fact]
        public void StripWorkerSuffix_leaves_a_bare_name_alone()
        {
            Assert.Equal("Tree", TreeSpecies.StripWorkerSuffix("Tree"));
            Assert.Equal("Tree", TreeSpecies.StripWorkerSuffix("Tree_unityclient"));
            Assert.Equal("Tree", TreeSpecies.StripWorkerSuffix("Tree_unityworker"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("MetalNugget")]
        [InlineData("ShipFrame01")]
        [InlineData("Island")]
        public void A_non_tree_asset_has_no_species_rather_than_defaulting_to_birch(string? asset)
        {
            // A silent birch default is how a newly placed species would quietly pay
            // out the wrong wood forever - and how a nugget would be planted as a
            // tree. Both are refusals, not defaults.
            Assert.Null(TreeSpecies.WoodFor(asset));
            Assert.False(TreeSpecies.IsTree(asset));
        }

        [Fact]
        public void Every_tree_prefab_reports_as_a_tree()
        {
            foreach (string prefab in TreeSpecies.Prefabs)
            {
                Assert.True(TreeSpecies.IsTree(prefab));
            }
        }

        [Fact]
        public void The_palms_are_the_biggest_group_and_birch_the_smallest()
        {
            // A shape check on the decode: 19 palms, 3 birches, matching the parse.
            int palms = 0;
            int birches = 0;
            foreach (string prefab in TreeSpecies.Prefabs)
            {
                if (TreeSpecies.WoodFor(prefab) == "palm") palms++;
                if (TreeSpecies.WoodFor(prefab) == "birch") birches++;
            }

            Assert.Equal(19, palms);
            Assert.Equal(3, birches);
        }
    }
}
