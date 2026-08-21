using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class PartPickupPolicyTests
    {
        [Fact]
        public void A_known_unmounted_part_is_a_common_world_object()
        {
            Assert.Equal(PartPickupReject.Accept,
                PartPickupPolicy.Evaluate(true, false, false, false));
        }

        [Fact]
        public void A_forged_pickup_cannot_turn_an_arbitrary_entity_into_a_carried_part()
        {
            Assert.Equal(PartPickupReject.UnknownPart,
                PartPickupPolicy.Evaluate(false, false, false, true));
        }

        [Fact]
        public void Two_players_cannot_authoritatively_carry_the_same_part()
        {
            Assert.Equal(PartPickupReject.AlreadyCarried,
                PartPickupPolicy.Evaluate(true, true, false, true));
        }

        [Fact]
        public void Another_owner_cannot_detach_a_mounted_part()
        {
            Assert.Equal(PartPickupReject.MountedShipNotOwned,
                PartPickupPolicy.Evaluate(true, false, true, false));
        }

        [Fact]
        public void The_owner_can_detach_a_mounted_part()
        {
            Assert.Equal(PartPickupReject.Accept,
                PartPickupPolicy.Evaluate(true, false, true, true));
        }

        [Fact]
        public void Unknown_part_wins_before_other_rejections()
        {
            Assert.Equal(PartPickupReject.UnknownPart,
                PartPickupPolicy.Evaluate(false, true, true, false));
        }
    }
}
