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

        [Fact]
        public void The_default_set_is_the_four_starters_only()
        {
            List<string> defaults = StarterSchematics.Default(CatalogueKeys).ToList();

            Assert.Equal(
                new[] { "torch", "guitar", "clothMakeshift", "makeshiftStorage" },
                defaults);
        }

        [Fact]
        public void The_default_set_is_far_smaller_than_the_catalogue()
        {
            int defaults = StarterSchematics.Default(CatalogueKeys).Count();
            Assert.True(defaults < CatalogueKeys.Length, "defaults must be a strict subset - the catalogue is gated");
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
