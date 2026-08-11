using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Phase 3: a completed ship-blueprint build spawns a real hull+deck next to the
    /// shipyard. This pins the PURE half - where it lands, what keys it takes, what
    /// component set it seeds, and which hull bytes it serves - so the parts that only
    /// fail on a live client (an invisible ship from a mismatched all-or-nothing batch,
    /// a hull overlapping the console, a bad blob that throws in ShipPlan.Load) are
    /// asserted here instead.
    /// </summary>
    public class BuiltShipPlacementTests
    {
        // A stand-in shipyard position: island-ish, off the origin so a bug that keeps
        // the origin is visible.
        private static readonly FixedPointPosition Shipyard =
            new FixedPointPosition(70502113, -1273730, -4580013);

        [Fact]
        public void Hull_is_centred_horizontally_on_the_shipyard_so_it_docks_above_not_beside()
        {
            FixedPointPosition hull = BuiltShipPlacement.HullNextTo(Shipyard);

            // Same X and Z as the shipyard: the ship hovers directly above the yard,
            // not offset to one side sitting on the ground next to the console.
            Assert.Equal(Shipyard.X, hull.X);
            Assert.Equal(Shipyard.Z, hull.Z);
        }

        [Fact]
        public void Hull_hovers_a_modest_height_above_the_shipyard()
        {
            FixedPointPosition hull = BuiltShipPlacement.HullNextTo(Shipyard);

            // Raised the derived hover height so the whole hull body floats clear of the
            // yard and reads as "docked above".
            Assert.Equal(Shipyard.Y + (long)(BuiltShipPlacement.HoverHeightMetres * FixedPointPosition.UnitsPerMetre), hull.Y);
            Assert.True(hull.Y > Shipyard.Y);

            // Derived from geometry (hull body height + clearance), a few metres - clear
            // of the hull's own 3.4 m body but not way up high.
            Assert.Equal(
                BuiltShipPlacement.HullBodyHeightMetres + BuiltShipPlacement.HoverClearanceMetres,
                BuiltShipPlacement.HoverHeightMetres);
            Assert.True(BuiltShipPlacement.HoverHeightMetres > BuiltShipPlacement.HullBodyHeightMetres);
            Assert.True(BuiltShipPlacement.HoverHeightMetres <= 10.0);
        }

        [Fact]
        public void Deck_is_centred_on_its_hull_via_the_proven_deck_placement()
        {
            FixedPointPosition hull = BuiltShipPlacement.HullNextTo(Shipyard);
            FixedPointPosition deck = BuiltShipPlacement.DeckOn(hull);

            // Reuses Deck.OnHull, so the deck centre coincides with the hull centre in X/Z.
            Assert.Equal(Deck.OnHull(hull), deck);
            Assert.Equal(hull.X, deck.X);
            Assert.Equal(hull.Z, deck.Z);
        }

        [Fact]
        public void Hull_seed_set_is_exactly_the_proven_test_hull_recognition_on()
        {
            // The client's interest batch on a ship hull is ALL-OR-NOTHING: one id off
            // and the whole batch drops -> invisible ship. So the built hull must seed
            // the same ids as the proven static test hull with recognition on, id-for-id.
            Assert.Equal(
                WorldEntities.HullSeedComponents(recogniseShip: true),
                BuiltShipPlacement.HullSeedComponents);

            // Concretely: geometry/placement/motion + the three recognition ids.
            Assert.Equal(
                new uint[] { 190602, 1209, 1099, 1130, 8062, 8071, 4349 },
                BuiltShipPlacement.HullSeedComponents.ToArray());

            // 190602 first: the position every other behaviour reads back.
            Assert.Equal(190602u, BuiltShipPlacement.HullSeedComponents.First());
        }

        [Fact]
        public void Deck_seed_set_is_the_deck_visualizer_readers_plus_its_transform()
        {
            // ShipDeckVisualizer requires 1518 (the polygon) + 1099 (one Wood material);
            // 190602 places it. No 8066 - the deck visualizer does not read it and the
            // static deck needs no ship membership to be a solid floor.
            Assert.Equal(new uint[] { 190602, 1518, 1099 }, BuiltShipPlacement.DeckSeedComponents.ToArray());
            Assert.DoesNotContain(8066u, BuiltShipPlacement.DeckSeedComponents);
        }

        [Fact]
        public void Hull_and_deck_keys_are_distinct_and_carry_the_build_sequence()
        {
            Assert.Equal("built-ship:0:hull", BuiltShipPlacement.HullKey(0));
            Assert.Equal("built-ship:0:deck", BuiltShipPlacement.DeckKey(0));
            Assert.NotEqual(BuiltShipPlacement.HullKey(0), BuiltShipPlacement.DeckKey(0));
            // Two builds never collide on a key -> never share a shared entity id.
            Assert.NotEqual(BuiltShipPlacement.HullKey(0), BuiltShipPlacement.HullKey(1));
        }

        [Fact]
        public void Built_hull_key_is_not_a_bolted_part_key_so_the_deck_seeds_world_absolute()
        {
            // The 190602 branch seeds hull-relative only for IsBoltedPartKey; a built
            // deck's key must NOT match, so its static position is seeded world-absolute
            // (a solid floor for a ship that does not move this phase).
            Assert.False(WorldEntities.IsBoltedPartKey(BuiltShipPlacement.HullKey(0)));
            Assert.False(WorldEntities.IsBoltedPartKey(BuiltShipPlacement.DeckKey(0)));
        }

        [Fact]
        public void A_valid_saved_design_drives_the_hull_shape()
        {
            // A real 39-byte one-cell design round-trips: the built ship serves the
            // player's OWN bytes, not the minimum hull.
            byte[] design = ShipPlanModel.MakeDefaultStarterHull().Encode();

            byte[] resolved = BuiltShipPlacement.ResolveHullBytes(design, out bool usedFallback);

            Assert.False(usedFallback);
            Assert.Equal(design, resolved);
        }

        [Fact]
        public void An_empty_or_corrupt_design_falls_back_to_the_minimum_hull()
        {
            // A build with no saved design (empty bytes) must not throw ShipPlan.Load on
            // the client; it falls back to the known-good minimum hull.
            byte[] fromEmpty = BuiltShipPlacement.ResolveHullBytes(System.Array.Empty<byte>(), out bool emptyFell);
            Assert.True(emptyFell);
            Assert.Equal(ShipHull.MinimumHullDataLength, fromEmpty.Length);

            byte[] fromNull = BuiltShipPlacement.ResolveHullBytes(null, out bool nullFell);
            Assert.True(nullFell);
            Assert.Equal(ShipHull.MinimumHullData(), fromNull);

            // A truncated blob (declares one cell, no body) is corrupt -> fallback.
            byte[] truncated = new byte[] { 1, 0 };
            byte[] fromTrunc = BuiltShipPlacement.ResolveHullBytes(truncated, out bool truncFell);
            Assert.True(truncFell);
            Assert.Equal(ShipHull.MinimumHullDataLength, fromTrunc.Length);
        }

        [Fact]
        public void The_fallback_hull_is_a_fresh_array_each_time()
        {
            // No two ships share a mutable hull buffer (ShipHull's own contract).
            byte[] a = BuiltShipPlacement.ResolveHullBytes(null, out _);
            byte[] b = BuiltShipPlacement.ResolveHullBytes(null, out _);
            Assert.NotSame(a, b);
        }
    }
}
