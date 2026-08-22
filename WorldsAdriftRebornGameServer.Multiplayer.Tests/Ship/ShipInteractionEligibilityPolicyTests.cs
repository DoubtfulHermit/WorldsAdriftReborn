using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class ShipInteractionEligibilityPolicyTests
    {
        private static readonly FixedPointPosition Origin =
            FixedPointPosition.FromMetres(0, 100, 0);

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void Ownership_and_checkout_are_required(
            bool ownsPlayer, bool targetCheckedOut)
        {
            Assert.False(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer, targetCheckedOut, playerPositionKnown: true,
                Origin, Origin, 5.0));
        }

        [Fact]
        public void Interaction_inside_recovered_client_completion_envelope_is_allowed()
        {
            Assert.True(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer: true, targetCheckedOut: true, playerPositionKnown: true,
                playerPosition: FixedPointPosition.FromMetres(0, 100, 6.499),
                targetPosition: Origin, radiusMetres: 5.0));
        }

        [Fact]
        public void Completion_envelope_uses_retail_strict_inequality()
        {
            Assert.False(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer: true, targetCheckedOut: true,
                playerPositionKnown: true,
                playerPosition: FixedPointPosition.FromMetres(0, 100, 6.5),
                targetPosition: Origin, radiusMetres: 5.0));
        }

        [Fact]
        public void Forged_remote_interaction_is_rejected_even_when_target_is_checked_out()
        {
            Assert.False(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer: true, targetCheckedOut: true,
                playerPositionKnown: true,
                playerPosition: FixedPointPosition.FromMetres(0, 100, 40),
                targetPosition: Origin, radiusMetres: 5.0));
        }

        [Fact]
        public void Interaction_requires_position_evidence()
        {
            Assert.False(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer: true, targetCheckedOut: true,
                playerPositionKnown: false, playerPosition: default,
                targetPosition: Origin, radiusMetres: 5.0));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Invalid_advertised_radius_is_rejected(double radius)
        {
            Assert.False(ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer: true, targetCheckedOut: true,
                playerPositionKnown: true, playerPosition: Origin,
                targetPosition: Origin, radiusMetres: radius));
        }
    }
}
