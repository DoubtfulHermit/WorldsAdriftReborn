using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Phase 2: mounting a loose crafted part onto a built ship. These pin the PURE
    /// half of the flow - the server-side gate order, the hull-local offset conversion,
    /// and the deck-child hull-key resolution that is the make-or-break for a placeable
    /// surface - so the parts that only fail on a live client (a modified client
    /// sending a bogus PlacePart, a part that never snaps because the deck was not a
    /// Unity child) are asserted here instead of by staring at a running game.
    /// </summary>
    public class PartMountTests
    {
        // ------------------------------------------------------------------
        // The server-side validation gate (mirror of the client's own refusal).
        // ------------------------------------------------------------------

        [Fact]
        public void All_checks_passing_accepts_the_mount()
        {
            Assert.Equal(
                PartMountReject.Accept,
                PartMount.EvaluatePlace(
                    ownsPlayerEntity: true,
                    hasCarriedPart: true,
                    partIsMountable: true,
                    shipIsBuilt: true,
                    targetIsChildOfShip: true));
        }

        [Fact]
        public void A_place_for_someone_elses_entity_is_rejected_first()
        {
            // Rule 6: ownership is checked before anything reads the event. Even with
            // every other fact wrong, NotOwner is the reason reported.
            Assert.Equal(
                PartMountReject.NotOwner,
                PartMount.EvaluatePlace(false, false, false, false, false));
        }

        [Fact]
        public void No_carried_part_is_rejected_before_the_ship_checks()
        {
            // PlacePart carries no part id; without a tracked pickup there is nothing to
            // mount, and that is caught before the (valid) ship/target are inspected.
            Assert.Equal(
                PartMountReject.NoCarriedPart,
                PartMount.EvaluatePlace(
                    ownsPlayerEntity: true,
                    hasCarriedPart: false,
                    partIsMountable: false,
                    shipIsBuilt: true,
                    targetIsChildOfShip: true));
        }

        [Fact]
        public void A_carried_but_unmountable_part_is_rejected()
        {
            // Carrying something that is not a loose crafted part (or is already mounted)
            // must not attach - a replayed PlacePart cannot bolt an arbitrary entity on.
            Assert.Equal(
                PartMountReject.PartNotMountable,
                PartMount.EvaluatePlace(true, true, partIsMountable: false, shipIsBuilt: true, targetIsChildOfShip: true));
        }

        [Fact]
        public void A_ship_the_server_never_built_is_rejected()
        {
            Assert.Equal(
                PartMountReject.ShipNotBuilt,
                PartMount.EvaluatePlace(true, true, true, shipIsBuilt: false, targetIsChildOfShip: true));
        }

        [Fact]
        public void A_target_that_is_not_a_child_of_the_ship_is_rejected_last()
        {
            // The client's HasParentEntity gate: the surface must be a Unity child of the
            // ship. This is the deck-child make-or-break, re-checked server-side.
            Assert.Equal(
                PartMountReject.TargetNotChildOfShip,
                PartMount.EvaluatePlace(true, true, true, true, targetIsChildOfShip: false));
        }

        [Fact]
        public void The_gate_order_matches_the_client_first_wrong_thing_first()
        {
            // Two things wrong at once: the EARLIER check wins, so the reason is stable
            // and matches what the client itself would have refused on first.
            Assert.Equal(
                PartMountReject.NoCarriedPart,
                PartMount.EvaluatePlace(true, hasCarriedPart: false, partIsMountable: true, shipIsBuilt: false, targetIsChildOfShip: false));
        }

        // ------------------------------------------------------------------
        // ship-local metres -> fixed-point offset (the value we mount with).
        // ------------------------------------------------------------------

        [Fact]
        public void Ship_local_offset_converts_metres_to_fixed_point()
        {
            // The client's shipLocalPosition is already hull-relative, in Unity metres;
            // it drops straight into the 190602 Parent(hull,"~") transform as fixed point.
            FixedPointPosition offset = PartMount.ShipLocalOffset(1.0f, 2.5f, -3.0f);

            Assert.Equal((long)(1.0 * FixedPointPosition.UnitsPerMetre), offset.X);
            Assert.Equal((long)(2.5 * FixedPointPosition.UnitsPerMetre), offset.Y);
            Assert.Equal((long)(-3.0 * FixedPointPosition.UnitsPerMetre), offset.Z);
        }

        [Fact]
        public void A_centred_placement_is_a_zero_offset()
        {
            // A part placed at the hull centre rides at Parent(hull,"~") + (0,0,0).
            FixedPointPosition offset = PartMount.ShipLocalOffset(0f, 0f, 0f);
            Assert.Equal(new FixedPointPosition(0, 0, 0), offset);
        }

        // ------------------------------------------------------------------
        // The deck-child fix: a built deck's hull is its sibling by sequence.
        // ------------------------------------------------------------------

        [Fact]
        public void A_built_deck_key_resolves_to_its_sibling_hull_key()
        {
            // built-ship:N:deck -> built-ship:N:hull, the hull the deck must become a
            // Unity child of so the client accepts it as a placement surface.
            Assert.Equal("built-ship:0:hull", BuiltShipPlacement.HullKeyForDeckKey("built-ship:0:deck"));
            Assert.Equal("built-ship:7:hull", BuiltShipPlacement.HullKeyForDeckKey("built-ship:7:deck"));
        }

        [Fact]
        public void The_resolved_hull_key_matches_the_spawn_plans_own_hull_key()
        {
            // The resolution must agree with how the spawner names the hull, or the deck
            // would parent to an id that does not exist.
            for (int sequence = 0; sequence < 4; sequence++)
            {
                Assert.Equal(
                    BuiltShipPlacement.HullKey(sequence),
                    BuiltShipPlacement.HullKeyForDeckKey(BuiltShipPlacement.DeckKey(sequence)));
            }
        }

        [Fact]
        public void A_non_deck_key_resolves_to_no_hull()
        {
            // Only a built-ship deck key resolves; anything else (a hull, a loose part,
            // a tree, null) yields null so the branch does not mis-parent it.
            Assert.Null(BuiltShipPlacement.HullKeyForDeckKey(BuiltShipPlacement.HullKey(0)));
            Assert.Null(BuiltShipPlacement.HullKeyForDeckKey("loose-part:3"));
            Assert.Null(BuiltShipPlacement.HullKeyForDeckKey("tree-haven"));
            Assert.Null(BuiltShipPlacement.HullKeyForDeckKey(null));
        }

        [Fact]
        public void The_mounted_part_rides_via_the_relative_slot_not_a_unity_child()
        {
            // A mounted part is a "~" position-follower (keeps its own rigidbody), unlike
            // the DECK which is a genuine Unity child (a real hierarchy key). The two keys
            // must stay distinct, or the wake heartbeat would churn an unparent/reparent.
            // The commit seeds the part with the RelativeSlotKey, exactly the class a
            // helm/engine is in - never the deck's real key.
            Assert.NotEqual(Deck.HierarchyKey, BoltedPartTransform.RelativeSlotKey);
            Assert.Equal("~", BoltedPartTransform.RelativeSlotKey);
            // IsUnityChild is keyed by the part's registration key: the walkable DECK is a
            // Unity child, a "~"-follower part (the helm) is not.
            Assert.True(BoltedPartTransform.IsUnityChild(WorldEntities.DeckKey));
            Assert.False(BoltedPartTransform.IsUnityChild(WorldEntities.HelmKey));
        }
    }
}
