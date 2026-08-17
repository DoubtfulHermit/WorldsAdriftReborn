using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class ShipUpdateVisibilityPolicyTests
    {
        private static readonly FixedPointPosition Hull = FixedPointPosition.FromMetres(100, 20, 100);

        [Fact]
        public void Missing_checkout_never_receives_an_update_even_when_nearby()
        {
            Assert.False(ShipUpdateVisibilityPolicy.ShouldPublish(
                false, false, false, Hull, Hull, 120));
        }

        [Fact]
        public void Nearby_checked_out_peer_receives_updates()
        {
            Assert.True(ShipUpdateVisibilityPolicy.ShouldPublish(
                true, false, false, FixedPointPosition.FromMetres(150, 20, 100), Hull, 120));
        }

        [Fact]
        public void Distant_bystander_does_not_receive_updates()
        {
            Assert.False(ShipUpdateVisibilityPolicy.ShouldPublish(
                true, false, false, FixedPointPosition.FromMetres(1000, 20, 1000), Hull, 120));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Pilot_or_passenger_always_receives_their_ship(bool pilot, bool aboard)
        {
            Assert.True(ShipUpdateVisibilityPolicy.ShouldPublish(
                true, pilot, aboard, FixedPointPosition.FromMetres(1000, 20, 1000), Hull, 120));
        }

        [Fact]
        public void Disabled_interest_is_fail_open_for_checked_out_entities()
        {
            Assert.True(ShipUpdateVisibilityPolicy.ShouldPublish(
                true, false, false, FixedPointPosition.FromMetres(1000, 20, 1000), Hull, 0));
        }
    }
}
