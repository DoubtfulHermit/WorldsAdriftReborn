using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Placement
{
    public class StationPickupVisibilityPolicyTests
    {
        [Fact]
        public void Successful_authoritative_unavailable_reply_hides_the_pending_station()
        {
            Assert.True(StationPickupVisibilityPolicy.ShouldHide(42, 42, interactionEnabled: false));
        }

        [Theory]
        [InlineData(0, 42, false)]   // no pickup request is pending
        [InlineData(42, 43, false)]  // reply belongs to another station
        [InlineData(42, 42, true)]   // server has not accepted the request
        public void Request_or_identity_or_server_acceptance_must_not_be_skipped(
            long pending, long observed, bool interactionEnabled)
        {
            Assert.False(StationPickupVisibilityPolicy.ShouldHide(
                pending, observed, interactionEnabled));
        }
    }
}
