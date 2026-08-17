using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public class ShipInteractionHoldPolicyTests
    {
        [Theory]
        [InlineData(2.0f)]
        [InlineData(10.15f)]
        public void Every_ship_part_hold_is_clamped_even_after_the_retail_penalty(float input)
        {
            Assert.Equal(ShipInteractionHoldPolicy.MaxImmediateHoldSeconds,
                ShipInteractionHoldPolicy.Clamp(true, input));
        }

        [Fact]
        public void Unrelated_interactions_are_untouched()
        {
            Assert.Equal(12f, ShipInteractionHoldPolicy.Clamp(false, 12f));
        }

        [Fact]
        public void Already_fast_interactions_are_not_lengthened()
        {
            Assert.Equal(0.05f, ShipInteractionHoldPolicy.Clamp(true, 0.05f));
        }
    }
}
