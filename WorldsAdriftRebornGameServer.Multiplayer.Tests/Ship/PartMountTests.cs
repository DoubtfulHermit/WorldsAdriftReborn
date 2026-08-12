using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
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
                    carriedIsLoosePart: true,
                    carriedNotAlreadyMounted: true,
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
                PartMount.EvaluatePlace(false, false, false, false, false, false));
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
                    carriedIsLoosePart: false,
                    carriedNotAlreadyMounted: false,
                    shipIsBuilt: true,
                    targetIsChildOfShip: true));
        }

        [Fact]
        public void Carrying_something_that_is_not_a_loose_part_is_rejected_by_name()
        {
            // Lifting a world prop (or a part the ledger has forgotten after a restart)
            // must not attach - a replayed PlacePart cannot bolt an arbitrary entity on.
            // Named distinctly from PartAlreadyMounted so a live log says which failed.
            Assert.Equal(
                PartMountReject.CarriedNotALoosePart,
                PartMount.EvaluatePlace(true, true, carriedIsLoosePart: false, carriedNotAlreadyMounted: true, shipIsBuilt: true, targetIsChildOfShip: true));
        }

        [Fact]
        public void A_loose_part_that_is_already_mounted_is_rejected_by_name()
        {
            // The part is a known loose part but is already on a ship: only reachable by a
            // replayed/duplicated place (a normal re-lift detaches it first). Distinct
            // reason from CarriedNotALoosePart.
            Assert.Equal(
                PartMountReject.PartAlreadyMounted,
                PartMount.EvaluatePlace(true, true, carriedIsLoosePart: true, carriedNotAlreadyMounted: false, shipIsBuilt: true, targetIsChildOfShip: true));
        }

        [Fact]
        public void A_ship_the_server_never_built_is_rejected()
        {
            Assert.Equal(
                PartMountReject.ShipNotBuilt,
                PartMount.EvaluatePlace(true, true, true, true, shipIsBuilt: false, targetIsChildOfShip: true));
        }

        [Fact]
        public void A_target_that_is_not_a_child_of_the_ship_is_rejected_last()
        {
            // The client's HasParentEntity gate: the surface must be a Unity child of the
            // ship (the hull, its deck, or a part already mounted on it). This is the
            // deck-child make-or-break, re-checked server-side.
            Assert.Equal(
                PartMountReject.TargetNotChildOfShip,
                PartMount.EvaluatePlace(true, true, true, true, true, targetIsChildOfShip: false));
        }

        [Fact]
        public void The_gate_order_matches_the_client_first_wrong_thing_first()
        {
            // Two things wrong at once: the EARLIER check wins, so the reason is stable
            // and matches what the client itself would have refused on first.
            Assert.Equal(
                PartMountReject.NoCarriedPart,
                PartMount.EvaluatePlace(true, hasCarriedPart: false, carriedIsLoosePart: true, carriedNotAlreadyMounted: true, shipIsBuilt: false, targetIsChildOfShip: false));
        }

        // ------------------------------------------------------------------
        // Rotation is HONORED, not identity: the placed hull-relative rotation
        // (PlacePart.shipLocalRotation) is what the mount commit packs into the
        // 190602 localRotation via Quaternion32Packing.Encode. The commit itself
        // is impure (game types), so the assertable slice is that a real placed
        // rotation survives the encode as a NON-identity value, while an identity
        // / degenerate placement collapses to the client's identity sentinel.
        // ------------------------------------------------------------------

        [Fact]
        public void A_placed_yaw_is_carried_into_the_mount_as_a_non_identity_rotation()
        {
            // A 90-degree yaw about +Y (the kind a player gets by pressing rotate/Z during
            // placement) must NOT collapse to identity - that was the "rotation dropped" bug.
            double half = 90.0 * System.Math.PI / 180.0 / 2.0;
            float w = (float)System.Math.Cos(half);
            float y = (float)System.Math.Sin(half);

            uint packed = Quaternion32Packing.Encode(w, 0f, y, 0f);

            Assert.NotEqual(Quaternion32Packing.Identity, packed);
        }

        [Fact]
        public void An_unrotated_placement_still_packs_to_the_identity_sentinel()
        {
            // A part placed with no yaw feeds identity - the same value the old code always
            // wrote, so an unrotated placement is unchanged and never a NaN on the wire.
            Assert.Equal(Quaternion32Packing.Identity, Quaternion32Packing.Encode(1f, 0f, 0f, 0f));
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

        // ------------------------------------------------------------------
        // BUG 1: the mount SURFACE a part's attachmentType selects. A helm must
        // resolve to the DECK surface (the walkable Deck01 collider the built ship
        // presents) so it can be placed across the whole deck - the old best-guess
        // "shipSurfaces" resolved to the Environment layer, which never hits that
        // deck, so the helm only landed on one incidental spot. This is the pure
        // mirror of the client's GetAttachmentType + DeterminePlacementType.
        // ------------------------------------------------------------------

        [Fact]
        public void A_helm_mounts_on_the_ship_deck_surface_not_a_generic_surface()
        {
            // The regression fix: the catalogue helm is authored "deck", which resolves
            // to the ShipDeck surface the client raycasts against the solid deck collider.
            string helmAttachment = LoosePartCatalogue.ForSchematic("helm")!.AttachmentType;

            Assert.Equal("deck", helmAttachment);
            Assert.Equal(PartMountSurface.ShipDeck, PartMountSurfaces.ForAttachmentType(helmAttachment));
            Assert.True(PartMountSurfaces.MountsOnDeckSurface(helmAttachment));
        }

        [Fact]
        public void The_old_shipSurfaces_guess_would_not_reach_the_deck_collider()
        {
            // Documents WHY "helm only mounts in one spot": a "shipSurfaces" part resolves
            // to the ShipSurfaces (Environment-layer, no-tag) raycast, which does NOT hit
            // the ShipAttachmentSolid "ShipDeck" collider our built deck presents.
            Assert.Equal(PartMountSurface.ShipSurfaces, PartMountSurfaces.ForAttachmentType("shipSurfaces"));
            Assert.False(PartMountSurfaces.MountsOnDeckSurface("shipSurfaces"));
        }

        [Theory]
        [InlineData("deck", PartMountSurface.ShipDeck)]
        [InlineData("deckForward", PartMountSurface.ShipDeck)]
        [InlineData("side", PartMountSurface.ShipSide)]
        [InlineData("engine", PartMountSurface.ShipSide)]
        [InlineData("wing", PartMountSurface.ShipSide)]
        [InlineData("deckGrid", PartMountSurface.DeckGrid)]
        [InlineData("shipSurfaces", PartMountSurface.ShipSurfaces)]
        [InlineData("coreModule", PartMountSurface.CoreModule)]
        [InlineData("none", PartMountSurface.None)]
        [InlineData("nonsense", PartMountSurface.None)]
        [InlineData(null, PartMountSurface.None)]
        public void Attachment_type_resolves_to_the_client_surface(string? attachmentType, PartMountSurface expected)
        {
            // Pins the string -> surface map against the decompiled client
            // (BuilderVisualizer.GetAttachmentType + ShipPartPlacement.DeterminePlacementType),
            // so a future catalogue edit that mis-authors a surface fails here.
            Assert.Equal(expected, PartMountSurfaces.ForAttachmentType(attachmentType));
        }

        // ------------------------------------------------------------------
        // BUG 2 coherence: a mounted part's placed rotation survives the pack/unpack
        // the 190602 and the re-checkout 1120 both use, so the served attach state is
        // self-consistent (the 1120 re-seed no longer disagrees with the 190602 beside
        // it). The visible facing is driven by the 190602 "~" localRotation; this pins
        // that the SAME rotation the commit packed decodes back to a real (non-identity)
        // quaternion for the 1120 re-seed, never snapping to identity.
        // ------------------------------------------------------------------

        [Fact]
        public void A_mounted_yaw_round_trips_through_the_packed_form_for_the_reseed()
        {
            // 90-degree yaw about +Y, packed exactly as the mount commit packs it, then
            // decoded exactly as the 1120 re-seed now decodes it: it must come back a
            // non-identity rotation, not collapse to (1,0,0,0).
            double half = 90.0 * System.Math.PI / 180.0 / 2.0;
            float w = (float)System.Math.Cos(half);
            float y = (float)System.Math.Sin(half);

            uint packed = Quaternion32Packing.Encode(w, 0f, y, 0f);
            (float dw, float dx, float dy, float dz) = Quaternion32Packing.Decode(packed);

            // Not identity - the placed facing is preserved for the re-checkout attach.
            Assert.False(System.Math.Abs(dw - 1f) < 1e-4 && System.Math.Abs(dx) < 1e-4
                && System.Math.Abs(dy) < 1e-4 && System.Math.Abs(dz) < 1e-4);
            // And close to the yaw we placed (smallest-three has ~1e-3 precision).
            Assert.True(System.Math.Abs(dw - w) < 0.01, "w=" + dw);
            Assert.True(System.Math.Abs(dy - y) < 0.01, "y=" + dy);
        }

        [Fact]
        public void An_unrotated_mount_reseed_decodes_to_identity()
        {
            // The sentinel an unrotated placement packs to must decode back to identity,
            // so the 1120 re-seed of a north-facing part is byte-identical to the old code.
            (float dw, float dx, float dy, float dz) = Quaternion32Packing.Decode(Quaternion32Packing.Identity);
            Assert.Equal(1f, dw);
            Assert.Equal(0f, dx);
            Assert.Equal(0f, dy);
            Assert.Equal(0f, dz);
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
