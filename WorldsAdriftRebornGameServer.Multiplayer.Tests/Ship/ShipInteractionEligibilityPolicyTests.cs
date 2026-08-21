using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class ShipInteractionEligibilityPolicyTests
    {
        private static readonly FixedPointPosition Origin =
            FixedPointPosition.FromMetres(0, 100, 0);

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public void Remote_or_unverified_interaction_is_rejected(
            bool ownsPlayer, bool targetCheckedOut, bool playerPositionKnown)
        {
            Assert.False(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer, targetCheckedOut, playerPositionKnown,
                Origin, Origin, 5.0));
        }

        [Fact]
        public void Checked_out_owned_interaction_inside_advertised_radius_is_allowed()
        {
            Assert.True(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer: true, targetCheckedOut: true, playerPositionKnown: true,
                playerPosition: FixedPointPosition.FromMetres(3, 100, 4),
                targetPosition: Origin, radiusMetres: 5.0));
        }

        [Fact]
        public void Checked_out_target_beyond_advertised_radius_is_rejected()
        {
            Assert.False(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer: true, targetCheckedOut: true, playerPositionKnown: true,
                playerPosition: FixedPointPosition.FromMetres(3.01, 100, 4),
                targetPosition: Origin, radiusMetres: 5.0));
        }
    }
}
