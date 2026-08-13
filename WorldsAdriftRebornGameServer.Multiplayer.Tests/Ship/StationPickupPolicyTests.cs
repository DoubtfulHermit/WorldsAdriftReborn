using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The pure gate on the non-retail 1211 station PickUp (packing a placed
    /// shipyard / Assembly Station back into inventory): ownership of the sending
    /// peer, verb, the placed-station kind, OWNER identity (the character uid the
    /// placement stamped vs the requester's, resolved by the same mechanism), the
    /// busy states read off the dock/build/design/craft ledgers, the "first pickup
    /// event wins" reservation, and the optional authoritative range check with
    /// retail's 2 m completion leeway. Every accept and every typed rejection is
    /// pinned here so the transaction glue can stay thin.
    /// </summary>
    public class StationPickupPolicyTests
    {
        /// <summary>The 1210 radius the serve branch advertises for placed stations.</summary>
        private const double Radius = 3.0;

        private const string Owner = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        private const string SomeoneElse = "11111111-2222-3333-4444-555555555555";

        /// <summary>A request that passes every check - the baseline the tests perturb.</summary>
        private static StationPickupDecision Valid(
            bool peerOwnsPlayer = true,
            bool verbIsPickUp = true,
            bool alreadyPickedUp = false,
            PickupStationKind kind = PickupStationKind.Shipyard,
            string? ownerUid = Owner,
            string? requesterUid = Owner,
            bool shipDocked = false,
            bool buildInProgress = false,
            bool craftInProgress = false,
            bool materialsLoaded = false,
            bool reservedByOther = false,
            double? distance = null)
        {
            return StationPickupPolicy.Evaluate(
                peerOwnsPlayer, verbIsPickUp, alreadyPickedUp, kind,
                ownerUid, requesterUid,
                shipDocked, buildInProgress, craftInProgress, materialsLoaded,
                reservedByOther, distance, Radius);
        }

        // ------------------------------------------------------------------
        // ACCEPTS
        // ------------------------------------------------------------------

        [Fact]
        public void A_fully_valid_shipyard_pickup_grants()
        {
            StationPickupDecision d = Valid();
            Assert.True(d.ShouldGrant);
            Assert.Equal(StationPickupOutcome.Grant, d.Outcome);
        }

        [Fact]
        public void A_fully_valid_assembly_station_pickup_grants()
        {
            Assert.Equal(StationPickupOutcome.Grant,
                Valid(kind: PickupStationKind.AssemblyStation).Outcome);
        }

        [Fact]
        public void An_unowned_station_is_pickable_by_anyone()
        {
            // The "" owner is the pre-identity convention everywhere else in this
            // assembly (OwnershipRegistrationPolicy: empty owner = nobody owns it),
            // so an unowned station must not be permanently stranded.
            Assert.Equal(StationPickupOutcome.Grant,
                Valid(ownerUid: "", requesterUid: SomeoneElse).Outcome);
            Assert.Equal(StationPickupOutcome.Grant,
                Valid(ownerUid: null, requesterUid: "").Outcome);
        }

        [Fact]
        public void A_missing_distance_skips_the_range_check()
        {
            // No trustworthy world-space position (relay v2 off, or aboard a ship):
            // trust the client's own two-stage range check, like the atlas pickup.
            Assert.Equal(StationPickupOutcome.Grant, Valid(distance: null).Outcome);
        }

        [Fact]
        public void A_distance_within_radius_plus_leeway_grants()
        {
            // Retail's completion check allows 2 m of drift beyond the prompt
            // radius, so 3 + 2 = 5 m must still pass.
            Assert.Equal(StationPickupOutcome.Grant, Valid(distance: Radius).Outcome);
            Assert.Equal(StationPickupOutcome.Grant,
                Valid(distance: Radius + StationPickupPolicy.CompletionLeewayMetres).Outcome);
        }

        // ------------------------------------------------------------------
        // REJECTIONS, one per gate, in gate order
        // ------------------------------------------------------------------

        [Fact]
        public void A_peer_that_does_not_own_the_player_is_rejected_first()
        {
            // Ownership is the most fundamental check - even a wrong-verb request
            // on a non-station from a non-owner reports NotOwner.
            StationPickupDecision d = Valid(
                peerOwnsPlayer: false, verbIsPickUp: false, kind: PickupStationKind.None);
            Assert.Equal(StationPickupOutcome.NotOwner, d.Outcome);
            Assert.False(d.ShouldGrant);
        }

        [Fact]
        public void A_non_pickup_verb_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.WrongVerb, Valid(verbIsPickUp: false).Outcome);
        }

        [Fact]
        public void An_already_picked_up_station_is_rejected_before_the_kind_check()
        {
            // After a pickup the membership ledgers are gone, so the kind resolves
            // to None - the tombstone must still name the real reason.
            Assert.Equal(StationPickupOutcome.AlreadyPickedUp,
                Valid(alreadyPickedUp: true, kind: PickupStationKind.None).Outcome);
        }

        [Fact]
        public void A_target_that_is_not_a_placed_station_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.NotAStation,
                Valid(kind: PickupStationKind.None).Outcome);
        }

        [Fact]
        public void Someone_elses_station_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.NotYourStation,
                Valid(requesterUid: SomeoneElse).Outcome);
        }

        [Fact]
        public void An_owned_station_rejects_a_requester_with_no_identity()
        {
            // A volatile-session requester ("" uid) may never take an OWNED
            // station: "" must not read as a wildcard.
            Assert.Equal(StationPickupOutcome.NotYourStation,
                Valid(requesterUid: "").Outcome);
            Assert.Equal(StationPickupOutcome.NotYourStation,
                Valid(requesterUid: null).Outcome);
        }

        [Fact]
        public void A_shipyard_with_a_docked_ship_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.ShipDocked, Valid(shipDocked: true).Outcome);
        }

        [Fact]
        public void A_shipyard_with_a_live_blueprint_build_or_design_edit_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.BuildInProgress, Valid(buildInProgress: true).Outcome);
        }

        [Fact]
        public void A_station_with_a_bound_craft_session_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.CraftInProgress,
                Valid(kind: PickupStationKind.AssemblyStation, craftInProgress: true).Outcome);
        }

        [Fact]
        public void A_station_with_slotted_materials_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.MaterialsLoaded,
                Valid(kind: PickupStationKind.AssemblyStation, materialsLoaded: true).Outcome);
        }

        [Fact]
        public void A_station_reserved_by_another_player_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.ReservedByOther, Valid(reservedByOther: true).Outcome);
        }

        [Fact]
        public void A_player_beyond_radius_plus_leeway_is_rejected()
        {
            Assert.Equal(StationPickupOutcome.TooFar,
                Valid(distance: Radius + StationPickupPolicy.CompletionLeewayMetres + 0.01).Outcome);
        }

        [Fact]
        public void Busy_states_outrank_the_reservation_and_range_checks()
        {
            // A docked yard reads ShipDocked even when it is also reserved and far
            // away - the caller's log should name the most fundamental problem.
            Assert.Equal(StationPickupOutcome.ShipDocked,
                Valid(shipDocked: true, reservedByOther: true, distance: 100.0).Outcome);
        }

        [Fact]
        public void The_decision_equality_follows_the_outcome()
        {
            Assert.Equal(new StationPickupDecision(StationPickupOutcome.Grant), Valid());
            Assert.NotEqual(
                new StationPickupDecision(StationPickupOutcome.TooFar),
                Valid());
        }
    }
}
