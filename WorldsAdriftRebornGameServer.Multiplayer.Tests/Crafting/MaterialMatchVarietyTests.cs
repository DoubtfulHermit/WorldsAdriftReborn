using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The ship-blueprint slot used to demand one exact substance ("you may build
    /// this hull out of birch and nothing else"). Retail's shipyard row read
    /// "Q3+ Metal" and let the player choose which metal
    /// (acs/ShipBlueprintMaterialUI.cs:81-86, VERIFIED). These pin the widening -
    /// and, just as importantly, pin that widening the SUBSTANCE did not widen the
    /// QUALITY STANDARD or open the concrete-ingredient slots.
    /// </summary>
    public class MaterialMatchVarietyTests
    {
        [Fact]
        public void A_Wood_slot_accepts_any_species_the_world_actually_yields()
        {
            var required = new MaterialRequirement("birch", "Wood", quality: 0, amount: 3);

            foreach (string wood in new[] { "birch", "oak", "palm", "cedar", "ash", "elm", "hemlock", "chestnut" })
            {
                Assert.True(MaterialMatch.Matches(required, wood, quality: 1), wood + " is a wood");
            }
        }

        [Fact]
        public void A_Metal_slot_accepts_any_metal_the_world_actually_yields()
        {
            var required = new MaterialRequirement("iron", "Metal", quality: 0, amount: 2);

            foreach (string metal in new[] { "iron", "copper", "aluminium", "titanium", "gold", "tungsten" })
            {
                Assert.True(MaterialMatch.Matches(required, metal, quality: 1), metal + " is a metal");
            }
        }

        [Fact]
        public void A_slot_still_refuses_the_other_family()
        {
            var wood = new MaterialRequirement("birch", "Wood", quality: 0, amount: 1);
            var metal = new MaterialRequirement("iron", "Metal", quality: 0, amount: 1);

            Assert.False(MaterialMatch.Matches(wood, "copper", quality: 5));
            Assert.False(MaterialMatch.Matches(metal, "oak", quality: 5));
            // And things that are not ship materials at all.
            Assert.False(MaterialMatch.Matches(metal, "fuel", quality: 5));
            Assert.False(MaterialMatch.Matches(wood, "atlasShard", quality: 5));
        }

        [Fact]
        public void Widening_the_substance_did_not_lower_the_quality_bar()
        {
            // This is the regression that would matter: "any metal" must not become
            // "any metal at any quality".
            var required = new MaterialRequirement("iron", "Metal", quality: 5, amount: 1);

            Assert.False(MaterialMatch.Matches(required, "copper", quality: 4));
            Assert.True(MaterialMatch.Matches(required, "copper", quality: 5));
            Assert.True(MaterialMatch.Matches(required, "copper", quality: 9));
            // Even the exemplar itself is held to the bar.
            Assert.False(MaterialMatch.Matches(required, "iron", quality: 1));
        }

        [Fact]
        public void A_slot_that_opts_out_stays_exactly_as_strict_as_before()
        {
            // An atlas shard is not "any metal", and a recipe must be able to say so.
            var strict = new MaterialRequirement(
                "atlasShard", "Metal", quality: 0, amount: 1, acceptsAnyInCategory: false);

            Assert.True(MaterialMatch.Matches(strict, "atlasShard", quality: 1));
            Assert.False(MaterialMatch.Matches(strict, "gold", quality: 10));
            Assert.False(MaterialMatch.Matches(strict, "iron", quality: 10));
        }

        [Fact]
        public void The_exemplar_id_is_matched_case_insensitively_as_it_always_was()
        {
            var required = new MaterialRequirement("birch", "Wood", quality: 0, amount: 1);
            Assert.True(MaterialMatch.Matches(required, "BIRCH", quality: 0));
            Assert.True(MaterialMatch.Matches(required, "Oak", quality: 0));
        }

        [Fact]
        public void A_null_requirement_is_refused_rather_than_throwing()
        {
            Assert.False(MaterialMatch.Matches(null!, "iron", quality: 5));
        }
    }
}
