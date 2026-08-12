using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The SHARED pickup gate: the pure rules that decide whether a 1211
    /// InteractWithObject(target, PickUp) may grant a lodgeable pickup. The atlas
    /// shard maps onto this one-to-one; the fuel pod uses it directly.
    /// </summary>
    public class LodgeablePickupPolicyTests
    {
        private const double Radius = 3.0;

        private static LodgeablePickupOutcome Eval(
            bool owns = true, bool pickUp = true, bool isPickup = true,
            bool released = true, bool collected = false, bool reservedByOther = false,
            double? distance = null)
            => LodgeablePickupPolicy.Evaluate(owns, pickUp, isPickup, released, collected,
                reservedByOther, distance, Radius).Outcome;

        [Fact]
        public void A_clean_request_on_a_released_pickup_grants()
            => Assert.Equal(LodgeablePickupOutcome.Grant, Eval());

        [Fact]
        public void A_peer_that_does_not_own_the_player_is_rejected_first()
            => Assert.Equal(LodgeablePickupOutcome.NotOwner, Eval(owns: false, pickUp: false));

        [Fact]
        public void A_non_pickup_verb_is_rejected()
            => Assert.Equal(LodgeablePickupOutcome.WrongVerb, Eval(pickUp: false));

        [Fact]
        public void A_target_that_is_not_a_pickup_is_rejected()
            => Assert.Equal(LodgeablePickupOutcome.NotAPickup, Eval(isPickup: false));

        [Fact]
        public void An_already_collected_pickup_is_rejected()
            => Assert.Equal(LodgeablePickupOutcome.AlreadyCollected, Eval(collected: true));

        [Fact]
        public void A_still_lodged_pickup_is_rejected()
            => Assert.Equal(LodgeablePickupOutcome.StillLodged, Eval(released: false));

        [Fact]
        public void A_pickup_reserved_by_another_player_is_rejected()
            => Assert.Equal(LodgeablePickupOutcome.Reserved, Eval(reservedByOther: true));

        [Fact]
        public void A_player_out_of_range_is_rejected_only_when_a_distance_is_known()
        {
            Assert.Equal(LodgeablePickupOutcome.TooFar, Eval(distance: Radius + 0.1));
            Assert.Equal(LodgeablePickupOutcome.Grant, Eval(distance: Radius));
            // Null distance skips the check (trusts the client raycast).
            Assert.Equal(LodgeablePickupOutcome.Grant, Eval(distance: null));
        }

        [Fact]
        public void Collected_is_checked_before_lodged_so_the_reason_is_the_final_state()
            => Assert.Equal(LodgeablePickupOutcome.AlreadyCollected,
                Eval(released: false, collected: true));
    }
}
