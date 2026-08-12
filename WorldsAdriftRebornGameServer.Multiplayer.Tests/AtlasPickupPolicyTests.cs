using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The pure gate on a 1211 shard PickUp: ownership, verb, target kind, released
    /// state, the "already taken" and "reserved by someone else" cases, and the
    /// optional range check. Every reason is pinned here so the transaction glue can
    /// stay thin.
    /// </summary>
    public class AtlasPickupPolicyTests
    {
        private const double Radius = 3.0;

        /// <summary>A request that passes every check - the baseline the tests perturb.</summary>
        private static AtlasPickupDecision Valid(
            bool peerOwnsPlayer = true,
            bool verbIsPickUp = true,
            bool targetIsShard = true,
            bool released = true,
            bool collected = false,
            bool reservedByOther = false,
            double? distance = null)
        {
            return AtlasPickupPolicy.Evaluate(
                peerOwnsPlayer, verbIsPickUp, targetIsShard,
                released, collected, reservedByOther, distance, Radius);
        }

        [Fact]
        public void A_fully_valid_released_shard_pickup_grants()
        {
            AtlasPickupDecision d = Valid();
            Assert.True(d.ShouldGrant);
            Assert.Equal(AtlasPickupOutcome.Grant, d.Outcome);
        }

        [Fact]
        public void A_peer_that_does_not_own_the_player_is_rejected_first()
        {
            // Ownership is the most fundamental check - even a lodged non-shard with a
            // wrong verb from a non-owner reports NotOwner.
            AtlasPickupDecision d = Valid(peerOwnsPlayer: false, verbIsPickUp: false, targetIsShard: false, released: false);
            Assert.Equal(AtlasPickupOutcome.NotOwner, d.Outcome);
        }

        [Fact]
        public void A_non_pickup_verb_is_rejected()
        {
            Assert.Equal(AtlasPickupOutcome.WrongVerb, Valid(verbIsPickUp: false).Outcome);
        }

        [Fact]
        public void A_target_that_is_not_a_shard_is_rejected()
        {
            Assert.Equal(AtlasPickupOutcome.NotAShard, Valid(targetIsShard: false).Outcome);
        }

        [Fact]
        public void A_shard_still_lodged_in_its_core_cannot_be_picked_up()
        {
            Assert.Equal(AtlasPickupOutcome.StillLodged, Valid(released: false).Outcome);
        }

        [Fact]
        public void An_already_collected_shard_is_rejected_ahead_of_the_lodged_check()
        {
            // Collected wins over StillLodged: a taken shard is neither lodged nor
            // released, and "already gone" is the more useful reason.
            Assert.Equal(AtlasPickupOutcome.AlreadyCollected, Valid(released: false, collected: true).Outcome);
        }

        [Fact]
        public void A_shard_reserved_by_another_player_is_rejected()
        {
            Assert.Equal(AtlasPickupOutcome.Reserved, Valid(reservedByOther: true).Outcome);
        }

        [Fact]
        public void A_player_outside_the_radius_is_rejected_when_a_position_is_known()
        {
            Assert.Equal(AtlasPickupOutcome.TooFar, Valid(distance: Radius + 0.01).Outcome);
        }

        [Fact]
        public void A_player_inside_the_radius_grants()
        {
            Assert.True(Valid(distance: Radius - 0.01).ShouldGrant);
            // On the boundary is inside (<= radius).
            Assert.True(Valid(distance: Radius).ShouldGrant);
        }

        [Fact]
        public void A_null_distance_skips_the_range_check_and_trusts_the_client()
        {
            // No server-side player position: the client already range-checked before
            // issuing, so a null distance must not reject on range.
            Assert.True(Valid(distance: null).ShouldGrant);
        }

        [Fact]
        public void The_reason_order_puts_ownership_before_verb_before_kind()
        {
            // verb wrong but owner ok -> WrongVerb (not NotAShard).
            Assert.Equal(AtlasPickupOutcome.WrongVerb, Valid(verbIsPickUp: false, targetIsShard: false).Outcome);
            // owner+verb ok, not a shard -> NotAShard (not StillLodged).
            Assert.Equal(AtlasPickupOutcome.NotAShard, Valid(targetIsShard: false, released: false).Outcome);
        }
    }
}
