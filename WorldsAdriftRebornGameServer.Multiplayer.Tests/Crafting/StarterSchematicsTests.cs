using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The GATE: a fresh player's default recipes are the minimal starter tier, NOT
    /// the whole catalogue. Everything richer is earned through the knowledge tree.
    /// </summary>
    public class StarterSchematicsTests
    {
        // A stand-in for the 53-recipe catalogue: the four starters plus a sample of
        // the gated recipes the knowledge tree unlocks.
        private static readonly string[] CatalogueKeys =
        {
            "torch", "guitar", "clothMakeshift", "makeshiftStorage",
            "shipyard", "glider", "proceduralWingDefault", "headTorch",
            "storageContainer", "skyCoreAtlasEnhancer", "campFire", "stove",
        };

        // The GATE is restored: Default is the faithful gated starter set again (the
        // 2026-08-11 grant-all testing override is reverted), NOT the whole catalogue.
        [Fact]
        public void Default_is_the_faithful_gated_starter_set_not_the_whole_catalogue()
        {
            List<string> defaults = StarterSchematics.Default(CatalogueKeys).ToList();

            // Only the starter Ids that exist in this stand-in catalogue survive
            // (lamp/assemblyStation are Ids but absent from CatalogueKeys, so dropped).
            Assert.Equal(
                new[] { "torch", "guitar", "clothMakeshift", "makeshiftStorage" },
                defaults);
            Assert.True(defaults.Count < CatalogueKeys.Length,
                "the default set must be a strict subset - the catalogue is knowledge-gated");
        }

        // FaithfulDefault is kept as a back-compat alias and must agree with Default.
        [Fact]
        public void FaithfulDefault_matches_Default()
        {
            Assert.Equal(
                StarterSchematics.Default(CatalogueKeys).ToList(),
                StarterSchematics.FaithfulDefault(CatalogueKeys).ToList());
        }

        [Theory]
        // The gated recipes are earned via the tree, so they must NOT be defaults.
        [InlineData("shipyard")]
        [InlineData("glider")]
        [InlineData("proceduralWingDefault")]
        [InlineData("headTorch")]
        [InlineData("storageContainer")]
        [InlineData("skyCoreAtlasEnhancer")]
        [InlineData("campFire")]
        public void A_gated_recipe_is_not_a_default(string recipeId)
        {
            Assert.DoesNotContain(recipeId, StarterSchematics.Default(CatalogueKeys));
        }

        [Fact]
        public void A_starter_absent_from_the_catalogue_is_dropped_not_dangled()
        {
            // A catalogue missing "makeshiftStorage" yields only the three that exist.
            string[] partial = { "torch", "guitar", "clothMakeshift", "shipyard" };
            List<string> defaults = StarterSchematics.Default(partial).ToList();

            Assert.Equal(new[] { "torch", "guitar", "clothMakeshift" }, defaults);
            Assert.DoesNotContain("makeshiftStorage", defaults);
        }
    }
}
