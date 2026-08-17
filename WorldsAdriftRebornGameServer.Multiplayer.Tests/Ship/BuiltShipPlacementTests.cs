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
        public void Hull_seed_set_is_the_proven_set_plus_the_placement_prerequisites()
        {
            // Fix 1 (findings-mount-placement.md section 1). The built hull carries the proven
            // static test hull's set PLUS 190601 (TransformHierarchyState -> the hull's
            // TransformParentHierarchyBehaviour, without which the deck's Parent(hull,"deck")
            // never becomes a real Unity child) and 1114 (DockableState -> the hull's
            // DockableVisualizer, without which Shipyard.DockedShip stays null). Both are
            // needed for the deck to be a valid placement surface, and both must ride the
            // hull's OWN all-or-nothing batch so they are present at checkout, not after a
            // later interest request.
            Assert.Equal(
                new uint[] { 190602, 1209, 1099, 1130, 190601, 1114, 8062, 8071, 4349 },
                BuiltShipPlacement.HullSeedComponents.ToArray());

            // The two prerequisites are present...
            Assert.Contains(190601u, BuiltShipPlacement.HullSeedComponents);
            Assert.Contains(1114u, BuiltShipPlacement.HullSeedComponents);
            Assert.Equal(BuiltShipPlacement.PlacementPrerequisiteComponents, new uint[] { 190601, 1114 });

            // ...and they are exactly what the built hull adds OVER the static test hull, which
            // deliberately carries neither (no shipyard docks it, and its 1114 serve is gated
            // on IsBuiltHull, so seeding 1114 there would drop its batch -> invisible ship).
            var staticSet = WorldEntities.HullSeedComponents(recogniseShip: true);
            Assert.DoesNotContain(190601u, staticSet);
            Assert.DoesNotContain(1114u, staticSet);
            Assert.Equal(
                new uint[] { 190601, 1114 },
                BuiltShipPlacement.HullSeedComponents.Except(staticSet).ToArray());

            // 190602 first (the position every other behaviour reads back); recognition last
            // (a recognition serialize failure can never precede the geometry in the batch).
            Assert.Equal(190602u, BuiltShipPlacement.HullSeedComponents.First());
            Assert.Equal(
                new uint[] { 8062, 8071, 4349 },
                BuiltShipPlacement.HullSeedComponents.Skip(BuiltShipPlacement.HullSeedComponents.Count - 3).ToArray());
        }

        [Fact]
        public void Decks_are_never_spawned_before_their_hull()
        {
            // Fix 1: the deck's Parent(hull,"deck") only turns into a Unity child if the hull
            // exists first. The spawner registers the hull then its deck panels, and the
            // connect-time SpawnPlan preserves registration order within the AfterPlayer block,
            // so a joining client always creates the hull before any deck panel it parents to.
            var panels = DeckGenerator.Generate(ShipPlanModel.MakeDefaultStarterHull());
            Assert.NotEmpty(panels);
            BuiltShipSpawnPlan.HullAndDecks plan = BuiltShipSpawnPlan.For(0, Shipyard, panels);

            var registry = new WorldEntityRegistry(new EntityIdAllocator());
            registry.Register(plan.Hull);
            foreach (WorldEntity deck in plan.Decks)
            {
                registry.Register(deck);
            }

            var steps = SpawnPlan.For(registry);
            int hullAt = AddEntityIndex(steps, plan.Hull.Key);
            Assert.True(hullAt >= 0, "hull was never added in the plan");
            foreach (WorldEntity deck in plan.Decks)
            {
                int deckAt = AddEntityIndex(steps, deck.Key);
                Assert.True(deckAt > hullAt,
                    "deck '" + deck.Key + "' was spawned before its hull (deck@" + deckAt + " <= hull@" + hullAt + ")");
            }
        }

        [Fact]
        public void Restored_heading_leaves_deck_seed_as_an_unrotated_parent_local_offset()
        {
            var panel = new DeckPanel(
                new ShipVector3(2f, 0f, 0f),
                new[] { new ShipVector3(0f, 0f, 0f) }, 0, 0);
            BuiltShipSpawnPlan.HullAndDecks plan = BuiltShipSpawnPlan.For(
                7, Shipyard, new[] { panel }, System.Math.PI / 2.0);

            // The hull carries the persisted world yaw. The deck is served as a real
            // Unity child with identity local rotation, so its registration delta must
            // remain the panel's raw hull-local offset. Unity rotates that child once.
            // If this delta were pre-rotated here, parenting would rotate it twice and
            // produce the live "boards shifted away from frame" regression.
            Assert.NotEqual(global::WorldsAdriftRebornGameServer.Multiplayer.Placement.Quaternion32Packing.Identity,
                plan.Hull.PackedRotation);
            Assert.Equal(global::WorldsAdriftRebornGameServer.Multiplayer.Placement.Quaternion32Packing.Identity,
                plan.Decks[0].PackedRotation);
            Assert.InRange(plan.Decks[0].Position.MetresX - Shipyard.MetresX, 1.999, 2.001);
            Assert.InRange(plan.Decks[0].Position.MetresZ - Shipyard.MetresZ, -0.001, 0.001);
        }

        private static int AddEntityIndex(System.Collections.Generic.IReadOnlyList<SpawnPlanStep> steps, string key)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Op == SpawnOp.AddEntity && steps[i].Entity != null && steps[i].Entity!.Key == key)
                {
                    return i;
                }
            }
            return -1;
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
        public void Built_ship_keys_are_not_static_ship_bolted_part_keys()
        {
            // The 190602 IsBoltedPartKey branch is for the STATIC test ship's parts and
            // resolves the single global ShipFrameKey hull; a built hull/deck must NOT
            // match it (they resolve their OWN built hull instead) - the built deck is
            // made a Unity child of its built hull by the dedicated built-deck branch
            // (BuiltShips.IsBuiltDeck), and a mounted part by the mounted-part branch.
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
