using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// Plugs a hole found by mutation testing on 2026-08-20.
    ///
    /// The 1258 lift seed was a bare literal in ComponentsSerializer, an assembly with
    /// no test project. Dropping it from 1,000,000 kg to 1,000 kg - enough to ground
    /// every ship in the live world, since the legacy 2-cell hull masses 1071 kg -
    /// passed all 5,422 tests without a murmur. These tests are what that edit now
    /// hits.
    ///
    /// Note what is and is not asserted. The seed's exact VALUE is deliberately not
    /// pinned: roadmap F2 may legitimately change it. What is pinned is the SAFETY
    /// PROPERTY - no buildable hull may ever be overloaded - which is the thing that
    /// silently broke.
    /// </summary>
    public class ShipLiftPolicyTests
    {
        [Fact]
        public void No_buildable_hull_can_be_overloaded_by_the_seeded_lift()
        {
            double heaviest = ShipLiftPolicy.PessimisticHullMassKg();

            Assert.False(ShipLiftPolicy.WouldBeOverloaded(heaviest),
                "a " + heaviest.ToString("0") + " kg hull would be overloaded against a seed of "
                + ShipLiftPolicy.SeededTotalLiftKg.ToString("0")
                + " kg, so the client would block vertical input and the ship could not climb");

            // Observed today: 58,400 kg against a 1,000,000 kg seed = 17.1x, on a ship
            // nobody will ever build. Real hulls sit near a thousand times safer.
            Assert.Equal(58400.0, heaviest, 0);
            Assert.Equal(17.1, ShipLiftPolicy.LiftMargin(), 1);

            Assert.True(ShipLiftPolicy.LiftMargin() >= ShipLiftPolicy.RequiredLiftMarginOverHeaviestHull,
                "lift margin is only " + ShipLiftPolicy.LiftMargin().ToString("0.0")
                + "x over the heaviest buildable hull; at least "
                + ShipLiftPolicy.RequiredLiftMarginOverHeaviestHull.ToString("0")
                + "x is required. Making lift realistic is roadmap F2 and is a balance "
                + "decision about live ships - see ShipLiftPolicy remarks.");
        }

        [Fact]
        public void The_legacy_hull_every_live_ship_descends_from_keeps_its_margin()
        {
            // 1071 kg. If this were ever to exceed the seed, every existing ship in the
            // world would be grounded at once, with no migration and no warning.
            double legacy = HullMassCalculator.HullMassKg(HullMaterials.Legacy, cellCount: 2, deckCount: 1);
            Assert.Equal(1071.0, legacy, 1);
            Assert.False(ShipLiftPolicy.WouldBeOverloaded(legacy));
            Assert.True(ShipLiftPolicy.SeededTotalLiftKg / legacy > 900.0);
        }

        [Fact]
        public void The_seed_is_wareborn_tuning_and_is_nowhere_near_retails_recovered_lift()
        {
            // Stated as a test so nobody mistakes the seed for a recovered number. A
            // bare retail sky core lifts 1000 kg; we seed a thousand times that on
            // purpose, so that lift is not the limiting factor while core internals
            // remain unmodelled.
            Assert.Equal(1000.0, MaterialCatalog.BaseSkyCoreLiftKg, 3);
            Assert.True(ShipLiftPolicy.SeededTotalLiftKg > MaterialCatalog.BaseSkyCoreLiftKg * 100.0,
                "the seed is deliberately unrealistic; if it ever approaches the recovered "
                + "1000 kg core, that is roadmap F2 and needs the legacy hull re-checked");
        }

        [Fact]
        public void The_heaviest_material_is_the_one_the_margin_is_computed_against()
        {
            // Guards the pessimism itself: if a heavier material is ever added and this
            // helper stops finding it, the margin above would be computed against the
            // wrong hull and would overstate safety.
            Assert.Equal("gold", ShipLiftPolicy.HeaviestMaterialId());
            Assert.Equal(0.73, MaterialCatalog.Find(ShipLiftPolicy.HeaviestMaterialId())!.MassPerUnitKg, 3);
        }
    }
}
