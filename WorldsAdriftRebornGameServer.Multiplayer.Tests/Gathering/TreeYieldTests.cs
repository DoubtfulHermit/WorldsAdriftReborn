using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Gathering;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Gathering
{
    /// <summary>
    /// A tree pays three materials off one cut, and the failure this guards is the
    /// silent one: two of the three simply not arriving, with the wood still
    /// landing so nothing looks broken.
    /// </summary>
    public class TreeYieldTests
    {
        [Fact]
        public void One_cut_pays_wood_and_fibre_and_berries()
        {
            HarvestYield yields = new();
            TreeYield.RegisterSpecies(yields, "birch");

            IReadOnlyList<YieldGrant> grants = yields.Resolve("birch", units: 1);

            Assert.Equal(3, grants.Count);
            Assert.Equal("birch", grants[0].ItemTypeId);
            Assert.Contains(grants, g => g.ItemTypeId == TreeYield.PlantFiberItemTypeId);
            Assert.Contains(grants, g => g.ItemTypeId == TreeYield.DaccatBerriesItemTypeId);
        }

        [Fact]
        public void The_wood_the_tree_is_named_for_comes_first()
        {
            // Order is the order the player sees the toasts in. The wood is what
            // they aimed at; fibre and berries are the bonus.
            HarvestYield yields = new();
            TreeYield.RegisterSpecies(yields, "oak");

            Assert.Equal("oak", yields.Resolve("oak", 1)[0].ItemTypeId);
            Assert.Equal("oak", yields.RuleFor("oak")!.ItemTypeId);
        }

        [Fact]
        public void Every_yield_scales_with_the_sections_felled()
        {
            HarvestYield yields = new();
            TreeYield.RegisterSpecies(yields, "elm");

            IReadOnlyList<YieldGrant> grants = yields.Resolve("elm", units: 5);

            Assert.Equal(5, grants.Single(g => g.ItemTypeId == "elm").Amount);
            Assert.Equal(5 * TreeYield.PlantFiberPerSection,
                grants.Single(g => g.ItemTypeId == TreeYield.PlantFiberItemTypeId).Amount);
            Assert.Equal(5 * TreeYield.BerriesPerSection,
                grants.Single(g => g.ItemTypeId == TreeYield.DaccatBerriesItemTypeId).Amount);
        }

        [Fact]
        public void Fibre_and_berries_are_quality_exempt()
        {
            // Nothing in the shipped build gives a plant material a quality, and an
            // invented one would be a FLOOR that no recipe was written against.
            HarvestYield yields = new();
            TreeYield.RegisterSpecies(yields, "ash");

            foreach (YieldGrant grant in yields.Resolve("ash", 1)
                .Where(g => g.ItemTypeId != "ash"))
            {
                Assert.Equal(YieldRule.QualityExempt, grant.Quality);
            }
        }

        [Fact]
        public void The_fibre_rate_stays_within_reach_of_the_ratio_retail_shipped()
        {
            // Bossa's tutorial asks for 15 Plant Fibers alongside 20 Wood in one
            // step - 0.75 fibre per wood. We pay 1 per section against 1 wood per
            // section, i.e. rounded up. This pins the ROUNDING: a rate that drifts
            // to 3 or 5 per section is no longer a rounding of retail's ask, it is
            // a new economy, and it should have to change this test to happen.
            Assert.InRange(TreeYield.PlantFiberPerSection, 1, 2);
            Assert.InRange(TreeYield.BerriesPerSection, 1, 2);
        }

        [Fact]
        public void Every_wood_species_pays_fibre_and_berries_not_just_the_one_Haven_plants()
        {
            // The silent failure this exists for: Haven plants birch only, so a
            // birch-only registration would test green and then pay nothing the
            // first time a player reached an island with oak on it.
            HarvestYield yields = new();

            foreach (string wood in TreeSpecies.Woods)
            {
                TreeYield.RegisterSpecies(yields, wood);
            }

            Assert.NotEmpty(TreeSpecies.Woods);

            foreach (string wood in TreeSpecies.Woods)
            {
                IReadOnlyList<YieldGrant> grants = yields.Resolve(wood, 1);
                Assert.Equal(3, grants.Count);
                Assert.Contains(grants, g => g.ItemTypeId == TreeYield.PlantFiberItemTypeId);
                Assert.Contains(grants, g => g.ItemTypeId == TreeYield.DaccatBerriesItemTypeId);
            }
        }

        [Fact]
        public void Registering_a_species_twice_does_not_double_its_yields()
        {
            // Activation is idempotent by design - a species can be registered again
            // when another tree of it is planted - and a second pass must not turn
            // one tree into two trees' worth of fibre.
            HarvestYield yields = new();
            TreeYield.RegisterSpecies(yields, "palm");
            TreeYield.RegisterSpecies(yields, "palm");

            Assert.Equal(3, yields.Resolve("palm", 1).Count);
            Assert.Equal(1, yields.Count);
        }

        [Fact]
        public void One_hit_may_not_grant_the_same_item_twice()
        {
            // Two rules for one item is always a mistake, and it reads as a
            // drop-rate bug rather than as the registration bug it is.
            HarvestYield yields = new();
            yields.Register("birch", new YieldRule("birch", 1));

            Assert.Throws<ArgumentException>(
                () => yields.AddYield("birch", new YieldRule("birch", 1)));
        }

        [Fact]
        public void The_ids_are_the_ones_retail_shipped()
        {
            // PROVED, not chosen. plantFiber is a verbatim itemCategory in shipped
            // quest data; daccatBerries is in the client's own collect-SFX table and
            // in the shipped quest conditions. If either of these strings changes,
            // the item stops being the retail item.
            Assert.Equal("plantFiber", TreeYield.PlantFiberItemTypeId);
            Assert.Equal("daccatBerries", TreeYield.DaccatBerriesItemTypeId);
        }
    }
}
