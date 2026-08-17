using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// Mass is the thing that makes materials AUTHORITATIVE rather than decorative:
    /// it drives the client's own overloaded-core rule and this server's flight.
    /// These pin the calibration against the two fixed points we did not choose -
    /// the 1000 kg sky core (RECOVERED) and the flat 800 kg this server has always
    /// published - and the invariants that keep the live world flying.
    /// </summary>
    public class HullMassCalculatorTests
    {
        private static HullMaterials Of(string? wood, string? metal) =>
            new HullMaterials(wood, 5, metal, 5);

        [Fact]
        public void A_stock_iron_hull_lands_where_the_old_flat_mass_was()
        {
            // The calibration point. This server published a flat 800 kg for every
            // ship; a one-cell all-iron frame must land near it so replacing the
            // constant with a real calculation is not a silent rebalance.
            double iron = HullMassCalculator.HullMassKg(Of(null, "iron"), cellCount: 1, deckCount: 0);
            Assert.InRange(iron, 700.0, 850.0);
        }

        [Fact]
        public void A_stock_wooden_hull_is_comfortably_inside_the_recovered_core_budget()
        {
            // A bare sky core lifts 1000 kg (RECOVERED). The starter ship is wooden
            // and MUST fly, so a legacy birch/iron hull has to clear that with room
            // for a helm, a deck and a sail on top.
            double starter = HullMassCalculator.HullMassKg(HullMaterials.Legacy, cellCount: 1, deckCount: 1);
            Assert.True(starter < MaterialCatalog.BaseSkyCoreLiftKg,
                "the starter hull at " + starter + " kg must fit under the 1000 kg core");
        }

        [Fact]
        public void A_solid_gold_hull_does_not_fit_on_a_stock_core()
        {
            // The consequence that makes material choice matter: the heaviest
            // materials are not viable for a whole hull, exactly as retail's
            // mass-versus-lift budget intended.
            double gold = HullMassCalculator.HullMassKg(Of(null, "gold"), cellCount: 1, deckCount: 0);
            Assert.True(gold > MaterialCatalog.BaseSkyCoreLiftKg,
                "a gold hull at " + gold + " kg should overload a bare 1000 kg core");
        }

        [Fact]
        public void Mass_follows_the_recovered_material_order()
        {
            // Same hull, different substance. The ordering must be retail's.
            double cedar = HullMassCalculator.HullMassKg(Of("cedar", null), 2, 1);
            double birch = HullMassCalculator.HullMassKg(Of("birch", null), 2, 1);
            double palm = HullMassCalculator.HullMassKg(Of("palm", null), 2, 1);
            double aluminium = HullMassCalculator.HullMassKg(Of(null, "aluminium"), 2, 1);
            double iron = HullMassCalculator.HullMassKg(Of(null, "iron"), 2, 1);
            double gold = HullMassCalculator.HullMassKg(Of(null, "gold"), 2, 1);

            Assert.True(cedar < birch);
            Assert.True(birch < palm);
            Assert.True(palm < aluminium);   // every wood is lighter than every metal
            Assert.True(aluminium < iron);
            Assert.True(iron < gold);
        }

        [Fact]
        public void A_bigger_ship_in_the_same_wood_is_heavier()
        {
            double small = HullMassCalculator.HullMassKg(Of("oak", null), cellCount: 1, deckCount: 1);
            double big = HullMassCalculator.HullMassKg(Of("oak", null), cellCount: 6, deckCount: 3);
            Assert.True(big > small);
            // And strictly monotonic in each dimension on its own.
            Assert.True(HullMassCalculator.HullMassKg(Of("oak", null), 3, 1)
                > HullMassCalculator.HullMassKg(Of("oak", null), 2, 1));
            Assert.True(HullMassCalculator.HullMassKg(Of("oak", null), 2, 2)
                > HullMassCalculator.HullMassKg(Of("oak", null), 2, 1));
        }

        [Fact]
        public void Quality_costs_nothing_because_retail_says_so_in_as_many_words()
        {
            // "Using higher quality materials will give a higher statistic boost ...
            // WITHOUT ANY ADDITIONAL COST OF WEIGHT." Q10 aluminium being free is the
            // reason it was the most sought-after material in the game; if this ever
            // starts failing, the economy has been inverted.
            double q1 = HullMassCalculator.HullMassKg(new HullMaterials("oak", 1, "steel", 1), 3, 2);
            double q10 = HullMassCalculator.HullMassKg(new HullMaterials("oak", 10, "steel", 10), 3, 2);
            Assert.Equal(q1, q10, 6);
            Assert.False(HullMassCalculator.QualityAffectsMass);
        }

        [Fact]
        public void A_mixed_hull_sits_between_its_two_pure_forms()
        {
            double pureWood = HullMassCalculator.HullMassKg(Of("birch", null), 2, 1);
            double pureMetal = HullMassCalculator.HullMassKg(Of(null, "tungsten"), 2, 1);
            double mixed = HullMassCalculator.HullMassKg(Of("birch", "tungsten"), 2, 1);

            Assert.True(mixed > pureWood);
            Assert.True(mixed < pureMetal);
        }

        [Fact]
        public void A_legacy_hull_with_no_recorded_material_still_gets_a_real_mass()
        {
            // The migration guarantee. Five ships in the live world have no material
            // record; none of them may come back weightless or unflyable.
            double legacy = HullMassCalculator.HullMassKg(new HullMaterials(null, 0, null, 0), 1, 1);
            Assert.True(legacy > 0.0);
            Assert.Equal(
                HullMassCalculator.HullMassKg(HullMaterials.Legacy, 1, 1),
                legacy, 6);
        }

        [Fact]
        public void A_malformed_plan_never_produces_a_weightless_or_negative_ship()
        {
            Assert.True(HullMassCalculator.HullMassKg(HullMaterials.Legacy, 0, 0) > 0.0);
            Assert.True(HullMassCalculator.HullMassKg(HullMaterials.Legacy, -5, -5) > 0.0);
            Assert.True(HullMassCalculator.HullMassKg(null!, 1, 1) > 0.0);
        }

        // ------------------------------------------------------------------
        // The agility multiplier that reaches the flight integrator.
        // ------------------------------------------------------------------

        [Fact]
        public void A_ship_of_the_old_reference_mass_flies_EXACTLY_as_it_does_today()
        {
            // The safety property of the whole change: at the mass this server used
            // to publish for every ship, the multiplier is exactly 1.0, so nothing
            // about the current feel moves.
            Assert.Equal(1.0, HullMassCalculator.AgilityScale(HullMassCalculator.ReferenceHullMassKg), 9);
        }

        [Fact]
        public void A_lighter_ship_is_more_agile_and_a_heavier_one_less()
        {
            Assert.True(HullMassCalculator.AgilityScale(400.0) > 1.0);
            Assert.True(HullMassCalculator.AgilityScale(1600.0) < 1.0);
            // Monotonic decreasing across the whole plausible range.
            double previous = double.MaxValue;
            for (double mass = 200.0; mass <= 4000.0; mass += 100.0)
            {
                double scale = HullMassCalculator.AgilityScale(mass);
                Assert.True(scale <= previous);
                previous = scale;
            }
        }

        [Fact]
        public void The_multiplier_is_clamped_so_no_hull_can_leave_the_flyable_range()
        {
            // A cedar skiff must not become uncontrollably fast, and a gold barge
            // must not become literally immobile.
            Assert.Equal(HullMassCalculator.MaxAgility, HullMassCalculator.AgilityScale(1.0), 9);
            Assert.Equal(HullMassCalculator.MinAgility, HullMassCalculator.AgilityScale(1_000_000.0), 9);

            foreach (ShipMaterial material in MaterialCatalog.Materials)
            {
                var pure = material.IsWood
                    ? new HullMaterials(material.Id, 5, null, 0)
                    : new HullMaterials(null, 0, material.Id, 5);
                double scale = HullMassCalculator.AgilityScale(
                    HullMassCalculator.HullMassKg(pure, cellCount: 4, deckCount: 2));
                Assert.InRange(scale, HullMassCalculator.MinAgility, HullMassCalculator.MaxAgility);
            }
        }

        [Fact]
        public void A_nonsense_mass_leaves_the_ship_flying_normally_rather_than_stopping_it()
        {
            Assert.Equal(1.0, HullMassCalculator.AgilityScale(0.0));
            Assert.Equal(1.0, HullMassCalculator.AgilityScale(-100.0));
            Assert.Equal(1.0, HullMassCalculator.AgilityScale(double.NaN));
            Assert.Equal(1.0, HullMassCalculator.AgilityScale(double.PositiveInfinity));
        }
    }
}
